"""在所有插件 bundle 里搜"退出选人 / 回大厅"相关的调用，判定是否存在第二条路径。

搜索目标：
  session/quit              选人退出
  request-lobby             请求回大厅
  cancel-champ-select       自定义取消选人
  dodge                     秒退相关
  gameflow/v1/session/...   流程控制
  leave-lobby / leaveLobby  离房

对每个命中，输出：插件名、命中次数、上下文（含 URL 的调用对象）。
"""
import glob
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BUNDLES = r"D:\project2\ggman\lcu-dump\bundles"
OUT = r"D:\project2\ggman\facm\docs\lcu\EXIT-SEARCH.md"

TARGETS = [
    ("session/quit", "选人退出"),
    ("request-lobby", "请求回大厅"),
    ("cancel-champ-select", "自定义取消选人"),
    ("dodge", "秒退"),
    ("leave-lobby", "离房"),
    ("leaveLobby", "离房(方法)"),
    ("gameflow/v1/session/reconnect", "重连"),
    ("gameflow/v1/session/", "流程会话写"),
    ("showHome", "回主页(UI)"),
]


def context(src, pos, radius=320):
    return src[max(0, pos - radius):pos + radius].replace("\n", " ")


def main():
    files = sorted(glob.glob(os.path.join(BUNDLES, "*.js")))
    print(f"[scan] {len(files)} 个 bundle")
    report = []
    for path in files:
        plugin = os.path.basename(path)[:-3]
        src = open(path, encoding="utf-8", errors="replace").read()
        rows = []
        for kw, label in TARGETS:
            hits = [m.start() for m in re.finditer(re.escape(kw), src)]
            if hits:
                rows.append((kw, label, len(hits), hits[:3]))
        if rows:
            report.append((plugin, len(src), rows))
            print(f"[hit] {plugin:<34} " + ", ".join(f"{kw}x{n}" for kw, _, n, _ in rows))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("# 退出选人 / 回大厅 相关的全插件搜索\n\n")
        f.write(f"扫描 {len(files)} 个插件的 JS bundle。\n\n")
        f.write("## 命中概览\n\n| 插件 | 命中 |\n|---|---|\n")
        for plugin, size, rows in report:
            f.write(f"| `{plugin}` | " + ", ".join(f"`{kw}`×{n}" for kw, _, n, _ in rows) + " |\n")

        f.write("\n## 命中上下文\n\n")
        for plugin, size, rows in report:
            src = open(os.path.join(BUNDLES, plugin + ".js"), encoding="utf-8", errors="replace").read()
            f.write(f"### {plugin}（{size/1024/1024:.2f} MB）\n\n")
            for kw, label, n, positions in rows:
                if kw in ("dodge", "showHome", "gameflow-phase"):
                    continue  # 噪声太多，只在概览里计
                f.write(f"**{label}** `{kw}` ×{n}\n\n```\n")
                for pos in positions:
                    f.write(context(src, pos)[:700] + "\n---\n")
                f.write("```\n\n")

    print(f"\n[out] {OUT}")


if __name__ == "__main__":
    main()
