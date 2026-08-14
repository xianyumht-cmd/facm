# FACM 当前项目状态

> 2026-08-14：FACM 3.2.0 是当前正式生产基线。Performance Contract、League Dashboard Gate 1、Player Gate 1、Champ Select / Current Game Gate 1 均已完成、通过 Windows 腾讯/国服验收并合入 `main`。
>
> 当前 League 规划进度：**3/5（60%）**。下一主线正式切到 **Player 后续 Gate：英雄名称/轻量元数据 + 英雄表现统计**。完整历史交接仍可参考 `docs/HANDOFF-20260814-LEAGUE.md`，但当前状态以本文件和 GitHub 实时状态为准。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 本轮没有创建新 Release、没有改 `online/version.json`、没有改变线上版本。

## 最新程序行为基线

Champ Select / Current Game Gate 1 已完成：

- Issue #85：completed。
- PR #86：merged。
- 候选行为 HEAD：`bf9ae52dbb814a5ac862c6671085e6ed0300d456`。
- merge commit：`4d0cbe43c9ae5e1bae62ad62d398f8fba1ab138a`。
- Windows 腾讯/国服候选 Build #955：用户实机验收反馈“没问题”。
- 候选 CI：Windows Build #955 / UI Text Contract #76 / Mayhem Source Probe #237 全部 SUCCESS。
- main post-merge：Windows Build #958 / UI Text Contract #79 / Mayhem Source Probe #239 全部 SUCCESS。
- Build #955 artifact：`FACM-Windows-x64-955`。
- artifact ZIP SHA-256：`E92F205D19F8672303FD9CE86166E5022DD33113D35C8BE3F9B99442433AC6F8`。
- packaged FACM.exe SHA-256：`93B00EC31B6B90BEC2A6A44FE1C6109241DC220899FB16AF1DB3BD84C28507E4`。

此前两项 League Gate 也保持完成：

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

Player 页面继续保持：无后台定时刷新；打开后先账号/缓存、再战绩；关闭立即取消；默认 10 场，手动最多 20 场；Queueing / Champ Select / In Game 禁止战绩详情预取。

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
4. **Player 后续 Gate — CURRENT**
5. **Tools / Automation — PLANNED**

当前总进度：**3/5 = 60%**。

## 下一主线：Player 后续 Gate

下一阶段不重做 Player Gate 1，而是在现有 `LeaguePlayerModule` 上渐进增强。优先顺序：

1. champion ID → 英雄名称；
2. 轻量本地英雄元数据；
3. 在已有最近对局数据上做有限英雄表现统计；
4. 只有性能预算允许时再评估小型图标资产，不做后台无界图片预取；
5. 保持 10 → 20 场分页、缓存、取消和敏感阶段零详情预取边界；
6. 所有静态可见文案继续走 UI Text Contract；
7. 腾讯字段继续按实测验证，不根据官网免责声明推断兼容性；
8. 不发布新版本，除非用户再次明确授权。

下一任务开始前先 fresh-check 当前 `main`、开放 Issue/PR 和现有 Player 代码，确认没有同任务在途分支，随后按一任务一短分支执行。
