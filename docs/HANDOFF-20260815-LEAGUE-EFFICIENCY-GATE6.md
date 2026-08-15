# League Efficiency Gate 6 交接

> Issue #111 / branch `feat/league-efficiency-gate6-111`，stacked 在 Gate 5 候选 `31b5c5a17cada0d5d9106746d360952fe99c33f7` 上。Gate 5 PR #110 必须能独立验收/合并；Gate 6 不得倒灌进 #110。

## 用户目标

- 一局结束后随机给 1 个队友点赞；
- 尽快自动返回大厅，不在结算界面浪费时间；
- 两项均默认关闭，用户在同一个 `游戏效率` 页面开启。

## Akari 当前源码证据

参考 `LeagueAkari/LeagueAkari@cb236b6caf196e2505c7dfa6b34185020fd1e570`：

- ballot read：`GET /lol-honor-v2/v1/ballot/`；
- ballot 有 `eligibleAllies / eligibleOpponents / gameId / votePool.votes`；
- honor write：`POST /lol-honor/v1/honor`，body `{ honorType, recipientPuuid }`；
- ballot submit：`POST /lol-honor/v1/ballot`；
- play again：`POST /lol-lobby/v2/play-again`；
- Akari 当前自动 honor 类别固定为 `HEART`；
- play-again 参考延迟：WaitingForStats 10000ms / PreEndOfGame 3250ms / EndOfGame 1575ms。

FACM 不照搬 Akari“尽量用掉所有票、必要时点赞敌人”的策略。用户需求是**随机队友点赞**，所以 FACM 每局最多选 1 名合法 ally，永不把 opponents 加入候选。

## 设置

`settings.ini` 新增：

- `LeagueAutoHonorTeammateEnabled=False`
- `LeagueAutoReturnLobbyEnabled=False`

旧设置无字段 => 两项默认 false。`ui-text.ini` 仍只负责文字。

## 共享 LCU 与 writer fence

没有第二套 connector。`LeagueClientModule` 继续只创建一个 `LeagueClientSessionProvider`，Gate 2 符文/技能 writer 与 Gate 6 post-game writer 共用该 session provider。

Gate 6 独立 writer 只允许 exact POST：

- `/lol-honor/v1/honor`
- `/lol-honor/v1/ballot`
- `/lol-lobby/v2/play-again`

ready-check accept、matchmaking search、ChampSelect action、其它 PATCH/DELETE 全部在 transport 层拒绝。

## 复用现有 Gameflow

`LeagueDashboardModule` 仅把现有常驻 `LeagueGameflowMonitor` 的 raw state 转发为：

- `CurrentGameflowState`
- `GameflowStateChanged`

没有新建第二个 phase poller。

Gate 6 只认：

- `WaitingForStats`
- `PreEndOfGame`
- `EndOfGame`

离开这三种阶段立即取消 pending task；InProgress / ReadyCheck / ChampSelect / Lobby 均零 Gate 6 写。

## 一次性赛后 cycle

一个连续赛后阶段只启动一个 cycle：

- WaitingForStats -> PreEndOfGame -> EndOfGame 的重复 state event 不会再次点赞/返回；
- 真正离开赛后后才 reset，下一局可再执行一次；
- 同一 cycle 失败不原地无限重试。

自动 honor：

1. 最多约 3.25 秒低频等待 ballot（650ms 间隔，串行）；
2. gameId <= 0 / votes <= 0 / 无合法 ally => skip；
3. allies 过滤 bot、空 puuid、重复 puuid，并 defensively 排除当前 self puuid；
4. 随机选择 1 人；
5. 固定 `HEART`；
6. honor 成功才 submit ballot；
7. 不日志 puuid。

自动 return：

- honor 成功/ballot 已出现：短 500ms buffer 后 play-again；
- honor 失败/无候选仍允许 return；
- return-only 延迟沿用 Akari 当前 10s / 3.25s / 1.575s；
- honor+return 若从 WaitingForStats 开始但 3.25s 内 ballot 一直不出现，只再补 6.75s，保持总兜底约 10s，不叠加成 13s；
- PreEndOfGame/EndOfGame 不额外拖长。

## UI

复用 Gate 5 同一个 `游戏效率` 窗口，不新增托盘项：

- 快捷键
- 赛后
  - `自动随机点赞一名队友`
  - `自动返回大厅`

两个 checkbox 改变即保存，不另加第二个“保存”按钮。

## deterministic smoke

`LeaguePostGameAutomationSmokeTest` + `LeagueEfficiencySmokeTest` 应随 Performance Contract 运行并验证：

- legacy settings 默认 false，true/false parse+serialize；
- post-game transport exact allowlist；
- ready-check/search/ChampSelect actions hard blocked；
- fixture 中 self / duplicate ally / bot / opponent：只能 honor 1 个合法 ally；
- votes > 1 仍最多 1 次 honor；
- honorType = HEART；
- honor success => ballot submit once；
- honor failure => no ballot submit，但 play-again 仍一次；
- 同一连续 postgame event 多次 => 0 additional writes；
- 离开 postgame 再进入 => 新 cycle；
- return-only => 0 honor read，1 play-again；
- InProgress / ReadyCheck / ChampSelect / Lobby 非 postgame。

## CI 说明

仓库现有 PR workflows 只监听 `main` base。Gate 6 是 stacked PR，因此验证时允许把 Draft PR #112 **临时** retarget 到 main，推一个真实的交接/测试提交触发 UI Text / Windows / Mayhem，run 一出现立即把 base 恢复到 `feat/league-efficiency-gate5-109`。不得在临时 main base 上 Ready 或 merge；最终 merge 仍必须遵守 Gate 5 -> Gate 6 顺序。

## 尚未验收的真实点

CI 无法替代腾讯/国服实机：

1. `/lol-honor-v2/v1/ballot/` 当前国服字段形状；
2. `/lol-honor/v1/honor` + `/lol-honor/v1/ballot` 在当前国服客户端是否仍接受；
3. `/lol-lobby/v2/play-again` 在三种赛后 phase 的成功时机；
4. 用户打开两项时实际从水晶爆炸到大厅的体感延迟。

这些必须在 Draft Windows 候选打一局验证后再 Ready/merge。

不创建 Release/Tag，不改 online update，不删任务分支。
