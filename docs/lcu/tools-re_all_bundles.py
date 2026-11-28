"""全量解包所有插件，把每个插件的最大 JS bundle 落盘，供后续全局搜索。

输出：
  lcu-dump/bundles/<plugin>.js        每个插件的 JS bundle
  lcu-dump/bundles/INDEX.md           清单（帧数/大小/路由数）
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
import zstandard

ROOT = r"D:\WeGameApps\英雄联盟\LeagueClient\plugins"
OUT = r"D:\project2\ggman\lcu-dump\bundles"
SIG = b"\x28\xb5\x2f\xfd"
ROUTE_RE = re.compile(r"/(?:lol-[a-z0-9\-]+|riotclient)(?:/v\d+)?/[A-Za-z0-9_\-/{}$:.]*")


def unbundle(wad_path):
    data = open(wad_path, "rb").read()
    frames, i = [], 0
    while True:
        i = data.find(SIG, i)
        if i < 0:
            break
        frames.append(i)
        i += 4
    dec = zstandard.ZstdDecompressor()
    best = b""
    for off in frames:
        try:
            blob = dec.decompress(data[off:], max_output_size=64 * 1024 * 1024)
        except Exception:
            try:
                with dec.stream_reader(io.BytesIO(data[off:])) as r:
                    blob = r.read()
            except Exception:
                continue
        if blob[:4] == b"\x89PNG" or blob[:4] in (b"wOFF", b"wOF2", b"OTTO", b"ttcf"):
            continue
        if b"function" in blob[:4000] or b"exports" in blob[:4000] or b"Ember" in blob[:4000]:
            if len(blob) > len(best):
                best = blob
    return best.decode("utf-8", "replace"), len(frames)


def main():
    os.makedirs(OUT, exist_ok=True)
    rows = []
    plugins = sorted(d for d in os.listdir(ROOT) if os.path.isdir(os.path.join(ROOT, d)))
    for n, plugin in enumerate(plugins, 1):
        wad = os.path.join(ROOT, plugin, "assets.wad")
        if not os.path.exists(wad):
            rows.append((plugin, 0, 0, 0, "no wad"))
            print(f"[{n}/{len(plugins)}] {plugin:<34} 无 assets.wad")
            continue
        try:
            src, frames = unbundle(wad)
        except Exception as exc:
            rows.append((plugin, 0, 0, 0, str(exc)[:40]))
            print(f"[{n}/{len(plugins)}] {plugin:<34} 解包失败 {exc}")
            continue
        if not src:
            rows.append((plugin, frames, 0, 0, "no js"))
            print(f"[{n}/{len(plugins)}] {plugin:<34} {frames} 帧，无 JS")
            continue
        with open(os.path.join(OUT, f"{plugin}.js"), "w", encoding="utf-8") as f:
            f.write(src)
        routes = len({m.group().rstrip('"\'`, ') for m in ROUTE_RE.finditer(src)})
        rows.append((plugin, frames, len(src), routes, "ok"))
        print(f"[{n}/{len(plugins)}] {plugin:<34} {len(src)/1024/1024:>5.2f}MB routes={routes}")

    with open(os.path.join(OUT, "INDEX.md"), "w", encoding="utf-8") as f:
        f.write("# 全量插件 JS bundle 清单\n\n| 插件 | 帧 | JS | 路由 | 状态 |\n|---|---|---|---|---|\n")
        for plugin, frames, size, routes, status in rows:
            f.write(f"| `{plugin}` | {frames} | {size/1024/1024:.2f} MB | {routes} | {status} |\n")
    total = sum(r[2] for r in rows)
    print(f"\n[total] {len(rows)} 个插件，JS 合计 {total/1024/1024:.1f} MB -> {OUT}")


if __name__ == "__main__":
    main()
