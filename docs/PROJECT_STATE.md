# FACM 当前项目状态

> 2026-08-14：FACM 3.2.0 是当前正式生产基线。Performance Contract、League Dashboard Gate 1、Player Gate 1、Champ Select / Current Game Gate 1 均已完成、通过 Windows 腾讯/国服验收并合入 `main`。
>
> 当前 League 规划进度：**3/5（60%）**。当前主线是 **Player Gate 2：英雄名称与轻量表现统计**，Issue #90 / Draft PR #91 已有 Windows 候选，等待腾讯/国服实机验收。完整历史交接仍可参考 `docs/HANDOFF-20260814-LEAGUE.md`，但当前状态以本文件和 GitHub 实时状态为准。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 本轮没有创建新 Release、没有改 `online/version.json`、没有改变线上版本。

## 当前 main 与最新完成行为

Champ Select / Current Game Gate 1 已完成并 canonical closeout：

- Issue #85：completed。
- PR #86：merged。
- 候选行为 HEAD：`bf9ae52dbb814a5ac862c6671085e6ed0300d456`。
- 行为 merge commit：`4d0cbe43c9ae5e1bae62ad62d398f8fba1ab138a`。
- Windows 腾讯/国服候选 Build #955：用户实机验收反馈“没问题”。
- 候选 CI：Windows Build #955 / UI Text Contract #76 / Mayhem Source Probe #237 全部 SUCCESS。
- 行为 main post-merge：Windows Build #958 / UI Text Contract #79 / Mayhem Source Probe #239 全部 SUCCESS。
- canonical closeout PR #89：merged；当前 main merge commit `6e27fc1f3aa3be711c7539342dff86c63f81ecff`。
- closeout main checks：Windows Build #960 / UI Text Contract #81 SUCCESS。

此前两项 League Gate 也保持完成：

- League Dashboard Gate 1：已完成并通过腾讯/国服实测。
- Player Gate 1：已完成并通过 Build #951 腾讯/国服实测；PR #82 已合并，Issue #81 completed。

## 当前未完成：Player Gate 2

- Issue #90：OPEN。
- Draft PR #91：OPEN / Draft / mergeable。
- branch：`feat/player-gate2-90`。
- base：`main` @ `6e27fc1f3aa3be711c7539342dff86c63f81ecff`。
- 当前候选 HEAD：`bac1aec66644ec1b9e94b2c0df6c19a434449810`。
- changed files：4，全部位于现有 Player 链；没有新增模块、Release 或 online 更新改动。
- Windows Build #961：SUCCESS。
- UI Text Contract #82：SUCCESS。
- 本次没有改 Mayhem 源码，因此没有额外 Mayhem Source Probe run；Windows Build 的 `--performance-contract-test` 已执行 `LeaguePlayerSmokeTest`。
- artifact：`FACM-Windows-x64-961`，artifact id `9221229324`。
- artifact ZIP SHA-256：`AAF50215D0854ADB3C508853830C6943D3C8649CAFFBF175E4692C616DF231D2`。
- packaged FACM.exe SHA-256：`49E14B51FE443F5130D9BCFEE5FC86A984E4055BC841D500C2E5442CB209447B`。
- packaged FACM.exe size：77,980,056 bytes。
- **腾讯/国服实机尚未验收；未通过前 PR #91 必须保持 Draft，不合并。**

### Player Gate 2 当前行为边界

在 Player Gate 1 基础上只做渐进增强：

- champion ID 通过本地 LCU `/lol-game-data/assets/v1/champion-summary.json` 映射客户端本地英雄名称；
- 英雄表是一次性汇总请求，内存缓存 30 分钟，不按每场/每英雄 fan-out；
- Queueing / Champ Select / In Game / hidden-background 性能档不会启动该非必要元数据请求；已有缓存仍可用于显示；
- 元数据不可用或字段变化时稳定 fallback 到 champion ID，不影响账号/战绩主链；
- 表现统计只基于当前已经加载的 10/20 场，不追加 match-history；
- 统计包含英雄样本场次、胜率、平均 K/D/A；
- UI 使用轻量有限 ListView，没有英雄图片、动态图表或后台资产预取；
- 保留 Gate 1 的账号缓存、45 秒战绩页缓存、10→20 明确分页、页面关闭取消和敏感阶段 0 战绩详情预取边界；
- deterministic smoke 覆盖英雄表解析/映射、30 分钟缓存、敏感阶段 0 英雄元数据请求、纯内存统计、0 新增 match-history fan-out、取消和原分页边界。

### Player Gate 2 下一步验收

Build #961 Windows 腾讯/国服集中验证：

1. 登录客户端后打开“玩家主页”，原账号和最近战绩仍正常；
2. 英雄列应优先显示本地中文英雄名并保留 ID；如果腾讯该 endpoint/字段有差异，至少必须稳定退回数字 ID，不能拖垮 Player；
3. 上方轻量统计区应只统计当前加载的 10 场；手动“再加载 10 场”后改为基于当前 20 场重新聚合；
4. 检查同一英雄多场时样本数、胜率、平均 K/D/A 是否符合当前可见战绩；
5. 进入 Queueing / Champ Select / In Game 后再打开/刷新 Player，不应因英雄名称增加额外卡顿或后台重请求；
6. 原 10→20、关闭页面停止请求、战绩读取和详情降级行为不能回归。

用户明确反馈“正常/没问题”后，fresh-check PR #91 HEAD 与 CI，再 Ready/merge；有 bug 则保持 Draft，只修 scoped bug。

## 已完成并冻结

没有真实缺陷或新需求时，不要顺手重做以下已验收基础：

- Modular Host Phase 1～5。
- Real Pet Gate 1。
- UI Text Contract。
- Performance Contract。
- League Dashboard Gate 1。
- Player Gate 1。
- Champ Select / Current Game Gate 1。
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
- Player Gate 2 的 `/lol-game-data/assets/v1/champion-summary.json` 腾讯兼容性仍待 Build #961 本轮实机验证，不提前标记实测可用。
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
4. **Player Gate 2 — CURRENT / CANDIDATE**
5. **Tools / Automation — PLANNED**

当前正式完成进度仍为：**3/5 = 60%**。Player Gate 2 只有在 Windows 腾讯/国服实机验收并合入 main 后才计为 4/5。
