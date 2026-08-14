# FACM 当前项目状态

> 2026-08-15：FACM 3.2.0 仍是当前正式生产基线。Performance Contract、League Dashboard Gate 1、Player Gate 1、Champ Select / Current Game Gate 1、Player Gate 2、Tools / Automation Gate 1 均已完成并合入 `main`。
>
> 原 League 五阶段规划仍为 **5/5（100%）DONE**。当前正在做的是 **5/5 之后的新扩展 Gate：Tools / Automation Gate 2（显式一键应用符文 + 召唤师技能）**，Issue #96 / Draft PR #97。它不重新打开已经完成的五个阶段，也不重新设计 Dashboard / Player / Live / Build Advisor Gate 1。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 本轮没有创建新 Release、Tag，没有修改 `online/version.json`，没有改变线上版本。

## 当前进行中扩展：Tools / Automation Gate 2

- Issue #96：OPEN。
- Draft PR #97：OPEN / Draft；腾讯/国服实机验收前不 Ready、不合并。
- branch：`feat/opgg-apply-gate2-96`。
- base：`main` @ `3517c80aaf514a5b8a8f3ad84658bd958c7e5b43`。
- 第一轮完整实现 HEAD `bce6b2ce409c0c04992058b4379ce88c762b933c`：UI Text #100 SUCCESS；Windows #979 在 Release compile 因 `ContextMenuStrip` 命名空间歧义失败，已按现有 WinForms 桥接模式 scoped 修复，不是 LCU 写入逻辑失败。
- 第二轮行为 HEAD `689c3e8afc2dab3c14c843f36a6dc687e8393b3c`：Windows #980 SUCCESS；UI Text #101 SUCCESS；Windows 日志明确输出 `FACM performance contract smoke passed.`，因此 Gate 2 deterministic smoke 已随 Performance Contract 实际执行。
- Build #980 artifact：`FACM-Windows-x64-980`，artifact id `9226698823`；GitHub artifact ZIP digest `39BACEAC1FB90B83E5D5583C88F111CF8BC8FB46012098A3D113CEB398144F1A`。
- Build #980 FACM.exe：3.2.0.0；CI 签名仍是既有 FACM 开发自签名行为。
- 后续 UI fail-containment 收紧已提交到同一分支；Windows #981 / UI Text #102 正在作为下一轮候选检查运行。以 GitHub 实时 PR HEAD / CI 为准。
- **尚未进行腾讯/国服真实写入验收，因此 Gate 2 当前仍只是 Draft 候选。**

### Gate 2 产品与安全边界

本 Gate 只增加 **用户显式触发的一键应用符文 + 召唤师技能**：

- 继续复用唯一 `LeagueClientModule`，不创建第二套 LCU discovery / auth connector；
- 新增最小 POST / PUT / PATCH transport，但与现有只读 transport 共享同一个 `LeagueClientSessionProvider`；
- 未点击“一键应用”时零 LCU 写请求；
- 用户点击后才读取当前 OP.GG structured spell/rune IDs，并先显示预览；
- 必须再经过 Yes / No 二次确认，默认按钮为 No；取消确认零写；
- 写入前重新读取 Gameflow / Champ Select，必须仍在 `ChampSelect`、仍是同一英雄，并在可判断时仍是同一 queue；上下文变化则 fail-closed；
- 召唤师技能写入前先 GET 当前 `/lol-champ-select/v1/session/my-selection`，推荐包含 Flash 时尽可能保持用户当前 D/F 闪现槽位；PATCH 后再次 GET 精确读回验证；
- 符文写入先 GET `/lol-perks/v1/inventory`；**只有 `canAddCustomPage=true` 才创建新的 `[FACM]` 自定义符文页**；
- `canAddCustomPage=false` 时直接跳过符文，不读取用户现有符文页用于覆盖，更不采用 Akari“覆盖第一页”的 fallback；
- 新符文页流程为 POST create → PUT page → PUT current page → GET pages read-back verify；
- 符文与召唤师技能独立记结果；一条失败、一条成功时必须显示 partial，不能伪报全部成功；
- 所有写入串行；页面关闭取消；异常留在 Gate 2 窗口内安全降级；日志不记录 LCU 密码/token。

Gate 2 明确禁止：

