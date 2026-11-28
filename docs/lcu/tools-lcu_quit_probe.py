"""LCU 退出选人 / 返回大厅 探针 —— 专治「PvP 选人没有退出按钮」。

背景（2026-11-28 实测，端口 62420）：
  · POST /lol-champ-select/v1/session/quit                 -> 404 Invalid URI format   ← 路由根本不存在
  · POST /lol-lobby-team-builder/champ-select/v1/session/quit -> 404 No session         ← 路由存在
  · POST /lol-gameflow/v1/session/dodge   (带任意 JSON body) -> 204                      ← 路由存在
  · WAMP 只支持 SUBSCRIBE，不支持 CALL（四种 procedure 格式全超时）

用法：
  python tools\\lcu_quit_probe.py --watch                # 只盯 phase，不写，先看 PvP 选人时 early-exit 开关
  python tools\\lcu_quit_probe.py --try quit             # 进选人后打 team-builder 的 quit
  python tools\\lcu_quit_probe.py --try dodge            # 进选人后打 gameflow dodge
  python tools\\lcu_quit_probe.py --try all --delay 3    # 依次试两条（会真的退出，惩罚由服务端记）

建议顺序：
  1) 先 --watch 进一局训练/自定义，确认脚本能抓到 ChampSelect 与事件序列（零成本）
  2) 再用 --try dodge 在「普通匹配」里试（惩罚是时间锁，不扣 LP）
  3) 排位最后再试，且自己承担扣分
日志：lcu-dump/09-quit-probe/quits-<时间戳>.jsonl，每条含 phase 前后、状态码、响应体、事件序列。
"""
import base64
import json
import os
import socket
import ssl
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

DUMP = r'D:\project2\ggman\lcu-dump'
OUTDIR = os.path.join(DUMP, '09-quit-probe')
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

# 2026-11-28 实测存在的路由（顺序即尝试顺序）
ROUTES = {
    'quit':  ('POST', '/lol-lobby-team-builder/champ-select/v1/session/quit', None),
    'dodge': ('POST', '/lol-gameflow/v1/session/dodge', {}),
    'quit_legacy': ('POST', '/lol-champ-select/v1/session/quit', None),
}


def discover():
    """环境变量优先，其次读 LeagueClientUx.exe 命令行。"""
    port, token = os.environ.get('LCU_PORT'), os.environ.get('LCU_TOKEN')
    if port and token:
        return int(port), token, 'env'
    cmd = ("Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'LeagueClientUx.exe' } "
           "| Select-Object -First 1 -ExpandProperty CommandLine")
    try:
        cl = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', cmd],
                            capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=25).stdout or ''
    except Exception:
        cl = ''
    for part in cl.split():
        s = part.strip('"')
        if s.startswith('--app-port='):
            port = s.split('=', 1)[1]
        elif s.startswith('--remoting-auth-token='):
            token = s.split('=', 1)[1]
    if not (port and token):
        raise SystemExit('拿不到 LCU 会话：先打开客户端，或用 LCU_PORT/LCU_TOKEN 显式指定')
    return int(port), token, 'Ux-cmdline'


