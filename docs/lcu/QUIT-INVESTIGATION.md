# LCU「退出选人 / 返回大厅」专题 —— 实测与根因定位

日期：2026-11-28
环境：国服客户端（`D:\WeGameApps\英雄联盟`），LCU `127.0.0.1:62420`，`phase = None`
方法：`/help` remoting schema（1452 functions / 692 events / 3569 types）+ 路由存在性探测 + WAMP 通道测试

---

## 0. 结论速览

| 结论 | 证据 |
|---|---|
| **ggman 现在打的 `POST /lol-champ-select/v1/session/quit` 在当前版本不存在** | 返回 `404 Invalid URI format`（路由级拒绝，不是会话级） |
| **PvP 选人走的是另一个插件：`lol-lobby-team-builder`** | `GET /lol-lobby-team-builder/champ-select/v1/implementation-active` → `200 false`（路由存在） |
| **正确的 PvP quit 路由存在** | `POST /lol-lobby-team-builder/champ-select/v1/session/quit` → `404 No champ select session in progress`（会话级拒绝 = 路由在） |
| **`POST /lol-gameflow/v1/session/dodge` 存在且只要任意 JSON body** | 不带 body → `400 dodgeData required`；带 `{}` → `204` |
| **LCU 的 WAMP 只能订阅，不能 CALL** | 四种 procedure 格式全超时（`GetX` / `Plugin.Function` / `plugin.route.GET` / 带参） |
| 因此：**退出选人靠的不是"关客户端"，而是三条 HTTP 路径里能通的那条** | 见第 4 节 |

---

## 0.5 真机实测（2026-11-28 13:54，普通匹配局，已跑完）

phase 序列：`Lobby → Matchmaking → ReadyCheck → ChampSelect`，进选人瞬间：

```text
tb_active = true          ← 确认 PvP 走 TeamBuilder 插件
early_exit = false        ← 官方提前退出未开
early_exit_quit = false   ← 官方提前退出-退出未开
leaver = 无惩罚
```

三条路由在同一局内依次实测：

| 路由 | 结果 | 判读 |
|---|---|---|
| `POST /lol-lobby-team-builder/champ-select/v1/session/quit` | **400** `"Current champ select does not allow quitting"` | 路由在，**服务端明确禁止** |
| `POST /lol-gameflow/v1/session/dodge` + `{}` | **204**，但 `phase_after = ChampSelect` | 请求被接受、**动作没发生**（被吞） |
| `POST /lol-champ-select/v1/session/quit` | **404** `"Invalid URI format"` | 路由不存在 |

**gate 字段找到了**（`GET /session` 快照，已存 `lcu-dump/09-quit-probe/snap-tb_session.json`）：

```json
{
  "showQuitButton": false,
  "isCustomGame": false,
  "isLegacyChampSelect": false,
  "queueId": ...,
  "timer": {"phase":"BAN_PICK","adjustedTimeLeftInPhase":72688,"totalTimeInPhase":90000}
}
```

`showQuitButton` 是**服务端下发的**，不是客户端 UI 的疏漏：

- PvP（匹配/排位）→ `false` → 界面不给按钮，且 `session/quit` 被 400 拒绝
- 自定义/训练 → `true` → 按钮出现，quit 可用（这就是 11/25 那次 `204` 能成功的原因）

同时 `gameflow/session` 里 `gameDodge = {"dodgeIds":[],"phase":"None","state":"Invalid"}`，`queue.removalFromGameAllowed = false`。

### 结论

**PvP 选人期间，LCU 侧不存在任何"不关客户端就能退出"的通道。** 这不是缺端点，是服务端把 `showQuitButton` 关掉并拒了 quit。玩家唯一手段就是关客户端——断连后由服务端判 dodge。

所以「一键秒退」的正确形态不是调 quit 端点，而是：**一键让客户端断连（等效关客户端）+ 自动重登回大厅**。

## 0.6 对照实验（2026-11-28 14:24，训练模式 vs PvP）—— 根因闭合

同一账号、同一客户端、同一端点、同一个插件，只有队列类型不同：

| 项 | 训练模式（queueId=3140） | PvP 匹配（queueId 见 snap） |
|---|---|---|
| `implementation-active` | `true` | `true` |
| `isCustomGame` | `true` | `false` |
| **`showQuitButton`** | **`true`** | **`false`** |
| `POST /lol-lobby-team-builder/champ-select/v1/session/quit` | **204** | **400 `Current champ select does not allow quitting`** |
| phase 结果 | **1.2 秒后 → `Lobby`** | 不动，停在 `ChampSelect` |
| `leaver/notifications` | `[]`（无惩罚） | 产生裁决之镰（5 分钟 → 30 分钟） |

