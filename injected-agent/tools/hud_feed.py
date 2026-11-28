"""Feed live League data into the injected GGman HUD.

Reads the LCU session straight from the LeagueClientUx.exe command line (the CN
build does not write `lockfile`), polls a few endpoints and pushes formatted
lines into ggman_hud.dll through its WM_COPYDATA IPC window.

Usage:
  python hud_feed.py --once
  python hud_feed.py --interval 3
"""
import argparse
import base64
import json
import ssl
import subprocess
import sys
import time
import urllib.request

sys.path.insert(0, r'D:\project2\ggman\tools')
import hud_send  # noqa: E402

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE

PHASE_ZH = {
    'None': '空闲', 'Lobby': '房间中', 'Matchmaking': '匹配中', 'ReadyCheck': '确认对局',
    'ChampSelect': '选人中', 'GameStart': '进入游戏', 'InProgress': '对局中',
    'Reconnect': '重连中', 'WaitingForStats': '结算中', 'PreEndOfGame': '对局结束',
    'EndOfGame': '结算面板', 'TerminatedInError': '异常终止',
}

QUEUE_ZH = {
    420: '单双排', 430: '匹配', 440: '灵活组排', 450: '极地大乱斗', 490: '快速匹配',
    900: '无限火力', 1020: '克隆大作战', 1700: '斗魂竞技场', 1710: '斗魂竞技场',
    1090: '云顶之弈', 1100: '云顶之弈排位', 1900: '无限乱斗',
}


def lcu_session():
    out = subprocess.run(
        ['powershell', '-NoProfile', '-Command',
         "(Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUx.exe'\").CommandLine"],
        capture_output=True, text=True, encoding='utf-8', errors='replace')
    cmd = out.stdout.strip()
    if not cmd:
        return None
    port = token = None
    for part in cmd.replace('"', ' ').split():
        if part.startswith('--app-port='):
            port = part.split('=', 1)[1]
        elif part.startswith('--remoting-auth-token='):
            token = part.split('=', 1)[1]
    if not (port and token):
        return None
    return port, token


def get(port, token, path):
    req = urllib.request.Request(f'https://127.0.0.1:{port}{path}')
    req.add_header('Authorization', 'Basic ' + base64.b64encode(f'riot:{token}'.encode()).decode())
    try:
        with urllib.request.urlopen(req, timeout=4, context=CTX) as r:
            return json.loads(r.read())
    except Exception:
        return None


def hud_pid():
    """pid of the renderer that owns the injected overlay window"""
    import ctypes
    import ctypes.wintypes as wt

    user32 = ctypes.WinDLL('user32')
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, lp):
        name = ctypes.create_unicode_buffer(128)
        user32.GetClassNameW(h, name, 128)
        if name.value == 'GGmanHudOverlay':
            pid = wt.DWORD()
            user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
            found.append(pid.value)
        return True

    user32.EnumWindows(cb, 0)
    return found[0] if found else None


def build_lines(port, token):
    me = get(port, token, '/lol-chat/v1/me') or {}
    gameflow = get(port, token, '/lol-gameflow/v1/gameflow-phase')
    lobby = get(port, token, '/lol-lobby/v2/lobby')
    ranked = get(port, token, '/lol-ranked/v1/current-ranked-stats') or {}

    lines = []
    name = me.get('gameName') or '?'
    tag = me.get('gameTag') or '?'
    lines.append(f'召唤师  {name} #{tag}   状态 {me.get("availability", "?")}')

    phase = gameflow if isinstance(gameflow, str) else '?'
    lines.append(f'阶段  {phase} / {PHASE_ZH.get(phase, phase)}   LCU :{port}')

    if lobby:
        qid = (lobby.get('gameConfig') or {}).get('queueId') or 0
        members = lobby.get('members') or []
        lines.append(f'房间  {QUEUE_ZH.get(qid, "队列 " + str(qid))}  {len(members)}/5   房主 '
                     f'{lobby.get("partyId", "")[:8]}')
    else:
        lines.append('房间  —（未在房间里）')

    entry = (ranked.get('queueMap') or {}).get('RANKED_SOLO_5x5') or {}
    if entry.get('tier'):
        lines.append(f'单双排  {entry.get("tier")} {entry.get("division")} {entry.get("leaguePoints", 0)} LP')
    else:
        lines.append('单双排  —（本赛季未定级）')

    lines.append(f'渲染  ggman_hud.dll @ pid {hud_pid() or "?"}   {time.strftime("%H:%M:%S")}')
    return lines, me


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--interval', type=float, default=3.0)
    ap.add_argument('--once', action='store_true')
    args = ap.parse_args()

    while True:
        sess = lcu_session()
        if not sess:
            print('LCU not running', flush=True)
            if args.once:
                return 2
            time.sleep(3)
            continue
        port, token = sess
        lines, me = build_lines(port, token)
        ok, detail = hud_send.send(1, 0, '\n'.join(lines))
        print(('ok   ' if ok else 'fail ') + f':{port} ' + ' | '.join(lines[:2]) + f'  [{detail}]', flush=True)
        if args.once:
            return 0 if ok else 3
        time.sleep(args.interval)


if __name__ == '__main__':
    sys.exit(main())
