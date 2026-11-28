"""在 LeagueClientUx 进程内存里搜插件 JS 的关键字符串，并把周边内容 dump 出来。

为什么这样做：磁盘上 0 个 .js（WAD 里是 zstd 资源、pak 里是 service worker），
但运行时的 V8 堆里一定有 JS 源码。搜 showQuitButton / champ-select 这类特征词，
命中处的周边内存就是插件 JS 的实现片段。

用法：<venv>\\Scripts\\python.exe tools\\frida_mem_scan.py --pattern showQuitButton --ctx 2000
输出：lcu-dump/12-mem-scan/<pid>-<pattern>.txt
"""
import base64
import json
import os
import sys
import time
from datetime import datetime

import frida

DUMP = r'D:\project2\ggman\lcu-dump\12-mem-scan'


def main():
    pattern = 'showQuitButton'
    ctx = 2000
    seconds = 90
    for i, a in enumerate(sys.argv):
        if a == '--pattern' and i + 1 < len(sys.argv):
            pattern = sys.argv[i + 1]
        if a == '--ctx' and i + 1 < len(sys.argv):
            ctx = int(sys.argv[i + 1])
        if a == '--seconds' and i + 1 < len(sys.argv):
            seconds = int(sys.argv[i + 1])

    os.makedirs(DUMP, exist_ok=True)
    targets = [p for p in frida.get_local_device().enumerate_processes()
               if p.name in ('LeagueClientUx.exe', 'LeagueClientUxRender.exe')]
    if not targets:
        print('[error] 没有 Ux / renderer 进程')
        return 1
    print(f'[info] 目标: {[(p.pid, p.name) for p in targets]}')

    js = r'''
    var PATTERN = %PATTERN%;
    var CTX = %CTX%;
    var seen = 0;
    function scanRange(base, size, label) {
      try {
        var m = Memory.scanSync(base, size, PATTERN);
        for (var i = 0; i < m.length && seen < 40; i++) {
          var addr = m[i].address;
          var start = addr.sub(CTX);
          var len = CTX * 2;
          var txt = '';
          try {
            var bytes = Memory.readByteArray(start, len);
            var u8 = new Uint8Array(bytes);
            for (var k = 0; k < u8.length; k++) {
              var c = u8[k];
              txt += (c >= 32 && c < 127) ? String.fromCharCode(c) : '.';
            }
          } catch (e) { continue; }
          seen++;
          send({k: 'hit', pid: Process.id, label: label, addr: addr.toString(), text: txt});
        }
      } catch (e) {}
    }
    var ranges = Process.enumerateRanges('r--');
    send({k: 'ranges', pid: Process.id, n: ranges.length});
    for (var i = 0; i < ranges.length; i++) {
      var r = ranges[i];
      if (r.size > 64 * 1024 * 1024) continue;
      scanRange(r.base, r.size, r.protection);
    }
    send({k: 'done', pid: Process.id, hits: seen});
    '''

    # 生成 ASCII 的 hex 模式，例如 "showQuitButton" -> "73 68 6f 77..."
    hexpat = ' '.join(f'{b:02x}' for b in pattern.encode())

    def on_message(msg, data):
        if msg.get('type') != 'send':
            return
        p = msg['payload']
        if p.get('k') == 'hit':
            safe = ''.join(c if c.isalnum() or c in '-_.' else '_' for c in pattern)
            path = os.path.join(DUMP, f"{p['pid']}-{safe}.txt")
            with open(path, 'a', encoding='utf-8') as f:
                f.write(f"=== pid={p['pid']} addr={p['addr']} {p['label']} ===\n")
                f.write(p['text'] + '\n\n')
            print(f"  [hit] pid={p['pid']} @{p['addr']}")
        else:
            print(' ', json.dumps(p, ensure_ascii=False)[:200])

    for p in targets[:3]:
        try:
            s = frida.attach(p.pid)
            sc = s.create_script(js.replace('%PATTERN%', json.dumps(hexpat)).replace('%CTX%', str(ctx)))
            sc.on('message', on_message)
            sc.load()
            print(f'[ok] scanning pid={p.pid}')
        except Exception as e:
            print(f'[warn] {p.pid}: {e}')
    print(f'[run] 扫描进行中，等 {seconds}s')
    time.sleep(seconds)
    print(f'\n[out] {DUMP}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
