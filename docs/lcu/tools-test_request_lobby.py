"""验证 POST /lol-gameflow/v1/session/request-lobby 能否让 PvP 选人回到大厅。

背景：FACM 3.5.90 的 LeagueChampSelectQuitRuntime 把它作为 quit 的回退路径
      （RequestLobbyPath = "/lol-gameflow/v1/session/request-lobby"），
      但没人验证过它在 PvP 选人（showQuitButton=false）时是否真的生效。
      这条如果成立，就是"不杀进程、不断网"的干净退出通道。

流程：排队 430 → 接受 → 进选人 → POST request-lobby（只打这一条）→ 观察 phase。
用法：python tools\\test_request_lobby.py [--max 12]
日志：lcu-dump/13-request-lobby/rl-<时间戳>.jsonl
"""
import base64
import json
import os
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

DUMP = r'D:\project2\ggman\lcu-dump\13-request-lobby'
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE
QUEUE = 430


def ps(cmd, timeout=30):
    enc = base64.b64encode(cmd.encode('utf-16-le')).decode()
    r = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
                        '-EncodedCommand', enc], capture_output=True, timeout=timeout)
    for name in ('utf-8', 'gbk', 'mbcs'):
        try:
            return r.stdout.decode(name).strip()
        except Exception:
            continue
    return r.stdout.decode('utf-8', 'replace').strip()


def sess():
    cl = ps("Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUx.exe'\" "
            "| Select-Object -First 1 -ExpandProperty CommandLine")
    port = tok = None
    for p in (cl or '').split():
        s = p.strip('"')
        if s.startswith('--app-port='):
            port = int(s.split('=', 1)[1])
        elif s.startswith('--remoting-auth-token='):
            tok = s.split('=', 1)[1]
    return port, tok


def call(method, path, body=None, port=None, tok=None, timeout=10):
    if not port:
        return None, 'no-session'
    r = urllib.request.Request(f'https://127.0.0.1:{port}{path}', method=method)
    r.add_header('Authorization', 'Basic ' + base64.b64encode(f'riot:{tok}'.encode()).decode())
    data = None
    if body is not None:
        r.add_header('Content-Type', 'application/json')
        data = json.dumps(body).encode()
    elif method in ('POST', 'PUT', 'PATCH'):
        r.add_header('Content-Type', 'application/json')
        data = b'{}'
    try:
        with urllib.request.urlopen(r, data=data, timeout=timeout, context=CTX) as x:
            return x.status, x.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'replace')
    except Exception as e:
        return None, repr(e)


def phase(port, tok):
    return (call('GET', '/lol-gameflow/v1/gameflow-phase', port=port, tok=tok)[1] or '').strip('"')


def main():
    tries = 12
    for i, a in enumerate(sys.argv):
        if a == '--max' and i + 1 < len(sys.argv):
            tries = int(sys.argv[i + 1])
    os.makedirs(DUMP, exist_ok=True)
    tag = datetime.now().strftime('%Y%m%d-%H%M%S')
    logf = os.path.join(DUMP, f'rl-{tag}.jsonl')
    log = open(logf, 'a', encoding='utf-8')

    def rec(**kw):
        kw['ts'] = datetime.now().strftime('%H:%M:%S')
        log.write(json.dumps(kw, ensure_ascii=False) + '\n')
        log.flush()
        print(json.dumps(kw, ensure_ascii=False)[:300])

    # 排队直到进选人
    for attempt in range(1, tries + 1):
        port, tok = sess()
        if not port:
            time.sleep(4)
            continue
        ph = phase(port, tok)
        rec(kind='attempt', n=attempt, phase=ph)
        if ph == 'ChampSelect':
            break
        call('POST', '/lol-lobby/v2/lobby', {'queueId': QUEUE}, port=port, tok=tok)
        time.sleep(0.8)
        st, b = call('POST', '/lol-lobby/v2/lobby/matchmaking/search', port=port, tok=tok)
        rec(kind='search', status=st)
        if st not in (200, 204):
            time.sleep(3)
            continue
        deadline = time.time() + 240
        accepted = False
        while time.time() < deadline:
            st, rb = call('GET', '/lol-matchmaking/v1/ready-check', port=port, tok=tok)
            if st == 200:
                try:
                    rc = json.loads(rb)
                except Exception:
                    rc = {}
                if rc.get('state') in ('InProgress', 'AwaitingAccept', 'Ready'):
                    call('POST', '/lol-matchmaking/v1/ready-check/accept', port=port, tok=tok)
                    rec(kind='accept')
                    accepted = True
                    break
            if phase(port, tok) == 'ChampSelect':
                accepted = True
                break
            time.sleep(0.5)
        if not accepted:
            call('DELETE', '/lol-lobby/v2/lobby/matchmaking/search', port=port, tok=tok)
            time.sleep(2)
            continue
        deadline = time.time() + 60
        while time.time() < deadline:
            if phase(port, tok) == 'ChampSelect':
                break
            time.sleep(0.4)
        if phase(port, tok) == 'ChampSelect':
            break
        rec(kind='ready-check-dissolved', n=attempt)
        time.sleep(3)

    port, tok = sess()
    ph = phase(port, tok)
    rec(kind='entered', phase=ph)
    if ph != 'ChampSelect':
        rec(kind='abort', why='never entered champ select')
        return 1

    # 记录 session 关键字段
    def get_full(path):
        r = urllib.request.Request(f'https://127.0.0.1:{port}{path}')
        r.add_header('Authorization', 'Basic ' + base64.b64encode(f'riot:{tok}'.encode()).decode())
        try:
            with urllib.request.urlopen(r, timeout=8, context=CTX) as x:
                return x.read().decode('utf-8', 'replace')
        except Exception:
            return ''
    try:
        doc = json.loads(get_full('/lol-lobby-team-builder/champ-select/v1/session'))
    except Exception:
        doc = {}
    rec(kind='champ-select', showQuitButton=doc.get('showQuitButton'),
        isCustomGame=doc.get('isCustomGame'), queueId=doc.get('queueId'), gameId=doc.get('gameId'))

    # 只打这一条
    t0 = time.time()
    st, b = call('POST', '/lol-gameflow/v1/session/request-lobby', port=port, tok=tok)
    rec(kind='request-lobby', status=st, body=b[:200], after_ms=int((time.time() - t0) * 1000))

    for i in range(20):
        time.sleep(1.0)
        p2, t2 = sess()
        ph2 = phase(p2, t2) if p2 else 'no-session'
        rec(kind='watch', t=i + 1, phase=ph2)
        if ph2 in ('Lobby', 'None', 'Matchmaking', 'EndOfGame'):
            rec(kind='result', verdict='LEFT CHAMP SELECT', phase=ph2)
            break
    p3, t3 = sess()
    if p3:
        rec(kind='leaver', body=call('GET', '/lol-leaver-buster/v1/notifications', port=p3, tok=t3)[1][:200])
        rec(kind='lowprio', body=call('GET', '/lol-matchmaking/v1/search', port=p3, tok=t3)[1][:260])
    print(f'\n[log] {logf}')
    log.close()
    return 0


if __name__ == '__main__':
    sys.exit(main())
