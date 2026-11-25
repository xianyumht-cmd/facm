# GGman In-Client Agent（v5）

`dll/ggman_core.c` → `dll/build/ggman_core.dll`（x64，Zig 编译，~230 KB）

这是第二阶段的成果：注入进 `LeagueClientUxRender.exe` 的**自给自足**代理。除绘图外它自己就能驱动客户端——不依赖任何外部轮询进程。

---

## 1. 它自己怎么找到 LCU

| 信息 | 来源 | 说明 |
|---|---|---|
| `--app-port` | **自己进程的命令行** | renderer 启动参数里就带 `--app-port=61038`，`GetCommandLineW()` 直接读 |
| `--remoting-auth-token` | **`LeagueClientUx.exe` 命令行（PEB 读取）** | 国服不写 `lockfile`，token 只在 Ux 的启动参数里。`NtQueryInformationProcess(ProcessBasicInformation)` → `PEB+0x20`（ProcessParameters）→ `+0x70`（CommandLine UNICODE_STRING）→ `ReadProcessMemory` |
| 兜底 | IPC `kind=5`（flags=port，text=token） | 若将来 ACE 把 Ux 变成受保护进程、PEB 读失败，外部 helper 可把会话推给 DLL |

踩过的坑：`--remoting-auth-token` 在命令行里是**带引号**的（`"--remoting-auth-token=…"`），按空格切词会整段跳过，必须用 `wcsstr` 逐位置匹配。

## 2. HTTP 通道

- WinHTTP：`WinHttpOpen` → `WinHttpConnect(127.0.0.1, port)` → `WinHttpOpenRequest(..., WINHTTP_FLAG_SECURE)`
- 自签名证书：`WINHTTP_OPTION_SECURITY_FLAGS` 忽略 CA / CN / 日期
- 认证：`Authorization: Basic base64("riot:" + token)`
- 已验证：`GET /lol-gameflow/v1/gameflow-phase`、`GET /lol-chat/v1/me`、`GET /lol-summoner/v1/current-summoner`、`POST /lol-matchmaking/v1/ready-check/accept`

## 3. 行为循环（300 ms）

```
读 phase
  ├─ phase 变化 → 记录日志 + 刷新面板
  ├─ phase == ReadyCheck 且 auto-accept 已开启 且 本轮未接受
  │     → POST /lol-matchmaking/v1/ready-check/accept → 面板显示 HTTP 码
  └─ 每 6 s → GET /lol-chat/v1/me（名字#tag、状态）+ GET /lol-summoner/v1/current-summoner（等级）
```

## 4. 热键（注入进程内注册，客户端焦点下直接生效）

| 键 | 作用 |
|---|---|
| F8 | HUD 显示 / 隐藏（被占用时自动回退 Alt+F8 → Ctrl+Alt+F8 → Ctrl+Shift+F8） |
| F9 | 自动接受开 / 关 |
| F10 | 立即接受当前确认框（POST accept） |

## 5. IPC（外部控制，仍保留）

窗口类 `GGmanHudIPC`，`WM_COPYDATA` 负载 `GGmanHudCommand { kind, flags, text[2048] }`：

| kind | 含义 |
|---|---|
| 1 | 设置面板附加文本（最多 6 行，`\n` 分隔） |
| 2 | 显隐（flags 0/1/2=切换） |
| 3 | 清空文本 |
| 5 | 覆盖会话（flags=port，text=token） |
| 6 | 自动接受开 / 关（flags 0/1/2） |
| 7 | 立即接受 |

命令行：`python tools\hud_send.py --arm | --disarm | --accept | --session PORT:TOKEN | "文本"`

## 6. 现在的分工

| 模块 | 位置 | 作用 |
|---|---|---|
| `ggman_core.dll` | 注入 `LeagueClientUxRender.exe` | 自读会话、自驱 REST、自动接受、选人接管、赛后自动化、面板 + 热键 |
| `dll/build/agent.ini` | 磁盘 | 策略配置，**每 10 秒热加载**，改完不用重新注入 |
| `tools/inject_watch.py` | 外部常驻 | renderer 重启后自动补注入 |
| `tools/hud_send.py` | 外部按需 | 推自定义文本 / 远程开关（IPC） |
| `tools/mock_lcu.py` | 测试 | 离线假 LCU，用来验证逻辑而不碰真实账号 |
| `dll/build/test_cs.exe` | 测试 | 选人决策的离线单测（喂 JSON，打印会发什么请求） |

## 7. 策略配置（agent.ini）

