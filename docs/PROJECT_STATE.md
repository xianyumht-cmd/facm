# FACM 当前项目状态

> 2026-08-14：FACM 3.2.0 是当前正式生产基线。Performance Contract、League Dashboard Gate 1 与 Player Gate 1 均已完成、通过 Windows 腾讯/国服验收并合入 `main`。当前主线：**Champ Select / Current Game Gate 1**。
>
> 当前活跃：Issue #85 / Draft PR #86，候选 HEAD `bf9ae52dbb814a5ac862c6671085e6ed0300d456`，Windows Build #955 / UI Text #76 / Mayhem #237 全绿，**等待 Windows 腾讯/国服实机验收，未验收前不要合并**。完整交接见 `docs/HANDOFF-20260814-LEAGUE.md`。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 当前轮次没有创建新 Release，也没有改变线上版本。

## 最新程序行为基线

- PR #82：`Player Gate 1：当前账号玩家主页与最近战绩渐进加载`，已 merged。
- 行为 merge commit：`28431338f915b60811c254581987cdd58e190dbe`。
- PR 最终行为 HEAD：`22959ba75e65ba03efd87891864ce79bc46c13d9`。
- PR checks：Windows Build #951 / UI Text Contract #72 / Mayhem Source Probe #234 全部 SUCCESS。
- main post-merge checks：Windows Build #952 / UI Text Contract #73 / Mayhem Source Probe #236 全部 SUCCESS。
- Issue #81：completed。
- Windows 腾讯/国服候选 Build #951：用户实机验收反馈“正常”。

## 已完成并冻结

- Modular Host Phase 1～5：模块所有权和依赖已稳定。
- Real Pet Gate 1：写真级真实蜜蜂已验收。
- UI Text Contract：稳定 TextKey、`[Text]`、`[Replace]` 兼容层与 CI 防回归已完成。
- Performance Contract：共享性能预算、Host 所有权与 deterministic smoke 已完成。
- League Dashboard Gate 1：客户端连接、当前账号、平台/区服、Gameflow、性能档联动、腾讯国服 discovery 已完成。
- Player Gate 1：当前账号玩家主页、最近战绩轻量列表、10→20 场渐进加载、缓存/取消、腾讯国服数据解析与性能降级边界已完成。

没有真实缺陷或新需求时，不要顺手重做以上基础，也不要重做单实例激活、Flying Runtime、VPet/PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务或现有配置兼容。

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

League Dashboard 是第一个常驻 Performance Budget consumer：Gameflow monitor 在首次 WinForms Idle 后启动，client 约 5s / queue 3s / champ-select 2s / in-game 10s。LCU 正常时不额外枚举游戏进程；LCU 暂不可用时才用 LeagueClient / League of Legends 进程做性能兜底。

Player Gate 1 继续服从同一预算：页面无后台定时刷新；打开后先账号/缓存、再加载最近战绩；页面关闭立即取消；默认只拉 10 场，手动扩展最多 20 场；只有摘要缺字段且预算允许时才串行补 `/lol-match-history/v1/games/{gameId}`，Queueing / Champ Select / In Game 自动禁止详情预取。

## League / 腾讯国服基线

- 继续复用唯一 `LeagueClientModule`，不要新增平行 LCU connector。
- 正常 discovery：进程路径 → 同目录 Riot `lockfile`。
- 活动 lockfile 必须 `FileShare.ReadWrite` 共享只读，并对瞬时 IO/半写入做短重试。
- `MainModule.FileName` 失败时可用 WMI `ExecutablePath` 补路径。
- lockfile 仍失败时，仅对 `LeagueClientUx` 使用 WMI `CommandLine` fallback，并交给已有 parser；凭据只在内存使用，禁止日志/UI 输出。
- `LeagueClientSmokeTest` 保留活动 lockfile 共享句柄回归 fixture。
- Akari 官网“不支持腾讯服务器”只视为官方免责声明；腾讯兼容按源码机制 + 实机功能测试判断。
- Akari 仓库 `2026-05-16-tencent-hn10` fixture 明确来自已登录腾讯客户端；其 LCU current-summoner match history 包含 participant identity、英雄 ID、KDA、CS、胜负和多种腾讯队列，可作为国服字段结构参考。
- 腾讯 history 的 `gameCount` 不作为账号全历史总数；FACM 的 Gate 1 分页按“请求窗口是否实际填满”判断是否允许继续加载。

## Player Gate 1 行为边界

- 独立 `LeaguePlayerModule`，依赖 `LeagueClientModule + PerformanceModule`；不创建第二套 LCU connector。
- 托盘入口“玩家主页”位于 League Dashboard 附近。
- 当前账号先加载；PUUID 改变时旧战绩缓存失效。
- profile 短缓存，match page 45 秒缓存；重新打开仍会校验当前账号。
- 首次严格请求 `begIndex=0&endIndex=9`；手动“再加载 10 场”最多扩到 20 场。
- 当前玩家 participant 优先按 PUUID 关联，summonerId 作为 fallback。
- 基础行显示：时间、模式/队列、英雄 ID、KDA、CS、胜负、时长。
- 使用 WinForms `ListView.VirtualMode`，不按对局数量创建无限复杂控件。
- 第一版不加载英雄图片/名称资产，不做千场统计、队友侦察、时间线、重型动态图表或自动 ban/pick。
- deterministic smoke 已覆盖解析、PUUID/summonerId 关联、分页、取消、详情兜底和 In Game 零详情预取。

## 产品方向

FACM 不复制 Akari 的 Electron/Vue 技术栈。继续使用 C#/.NET Windows 基础，学习其功能边界、数据能力和客户端联动，同时把低资源占用作为产品差异。

顺序：
1. League Dashboard：已完成
2. Player Gate 1：当前账号 + 最近对局，已完成
3. Champ Select / Current Game：当前 Draft PR #86，等待国服实测
4. Player 后续：英雄名称/图标、英雄表现与统计，在不破坏性能预算的前提下逐步补齐
5. Tools / Automation

## 当前任务：Champ Select / Current Game Gate 1

当前候选已实现，不要从零重做：
- Issue #85 / Draft PR #86 / branch `feat/champ-current-gate1-85`；
- 候选 HEAD `bf9ae52dbb814a5ac862c6671085e6ed0300d456`；
- Champ Select 只读 `/lol-champ-select/v1/session`；
- Current Game 只读已有 Gameflow + `/lol-gameflow/v1/session`；
- 不新增第二套客户端连接；
- 不做 teammate history fan-out、图片预取、时间线或自动客户端操作；
- Champ Select / In Game 严格服从当前 Performance Contract；
- 静态可见文案走 UI Text Contract；
- Build #955 / UI Text #76 / Mayhem #237 已全绿；
- 下一步是 Windows 腾讯/国服集中验收，不是继续堆功能；
- 未通过实测前保持 Draft，不合并；
- 不发布新版本，除非用户再次明确授权。

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不是当前主线，不自动恢复。
