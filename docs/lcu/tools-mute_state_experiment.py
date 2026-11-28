"""静音状态判别实验：muted-players 到底反映真实状态，还是永远返回空？

流程（同一选人会话内）：
  1. 排队 430 → 接受 → 进 ChampSelect
  2. GET muted-players            (baseline)
  3. POST toggle-player-muted 对一名队友（四元组 body，与客户端组件一致）
  4. GET muted-players            (after mute)
  5. POST toggle-player-muted 同一人（取消）
  6. GET muted-players            (after unmute)
  7. 全部记录并脱敏（puuid 保留前 6 位）

输出：lcu-dump/mute-experiment/report-<时间戳>.json + 控制台可读摘要
用法：python tools/mute_state_experiment.py [--max 10]
"""
import base64
import json
import os
import re
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

DUMP = r'D:\project2\ggman\lcu-dump\mute-experiment'
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
        data = body.encode() if isinstance(body, str) else json.dumps(body).encode()
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


def redact(text):
    """脱敏：puuid 只留前 6 位，summonerId 保留（非个人标识）。"""
    if not text:
        return text
    def sub_puuid(m):
        v = m.group(1)
        return '"puuid":"' + (v[:6] + '…' if len(v) > 8 else v) + '"'
    return re.sub(r'"puuid":"([0-9a-fA-F\-]{8,})"', sub_puuid, text)


def enter_champ_select(port, tok, tries=10):
    for attempt in range(1, tries + 1):
        ph = phase(port, tok)
        print(f'  [enter] attempt {attempt}: phase={ph}')
        if ph == 'ChampSelect':
            return True
        call('POST', '/lol-lobby/v2/lobby', {'queueId': QUEUE}, port=port, tok=tok)
        time.sleep(0.8)
        st, _ = call('POST', '/lol-lobby/v2/lobby/matchmaking/search', port=port, tok=tok)
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
                return True
            time.sleep(0.4)
        time.sleep(3)
    return False


def main():
    tries = 10
    for i, a in enumerate(sys.argv):
        if a == '--max' and i + 1 < len(sys.argv):
            tries = int(sys.argv[i + 1])
    os.makedirs(DUMP, exist_ok=True)
    tag = datetime.now().strftime('%Y%m%d-%H%M%S')
    report = {'tag': tag, 'steps': [], 'verdict': None}

    port, tok = sess()
    if not port:
        raise SystemExit('[error] 找不到 LCU 会话')
    print(f'[init] port={port} phase={phase(port, tok)}')

    if not enter_champ_select(port, tok, tries):
        report['verdict'] = 'never entered champ select'
        json.dump(report, open(os.path.join(DUMP, f'report-{tag}.json'), 'w', encoding='utf-8'),
                  ensure_ascii=False, indent=1)
        print('[abort] 未进入选人')
        return 1

    def snap(label):
        st, body = call('GET', '/lol-champ-select/v1/muted-players', port=port, tok=tok)
        rec = {'label': label, 'status': st, 'body': redact(body)[:600], 'at': datetime.now().isoformat(timespec='seconds')}
        report['steps'].append(rec)
        print(f'  [{label}] {st}  {rec["body"][:160]}')
        return rec

    # 会话基本信息
    st, raw = call('GET', '/lol-lobby-team-builder/champ-select/v1/session', port=port, tok=tok)
    doc = {}
    try:
        doc = json.loads(raw)
    except Exception:
        pass
    team = doc.get('myTeam') or []
    target = None
    for m in team:
        if m.get('cellId') == doc.get('localPlayerCellId'):
            continue
        target = m
        break
    report['session'] = {
        'phase': phase(port, tok),
        'showQuitButton': doc.get('showQuitButton'),
        'queueId': doc.get('queueId'),
        'localPlayerCellId': doc.get('localPlayerCellId'),
        'teamSize': len(team),
        'targetRedacted': None if not target else {
            'cellId': target.get('cellId'),
            'summonerId': target.get('summonerId'),
            'puuid': (target.get('puuid') or '')[:6] + '…',
            'hasObfuscated': bool(target.get('obfuscatedPuuid'))
        }
    }
    print(f'[session] showQuitButton={doc.get("showQuitButton")} queueId={doc.get("queueId")} '
          f'team={len(team)}')

    if not target:
        report['verdict'] = 'no teammate to mute (single-player session)'
        json.dump(report, open(os.path.join(DUMP, f'report-{tag}.json'), 'w', encoding='utf-8'),
                  ensure_ascii=False, indent=1)
        print('[abort] 没有可操作的队友')
        return 1

    body = json.dumps({
        'summonerId': target.get('summonerId'),
        'puuid': target.get('puuid'),
        'obfuscatedSummonerId': target.get('obfuscatedSummonerId'),
        'obfuscatedPuuid': target.get('obfuscatedPuuid')
    })
    report['toggleBodyRedacted'] = redact(body)

    snap('baseline')
    st1, b1 = call('POST', '/lol-champ-select/v1/toggle-player-muted', body, port=port, tok=tok)
    report['steps'].append({'label': 'toggle#1 (mute)', 'status': st1, 'body': redact(b1)[:300]})
    print(f'  [toggle#1] POST -> {st1}')
    time.sleep(1.2)
    snap('after-mute')

    st2, b2 = call('POST', '/lol-champ-select/v1/toggle-player-muted', body, port=port, tok=tok)
    report['steps'].append({'label': 'toggle#2 (unmute)', 'status': st2, 'body': redact(b2)[:300]})
    print(f'  [toggle#2] POST -> {st2}')
    time.sleep(1.2)
    final = snap('after-unmute')

    states = [s['body'].strip() for s in report['steps'] if s['label'] in
              ('baseline', 'after-mute', 'after-unmute')]
    if states[0] == states[1] and states[1] == states[2]:
        report['verdict'] = 'muted-players did not change across mute/unmute -> unreliable for state'
    elif states[0] != states[1] and states[1] != states[2]:
        report['verdict'] = 'muted-players tracked mute and unmute -> usable for explicit idempotent mute'
    else:
        report['verdict'] = 'partial: ' + json.dumps(states, ensure_ascii=False)

    path = os.path.join(DUMP, f'report-{tag}.json')
    json.dump(report, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f'\n[verdict] {report["verdict"]}')
    print(f'[out] {path}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