```ini
hud=1               ; 面板开关
auto_accept=0       ; 自动接受（也可 F9 切换）
auto_pick=0
pick_champ=0        ; 自动选人：英雄 ID（0=关闭）
auto_ban=0
ban_champ=0         ; 自动 ban 英雄 ID
auto_honor=0        ; 赛后自动点赞（ballot 第一位队友）
auto_play_again=0   ; 结算页自动再来一局
; 仅离线测试用：mock_http=1 / mock_port=18080 / mock_token=xxx
```

## 8. 选人接管的实现与验证

逻辑：300 ms 轮询 `GET /lol-champ-select/v1/session`，在 `actions` 里找 `isInProgress:true` 且
`actorCellId == localPlayerCellId`、`championId == 0` 的动作，然后

```
PATCH /lol-champ-select/v1/session/actions/{id}          {"championId":22}
POST  /lol-champ-select/v1/session/actions/{id}/complete {}
```

同一个 action id 只发一次（`g_cs_last_action`），离开 ChampSelect 时复位。

两层验证：

**（a）离线单测** `test_cs.exe`（把 `session.json` 喂进去，看决策）：

| 场景 | 结果 |
|---|---|
| 我的选人回合（cell 3, action 12） | `action_id=12 type=pick champion=22` ✅ |
| 我的 ban 回合（cell 3, action 21） | `action_id=21 type=ban champion=99` ✅ |
| 别人回合（cell 4 在选） | `decisions=0` ✅ |
| 关闭自动选人（pick=0） | `decisions=0` ✅ |

**（b）端到端** `mock_lcu.py` 假 LCU（`ChampSelect` + 上述 session），agent 指向它后服务端收到的请求：

```
GET   /lol-gameflow/v1/gameflow-phase            auth=True
GET   /lol-chat/v1/me                            auth=True
GET   /lol-summoner/v1/current-summoner          auth=True
GET   /lol-champ-select/v1/session               auth=True
PATCH /lol-champ-select/v1/session/actions/12    auth=True body={"championId":22}
POST  /lol-champ-select/v1/session/actions/12/complete auth=True body={}
```

面板同步显示：`最近动作 12:54:34 自动选中 22 -> 200/200`；后续轮询没有再重复发送，说明幂等守卫有效。

### 8.1 符文页 + 召唤师技能（v10 起）

```
GET  /lol-perks/v1/pages                                  → 按名字找 id（容忍 JSON 空白）
PUT  /lol-perks/v1/currentpage        body=1101
PATCH /lol-champ-select/v1/session/my-selection
      body={"spell1Id":4,"spell2Id":14}
```

- 页名找不到时回退到 `"isActive":true` 那页；仍找不到就放弃，不刷接口。
- 每个 ChampSelect 只做一次（离开阶段复位）。
- 离线单测：`test_cs.exe perk_pages.json "GGman Runes"` → `id=1101`；页名不存在 → `-1`。
- mock 端到端日志：`PUT /lol-perks/v1/currentpage auth=True body=1101` + `PATCH .../my-selection body={"spell1Id":4,"spell2Id":14}`，面板记 `符文页 1101 已应用 (HTTP 200)`。

### 8.2 队友信息上屏（v11 起）

进入 ChampSelect 后读一次 `session.myTeam[]`，对每个队友按 `puuid`（没有则用 `summonerId`）拉：

```
GET /lol-summoner/v2/summoners/puuid/{puuid}   → gameName / gameTag / summonerLevel
GET /lol-ranked/v1/ranked-stats/{puuid}        → queueMap.RANKED_SOLO_5x5 tier/division/leaguePoints
```

结果画进面板（最多 5 行）：`队3  名字#1234  Lv210  黄金 II 45LP`。
每个会话只拉一次（`g_ally_done`），离开 ChampSelect 复位。

mock 证据：

```
GET /lol-summoner/v2/summoners/puuid/mock-puuid-self
GET /lol-ranked/v1/ranked-stats/mock-puuid-self
GET /lol-summoner/v2/summoners/puuid/mock-puuid-ally
GET /lol-ranked/v1/ranked-stats/mock-puuid-ally
log: allies: 2 fetched
```

### 8.3 可点击面板（v12 起）

- 去掉 `WS_EX_TRANSPARENT`，改为 **按区域命中**：`WM_NCHITTEST` 只在按钮矩形里返回 `HTCLIENT`，其余返回 `HTTRANSPARENT`（不挡客户端点击）。
- `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`（点了也不抢焦点）。
- 三个按钮：`接受对局`（POST accept）/ `自动接受`（开关，绿底=已开）/ `点赞队友`（立即 ballot→honor）。

