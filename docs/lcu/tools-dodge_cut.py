"""GGman 秒退（断连版）—— 不关客户端，只切断 LeagueClient 出站连接。

2026-11-28 实测依据（见 LCU-退出选人-专题.md）：
  · showQuitButton = false（服务端下发）
  · POST /lol-lobby-team-builder/champ-select/v1/session/quit -> 400 "does not allow quitting"
  · POST /lol-gameflow/v1/session/dodge -> 204 但 phase 不变
  · DELETE /lol-lobby/v2/lobby -> 400 INVALID_PARTY_STATE
  · POST /lol-gameflow/v1/early-exit -> 403 "Not in InProgress or Reconnect phase"
  · 唯一让 phase 变化的手段：断连（服务端判 dodge）或超时未选

用法：
  python tools\\dodge_cut.py --cut 4             # 立刻断 4 秒（当前在大厅也可安全试）
  python tools\\dodge_cut.py --watch --cut 4     # 等进入 ChampSelect 再断（推荐）
  python tools\\dodge_cut.py --watch --cut 6     # 4 秒不够时加长

原理：Windows 防火墙按进程名加 Outbound Block 规则 → 等 N 秒 → 删规则。
      客户端进程全程存活（不关大厅），断连期间服务端判 dodge，恢复后客户端自动回大厅。
"""
import base64
import json
import os
import subprocess
import ssl
import sys
import time
import urllib.error
import urllib.request

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

RULE = 'GGMAN-DODGE-CUT'
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE


def ps(cmd, timeout=25):
    """通过 EncodedCommand 执行 PowerShell，彻底规避中文路径的编码问题。"""
    enc = base64.b64encode(cmd.encode('utf-16-le')).decode()
    r = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
                        '-EncodedCommand', enc], capture_output=True, timeout=timeout)
    for enc_name in ('utf-8', 'gbk', 'mbcs'):
        try:
            return r.stdout.decode(enc_name).strip()
        except Exception:
            continue
    return r.stdout.decode('utf-8', 'replace').strip()


def league_exes():
    """LeagueClient.exe 的 Path/ExecutablePath 被保护（返回空），
    改从 LeagueClientUx 的同目录推导，再补 RiotClientServices。"""
    out = []
    ux = ps("Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUx.exe'\" "
            "| Select-Object -First 1 -ExpandProperty ExecutablePath")
    if ux and os.path.exists(ux):
        d = os.path.dirname(ux)
        for name in ('LeagueClient.exe', 'LeagueClientUx.exe', 'LeagueClientUxRender.exe'):
            p = os.path.join(d, name)
            if os.path.exists(p):
                out.append(p)
    rc = ps("(Get-Process -Name RiotClientServices -ErrorAction SilentlyContinue "
            "| Select-Object -First 1).Path")
    if rc and os.path.exists(rc) and rc not in out:
        out.append(rc)
    return out


def session():
    cl = ps("Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUx.exe'\" "
            "| Select-Object -First 1 -ExpandProperty CommandLine")
    port = token = None
    for part in (cl or '').split():
        s = part.strip('"')
        if s.startswith('--app-port='):
            port = s.split('=', 1)[1]
        elif s.startswith('--remoting-auth-token='):
            token = s.split('=', 1)[1]
    return (int(port) if port else None), token


def phase(port, token):
    if not (port and token):
        return 'no-session'
    req = urllib.request.Request(f'https://127.0.0.1:{port}/lol-gameflow/v1/gameflow-phase')
    req.add_header('Authorization', 'Basic ' + base64.b64encode(f'riot:{token}'.encode()).decode())
    try:
        with urllib.request.urlopen(req, timeout=5, context=CTX) as r:
            return json.loads(r.read().decode())
    except Exception as e:
        return f'err:{type(e).__name__}'


def rule_exists():
    return 'True' in ps(f"[bool](Get-NetFirewallRule -DisplayName '{RULE}' -ErrorAction SilentlyContinue)")


def add_rule(exe):
    ps(f"New-NetFirewallRule -DisplayName '{RULE}' -Direction Outbound -Program '{exe}' "
       f"-Action Block -Profile Any -Protocol Any -ErrorAction Stop | Out-Null")
    return rule_exists()


def del_rule():
    ps(f"Get-NetFirewallRule -DisplayName '{RULE}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule")


def main():
    args = sys.argv[1:]
    watch = '--watch' in args
    secs = 4
    for i, a in enumerate(args):
        if a == '--cut' and i + 1 < len(args):
            secs = float(args[i + 1])

    port, token = session()
    if not (port and token):
        raise SystemExit('[error] 找不到 LeagueClientUx 的 --app-port/--remoting-auth-token')
    print(f'[session] port={port}')

    cur = phase(port, token)
    print(f'[before] phase={cur}')

    if watch:
        print('[watch] 等待 ChampSelect（最多 900s，每 1s 轮询）...')
        deadline = time.time() + 900
        while time.time() < deadline:
            cur = phase(port, token)
            if cur == 'ChampSelect':
                break
            time.sleep(1)
        print(f'[watch] phase={cur}')
        if cur != 'ChampSelect':
            print('[watch] 超时未进入选人，退出')
            return 1

    exes = league_exes()
    if not exes:
        raise SystemExit('[error] 找不到 LeagueClient.exe / RiotClientServices.exe')
    print('[target] ' + ' ; '.join(exes))

    del_rule()                       # 清残留
    cut_ok = []
    try:
        for e in exes:
            if os.path.exists(e) and add_rule(e):
                cut_ok.append(e)
                print(f'[cut] 已阻断 {e}')
        if not cut_ok:
            print('[cut] 规则未生效，检查管理员权限')
            return 2
        print(f'[cut] 保持 {secs}s ...')
        time.sleep(secs)
    finally:
        del_rule()
        print('[restore] 规则已移除')

    deadline = time.time() + 25
    last = None
    while time.time() < deadline:
        now = phase(port, token)
        if now != last:
            print(f'[after] phase={now}')
            last = now
        if now in ('Lobby', 'None', 'Matchmaking', 'EndOfGame'):
            break
        time.sleep(0.8)

    print()
    print(f'[result] {cur} -> {last}')
    if last in ('Lobby', 'None', 'Matchmaking'):
        print('[result] 已回大厅（服务端判 dodge），客户端未关闭')
        return 0
    print('[result] 仍在选人；加大 --cut（6~8）再试')
    return 3


if __name__ == '__main__':
    sys.exit(main())
