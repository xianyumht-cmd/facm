"""Relaunch LoL through WeGame and interrogate the LCU the moment it is up.

Runs at 200 ms cadence so it can catch the client before ACE tears it down:
  1. click WeGame 启动
  2. wait for LeagueClientUx.exe
  3. read its command line (--app-port / --remoting-auth-token) via CIM
  4. probe LCU REST (self-signed TLS, basic riot:<token>)
  5. read every LeagueClientUxRender.exe command line, look for a DevTools port
  6. if a devtools port exists, fetch /json/list
  7. dump lockfile state + listening ports, copy fresh logs
"""
import base64
import ctypes
import ctypes.wintypes as wt
import json
import os
import shutil
import ssl
import subprocess
import sys
import time
import urllib.request
from datetime import datetime

user32 = ctypes.WinDLL('user32', use_last_error=True)
k32 = ctypes.WinDLL('kernel32', use_last_error=True)
user32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
user32.GetClassNameW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
user32.GetWindowTextW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]

ROOT = r'D:\WeGameApps\英雄联盟'
LC_DIR = os.path.join(ROOT, 'LeagueClient')
OUT = r'D:\project2\ggman\captures'
BUTTON_REL = (1375, 852)

TH32CS_SNAPPROCESS = 0x00000002


class PROCESSENTRY32(ctypes.Structure):
    _fields_ = [('dwSize', wt.DWORD), ('cntUsage', wt.DWORD), ('th32ProcessID', wt.DWORD),
                ('th32DefaultHeapID', ctypes.POINTER(ctypes.c_ulong)), ('th32ModuleID', wt.DWORD),
                ('cntThreads', wt.DWORD), ('th32ParentProcessID', wt.DWORD), ('pcPriClassBase', ctypes.c_long),
                ('dwFlags', wt.DWORD), ('szExeFile', ctypes.c_char * 260)]


def snapshot():
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snap == -1:
        return {}
    e = PROCESSENTRY32()
    e.dwSize = ctypes.sizeof(PROCESSENTRY32)
    out = {}
    ok = k32.Process32First(snap, ctypes.byref(e))
    while ok:
        out[e.th32ProcessID] = e.szExeFile.decode('mbcs', 'replace')
        ok = k32.Process32Next(snap, ctypes.byref(e))
    k32.CloseHandle(snap)
    return out


def ps(cmd, timeout=25):
    try:
        r = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', cmd],
                           capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout)
        return r.stdout
    except Exception as exc:
        return f'PS-ERROR {exc}'


def find_wegame_window():
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        if not user32.IsWindowVisible(h):
            return True
        cls = ctypes.create_unicode_buffer(256)
        ttl = ctypes.create_unicode_buffer(512)
        user32.GetClassNameW(h, cls, 256)
        user32.GetWindowTextW(h, ttl, 512)
        if ttl.value.strip() == 'WeGame' or (cls.value == 'CefTopWindow' and 'WeGame' in ttl.value):
            r = wt.RECT()
            user32.GetWindowRect(h, ctypes.byref(r))
            if r.right - r.left > 800:
                found.append((h, r, cls.value, ttl.value))
        return True

    user32.EnumWindows(cb, 0)
    return max(found, key=lambda x: (x[1].right - x[1].left) * (x[1].bottom - x[1].top)) if found else None


def click_launch():
    win = find_wegame_window()
    if not win:
        print('WeGame window not found')
        return False
    h, r, cls, ttl = win
    print(f'WeGame hwnd={h} cls={cls} title={ttl!r} rect=({r.left},{r.top},{r.right},{r.bottom})')
    user32.ShowWindow(h, 9)
    user32.SetForegroundWindow(h)
    time.sleep(0.7)
    x, y = r.left + BUTTON_REL[0], r.top + BUTTON_REL[1]
    print(f'click 启动 at ({x},{y})')
    user32.SetCursorPos(x, y)
    time.sleep(0.2)
    user32.mouse_event(0x0002, 0, 0, 0, 0)
    time.sleep(0.1)
    user32.mouse_event(0x0004, 0, 0, 0, 0)
    return True


CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE


def lcu_get(port, token, path):
    url = f'https://127.0.0.1:{port}{path}'
    req = urllib.request.Request(url)
    req.add_header('Authorization', 'Basic ' + base64.b64encode(f'riot:{token}'.encode()).decode())
    with urllib.request.urlopen(req, timeout=4, context=CTX) as resp:
        return resp.status, resp.read()[:400]


def plain_get(url, timeout=3):
    with urllib.request.urlopen(url, timeout=timeout) as resp:
        return resp.status, resp.read()[:800]


def cim_dump(tag):
    out = ps("Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'LeagueClient|RiotClient|ACE-' } "
             "| Select-Object ProcessId,ParentProcessId,Name,CommandLine | ConvertTo-Json -Depth 3")
    path = os.path.join(OUT, f'{tag}-cmdlines.json')
    open(path, 'w', encoding='utf-8').write(out)
    try:
        data = json.loads(out) if out.strip().startswith(('[', '{')) else []
    except Exception:
        data = []
    if isinstance(data, dict):
        data = [data]
    return data