- 不自动触发；
- 不 auto accept；
- 不自动 pick / ban / swap / reroll / dodge；
- 不改皮肤；
- 不发送 Champ Select 聊天广播；
- 不做 Overlay，不注入游戏进程；
- In Game 零写请求；
- 本 Gate **不写装备集文件**。

### 为什么装备集不放进本 Gate

已 fresh-read League Akari `dev` @ `cb236b6caf196e2505c7dfa6b34185020fd1e570`：

- 召唤师技能使用 GET / PATCH `my-selection`；
- 符文使用 perk inventory / pages / currentpage LCU 接口；
- 装备集不是同一类 LCU 写请求，而是先读取 `/data-store/v1/install-dir` 后直接写客户端磁盘；腾讯路径还需要特殊落到 `../Game/Config/Global/Recommended`，并管理自身前缀文件。

因此装备集需要单独定义腾讯路径、文件事务、失败恢复/回滚、旧文件清理语义，再作为新的独立 Gate 处理；不能为了“功能齐”把磁盘写入硬塞进当前 Gate 2。

### Gate 2 deterministic smoke

当前已覆盖：

- Prepare / 预览阶段零写；
- 当前 OP.GG `runes` style / rune / stat-mod ID 结构解析；
- happy path 精确写请求：POST page + PUT page + PUT current page + PATCH spells；
- Flash 在 D / F 的原槽位偏好保持；
- 符文页容量已满时 fail-closed，不 GET/覆盖用户现有 page，只允许独立技能写入；
- 已离开 Champ Select：零写；
- 英雄变化：零写；
- spell PATCH 失败而 rune 成功：只报 partial；
- caller cancellation：零写；
- 禁止 actions / reroll / skin 等越界 endpoint。

### Gate 2 下一步验收

候选 CI 全绿后，提供对应 Windows artifact 做腾讯/国服实机测试，至少确认：

1. 原 `OP.GG 对局助手` Gate 1 仍正常，不因新增写 transport 回归；
2. 托盘新增 `OP.GG 一键应用`，Lobby / 非 Champ Select 时无法写入；
3. Champ Select 推荐就绪后，窗口能显示当前英雄、分路、召唤师技能和符文预览；
4. 未点击按钮时客户端任何符文/召唤师技能都不变化；
5. 点击后先出现确认框；选“否”必须零写；
6. 选“是”后才应用；Flash D/F 原偏好不应无故互换；
7. 有空自定义符文页时新增 `[FACM]` 页并切换到该页；
8. 自定义符文页已满时必须明确提示“跳过符文”，不能覆盖用户已有页面；技能仍可独立成功；
9. 写后结果必须与客户端真实状态一致；失败/partial 不能伪报成功；
10. 选人过程保持流畅；切换英雄/离开 Champ Select 后 stale plan 必须被阻止；
11. In Game 不允许新写请求；关闭窗口停止请求。

用户实机明确通过后，再 fresh-check PR #97 exact HEAD / CI，Ready/merge，验证 main post-merge，再做 canonical closeout。**不因此自动发布新版本。**

## 最新完成行为：Tools / Automation Gate 1

Tools / Automation Gate 1 已完成腾讯/国服实机验收并合入 `main`：

- Issue #93：completed。
- PR #94：merged。
- 最终行为候选 HEAD：`3b3a3e3ddeeb3fb40fa86a9de4a440c42d34d66f`。
- 最终 PR HEAD：`35eb3eb4bb3e15d667037d82e08d4173bacae4f8`。
- merge commit：`90b3c829aa8682f0d6be139512b348eb4f4aff78`。
- Build #974：Windows SUCCESS；UI Text #95 SUCCESS；Mayhem Source Probe #246 SUCCESS。
- 最终 artifact：`FACM-Windows-x64-974`，artifact id `9225312518`。
- artifact ZIP SHA-256：`62238B804ABDC3571E2458FAA78BA96FE7192290C189C690D642DAD328D5A622`。
- 最终 PR HEAD 检查：Windows #975 / UI Text #96 / Mayhem #247 全部 SUCCESS。
- merge 后 main：Windows #976 / UI Text #97 / Mayhem #248 全部 SUCCESS。
- 构建 FACM.exe 版本仍为 `3.2.0.0`；开发自签名行为未改变。

### 腾讯/国服实机验收记录

Build #971 第一次真实 Champ Select 测试提供了有效故障证据：

