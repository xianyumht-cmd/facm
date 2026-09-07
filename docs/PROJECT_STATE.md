<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.26
- GitHub Release：v3.5.26
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：0c6ba1378a94eb099262efdb199550d79f09b5d9
- 发布元数据提交：829e8634dc4aca6f115d48c166107115a5207afe
- Release FACM.exe SHA-256：E150CDECC5C09295C42CE23A91A86BA7F7589AF6FAC8DFA9A2B8D872F169B852
- release_notes：FACM 3.5.26：启动第一轮整体换皮，继续保持 .NET Framework 4.8 + WinForms + 单 EXE 的 3.5.x lightweight 路线。共享设计系统改为更克制的现代 Windows 桌面风格：压低卡片/按钮圆角，弱化蓝紫强调色对 hover 和边框的污染，移除旧玻璃卡片中的渐变、高光和装饰性双色光斑；LOL 工作台左侧导航改为更轻的选中/悬停层级，顶部子导航从大胶囊按钮调整为紧凑标签式选中下划线。右上角最小化/最大化/关闭按钮从通用 Button 主题中隔离，并在每次 owner-draw 前完整清理自身像素，修复圆角业务按钮样式污染、图标叠画和旧像素残留。继续保留 3.5.23 的标题区/内容区显式不重叠几何、3.5.24 的开关完整重绘，以及 3.5.25 的 36px 一体化无标题栏交互区域。
<!-- FACM_RELEASE_STATE_END -->

# FACM Project State

更新时间：2026-09-07

## 当前产品线

FACM 只维护 **3.5.x lightweight**：WinForms / .NET Framework 4.8 / 单 `FACM.exe`。4.x 已退出默认工作树、当前 CI 与发布链；历史实现只保留在 Git 历史、旧 tag/release/remote branch/旧 PR 中，不作为当前产品依据。

当前在线正式版为 **3.5.26**，在线更新已启用且不强制。当前 `main` 在 3.5.26 发布启用后的提交为 `705a0b5ea47bf5e59b8cefb1327c58a729652119`；该提交只启用已经公开验证过的 3.5.26 在线更新，没有改变 Release 二进制。

版本演进：3.5.20 完成 P1 回灌和 4.x 工作树清理；3.5.21 修复更新元数据接受旧缓存；3.5.22 合并 UI Round 1 + Round 2；3.5.23 修复 LOL Hub 顶部布局遮挡；3.5.24 修复共享自绘开关在布局变化后的重影/黑条；3.5.25 将普通顶层 WinForms 外壳收敛为视觉一体化无边框模式；3.5.26 启动整体换皮 Pass 1，压低共享圆角/玻璃装饰并修复窗口控制按钮被业务主题污染和重绘残影。

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
- UI Round 1：引入共享 `FacmActionButton` / `FacmToggleSwitch` / `FacmStatusBadge`，建立 `ThemeCatalog` / `FacmThemeRuntime` / `FacmDesignSystem` 语义边界，并迁移 Update Center 等表面。
- UI Round 2：直接左键点击内置悬浮入口时可显示当前 LOL 状态、自动下一局摘要和场景提示；Lobby / Matchmaking / ReadyCheck / 结算后指向下一局设置，ChampSelect / InGame 指向实时对局，普通客户端状态指向当前状态。
- 场景导航只消费 `LeagueDashboardModule` 的共享 Gameflow 状态，不新增 LCU polling、第二 League session 或新的 League 写入路径；托盘/第二实例普通控制中心和右键完整菜单保持原行为。
- 3.5.25 普通顶层 WinForms 使用共享视觉无标题栏外壳：36px 默认交互区与内容画布同色，只保留品牌、拖动、最小化/最大化/关闭；LOL Hub 不再展示副标题层。
- 3.5.26 UI Reskin Pass 1：共享卡片改为纯 surface、公共圆角上限收紧为 window 12 / card 8 / control 6；LOL Hub 左侧导航使用轻选中面与 2px accent indicator，顶部子导航改为标签式底部选中线；`FacmChromeButton` 被显式排除在通用 Button 主题之外。

P1 合并 PR：#241；4.x working-tree cleanup：#242；3.5.21 更新一致性：#243；UI Round 1：#244；UI Round 2：#245；3.5.23 顶部布局修复：#246；3.5.24 Toggle 重绘修复：#247；3.5.25 一体化无边框外壳：#248；3.5.26 UI Reskin Pass 1：#249。

