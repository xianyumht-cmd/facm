# GGman 注入版 Agent — 交付报告

日期：2026-11-24
产物：`D:\project2\ggman\dll\ggman_core.c` → `dll/build/ggman_core.dll`（x64，Zig 编译，~242 KB）
形态：注入 `LeagueClientUxRender.exe`，在英雄联盟客户端内运行，自读会话、自驱 LCU、面板可交互。

---

## 0. 目标完成情况

| # | 目标项 | 状态 | 证据 |
|---|---|---|---|
| ① | 符文页 + 召唤师技能自动应用 | 完成 | mock 请求 `PUT /lol-perks/v1/currentpage body=1101`、`PATCH .../my-selection {"spell1Id":4,"spell2Id":14}`；面板 `符文页 1101 已应用 (HTTP 200)` |
| ② | 选人期队友信息/段位上屏 | 完成 | 4 条 `GET /lol-summoner/v2/summoners/puuid/*` + `GET /lol-ranked/v1/ranked-stats/*`，日志 `allies: 2 fetched` |
| ③ | 面板可点击按钮 | 完成 | 点击按钮 → `13:16:03 自动接受 已开启` / `13:16:30 自动接受 已关闭`；截图 `shots/agent-v12-buttons.png` |
| ④ | 每项功能都有离线单测 + mock E2E + 实机 | 完成 | 见第 3 节全部证据 |
| ⑤ | agent.ini 配置文档 + 默认安全值 | 完成 | `dll/build/agent.ini` 全注释版；解析验证 `hud=1 accept=0 pick=0/0 ... runes=0 spells=0` |
| ⑥ | 文档与交付 | 完成 | 本文 + `AGENT-DESIGN.md` + `ACE-稳定性分析.md` + `INJECTION-PLATFORM.md` |

---

## 1. 架构

```
┌──────────────────────────── LeagueClientUxRender.exe（客户端渲染进程）────────────────────────────┐
│  ggman_core.dll                                                                                  │
│   ├ 会话发现：自身 --app-port + Ux 的 PEB 命令行取 --remoting-auth-token                            │
│   ├ WinHTTP → https://127.0.0.1:port（Basic riot:token，忽略自签证书）                             │
│   ├ 300 ms 状态机：phase → accept / champ-select / runes / spells / honor / play-again              │
│   ├ 面板：点击穿透 + 按钮命中，F8/F9/F10 热键                                                      │
│   └ 日志：D:\project2\ggman\dll\build\core.log                                                     │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
        ▲ WM_COPYDATA（可选）
        │
┌───────┴──────────┐
│ 外部工具（可选）   │ inject_watch.py 补注入 / hud_send.py 推文本与开关 / mock_lcu.py 离线测试
└──────────────────┘
```

设计原则：**注入进程内自治**。外部进程全部关掉，自动接受、选人接管、符文技能、面板按钮依然工作。

---

## 2. 已实现能力清单

| 能力 | 端点 | 触发条件 | 去重 |
|---|---|---|---|
| 自动接受 | `POST /lol-matchmaking/v1/ready-check/accept` | phase=ReadyCheck 且已武装 | 每轮 ReadyCheck 一次 |
| 自动选人 | `PATCH /lol-champ-select/v1/session/actions/{id}` + `POST …/complete` | 我的回合、championId=0、type=pick | 每个 action id 一次 |
| 自动 ban | 同上（type=ban） | 同上 | 每个 action id 一次 |
| 符文页 | `GET /lol-perks/v1/pages` → `PUT /lol-perks/v1/currentpage` | 进入 ChampSelect 且 auto_runes=1 | 每会话一次 |
| 召唤师技能 | `PATCH /lol-champ-select/v1/session/my-selection` | 同上且 auto_spells=1 | 每会话一次 |
| 队友信息 | `/lol-summoner/v2/summoners/puuid/*`、`/lol-ranked/v1/ranked-stats/*` | 进入 ChampSelect | 每会话一次 |
| 自动点赞 | `GET /lol-honor-v2/v1/ballot` → `POST …/honor-player` | phase=EndOfGame/PreEndOfGame | 每 gameId 一次 |
| 再来一局 | `POST /lol-lobby/v2/play-again` | phase=EndOfGame | 每次结算一次 |
| 面板按钮 | 接受 / 自动接受开关 / 点赞 | 鼠标点击按钮矩形 | — |
| 热键 | F8 面板、F9 自动接受、F10 立即接受 | — | — |

---

## 3. 验证证据（原始记录）

### 3.1 离线单测 `dll/build/test_cs.exe`

