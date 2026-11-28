"""List (and optionally kill) renderer processes that carry a ggman module.

Usage:
  python tools\agent_procs.py
  python tools\agent_procs.py --kill
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import subprocess
import sys

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
TH32CS_SNAPMODULE, TH32CS_SNAPMODULE32 = 0x8, 0x10


class ME(ctypes.Structure):
    _fields_ = [('dwSize', wt.DWORD), ('th32ModuleID', wt.DWORD), ('th32ProcessID', wt.DWORD),
                ('GlblcntUsage', wt.DWORD), ('ProccntUsage', wt.DWORD),
                ('modBaseAddr', ctypes.POINTER(ctypes.c_byte)), ('modBaseSize', wt.DWORD),
                ('hModule', wt.HMODULE), ('szModule', ctypes.c_char * 256), ('szExePath', ctypes.c_char * 260)]


def modules(pid):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid)
    if snap == ctypes.c_void_p(-1).value:
        return []
    m = ME()
    m.dwSize = ctypes.sizeof(ME)
    out = []
    ok = k32.Module32First(snap, ctypes.byref(m))
    while ok:
        out.append(m.szModule.decode('mbcs', 'replace').lower())
        ok = k32.Module32Next(snap, ctypes.byref(m))
    k32.CloseHandle(snap)
    return out


def renderers():
    r = subprocess.run(['powershell', '-NoProfile', '-Command',
                        "Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUxRender.exe'\" | "
                        "Where-Object { $_.CommandLine -match '--type=renderer' } | ForEach-Object { $_.ProcessId }"],
                       capture_output=True, text=True)
    return [int(x) for x in r.stdout.split() if x.strip().isdigit()]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--kill', action='store_true')
    a = ap.parse_args()
    hits = []
    for pid in renderers():
        mods = modules(pid)
        gg = [m for m in mods if m.startswith('ggman')]
        if gg:
            hits.append((pid, gg))
            print(f'pid {pid}: {", ".join(gg)}')
    if not hits:
        print('no agent-carrying renderer')
        return 0
    if a.kill:
        for pid, _ in hits:
            print(f'killing renderer {pid}')
            subprocess.run(['powershell', '-NoProfile', '-Command', f'Stop-Process -Id {pid} -Force'],
                           capture_output=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
