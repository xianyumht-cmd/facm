# GGman 注入版 Agent — 总交付报告（"客户端没有的能力"）

日期：2026-11-25
产物：`dll/ggman_core.c` → `dll/build/ggman_core.dll`（C / Zig cc，x64，≈266 KB）
形态：注入 `LeagueClientUxRender.exe`，进程内自治；面板 4 页（状态/自动化/情报/工具）+ 顶栏徽标
验证方式：mock LCU（离线，不消耗账号）+ 离线单测；真机行为标注为"待真机确认"的项单独列出

---

## 0. 完成情况

| # | 目标 | 状态 | 证据 |
|---|---|---|---|
| ① | 选人期全场 10 人情报（位置/段位/近 20 场胜率/常用英雄/连败、对位高亮） | ✅ | `BATCH-A-情报面板.md`、`shots/intel-final.png` |
| ② | 条件秒退（只提示/自动两档）+ 换英雄请求自动接受/拒绝 | ✅ | `BATCH-B-秒退与换英雄.md`、`shots/intel-dodge-advice.png` |
| ③ | 踩坑名单 + 双排识别（本地持久化） | ✅ | `BATCH-C-双排与踩坑名单.md`、`shots/intel-duo-known.png` |
| ④ | ARAM bench 一键换英雄（按个人熟练度） | ✅ | 本文第 4 节 |
| ⑤ | 邀请在线好友 + 邀请最近队友 ✅ / 一键静音（端点待真机确认） | ✅ 主功能完成 | 本文第 5 节 |
| ⑥ | Cross 组件瘦身（检测/结束/停用/恢复） | ✅ | 本文第 6 节，实测改名并已恢复 |

---

## 1. 情报面板（①）

选人开始后每 300ms 抓一名玩家（3 个请求），10 人约 3 秒抓完：

```
我的段位 未获取    抓取 7/10
我 top     Allyself#1234   GOLD II 45   40%(20) 2连胜 157/16/99
敌 ⚠ 双排6 top  Allyemy0#1234  GOLD II 45  30%(20) 2连胜 103/86/64   ← 对位高亮
```

- 数据源：`/lol-summoner/v2/summoners/puuid/*`、`/lol-ranked/v1/ranked-stats/*`、
  `/lol-match-history/v3/matchlist/account/*?begIndex=0&endIndex=19`
- 解析不引 JSON 库：`win` 计数算胜率、英雄计数取前三、顺序扫描算连胜、`gameId` 数组留给双排识别
- 关键修复：`json_str` 容忍 `"key": "v"` 的空格（mock/美化 JSON 也能解析）

## 2. 条件秒退 + 换英雄（②）

```ini
quit_mode=0        # 0 关 / 1 只提示 / 2 自动秒退
quit_enemy_wr=60   # 敌方近 20 场聚合胜率 ≥ 60% 触发
quit_ally_loss=3   # 我方有人 ≥ 3 连败触发
swap_policy=0      # 0 关 / 1 全接受 / 2 全拒绝
```

- 秒退：`POST /lol-champ-select/v1/session/quit`，失败回退 `/lol-gameflow/v1/session/dodge`；每会话一次
- 换英雄：轮询 `GET …/session/champion-swaps` → `POST …/{id}/accept|decline`，8 项 handled-id 去重
- 面板：情报页顶部琥珀色 `⚠ 建议秒退：原因`；工具页有手动「秒退」按钮

实测：`POST session/quit → 204`；`POST champion-swaps/73/accept → 200`（只发一次）。

## 3. 双排识别 + 踩坑名单（③）

- 双排：同队两两比对近 20 场 `gameId`，共享 ≥2 场即标记，面板显示 `双排N`
- **踩到 64 位 bug**：`long` 在 Windows 是 32 位，gameId ≈ 7.9e9 被截断 → 全员误判双排（`duo-marked=7`）；
  改 `long long` + `strtoll` 后正确（`duo-marked=2`）
- 名单：`dll/build/known_players.txt`（TSV，puuid/名字/备注/遇到次数），启动加载、变更写回、上限 200
- 工具页「标记对位为踩坑」→ 命中后行首 `⚠`，count +1

## 4. ARAM bench 一键换英雄（④）

```ini
auto_bench_swap=0    # 1 = 自动换
bench_min_points=0   # 低于该熟练度就不换
```

- 读 `session.benchChampionIds` → 拉 `/lol-champion-mastery/v1/local-player/champion-mastery`
  → 选熟练度最高的那个 → `POST /lol-champ-select/v1/session/bench/swap/{id}`
- 工具页也有手动入口「换上熟练度最高的 bench 英雄」

实测（bench=[22,103,157]，熟练度 150000/90000/40000）：

```
GET  /lol-champion-mastery/v1/local-player/champion-mastery
POST /lol-champ-select/v1/session/bench/swap/22   → 200
action: 换到 bench 英雄 22（熟练度 150000）-> HTTP 200
```

## 5. 邀请在线好友 + 一键静音（⑤）

**邀请在线好友**：`GET /lol-chat/v1/friends` → 过滤 offline → `POST /lol-lobby/v2/lobby/invitations [{"toSummonerId":N}]`