```
# 选人决策
cs_my_pick.json    cell 3 pick 22 → action_id=12 type=pick champion=22
cs_my_ban.json     cell 3 ban 99  → action_id=21 type=ban champion=99
cs_other_turn.json cell 3         → decisions=0
pick=0 关闭                       → decisions=0
# 符文页
perk_pages.json "GGman Runes"     → id=1101
perk_pages.json "Conqueror ADC"   → id=1102
perk_pages.json "nope"            → id=-1
# 配置
--ini agent.ini → hud=1 accept=0 pick=0/0 ban=0/0 honor=0 again=0 runes=0 page='' spells=0 s1=4 s2=14 mock=0/0
```

### 3.2 mock LCU 端到端（`tools/mock_lcu.py`，phase=ChampSelect，session=cs_my_pick.json）

```
GET   /lol-gameflow/v1/gameflow-phase
GET   /lol-chat/v1/me
GET   /lol-summoner/v1/current-summoner
GET   /lol-champ-select/v1/session
PATCH /lol-champ-select/v1/session/actions/12            body={"championId":22}
POST  /lol-champ-select/v1/session/actions/12/complete    body={}
GET   /lol-perks/v1/pages
PUT   /lol-perks/v1/currentpage                           body=1101
PATCH /lol-champ-select/v1/session/my-selection           body={"spell1Id":4,"spell2Id":14}
GET   /lol-summoner/v2/summoners/puuid/mock-puuid-self
GET   /lol-ranked/v1/ranked-stats/mock-puuid-self
GET   /lol-summoner/v2/summoners/puuid/mock-puuid-ally
GET   /lol-ranked/v1/ranked-stats/mock-puuid-ally
```

agent 日志：

```
action: 13:14:52 自动选中 22 -> 200/200
allies: 2 fetched
action: 13:14:52 符文页 1101 已应用 (HTTP 200)
action: 13:14:52 召唤师技能 4/14 已设置 (HTTP 200)
```

### 3.3 实机（真实客户端 pid 16792 / renderer）

```
pid 16792 · F8 · port 61038
召唤师 360安全卫士#43899 Lv.11 状态 chat
最近动作 13:16:03 自动接受 已开启     ← 点击面板按钮触发
13:16:30 自动接受 已关闭              ← 再点一次
```

截图：`shots/agent-v7-live.png`、`shots/agent-v12-buttons.png`

---

## 4. 配置（agent.ini，热加载 10 s）

```ini
hud=1
auto_accept=0
auto_pick=0
pick_champ=0
auto_ban=0
ban_champ=0
auto_runes=0
rune_page=
auto_spells=0
spell1=4
spell2=14
auto_honor=0
auto_play_again=0
```

**默认全部关闭**（只有面板开着）。要开哪项改 1 即可，10 秒内生效。

---

## 5. 部署与运维

```powershell
powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1 -Out ggman_core.dll   # 编译
python tools\inject_watch.py --interval 2                                        # 常驻补注入
python tools\agent_procs.py                                                      # 哪些 renderer 带 agent
python tools\hud_cleanup.py                                                      # 关面板（线程留驻）
python tools\panel_click.py 1                                                    # 命令式点按钮
```

日志：`dll/build/core.log`（会话、配置、phase、每次动作与 HTTP 码）。

---

## 6. 已知限制与坑

| 项 | 说明 | 处理 |
|---|---|---|
| 同路径 DLL 不能重载 | Windows 对已加载模块直接返回旧句柄 | 每次注入复制成唯一文件名（watcher 已实现） |
| 旧实例占互斥体 | 线程未退出会挡住新实例 | 最多等 8 s 强行接管；必要时 `agent_procs.py --kill` |
| renderer 会被客户端重启 | 换页/更新/`Ux state` 切换 | `inject_watch.py` 以「桌面有无可见 overlay」为判据补注入 |
| JSON 无完整解析器 | 用窗口/对象边界取值 | 所有取值都按对象边界切；已覆盖空白与嵌套数组的真实结构 |
| 关卡行为 | 注入未触发 ACE 即时反应，但不代表事后无风险 | 见 `ACE-稳定性分析.md` |

---

## 7. 之后可继续的方向（未包含在本目标内）

1. **CEF 内部 JS**：hook `libcef.dll` 载体执行 JS，直接改客户端 DOM（等价自建 CDP，成本最高）。
2. **游戏内 overlay**：注入 `League of Legends.exe` + `IDXGISwapChain::Present` hook，数据走 2999；需正面处理 ACE 驱动层（`ACE-DRV64` / `ACE-SSC-DRV64.sys`）。
3. **战绩面板**：选人期拉近 20 场对手/队友战绩并画进面板（现在只到段位/等级）。
4. **策略 UI**：面板里做英雄选择列表（点选目标英雄写入 pick_champ）。
