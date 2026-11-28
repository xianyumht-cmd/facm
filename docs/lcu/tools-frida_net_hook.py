"""原生层抓包：hook ws2_32!send，从 TCP 数据里捞 HTTP 请求行与 RPC 帧。

为什么不用 window.fetch：
  LeagueClientUxRender 是 CEF renderer，Frida 注入的是原生上下文，没有 window；
  而 LCU 的请求由 CEF 的 network service 经由 browser 进程发出，
  所以要在原生层拦 ws2_32!send / WSASend，才能看到真实发出的字节。

用法：<venv>\\Scripts\\python.exe tools\\frida_net_hook.py --seconds 120
日志：lcu-dump/10-ui-hook/net-<时间戳>.jsonl
"""
import json
import os
import sys
import time
from datetime import datetime

import frida

DUMP = r'D:\project2\ggman\lcu-dump\10-ui-hook'
PROCS = ('LeagueClientUx.exe', 'LeagueClientUxRender.exe', 'LeagueClient.exe')

JS = r'''
(function () {
  function hook(mod, name) {
    var p = null;
    try { p = Module.getExportByName(mod, name); } catch (e) { return; }
    if (!p) return;
    Interceptor.attach(p, {
      onEnter: function (args) {
        try {
          this.buf = args[1];
          this.len = args[2].toInt32();
        } catch (e) { this.len = 0; }
      },
      onLeave: function (ret) {
        try {
          var n = this.len;
          if (!n || n <= 0 || n > 65536) return;
          var bytes = Memory.readByteArray(this.buf, Math.min(n, 8192));
          if (!bytes) return;
          var arr = new Uint8Array(bytes);
          // 只关心的内容：HTTP 方法、/lol 路径、JSON、WebSocket 帧
          var txt = '';
          for (var i = 0; i < arr.length; i++) txt += String.fromCharCode(arr[i]);
          if (txt.indexOf('HTTP') < 0 && txt.indexOf('/lol') < 0 &&
              txt.indexOf('POST ') < 0 && txt.indexOf('GET ') < 0 &&
              txt.indexOf('"uri"') < 0 && txt.indexOf('champ') < 0) return;
          send({k: 'tcp', n: n, d: txt.slice(0, 1500)});
        } catch (e) {}
      }
    });
  }
  hook('ws2_32.dll', 'send');
  hook('ws2_32.dll', 'WSASend');
  send({k: 'ready', pid: Process.id, arch: Process.arch});
})();
'''


def main():
    seconds = 120
    for i, a in enumerate(sys.argv):
        if a == '--seconds' and i + 1 < len(sys.argv):
            seconds = int(sys.argv[i + 1])
    os.makedirs(DUMP, exist_ok=True)
    tag = datetime.now().strftime('%Y%m%d-%H%M%S')
    logf = os.path.join(DUMP, f'net-{tag}.jsonl')
    fh = open(logf, 'a', encoding='utf-8')

    procs = [p for p in frida.get_local_device().enumerate_processes()
             if p.name in PROCS]
    if not procs:
        print('[error] 没找到目标进程')
        return 1
    print(f'[info] 目标: {[(p.pid, p.name) for p in procs]}')

    def on_message(msg, data):
        if msg.get('type') != 'send':
            return
        p = msg['payload']
        rec = {'ts': datetime.now().strftime('%H:%M:%S.%f')[:-3], **p}
        fh.write(json.dumps(rec, ensure_ascii=False) + '\n')
        fh.flush()
        if p.get('k') == 'ready':
            print(' ', json.dumps(rec, ensure_ascii=False)[:160])
        else:
            print(' ', json.dumps(rec, ensure_ascii=False)[:300])

    sessions = []
    for p in procs:
        try:
            s = frida.attach(p.pid)
            sc = s.create_script(JS)
            sc.on('message', on_message)
            sc.load()
            sessions.append(s)
            print(f'[ok] hooked {p.name} pid={p.pid}')
        except Exception as e:
            print(f'[warn] {p.name} pid={p.pid} 失败: {e}')

    if not sessions:
        print('[error] 全部失败')
        return 1
    print(f'[run] 记录 {seconds}s，期间请在客户端里操作')
    try:
        time.sleep(seconds)
    finally:
        fh.close()
    print(f'\n[log] {logf}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
