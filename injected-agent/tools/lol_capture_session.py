"""Relaunch LoL via WeGame, then capture the short-lived client session.

Captures:
  * per-process lifetimes (fast Toolhelp32 poll, 250 ms)
  * full command lines of every LeagueClient*/RiotClient* process
  * listening TCP ports per PID
  * lockfile contents
  * newest LeagueClientUx*.log files copied out

Usage: python lol_capture_session.py [--no-click]
"""
import ctypes
import ctypes.wintypes as wt
import json
import os
import shutil
import subprocess
import sys
import time
from datetime import datetime

user32 = ctypes.WinDLL('user32', use_last_error=True)
k32 = ctypes.WinDLL('kernel32', use_last_error=True)
user32.SetForegroundWindow.argtypes = [wt.HWND]
user32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]

WEGAME_PID = 2896
BUTTON_REL = (1375, 852)
MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP = 0x0002, 0x0004

ROOT = r'D:\WeGameApps\英雄联盟'
LC_DIR = os.path.join(ROOT, 'LeagueClient')
OUT = r'D:\project2\ggman\captures'

TH32CS_SNAPPROCESS = 0x00000002


class PROCESSENTRY32(ctypes.Structure):
    _fields_ = [
        ('dwSize', wt.DWORD), ('cntUsage', wt.DWORD), ('th32ProcessID', wt.DWORD),
        ('th32DefaultHeapID', ctypes.POINTER(ctypes.c_ulong)), ('th32ModuleID', wt.DWORD),
        ('cntThreads', wt.DWORD), ('th32ParentProcessID', wt.DWORD), ('pcPriClassBase', ctypes.c_long),
        ('dwFlags', wt.DWORD), ('szExeFile', ctypes.c_char * 260)]


def snapshot():
    """pid -> exe name"""
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snap == -1:
        return {}
    entry = PROCESSENTRY32()
    entry.dwSize = ctypes.sizeof(PROCESSENTRY32)
    out = {}
    ok = k32.Process32First(snap, ctypes.byref(entry))
    while ok:
        out[entry.th32ProcessID] = entry.szExeFile.decode('mbcs', 'replace')
        ok = k32.Process32Next(snap, ctypes.byref(entry))
    k32.CloseHandle(snap)
    return out


def ps(cmd):
    r = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', cmd],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.stdout


def click_launch():
    hwnd = wt.HWND()

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        p = wt.DWORD()
        user32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == WEGAME_PID and user32.IsWindowVisible(h):
            r = wt.RECT()
            user32.GetWindowRect(h, ctypes.byref(r))
            if r.right - r.left > 800:
                found.append((h, r))
        return True

    found = []
    user32.EnumWindows(cb, 0)
    if not found:
        print('WeGame window missing')
        return False
    h, r = max(found, key=lambda x: (x[1].right - x[1].left) * (x[1].bottom - x[1].top))
    user32.ShowWindow(h, 9)
    user32.SetForegroundWindow(h)
    time.sleep(0.7)
    x, y = r.left + BUTTON_REL[0], r.top + BUTTON_REL[1]
    print(f'click 启动 at ({x},{y})')
    user32.SetCursorPos(x, y)
    time.sleep(0.2)
    user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.1)
    user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    return True


def main():
    os.makedirs(OUT, exist_ok=True)
    stamp = datetime.now().strftime('%Y%m%d-%H%M%S')
    if '--no-click' not in sys.argv:
        if not click_launch():
            return 1

    timeline = []
    seen = {}
    cmdline_done = set()
    lock_seen = ''
    deadline = time.time() + 60
    while time.time() < deadline:
        snap = snapshot()
        league = {p: n for p, n in snap.items() if n.lower().startswith(('leagueclient', 'riotclient', 'ace-'))}
        now = round(time.time() - (deadline - 60), 2)
        for p, n in league.items():
            if p not in seen:
                seen[p] = {'pid': p, 'name': n, 'start': now}
                timeline.append((now, 'start', p, n))
        for p in list(seen):
            if p not in snap and 'end' not in seen[p]:
                seen[p]['end'] = now
                timeline.append((now, 'end', p, seen[p]['name']))

        lock = ''
        try:
            lock = open(os.path.join(LC_DIR, 'lockfile'), 'rb').read().decode('utf-8', 'replace').strip()
        except Exception:
            pass
        if lock and not lock_seen:
            lock_seen = lock
            timeline.append((now, 'lockfile', 0, lock))

        # once Ux is up for a moment, grab command lines / ports
        ux = [p for p, n in league.items() if n.lower().startswith('leagueclientux')]
        if ux and 'ux' not in cmdline_done:
            cmdline_done.add('ux')
            time.sleep(2.2)
            dump = ps("Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'LeagueClient|RiotClient|ACE-' } "
                      "| Select-Object ProcessId,ParentProcessId,Name,CommandLine | ConvertTo-Json -Depth 3")
            open(os.path.join(OUT, f'{stamp}-cmdlines.json'), 'w', encoding='utf-8').write(dump)
            nets = ps("Get-NetTCPConnection -State Listen | Select-Object LocalAddress,LocalPort,OwningProcess | ConvertTo-Json -Depth 3")
            open(os.path.join(OUT, f'{stamp}-listen.json'), 'w', encoding='utf-8').write(nets)
            tree = ps("Get-Process | Where-Object { $_.ProcessName -match 'LeagueClient|RiotClient|ACE-' } "
                      "| Select-Object Id,ProcessName,SessionId,StartTime,WorkingSet64 | ConvertTo-Json -Depth 3")
            open(os.path.join(OUT, f'{stamp}-procs.json'), 'w', encoding='utf-8').write(tree)

        if not league and len(seen) > 3:
            timeline.append((now, 'all-gone', 0, len(seen)))
            break
        time.sleep(0.25)

    # collect logs
    logs = sorted((os.path.join(LC_DIR, f) for f in os.listdir(LC_DIR) if f.endswith('.log')),
                  key=os.path.getmtime, reverse=True)[:8]
    for lg in logs:
        try:
            shutil.copy2(lg, os.path.join(OUT, f'{stamp}-{os.path.basename(lg)}'))
        except Exception as e:
            print('log copy failed', lg, e)

    with open(os.path.join(OUT, f'{stamp}-timeline.txt'), 'w', encoding='utf-8') as f:
        for t, kind, pid, name in timeline:
            f.write(f'{t:8.2f}s {kind:10s} {pid:>7} {name}\n')
        f.write(f'\nlockfile={lock_seen!r}\n')
        f.write('\nprocesses seen:\n')
        for p, info in seen.items():
            f.write(f"  {info['name']} pid={p} alive={info.get('start')}->{info.get('end','?')}\n")

    print(f'capture dir: {OUT} stamp={stamp}')
    for t, kind, pid, name in timeline:
        print(f'{t:8.2f}s {kind:10s} {pid:>7} {name}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
