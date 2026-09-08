<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.37
- GitHub Release：v3.5.37
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：399572594c2618482171be426fae3d4d0c0d1434
- 发布元数据提交：be953c405c27d1940a411720c1fb1a73522b415d
- Release FACM.exe SHA-256：1C4E46D1107AD0AF568CBADDABAA1A047C4345257430E12774F3FAAB2664A645
- release_notes：FACM 3.5.37：继续加固 LOL Gate 7 自动匹配的 exactly-once 语义，修复匹配搜索 POST 已经发出但仍在进行中时关闭自动匹配，随后在同一 Lobby 重新开启可能重复发送开始匹配请求的问题。此前 Lobby 指纹只会在 POST 明确成功或通过 isCurrentlyInQueue=true 对账确认成功后记录；如果关闭开关触发取消，而请求实际已经到达 LCU 但客户端在记录指纹前收到 OperationCanceledException，这次写入结果就会变成不确定状态，同一 Lobby 重新开启时可能再次 POST。现在会在匹配写入发生前先声明当前 Lobby 的队列与真实成员指纹作为 at-most-once claim；取消或关闭功能造成的结果不确定会保留该 claim，只有 Gameflow 真正离开 Lobby 才重置。明确返回失败且当前仍处于启用状态和同一 Lobby 时，会只释放本次 claim，继续沿用原有 3 秒退避重试，因此不会牺牲真实失败后的恢复能力。新增阻塞式确定性 smoke，覆盖进行中的搜索被关闭取消、同 Lobby 重新开启不重复，以及真实 Lobby 阶段边界后允许新一轮搜索。ReadyCheck 自动接受、endpoint/请求体/轮询节奏、LCU 会话/传输与 UI 均未改变；继续保持 .NET Framework 4.8 + WinForms + 单 FACM.exe。
<!-- FACM_RELEASE_STATE_END -->

# FACM Project State

更新时间：2026-09-08

## 当前产品线

FACM 只维护 **3.5.x lightweight**：WinForms / .NET Framework 4.8 / 单 `FACM.exe`。4.x 已退出默认工作树、当前 CI 与发布链；历史实现只保留在 Git 历史、旧 tag/release/remote branch/旧 PR 中，不作为当前产品依据。

当前在线正式版是 **3.5.28**。在线更新已启用，`minimum_version=3.0.0`，`force_update=false`。后续实机发现问题按普通 3.5.x patch 修复，不回到 4.x 产品线。

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

历史主线：P1 合并 #241；4.x working-tree cleanup #242；3.5.21 更新一致性 #243；UI Round 1 #244；UI Round 2 #245；3.5.23 顶部布局修复 #246；3.5.24 Toggle 重绘 #247；3.5.25 一体化无边框外壳 #248；3.5.26 UI Reskin Pass 1 #249；Agent knowledge consistency #250；3.5.27 UI 收口 #251–#259；3.5.28 真机 UI 收尾 #261–#262。

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

- `online/version.json`：**3.5.28**，enabled=true，minimum_version=3.0.0，force_update=false。
- GitHub Release：`v3.5.28`，非 draft、非 prerelease，Release id `384240270`。
- Release `FACM.exe`：**1,891,736 bytes**。
- Release `FACM.exe` SHA-256：`D1DC6E07AD885729D9207B877BDDF82D0E6C7E135309E14744677911099DB8C6`。
- 发布请求合并 / frozen base：`dbf61e5e8f44beaf06df3904d1d76bd865fe3ee5`。
- Release target / 发布元数据提交：`f6e672b7d5d9d8539b6dc86057f955e7f57032b8`。
- 在线更新启用提交：`f0ad628c71294f4f62dd7d2d1214d07b7264615f`。

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

后续若发现实机问题，按普通 3.5.x bugfix 处理并发布新的 patch 版本，不恢复 4.x 产品线。
