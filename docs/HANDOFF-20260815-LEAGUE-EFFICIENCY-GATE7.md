# League Efficiency Gate 7 交接

> Issue #113 / branch `feat/league-efficiency-gate7-113`，stacked 在 Gate 6 候选 `6b87adbe8c0aaa1a0f9999c844e37cb52d55ce40` 上。Gate 5 -> Gate 6 -> Gate 7 必须保持顺序；本 Gate 不倒灌到前两个 PR。

## 目标

在同一个 `游戏效率` 页面增加第三块“自动下一局”：

- 自动寻找对局；
- 自动接受对局。

两项默认 OFF、改变即保存。第一版不开放延迟秒数、不做自动建房/切队列/邀请/踢人/转房主。

## Akari 当前源码证据

参考 `LeagueAkari/LeagueAkari@cb236b6caf196e2505c7dfa6b34185020fd1e570`：

- `GET /lol-lobby/v2/lobby`；
- Lobby 包含 `canStartActivity`、`localMember.allowedStartActivity`、`localMember.isLeader`、`gameConfig.queueId`、`partyId`、members、restrictions/warnings；
- `POST /lol-lobby/v2/lobby/matchmaking/search`；
- `GET /lol-matchmaking/v1/search`；
- matchmaking search 包含 `readyCheck.state / playerResponse / lobbyId / queueId / isCurrentlyInQueue`；
- `POST /lol-matchmaking/v1/ready-check/accept`；
- Akari auto accept 仅在 ReadyCheck phase 计划执行并在离开 ReadyCheck 时取消。

## 设置

`settings.ini` 新增：

- `LeagueAutoMatchmakingEnabled=False`
- `LeagueAutoAcceptEnabled=False`

旧配置无字段安全落到 false。`ui-text.ini` 仍只负责可见文案。

## 唯一 LCU session + Gate 7 writer fence

`LeagueClientModule` 继续只创建一个 `LeagueClientSessionProvider`。Gate2/Gate6/Gate7 各自 writer 独立 allowlist，但共享 session。

Gate7 writer exact POST only：

- `/lol-lobby/v2/lobby/matchmaking/search`
- `/lol-matchmaking/v1/ready-check/accept`

明确拒绝：

- ready-check decline；
- matchmaking DELETE/cancel；
- lobby create/delete；
- ChampSelect actions；
- Gate6 honor/play-again；
- 任意其它 method/path。

## 自动寻找对局

只在 setting ON + gameflow `Lobby` 时运行自己的轻量 eligibility observer：

- 进入 Lobby 先等约 1.5 秒；
- 此后约 3 秒一次读取本地 lobby，非 Lobby 立即取消；
- 不在 Desktop/Queueing/ReadyCheck/ChampSelect/InGame/postgame 运行此 observer。

必须全部满足：

- `canStartActivity == true`；
- `localMember.allowedStartActivity == true`；
- `localMember.isLeader == true`；
- `queueId > 0`；
- 至少一个非 bot / 非 spectator 且有 puuid 的 member；
- restrictions / warnings 均为空（第一版 conservative fail-closed）；
- `partyId` 有效；
- matchmaking search 不是 already in queue。

fingerprint = `partyId | queueId | sorted unique member puuids`。

- 同 fingerprint 最多 POST search 一次；
- POST 失败不对同 fingerprint 狂重试；
- member / queue / party 变化形成新 fingerprint 才允许新尝试；
- search 成功后 observer 停止，随后 gameflow 应进入 Queueing；
- 离开 Lobby 会 reset，下一次 Lobby 可以重新工作。

## 自动接受

只在 setting ON + gameflow `ReadyCheck`：

- phase 进入后先等约 450ms；
- 最多 4 次、每约 350ms 确认 `/lol-matchmaking/v1/search`，总等待很短；
- `readyCheck.state` 必须是 `InProgress`；
- `playerResponse` 如果已 `Accepted` 或 `Declined`，零写；
- fingerprint = `lobbyId | queueId | readyCheck.state`；
- 同 fingerprint 最多 POST accept 一次；
- 离开 ReadyCheck 立即取消 pending；
- 用户 Declined 后 FACM 不反向接受。

## UI

复用同一个 `游戏效率` 窗口，不新增 tray entry：

1. 快捷键；
2. 赛后；
3. 自动下一局：
   - `自动寻找对局`
   - `自动接受对局`

窗口可滚动，避免小屏硬挤；开关改变即保存。

## deterministic smoke

`LeagueMatchmakingAutomationSmokeTest` 随 Performance Contract：

- legacy Gate7 settings 默认 OFF + true persistence；
- writer only allows search + accept；
- decline / DELETE search / ChampSelect / Gate6 play-again hard blocked；
- eligible Lobby -> exactly one search；
- repeated same fingerprint -> 0 additional search；
- member change -> one new eligible attempt；
- canStart false / allowed false / not leader / queueId 0 / no real member / restriction -> 0；
- already in queue -> 0；
- eligible ReadyCheck -> exactly one accept；
- same ReadyCheck fingerprint -> 0 additional accept；
- state not InProgress / Accepted / Declined / missing fingerprint -> 0；
- InProgress/ChampSelect/EndOfGame observations -> zero Gate7 read/write。

## 腾讯实机待验证

CI 不能替代：

1. 腾讯 Lobby JSON 当前字段与 Akari 类型是否一致；
2. restrictions / warnings 正常空值形状；
3. `/lol-lobby/v2/lobby/matchmaking/search` 当前国服 POST 成功时机；
4. `/lol-matchmaking/v1/search` 的 ReadyCheck state 是否仍为 `InProgress`；
5. accept endpoint 当前国服是否成功；
6. 用户主动 Decline 后是否能稳定观察为 Declined 并保持零反向写。

腾讯实机通过前 Draft，不 Ready、不合并。不创建 Release/Tag，不修改 online update，不删除任务分支。