def main():
    os.makedirs(OUT, exist_ok=True)
    tag = datetime.now().strftime('%Y%m%d-%H%M%S')
    if '--no-click' not in sys.argv and not click_launch():
        return 1

    log = []
    started = time.time()
    ux_seen = None
    renderers = {}
    result = {'tag': tag, 'lockfile': '', 'lcu': None, 'devtools': None, 'timeline': []}
    probed = False

    while time.time() - started < 70:
        snap = snapshot()
        league = {p: n for p, n in snap.items() if n.lower().startswith(('leagueclient', 'riotclient', 'ace-'))}
        t = round(time.time() - started, 2)
        if league:
            log.append(f'{t:6.2f} ' + ','.join(sorted(set(league.values()))))
        lock = ''
        try:
            lock = open(os.path.join(LC_DIR, 'lockfile'), 'rb').read().decode('utf-8', 'replace').strip()
        except Exception:
            pass
        if lock and not result['lockfile']:
            result['lockfile'] = lock
            log.append(f'{t:6.2f} LOCKFILE {lock}')

        ux = [p for p, n in league.items() if n.lower() in ('leagueclientux','leagueclientux.exe')]
        rnd = [p for p, n in league.items() if n.lower() in ('leagueclientuxrender','leagueclientuxrender.exe')]
        if ux and not ux_seen:
            ux_seen = (t, ux[0])
            log.append(f'{t:6.2f} UX pid={ux[0]}; dumping command lines')
        if rnd:
            for p in rnd:
                renderers.setdefault(p, t)

        if ux_seen and not probed and time.time() - started > ux_seen[0] + 1.5:
            probed = True
            procs = cim_dump(tag)
            result['cmdlines'] = procs
            ux_entry = next((x for x in procs if (x.get('Name') or '').lower() == 'leagueclientux.exe'), None)
            rnd_entries = [x for x in procs if (x.get('Name') or '').lower() == 'leagueclientuxrender.exe']
            result['renderer_cmdlines'] = [x.get('CommandLine') for x in rnd_entries]
            log.append(f'{t:6.2f} renderers={len(rnd_entries)}')
            if ux_entry:
                cl = ux_entry.get('CommandLine') or ''
                port = token = None
                for part in cl.split():
                    if part.startswith('--app-port='):
                        port = part.split('=', 1)[1].strip('"')
                    if part.startswith('--remoting-auth-token='):
                        token = part.split('=', 1)[1].strip('"')
                result['ux_port'], result['ux_token_len'] = port, (len(token) if token else 0)
                log.append(f'{t:6.2f} ux port={port} token_len={len(token) if token else 0}')
                if port and token:
                    for path in ('/lol-summoner/v1/current-summoner', '/lol-gameflow/v1/gameflow-phase', '/riotclient/region-locale'):
                        try:
                            st, body = lcu_get(port, token, path)
                            log.append(f'{t:6.2f} REST {path} -> {st} {body[:160]!r}')
                            result.setdefault('rest', {})[path] = [st, body.decode('utf-8', 'replace')]
                        except Exception as exc:
                            log.append(f'{t:6.2f} REST {path} -> ERR {exc}')
            # devtools hunt
            dev = []
            for cmd in result['renderer_cmdlines']:
                if cmd and any(k in cmd.lower() for k in ('remote-debugging', 'devtools', 'inspect')):
                    dev.append(cmd)
            result['devtools'] = dev or None
            log.append(f'{t:6.2f} devtools-flags={dev}')
            if dev:
                for cmd in dev:
                    for part in cmd.split():
                        if part.startswith('--remote-debugging-port='):
                            p = part.split('=', 1)[1].split()[0]
                            try:
                                st, body = plain_get(f'http://127.0.0.1:{p}/json/list')
                                log.append(f'{t:6.2f} DEVTools {p} -> {st} {body[:300]!r}')
                            except Exception as exc:
                                log.append(f'{t:6.2f} DEVTools {p} -> ERR {exc}')

        if probed and not league:
            log.append(f'{t:6.2f} client tree gone')
            break
        time.sleep(0.2)

    nets = ps("Get-NetTCPConnection -State Listen | Select-Object LocalAddress,LocalPort,OwningProcess | ConvertTo-Json -Depth 3")
    open(os.path.join(OUT, f'{tag}-listen.json'), 'w', encoding='utf-8').write(nets)

    logs = sorted((os.path.join(LC_DIR, f) for f in os.listdir(LC_DIR) if f.endswith('.log')),
                  key=os.path.getmtime, reverse=True)[:10]
    for lg in logs:
        try:
            shutil.copy2(lg, os.path.join(OUT, f'{tag}-{os.path.basename(lg)}'))
        except Exception as exc:
            print('log copy failed', lg, exc)

    result['log'] = log
    open(os.path.join(OUT, f'{tag}-report.json'), 'w', encoding='utf-8').write(
        json.dumps(result, ensure_ascii=False, indent=2))
    print('\n'.join(log))
    print(f'\nreport: {OUT}\\{tag}-report.json')
    return 0


if __name__ == '__main__':
    sys.exit(main())