## 当前产品体验状态

3.5.26 已按“正式版直接推送、有问题再修”的策略发布，不保留候选包阶段。后续实机若发现 DPI、主题、拖拽、窗口控制、重绘或其它问题，按普通 3.5.x patch bugfix 处理并发布新的版本。

当前体验方向：

- 继续保持 WinForms/net48/single-EXE，不为视觉升级引入第二 UI 框架。
- `ThemeCatalog` 为 palette source，`FacmThemeRuntime` 为 process-wide active theme owner，`FacmDesignSystem`/共享控件承载公共视觉语义。
- `FacmWindowChrome` 保留窗口行为所有权，但顶部交互区和页面视觉合并；不得重新引入独立标题栏式副标题层。
- 顶部交互区与内容区继续使用显式互不重叠坐标，不回到依赖 Dock/Z-order 的布局。
- 悬浮入口场景化仅复用唯一 Gameflow owner；不得为了“更快”再建轮询器。
- 高频用户操作优先收敛到状态首页、统一 LOL Hub 与自动化设置，不再增加重复入口。
- UI 目标参考现代 Windows / Fluent / PowerToys 的克制桌面产品行为，并用 anti-template 审计规则抑制大圆角、蓝紫双强调、卡片滥用、胶囊滥用和无意义装饰。

### 已确认但尚未收口的 UI 技术债

- `CompactMenuForm` 仍保留自己的 `ThemedPanel` / `ThemedButton`、主题渐变与 Style-specific 装饰绘制，没有完全由 `FacmDesignSystem` 接管；这是下一轮 UI Design System Pass 的主要迁移对象。
- `ThemeCatalog` 仍保留 Glass/Luxury/Cyber/Soft/Brutalist/Holographic/Minimal/Rgb/Aurora/Synthwave 等历史视觉风格；共享设计系统会钳制公共组件几何，但 Compact Launcher 等旧表面仍可能直接消费较夸张的原始 radius/双色 palette。
- `LeagueDashboardForm`、`LeaguePlayerForm` 等较早页面仍包含 page-local `Color.FromArgb(...)` 和固定坐标/尺寸。嵌入 Hub 时会经过共享 surface 处理，但它们应逐步迁移为共享 semantic tokens / primitives，而不是长期依赖私有 palette。
- `FacmNavButton` / `FacmPillButton` 当前主要解决鼠标导航和视觉一致性，键盘 focus/tab contract 仍需在后续可访问性 pass 中补齐。

## 当前保留组件

- `src/FACM`：主程序与 3.5 runtime。
- `src/FACM.ToolBundle`：内置工具资源。
- `src/FACM.Updater`：3.5 单 EXE 更新替换、校验、回滚。
- `src/FACM.PetHost`：可选桌宠 runtime 源码与 IPC。
- `online/version.json`、`online/announcement.json`、`online/mirrors.json`。
- 3.5 Windows Build、UI Text Contract、Mayhem probe、Online Management、3.5 Lightweight Release workflows。

## 已退出的 4.x 范围

不再维护或构建：WinUI/Morphing Surface、`FACM.App/Core/Infrastructure/Platform.Windows`、native bootstrapper、CAB、多版本 `.facm/versions`、4.x migration、4.x foundation/smoke/probe、旧 heavyweight embedded-PetHost publisher。

3.5 Updater 中曾保留的 4.x migration CLI/model/bootstrapper handoff 已移除；普通 3.5 原子替换/回滚/self-test 继续保留。当前 `online/version.json` 与 3.5 lightweight publisher 都保持 migration-free。

## 当前发布状态

- `online/version.json`：**3.5.26**，enabled=true，force_update=false。
- GitHub Release：`v3.5.26`，非 draft、非 prerelease，Release id `384029059`。
- Release `FACM.exe`：**1,874,328 bytes**。
- Release `FACM.exe` SHA-256：`E150CDECC5C09295C42CE23A91A86BA7F7589AF6FAC8DFA9A2B8D872F169B852`。
- Release target / 发布元数据提交：`829e8634dc4aca6f115d48c166107115a5207afe`。
- 在线更新启用提交：`705a0b5ea47bf5e59b8cefb1327c58a729652119`。

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

后续若发现实机问题，按普通 3.5.x bugfix 处理并发布新的 patch 版本，不恢复 4.x 产品线。
