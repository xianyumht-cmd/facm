"""Click a GGman agent panel button by index (0=接受, 1=自动接受, 2=点赞).

Computes the on-screen position from the overlay window rect, so it works no
matter where the client window sits.

Usage: python tools\panel_click.py 1
"""
import ctypes
import ctypes.wintypes as wt
import sys
import time

user32 = ctypes.WinDLL('user32', use_last_error=True)

BTN_W, BTN_H, PAD, GAP = 132, 26, 16, 10


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
    idx = int(sys.argv[1]) if len(sys.argv) > 1 else 1
    h = find_overlay()
    if not h:
        print('overlay not found')
        return 2
    r = wt.RECT()
    user32.GetWindowRect(h, ctypes.byref(r))
    w, hh = r.right - r.left, r.bottom - r.top
    x = PAD + idx * (BTN_W + GAP) + BTN_W // 2
    y = hh - BTN_H - 12 + BTN_H // 2
    sx, sy = r.left + x, r.top + y
    print(f'overlay=0x{h:X} rect=({r.left},{r.top},{w}x{hh}) button {idx} -> ({sx},{sy})')
    user32.SetCursorPos(sx, sy)
    time.sleep(0.2)
    user32.mouse_event(0x0002, 0, 0, 0, 0)
    time.sleep(0.08)
    user32.mouse_event(0x0004, 0, 0, 0, 0)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
