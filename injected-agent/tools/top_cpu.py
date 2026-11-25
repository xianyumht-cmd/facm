"""Top CPU consumers system-wide over a sample window."""
import ctypes
import ctypes.wintypes as wt
import subprocess
import sys
import time

k32 = ctypes.WinDLL('kernel32', use_last_error=True)


class FT(ctypes.Structure):
    _fields_ = [('lo', wt.DWORD), ('hi', wt.DWORD)]


def fti(f):
    return (f.hi << 32) | f.lo


def cpu(pid):
    h = k32.OpenProcess(0x1000, False, pid)
    if not h:
        return None
    try:
        c, e, k, u = FT(), FT(), FT(), FT()
        if not k32.GetProcessTimes(h, ctypes.byref(c), ctypes.byref(e), ctypes.byref(k), ctypes.byref(u)):
            return None
        return fti(k) + fti(u)
    finally:
        k32.CloseHandle(h)


PS = ("Get-Process | ForEach-Object { \"$($_.ProcessName)|$($_.Id)|"
      "$([math]::Round($_.WorkingSet64/1MB,0))\" }")


def procs():
    out = subprocess.run(['powershell', '-NoProfile', '-Command', PS],
                         capture_output=True, text=True, encoding='utf-8', errors='replace').stdout
    d = {}
    for line in out.splitlines():
        p = line.strip().split('|')
        if len(p) == 3 and p[1].isdigit():
            d[int(p[1])] = (p[0], int(p[2]))
    return d


def main():
    secs = float(sys.argv[1]) if len(sys.argv) > 1 else 20.0
    t0 = {}
    for pid in procs():
        v = cpu(pid)
        if v is not None:
            t0[pid] = v
    time.sleep(secs)
    rows = []
    for pid, (name, ws) in procs().items():
        if pid not in t0:
            continue
        v = cpu(pid)
        if v is None:
            continue
        rows.append(((v - t0[pid]) / 1e7 / secs * 100.0, name, pid, ws))
    rows.sort(reverse=True)
    print(f'{"CPU%":>7}  {"process":<30}{"pid":>7}{"WS(MB)":>9}')
    for pct, name, pid, ws in rows[:15]:
        print(f'{pct:>7.2f}  {name:<30}{pid:>7}{ws:>9}')
    total = sum(r[0] for r in rows if r[0] > 0.5)
    print(f'... sum of processes above 0.5%: {total:.1f}%')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
