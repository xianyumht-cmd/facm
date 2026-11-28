"""Diagnose reading another process's command line via PEB (same-user, x64).

Checks: OpenProcess rights, NtQueryInformationProcess, PEB->ProcessParameters,
RTL_USER_PROCESS_PARAMETERS->CommandLine, and reads the UNICODE_STRING buffer.
"""
import ctypes
import ctypes.wintypes as wt
import subprocess
import sys

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
ntdll = ctypes.WinDLL('ntdll')


class PROCESS_BASIC_INFORMATION(ctypes.Structure):
    _fields_ = [('Reserved1', wt.LPVOID), ('PebBaseAddress', wt.LPVOID),
                ('Reserved2', wt.LPVOID * 2), ('UniqueProcessId', ctypes.c_size_t),
                ('Reserved3', wt.LPVOID)]


def pid_of(name):
    out = subprocess.run(['powershell', '-NoProfile', '-Command',
                          f"(Get-Process {name} -ErrorAction SilentlyContinue | Select-Object -First 1).Id"],
                         capture_output=True, text=True)
    try:
        return int(out.stdout.strip())
    except Exception:
        return 0


def main():
    names = sys.argv[1:] or ['LeagueClientUx', 'LeagueClientUxRender']
    for name in names:
        pid = pid_of(name)
        print(f'== {name} pid={pid}')
        if not pid:
            continue
        for rights, label in ((0x0410, 'QUERY_LIMITED|VM_READ'), (0x1F0FFF, 'ALL_ACCESS')):
            h = k32.OpenProcess(rights, False, pid)
            print(f'   OpenProcess(0x{rights:X} {label}) -> {h} err={ctypes.get_last_error()}')
            if not h:
                continue
            pbi = PROCESS_BASIC_INFORMATION()
            ret_len = wt.ULONG(0)
            st = ntdll.NtQueryInformationProcess(h, 0, ctypes.byref(pbi), ctypes.sizeof(pbi), ctypes.byref(ret_len))
            print(f'   NtQueryInformationProcess status=0x{st & 0xFFFFFFFF:X} peb=0x{(pbi.PebBaseAddress or 0):X}')
            if st == 0 and pbi.PebBaseAddress:
                params = ctypes.c_void_p()
                got = ctypes.c_size_t()
                ok = k32.ReadProcessMemory(h, ctypes.c_void_p(pbi.PebBaseAddress + 0x20),
                                           ctypes.byref(params), 8, ctypes.byref(got))
                print(f'   PEB+0x20 ProcessParameters ok={bool(ok)} ptr=0x{(params.value or 0):X} '
                      f'err={ctypes.get_last_error()}')
                if ok and params.value:
                    base = params.value + 0x70
                    buf = (ctypes.c_ubyte * 16)()
                    ok2 = k32.ReadProcessMemory(h, ctypes.c_void_p(base), buf, 16, ctypes.byref(got))
                    if ok2:
                        length = int.from_bytes(bytes(buf[0:2]), 'little')
                        maxlen = int.from_bytes(bytes(buf[2:4]), 'little')
                        ptr = int.from_bytes(bytes(buf[8:16]), 'little')
                        print(f'   CommandLine Length={length} Max={maxlen} Buffer=0x{ptr:X}')
                        if ptr and length:
                            text = ctypes.create_unicode_buffer(length // 2 + 1)
                            ok3 = k32.ReadProcessMemory(h, ctypes.c_void_p(ptr), text, length,
                                                        ctypes.byref(got))
                            print(f'   read buffer ok={bool(ok3)} err={ctypes.get_last_error()}')
                            if ok3:
                                cmd = text.value
                                print(f'   cmdline[:200]={cmd[:200]!r}')
                                for part in cmd.replace('"', ' ').split():
                                    if part.startswith('--remoting-auth-token='):
                                        print(f'   TOKEN OK len={len(part.split("=", 1)[1])}')
                    else:
                        print(f'   read UNICODE_STRING failed err={ctypes.get_last_error()}')
            k32.CloseHandle(h)
            break
    return 0


if __name__ == '__main__':
    sys.exit(main())