**结论：客户端侧能力是完整的，服务端按队列类型把 PvP 的 `showQuitButton` 关掉了。**

- 训练/自定义：可以正常"返回大厅"，零惩罚，一条 HTTP 就够；
- PvP：端点存在、插件相同、参数相同，唯一变量是服务端下发 `false`，于是 400；
- 因此"进选人没有返回大厅按钮"是**服务端策略**，不是客户端缺陷；玩家在 PvP 里能做的只有超时或断连，两者都记 dodge。

snap 证据：`lcu-dump/09-quit-probe/snap-training-session.json`（训练）与 `snap-tb_session.json`（PvP）。

### 对 ggman 的落点

1. **训练/自定义/部分模式**：直接实现"返回大厅"按钮 → `POST /lol-lobby-team-builder/champ-select/v1/session/quit`（已验证 204 + 回大厅 + 零惩罚）；
2. **PvP**：按钮可以画，但点了只能走"断连"语义，必须明确提示"会记裁决之镰"，不能假装无惩罚；
3. 实现上按 `session.showQuitButton` 决定按钮行为：
   - `true` → 调 quit（干净退出）
   - `false` → 弹确认"本次退出将计为秒退（裁决之镰 X 分钟）"，再执行断连或直接禁用按钮。

## 0.7 杀主进程实测（2026-11-28 14:40，PvP 匹配 430）—— 最终结论

前提：`showQuitButton=false`、`isCustomGame=false`、`queueId=430`、`gameId=500959584712`（确认是真 PvP 选人）。

动作：结束 `LeagueClient.exe`（主进程，pid 11368）。

逐秒观察（脚本 `tools/retry_kill_test.py`，日志 `retry-20261128-143935.jsonl`）：

```text
t+1～16s   procs = (空)                     phase = no-session    ← 全链退出
t+17s      procs = RiotClientServices        phase = no-session    ← 自己回来了
t+18s      procs = LeagueClient, Riot...     phase = no-session
t+20s      procs = LeagueClient, Riot...     port=49881 连接拒绝    ← LCU 还没起
t+21s      procs = LeagueClient, Ux, Riot... phase = None          ← 回到大厅起始页
```

**结果：杀 `LeagueClient` 会让整条链瞬间全灭（Ux / UxRender / RiotClientServices 一起没），但 17 秒后 `RiotClientServices` 会自己回来，21 秒左右 UX 起、`phase = None`。** WeGame 全程没退（pid 13040）。

惩罚：`leaver/notifications = []`、`ranked-restriction = 无`、`lowPriorityData.penaltyTimeRemaining = 0` —— **本次没有留下 dodge 惩罚记录**（单次样本，可能与"客户端被判定为崩溃"有关）。

### 这一条推翻的假设与留下的真相

| 假设 | 实测 |
|---|---|
| 杀 `LeagueClient` → 只断连接、UI 还在 | **否**：整链退出 |
| 杀 `LeagueClient` → 永不恢复，必须手动重开 | **否**：约 17–21 秒自动恢复（RiotClientServices 守护拉起） |
| 杀 `LeagueClient` → 直接回 Lobby | **否**：回到 `phase = None`（大厅起始页），要再点进大厅 |
| 杀进程会被记一次 dodge | 本次：**未记**（需多样本确认） |

### 最终结论：PvP 选人没有任何"干净退出"

```
PvP 选人期间可行路径（穷尽）
├─ 主动 quit 端点        → 服务端 400（showQuitButton=false）
├─ dodge 端点            → 204 但 phase 不动
├─ 断网 5/15 秒          → phase 不动（15 秒那次的变化实为选人超时）
├─ 杀 UX（process/quit） → 窗口消失、连接仍在、服务烂尾
└─ 杀主进程              → 整链退出 → 17~21 秒自动恢复 → phase=None（本次无惩罚）
```

市面辅助那个"退出房间"按钮，效果等于最后一行的**封装版**：一键杀，然后等 Riot Client 自己把客户端拉起来。用户体感是"点一下 → 客户端消失 → 几十秒后回到大厅"，而不是真正的"瞬间返回"。

### 对 ggman 的最终建议

1. **训练/自定义**：`showQuitButton=true` → `POST /lol-lobby-team-builder/champ-select/v1/session/quit`，204、1.2 秒回大厅、零惩罚 —— 这条完全可用，值得做成按钮；
2. **PvP**：做成「秒退（重开客户端）」按钮，动作 = 结束 `LeagueClient` → 等 `RiotClientServices` 自动回来（约 20 秒）→ 等 `phase` 变 `None/Lobby`；UI 上标明"约 20–30 秒，期间客户端会消失"；
3. 不要承诺"无惩罚""不关客户端"——PvP 里这两条都不成立。