实测：`python tools\panel_click.py 1` → 日志 `13:16:03 自动接受 已开启`；再点一次 → `13:16:30 自动接受 已关闭`。
截图 `shots/agent-v12-buttons.png`。


## 9. 已知坑

| 现象 | 原因 | 处理 |
|---|---|---|
| 新注入的实例一直"等待"、面板不更新 | 旧实例线程仍持有 `Local\GGmanHudOwner` 互斥体（窗口关了但线程没退） | 现在最多等 8 s 就强行接管；要彻底干净就 `agent_procs.py --kill` 杀掉带旧模块的 renderer |
| 同一 renderer 里堆了 7 个 ggman 模块 | 反复注入不同版本 | `agent_procs.py` 一眼看出，杀掉该 renderer 让客户端重建 |
| 客户端 UI 整块空白 | 杀掉 `--first-renderer-process` 后 Chromium 需要重新起渲染进程 | 等 10–15 s；仍空白就 WeGame 重新启动 |


## 10. 构建与部署

```powershell
powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1 -Out ggman_core.dll      # 编译
python tools\inject_watch.py --once                                                 # 注入
python tools\inject_watch.py --interval 2                                           # 常驻守护
python tools\agent_procs.py                                                         # 看哪些 renderer 带 agent
python tools\hud_send.py --arm                                                      # 打开自动接受
python tools\hud_cleanup.py                                                         # 拆除（仅窗口）
```

离线验证：

```powershell
# 选人决策单测
dll\build\test_cs.exe dll\tests\cs_my_pick.json 3 22 99
# 端到端假 LCU（另开窗口跑 agent 指向它，见 agent.ini 的 mock_* 项）
python tools\mock_lcu.py --port 18080 --session dll\tests\cs_my_pick.json --seconds 120
```

日志：`dll/build/core.log`（会话发现、配置、phase 变化、每次动作与 HTTP 码）。

## 11. 下一批可以接的能力（按收益排序）

1. **符文/召唤师技能自动应用**：`/lol-perks/v1/pages` 选预设 + `/lol-champ-select/v1/session/my-selection` 写 spell1/spell2。
2. **选人期数据上屏**：`/lol-champ-select/v1/session` 里队友 puuid → `/lol-match-history` 或 SGP 拉近 20 场，画在面板里（比外置窗口快一档）。
3. **面板交互化**：去掉 `WS_EX_TRANSPARENT`，按钮命中测试（同进程，点自己的窗口不涉检测面）。
4. **CEF 内部 JS**（成本最高）：hook `libcef.dll` 执行 JS，直接改客户端 DOM —— 真·客户端内页面。
5. **游戏内 overlay**：注入 `League of Legends.exe` + DXGI Present hook，数据走 `2999`；要正面处理 ACE 驱动层。



## 12. v14/v15：分页面板 + 开关持久化

面板从"单页 HUD + 3 个按钮"升级成 4 个标签页（600×372）：

| 标签 | 内容 |
|---|---|
| 状态 | 召唤师/等级/在线状态、Gameflow 阶段、选人格位、最近动作 |
| 自动化 | 7 行开关：自动接受 / 自动选人 / 自动禁人 / 自动符文页 / 自动召唤师技能 / 自动点赞 / 结算再来一局 |
| 数据 | 我的段位（`/lol-ranked/v1/current-ranked-stats`）+ 本局队友（名字#tag / 等级 / 段位 / LP） |
| 工具 | 立即接受对局 / 给队友点赞 / 结算再来一局 / 重新读取配置 |

交互实现：
- 标签与行都由 `layout_panel()` 计算矩形，`WM_NCHITTEST` 只在这两类矩形里返回 `HTCLIENT`，其余 `HTTRANSPARENT`（不挡客户端点击）。
- 点开关行 → 翻转 `g_cfg` 对应字段 → **立即写回 `agent.ini`**（`config_save()`）→ 生效。
- `g_armed`（自动接受）在配置热加载时与 `auto_accept` 同步，避免面板与文件不一致。

实测（`tools/panel_tap.py`）：

```
tap (87,48)   → 状态页
tap (513,48)  → 工具页
tap (300,78)  → 自动接受 ON  → log: "自动接受 已开启" + "config saved (accept=1 ...)"
agent.ini     → auto_accept=1（文件被重写）
```

截图：`shots/panel-tab-status2.png`、`shots/panel-tab-tools.png`、`shots/panel-tab-auto-on.png`。

## 13. 性能实测：去广告到底能省多少

在同一台机器上采样（`tools/perf_sample.py`，30 s 窗口，`LeagueClient` 全树）：

