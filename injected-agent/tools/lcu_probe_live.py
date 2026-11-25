"""Probe the running LCU: REST, WAMP, and renderer DevTools surface."""
import base64
import json
import ssl
import subprocess
import sys
import urllib.request

TOKEN = 'tDdPTvNDMcPTVFkU_AXznA'
PORT = 56680
RIOT_TOKEN = 'djINwWTh1N6_WVScG5ioxQ'
RIOT_PORT = 56581

CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE


def get(port, token, path, method='GET'):
    req = urllib.request.Request(f'https://127.0.0.1:{port}{path}', method=method)
    req.add_header('Authorization', 'Basic ' + base64.b64encode(f'riot:{token}'.encode()).decode())
    try:
        with urllib.request.urlopen(req, timeout=5, context=CTX) as r:
            return r.status, r.read()[:600]
    except urllib.error.HTTPError as e:
        return e.code, e.read()[:300]
    except Exception as e:
        return 'ERR', repr(e).encode()


def main():
    print('== LCU REST (app-port %d) ==' % PORT)
    for p in ('/lol-summoner/v1/current-summoner', '/lol-gameflow/v1/gameflow-phase',
              '/lol-region-locale/v1/region', '/riotclient/region-locale', '/lol-chat/v1/me'):
        st, body = get(PORT, TOKEN, p)
        print(f'{p:45s} -> {st} {body.decode("utf-8", "replace")[:280]!r}')

    print('\n== Riot client (riotclient-app-port %d) ==' % RIOT_PORT)
    for p in ('/riotclient/region-locale', '/riotclient/get_region_locale'):
        st, body = get(RIOT_PORT, RIOT_TOKEN, p)
        print(f'{p:45s} -> {st} {body.decode("utf-8", "replace")[:280]!r}')

    print('\n== renderer command lines ==')
    out = subprocess.run(['powershell', '-NoProfile', '-Command',
                          "Get-CimInstance Win32_Process -Filter \"Name='LeagueClientUxRender.exe'\" | "
                          "Select-Object ProcessId,CommandLine | ConvertTo-Json -Depth 3"],
                         capture_output=True, text=True, encoding='utf-8', errors='replace')
    try:
        data = json.loads(out.stdout)
    except Exception:
        print(out.stdout[:2000])
        return
    if isinstance(data, dict):
        data = [data]
    for proc in data:
        print(f"\npid={proc['ProcessId']}\n  {proc['CommandLine']}")


if __name__ == '__main__':
    main()
