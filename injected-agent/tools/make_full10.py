"""Build a full 5v5 champ-select fixture + extend mock LCU with match history."""
import json
import os
import re

ROOT = r'D:\project2\ggman'
FIX = os.path.join(ROOT, 'dll', 'tests', 'cs_full10.json')
MOCK = os.path.join(ROOT, 'tools', 'mock_lcu.py')

src = json.load(open(os.path.join(ROOT, 'dll', 'tests', 'cs_my_pick.json'), encoding='utf-8-sig'))
for m in src['myTeam']:
    m.setdefault('puuid', f'mock-puuid-self{m["cellId"]}')
    m.setdefault('assignedPosition', ['top', 'jungle', 'middle', 'bottom', 'utility'][m['cellId'] % 5])
src['myTeam'][0]['assignedPosition'] = 'top'
src['myTeam'][1]['assignedPosition'] = 'utility'

enemy_positions = ['top', 'jungle', 'middle', 'bottom', 'utility']
src['theirTeam'] = []
for i in range(5):
    src['theirTeam'].append({
        'assignedPosition': enemy_positions[i],
        'cellId': 5 + i,
        'championId': 0,
        'entitledFeatureType': '',
        'selectedSkinId': 0,
        'spell1Id': 4,
        'spell2Id': 14,
        'summonerId': 90000 + i,
        'puuid': f'mock-puuid-enemy{i}',
        'team': 2,
        'wardSkinId': -1,
    })
json.dump(src, open(FIX, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('fixture ->', FIX, 'myTeam', len(src['myTeam']), 'theirTeam', len(src['theirTeam']))

# ---- mock: add match-history endpoint ----
m = open(MOCK, encoding='utf-8').read()
if 'matchlist/account' not in m:
    helper = '''

def make_matchlist(puuid):
    """deterministic fake 20-game history per puuid"""
    h = sum(ord(c) for c in puuid)
    wins = 6 + h % 12          # 6..17
    total = 20
    streak = 1 + (h % 4)
    pool = [22, 103, 86, 64, 222, 157, 16, 99]
    games = []
    for i in range(total):
        win = i < streak or (i >= streak and (i - streak) % 3 != 0 and i >= streak + (total - wins))
        games.append({
            'gameId': 7000000000 + h * 100 + i,
            'gameCreation': 1764000000000 - i * 1800000,
            'gameDuration': 1800 + (i * 37) % 900,
            'queueId': 450,
            'participants': [{
                'championId': pool[(h + i) % len(pool)],
                'stats': {'win': bool(win), 'kills': 3 + i % 9, 'deaths': 2 + i % 7, 'assists': 5 + i % 11},
            }],
        })
    return {'accountId': 0, 'games': {'gameCount': total, 'games': games}}
'''
    m = m.replace('class Handler(BaseHTTPRequestHandler):', helper + '\n\nclass Handler(BaseHTTPRequestHandler):')
    m = m.replace("""        if p == '/lol-perks/v1/pages':""",
"""        if p.startswith('/lol-match-history/v3/matchlist/account/'):
            puuid = p.rsplit('/', 1)[-1]
            return self._send(200, make_matchlist(puuid))
        if p == '/lol-perks/v1/pages':""")
    open(MOCK, 'w', encoding='utf-8').write(m)
    print('mock: match-history endpoint added')
else:
    print('mock: already has match-history endpoint')