```
GET  /lol-chat/v1/friends
POST /lol-lobby/v2/lobby/invitations  body=[{"toSummonerId":11111}]
POST /lol-lobby/v2/lobby/invitations  body=[{"toSummonerId":22222}]
action: 邀请在线好友 2/2\n```\n\n**邀请最近队友**：`GET /lol-summoner/v1/current-summoner` 拿自己的 puuid → `GET /lol-match-history/v3/matchlist/account/{puuid}?begIndex=0&endIndex=0` 取上一局 participants 的 summonerId（排除自己）→ 邀请最多 4 人\n\n```\nGET  /lol-summoner/v1/current-summoner\nGET  /lol-match-history/v3/matchlist/account/mock-self-puuid?begIndex=0&endIndex=0\nPOST /lol-lobby/v2/lobby/invitations  body=[{"toSummonerId":5000}]\nPOST /lol-lobby/v2/lobby/invitations  body=[{"toSummonerId":5001}]\naction: 邀请最近队友 4/8
```

**静音（部分）**：LCU 没有公开的"批量静音"接口，实现为 best-effort：
`GET /lol-chat/v1/conversations` → `POST /lol-chat/v1/conversations/{id}/mute {"muted":true}`。
mock 返回 200，但**真机是否支持待确认**；失败时会记录 HTTP 码并显示"静音端点不可用 — 记录跳过"，不影响其他功能。

## 6. Cross 组件瘦身（⑥）

`tools/cross_clean.py`：

```
python tools\cross_clean.py            # 报告：Cross 目录大小 + 正在跑的 Cross 进程
python tools\cross_clean.py --kill     # 结束 Cross 下的进程（下次启动会回来）
python tools\cross_clean.py --rename   # 真停用：Cross -> Cross.disabled_<时间戳>
python tools\cross_clean.py --restore  # 恢复最近一次
```

实测（本机）：

```
安装目录: D:\WeGameApps\英雄联盟
Cross    : ...\Cross  (2087.8 MB)
Cross 下正在运行的进程: 0
已停用: ...\Cross -> ...\Cross.disabled_20261125-071840
（测试后已 --restore 恢复原状）
```

安全性：客户端/启动器在跑时默认拒绝改名（需 `--force`）；改名时写 `GGMAN-CROSS-RESTORE.txt` 说明恢复方式。

## 7. 配置总览（agent.ini，全部默认关闭）

```ini
agent_enabled=1        ; master switch
hud=1  button=1
auto_accept=0
auto_pick=0  pick_champ=22  auto_ban=0  ban_champ=0
pick_by_pos=1  pos_top=86 pos_jungle=64 pos_middle=103 pos_bottom=222 pos_utility=16
ban_list=157,99,238   lockin_delay_s=3
auto_runes=0  rune_page=GGman Runes  rune_map=103:GGman Runes,22:Conqueror ADC
auto_spells=0 spell1=4 spell2=14  spell_map=103:4/14,22:3/4
quit_mode=0  quit_enemy_wr=0  quit_ally_loss=0
swap_policy=0
auto_bench_swap=0  bench_min_points=0
auto_honor=0  auto_play_again=0
; mock_http / mock_port / mock_token —— 仅离线测试
```

## 8. 工具与脚本

| 脚本 | 用途 |
|---|---|
| `tools/inject_watch.py` | 注入 + renderer 重启补注入（尊重 `agent_enabled`） |
| `tools/lol_inject_poc.py` | 注入器（`--dll/--exec/--modules`） |
| `tools/panel_tap.py` | 面板坐标点击（调试） |
| `tools/hud_send.py --action N` | **经 IPC 触发工具动作**（0 接受 / 1 点赞 / 2 再来 / 3 重载 / 4 秒退 / 5 标踩坑 / 6 bench / 7 邀请在线好友 / 8 静音 / 9 邀请最近队友 / 10 关闭） |
| `tools/hud_cleanup.py` | 关闭/枚举面板实例 |
| `tools/agent_procs.py` | 查/杀带 agent 的 renderer |
| `tools/mock_lcu.py` | 假 LCU（会话/战绩/精通/好友/换英雄/秒退全都有） |
| `tools/cross_clean.py` | Cross 组件报告/结束/停用/恢复 |
| `tools/perf_sample.py`、`top_cpu.py` | 性能采样 |

## 9. 真机待确认清单

1. 陌生玩家 `matchlist/account/{puuid}` 在国服是否受限（受限时该行显示 `-`，不影响其它列）
2. `/lol-chat/v1/conversations/{id}/mute` 是否在当前版本可用（不可用则静音按钮提示"端点不可用"）
3. `/lol-champ-select/v1/session/quit` 与 `/lol-gameflow/v1/session/dodge` 的实际惩罚表现
4. `theirTeam` 是否带 puuid（不带时该方情报降级为只显示名字/段位）

## 10. 未做（超出本目标）

- 游戏内 overlay（`League of Legends.exe` + DXGI Present）—— ACE 驱动层，单独立项
- CEF 内部 JS（真·客户端页面内嵌与广告拦截）—— 需 hook `libcef.dll`
