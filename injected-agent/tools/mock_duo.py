"""Make enemy0/enemy1 share recent games so duo detection has something to find."""
MOCK = r'D:\project2\ggman\tools\mock_lcu.py'
s = open(MOCK, encoding='utf-8').read()

if 'shared_ids' not in s:
    s = s.replace("""    games = []
    for i in range(total):""",
"""    duo = ('enemy0' in puuid) or ('enemy1' in puuid)
    games = []
    for i in range(total):""")
    s = s.replace("""        games.append({
            'gameId': 7000000000 + h * 100 + i,""",
"""        gid = 7000000000 + h * 100 + i
        if duo and i < 6:
            gid = 7900000000 + i          # six games shared by the duo pair
        games.append({
            'gameId': gid,""")
    open(MOCK, 'w', encoding='utf-8').write(s)
    print('mock: duo shared games added')
else:
    print('mock: already patched')
