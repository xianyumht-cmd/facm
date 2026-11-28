"""Keep ggman_core.dll loaded in every League client renderer.

The LCU recreates LeagueClientUxRender.exe processes on page changes and client
updates, and a DLL cannot be unloaded/reloaded reliably from user mode, so the
watcher injects a freshly named copy of the DLL whenever a renderer appears
without it.

Usage:
  python inject_watch.py --once
  python inject_watch.py --interval 2 --dll ..\\dll\\build\\ggman_core.dll
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import hashlib
import os
import shutil
import subprocess
import sys
import time

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
TH32CS_SNAPMODULE = 0x00000008
TH32CS_SNAPMODULE32 = 0x00000010


class MODULEENTRY32(ctypes.Structure):
    _fields_ = [('dwSize', wt.DWORD), ('th32ModuleID', wt.DWORD), ('th32ProcessID', wt.DWORD),
                ('GlblcntUsage', wt.DWORD), ('ProccntUsage', wt.DWORD),
                ('modBaseAddr', ctypes.POINTER(ctypes.c_byte)), ('modBaseSize', wt.DWORD),
                ('hModule', wt.HMODULE), ('szModule', ctypes.c_char * 256), ('szExePath', ctypes.c_char * 260)]


def modules(pid):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid)
    if snap == ctypes.c_void_p(-1).value:
        return []
    me = MODULEENTRY32()
    me.dwSize = ctypes.sizeof(MODULEENTRY32)
    out = []
    ok = k32.Module32First(snap, ctypes.byref(me))
    while ok:
        out.append(me.szModule.decode('mbcs', 'replace').lower())
        ok = k32.Module32Next(snap, ctypes.byref(me))
    k32.CloseHandle(snap)
    return out


def renderers():
    out = subprocess.run(
        ['powershell', '-NoProfile', '-Command',
         "Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUxRender.exe'\" | "
         "Where-Object { $_.CommandLine -match '--type=renderer' } | "
         "ForEach-Object { \"$($_.ProcessId)|$($_.CommandLine -match '--first-renderer-process')\" }"],
        capture_output=True, text=True, encoding='utf-8', errors='replace')
    rows = []
    for line in out.stdout.splitlines():
        line = line.strip()
        if '|' in line:
            pid, first = line.split('|', 1)
            rows.append((int(pid), first.lower() == 'true'))
    return rows


def inject(pid, dll, unique_name):
    """copy the dll to a unique name (a loaded module cannot be re-injected) and load it"""
    src_hash = hashlib.sha256(open(dll, 'rb').read()).hexdigest()[:8]
    dst = os.path.join(os.path.dirname(dll), f'ggman_core_{pid}_{src_hash}.dll')
    last = None
    for _attempt in range(8):          # ACE/AV may lock a fresh file briefly
        try:
            shutil.copy2(dll, dst)
            last = None
            break
        except PermissionError as exc:
            last = exc
            time.sleep(0.5)
    if last:
        raise last
    r = subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), 'lol_inject_poc.py'),
                        '--pid', str(pid), '--dll', dst],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    ok = 'module now present in target' in r.stdout and "[]" not in r.stdout.split('module now present in target:')[-1]
    return ok, dst, r.stdout.strip().splitlines()[-1] if r.stdout.strip() else r.stderr[-200:]


def overlay_alive():
    """True when at least one GGmanHudOverlay window is visible on the desktop"""
    user32 = ctypes.WinDLL('user32')
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        name = ctypes.create_unicode_buffer(128)
        user32.GetClassNameW(h, name, 128)
        if name.value == 'GGmanHudOverlay' and user32.IsWindowVisible(h):
            found.append(h)
        return True

    user32.EnumWindows(cb, 0)
    return bool(found)


def agent_enabled():
    """read agent_enabled from agent.ini (missing key -> enabled)"""
    ini = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'dll', 'build', 'agent.ini')
    try:
        for line in open(ini, encoding='utf-8', errors='replace'):
            line = line.strip()
            if line.startswith('agent_enabled'):
                return line.split('=', 1)[1].strip() not in ('0', '')
    except Exception:
        pass
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dll', default=r'D:\project2\ggman\dll\build\ggman_core.dll')
    ap.add_argument('--interval', type=float, default=2.0)
    ap.add_argument('--once', action='store_true')
    args = ap.parse_args()

    while True:
        if not agent_enabled():
            print('agent_enabled=0 in agent.ini; not injecting', flush=True)
            if args.once:
                return 0
            time.sleep(args.interval)
            continue
        targets = renderers()
        alive = overlay_alive()
        if not alive and targets:
            # prefer a renderer that does not carry the module yet
            candidates = [pid for pid, _ in targets if not any(m.startswith('ggman') for m in modules(pid))]
            pid = candidates[0] if candidates else targets[0][0]
            ok, dst, detail = inject(pid, args.dll, True)
            tag = 'first-renderer' if dict(targets).get(pid) else 'renderer'
            print(f'[{"ok" if ok else "FAIL"}] overlay missing -> inject pid {pid} ({tag}) '
                  f'{os.path.basename(dst)} :: {detail}', flush=True)
        elif alive:
            print(f'hud alive ({len(targets)} renderer(s)); nothing to do', flush=True)
        else:
            print('no renderer running', flush=True)
        if args.once:
            return 0
        time.sleep(args.interval)


if __name__ == '__main__':
    sys.exit(main())




