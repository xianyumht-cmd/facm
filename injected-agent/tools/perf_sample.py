"""Sample LoL client resource usage over a window.

Reports per-process CPU% (100% = one full core), working set, and the number of
non-loopback TCP connections the client tree holds.

Usage: python tools\perf_sample.py --seconds 30 [--label base]
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import json
import os
import subprocess
import time

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
psapi = ctypes.WinDLL('psapi', use_last_error=True)
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000


class PROCESS_MEMORY_COUNTERS(ctypes.Structure):
    _fields_ = [('cb', wt.DWORD), ('PageFaultCount', wt.DWORD),
                ('PeakWorkingSetSize', ctypes.c_size_t), ('WorkingSetSize', ctypes.c_size_t),
                ('QuotaPeakPagedPoolUsage', ctypes.c_size_t), ('QuotaPagedPoolUsage', ctypes.c_size_t),
                ('QuotaPeakNonPagedPoolUsage', ctypes.c_size_t), ('QuotaNonPagedPoolUsage', ctypes.c_size_t),
                ('PagefileUsage', ctypes.c_size_t), ('PeakPagefileUsage', ctypes.c_size_t)]


class FILETIME(ctypes.Structure):
    _fields_ = [('dwLowDateTime', wt.DWORD), ('dwHighDateTime', wt.DWORD)]


def ft_to_int(ft):
    return (ft.dwHighDateTime << 32) | ft.dwLowDateTime


def read_proc(pid):
    h = k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h:
        return None
    try:
        ct, et, kt, ut = FILETIME(), FILETIME(), FILETIME(), FILETIME()
        if not k32.GetProcessTimes(h, ctypes.byref(ct), ctypes.byref(et), ctypes.byref(kt), ctypes.byref(ut)):
            return None
        cpu = ft_to_int(kt) + ft_to_int(ut)   # 100ns units
        pmc = PROCESS_MEMORY_COUNTERS()
        pmc.cb = ctypes.sizeof(pmc)
        psapi.GetProcessMemoryInfo(h, ctypes.byref(pmc), pmc.cb)
        return {'cpu': cpu, 'ws': pmc.WorkingSetSize}
    finally:
        k32.CloseHandle(h)


def league_pids():
    out = subprocess.run(['powershell', '-NoProfile', '-Command',
                          "Get-Process | Where-Object { $_.ProcessName -match 'LeagueClient|RiotClient|ACE' } | "
                          "ForEach-Object { \"$($_.ProcessName)|$($_.Id)\" }"],
                         capture_output=True, text=True)
    rows = []
    for line in out.stdout.splitlines():
        line = line.strip()
        if '|' in line:
            name, pid = line.split('|', 1)
            rows.append((name, int(pid)))
    return rows


def conn_counts(pids):
    out = subprocess.run(['powershell', '-NoProfile', '-Command',
                          "Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | "
                          "Select-Object OwningProcess,RemoteAddress | ConvertTo-Json -Depth 2"],
                         capture_output=True, text=True, encoding='utf-8', errors='replace')
    try:
        data = json.loads(out.stdout) if out.stdout.strip() else []
    except Exception:
        return 0, 0
    if isinstance(data, dict):
        data = [data]
    loop = remote = 0
    for c in data:
        if c.get('OwningProcess') in pids:
            addr = (c.get('RemoteAddress') or '')
            if addr.startswith('127.') or addr == '::1':
                loop += 1
            else:
                remote += 1
    return loop, remote


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--seconds', type=float, default=30)
    ap.add_argument('--label', default='sample')
    a = ap.parse_args()

    procs = league_pids()
    t0 = {}
    for name, pid in procs:
        s = read_proc(pid)
        if s:
            t0[pid] = (name, s)
    loop0, rem0 = conn_counts({p for _, p in procs})
    time.sleep(a.seconds)
    loop1, rem1 = conn_counts({p for _, p in procs})

    rows = []
    total_cpu_pct = 0.0
    total_ws = 0
    for name, pid in league_pids():
        s = read_proc(pid)
        if not s or pid not in t0:
            continue
        base_name, base = t0[pid]
        cpu_pct = (s['cpu'] - base['cpu']) / 1e7 / a.seconds * 100.0
        rows.append((base_name, pid, cpu_pct, s['ws'] / 1048576))
        total_cpu_pct += cpu_pct
        total_ws += s['ws'] / 1048576

    rows.sort(key=lambda r: -r[2])
    print(f'=== {a.label} over {a.seconds:.0f}s ===')
    print(f'{"process":<26}{"pid":>7}{"CPU%":>9}{"WS(MB)":>10}')
    for name, pid, cpu, ws in rows:
        print(f'{name:<26}{pid:>7}{cpu:>9.2f}{ws:>10.1f}')
    print(f'{"TOTAL":<26}{"":>7}{total_cpu_pct:>9.2f}{total_ws:>10.1f}')
    print(f'connections loopback {loop0}->{loop1}, remote {rem0}->{rem1}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
