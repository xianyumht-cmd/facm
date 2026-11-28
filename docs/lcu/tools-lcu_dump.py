"""LCU 全量链路采集器 —— 把大厅（LeagueClientUx）的底层实现链路扒干净。

只读采集：本脚本不会向 LCU 发送任何写操作（POST/PUT/PATCH/DELETE），
所有可能改变账号状态的端点只登记进待测清单，由人工在 mock / 自定义对局里验证。

用法（客户端已启动并停在大厅）：
    python tools\\lcu_dump.py                 # 全量：会话+端点表+插件索引+日志+grep
    python tools\\lcu_dump.py --phase session # 只取端口/token/进程树/证书
    python tools\\lcu_dump.py --phase api     # 只拉 swagger / 端点表
    python tools\\lcu_dump.py --phase plugins # 只索引 LeagueClient\\plugins 前端代码
    python tools\\lcu_dump.py --phase logs    # 只处理 Logs（含 HTTP 请求行）
    python tools\\lcu_dump.py --phase watch --seconds 600   # WAMP 全事件录制
    python tools\\lcu_dump.py --phase hook  --seconds 300   # Frida hook 前端 fetch/XHR（需 frida）

也可以显式给会话（客户端已在跑但自动发现失败时）：
    set LCU_PORT=62813 & set LCU_TOKEN=xxxx & python tools\\lcu_dump.py --phase api

产物全部落在 lcu-dump\\ 下，01~08 见 README（首次运行自动生成）。
"""
import base64
import ctypes
import ctypes.wintypes as wt
import json
import os
import re
import shutil
import socket
import ssl
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime

DUMP = r'D:\project2\ggman\lcu-dump'
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

SWAGGER_CANDIDATES = [
    '/swagger/v3/openapi.json',
    '/swagger/v2/swagger.json',
    '/swagger.json',
    '/openapi.json',
    '/docs/openapi.json',
    '/help',
]

READONLY_PROBES = [
    '/lol-summoner/v1/current-summoner',
    '/lol-gameflow/v1/gameflow-phase',
    '/lol-gameflow/v1/session',
    '/lol-champ-select/v1/session',
    '/lol-lobby/v2/lobby',
    '/lol-matchmaking/v1/ready-check',
    '/riotclient/region-locale',
    '/riotclient/auth-token',
    '/lol-patch/v1/products/league-of-legends/state',
    '/lol-system/v1/builds',
]

INTEREST = ('champ-select', 'gameflow', 'lobby', 'matchmaking', 'ready-check', 'parties', 'end-of-game',
            'reconnect', 'spectator', 'game-data', 'login', 'summoner', 'perks', 'champ')

ENDPOINT_RE = re.compile(r'"(/(?:lol|riotclient|data-store|lol-settings|patcher|process-control|system)[A-Za-z0-9_\-/{}.$:]*)')


# ---------------------------------------------------------------- 会话发现
def discover():
    """优先环境变量，其次 CIM 读 LeagueClientUx.exe 命令行，最后 lockfile。"""
    port = os.environ.get('LCU_PORT')
    token = os.environ.get('LCU_TOKEN')
    src = 'env'
    procs = []
    if not (port and token):
        cmd = ("Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'LeagueClient|RiotClient|ACE-' } "
               "| Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Depth 3")
        try:
            out = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', cmd],
                                 capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=30).stdout
            procs = json.loads(out) if out.strip().startswith(('[', '{')) else []
        except Exception as exc:
            print(f'[session] CIM 查询失败: {exc}')
            procs = []
        if isinstance(procs, dict):
            procs = [procs]
        for p in procs:
            name = (p.get('Name') or '').lower()
            if name != 'leagueclientux.exe':
                continue
            cl = p.get('CommandLine') or ''
            for part in cl.split():
                s = part.strip('"')
                if s.startswith('--app-port='):
                    port = s.split('=', 1)[1]
                elif s.startswith('--remoting-auth-token='):
                    token = s.split('=', 1)[1]
            if port and token:
                src = 'Ux-cmdline'
                break
        if not (port and token):
            for p in procs:
                exe = p.get('ExecutablePath') or ''
                if exe and os.path.basename(exe).lower() == 'leagueclientux.exe':
                    lock = os.path.join(os.path.dirname(exe), 'lockfile')
                    try:
                        raw = open(lock, 'r', encoding='utf-8', errors='replace').read().strip()
                        if raw.count(':') >= 4:
                            _n, _pid, p2, t2, _proto = raw.split(':', 4)
                            port, token, src = p2, t2, 'lockfile'
                    except Exception:
                        pass
    return {'port': int(port) if port else None, 'token': token, 'source': src if port else None, 'procs': procs}


