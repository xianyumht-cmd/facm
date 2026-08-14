# FACM 当前项目状态

> 2026-08-14：FACM 3.2.0 是当前正式生产基线。Performance Contract、League Dashboard Gate 1、Player Gate 1、Champ Select / Current Game Gate 1、Player Gate 2 均已完成并合入 `main`。
>
> 当前 League 规划进度：**4/5（80%）**。当前主线是 **Tools / Automation Gate 1：OP.GG 对局助手（只读推荐）**，Issue #93 / Draft PR #94 已有 Windows 候选，等待腾讯/国服实机验收。完整历史交接可参考 `docs/HANDOFF-20260814-LEAGUE.md`，但当前状态以本文件和 GitHub 实时状态为准。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 本轮没有创建新 Release、没有改 `online/version.json`、没有改变线上版本。

## 当前未完成：Tools / Automation Gate 1

- Issue #93：OPEN。
- Draft PR #94：OPEN / Draft / mergeable；腾讯/国服实机通过前保持 Draft，不合并。
- branch：`feat/opgg-build-advisor-93`。
- base：`main` @ `1c3afae6be81759a0a708e438641719215e4f4f6`。
- 行为候选 HEAD：`a3d84121ccb6c89a972d364c8a77ef3d8bc06568`。
- Windows Build #971：SUCCESS；Release build、`FACM.exe` 验证、Performance Contract 与 `LeagueBuildAdvisorSmokeTest` 均通过。
- UI Text Contract #92：SUCCESS。
- Mayhem Source Probe #243：SUCCESS。此前 #242 曾因旧 live Mayhem 源临时返回 `Ranked augment queue is incomplete` 失败；未修改 Mayhem 代码，下一 HEAD #243 自动恢复为 SUCCESS。
- artifact：`FACM-Windows-x64-971`，artifact id `9224683838`，未过期。
- artifact ZIP SHA-256：`6386DB56C48D106785114B2FC60EA13062162502D3B0591D730C66B6B7A85824`。
- **腾讯/国服实机尚未验收；正式完成进度仍保持 4/5 = 80%。**

### Gate 1 参考与产品边界

本 Gate 同时参考 League Akari 源码与 OP.GG 产品/公开能力，但不机械复制任一实现：

- Akari 源码用于确认 `opgg-window`、OP.GG 构筑数据结构、LCU 本地 game-data 路径以及写入能力的技术边界；
- OP.GG 产品/官方公开能力用于校验用户价值：英雄胜/选/禁率、装备、符文、技能、召唤师技能和 Counter 等确实属于成熟对局辅助能力；
- FACM 继续使用 C#/.NET Windows 架构，复用唯一 `LeagueClientModule + PerformanceModule`；不新增第二套 LCU connector；
- FACM 已有 Mayhem OP.GG 网络/容灾经验，本 Gate 借鉴其短超时、缓存、取消和失败降级原则，但不反向依赖 Mayhem 模块。

### Gate 1 当前行为

- 新增 `LeagueBuildAdvisorModule`，托盘入口位于“实时对局”之后；
- 上下文直接复用 `LeagueLiveDataService` 的 Gameflow / Champ Select 解析边界，读取本地玩家 `championId`、预选 intent、位置、queue/mode；
- OP.GG 构筑数据使用 `https://lol-api-champion.op.gg` 的 global 数据；腾讯没有单独 OP.GG China 统计时，UI 明确显示 **OP.GG Global**，不冒充“国服胜率”；
- LCU 本地 `/lol-game-data/assets/v1/champion-summary.json`、`items.json`、`summoner-spells.json`、`perks.json` 只用于把 ID 映射成客户端本地名称；不加载英雄/装备图片；
- 第一版展示：Tier/rank（有数据时）、胜率/选取率/Ban 率、召唤师技能、符文、出门装、鞋、核心装备、技能加点、Counter；
- 同一 champion/mode/position/version 构筑缓存 10 分钟；本地静态元数据和 OP.GG version 缓存 30 分钟；
- OP.GG 网络请求只在助手窗口可见且处于需要推荐的上下文发生；相同 key 命中缓存，不按行/装备/符文 fan-out；
- In Game 强制 cache-only：允许显示本局已经缓存的推荐，但不新增 OP.GG 请求，也不加载新的本地静态表；
- 页面关闭立即取消；刷新串行；OP.GG 不可用/字段异常/超时时稳定降级，League 客户端主链不受影响；
- deterministic smoke 覆盖 OP.GG JSON 解析、本地名称映射、global/mode/position、10 分钟缓存、英雄变化仅请求新 build、In Game 零新增 OP.GG 请求、取消、无 match-history/scouting fan-out 和托盘依赖。

