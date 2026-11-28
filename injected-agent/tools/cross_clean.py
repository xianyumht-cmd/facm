r"""Cross 组件瘦身工具：检测 / 结束 / 停用（重命名）/ 恢复。

League 客户端的直播、录制、教练等组件都在 <安装目录>\\Cross 下，
它们与对局无关，但会在后台占用 CPU/磁盘。本工具提供可逆的"停用"方式。

用法：
  python tools\cross_clean.py                 # 报告：哪些 Cross 进程在跑、目录多大
  python tools\cross_clean.py --kill          # 结束所有 Cross 下的进程
  python tools\cross_clean.py --rename        # 把 Cross 改名为 Cross.disabled_<时间戳>
  python tools\cross_clean.py --restore       # 恢复最近一次改名
  python tools\cross_clean.py --restore 20261125-073000
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time

CANDIDATES = [
    r'D:\WeGameApps\英雄联盟',
    r'C:\Program Files\WeGame\Games\英雄联盟',
    r'D:\Program Files\WeGame\Games\英雄联盟',
]


def find_install():
    """install root = has Game\\ + LeagueClient\\ (Cross may already be renamed)"""
    def looks_like_install(p):
        return (os.path.isdir(os.path.join(p, 'Game'))
                and os.path.isdir(os.path.join(p, 'LeagueClient')))
    for c in CANDIDATES:
        if looks_like_install(c):
            return c
    # fall back: scan drives one level deep
    for drive in ('C:\\', 'D:\\', 'E:\\'):
        if not os.path.isdir(drive):
            continue
        for name in os.listdir(drive):
            p = os.path.join(drive, name)
            if looks_like_install(p):
                return p
    return None


def processes_under(root):
    """[(pid, name, path)] for processes whose exe lives under root"""
    ps = ("Get-CimInstance Win32_Process | Select-Object ProcessId,Name,ExecutablePath | ConvertTo-Json -Depth 3")
    out = subprocess.run(['powershell', '-NoProfile', '-Command', ps],
                         capture_output=True, text=True, encoding='utf-8', errors='replace')
    try:
        data = json.loads(out.stdout) if out.stdout.strip() else []
    except Exception:
        return []
    if isinstance(data, dict):
        data = [data]
    root_l = os.path.normcase(root)
    hits = []
    for p in data:
        exe = p.get('ExecutablePath') or ''
        if exe and os.path.normcase(exe).startswith(root_l):
            hits.append((p.get('ProcessId'), p.get('Name'), exe))
    return hits


def dir_size(path):
    total = 0
    for base, _dirs, files in os.walk(path):
        for f in files:
            try:
                total += os.path.getsize(os.path.join(base, f))
            except OSError:
                pass
    return total


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--kill', action='store_true')
    ap.add_argument('--rename', action='store_true')
    ap.add_argument('--restore', nargs='?', const='latest')
    ap.add_argument('--force', action='store_true')
    a = ap.parse_args()

    inst = find_install()
    if not inst:
        print('找不到英雄联盟安装目录（Cross 子目录不存在）')
        return 2
    cross = os.path.join(inst, 'Cross')
    print(f'安装目录: {inst}')
    if os.path.isdir(cross):
        print(f'Cross    : {cross}  ({dir_size(cross) / 1048576:.1f} MB)')
    else:
        print('Cross    : 不存在（可能已被停用，可用 --restore 恢复）')

    procs = processes_under(cross)
    print(f'\nCross 下正在运行的进程: {len(procs)}')
    for pid, name, exe in procs:
        print(f'  pid {pid:<7} {name:<22} {exe}')

    # 其他相关进程（客户端本体），改名前置检查
    others = [p for p in processes_under(inst) if not os.path.normcase(p[2] or '').startswith(os.path.normcase(cross))]

    if a.kill:
        for pid, name, _ in procs:
            subprocess.run(['powershell', '-NoProfile', '-Command', f'Stop-Process -Id {pid} -Force -ErrorAction SilentlyContinue'])
            print(f'killed {pid} {name}')
        time.sleep(1)
        left = processes_under(cross)
        print(f'剩余: {len(left)}')
        return 0

    if a.rename:
        if procs and not a.force:
            print('\n还有 Cross 进程在跑；先 --kill，或加 --force 强停后改名')
            return 3
        if procs and a.force:
            for pid, name, _ in procs:
                subprocess.run(['powershell', '-NoProfile', '-Command', f'Stop-Process -Id {pid} -Force -ErrorAction SilentlyContinue'])
            time.sleep(1)
        if others and not a.force:
            names = ', '.join(sorted({n for _, n, _ in others}))
            print(f'\n客户端/启动器还在运行（{names}）——先完全退出客户端，或加 --force')
            return 3
        stamp = time.strftime('%Y%m%d-%H%M%S')
        dst = cross + '.disabled_' + stamp
        os.rename(cross, dst)
        with open(os.path.join(inst, 'GGMAN-CROSS-RESTORE.txt'), 'w', encoding='utf-8') as f:
            f.write(f'Cross 已停用于 {stamp}\n恢复：python tools\\cross_clean.py --restore {stamp}\n'
                    f'或手工把 {dst} 改回 Cross\n')
        print(f'\n已停用: {cross} -> {dst}')
        print('恢复: python tools\\cross_clean.py --restore ' + stamp)
        return 0

    if a.restore:
        want = a.restore
        if want == 'latest':
            cands = sorted([d for d in os.listdir(inst) if d.startswith('Cross.disabled_')])
            if not cands:
                print('没有找到可恢复的 Cross.disabled_*')
                return 2
            want = cands[-1].replace('Cross.disabled_', '')
        src = os.path.join(inst, 'Cross.disabled_' + want)
        if not os.path.isdir(src):
            print(f'找不到 {src}')
            return 2
        if os.path.isdir(cross):
            print(f'{cross} 已存在，先移开再恢复')
            return 3
        os.rename(src, cross)
        print(f'已恢复: {src} -> {cross}')
        return 0

    # 默认：报告
    print('\n提示：--kill 只结束进程（下次客户端启动还会起来）；--rename 才是真正停用')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
