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

明确拒绝 ready-check decline、matchmaking DELETE/cancel、lobby create/delete、ChampSelect actions、Gate6 honor/play-again 和任意其它 method/path。

## 自动寻找对局

只在 setting ON + gameflow `Lobby` 时运行自己的轻量 eligibility observer：进入 Lobby 先等约 1.5 秒，此后约 3 秒一次读取本地 lobby，非 Lobby 立即取消。其它 phase 不运行这条 observer。

必须同时满足：`canStartActivity`、`localMember.allowedStartActivity`、`localMember.isLeader`、queueId>0、至少一个真实 member、restrictions/warnings 为空、partyId 有效，且 matchmaking search 不是 already in queue。

fingerprint = `partyId | queueId | sorted unique member puuids`。同 fingerprint 最多 POST search 一次；失败不对同 fingerprint 狂重试；成员/队列/party 变化形成新 fingerprint 才允许新尝试；成功后 observer 停止。

## 自动接受

只在 setting ON + gameflow `ReadyCheck`：进入后先等约 450ms，最多 4 次、每约 350ms 确认 `/lol-matchmaking/v1/search`。`readyCheck.state` 必须为 `InProgress`；playerResponse 已 `Accepted` 或 `Declined` 时零写。

fingerprint = `lobbyId | queueId | readyCheck.state`，同 fingerprint 最多 POST accept 一次。离开 ReadyCheck 立即取消；用户 Declined 后 FACM 不反向接受。

## UI

复用同一个 `游戏效率` 窗口，不新增 tray entry：快捷键 / 赛后 / 自动下一局。第三块只有 `自动寻找对局` 和 `自动接受对局` 两个 checkbox，改变即保存。窗口可滚动，避免小屏硬挤。

## deterministic smoke

`LeagueMatchmakingAutomationSmokeTest` 随 Performance Contract，覆盖：legacy settings 默认 OFF + persistence；writer only allows search+accept；decline/DELETE search/ChampSelect/Gate6 play-again hard blocked；eligible Lobby exactly-one search；same fingerprint no repeat；member change one new attempt；各类 Lobby blocker；already queued zero search；eligible ReadyCheck exactly-one accept；same fingerprint no repeat；非 InProgress / Accepted / Declined / missing fingerprint zero accept；非 Lobby/ReadyCheck observations zero Gate7 read/write。

## CI 说明

仓库 PR workflows 只监听 `main` base。PR #114 是 stacked PR，因此验证时只允许在 **Draft** 状态临时 retarget 到 main，通过本交接文档的真实更新触发 UI Text / Windows / Mayhem；run 一出现立即恢复 base 到 `feat/league-efficiency-gate6-111`。临时 main base 绝不 Ready/merge。

## 腾讯实机待验证

CI 不能替代：腾讯 Lobby JSON 当前字段；restrictions/warnings 正常形状；matchmaking search POST 时机；`/lol-matchmaking/v1/search` 的 ReadyCheck state 是否仍为 `InProgress`；accept endpoint；用户主动 Decline 后保持零反向写。

腾讯实机通过前 Draft，不 Ready、不合并。不创建 Release/Tag，不修改 online update，不删除任务分支。