### Gate 1 明确禁止

本 Gate 的“Automation”仅指 **自动识别当前上下文 + 自动切换只读推荐**，不执行客户端写入：

- 不 auto accept；
- 不自动 pick / ban；
- 不 swap / reroll / dodge；
- 不自动修改召唤师技能；
- 不自动写符文页；
- 不自动写装备集；
- 不改皮肤或其它客户端设置；
- 不做 teammate match-history fan-out / 玩家侦察；
- 不做游戏内 Overlay；
- 不注入游戏进程。

如果未来需要“一键应用符文 / 技能 / 装备集”，必须作为独立 Gate，要求显式用户触发、明确预览/替换语义、零自动误操作，并单独做腾讯/国服实机验收。

### Gate 1 下一步验收

Build #971 Windows 腾讯/国服集中验证：

1. Lobby：托盘出现“OP.GG 对局助手”，打开顺畅；未进入选人时只显示等待状态，不修改客户端；
2. Champ Select：选择或预选英雄后，当前英雄、模式/位置、OP.GG Global、数据版本与推荐内容能自动跟随；
3. 推荐应尽量显示本地名称，而不是纯 ID：召唤师技能、符文、出门装、鞋、核心装备、技能加点、Counter；某字段缺失时允许稳定少显示，不能拖垮页面；
4. 切换英雄后推荐跟随变化；重复相同英雄不应持续重拉 OP.GG；
5. 明确确认整个过程没有自动改符文、召唤师技能、装备集、pick/ban 等客户端配置；
6. 选人过程保持流畅，无明显点击/切换/输入延迟；
7. 进入 In Game 后助手只允许显示已有缓存或“暂无缓存”，不继续发新的 OP.GG 网络请求；
8. 关闭助手后请求停止，无残留轮询。

用户明确反馈“正常/没问题”后，fresh-check PR #94 HEAD 与 CI，再 Ready/merge；有 bug 则保持 Draft，只修 scoped bug。

## 最新完成行为：Player Gate 2

Player Gate 2 已完成腾讯/国服实机验收并合入 `main`：

- Issue #90：completed。
- PR #91：merged。
- 最终验收候选 HEAD：`24b9db09bc50d0d8490fa46bdd303d59a0f1583a`。
- merge commit：`1ae84844feddddda91226867172ff93c9cb8d5aa`。
- Windows 腾讯/国服：Build #961 已验证本地中文英雄名称映射与当前已加载战绩聚合；随后按实机截图反馈修正 Player 文案。
- 最终 Build #965：用户实机截图确认 `英雄` 列、`英雄表现（当前已加载 2 场）` 标题、中文英雄名称与两场当前战绩统计正常。
- 最终候选 CI：Windows Build #965 / UI Text Contract #86 SUCCESS。
- merge 后 main：Windows Build #966 / UI Text Contract #87 SUCCESS。
- 本 Gate 没有修改 Mayhem 源码，因此本轮没有新增 Mayhem Source Probe run；Windows Build 中的 Performance Contract / `LeaguePlayerSmokeTest` 已通过。
- 最终 artifact：`FACM-Windows-x64-965`，artifact id `9221920312`。
- artifact ZIP SHA-256：`D273D4834B1B20E23D601683EFABFB1BCEB783FFFAA0DE03B5CF76BF678615C9`。
- 构建 FACM.exe 版本：`3.2.0.0`；开发自签名行为未改变。

