"""批量解包关键插件 WAD 并汇总能力面。

对每个插件：解 assets.wad → 找最大的 JS bundle → 提取路由 / 写调用 / 关键字。
输出到 lcu-dump/plugin-capabilities/：
  <plugin>.routes.txt     该插件声明的路由
  <plugin>.writes.txt     POST/PUT/PATCH/DELETE 调用及上下文
  SUMMARY.md              跨插件汇总
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
import zstandard

PLUGIN_ROOT = r"D:\WeGameApps\英雄联盟\LeagueClient\plugins"
OUT = r"D:\project2\ggman\lcu-dump\plugin-capabilities"

TARGETS = [
    "rcp-fe-lol-champ-select",
    "rcp-fe-lol-navigation",
    "rcp-fe-lol-social",
    "rcp-fe-lol-parties",
    "rcp-fe-lol-postgame",
    "rcp-fe-lol-loot",
    "rcp-fe-lol-match-history",
    "rcp-fe-lol-kiwi-hub",
    "rcp-fe-lol-collections",
    "rcp-fe-lol-store",
]

SIG = b"\x28\xb5\x2f\xfd"
ROUTE_RE = re.compile(r"/(?:lol-[a-z0-9\-]+|riotclient|lol-settings|data-store)(?:/v\d+)?/[A-Za-z0-9_\-/{}$:.]*")
WRITE_RE = re.compile(
    r"""(?:type\s*:\s*["'](POST|PUT|PATCH|DELETE)["']|method\s*:\s*["'](POST|PUT|PATCH|DELETE)["']|\.(post|put|patch|delete)\s*\(\s*["'`]([^"'`]{6,140})["'`])""",
    re.I)


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
    best_js = b""
    for off in frames:
        try:
            blob = dec.decompress(data[off:], max_output_size=64 * 1024 * 1024)
        except Exception:
            try:
                with dec.stream_reader(io.BytesIO(data[off:])) as r:
                    blob = r.read()
            except Exception:
                continue
        if blob[:4] == b"\x89PNG" or blob[:4] in (b"wOFF", b"wOF2", b"OTTO"):
            continue
        if b"function" in blob[:4000] or b"exports" in blob[:4000] or b"Ember" in blob[:4000]:
            if len(blob) > len(best_js):
                best_js = blob
    return best_js.decode("utf-8", "replace"), len(frames)


def main():
    os.makedirs(OUT, exist_ok=True)
    summary = []
    for plugin in TARGETS:
        wad = os.path.join(PLUGIN_ROOT, plugin, "assets.wad")
        if not os.path.exists(wad):
            summary.append((plugin, 0, 0, 0, "no assets.wad"))
            print(f"[skip] {plugin}: 无 assets.wad")
            continue
        src, frames = unbundle(wad)
        if not src:
            summary.append((plugin, frames, 0, 0, "no js found"))
            print(f"[miss] {plugin}: {frames} 帧，未找到 JS")
            continue

        routes = sorted({m.group().rstrip('"\'`, ') for m in ROUTE_RE.finditer(src)})
        writes = []
        for m in WRITE_RE.finditer(src):
            verb = (m.group(1) or m.group(2) or m.group(3) or "").upper()
            path = m.group(4) or ""
            if path:
                writes.append((verb, path, m.start()))

        with open(os.path.join(OUT, f"{plugin}.routes.txt"), "w", encoding="utf-8") as f:
            for r in routes:
                f.write(r + "\n")
        with open(os.path.join(OUT, f"{plugin}.writes.txt"), "w", encoding="utf-8") as f:
            for verb, path, pos in writes[:80]:
                ctx = src[max(0, pos - 120):pos + 160].replace("\n", " ")
                f.write(f"{verb:<7} {path:<56} {ctx[:200]}\n")

        summary.append((plugin, frames, len(src), len(routes), len(writes)))
        print(f"[ok] {plugin:<28} frames={frames:<4} js={len(src)/1024/1024:>5.2f}MB routes={len(routes):<4} writes={len(writes)}")

    with open(os.path.join(OUT, "SUMMARY.md"), "w", encoding="utf-8") as f:
        f.write("# 插件能力面汇总（从 WAD 解包得出）\n\n")
        f.write("| 插件 | zstd 帧 | JS 大小 | 路由数 | 写调用数 |\n|---|---|---|---|---|\n")
        for plugin, frames, size, routes, writes in summary:
            if isinstance(writes, str):
                f.write(f"| `{plugin}` | {frames} | - | - | {writes} |\n")
            else:
                f.write(f"| `{plugin}` | {frames} | {size/1024/1024:.2f} MB | {routes} | {writes} |\n")
    print(f"\n[out] {OUT}")

if __name__ == "__main__":
    main()
