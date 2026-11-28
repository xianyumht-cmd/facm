"""把客户端落地页指向 GGman 本地控制台（可逆）。默认只打印，不改文件。

用法：
  python tools\\set_landing.py                # 查看当前值
  python tools\\set_landing.py --apply        # 备份 system.yaml 后写入 custom_landing_url
  python tools\\set_landing.py --restore      # 从 .bak 还原
"""
import argparse
import os
import re
import shutil
import sys
import time

INSTALLS = [
    r'D:\WeGameApps\英雄联盟',
    r'C:\Program Files\WeGame\Games\英雄联盟',
]
URL = 'http://127.0.0.1:27888/'
KEY = 'custom_landing_url'


def find_yaml():
    for root in INSTALLS:
        for rel in (r'LeagueClient\system.yaml', r'LeagueClient\Config\system.yaml'):
            p = os.path.join(root, rel)
            if os.path.isfile(p):
                return p
    return None


def show(path):
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            if KEY in line:
                print('当前:', line.strip())
                return
    print('当前: 未设置')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--apply', action='store_true')
    ap.add_argument('--restore', action='store_true')
    a = ap.parse_args()

    path = find_yaml()
    if not path:
        print('找不到 system.yaml')
        return 2
    print('system.yaml:', path)
    show(path)

    if a.restore:
        bak = path + '.ggman.bak'
        if not os.path.isfile(bak):
            print('没有备份可还原')
            return 2
        shutil.copy2(bak, path)
        print('已从备份还原（重启客户端生效）')
        show(path)
        return 0

    if a.apply:
        bak = path + '.ggman.bak'
        if not os.path.isfile(bak):
            shutil.copy2(path, bak)
            print('已备份 ->', bak)
        with open(path, encoding='utf-8', errors='replace') as f:
            text = f.read()
        if re.search(r'^\s*' + KEY + r'\s*:', text, re.M):
            text = re.sub(r'^\s*' + KEY + r'\s*:.*$', f'{KEY}: "{URL}"', text, count=1, flags=re.M)
        else:
            text = text.rstrip() + f'\n{KEY}: "{URL}"\n'
        with open(path, 'w', encoding='utf-8') as f:
            f.write(text)
        print('已写入', f'{KEY}: "{URL}"（重启客户端生效）')
        show(path)
        return 0

    print(f'\n要启用：python tools\\set_landing.py --apply   （目标 {URL}）')
    print('前提：agent 已注入且 http_port 打开（agent.ini 里 http_port=27888）')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