### Player Gate 2 冻结边界

在 Player Gate 1 基础上只做了渐进增强：

- champion ID 通过本地 LCU `/lol-game-data/assets/v1/champion-summary.json` 映射客户端本地英雄名称；
- 英雄表是一次性汇总请求，内存缓存 30 分钟，不按每场/每英雄 fan-out；
- Queueing / Champ Select / In Game / hidden-background 性能档不会启动该非必要元数据请求；已有缓存仍可用于显示；
- 元数据不可用或字段变化时稳定 fallback 到 champion ID，不影响账号/战绩主链；
- 表现统计只基于当前已经加载的 10/20 场，不追加 match-history；
- 统计包含英雄样本场次、胜率、平均 K/D/A；
- UI 使用轻量有限 ListView，没有英雄图片、动态图表或后台资产预取；
- Player 英雄列默认文案为 `英雄`，统计标题为 `英雄表现（当前已加载 {0} 场）`；旧 `ui-text.ini` 中遗留的 `英雄 ID` 显示兼容，不要求用户手动删除配置；
- 保留 Gate 1 的账号缓存、45 秒战绩页缓存、10→20 明确分页、页面关闭取消和敏感阶段 0 战绩详情预取边界；
- deterministic smoke 覆盖英雄表解析/映射、30 分钟缓存、敏感阶段 0 英雄元数据请求、纯内存统计、0 新增 match-history fan-out、取消和原分页边界。

没有真实缺陷或新独立需求时，不再重新设计 Player Gate 1/2。

## 此前 League Gate 已完成

### Champ Select / Current Game Gate 1

- Issue #85：completed。
- PR #86：merged。
- 候选行为 HEAD：`bf9ae52dbb814a5ac862c6671085e6ed0300d456`。
- 行为 merge commit：`4d0cbe43c9ae5e1bae62ad62d398f8fba1ab138a`。
- Windows 腾讯/国服候选 Build #955：用户实机验收反馈“没问题”。
- 候选 CI：Windows Build #955 / UI Text Contract #76 / Mayhem Source Probe #237 全部 SUCCESS。
- 行为 main post-merge：Windows Build #958 / UI Text Contract #79 / Mayhem Source Probe #239 全部 SUCCESS。
- canonical closeout PR #89：merged；closeout main commit `6e27fc1f3aa3be711c7539342dff86c63f81ecff`。
- closeout main checks：Windows Build #960 / UI Text Contract #81 SUCCESS。

### 更早基线

- League Dashboard Gate 1：已完成并通过腾讯/国服实测。
- Player Gate 1：已完成并通过 Build #951 腾讯/国服实测；PR #82 已合并，Issue #81 completed。

## 已完成并冻结

没有真实缺陷或新需求时，不要顺手重做以下已验收基础：

- Modular Host Phase 1～5。
- Real Pet Gate 1。
- UI Text Contract。
- Performance Contract。
- League Dashboard Gate 1。
- Player Gate 1。
- Champ Select / Current Game Gate 1。
- Player Gate 2。
- 单实例二次启动 Ensure Open / Activate。
- Flying Runtime。
- VPet / PetHost。
- Cleanup 安全语义。
- Mayhem 多源容灾。
- Online Release 事务和现有配置兼容。

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不是当前主线，不自动恢复。

## Performance Contract

原则：**高配要快，普通机要顺；游戏优先，FACM 第二优先。**

预算上限：

- Desktop：network 4 / image 2 / disk 2 / CPU 2 / prefetch 20
- League Client：3 / 2 / 2 / 2 / 12
- Queueing：2 / 1 / 1 / 1 / 4
- Champ Select：2 / 1 / 1 / 1 / 0
- In Game：1 / 1 / 1 / 1 / 0；后台预取、维护和非必要视觉增强关闭
- Hidden/background：1 / 1 / 1 / 1 / 0

从 Desktop → Client → Queueing → Champ Select → In Game，数字预算和功能开关只能更保守。

