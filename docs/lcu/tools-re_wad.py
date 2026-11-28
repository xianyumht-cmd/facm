"""解 Riot WAD（RW 0x0304）：定位 zstd 帧、逐个解压、识别内容类型。

已知（前面测出来的）：
  · 魔数 "RW" + 0x0304
  · 文件里散布 zstd 帧（28 B5 2F FD），champ-select 的 assets.wad 有 51 个
  · 帧前有 16 字节对齐的空洞（0x00），说明存在条目表

本脚本不做完整格式推导，而是：
  1. 扫出所有 zstd 帧起始
  2. 用 zstandard 逐帧解压（stream_reader 能吃多个连续帧）
  3. 每个解压结果判断类型（JSON / JS / PNG / 字体 / 其它）
  4. 把命中的 JS/JSON 落盘

用法：python tools/re_wad.py "路径\\assets.wad" [--out 目录]
"""
import io
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
import zstandard

SIG = b"\x28\xb5\x2f\xfd"


def sniff(blob):
    if blob[:4] == b"\x89PNG":
        return "png"
    if blob[:3] == b"\xff\xd8\xff":
        return "jpeg"
    if blob[:4] in (b"wOFF", b"wOF2", b"OTTO", b"ttcf"):
        return "font"
    head = blob[:400].lstrip()
    if head[:1] in (b"{", b"["):
        return "json"
    if b"function" in blob[:4000] or b"exports" in blob[:4000] or b"Ember" in blob[:4000]:
        return "js"
    if head[:1] == b"<":
        return "html"
    return "other"


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    path = sys.argv[1]
    out_dir = None
    for i, a in enumerate(sys.argv):
        if a == "--out" and i + 1 < len(sys.argv):
            out_dir = sys.argv[i + 1]
    if out_dir is None:
        out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "lcu-dump", "wad-out",
                               os.path.basename(os.path.dirname(path)) or "wad")
    os.makedirs(out_dir, exist_ok=True)

    data = open(path, "rb").read()
    print(f"[file] {path}  {len(data)/1024/1024:.1f} MB")
    print(f"[head] {data[:16].hex()}  magic={data[:2]!r} ver={data[2:4].hex()}")

    frames = []
    i = 0
    while True:
        i = data.find(SIG, i)
        if i < 0:
            break
        frames.append(i)
        i += 4
    print(f"[frames] zstd 帧 {len(frames)} 个，首帧偏移 {frames[:6]}")

    dec = zstandard.ZstdDecompressor()
    stats = {}
    kept = []
    for n, off in enumerate(frames):
        try:
            blob = dec.decompress(data[off:], max_output_size=64 * 1024 * 1024)
        except Exception:
            try:
                with dec.stream_reader(io.BytesIO(data[off:])) as r:
                    blob = r.read()
            except Exception as exc:
                stats["failed"] = stats.get("failed", 0) + 1
                continue
        kind = sniff(blob)
        stats[kind] = stats.get(kind, 0) + 1
        if kind in ("js", "json", "html", "other") and len(blob) > 64:
            ext = {"js": ".js", "json": ".json", "html": ".html"}.get(kind, ".bin")
            name = f"{n:03d}_{kind}{ext}"
            with open(os.path.join(out_dir, name), "wb") as fh:
                fh.write(blob)
            kept.append((name, len(blob), kind))

    print("[stats]", json.dumps(stats, ensure_ascii=False))
    print(f"[kept] {len(kept)} 条落盘到 {out_dir}")
    for name, size, kind in kept[:20]:
        print(f"   {name:<18} {size/1024:>8.1f} KB  {kind}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
