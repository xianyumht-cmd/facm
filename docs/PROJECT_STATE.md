<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.38
- GitHub Release：v3.5.38
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：c8eaa274d768881608bb5381f90cb9407ba98b23
- 发布元数据提交：8632e243e5faff2c59313afd30aa4ba17eccb361
- Release FACM.exe SHA-256：C113ECB881A597191A1F0022DE763686805B508FB12080A693AECFABA6B77C85
- release_notes：FACM 3.5.38：新增 LOL Champion Select 秒退阵营只读公开测试 Probe。腾讯服实机已确认真实秒退会返回 StrangerDodged，但 dodgerId=0，且对方 Summoner ID 被隐藏，因此本版不把 StrangerDodged 单独当作对方秒退。Probe 保留 dodgeData，并组合 ChampSelect myTeam 瞬时变化、chatDetails/chat room system 退出消息、聊天参与者变化和 Lobby 成员变化等 GET-only 旁路证据；确认秒退后仅进行约 1 秒的短时高频取证，平时会话详情只做基线，避免长期高频轮询。没有正向证据时继续保持 unknown，不猜测 enemy。普通玩家聊天正文不记录，仅与秒退诊断相关的 system/event departure 文本可能写入本地日志；Probe 不发送聊天、不改变匹配/ReadyCheck/选人行为、不新增 LCU 写请求或第二 Gameflow owner。
<!-- FACM_RELEASE_STATE_END -->

# FACM Project State

更新时间：2026-09-10

## 当前产品线

FACM 只维护 **3.5.x lightweight**：WinForms / .NET Framework 4.8 / 单 `FACM.exe`。4.x 已退出默认工作树、当前 CI 与发布链；历史实现只保留在 Git 历史、旧 tag/release/remote branch/旧 PR 中，不作为当前产品依据。

当前在线正式版是 **3.5.38**。在线更新已启用，`minimum_version=3.0.0`，`force_update=false`。后续实机发现问题按普通 3.5.x patch 修复，不回到 4.x 产品线。

## 当前已交付行为

- Mayhem 百分比单位修正，长内容/装备/强化展示完整性改善；3.5 快速数据链未重写。
- Lobby 进入后立即评估自动寻找，不再固定等待 1500 ms。
- ReadyCheck 立即评估自动接受；失败可在同一 episode 内短间隔重试并做最终状态 reconciliation。
- Matchmaking 写失败/结果不明确时读取 queue state，避免“已生效但响应丢失”造成重复 POST。
- disconnected/null Gameflow cadence 为 3 秒；ChampSelect 约 2 秒、Queue/ReadyCheck 3 秒、InGame 10 秒。
- InGame 自动隐藏悬浮入口/桌宠，离开 InGame 后只恢复由 Gameflow 自己隐藏的入口；用户在同一 InGame 显式重开控制中心不会被 heartbeat 重复关闭。
- PetHost 启动过程中保留 desired visibility，避免游戏中晚启动闪现。
- 导航 owner-draw 残影、紧凑控制中心首次裁剪残影、LOL Hub 顶部遮挡、共享 Toggle 重影和窗口控制按钮旧像素残留均已有 deterministic 回归保护。
- 普通构建不内嵌 self-contained PetHost；轻量 FACM.exe 体积 gate <10 MiB。
- 更新 manifest 以 GitHub main 的 3.5 清单为唯一版本基准；多个传输候选选择最高有效版本，旧镜像不能把服务器版本倒退到当前客户端以下。
- UI Round 1 引入共享 `FacmActionButton` / `FacmToggleSwitch` / `FacmStatusBadge`，建立 `ThemeCatalog` / `FacmThemeRuntime` / `FacmDesignSystem` 语义边界。
- UI Round 2 的场景导航只消费 `LeagueDashboardModule` 的共享 Gameflow 状态，不新增 LCU polling、第二 League session 或新的 League 写入路径。
- 3.5.25 普通顶层 WinForms 使用共享视觉无标题栏外壳：36px 默认交互区与内容画布同色，只保留品牌、拖动、最小化/最大化/关闭；LOL Hub 不展示副标题层。
- 3.5.26 UI Reskin Pass 1：共享卡片改为纯 surface、公共圆角收紧为 window 12 / card 8 / control 6；LOL Hub 左侧导航改为轻选中面 + 2px accent indicator，顶部子导航改为紧凑标签式底部选中线；`FacmChromeButton` 与通用业务 Button 主题隔离。