- 正常：`ChampSelect`、本地英雄 `疾风剑豪 #157`、OP.GG version `16.16`、Performance=`champ-select` 均读取正常；
- 异常：上下文停在 `ranked / all`，OP.GG build 返回不可用，推荐表为空；因此 Build #971 不通过验收；
- 结论：腾讯 LCU / Champ Select / 英雄本地名称 / OP.GG versions 链已工作，故障收敛到 ranked champion-build 请求层。

对照 League Akari 当前源码后，Build #974 做 scoped 修复并通过第二次实机：

- OP.GG champion-build 请求绑定 `tier=all` 与当前 version；
- `ranked / all` 只作为 FACM 内部“分路尚未确定”哨兵，不直接发给 ranked build；
- 腾讯有 `assignedPosition` 时直接使用；没有时只额外读取一次当前 version 的 OP.GG ranked champion list，按 `role_rate`、其次 `play` 推断主位置，结果缓存 30 分钟；
- champion list 不可用时才使用 Akari 默认 ranked 偏好 `top` 做最终只读降级；
- 不做五路逐个探测；当前 OP.GG `runes` 与旧 `rune_pages` 均兼容；
- 非 2xx 只记录安全 HTTP 状态码 / path，不记录 LCU 凭据；
- In Game 仍严格 cache-only，不新增 OP.GG 请求。

最终腾讯/国服截图确认：

- `ChampSelect · 疾风剑豪 #157 · ranked / mid`；
- 数据来源 `OP.GG Global`，版本 `16.16`；
- Tier/rank、Win/Pick/Ban 率正常显示；
- 召唤师技能、符文、出门装、鞋、核心装备、技能加点、Counter 均正常渲染为本地名称；
- Performance 保持 `champ-select`；
- UI 明确只读，没有自动修改客户端配置。

### Tools / Automation Gate 1 冻结边界

- 独立 `LeagueBuildAdvisorModule` 继续依赖现有 `LeagueClientModule + PerformanceModule`；不创建第二套 LCU connector。
- 上下文复用现有 `LeagueLiveDataService` Gameflow / Champ Select 解析边界。
- OP.GG 数据明确标注 **OP.GG Global**，不冒充腾讯/国服胜率。
- LCU 本地 game-data 只用于名称映射，不加载英雄/装备图片。
- 同一 champion/mode/position/version 构筑缓存 10 分钟；本地静态元数据、OP.GG version、推断的 ranked position 缓存 30 分钟。
- 请求串行；页面关闭立即取消；相同上下文命中缓存；In Game cache-only。
- 不 auto accept。
- 不自动 pick / ban / swap / reroll / dodge。
- 不自动改召唤师技能、符文页、装备集、皮肤或其它客户端设置。
- 不做 teammate match-history fan-out / 玩家侦察。
- 不做游戏内 Overlay，不注入游戏进程。
- Tools / Automation Gate 2 是后续独立扩展，不改变 Gate 1 当时“严格只读”的验收事实和冻结边界。

## 此前 League Gate 已完成

### Player Gate 2

- Issue #90：completed；PR #91：merged。
- 最终候选 HEAD：`24b9db09bc50d0d8490fa46bdd303d59a0f1583a`。
- merge commit：`1ae84844feddddda91226867172ff93c9cb8d5aa`。
- Build #965 腾讯/国服截图确认 `英雄` 列、`英雄表现（当前已加载 2 场）`、中文英雄名称与当前已加载战绩统计正常。
- Windows #965 / UI Text #86 SUCCESS；merge 后 Windows #966 / UI Text #87 SUCCESS。
- champion ID 通过本地 `/lol-game-data/assets/v1/champion-summary.json` 映射名称；英雄表缓存 30 分钟；敏感阶段不新增该请求；统计只基于当前 10/20 场，不追加 match-history。

### Champ Select / Current Game Gate 1

- Issue #85：completed；PR #86：merged。
- 候选行为 HEAD：`bf9ae52dbb814a5ac862c6671085e6ed0300d456`。
- merge commit：`4d0cbe43c9ae5e1bae62ad62d398f8fba1ab138a`。
- Build #955 腾讯/国服实机验收通过。
- 候选 Windows #955 / UI Text #76 / Mayhem #237 全绿；merge 后 Windows #958 / UI Text #79 / Mayhem #239 全绿。
- Gate 保持只读：不 auto accept / pick / ban / swap / reroll / dodge / 改技能或皮肤，不做队友战绩 fan-out、千场分析、live timeline、图片后台预取或 SGP 扩展。

