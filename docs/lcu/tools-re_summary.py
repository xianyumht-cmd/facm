"""把 re_plugins.py 的产物汇总成一份可读的能力面文档。"""
import os
import sys
from collections import defaultdict

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SRC = r"D:\project2\ggman\lcu-dump\plugin-capabilities"
OUT = r"D:\project2\ggman\facm\docs\lcu\PLUGIN-CAPABILITIES.md"


def read_lines(path):
    if not os.path.exists(path):
        return []
    return [l.rstrip("\n") for l in open(path, encoding="utf-8", errors="replace") if l.strip()]


def main():
    plugins = sorted({f.split(".")[0] for f in os.listdir(SRC) if f.endswith(".routes.txt")})

    route_owner = defaultdict(set)
    for plugin in plugins:
        for r in read_lines(os.path.join(SRC, f"{plugin}.routes.txt")):
            route_owner[r].add(plugin)

    writes = defaultdict(list)
    for plugin in plugins:
        for line in read_lines(os.path.join(SRC, f"{plugin}.writes.txt")):
            parts = line.split(None, 2)
            if len(parts) >= 2:
                writes[parts[0]].append((parts[1], plugin))

    groups = defaultdict(list)
    for route in route_owner:
        seg = route.strip("/").split("/")
        key = "/".join(seg[:2]) if len(seg) >= 2 else route
        groups[key].append(route)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("# 插件能力面（从 WAD 解包 + JS bundle 提取）\n\n")
        f.write("数据来源：`LeagueClient/plugins/<plugin>/assets.wad` → zstd 帧解压 → 最大 JS bundle → 静态提取。\n")
        f.write("这是**客户端自己声明的路由**，比 `/help` 更贴近实现，也比黑盒探测更完整。\n\n")
        f.write(f"- 覆盖插件: {len(plugins)}\n")
        f.write(f"- 去重路由: {len(route_owner)}\n")
        f.write(f"- 写调用（去重方法+路径）: {sum(len(v) for v in writes.values())}\n\n")

        f.write("## 按前缀分组的路由\n\n")
        for key in sorted(groups):
            items = sorted(groups[key])
            f.write(f"### `/{key}`（{len(items)}）\n\n")
            f.write("| 路由 | 声明插件 |\n|---|---|\n")
            for route in items:
                owners = ", ".join(sorted(route_owner[route]))
                f.write(f"| `{route}` | {owners} |\n")
            f.write("\n")

        f.write("## 写调用（POST / PUT / PATCH / DELETE）\n\n")
        f.write("| 方法 | 路径 | 插件 |\n|---|---|---|\n")
        seen = set()
        for verb in sorted(writes):
            for path, plugin in sorted(writes[verb]):
                key = (verb, path)
                if key in seen:
                    continue
                seen.add(key)
                f.write(f"| {verb} | `{path}` | {plugin} |\n")

    print(f"[out] {OUT}")
    print(f"[stats] 插件 {len(plugins)}，路由 {len(route_owner)}，写调用 {len(seen)}")


if __name__ == "__main__":
    main()