## 0.8 插件 JS 逆向结果（2026-11-28 15:15）—— 按钮的唯一实现

**怎么拿到的**：磁盘上 0 个 `.js`（WAD 是 zstd 资源包、pak 是 CEF service worker），
于是用 Frida 在 `LeagueClientUx.exe` 的 `rw-` 内存段里扫特征字符串，dump 命中处上下文。
产物：`lcu-dump/12-mem-scan/14116-session_quit.txt`、`4476-showQuitButton.txt`。

### 退出按钮的 action（源码原文，压缩后的构建产物）

```js
e.exports = l.Component.extend({
  classNameBindings: ["isSpectating:quit-spectate-button:quit-button"],
  layout: n(142),
  actions: {
    quitCustom: function () {
      o.startOperation(o.XP_CGL_PREGAME_CHAMP_SELECT_QUIT);
      const e = this.get("isLegacyChampSelect") && this.get("isSpectating")
        ? "/lol-lobby/v1/lobby/custom/cancel-champ-select"
        : "/lol-lobby-team-builder/champ-select/v1/session/quit";
      a.default.ajax({ type: "POST", url: e })
        .then(() => { ... })
        .catch((e) => { ... });
    }
  }
})
```

源码路径（构建期 VFS 路径，可直接用于继续挖其它插件）：

```
T:\vfs\mount-Releases\16.23\DevRoot\Client\fe\rcp-fe-lol-champ-select\src\app\quit-button-component\index.js
T:\vfs\mount-Releases\16.23\DevRoot\Client\fe\rcp-fe-lol-champ-select\src\app\quit-button-component\layout.hbs
```

### 模板（Glimmer AST）

```js
["open-element","lol-uikit-flat-button"],
["dynamic-attr","onclick",["helper",["action"],[["get",[null]],"quitCustom"],null],null],
["dynamic-attr","disabled",["unknown",["disabled"]],null],
```

### 显隐与置灰逻辑

```js
showQuitButton: Ember.computed("session.showQuitButton","isNotSpectating","isDropInSpectator",
  function () {
    return this.get("session.showQuitButton")
        && (this.get("isNotSpectating") || this.get("isDropInSpectator"));
  }),

disableQuitButton: Ember.computed("session.timer.timerAvailable","session.timer.inFinalizationPhase",
  "session.timer.inGameStartingPhase","isUILockedForGameStart","isDropInSpectator",
  function () { ... })
```

按钮挂在 `.bottom-right-buttons` 容器，模板里是 `["block",["if"],[["get",["showQuitButton"]]],...]`。

### 结论（代码级，不再是推测）

**客户端退出按钮的唯一实现就是 `POST /lol-lobby-team-builder/champ-select/v1/session/quit`**
（观战+旧实现时才换 `POST /lol-lobby/v1/lobby/custom/cancel-champ-select`）。

- 没有内部 RPC、没有 WebSocket 提交、没有隐藏端点；
- 显隐 100% 由服务端下发的 `session.showQuitButton` 决定；
- 所以 PvP 里那个 400 `Current champ select does not allow quitting` 就是终点：
  **按钮即使被强行画出来，点下去也只是再吃一次同样的 400。**

这解释了市面辅助"有按钮但会秒退惩罚"的全部成因：它们绕过的只是 UI 显隐，绕不过服务端的 gate。

## 0.9 `request-lobby` 实测（2026-11-28 15:29，PvP 430）—— 也被 gate

来源：FACM 3.5.90 的 `LeagueChampSelectQuitRuntime.RequestLobbyPath`
= `POST /lol-gameflow/v1/session/request-lobby`（原代码把它当 quit 的回退路径）。

```
GET  /lol-gameflow/v1/session/request-lobby   → 405 WRONG_METHOD   （路由存在）
phase=None  时 POST                            → 200  body = "true"
PvP 选人时   POST                              → 200  body = "false"
                 随后 20 秒：phase 始终 ChampSelect
```

**`200` 只表示请求被受理，body 的布尔值才是服务端的回答**：`true` = 已在大厅/允许回，`false` = 不允许。

结论：`request-lobby` 在 PvP 选人期同样被服务端拒绝（返回 false），不能作为退出通道。

### 顺带发现 FACM 3.5.90 的一个判断缺陷

