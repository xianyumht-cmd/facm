"""Pick the renderer that actually draws the client UI: the process owning visible child
windows of the RCLIENT window (works even when --first-renderer-process is gone)."""
import ctypes
import ctypes.wintypes as wt

u = ctypes.WinDLL('user32', use_last_error=True)
u.GetWindow.restype = wt.HWND
u.GetWindowLongW.restype = ctypes.c_long


def cls(h):
    b = ctypes.create_unicode_buffer(128)
    u.GetClassNameW(h, b, 128)
    return b.value


def rect(h):
    r = wt.RECT()
    u.GetWindowRect(h, ctypes.byref(r))
    return (r.left, r.top, r.right, r.bottom)


def visible(h):
    return bool(u.IsWindowVisible(h))


def main():
    hosts = []
    proc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)(lambda h, l: hosts.append(h) or True)
    u.EnumWindows(proc, 0)
    client = [h for h in hosts if cls(h) == 'RCLIENT']
    if not client:
        print('NO_CLIENT')
        return 2
    ch = max(client, key=lambda h: (rect(h)[2] - rect(h)[0]) * (rect(h)[3] - rect(h)[1]))
    print('client', hex(ch), rect(ch))

    pids = {}
    child = u.GetWindow(ch, 5)      # GW_CHILD
    sib = u.GetWindow(ch, 6)        # GW_HWNDNEXT
    h = child
    while h:
        if visible(h):
            pid = wt.DWORD()
            u.GetWindowThreadProcessId(h, ctypes.byref(pid))
            if pid.value:
                r = rect(h)
                area = max(0, r[2] - r[0]) * max(0, r[3] - r[1])
                pids[pid.value] = max(pids.get(pid.value, 0), area)
        h = u.GetWindow(h, 2)       # GW_HWNDNEXT

    if not pids:
        print('NO_RENDERER')
        return 3
    best = max(pids.items(), key=lambda kv: kv[1])[0]
    for pid, area in sorted(pids.items(), key=lambda kv: -kv[1]):
        print(f'pid {pid} area {area}')
    print('BEST', best)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
