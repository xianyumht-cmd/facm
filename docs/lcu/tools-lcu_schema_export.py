"""把 LCU 的 /help schema 全量拆成可读文档。

输入：lcu-dump/01-swagger/openapi.raw.json（GET /help 的原始返回）
输出（lcu-dump/schema/）：
  endpoints-full.md   1452 个函数 → 按 METHOD + 路由分组，标注 remoting/HTTP 性质
  events-full.md      692 个事件 → 按路由分组
  types-full.md       3569 个类型名
  endpoints.csv       机器可读：method,path,plugin,name

路由推导规则（Riot 插件函数名 → HTTP 路由）：
  GetLolChampSelectV1Session        → GET  /lol-champ-select/v1/session
  PostLolLobbyTeamBuilderChampSelectV1SessionQuit
                                    → POST /lol-lobby-team-builder/champ-select/v1/session/quit
  ...ActionsById                    → .../actions/{id}
  ...BySummonerId                   → .../{summonerId}
未知段一律按 camelCase→kebab-case 处理，并标 § 表示"推导置信度低"。
"""
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SRC = r"D:\project2\ggman\lcu-dump\01-swagger\openapi.raw.json"
OUT = r"D:\project2\ggman\lcu-dump\schema"

VERBS = ("Get", "Post", "Put", "Patch", "Delete", "Head", "Options")
# 已知插件前缀（来自 plugins 目录 + 实测），用于提高切分置信度
PLUGINS = [
    "LolLobbyTeamBuilderChampSelect", "LolLobbyTeamBuilder", "LolChampSelect",
    "LolLobby", "LolMatchmaking", "LolGameflow", "LolChat", "LolSummoner",
    "LolRanked", "LolLeaverBuster", "LolMissions", "LolLoot", "LolChallenges",
    "LolPlayerBehavior", "LolHonorV2", "LolHonor", "LolReplays", "LolSpectator",
    "LolPerks", "LolItemSets", "LolLoadouts", "LolRegalia", "LolServiceStatus",
    "LolInventory", "LolPurchaseWidget", "LolStore", "LolCollections",
    "LolGameData", "LolSettings", "LolLogin", "LolPatch", "LolPreEndOfGame",
    "LolEndOfGame", "LolPlayerReportSender", "LolPremadeVoice", "LolClash",
    "LolTft", "LolSocialLeaderboard", "LolRsoAuth", "LolSystem", "LolLicenseAgreement",
    "LolSanitizer", "LolSimpleDialogMessages", "LolSpectate", "LolStatstones",
    "LolYourshop", "LolRewards", "LolRewardTrack", "LolEventHub", "LolHighlights",
    "LolKickout", "LolNavigation", "LolPaw", "LolPft", "LolProfiles", "LolSkinsPicker",
    "LolSocial", "LolEsportsSpectate", "LolChampionDetails", "LolChampionStatistics",
    "Riotclient", "RiotClient", "ProcessControl", "Performance", "Patch",
    "Sanitizer", "System", "PluginManager", "DataStore", "Entitlements",
]

def kebab(seg):
    return re.sub(r"(?<!^)(?=[A-Z])", "-", seg).lower()

def split_plugin(rest):
    for name in sorted(PLUGINS, key=len, reverse=True):
        if rest.startswith(name):
            return name, rest[len(name):]
    m = re.match(r"([A-Z][A-Za-z0-9]*?)(V\d)", rest)
    if m:
        return m.group(1), rest[m.group(1).__len__():]
    return rest, ""

def split_segments(tail):
    """V1SessionActionsById -> ['v1','session','actions','{id}']"""
    if not tail:
        return [], 100
    segs, cur, confidence = [], "", 100
    i = 0
    while i < len(tail):
        ch = tail[i]
        if ch.isupper() and cur:
            segs.append(cur)
            cur = ch
        else:
            cur += ch
        i += 1
    if cur:
        segs.append(cur)

    out = []
    for seg in segs:
        if re.fullmatch(r"V\d+", seg):
            out.append(seg.lower())
        elif seg.startswith("By") and len(seg) > 2:
            out.append("{" + seg[2:3].lower() + seg[3:] + "}")
            confidence = min(confidence, 70)
        else:
            out.append(kebab(seg))
    return out, confidence

def parse_function(name):
    for verb in VERBS:
        if name.startswith(verb):
            rest = name[len(verb):]
            plugin, tail = split_plugin(rest)
            if not plugin:
                return None
            segs, conf = split_segments(tail)
            path = "/" + kebab(plugin)
            if segs:
                path += "/" + "/".join(segs)
            return verb.upper(), path, plugin, conf
    return None

