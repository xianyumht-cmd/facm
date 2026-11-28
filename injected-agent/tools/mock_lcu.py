"""Tiny mock LCU server for offline testing of the injected agent.

Serves just enough of the LCU surface for champ-select / accept / honor logic and
logs every request so the agent's behaviour can be asserted without touching a
real League account.

Usage:
  python tools\mock_lcu.py --port 18080 --session dll\tests\cs_my_pick.json
  curl http://127.0.0.1:18080/_requests
"""
import argparse
import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = r'D:\project2\ggman'
LOG_PATH = os.path.join(ROOT, 'captures', 'mock-requests.log')

STATE = {
    'phase': 'ChampSelect',
    'session': {},
    'pages': [],
    'requests': [],
}


def log(line):
    os.makedirs(os.path.dirname(LOG_PATH), exist_ok=True)
    with open(LOG_PATH, 'a', encoding='utf-8') as f:
        f.write(line + '\n')




def make_matchlist(puuid):
    """deterministic fake 20-game history per puuid"""
    h = sum(ord(c) for c in puuid)
    wins = 6 + h % 12          # 6..17
    total = 20
    streak = 1 + (h % 4)
    pool = [22, 103, 86, 64, 222, 157, 16, 99]
    duo = ('enemy0' in puuid) or ('enemy1' in puuid)
    games = []
    for i in range(total):
        win = i < streak or (i >= streak and (i - streak) % 3 != 0 and i >= streak + (total - wins))
        gid = 7000000000 + h * 100 + i
        if duo and i < 6:
            gid = 7900000000 + i          # six games shared by the duo pair
        games.append({
            'gameId': gid,
            'gameCreation': 1764000000000 - i * 1800000,
            'gameDuration': 1800 + (i * 37) % 900,
            'queueId': 450,
            'participants': [{
                'championId': pool[(h + i) % len(pool)],
                'stats': {'win': bool(win), 'kills': 3 + i % 9, 'deaths': 2 + i % 7, 'assists': 5 + i % 11},
                'summonerId': 5000 + i,
            }],
        })
    return {'accountId': 0, 'games': {'gameCount': total, 'games': games}}