## 3.5.27 UI Design System 收口

3.5.27 将 3.5.26 发布后已合并的高频可见页面统一打包为正式版本：

- PR #251：共享侧栏/顶部标签恢复键盘 Tab/focus contract；League Dashboard 从六张等权卡片改为“连接 + Gameflow”主状态区与紧凑元数据列表。
- PR #252：League Player 战绩页移除主要 page-local 深色 RGB palette 和私有按钮样式，改用共享 semantic tokens/primitives；保持高密度虚拟化列表，不把每条数据包装成卡片。
- PR #253：正常 Compact Launcher 可见增强路径覆盖旧玻璃渐变、双色强调和大圆角，使用共享 Canvas/header/tile/context 视觉；legacy fallback action 不变。
- PR #254：LOL Hub 增加 DPI 感知和自适应布局；sidebar/context/subnav 根据可用宽度分配空间，实时 Live 页优先保留高密度宽视图。
- PR #255：League Live 的 phase/status/bench/player list/actions 收敛到共享设计系统，同时保留原 polling、snapshot 和 bench-swap ownership。
- PR #256：统一 Recommendation 页面从 legacy cyan/violet presentation 收敛到共享语义色；旧双色 header glow 改为单 accent 细线；推荐选择和刷新/应用恢复键盘可达。
- PR #257：Presence 不再直接消费 page-local `ThemeDefinition` 视觉；状态改用共享 accent/success/warning/error；Presence 与 Game Repair 操作恢复 Tab 可达性。
- PR #258：Mayhem Lookup 外壳、输入框、状态和四个操作按钮迁移到共享设计系统；Search 是唯一 primary action。**MayhemCardRenderer 保持领域专用攻略图视觉与信息密度，不做模板式“统一”。** Windows Build、UI Text Contract 和 Mayhem Source Probe 均通过后合并。
- PR #259：请求并发布正式 FACM 3.5.27；发布链完成签名、公开字节校验、manifest 启用与项目 release-state 更新。

## 3.5.28 Windows 真机 UI 收尾

3.5.28 只处理 3.5.27 真机截图暴露出的具体问题，不再开启新一轮大换皮：

- PR #261：Mayhem Lookup 新增共享响应式几何策略，查询/取消/保存/复制从实际 client width 反向分配，920px 正常窗口和更窄的 Hub 嵌入区域都不再截断；攻略预览提前预留垂直滚动条宽度并只建立纵向滚动范围，避免正常窗口出现横向滚动。共享 smoke 固化 1120/920/700px 几何边界，Windows Build #1628、UI Text Contract #736、Mayhem Source Probe #477 全绿后合并。
- PR #261：共享 `Border` / `BorderSoft` 从历史主题的高饱和原始 border 向 material surface 收敛，使强调色重新集中到选中、动作和状态；不删除任何历史 ThemeCatalog id。
- PR #261：清理与修复页去掉动作区的一层外卡片，驱动修复/环境清理改用 `FacmActionButton` Secondary 语义并恢复 Tab 可达性，同时补充 DPI autoscale；清理目标、提权、进程检查、驱动工具和执行逻辑不变。
- PR #262：请求并发布正式 FACM 3.5.28；发布工作流完成 Release build/smoke、Authenticode 签名、公开字节与签名者复验，并在公开验证通过后启用在线更新。

## 3.5.38 Champion Select 秒退阵营只读公开测试

- 实现来源：PR #282，`feat/read-only-dodge-probe-20260909`。
- 2026-09-09 腾讯客户端实机日志已经抓到多次自然秒退：`dodgeData.state=StrangerDodged`，但 `dodgerId=0`；同时 `myTeam` 5 人身份完整可读、`theirTeam` 只有槽位且对方 Summoner ID 全部隐藏。
- 因此 `StrangerDodged` 本身不再被视为对方秒退证据。阵营判断必须 fail closed，没有正向证据就保持 `unknown`。
- 新 Probe 保留 `/lol-matchmaking/v1/search`，并组合 ChampSelect 原始 `myTeam` 瞬时变化、`chatDetails.chatRoomName`、房间 system/event 退出消息、聊天参与者变化和 Lobby 成员变化等只读信号。
- 正常选人阶段的会话详情只做一次基线；确认秒退后才进入约 1 秒、125 ms cadence 的短时证据 burst，避免公共版本长期高频读取消息/参与者接口。
- 普通玩家聊天正文不记录；只有与秒退诊断相关的 system/event departure 文本允许以 160 字符上限写入本地 FACM 日志。会话 ID 使用 `c1`/`c2` 等本地标签。
- Probe 只接收 `ILeagueClientApi`，不新增任何 LCU `POST` / `PUT` / `PATCH` / `DELETE`，不发送聊天、不改变匹配/ReadyCheck/选人行为，也不新增第二 Gameflow owner。
- `LeagueDodgeProbeService.ValidateForSmokeTest()` 已接入 `--league-dashboard-test`，公共发布仍要求 Windows Build 与 UI Text Contract 通过。
- 3.5.38 定位为公开实机取证版本；正常用户可通过在线更新共同积累真实 Tencent dodge evidence。最终“己方玩家秒退 / 对方玩家秒退”用户提示仍需等公开数据证明某个正向映射稳定后再产品化。