| 场景 | LeagueClient.exe | Ux | 渲染进程合计 | 整树合计 | 内存 |
|---|---|---|---|---|---|
| 主页（营销内容视图） | **79.90%** | 20.00% | 24.74% | **128.96%** | 1413 MB |
| 经典模式页（无营销内容） | **75.21%** | 18.96% | 25.09% | **124.84%** | 1395 MB |

结论：**切换页面只差约 4 个百分点（≈0.04 核），去广告对 CPU 的收益很小**。
真正的开销在 `LeagueClient.exe` 自身（常驻 75–80% 单核）与客户端 CEF 渲染，不在营销页。
另外机器连通性检查：`https://lol.qq.com/client/client_lcu.shtml` 返回 200，但客户端里那块区域显示为灰色空块——
说明营销视图在本机本来就没成功渲染，拿它做"减负"更没有意义。

要真正降占用，优先级应该是：客户端自身动画/特效开关 → 事件驱动替代轮询（agent 侧）→ CEF 内部替换页面（L3）。

## 14. v16/v17：顶栏徽标按钮（打开/收起面板）

面板不再只能靠 F8：客户端顶栏多了一个 **G 圆形徽标**（32×32，`WS_EX_LAYERED` colorkey 透明）。

| 行为 | 说明 |
|---|---|
| 左键单击 | 显示 / 隐藏面板（徽标边框变色：青=面板开着，灰=收起） |
| 按住拖动 | 自由挪位置，松手后把相对客户区的坐标写进 `agent.ini`（`btn_dx` / `btn_dy`） |
| 默认位置 | `btn_dx=-1` 时自动落在 **云顶之弈标签与通行证图标之间** 的空隙（客户区 x=404, y=10），不覆盖任何客户端按钮 |
| 点击穿透 | 徽标只占 32×32，其余区域照常点到客户端；实测点"云顶之弈"标签照常切页 |

为什么没放在"商城右边"：实测在那里放不下——`商城` 图标与右侧货币条之间只有 ~30px，32px 徽标会盖住 `商城` 文案（截图 `shots/badge-topbar-crop.png` 为证）。
所以默认放在标签区右侧的空隙，并做成 **可拖动 + 记住位置**：想贴着商城就把徽标拖过去，位置会写进配置，下次注入自动恢复。

实现要点（`ggman_core.c`）：

```c
#define BTN_CLASS L"GGmanHudToggle"
#define BADGE_W 32
#define BADGE_H 32
static HWND g_badge;   /* 顶层 layered 窗口，随定时器贴着客户区顶栏 */
// WM_LBUTTONDOWN → SetCapture；WM_MOUSEMOVE 位移>3px 记为拖动；
// WM_LBUTTONUP → 拖动则 badge_save_pos()，否则切换 g_visible 并 reposition()
```

验证（`python -c` 直接查窗口可见性，避免截图时序干扰）：

```
badge at (1252,61,1284,93)  panel visible = True
after click 1: panel visible = False
after click 2: panel visible = True
core.log: badge click -> panel visible=0 / 1
```

截图：`shots/badge-v17-crop.png`（徽标在顶栏空隙）、`shots/badge-toggle-hidden.png`、`shots/badge-toggle-shown.png`。

## 15. agent.ini 新增项

```ini
button=1        ; 是否显示顶栏徽标
btn_dx=-1       ; -1=自动位置；否则为相对客户区左边的像素
btn_dy=-1
```

## 16. v18–v20：闪烁与"徽标满屏乱跑"修复

用户实测反馈：**客户端一直在闪**，且 **徽标能拖出客户端窗口**。逐项定位与修复：

| # | 症状 | 根因 | 修复 |
|---|---|---|---|
| 1 | 画面闪 | 同一 renderer 里堆了多个实例：早期注入的 `ggman_hud_*` + 多次 `ggman_core_*`，每个都建了徽标和面板，互相盖/抢点击 | 所有权闸门（`Local\GGmanHudOwner`）**提前到创建任何窗口之前**，拿不到就不建窗口；`inject_watch.py` 的前缀判断从 `ggman_hud` 改为 `ggman`（任何 ggman 模块存在就跳过），并支持 `agent_procs.py --kill` 清理 |
| 2 | 拖动时抖 | 250 ms 定时器每轮都按锚点 `SetWindowPos`，把正在拖的徽标拽回去 | `badge_reposition()` 开头 `if (g_badge_drag) return;`，拖动期间定时器不干预 |
| 3 | 静态也闪 | 每 250 ms 无条件 `SetWindowPos` + `ShowWindow` + `InvalidateRect` | 变更检测：位置变了才 `SetWindowPos`，可见性变了才 `ShowWindow`，内容脏（`g_dirty`）才重绘 |
| 4 | 徽标自己乱跑、ini 被反复改写 | 幻影拖动：鼠标并未按下也进入拖动分支 | `WM_MOUSEMOVE` 用 `GetKeyState(VK_LBUTTON)` 校验，`WM_CAPTURECHANGED` 清状态，`WM_LBUTTONDOWN` 也先校验按键 |
| 5 | 徽标能拖出窗口 | 只做了锚点钳制，没有拖动钳制 | 拖动中实时钳制到客户区（4px 边距），松手保存时再钳一次，自动锚点也钳 |