`LeagueChampSelectQuitRuntime.TryRequestLobbyAsync()` 返回的是 `LeagueClientWriteResponse`
（`StatusCode` + `Body` 字节）。如果调用方只看 `StatusCode == 200`，会把这里的 `false`
当成成功——实际什么都没发生。正确判断是 `Body` 解析为 `true`。

## 1. 三条路由的对照实验（已跑，可复现）

```text
POST /lol-champ-select/v1/session/quit
  -> 404 {"errorCode":"RESOURCE_NOT_FOUND","message":"Invalid URI format"}      ← 路由不存在

POST /lol-lobby-team-builder/champ-select/v1/session/quit
  -> 404 {"errorCode":"RPC_ERROR","message":"No champ select session in progress."}  ← 路由存在

POST /lol-gameflow/v1/session/dodge          (无 body)
  -> 400 {"errorCode":"RPC_ERROR","message":"A value for 'dodgeData' is required."} ← 路由存在，缺 body

POST /lol-gameflow/v1/session/dodge          (body = {})
  -> 204                                                                          ← 接受
```

判据说明（很关键，后面都要用）：

- `RESOURCE_NOT_FOUND / Invalid URI format` = **路由没注册**
- `RPC_ERROR / No champ select session in progress`、`No active delegate`、`LOBBY_NOT_FOUND` = **路由在，只是当前没有会话**

## 2. 为什么「自定义/训练能退、PvP 不能」

国服客户端里并存两套选人实现：

| 实现 | 插件 | HTTP 前缀 | 用于 |
|---|---|---|---|
| 旧（ChampSelect） | `lol-champ-select` | `/lol-champ-select/v1/*` | 自定义、训练、部分特殊模式 |
| 新（TeamBuilder） | `lol-lobby-team-builder` | `/lol-lobby-team-builder/champ-select/v1/*` | **匹配 / 排位（PvP）** |

`GET /lol-lobby-team-builder/champ-select/v1/implementation-active` 这个开关就是判据：PvP 选人时它会变成 `true`，此刻旧命名空间里的 delegate 不在，所以 `session/quit` 返回 `No active delegate`，ggman 就掉进 `dodge` 回退。

ggman 在 11/25 记录的「`session/quit → 204`」是在**自定义对局**里验的，那条路由当时属于旧插件；PvP 换插件后就打空了。代码位置：

```
dll/ggman_core.c:1087   lcu_http("POST", "/lol-champ-select/v1/session/quit", ...)   ← 自动秒退
dll/ggman_core.c:1089   lcu_http("POST", "/lol-gameflow/v1/session/dodge", ...)      ← 回退
dll/ggman_core.c:2539   lcu_http("POST", "/lol-champ-select/v1/session/quit", ...)   ← 手动秒退
dll/ggman_core.c:2541   lcu_http("POST", "/lol-gameflow/v1/session/dodge", ...)
```

## 3. PvP 选人真实路由树（19 条，已探测存在）

前缀 `/lol-lobby-team-builder/champ-select/v1`：

```text
GET    /session                        GET  /session/timer
GET    /session/my-selection           GET  /session/champion-swaps
GET    /session/position-swaps         GET  /session/pick-order-swaps
PATCH  /session/actions/{id}           POST /session/actions/{id}/complete
POST   /session/bench/swap/{championId}
POST   /session/champion-swaps/{id}/accept|decline|cancel|request
POST   /session/pick-order-swaps/{id}/accept|decline|cancel|request
POST   /session/position-swaps/{id}/accept|decline|cancel|request
GET    /pickable-champion-ids          GET  /bannable-champion-ids
GET    /disabled-champion-ids          GET  /current-champion
GET    /implementation-active          GET  /has-auto-assigned-smite
GET    /preferences                    GET  /session/obfuscated-puuids
POST   /session/quit                   ← 本次目标
```

实测细节：

- `/session/actions/0` → `405 WRONG_METHOD`（说明该路径只接 PATCH）
- `/preferences` → `200 {"skins":{"157":157000},"spells":{"430":[14,4]}}`（证明这是活路由）
- `/session/obfuscated-puuids` → `200 []`（同上）
- `/session/quit` → `404 No champ select session in progress`（同上，只差一局选人）

## 4. 三条候选路径与优先级

| 优先级 | 路径 | 依据 | 风险 |
|---|---|---|---|
| **1** | `POST /lol-lobby-team-builder/champ-select/v1/session/quit` | 路由存在；PvP 专属插件自带 quit 函数 `PostLolLobbyTeamBuilderChampSelectV1SessionQuit` | 若服务端 gate，退化成 dodge（同样有惩罚，但动作成立） |
| **2** | `POST /lol-gameflow/v1/session/dodge` + body `{}` | 路由存在，body 只要 `{}` | 直接走 dodge 语义，惩罚明确 |
| 3 | `POST /lol-champ-select/v1/session/quit` | **路由不存在** | 保留仅在旧插件激活时（自定义/训练）有意义 |