## Runtime Companion modernization（PR #283，未合并/未发布）

- 唯一任务分支：`feat/runtime-companion-20260910`；唯一任务 PR：#283，保持 draft，正式版仍是 3.5.38。
- 旧 660px 横向 ChampSelect assistant 的正常创建入口已切换为约 388px 逻辑宽度的纵向 Runtime Companion；旧实现暂时保留为未使用兼容代码，待真实 Tencent 客户端验收后再决定是否删除。
- `LeagueHubModule` 继续持有 Champion Select episode/popup 生命周期；Runtime Companion 不新增 Gameflow owner、第二 League session 或 page-local LCU write stack。
- `LeagueRuntimeCompanionController` 将现有 Bench、Build Advisor、Mayhem 数据投影为 snapshot，并用 generation/cancellation 阻止换英雄后的旧异步结果覆盖新上下文。
- Build Advisor 从同一份已获取的 OP.GG payload 中为符文、召唤师技能、出门装、鞋子、核心装和技能顺序保留最多 3 套源顺序方案；第 1 套仍是既有默认，额外方案仅用于“更多”展开，不增加网络请求。英雄头部同时投影 mode/position/patch 与源数据真实存在的 Tier、排名、胜率、登场率、禁用率；缺失统计保持未知，不伪造为 `0%`。
- Rune / Summoner Spell 就地 Apply 复用 `LeagueBuildApplyService`，继续执行用户确认、phase/champion/queue 重验与 settled postcondition。Core 的“导入装备”复用 `LeagueItemSetService`：准备阶段只读，确认后再次验证选人上下文，只维护 FACM 自有推荐装备文件，并验证最终 JSON；其他没有安全 owner 的类别继续只读。
- ARAM 基础平衡复用现有 `OpggAramBaseBalanceService` 的 bounded/10 分钟完整结果缓存，并由 `RiotGameDataService.EnrichAsync` 作为 automatic-guide 的唯一调用 owner 与视觉元数据并行获取；已移除 `MayhemAutomaticGuideService` 中的重复预调用，避免失败态触发第二次外部请求。Runtime Companion 已新增独立“大乱斗基础平衡”行：有真实 `BaseBalanceSummary` 才显示，`syncing`/`unavailable` 保持警告语义，缺失时直接省略。
- Bench 快速换英雄继续走 `LeagueBenchQuickPickService`，但 `benchEnabled` 不再被当作 Mayhem 证明。`LeagueQueueModePolicy` 将普通 ARAM（450/ARAM）与 Mayhem（2400、CN/WeGame 3270、KIWI/ARAM_MAYHEM）分开：普通 ARAM 只读取版本绑定的基础平衡补充，Mayhem 才启动完整攻略/强化链；未知 Bench 模式 fail closed。普通空状态不生成装饰性 N/A 卡片。
- Runtime Companion 的位置、置顶、收起状态通过共享 `AppSettings` 与现有 last-known-good recovery 持久化；不创建 Form 私有配置文件，也不重新加载第二份 settings。
- `app.manifest` 已有 PerMonitorV2。Companion 的恢复/默认定位延迟到 `Shown` 后按物理 DPI 尺寸处理，允许左侧屏幕负坐标，并在显示器拓扑变化时 clamp 到当前 working area。
- deterministic smoke 覆盖紧凑宽度/高度、100%/150% DPI 高度策略、负坐标多屏 clamp、settings round-trip/LKG recovery、snapshot clone、最多 3 套方案投影、缺失胜率不伪造、rune/spell scoped apply、item-set owner 边界，以及 ARAM balance 有值显示/无值省略。
- ARAM 可视化补丁之前的完整实现 head `df835a040f90caa242d1069bc9d4afbd3d680ff4` 已通过 UI Text Contract #831、Mayhem Source Probe #491 与 Windows Build #1723（含 lightweight FACM.exe verification 和 optional PetHost self-test）。当前收口 head 必须重新通过同样 Gate；旧成功记录不能替代最新代码验证。
- 剩余外部 Gate 是真实 Tencent 客户端验收：普通 Ranked 的自动出现/不抢焦点/英雄切换；Rune/召唤师技能 Apply；装备导入；ARAM/Mayhem 基础平衡/强化/Bench；关闭仅 dismiss 当前 episode；拖动/置顶/收起跨 episode 恢复，以及有条件时的多屏/125%/150%/200% DPI。未通过这些实机 Gate 前不 merge、不 bump version、不改在线 manifest、不生产发布。