验证（脚本直接读窗口矩形，避免截图误判）：

```
auto anchor  -> client (823,55)  badge rel (404,10)          # 顶栏空隙
drag 到屏幕左上角外侧 -> badge rel (204,4)                    # y 被钳到 4
hold 2.5s 不动 -> 位置不变；松手后保存；2s 后仍不变
5 秒稳定性采样 -> badge stable for 5s: True
```

另外 `agent_procs.py --kill` 一次清掉所有带 ggman 模块的 renderer，是"闪到没法用"时的急救手段（客户端会自己重建渲染进程，watcher 再补注入）。

## 17. 一键体检（出问题时按这个顺序查）

```powershell
python tools\agent_procs.py              # 一个 renderer 最多一个 ggman 模块
python tools\hud_cleanup.py --list       # 只应看到 1 个 overlay + 1 个 ipc + 1 个 badge
python tools\agent_procs.py --kill       # 有重复就清场
python tools\inject_watch.py --once      # 重新注入一份
```

## 18. v21：关闭入口 + 唯一实例硬约束

用户反馈两条：**能出现多个悬浮球**、**GGman Agent 窗口关不掉**。

### 18.1 为什么会出现多个悬浮球

| 原因 | 说明 |
|---|---|
| 旧逻辑"等 8 秒强夺所有权" | 第二个实例等不到互斥体也照样建窗口 → 两个徽标、两个面板 |
| watcher 前缀判断写成 `ggman_hud` | 实际模块名是 `ggman_core_*`，判断失效 → 反复注入新副本 |
| 历史注入残留 | 同一个 renderer 里同时挂着 `ggman_hud_*` 和多个 `ggman_core_*` |

修复：

1. **窗口存在性硬门禁**：创建任何窗口前先 `FindWindowW(BTN_CLASS/HUD_CLASS/IPC_CLASS)`，只要桌面上已经有任何一个是自己的窗口，就 `log "stays passive"` 并直接返回——不建窗、不设定时器。
2. **互斥体不再"强夺"**：等 3 秒拿不到就判定"仍有活实例"，同样退出（只有前一个进程真的死了，互斥体变成 abandoned 才会接管）。
3. **watcher 前缀修正** + **复制重试**（ACE/杀软会短暂锁新文件，重试 8 次 × 0.5 s）。
4. `agent_procs.py --kill` 作为急救：清掉所有带 ggman 模块的 renderer。

实测：往两个不同 renderer 各注入一份 →

```
renderer A: 创建徽标+面板
renderer B: "existing ggman windows found; this instance stays passive (no duplicate badge)"
窗口总数保持 3（Toggle / Overlay / IPC）
```

### 18.2 现在有两种关闭方式

| 入口 | 效果 |
|---|---|
| 面板右上角 **×** | 只隐藏面板（徽标还在，点徽标可再打开） |
| 工具页最后一行 **"关闭 agent（移除所有窗口）"** | 销毁徽标 + 面板 + IPC，写 `agent_enabled=0`，watcher 从此不再注入 |

实测：

```
点击 × → core.log: "close button -> panel hidden"，面板 IsWindowVisible=false
工具页点"关闭 agent" → 所有 ggman 窗口消失（windows: NONE），agent.ini: agent_enabled=0
watcher 日志: "agent_enabled=0 in agent.ini; not injecting"
```

恢复方式：

```ini
; D:\project2\ggman\dll\build\agent.ini
agent_enabled=1
```

然后 `python tools\inject_watch.py --once`（或等 watcher 下一轮）。

### 18.3 现在的窗口纪律

- 一个进程最多一套窗口，全桌面最多一套（FindWindow 判据跨进程）
- 面板可以 × 关闭，agent 可以整包退出，退出后 watcher 尊重配置不再骚扰
- 徽标只能在客户端客户区内移动（4px 边距），位置可拖动保存
