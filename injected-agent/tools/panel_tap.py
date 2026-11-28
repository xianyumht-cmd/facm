"""Tap at panel-relative coordinates inside the GGman agent overlay.

Usage: python tools\panel_tap.py 229 48      # tab 2 (自动化)
       python tools\panel_tap.py 300 138     # row 3 toggle
"""
import ctypes
import ctypes.wintypes as wt
import sys
import time

user32 = ctypes.WinDLL('user32', use_last_error=True)


def find_overlay():
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        buf = ctypes.create_unicode_buffer(128)
        user32.GetClassNameW(h, buf, 128)
        if buf.value == 'GGmanHudOverlay' and user32.IsWindowVisible(h):
            found.append(h)
        return True

    user32.EnumWindows(cb, 0)
    return found[0] if found else None


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    px, py = int(sys.argv[1]), int(sys.argv[2])
    h = find_overlay()
    if not h:
        print('overlay not found')
        return 2
    r = wt.RECT()
    user32.GetWindowRect(h, ctypes.byref(r))
    sx, sy = r.left + px, r.top + py
    print(f'overlay=0x{h:X} rect=({r.left},{r.top}) panel-rel=({px},{py}) -> screen ({sx},{sy})')
    user32.SetCursorPos(sx, sy)
    time.sleep(0.2)
    user32.mouse_event(0x0002, 0, 0, 0, 0)
    time.sleep(0.08)
    user32.mouse_event(0x0004, 0, 0, 0, 0)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