历史主线：P1 合并 #241；4.x working-tree cleanup #242；3.5.21 更新一致性 #243；UI Round 1 #244；UI Round 2 #245；3.5.23 顶部布局修复 #246；3.5.24 Toggle 重绘 #247；3.5.25 一体化无边框外壳 #248；3.5.26 UI Reskin Pass 1 #249；Agent knowledge consistency #250；3.5.27 UI 收口 #251–#259；3.5.28 真机 UI 收尾 #261–#262；3.5.38 秒退阵营只读公开测试 #282。PR #283 当前仅为 Runtime Companion review task，尚未进入正式历史主线。

## 当前产品体验方向

- 继续保持 WinForms / net48 / single-EXE，不为视觉升级引入第二 UI 框架。
- `ThemeCatalog` 是 palette source，`FacmThemeRuntime` 是 process-wide active theme owner，`FacmDesignSystem` / 共享控件承载公共视觉语义。
- `FacmWindowChrome` 保留窗口行为所有权；36px 顶部交互区与内容视觉合并，不重新引入独立标题栏式副标题层。
- 顶部交互区与内容区继续使用显式互不重叠坐标，不回到依赖 Dock/Z-order 的布局。
- 悬浮入口场景化仅复用唯一 Gameflow owner；不得为了“更快”再建轮询器或第二 League session。
- 高频用户操作优先收敛到状态首页、统一 LOL Hub 与自动化设置，不增加重复入口。
- UI 参考现代 Windows / Fluent / PowerToys 的克制桌面产品行为，并用 anti-template 审计规则抑制大圆角、蓝紫双强调、卡片滥用、胶囊滥用、强制对称矩阵和无意义装饰。
- 数据密集页优先保持表格/列表密度与明确层级；领域专用可视化（例如 Mayhem 攻略图）允许保留自己的信息设计，不为了表面统一损失可读性。

### 已确认但尚未收口的 UI 技术债

- 正常可见 Compact Launcher 已由 `DesktopLauncherEnhancer` 覆盖为共享 flat 视觉；`CompactMenuForm` 内部仍保留 `ThemedPanel` / `ThemedButton`、主题渐变和 Style-specific 装饰作为 fallback/legacy 实现。只有确认 fallback 仍真实可达或存在维护价值时才继续收敛，不为了删代码冒行为回归风险。
- `ThemeCatalog` 仍保留 Glass/Luxury/Cyber/Soft/Brutalist/Holographic/Minimal/Rgb/Aurora/Synthwave 等历史 palette 兼容项。共享可见产品 surface 只消费 semantic tokens/受控 geometry；3.5.28 已进一步压低共享 border 的高饱和主题泄漏。
- Dashboard、Player、Live、Recommendation、Presence、Mayhem Lookup 等高频表面已经完成主要视觉收口；较早的 standalone `LeagueBuildAdvisorForm` / `LeagueBuildApplyForm` / `LeagueItemSetForm` 等仍可见 page-local RGB/native styling。后续先确认这些旧入口的真实可达性和用户价值，再决定是否迁移，不以“零 Color.FromArgb”为目标。
- LOL Hub 的自适应 geometry 已在 #254 引入，Mayhem 嵌入页的剩余宽度/滚动问题已在 #261 收口；后续布局工作只以新的真机 DPI/缩放/窄窗口问题为证据。
- PR #283 的旧 `LeagueChampSelectAssistantForm` 暂时保留为未使用兼容代码；只有 Runtime Companion 完成真实 Tencent 客户端验收后，才判断是否删除旧类/旧 smoke，不在同一未验收阶段提前清理回退路径。

## 当前保留组件