另外两个还没读的开关，进选人后要立刻记录：

```
GET /lol-gameflow/v1/early-exit-enabled        ← 当前 false
GET /lol-gameflow/v1/early-exit-quit-enabled   ← 当前 false
GET /lol-gameflow/v1/game-exit-early-vanguard  ← 当前 0
GET /lol-leaver-buster/v1/ranked-restriction   ← 当前 needsAck=false, punishedGamesRemaining=0
```

`early-exit-quit-enabled` 如果 PvP 选人时变 `true`，说明官方开放了「提前退出」通道，`POST /lol-gameflow/v1/early-exit` 才是正解。这三个值必须在选人瞬间读。

## 5. 验证脚本

`tools/lcu_quit_probe.py` —— 进选人自动触发，记录 phase 前后 + 事件序列 + 每条路由的响应。

```powershell
$py = "C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe"
cd D:\project2\ggman

# 第 1 步：零成本，只观察（训练/自定义即可跑通链路）
& $py tools\lcu_quit_probe.py --watch

# 第 2 步：普通匹配里试 dodge（惩罚是时间锁，不扣 LP）
& $py tools\lcu_quit_probe.py --try dodge

# 第 3 步：普通匹配里试 team-builder quit（首选）
& $py tools\lcu_quit_probe.py --try quit

# 一次性全试（会真的退出，建议只在匹配里用）
& $py tools\lcu_quit_probe.py --try all --delay 3
```

日志：`lcu-dump/09-quit-probe/quits-<时间戳>.jsonl`，每条含 `phase_before / status / response / phase_after / early_exit` 快照。
判读：

- 若 `status = 204` 且 `phase_after` 从 `ChampSelect` 变成 `Lobby`/`None` → **这条就是答案**，服务端接受，惩罚按 dodge 记；
- 若 `status = 4xx` 且 phase 不变 → 被 gate，换下一条；
- 若 `status = 204` 但 phase 不变 → 请求被吞，需要 WAMP 事件里看有没有 `dodge` 相关事件。

## 6. ggman 侧要改的（等验证通过再动）

1. `ggman_core.c:1087`、`2539` 主路径改为 `/lol-lobby-team-builder/champ-select/v1/session/quit`；
2. 旧路径 `/lol-champ-select/v1/session/quit` 降为**第二**回退（自定义/训练仍走它）；
3. `dodge` 保留第三回退，body 继续用 `{}`（已验证 LCU 接受）；
4. 秒退前先读 `GET /lol-lobby-team-builder/champ-select/v1/implementation-active`：
   - `true` → 走 TeamBuilder 路径
   - `false` → 走旧路径
   这样两套插件都覆盖，不用盲试；
5. 秒退判定条件补一条：`early-exit-quit-enabled == true` 时优先 `POST /lol-gameflow/v1/early-exit`。

## 7. 还没答案的部分

| 问题 | 怎么拿答案 |
|---|---|
| PvP 选人时 `early-exit-quit-enabled` 是 true 还是 false | `--watch` 进一次匹配即可 |
| team-builder quit 在 PvP 里是否被服务端接受 | `--try quit` 一次 |
| 退出后惩罚是「时间锁」还是「LP + 时间锁」 | 退出后立刻读 `ranked-restriction` + 结算页 |
| 「关闭客户端」和 quit 的惩罚是否等价 | 同上，对比两种退法的 restriction 字段 |
| 是否存在更"干净"的返回大厅（不记 dodge） | 现有 1452 个函数里没有任何 `leave-lobby-without-penalty` 性质的路由；`early-exit` 是唯一例外，取决于开关 |

## 8. 这次顺带确认的两件事

1. **LCU 没有 swagger**：`/swagger/v3/openapi.json` 等 5 个候选全 404；权威 schema 在 `GET /help`（245 KB），含 `events/functions/types` 三棵表。`lcu_dump.py` 的 `--phase api` 应改为解析 `/help` 的 `functions`（1452 条）而不是等 swagger。
2. **WAMP 单向**：订阅可用（`[5,"OnJsonApiEvent"]` + 各种 `OnJsonApiEvent_<route>` 细分事件，692 个），CALL 不可用。想让 ggman 调内部 remoting，只能在注入进程内直接调 plugin 导出函数，或者继续走 HTTP。