def event_path(ev):
    if not ev.startswith("OnJsonApiEvent_"):
        return None
    rest = ev[len("OnJsonApiEvent_"):]
    m = re.match(r"([a-z0-9\-]+)(?:_v(\d+))?(?:_(.*))?$", rest)
    if not m:
        return None
    base, ver, tail = m.group(1), m.group(2), m.group(3)
    path = "/" + base
    if ver:
        path += "/v" + ver
    if tail:
        path += "/" + tail.replace("_", "/")
    return path

def main():
    doc = json.load(open(SRC, encoding="utf-8"))
    funcs, events, types = doc.get("functions") or {}, doc.get("events") or {}, doc.get("types") or {}
    os.makedirs(OUT, exist_ok=True)

    rows, parsed, unparsed = [], 0, []
    for name in funcs:
        got = parse_function(name)
        if got:
            verb, path, plugin, conf = got
            rows.append((verb, path, plugin, name, conf))
            parsed += 1
        else:
            unparsed.append(name)

    rows.sort(key=lambda r: (r[2], r[1], r[0]))
    by_plugin = {}
    for verb, path, plugin, name, conf in rows:
        by_plugin.setdefault(plugin, []).append((verb, path, name, conf))

    with open(os.path.join(OUT, "endpoints-full.md"), "w", encoding="utf-8") as f:
        f.write("# LCU 端点全表（由 GET /help 推导）\n\n")
        f.write(f"- 函数总数: {len(funcs)}\n- 可推导为 HTTP 路由: {parsed}\n")
        f.write(f"- 无法推导（内部基类方法等）: {len(unparsed)}\n")
        f.write("- 置信度 §: `ByXxx` 段按 `{xxx}` 处理，参数名可能与实际不同\n\n")
        for plugin in sorted(by_plugin):
            items = by_plugin[plugin]
            f.write(f"## {plugin}（{len(items)}）\n\n| Method | Path | 函数名 | 置信 |\n|---|---|---|---|\n")
            for verb, path, name, conf in items:
                mark = "" if conf >= 100 else "§"
                f.write(f"| {verb} | `{path}` | `{name}` | {mark} |\n")
            f.write("\n")
        if unparsed:
            f.write("## 未推导的函数名\n\n")
            for name in sorted(unparsed):
                f.write(f"- `{name}`\n")

    with open(os.path.join(OUT, "endpoints.csv"), "w", encoding="utf-8") as f:
        f.write("method,path,plugin,function,confidence\n")
        for verb, path, plugin, name, conf in rows:
            f.write(f"{verb},{path},{plugin},{name},{conf}\n")

    ev_rows = [(ev, event_path(ev)) for ev in events]
    with open(os.path.join(OUT, "events-full.md"), "w", encoding="utf-8") as f:
        f.write(f"# LCU 事件全表（{len(events)} 个）\n\n")
        f.write("`OnJsonApiEvent` 是总通道；其余为按路由细分的事件名，可单独订阅。\n\n")
        named = [r for r in ev_rows if r[1]]
        misc = [r for r in ev_rows if not r[1]]
        f.write(f"- 可反推路由: {len(named)}\n- 其它: {len(misc)}\n\n")
        f.write("| 事件名 | 对应路由 |\n|---|---|\n")
        for ev, path in sorted(named, key=lambda r: r[1]):
            f.write(f"| `{ev}` | `{path}` |\n")
        if misc:
            f.write("\n## 其它事件\n\n")
            for ev, _ in misc:
                f.write(f"- `{ev}`\n")

    with open(os.path.join(OUT, "types-full.md"), "w", encoding="utf-8") as f:
        f.write(f"# LCU 类型全表（{len(types)} 个）\n\n")
        f.write("`/help` 只给类型名不给字段结构；字段需要从实际响应里反推。\n\n")
        for name in sorted(types):
            f.write(f"- `{name}`\n")

    print(f"函数 {len(funcs)} → 推导 {parsed}，未推导 {len(unparsed)}")
    print(f"事件 {len(events)}，可反推路由 {len([r for r in ev_rows if r[1]])}")
    print(f"类型 {len(types)}")
    print("插件分组:", ", ".join(f"{k}({len(v)})" for k, v in sorted(by_plugin.items(), key=lambda x: -len(x[1]))[:12]))
    print(f"输出目录: {OUT}")

if __name__ == "__main__":
    main()
