"""Measure how tightly the overlay windows follow the client window while dragging.

Moves the client window step by step and, after each step, reads the badge/panel
positions to compute the tracking error (in pixels) against the expected anchor.

Usage: python tools/follow_test.py [steps] [step_ms]
"""
import ctypes
import ctypes.wintypes as wt
import statistics
import sys
import time

u = ctypes.WinDLL('user32')

HUD_W, HUD_H, MARGIN = 600, 372, 18
BADGE_AX, BADGE_AY = 404, 10


def rect(cls, visible_only=True):
    out = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, l):
        b = ctypes.create_unicode_buffer(128)
        u.GetClassNameW(h, b, 128)
        if b.value == cls and (u.IsWindowVisible(h) or not visible_only):
            r = wt.RECT()
            u.GetWindowRect(h, ctypes.byref(r))
            out.append((r.left, r.top, r.right, r.bottom))
        return True

    u.EnumWindows(cb, 0)
    out.sort(key=lambda r: -((r[2] - r[0]) * (r[3] - r[1])))
    return out


def main():
    steps = int(sys.argv[1]) if len(sys.argv) > 1 else 40
    step_ms = float(sys.argv[2]) if len(sys.argv) > 2 else 16.0

    c = rect('RCLIENT')[0]
    hwnd = None

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def find(h, l):
        nonlocal hwnd
        b = ctypes.create_unicode_buffer(128)
        u.GetClassNameW(h, b, 128)
        if b.value == 'RCLIENT' and u.IsWindowVisible(h):
            r = wt.RECT()
            u.GetWindowRect(h, ctypes.byref(r))
            if (r.right - r.left) > 600:
                hwnd = h
                return False
        return True

    u.EnumWindows(find, 0)
    if not hwnd:
        print('client window not found')
        return 2

    badge_err, panel_err = [], []
    x0, y0 = c[0], c[1]
    for i in range(steps):
        nx, ny = x0 + i * 5, y0 + i * 3
        u.SetWindowPos(hwnd, 0, nx, ny, 0, 0, 0x0001 | 0x0004)   # NOSIZE|NOZORDER
        time.sleep(step_ms / 1000.0)

        cc = rect('RCLIENT')[0]
        b = rect('GGmanHudToggle')
        p = rect('GGmanHudOverlay')
        if b:
            badge_err.append(abs((b[0][0] - cc[0]) - BADGE_AX) + abs((b[0][1] - cc[1]) - BADGE_AY))
        if p:
            exp_x = MARGIN
            exp_y = (cc[3] - cc[1]) - HUD_H - MARGIN
            panel_err.append(abs((p[0][0] - cc[0]) - exp_x) + abs((p[0][1] - cc[1]) - exp_y))

    def stat(name, errs):
        if not errs:
            print(f'{name}: no samples')
            return
        print(f'{name}: samples={len(errs)} mean={statistics.mean(errs):.1f}px '
              f'max={max(errs)}px p95={sorted(errs)[int(len(errs)*0.95)-1]}px')

    stat('badge', badge_err)
    stat('panel', panel_err)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