- `src/FACM`：主程序与 3.5 runtime。
- `src/FACM.ToolBundle`：内置工具资源。
- `src/FACM.Updater`：3.5 单 EXE 更新替换、校验、回滚。
- `src/FACM.PetHost`：可选桌宠 runtime 源码与 IPC。
- `online/version.json`、`online/announcement.json`、`online/mirrors.json`。
- 3.5 Windows Build、UI Text Contract、Mayhem Source Probe、Online Management、3.5 Lightweight Release workflows。

## 已退出的 4.x 范围

不再维护或构建：WinUI/Morphing Surface、`FACM.App/Core/Infrastructure/Platform.Windows`、native bootstrapper、CAB、多版本 `.facm/versions`、4.x migration、4.x foundation/smoke/probe、旧 heavyweight embedded-PetHost publisher。

3.5 Updater 中曾保留的 4.x migration CLI/model/bootstrapper handoff 已移除；普通 3.5 原子替换/回滚/self-test 继续保留。当前 `online/version.json` 与 3.5 lightweight publisher 都保持 migration-free。

## 当前发布状态

正式发布状态以文件顶部 `FACM_RELEASE_STATE_BEGIN/END` 自动块为唯一权威。该块由 3.5 lightweight publisher 在成功发布后维护；普通功能 PR 不手工伪造未来版本的正式发布结果。

FACM **3.5.38 已正式发布并启用在线更新**。PR #282 已合并；canonical publisher 已完成 Release build/smoke、签名、GitHub Release 发布、公共 FACM.exe 字节与签名者复验，并在复验成功后将 `online/version.json` 设为 `enabled=true`。`force_update=false`，因此这是正常可选更新而不是强制升级。

PR #283 的 Runtime Companion 仍是 **review candidate**：没有修改 `release/3.5-request.json`、`online/version.json`、版本号、tag 或 Release，也没有合并到 main。CI 通过只证明构建/smoke contract，不等于生产发布或腾讯客户端实机验收。

## 当前维护 Gate

后续修改继续满足：

1. `FACM.sln` 只引用当前 3.5 项目。
2. Windows Build PASS。
3. UI Text Contract PASS。
4. ToolBundle 嵌入正常；PetHost ZIP 不嵌入；FACM.exe <10 MiB。
5. Updater self-test PASS。
6. retained source/workflow 不依赖已删除 4.x 项目/脚本。
7. `online/version.json` 和 publisher 不重新引入 4.x migration 配置。
8. UI 重构保留原 WinForms 交互语义；不得为了视觉一致性改变 League 写入、更新协议或现有业务所有权。
9. 场景首页必须复用共享 Gameflow 状态；不得为导航速度新建轮询器或第二 League session。
10. 共享无边框顶部交互区和内容区必须保持显式不重叠；默认顶部视觉应与内容画布一致，不重新引入副标题层。
11. 新增或实质重做的 UI 不得再创建 page-local 主题体系；优先使用 `FacmDesignSystem`、共享 primitives 和明确的布局/可访问性 contract。
12. `FacmNavButton` / `FacmPillButton` 必须保持 native `Button` + `TabStop=true` 的键盘可达 contract。
13. 默认 Compact Launcher 的可见增强路径应覆盖 legacy 渐变/双强调背景并使用共享 Canvas/WindowRadius；legacy fallback rendering 不能重新成为默认可见 surface。
14. 领域专用 UI（例如 MayhemCardRenderer）应优先保留其信息密度与功能语义；共享设计系统主要约束窗口壳、导航、状态和通用控件，不强行抹平领域视觉。
15. Mayhem Lookup 的 toolbar 必须保持在 client bounds 内，攻略预览应预留纵向滚动条宽度并避免正常窗口产生横向滚动；1120/920/700px 几何由 deterministic smoke 保护。
16. Champion Select 秒退阵营 probe 必须保持 GET-only、复用唯一 Gameflow owner、普通聊天正文不记录，并对 `StrangerDodged`/缺失身份 fail closed；没有正向阵营证据不得猜测 enemy。
17. Runtime Companion 必须继续复用 `LeagueHubModule` episode owner、共享 League/Build/Settings owners；位置恢复应在 PerMonitorV2 DPI 生效后 clamp，inline write 必须走现有安全 owner。真实 Tencent 客户端验收前不得把 PR #283 当作已发布行为，也不得删除旧 assistant 回退代码。

后续若发现实机问题，按普通 3.5.x bugfix 处理并发布新的 patch 版本，不恢复 4.x 产品线。