class Ws:
    """最小 WAMP 客户端：只订阅，不发 CALL（LCU 不支持 CALL，实测四种格式全超时）。"""

    def __init__(self, port, token):
        self.sock = CTX.wrap_socket(socket.create_connection(('127.0.0.1', port), timeout=15),
                                    server_hostname='127.0.0.1')
        key = base64.b64encode(os.urandom(16)).decode()
        auth = 'Basic ' + base64.b64encode(f'riot:{token}'.encode()).decode()
        self.sock.sendall((
            f'GET / HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n'
            f'Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n'
            f'Sec-WebSocket-Protocol: wamp\r\nAuthorization: {auth}\r\n\r\n').encode())
        head = b''
        while b'\r\n\r\n' not in head:
            chunk = self.sock.recv(4096)
            if not chunk:
                raise SystemExit('WAMP 握手失败')
            head += chunk
        self.buf = head.split(b'\r\n\r\n', 1)[1]
        self.frag, self.frag_op = b'', 0
        self.sock.settimeout(1.0)
        self.subscribe('[5,"OnJsonApiEvent"]')

    def send(self, text):
        d = text.encode()
        m = os.urandom(4)
        h = bytearray([0x81])
        n = len(d)
        if n < 126:
            h.append(0x80 | n)
        elif n < 65536:
            h.append(0x80 | 126)
            h += struct.pack('>H', n)
        else:
            h.append(0x80 | 127)
            h += struct.pack('>Q', n)
        h += m
        self.sock.sendall(bytes(h) + bytes(d[i] ^ m[i % 4] for i in range(n)))

    def subscribe(self, frame):
        self.send(frame)

    def _need(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError('closed')
            self.buf += chunk

    def poll(self, timeout=1.0):
        """返回一批事件 dict，超时返回 []。"""
        self.sock.settimeout(timeout)
        out = []
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                self._need(2)
            except (socket.timeout, ConnectionError):
                break
            b0, b1 = self.buf[0], self.buf[1]
            op, fin, masked, ln, need = b0 & 0x0F, b0 >> 7, b1 >> 7, b1 & 0x7F, 2
            if ln == 126:
                self._need(4); ln = struct.unpack('>H', self.buf[2:4])[0]; need = 4
            elif ln == 127:
                self._need(10); ln = struct.unpack('>Q', self.buf[2:10])[0]; need = 10
            if masked:
                self._need(need + 4); need += 4
            self._need(need + ln)
            payload, self.buf = self.buf[need:need + ln], self.buf[need + ln:]
            if op == 0x8:
                raise ConnectionError('server closed')
            if op not in (0x1, 0x2, 0x0):
                continue
            if op:
                self.frag_op, self.frag = op, payload
            else:
                self.frag += payload
            if not fin:
                continue
            raw, self.frag = self.frag, b''
            try:
                msg = json.loads(raw.decode('utf-8', 'replace'))
            except Exception:
                continue
            if isinstance(msg, list) and len(msg) >= 3 and msg[1] == 'OnJsonApiEvent':
                out.append(msg[2] or {})
        return out


def call(port, token, method, path, body=None, timeout=8):
    req = urllib.request.Request(f'https://127.0.0.1:{port}{path}', method=method)
    req.add_header('Authorization', 'Basic ' + base64.b64encode(f'riot:{token}'.encode()).decode())
    data = None
    if body is not None:
        req.add_header('Content-Type', 'application/json')
        data = json.dumps(body).encode()
    elif method in ('POST', 'PUT', 'PATCH'):
        req.add_header('Content-Type', 'application/json')
        data = b'{}'          # 空 body 会被 LCU 判成 "dodgeData required"，给 {} 即可
    try:
        with urllib.request.urlopen(req, data=data, timeout=timeout, context=CTX) as r:
            return r.status, r.read()[:400].decode('utf-8', 'replace')
    except urllib.error.HTTPError as e:
        return e.code, e.read()[:400].decode('utf-8', 'replace')
    except Exception as e:
        return None, repr(e)


def phase(port, token):
    st, body = call(port, token, 'GET', '/lol-gameflow/v1/gameflow-phase')
    if st == 200:
        try:
            return json.loads(body)
        except Exception:
            return body
    return f'err-{st}'


def early_exit_state(port, token):
    out = {}
    for name, path in (('early_exit', '/lol-gameflow/v1/early-exit-enabled'),
                       ('early_exit_quit', '/lol-gameflow/v1/early-exit-quit-enabled'),
                       ('vanguard_exit', '/lol-gameflow/v1/game-exit-early-vanguard'),
                       ('tb_active', '/lol-lobby-team-builder/champ-select/v1/implementation-active'),
                       ('leaver', '/lol-leaver-buster/v1/ranked-restriction')):
        st, body = call(port, token, 'GET', path)
        out[name] = body if st == 200 else f'{st}'
    return out


def main():
    args = sys.argv[1:]
    mode = 'watch'
    tries = ['quit', 'dodge']
    delay = 3.0
    for i, a in enumerate(args):
        if a == '--watch':
            mode = 'watch'
        if a == '--try' and i + 1 < len(args):
            mode = 'try'
            v = args[i + 1]
            tries = ['quit', 'dodge', 'quit_legacy'] if v == 'all' else [v]
        if a == '--delay' and i + 1 < len(args):
            delay = float(args[i + 1])

    port, token, src = discover()
    os.makedirs(OUTDIR, exist_ok=True)
    tag = datetime.now().strftime('%Y%m%d-%H%M%S')
    logpath = os.path.join(OUTDIR, f'quits-{tag}.jsonl')
    log = open(logpath, 'a', encoding='utf-8')

    def rec(**kw):
        kw['t'] = round(time.time(), 3)
        log.write(json.dumps(kw, ensure_ascii=False) + '\n')
        log.flush()
        print(' ', json.dumps(kw, ensure_ascii=False)[:300])

    print(f'[init] port={port} src={src} mode={mode} tries={tries if mode == "try" else "-"}')
    print('[init] early-exit 状态:', json.dumps(early_exit_state(port, token), ensure_ascii=False))
    ws = Ws(port, token)
    print('[init] WAMP 已订阅 OnJsonApiEvent')

    cur = phase(port, token)
    rec(kind='phase', value=cur)
    seen_champ_select = False
    fired = []
    deadline = time.time() + 3600 * 3

    while time.time() < deadline:
        events = ws.poll(1.0)
        for ev in events:
            uri = ev.get('uri')
            if uri and 'gameflow-phase' in uri:
                rec(kind='event', uri=uri, data=ev.get('data'))
        now = phase(port, token)
        if now != cur:
            rec(kind='phase-change', frm=cur, to=now, early_exit=early_exit_state(port, token))
            cur = now

        if now == 'ChampSelect':
            if not seen_champ_select:
                seen_champ_select = True
                rec(kind='champ-select-enter', early_exit=early_exit_state(port, token),
                    tb_session=call(port, token, 'GET', '/lol-lobby-team-builder/champ-select/v1/session')[0],
                    tb_active=call(port, token, 'GET', '/lol-lobby-team-builder/champ-select/v1/implementation-active')[1][:80])
            if mode == 'try':
                for name in tries:
                    if name in fired:
                        continue
                    method, path, body = ROUTES[name]
                    st, resp = call(port, token, method, path, body)
                    after = phase(port, token)
                    rec(kind='attempt', route=name, method=method, path=path,
                        sent_body=body, status=st, response=resp[:300], phase_after=after)
                    fired.append(name)
                    time.sleep(delay)
                    if phase(port, token) != 'ChampSelect':
                        rec(kind='result', verdict='left champ select', route=name)
                        break
        if now in ('None', 'Lobby', 'EndOfGame') and seen_champ_select and fired:
            rec(kind='done', phase=now, fired=fired)
            break

    log.close()
    print(f'\n[log] {logpath}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
