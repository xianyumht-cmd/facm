"""Click the WeGame 启动 button (window found by class CefTopWindow / title WeGame).

Usage: python wegame_launch.py [--wait 60]
Prints client process appearance and, once up, the LCU port from the Ux command line.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import json
import subprocess
import sys
import time

user32 = ctypes.WinDLL('user32', use_last_error=True)
user32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
user32.GetClassNameW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
user32.GetWindowTextW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]

BUTTON_REL = (1375, 852)


def find_wegame():
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        if not user32.IsWindowVisible(h):
            return True
        cls = ctypes.create_unicode_buffer(256)
        ttl = ctypes.create_unicode_buffer(512)
        user32.GetClassNameW(h, cls, 256)
        user32.GetWindowTextW(h, ttl, 512)
        if ttl.value.strip() == 'WeGame' and cls.value in ('CefTopWindow', 'Chrome_WidgetWin_1', 'Chrome_WidgetWin_0'):
            r = wt.RECT()
            user32.GetWindowRect(h, ctypes.byref(r))
            if r.right - r.left > 800:
                found.append((h, r))
        return True

    user32.EnumWindows(cb, 0)
    return max(found, key=lambda x: (x[1].right - x[1].left) * (x[1].bottom - x[1].top)) if found else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--wait', type=int, default=90)
    ap.add_argument('--no-click', action='store_true')
    args = ap.parse_args()

    if not args.no_click:
        win = find_wegame()
        if not win:
            print('WeGame window not found')
            return 1
        h, r = win
        print(f'WeGame hwnd={h} rect=({r.left},{r.top},{r.right},{r.bottom})')
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

    deadline = time.time() + args.wait
    last = ''
    while time.time() < deadline:
        time.sleep(2)
        out = subprocess.run(
            ['powershell', '-NoProfile', '-Command',
             "Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'LeagueClient|RiotClient' } | "
             "ForEach-Object { $_.Name } | Sort-Object -Unique"],
            capture_output=True, text=True, encoding='utf-8', errors='replace').stdout.strip().replace('\r', '')
        names = ','.join(out.split('\n'))
        if names != last:
            print(f'  {names}')
            last = names
        if 'LeagueClientUx' in names:
            cmd = subprocess.run(
                ['powershell', '-NoProfile', '-Command',
                 "(Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUx.exe'\").CommandLine"],
                capture_output=True, text=True, encoding='utf-8', errors='replace').stdout
            port = next((p.split('=', 1)[1] for p in cmd.replace('"', ' ').split() if p.startswith('--app-port=')), None)
            print(f'Ux up, LCU port = {port}')
            return 0
    print('timeout waiting for LeagueClientUx')
    return 2


if __name__ == '__main__':
    sys.exit(main())