def hdr(token):
    return 'Basic ' + base64.b64encode(f'riot:{token}'.encode()).decode()


def api(port, token, path, method='GET', timeout=6):
    url = f'https://127.0.0.1:{port}{path}'
    req = urllib.request.Request(url, method=method)
    req.add_header('Authorization', hdr(token))
    try:
        with urllib.request.urlopen(req, timeout=timeout, context=CTX) as r:
            return r.status, r.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()
    except Exception as e:
        return None, repr(e).encode()


def save(rel, data):
    path = os.path.join(DUMP, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    mode = 'wb' if isinstance(data, (bytes, bytearray)) else 'w'
    with open(path, mode, **({} if mode == 'wb' else {'encoding': 'utf-8'})) as f:
        f.write(data)
    return path


# ---------------------------------------------------------------- phase: session
def phase_session(sess):
    os.makedirs(DUMP, exist_ok=True)
    lines = [f'# LCU 会话快照  {datetime.now():%Y-%m-%d %H:%M:%S}', '']
    lines.append(f"- 发现来源: `{sess['source']}`")
    lines.append(f"- app-port: `{sess['port']}`")
    lines.append(f"- auth-token: `{sess['token']}`")
    lines.append(f"- 连接串: `https://riot:{sess['token']}@127.0.0.1:{sess['port']}/`")
    lines.append('')
    lines.append('## 进程树')
    lines.append('```')
    for p in sess['procs']:
        lines.append(f"{p.get('ProcessId'):>7}  ppid={p.get('ParentProcessId'):<7} {p.get('Name')}")
        exe = p.get('ExecutablePath')
        if exe:
            lines.append(f"{'':>7}  exe: {exe}")
    lines.append('```')
    if sess['port'] and sess['token']:
        lines.append('')
        lines.append('## 只读探针（不改状态）')
        lines.append('| 端点 | 状态 | 响应片段 |')
        lines.append('|---|---|---|')
        for path in READONLY_PROBES:
            st, body = api(sess['port'], sess['token'], path)
            frag = body[:200].decode('utf-8', 'replace').replace('|', '\\|').replace('\n', ' ')
            lines.append(f'| `{path}` | {st} | {frag} |')
    save('00-session.md', '\n'.join(lines))
    print(f'[session] port={sess["port"]} src={sess["source"]} -> 00-session.md')


# ---------------------------------------------------------------- phase: api
def phase_api(sess):
    if not (sess['port'] and sess['token']):
        print('[api] 没有可用会话，先跑 --phase session 或用 LCU_PORT/LCU_TOKEN 指定')
        return
    got = None
    for path in SWAGGER_CANDIDATES:
        st, body = api(sess['port'], sess['token'], path, timeout=8)
        print(f'[api] GET {path} -> {st} ({len(body)}B)')
        if st == 200 and body:
            got = (path, body)
            break
    if not got:
        print('[api] 没有拿到 swagger；LCU 日志里仍会有完整请求行，用 --phase logs')
        return
    path, body = got
    ext = '.html' if path == '/help' else '.json'
    save(f'01-swagger/{path.strip("/").replace("/", "_")}{ext}', body)
    doc = None
    try:
        doc = json.loads(body)
    except Exception:
        pass
    if not doc:
        print(f'[api] 已保存 {path}（非 JSON，按 HTML 存档）')
        return
    rows, groups = [], {}
    for p, item in (doc.get('paths') or {}).items():
        for method, op in item.items():
            if method.lower() not in ('get', 'post', 'put', 'patch', 'delete', 'head'):
                continue
            tags = (op.get('tags') or ['untagged'])[0]
            summ = (op.get('summary') or op.get('description') or '').strip().replace('\n', ' ')[:90]
            rows.append((tags, method.upper(), p, summ))
            groups.setdefault(tags, []).append((method.upper(), p, summ))
    rows.sort()
    out = [f'# LCU 端点全表（来源 `{path}`，共 {len(rows)} 条）', '',
           '> 由 swagger 自动生成。状态列留空表示尚未实测；写操作统一在 mock/自定义对局里验证。', '']
    for tag in sorted(groups):
        out.append(f'## {tag}（{len(groups[tag])}）')
        out.append('| Method | Path | 摘要 | 实测 |')
        out.append('|---|---|---|---|')
        for m, p, s in sorted(groups[tag]):
            out.append(f'| {m} | `{p}` | {s} | |')
        out.append('')
    save('02-endpoints.md', '\n'.join(out))
    save('01-swagger/openapi.raw.json', json.dumps(doc, ensure_ascii=False, indent=1))
    print(f'[api] 端点 {len(rows)} 条 / 分组 {len(groups)} 个 -> 02-endpoints.md')


# ---------------------------------------------------------------- phase: plugins
def find_client_dir(sess):
    for p in sess['procs']:
        exe = p.get('ExecutablePath') or ''
        if os.path.basename(exe).lower() == 'leagueclientux.exe':
            return os.path.dirname(exe)
    for guess in (r'D:\WeGameApps\英雄联盟\LeagueClient', r'C:\Riot Games\League of Legends\LeagueClient'):
        if os.path.isdir(guess):
            return guess
    return None


def phase_plugins(sess):
    root = find_client_dir(sess)
    if not root:
        print('[plugins] 找不到 LeagueClient 安装目录')
        return
    plugins = os.path.join(root, 'plugins')
    if not os.path.isdir(plugins):
        print(f'[plugins] 无 plugins 目录: {plugins}')
        return
    index, total_bytes, hits_total = [], 0, 0
    for name in sorted(os.listdir(plugins)):
        d = os.path.join(plugins, name)
        if not os.path.isdir(d):
            continue
        files = []
        for dp, _dn, fn in os.walk(d):
            for f in fn:
                files.append(os.path.join(dp, f))
        size = sum(os.path.getsize(f) for f in files if os.path.exists(f))
        total_bytes += size
        index.append(f'- `{name}`  {len(files)} 个文件 / {size/1024:.0f} KB')
        js = [f for f in files if f.lower().endswith(('.js', '.jsx', '.map'))]
        hits = []
        for f in js:
            try:
                txt = open(f, 'r', encoding='utf-8', errors='replace').read()
            except Exception:
                continue
            for m in ENDPOINT_RE.finditer(txt):
                line = txt.count('\n', 0, m.start()) + 1
                hits.append((m.group(1), os.path.relpath(f, plugins), line))
        if hits:
            hits_total += len(hits)
            seen, lines = set(), []
            for ep, rel, line in hits:
                key = (ep, rel)
                if key in seen:
                    continue
                seen.add(key)
                lines.append(f'  - `{ep}`  ← {rel}:{line}')
            index.append('  - 端点命中:')
            index.extend(lines[:400])
    save('04-plugin-index.md',
         '\n'.join([f'# 前端插件与端点调用点索引', '',
                    f'- 目录: `{plugins}`',
                    f'- 插件数: {sum(1 for n in os.listdir(plugins) if os.path.isdir(os.path.join(plugins, n)))}',
                    f'- 体积: {total_bytes/1024/1024:.1f} MB',
                    f'- 端点命中: {hits_total}', ''] + index))
    print(f'[plugins] {plugins} -> 04-plugin-index.md（端点命中 {hits_total}）')


# ---------------------------------------------------------------- phase: logs
def phase_logs(sess):
    root = find_client_dir(sess)
    if not root:
        print('[logs] 找不到 LeagueClient 目录')
        return
    logs_dir = os.path.join(root, 'Logs')
    if not os.path.isdir(logs_dir):
        print(f'[logs] 无 Logs 目录: {logs_dir}')
        return
    dst = os.path.join(DUMP, '03-logs')
    os.makedirs(dst, exist_ok=True)
    copied, endpoints = 0, set()
    for f in sorted(os.listdir(logs_dir)):
        if not f.lower().endswith('.log'):
            continue
        src = os.path.join(logs_dir, f)
        try:
            shutil.copy2(src, os.path.join(dst, f))
            copied += 1
            txt = open(src, 'r', encoding='utf-8', errors='replace').read()
            for m in re.finditer(r'(?:GET|POST|PUT|PATCH|DELETE)\s+(/(?:lol|riotclient|data-store|lol-settings)[A-Za-z0-9_\-/{}.$:]*)', txt):
                endpoints.add(m.group(1))
        except Exception:
            pass
    save('03-logs-endpoints.txt', '\n'.join(sorted(endpoints)))
    print(f'[logs] 复制 {copied} 个日志，抽出 {len(endpoints)} 个端点 -> 03-logs/')


# ---------------------------------------------------------------- phase: watch
def phase_watch(sess, seconds=300):
    if not (sess['port'] and sess['token']):
        print('[watch] 没有可用会话')
        return
    port, token = sess['port'], sess['token']
    raw = socket.create_connection(('127.0.0.1', port), timeout=15)
    sock = CTX.wrap_socket(raw, server_hostname='127.0.0.1')
    key = base64.b64encode(os.urandom(16)).decode()
    sock.sendall(('GET / HTTP/1.1\r\n'
                  f'Host: 127.0.0.1:{port}\r\n'
                  'Upgrade: websocket\r\nConnection: Upgrade\r\n'
                  f'Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n'
                  'Sec-WebSocket-Protocol: wamp\r\n'
                  f'Authorization: {hdr(token)}\r\n\r\n').encode())
    head = b''
    while b'\r\n\r\n' not in head:
        chunk = sock.recv(4096)
        if not chunk:
            print('[watch] 握手失败')
            return
        head += chunk
    print('[watch]', head.split(b'\r\n')[0].decode())

    def send_text(text):
        data = text.encode()
        mask = os.urandom(4)
        h = bytearray([0x81])
        n = len(data)
        if n < 126:
            h.append(0x80 | n)
        elif n < 65536:
            h.append(0x80 | 126); h += struct.pack('>H', n)
        else:
            h.append(0x80 | 127); h += struct.pack('>Q', n)
        h += mask
        sock.sendall(bytes(h) + bytes(data[i] ^ mask[i % 4] for i in range(n)))

    send_text('[5,"OnJsonApiEvent"]')
    buf, frag, frag_op = b'', b'', 0
    tag = datetime.now().strftime('%Y%m%d-%H%M%S')
    out = os.path.join(DUMP, '05-events', f'events-{tag}.jsonl')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    fh = open(out, 'a', encoding='utf-8')
    seen, deadline, count = {}, time.time() + seconds, 0
    sock.settimeout(3.0)
    print(f'[watch] 录制 {seconds}s -> {out}')
    while time.time() < deadline:
        try:
            while len(buf) < 2:
                buf += sock.recv(65536)
            b0, b1 = buf[0], buf[1]
            op, fin, masked, ln = b0 & 0x0F, b0 >> 7, b1 >> 7, b1 & 0x7F
            need = 2
            if ln == 126:
                while len(buf) < 4:
                    buf += sock.recv(65536)
                ln = struct.unpack('>H', buf[2:4])[0]; need = 4
            elif ln == 127:
                while len(buf) < 10:
                    buf += sock.recv(65536)
                ln = struct.unpack('>Q', buf[2:10])[0]; need = 10
            mask = b''
            if masked:
                while len(buf) < need + 4:
                    buf += sock.recv(65536)
                mask = buf[need:need + 4]; need += 4
            while len(buf) < need + ln:
                buf += sock.recv(65536)
            payload = buf[need:need + ln]
            buf = buf[need + ln:]
            if masked and mask:
                payload = bytes(payload[i] ^ mask[i % 4] for i in range(len(payload)))
            if op == 0x8:
                break
            if op in (0x1, 0x2, 0x0):
                if op:
                    frag_op, frag = op, payload
                else:
                    frag += payload
                if not fin:
                    continue
                msg_raw, frag = frag, b''
                try:
                    msg = json.loads(msg_raw.decode('utf-8', 'replace'))
                except Exception:
                    continue
                if isinstance(msg, list) and len(msg) >= 3 and msg[1] == 'OnJsonApiEvent':
                    d = msg[2] or {}
                    uri, et = d.get('uri'), d.get('eventType')
                    seen[uri] = seen.get(uri, 0) + 1
                    count += 1
                    fh.write(json.dumps({'t': round(time.time(), 3), 'eventType': et, 'uri': uri,
                                         'data': d.get('data')}, ensure_ascii=False)[:200000] + '\n')
                    fh.flush()
        except socket.timeout:
            continue
        except Exception as exc:
            print('[watch] 读取结束:', exc)
            break
    fh.close(); sock.close()
    lst = [f'# WAMP 事件覆盖  {tag}', '', f'- 事件数: {count}', f'- 不同 URI: {len(seen)}', '',
           '| URI | 次数 |', '|---|---|']
    lst += [f'| `{u}` | {c} |' for u, c in sorted(seen.items(), key=lambda x: -x[1])]
    save(f'05-events/uris-{tag}.md', '\n'.join(lst))
    print(f'[watch] {count} 个事件 / {len(seen)} 个 URI -> 05-events/')


# ---------------------------------------------------------------- phase: hook
FRIDA_JS = r'''
var log = [];
function rec(kind, url, method, body, res) {
    send({k: kind, u: String(url), m: method || 'GET', b: body ? String(body).slice(0,4000) : '', r: res ? String(res).slice(0,4000) : ''});
}
var of = window.fetch;
window.fetch = function(input, init) {
    var url = (typeof input === 'string') ? input : (input && input.url);
    var method = (init && init.method) || (input && input.method) || 'GET';
    var body = init && init.body;
    return of.apply(this, arguments).then(function(r) {
        try { r.clone().text().then(function(t){ rec('fetch', url, method, body, t); }); } catch(e) {}
        return r;
    });
};
var ox = XMLHttpRequest.prototype.open, os = XMLHttpRequest.prototype.send;
XMLHttpRequest.prototype.open = function(m, u) { this.__m = m; this.__u = u; return ox.apply(this, arguments); };
XMLHttpRequest.prototype.send = function(b) {
    var self = this;
    this.addEventListener('load', function(){ try { rec('xhr', self.__u, self.__m, b, self.responseText); } catch(e){} });
    return os.apply(this, arguments);
};
'''

def phase_hook(sess, seconds=300):
    try:
        import frida
    except Exception:
        print('[hook] 本机没有 frida python 包；可在 D:\\project2\\test\\venv_f16 里装好后重跑，或跳过本阶段')
        return
    tag = datetime.now().strftime('%Y%m%d-%H%M%S')
    out = os.path.join(DUMP, '06-hooks', f'calls-{tag}.jsonl')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    fh = open(out, 'a', encoding='utf-8')

    def on_msg(msg, data):
        if msg.get('type') == 'send':
            fh.write(json.dumps({'t': round(time.time(), 3), **msg['payload']}, ensure_ascii=False) + '\n')
            fh.flush()

    hit = 0
    for name in ('LeagueClientUxRender.exe', 'LeagueClientUx.exe'):
        try:
            s = frida.attach(name)
        except Exception as exc:
            print(f'[hook] attach {name} 失败: {exc}')
            continue
        sc = s.create_script(FRIDA_JS)
        sc.on('message', on_msg)
        sc.load()
        hit += 1
        print(f'[hook] 已挂在 {name}，录 {seconds}s -> {out}')
    if not hit:
        fh.close()
        return
    time.sleep(seconds)
    fh.close()
    print('[hook] 结束')


# ---------------------------------------------------------------- 专题：退出选人 / 返回大厅
def phase_select_quit(sess):
    sw = os.path.join(DUMP, '01-swagger', 'openapi.raw.json')
    if not os.path.exists(sw):
        print('[select-quit] 先跑 --phase api 拿到 swagger')
        return
    doc = json.load(open(sw, 'r', encoding='utf-8'))
    rows = []
    for p, item in (doc.get('paths') or {}).items():
        if not any(k in p for k in INTEREST):
            continue
        for method, op in item.items():
            if method.lower() not in ('get', 'post', 'put', 'patch', 'delete'):
                continue
            rows.append((p, method.upper(), (op.get('summary') or '').strip()[:80]))
    rows.sort()
    lines = ['# 选人退出 / 返回大厅 专题待测表', '',
             '> 目标：找出「进入选人后不关客户端就退出选人」的底层调用。',
             '> 纪律：全部先在 mock_lcu 或自定义对局验证；带写操作的条目**不要**在排位里试。', '',
             '## 候选端点', '| Path | Method | 摘要 | 实测结果 |', '|---|---|---|---|']
    for p, m, s in rows:
        lines.append(f'| `{p}` | {m} | {s} | |')
    lines += ['', '## 已验证结论（人工填写）', '',
              '- [ ] 退出选人是否可纯客户端触发：',
              '- [ ] 触发后服务端 phase 变化：',
              '- [ ] 是否有排队惩罚 / LP 扣除：',
              '- [ ] 与「关闭客户端」的差异：', '',
              '## 观测手段', '',
              '1. `python tools\\lcu_dump.py --phase watch --seconds 300` 同时手动操作，看 champ-select / gameflow 事件序列；',
              '2. `--phase hook` 抓 UI 上「关闭/退出」按钮实际打的请求；',
              '3. `05-events/uris-*.md` 里按 `champ-select` 过滤，得到选人期全部事件面。']
    save('08-select-quit.md', '\n'.join(lines))
    print(f'[select-quit] 候选 {len(rows)} 条 -> 08-select-quit.md')


def write_readme():
    text = f'''# lcu-dump —— 大厅底层链路全量资料

采集时间基线：{datetime.now():%Y-%m-%d %H:%M:%S}

| 目录/文件 | 内容 |
|---|---|
| `00-session.md` | 端口/token/进程树/证书/只读探针结果 |
| `01-swagger/` | LCU 自带 OpenAPI（端点全表的原始来源） |
| `02-endpoints.md` | 端点分类全表（method/path/摘要/实测列） |
| `03-logs/`、`03-logs-endpoints.txt` | 客户端日志原件 + 日志里出现过的全部端点 |
| `04-plugin-index.md` | `LeagueClient\\plugins` 前端代码索引：插件 → 端点 → 文件:行号 |
| `05-events/` | WAMP `OnJsonApiEvent` 全量录制（jsonl）+ URI 覆盖表 |
| `06-hooks/` | Frida 记录的 UI 真实调用（fetch/XHR，含请求体） |
| `08-select-quit.md` | 选人退出/返回大厅专题待测与结论 |

重建方式：客户端开在大厅 → `python tools\\lcu_dump.py`。
'''
    save('README.md', text)


def main():
    args = sys.argv[1:]
    phase = 'all'
    seconds = 300
    for i, a in enumerate(args):
        if a == '--phase' and i + 1 < len(args):
            phase = args[i + 1]
        if a == '--seconds' and i + 1 < len(args):
            seconds = int(args[i + 1])
    os.makedirs(DUMP, exist_ok=True)
    write_readme()
    sess = discover()
    if phase in ('all', 'session'):
        phase_session(sess)
    if phase in ('all', 'api'):
        phase_api(sess)
    if phase in ('all', 'plugins'):
        phase_plugins(sess)
    if phase in ('all', 'logs'):
        phase_logs(sess)
    if phase == 'watch':
        phase_watch(sess, seconds)
    if phase in ('all', 'select-quit'):
        phase_select_quit(sess)
    if phase == 'hook':
        phase_hook(sess, seconds)
    print(f'\n产物: {DUMP}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
