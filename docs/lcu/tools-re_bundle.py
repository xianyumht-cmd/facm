"""从解包出来的插件 JS bundle 里提取能力面。

输入：lcu-dump/wad-out/<plugin>/050_js.js（完整 bundle）
输出：
  routes.txt    该插件声明的全部 LCU 路由（去重、按前缀分组）
  quit-context.txt  showQuitButton / quit 相关代码片段
  writes.txt    POST/PUT/PATCH/DELETE 调用的上下文
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BUNDLE = sys.argv[1] if len(sys.argv) > 1 else \
    r"D:\project2\ggman\lcu-dump\wad-out\rcp-fe-lol-champ-select\050_js.js"
OUT = os.path.dirname(BUNDLE)

src = open(BUNDLE, encoding="utf-8", errors="replace").read()
print(f"[bundle] {BUNDLE}  {len(src)/1024/1024:.2f} MB")

route_re = re.compile(r"/(?:lol-[a-z0-9\-]+|riotclient|lol-settings|data-store)(?:/v\d+)?/[A-Za-z0-9_\-/{}$:.]*")
routes = {}
for m in route_re.finditer(src):
    r = m.group().rstrip('"\'`, ')
    routes[r] = routes.get(r, 0) + 1

print(f"[routes] 去重 {len(routes)} 条")
with open(os.path.join(OUT, "routes.txt"), "w", encoding="utf-8") as f:
    by_prefix = {}
    for r in routes:
        parts = r.split("/")
        key = "/".join(parts[1:3]) if len(parts) > 2 else r
        by_prefix.setdefault(key, []).append(r)
    for key in sorted(by_prefix):
        f.write(f"## {key}\n")
        for r in sorted(by_prefix[key]):
            f.write(f"  {r}   x{routes[r]}\n")
        f.write("\n")
print("[routes] 已写 routes.txt")

for kw, fname in (("showQuitButton", "quit-context.txt"),
                  ("quitCustom", "quit-context.txt"),
                  ("cancel-champ-select", "quit-context.txt")):
    hits = [m.start() for m in re.finditer(re.escape(kw), src)]
    print(f"[kw] {kw}: {len(hits)} 处")
    if hits:
        with open(os.path.join(OUT, fname), "a", encoding="utf-8") as f:
            f.write(f"\n===== {kw} ({len(hits)}) =====\n")
            for i in hits[:6]:
                f.write(src[max(0, i - 600):i + 600] + "\n---\n")

write_re = re.compile(
    r"""(?:type\s*:\s*["'](POST|POST|PUT|PATCH|DELETE)["']|method\s*:\s*["'](POST|PUT|PATCH|DELETE)["']|\.(post|put|patch|delete)\s*\(\s*["'`]([^"'`]{6,120})["'`])""",
    re.I)
writes = []
for m in write_re.finditer(src):
    verb = (m.group(1) or m.group(2) or m.group(3) or "").upper()
    path = m.group(4) or ""
    writes.append((verb, path, m.start()))
print(f"[writes] 写调用 {len(writes)} 处")
with open(os.path.join(OUT, "writes.txt"), "w", encoding="utf-8") as f:
    for verb, path, pos in writes[:60]:
        ctx = src[max(0, pos - 160):pos + 200].replace("\n", " ")
        f.write(f"{verb:<7} {path:<52} {ctx[:220]}\n")

print(f"[out] {OUT}")