class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def _send(self, code, payload=None, raw=None):
        body = raw if raw is not None else (json.dumps(payload).encode() if payload is not None else b'')
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _record(self, method, body=None):
        entry = {'t': time.strftime('%H:%M:%S'), 'method': method, 'path': self.path,
                 'body': body, 'auth': bool(self.headers.get('Authorization'))}
        STATE['requests'].append(entry)
        log(f"{entry['t']} {method:6s} {self.path} auth={entry['auth']} body={body}")

    def do_GET(self):
        self._record('GET')
        p = self.path.split('?')[0]
        if p == '/lol-gameflow/v1/gameflow-phase':
            return self._send(200, raw=json.dumps(STATE['phase']).encode())
        if p == '/lol-champ-select/v1/session':
            return self._send(200, STATE['session'] or {})
        if p.startswith('/lol-summoner/v2/summoners/puuid/'):
            return self._send(200, {'gameName': 'Ally' + p[-4:], 'gameTag': '1234',
                                    'summonerLevel': 210, 'puuid': p.rsplit('/', 1)[-1]})
        if p.startswith('/lol-ranked/v1/ranked-stats/'):
            return self._send(200, {'queueMap': {'RANKED_SOLO_5x5': {
                'tier': 'GOLD', 'division': 'II', 'leaguePoints': 45}}})
        if p.startswith('/lol-match-history/v3/matchlist/account/'):
            puuid = p.rsplit('/', 1)[-1]
            return self._send(200, make_matchlist(puuid))
        if p == '/lol-champ-select/v1/session/champion-swaps':
            return self._send(200, [{'id': 73, 'state': 'AVAILABLE', 'stateId': 1,
                                     'toSummonerId': 16056685666, 'championId': 22}])
        if p == '/lol-champion-mastery/v1/local-player/champion-mastery':
            return self._send(200, [
                {'championId': 22, 'championPoints': 150000, 'championLevel': 9},
                {'championId': 103, 'championPoints': 90000, 'championLevel': 7},
                {'championId': 157, 'championPoints': 40000, 'championLevel': 5},
            ])
        if p == '/lol-perks/v1/pages':
            return self._send(200, STATE['pages'] or [])
        if p == '/lol-chat/v1/friends':
            return self._send(200, [
                {'summonerId': 11111, 'name': 'FriendA', 'availability': 'chat'},
                {'summonerId': 22222, 'name': 'FriendB', 'availability': 'dnd'},
                {'summonerId': 33333, 'name': 'FriendC', 'availability': 'offline'},
            ])
        if p == '/lol-chat/v1/conversations':
            return self._send(200, [{'id': 'lobby-conv-1', 'type': 'chat', 'muted': False}])
        if p == '/lol-chat/v1/me':
            return self._send(200, {'gameName': 'MockPlayer', 'gameTag': '0001', 'availability': 'chat'})
        if p == '/lol-summoner/v1/current-summoner':
            return self._send(200, {'gameName': 'MockPlayer', 'summonerId': 16056685666,
                                    'puuid': 'mock-self-puuid', 'summonerLevel': 30,
                                    'percentCompleteForNextLevel': 50})
        if p == '/lol-summoner/v1/current-summoner':
            return self._send(200, {'gameName': 'MockPlayer', 'summonerLevel': 30,
                                    'percentCompleteForNextLevel': 50})
        if p == '/lol-honor-v2/v1/ballot':
            return self._send(200, {'gameId': 1234, 'eligibleAllies': [
                {'summonerId': 42, 'puuid': 'mock-puuid-1', 'championId': 22}],
                'votePool': {'votes': 1}})
        if p == '/_requests':
            return self._send(200, STATE['requests'])
        return self._send(404, {'errorCode': 'RESOURCE_NOT_FOUND'})

    def _body(self):
        n = int(self.headers.get('Content-Length') or 0)
        return self.rfile.read(n).decode('utf-8', 'replace') if n else ''

    def do_POST(self):
        body = self._body()
        self._record('POST', body)
        if 'bench/swap' in self.path:
            return self._send(200, {})
        if 'session/quit' in self.path or 'session/dodge' in self.path:
            return self._send(204, raw=b'')
        if '/mute' in self.path:
            return self._send(200, {})
        if 'lobby/invitations' in self.path:
            return self._send(200, {})
        if 'champion-swaps' in self.path:
            return self._send(200, {})
        if '/complete' in self.path or 'ready-check/accept' in self.path or 'play-again' in self.path:
            return self._send(200, {})
        if 'honor-player' in self.path:
            return self._send(200, {})
        return self._send(404, {'errorCode': 'RPC_ERROR'})

    def do_PATCH(self):
        body = self._body()
        self._record('PATCH', body)
        return self._send(200, {})

    def do_PUT(self):
        body = self._body()
        self._record('PUT', body)
        return self._send(200, {})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--port', type=int, default=18080)
    ap.add_argument('--session', default=os.path.join(ROOT, 'dll', 'tests', 'cs_my_pick.json'))
    ap.add_argument('--pages', default=os.path.join(ROOT, 'dll', 'tests', 'perk_pages.json'))
    ap.add_argument('--phase', default='ChampSelect')
    ap.add_argument('--seconds', type=float, default=0, help='0 = run forever')
    args = ap.parse_args()

    STATE['phase'] = args.phase
    with open(args.session, encoding='utf-8') as f:
        STATE['session'] = json.load(f)
    if os.path.exists(args.pages):
        with open(args.pages, encoding='utf-8') as f:
            STATE['pages'] = json.load(f)
    if os.path.exists(LOG_PATH):
        os.remove(LOG_PATH)

    srv = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    print(f'mock LCU on http://127.0.0.1:{args.port} phase={args.phase} session={os.path.basename(args.session)}', flush=True)
    if args.seconds:
        t = threading.Timer(args.seconds, srv.shutdown)
        t.start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        srv.server_close()
        print('mock stopped; requests:')
        for r in STATE['requests']:
            print(f"  {r['method']:6s} {r['path']} {r['body'] or ''}")
    return 0


if __name__ == '__main__':
    raise SystemExit(main())