### 更早基线

- League Dashboard Gate 1：DONE，腾讯/国服已验收。
- Player Gate 1：DONE，Build #951 腾讯/国服已验收；PR #82 merged，Issue #81 completed。
- 单实例二次启动 Ensure Open / Activate：DONE；Issue #53 / PR #54 仅作历史，不重新设计。

## 已完成并冻结

没有真实缺陷或新独立需求时，不要顺手重做：

- Modular Host Phase 1～5。
- Real Pet Gate 1。
- UI Text Contract。
- Performance Contract。
- League Dashboard Gate 1。
- Player Gate 1 / 2。
- Champ Select / Current Game Gate 1。
- Tools / Automation Gate 1（OP.GG 对局助手只读推荐）。
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

- League Dashboard Gameflow monitor 继续负责阶段预算联动：client 约 5s / queue 3s / champ-select 2s / in-game 10s。
- Player：无后台定时刷新；默认 10 场，手动最多 20 场；Queueing / Champ Select / In Game 禁止战绩详情预取；Gate 2 英雄名称元数据也服从同一预算。
- League Live：Champ Select 可见轮询不快于约 2s；In Game 不快于约 10s；refresh 串行；关闭取消；不请求战绩/队友侦察/图片预取。
- OP.GG 对局助手：只在用户打开窗口时工作；请求串行；同一上下文缓存；未知 ranked 分路最多额外一次可缓存 champion-list lookup；In Game 不新增 OP.GG 请求；不加载图片、不做玩家 fan-out、不注入游戏。
- Gate 2 一键应用：无后台轮询和自动触发；只有用户点击后才准备 structured IDs，确认后才串行写入；In Game 零写请求。它不能放宽任何现有 Performance Contract 数字预算。

## League / 腾讯国服已验证基线

- 所有 League 功能继续复用唯一 `LeagueClientModule`，不要新增平行 LCU connector。
- 正常 discovery：进程路径 → 同目录 Riot `lockfile`。
- 活动 lockfile 必须使用 `FileShare.ReadWrite` 共享只读，并对瞬时 IO / 半写入做短重试。
- `MainModule.FileName` 失败时可用 WMI `ExecutablePath` 补路径。
- lockfile 仍失败时，仅对 `LeagueClientUx` 使用 WMI `CommandLine` fallback，并交给已有 parser；凭据只在内存使用，禁止日志/UI 输出。
- Akari 官网“不支持腾讯服务器”只视为官方免责声明；腾讯兼容按源码机制 + 实机功能测试判断。
- Dashboard：腾讯环境可读取当前召唤师、平台/区服 `CQ100`、Gameflow、Performance。
- Player Gate 1：腾讯实机通过。
- Champ Select / Current Game：Build #955 腾讯实机通过。
- Player Gate 2：Build #965 验证本地英雄名称映射与视觉统计。
- OP.GG 对局助手：Build #974 腾讯 Champ Select 实机确认 `ranked / mid`、OP.GG Global 16.16 与完整只读推荐正常。
- Gate 2 写入链尚未腾讯实机验收；CI / offline smoke 不能替代真实腾讯客户端验收。
- 腾讯 match-history 的 `gameCount` 不作为账号全历史总数；分页按请求窗口实际返回数量判断。

## League 产品路线与总进度

原 5 个主阶段：

1. **League Dashboard Gate 1 — DONE**
2. **Player Gate 1 — DONE**
3. **Champ Select / Current Game Gate 1 — DONE**
4. **Player Gate 2 — DONE**
5. **Tools / Automation Gate 1 — DONE**

原规划正式完成进度：**5/5 = 100%**。

当前后续扩展：

- **Tools / Automation Gate 2：OP.GG 一键应用符文 + 召唤师技能 — CURRENT / DRAFT CANDIDATE（Issue #96 / PR #97）**。
- 装备集磁盘写入不属于 Gate 2；如继续做，另建独立 Gate。

新的扩展仍必须先 fresh-check `main`、开放 Issue/PR 与现有边界；不重开已完成的五个主阶段。