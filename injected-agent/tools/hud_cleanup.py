"""Close every GGmanHudOverlay window whose owner process is not the wanted pid.

Usage: python hud_cleanup.py --keep <pid>
       python hud_cleanup.py --list
"""
import argparse
import ctypes
import ctypes.wintypes as wt

user32 = ctypes.WinDLL('user32', use_last_error=True)


def windows(cls):
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        name = ctypes.create_unicode_buffer(128)
        user32.GetClassNameW(h, name, 128)
        if name.value == cls:
            pid = wt.DWORD()
            user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
            found.append((h, pid.value))
        return True

    user32.EnumWindows(cb, 0)
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--keep', type=int)
    ap.add_argument('--list', action='store_true')
    args = ap.parse_args()

    for cls in ('GGmanHudOverlay', 'GGmanHudIPC'):
        pass
    overlays = windows('GGmanHudOverlay')
    ipcs = windows('GGmanHudIPC')
    if args.list:
        for h, pid in overlays:
            print(f'overlay hwnd=0x{h:X} pid={pid}')
        for h, pid in ipcs:
            print(f'ipc     hwnd=0x{h:X} pid={pid}')
        return 0

    for h, pid in overlays + ipcs:
        if args.keep is not None and pid == args.keep:
            continue
        user32.PostMessageW(h, 0x0010, 0, 0)
        print(f'sent WM_CLOSE to 0x{h:X} (pid {pid})')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

