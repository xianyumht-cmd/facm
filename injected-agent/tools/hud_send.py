"""Push text into the injected GGman HUD (ggman_hud.dll).

Talks to the DLL's hidden IPC window (class "GGmanHudIPC") through WM_COPYDATA.

Usage:
  python hud_send.py "第一行" "第二行" ...
  python hud_send.py --file lines.txt
  python hud_send.py --toggle
  python hud_send.py --clear
  python hud_send.py --whoami        # just prove the IPC window is reachable
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import sys

user32 = ctypes.WinDLL('user32', use_last_error=True)
user32.FindWindowW.restype = wt.HWND
user32.FindWindowW.argtypes = [wt.LPCWSTR, wt.LPCWSTR]
user32.SendMessageW.restype = ctypes.c_ssize_t
user32.SendMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]

TEXT_LEN = 2048


class Cmd(ctypes.Structure):
    _fields_ = [('kind', wt.DWORD), ('flags', wt.DWORD), ('text', ctypes.c_wchar * TEXT_LEN)]


class COPYDATASTRUCT(ctypes.Structure):
    _fields_ = [('dwData', ctypes.c_size_t), ('cbData', wt.DWORD), ('lpData', ctypes.c_void_p)]


WM_COPYDATA = 0x004A


def send(kind, flags=0, text=''):
    hwnd = user32.FindWindowW('GGmanHudIPC', None)
    if not hwnd:
        return False, 'ipc window not found (hud dll not injected?)'
    cmd = Cmd(kind=kind, flags=flags, text=text[:TEXT_LEN - 1])
    cds = COPYDATASTRUCT(dwData=0x474D414E, cbData=ctypes.sizeof(cmd), lpData=ctypes.cast(ctypes.pointer(cmd), ctypes.c_void_p))
    r = user32.SendMessageW(hwnd, WM_COPYDATA, 0, ctypes.cast(ctypes.pointer(cds), ctypes.c_void_p).value)
    return bool(r), f'ipc hwnd=0x{hwnd:X} reply={r}'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('lines', nargs='*')
    ap.add_argument('--file')
    ap.add_argument('--toggle', action='store_true')
    ap.add_argument('--show', action='store_true')
    ap.add_argument('--hide', action='store_true')
    ap.add_argument('--clear', action='store_true')
    ap.add_argument('--arm', action='store_true', help='arm auto-accept (agent only)')
    ap.add_argument('--disarm', action='store_true')
    ap.add_argument('--accept', action='store_true', help='accept ready check now')
    ap.add_argument('--session', help='PORT:TOKEN override for the in-client agent')
    ap.add_argument('--action', type=int, help='run tool action id (0 accept,1 honor,2 play-again,3 reload,4 dodge,5 mark,6 bench,7 invite,8 mute,9 shutdown)')
    ap.add_argument('--whoami', action='store_true')
    args = ap.parse_args()

    if args.session:
        port, _, token = args.session.partition(':')
        print(send(5, int(port), token))
        return 0
    if args.arm:
        print(send(6, 1))
        return 0
    if args.disarm:
        print(send(6, 0))
        return 0
    if args.accept:
        print(send(7, 0))
        return 0

    if args.whoami:
        hwnd = user32.FindWindowW('GGmanHudIPC', None)
        print(f'GGmanHudIPC hwnd = 0x{hwnd:X}' if hwnd else 'GGmanHudIPC not found')
        return 0 if hwnd else 2
    if args.action is not None:
        print(send(8, args.action))
        return 0
    if args.toggle:
        print(send(2, 2))
        return 0
    if args.show:
        print(send(2, 1))
        return 0
    if args.hide:
        print(send(2, 0))
        return 0
    if args.clear:
        print(send(3, 0))
        return 0

    lines = list(args.lines)
    if args.file:
        lines += [l.rstrip('\n') for l in open(args.file, encoding='utf-8')]
    if not lines:
        print('nothing to send')
        return 1
    ok, detail = send(1, 0, '\n'.join(lines[:8]))
    print(ok, detail)
    return 0 if ok else 3


if __name__ == '__main__':
    sys.exit(main())