League Dashboard 的 Gameflow monitor 继续负责阶段预算联动：client 约 5s / queue 3s / champ-select 2s / in-game 10s。正常 LCU 可用时不额外枚举游戏进程；LCU 暂不可用时才使用进程作为性能兜底。

Player 页面继续保持：无后台定时刷新；打开后先账号/缓存、再战绩；关闭立即取消；默认 10 场，手动最多 20 场；Queueing / Champ Select / In Game 禁止战绩详情预取。Gate 2 的英雄名称元数据也服从同一预算：敏感阶段不新增该请求。

League Live 页面继续保持：Champ Select 可见轮询不快于约 2s，In Game 不快于约 10s；refresh 串行；关闭页面取消；不请求战绩/队友侦察/图片预取。

OP.GG 对局助手继续服从同一预算：只在用户打开窗口时工作；自身请求串行；同一上下文命中缓存；In Game 不新增 OP.GG 请求；不加载图片、不做玩家战绩 fan-out、不注入游戏进程。

## League / 腾讯国服已验证基线

- 所有 League 功能继续复用唯一 `LeagueClientModule`，不要新增平行 LCU connector。
- 正常 discovery：进程路径 → 同目录 Riot `lockfile`。
- 活动 lockfile 必须使用 `FileShare.ReadWrite` 共享只读，并对瞬时 IO / 半写入做短重试。
- `MainModule.FileName` 失败时可用 WMI `ExecutablePath` 补路径。
- lockfile 仍失败时，仅对 `LeagueClientUx` 使用 WMI `CommandLine` fallback，并交给已有 parser；凭据只在内存使用，禁止日志/UI 输出。
- Akari 官网“不支持腾讯服务器”只视为官方免责声明；腾讯兼容按源码机制 + 实机功能测试判断。
- Dashboard 已在腾讯环境读取当前召唤师、平台/区服 `CQ100`、Gameflow 和 Performance。
- Player Gate 1 已在腾讯环境实机验收正常。
- Champ Select / Current Game Gate 1 已在腾讯环境使用 Build #955 实机验收通过。
- Player Gate 2 已在腾讯环境验证 `/lol-game-data/assets/v1/champion-summary.json` 本地英雄名称映射，并完成 Build #965 最终视觉验收。
- OP.GG 对局助手 Gate 1 的腾讯/国服上下文识别、Global 构筑数据可达性与选人性能仍待 Build #971 实机验证，不提前标记通过。
- 腾讯 match-history 的 `gameCount` 不作为账号全历史总数；分页按请求窗口实际返回数量判断。

## Champ Select / Current Game Gate 1 冻结边界

独立 `LeagueLiveModule` 依赖 `LeagueClientModule + PerformanceModule`，不创建第二套 LCU connector。

Champ Select 只读：

- `/lol-champ-select/v1/session`
- game / queue
- `localPlayerCellId`
- timer
- bans
- myTeam / theirTeam
- champion intent
- 当前本地 action
- spell IDs

Current Game 只读：

- 已有 Gameflow phase
- `/lol-gameflow/v1/session`
- gameId
- map
- mode
- queue
- team/player/champion 字段

Gate 1 明确不包含：

- auto accept
- 自动 pick / ban
- swap / reroll / dodge
- 改技能 / 皮肤
- teammate match-history fan-out
- 千场分析
- live timeline
- champion 图片后台预取
- SGP 扩展请求

除非出现真实缺陷或新独立需求，不要扩大该已验收 Gate 的范围。

## League 产品路线与总进度

当前按 5 个主阶段计算：

1. **League Dashboard Gate 1 — DONE**
2. **Player Gate 1 — DONE**
3. **Champ Select / Current Game Gate 1 — DONE**
4. **Player Gate 2 — DONE**
5. **Tools / Automation — CURRENT / CANDIDATE**

当前正式完成进度仍为：**4/5 = 80%**。Tools / Automation 只有在 Windows 腾讯/国服实机验收并合入 `main` 后才计为 5/5。
