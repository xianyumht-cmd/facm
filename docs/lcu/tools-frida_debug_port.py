"""给 LeagueClientUx 注入 CEF 调试端口。

思路：hook 启动链里的 CreateProcessW，当它要创建 LeagueClientUx.exe 时，
      在命令行末尾追加 --remote-debugging-port=9222（Chromium 认这个参数）。
      成功的话 http://127.0.0.1:9222/json 就能列出页面，用 CDP 执行任意 JS。

用法：
  1) 先关掉 LOL（If 已开）
  2) <venv>\\Scripts\\python.exe tools\\frida_debug_port.py --seconds 240
  3) 立刻从 WeGame 点「启动」
  4) 看是否出现 [hit] 与 9222 监听

日志：lcu-dump/11-cef-debug/spawn-<时间戳>.jsonl
"""
import json
import os
import sys
import time
from datetime import datetime

import frida

DUMP = r'D:\project2\ggman\lcu-dump\11-cef-debug'
TARGETS = ('RiotClientServices.exe', 'wegame.exe', 'client_launcher.exe', 'LeagueClient.exe')
WANT = 'LeagueClientUx'

JS = r'''
(function () {
  var EXTRA = ' --remote-debugging-port=9222 --remote-allow-origins=*';
  function hookCreate(mod) {
    var p = null;
    try { p = Module.getExportByName(mod, 'CreateProcessW'); } catch (e) { return false; }
    if (!p) return false;
    Interceptor.attach(p, {
      onEnter: function (args) {
        try {
          var app = args[0].isNull() ? '' : args[0].readUtf16String();
          var cmd = args[1].isNull() ? '' : args[1].readUtf16String();
          var line = (app || '') + ' | ' + (cmd || '');
          send({k: 'spawn', line: line.slice(0, 400)});
          var isUx = line.indexOf('LeagueClientUx') >= 0 || line.indexOf('LeagueClient.exe') >= 0;
          var hasPort = line.indexOf('remote-debugging-port') >= 0;
          send({k: 'seen', target: isUx, hasPort: hasPort, line: line.slice(0, 200)});
          if (isUx && !hasPort) {
            var neu = (cmd && cmd.length ? cmd : app) + EXTRA;
            var buf = Memory.allocUtf16String(neu);
            args[1] = buf;              // lpCommandLine 指向新串
            if (app) args[0] = ptr(0);  // 让系统用命令行定位 exe
            send({k: 'patched', newcmd: neu.slice(0, 400)});
          }
        } catch (e) {
          send({k: 'err', msg: String(e)});
        }
      }
    });
    return true;
  }
  hookCreate('kernel32.dll');
  hookCreate('kernelbase.dll');
  send({k: 'ready', pid: Process.id, name: Process.mainModule ? Process.mainModule.name : ''});
})();
'''


def main():
    seconds = 240
    for i, a in enumerate(sys.argv):
        if a == '--seconds' and i + 1 < len(sys.argv):
            seconds = int(sys.argv[i + 1])
    os.makedirs(DUMP, exist_ok=True)
    tag = datetime.now().strftime('%Y%m%d-%H%M%S')
    logf = os.path.join(DUMP, f'spawn-{tag}.jsonl')
    fh = open(logf, 'a', encoding='utf-8')

    def on_message(msg, data):
        if msg.get('type') != 'send':
            return
        p = msg['payload']
        rec = {'ts': datetime.now().strftime('%H:%M:%S.%f')[:-3], **p}
        fh.write(json.dumps(rec, ensure_ascii=False) + '\n')
        fh.flush()
        print(' ', json.dumps(rec, ensure_ascii=False)[:300])

    hooked = {}
    deadline = time.time() + seconds
    print(f'[run] 盯 {seconds}s，等待启动链出现（现在去 WeGame 点启动）')
    while time.time() < deadline:
        procs = [p for p in frida.get_local_device().enumerate_processes() if p.name in TARGETS]
        for p in procs:
            if p.pid in hooked:
                continue
            try:
                s = frida.attach(p.pid)
                sc = s.create_script(JS)
                sc.on('message', on_message)
                sc.load()
                hooked[p.pid] = s
                print(f'[ok] hooked {p.name} pid={p.pid}')
            except Exception as e:
                print(f'[warn] {p.name} pid={p.pid}: {e}')
        # 检查 9222
        import socket
        try:
            with socket.create_connection(('127.0.0.1', 9222), timeout=1):
                print('\n*** 9222 已监听 ***')
                import urllib.request
                try:
                    print(urllib.request.urlopen('http://127.0.0.1:9222/json/version', timeout=3).read()[:300])
                except Exception as e:
                    print('version err:', e)
                break
        except Exception:
            pass
        time.sleep(2)
    fh.close()
    print(f'\n[log] {logf}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
