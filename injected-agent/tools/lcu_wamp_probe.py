"""Clean LCU WAMP event-channel probe with a buffered frame reader.

Usage: python lcu_wamp_probe.py [--seconds N] [--trigger chat|none]
"""
import base64
import json
import os
import socket
import ssl
import struct
import sys
import time
import urllib.request

TOKEN = os.environ.get('LCU_TOKEN', 'tDdPTvNDMcPTVFkU_AXznA')
PORT = int(os.environ.get('LCU_PORT', '56680'))

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE


def auth_header():
    return 'Basic ' + base64.b64encode(f'riot:{TOKEN}'.encode()).decode()


class WsReader:
    def __init__(self, sock):
        self.sock = sock
        self.buf = b''
        self.frag = b''
        self.frag_op = 0

    def _need(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError('socket closed')
            self.buf += chunk

    def _take(self, n):
        self._need(n)
        out, self.buf = self.buf[:n], self.buf[n:]
        return out

    def next_message(self):
        """returns (opcode, payload) for a complete text/binary message"""
        while True:
            b0, b1 = self._take(2)
            op = b0 & 0x0F
            fin = b0 >> 7
            masked = b1 >> 7
            ln = b1 & 0x7F
            if ln == 126:
                ln = struct.unpack('>H', self._take(2))[0]
            elif ln == 127:
                ln = struct.unpack('>Q', self._take(8))[0]
            mask = self._take(4) if masked else b''
            payload = self._take(ln)
            if masked:
                payload = bytes(payload[i] ^ mask[i % 4] for i in range(len(payload)))
            if op == 0x8:
                return ('close', payload)
            if op == 0x9:
                continue
            if op in (0x1, 0x2, 0x0):
                if op:
                    self.frag_op = op
                    self.frag = payload
                else:
                    self.frag += payload
                if fin:
                    msg, self.frag = self.frag, b''
                    return ('text' if self.frag_op == 1 else 'binary', msg)


def send_text(sock, text):
    data = text.encode()
    mask = os.urandom(4)
    header = bytearray([0x81])
    n = len(data)
    if n < 126:
        header.append(0x80 | n)
    elif n < 65536:
        header.append(0x80 | 126)
        header += struct.pack('>H', n)
    else:
        header.append(0x80 | 127)
        header += struct.pack('>Q', n)
    header += mask
    sock.sendall(bytes(header) + bytes(data[i] ^ mask[i % 4] for i in range(n)))


def rest(path, method='GET', body=None):
    req = urllib.request.Request(f'https://127.0.0.1:{PORT}{path}', method=method)
    req.add_header('Authorization', auth_header())
    data = None
    if body is not None:
        req.add_header('Content-Type', 'application/json')
        data = json.dumps(body).encode()
    try:
        with urllib.request.urlopen(req, data=data, timeout=6, context=CTX) as r:
            return r.status, r.read()[:300]
    except urllib.error.HTTPError as e:
        return e.code, e.read()[:200]
    except Exception as e:
        return 'ERR', repr(e).encode()


def main():
    seconds = 12
    trigger = 'chat'
    for i, a in enumerate(sys.argv):
        if a == '--seconds':
            seconds = int(sys.argv[i + 1])
        if a == '--trigger':
            trigger = sys.argv[i + 1]

    raw = socket.create_connection(('127.0.0.1', PORT), timeout=15)
    sock = CTX.wrap_socket(raw, server_hostname='127.0.0.1')
    key = base64.b64encode(os.urandom(16)).decode()
    sock.sendall((
        'GET / HTTP/1.1\r\n'
        f'Host: 127.0.0.1:{PORT}\r\n'
        'Upgrade: websocket\r\nConnection: Upgrade\r\n'
        f'Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n'
        'Sec-WebSocket-Protocol: wamp\r\n'
        f'Authorization: {auth_header()}\r\n\r\n').encode())
    head = b''
    while b'\r\n\r\n' not in head:
        head += sock.recv(4096)
    print('handshake:', head.split(b'\r\n')[0].decode())
    reader = WsReader(sock)
    send_text(sock, '[5,"OnJsonApiEvent"]')
    print('sent subscribe frame: [5,"OnJsonApiEvent"]')

    if trigger == 'chat':
        st, body = rest('/lol-chat/v1/me', 'PUT', {'availability': 'dnd'})
        print('trigger PUT /lol-chat/v1/me availability=dnd ->', st, body[:120])
        time.sleep(1.5)
        st, body = rest('/lol-chat/v1/me', 'PUT', {'availability': 'chat'})
        print('restore availability=chat ->', st, body[:120])

    events = []
    deadline = time.time() + seconds
    sock.settimeout(max(1.0, seconds))
    while time.time() < deadline and len(events) < 60:
        try:
            kind, payload = reader.next_message()
        except socket.timeout:
            break
        except Exception as exc:
            print('reader error:', exc)
            break
        if kind == 'close':
            print('server closed')
            break
        try:
            msg = json.loads(payload.decode('utf-8', 'replace'))
        except Exception:
            print('non-json frame', payload[:80])
            continue
        if isinstance(msg, list) and len(msg) >= 3 and msg[1] == 'OnJsonApiEvent':
            d = msg[2]
            events.append((d.get('eventType'), d.get('uri')))
    sock.close()

    print(f'\nevents: {len(events)}')
    for e in events:
        print(' ', e)
    uris = sorted({e[1] for e in events if e[1]})
    print(f'distinct uris: {len(uris)}')
    for u in uris[:25]:
        print('  ', u)
    return 0 if events else 3


if __name__ == '__main__':
    sys.exit(main())
