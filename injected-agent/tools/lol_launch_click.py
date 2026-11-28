"""Click the WeGame 启动 button and watch what the game client does.

Pure ctypes: bring WeGame forward, click the launch button, then poll for
LeagueClient* processes and lockfile contents.
"""
import ctypes
import ctypes.wintypes as wt
import json
import os
import subprocess
import sys
import time

user32 = ctypes.WinDLL('user32', use_last_error=True)
user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
user32.mouse_event.argtypes = [wt.DWORD, wt.DWORD, wt.DWORD, wt.DWORD, ctypes.c_size_t]
user32.SetForegroundWindow.argtypes = [wt.HWND]
user32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]

WEGAME_PID = 2896
BUTTON_REL = (1375, 852)   # window-relative coords of 启动 in the 1600x900 client area

MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004


def win_rect(pid):
    # find the top-level window of the pid
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(hwnd, lparam):
        pid_out = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid_out))
        if pid_out.value == pid and user32.IsWindowVisible(hwnd):
            r = wt.RECT()
            user32.GetWindowRect(hwnd, ctypes.byref(r))
            if r.right - r.left > 400:
                found.append((hwnd, r))
        return True

    user32.EnumWindows(cb, 0)
    found.sort(key=lambda x: (x[1].right - x[1].left) * (x[1].bottom - x[1].top), reverse=True)
    return found[0] if found else (None, None)


def click(x, y):
    user32.SetCursorPos(int(x), int(y))
    time.sleep(0.25)
    user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.12)
    user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)


def procs():
    out = subprocess.run(
        ['powershell', '-NoProfile', '-Command',
         "Get-Process | Where-Object { $_.ProcessName -match 'LeagueClient|RiotClient|TCLS|client|tenio|ACE|AntiCheat|TenProtect|LeagueCrash' } "
         "| ForEach-Object { \"$($_.ProcessName)|$($_.Id)\" }"],
        capture_output=True, text=True, encoding='utf-8', errors='replace')
    return [l for l in out.stdout.splitlines() if l.strip()]


LOCK = r'D:\WeGameApps\英雄联盟\LeagueClient\lockfile'


def main():
    hwnd, r = win_rect(WEGAME_PID)
    if not hwnd:
        print('WeGame window not found')
        return 1
    print(f'hwnd={hwnd} rect=({r.left},{r.top},{r.right},{r.bottom})')
    user32.ShowWindow(hwnd, 9)  # SW_RESTORE
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.8)
    x, y = r.left + BUTTON_REL[0], r.top + BUTTON_REL[1]
    print(f'clicking 启动 at screen ({x},{y})')
    click(x, y)

    for i in range(45):
        time.sleep(2)
        ps = procs()
        lock = ''
        try:
            lock = open(LOCK, 'rb').read().decode('utf-8', 'replace').strip()
        except Exception:
            pass
        print(f'[{(i + 1) * 2:3d}s] lockfile={lock!r} procs={ps}', flush=True)
        if lock:
            print('LOCKFILE POPULATED — LCU session available')
            return 0
    return 2


if __name__ == '__main__':
    sys.exit(main())
