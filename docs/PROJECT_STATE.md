# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.15
- GitHub Release：v3.5.15
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：908d5782e6eb5b30fee0e4d5794c312d70ac0e36
- 发布元数据提交：a12b561f2c229ecbb8f18dfa44de07e29d2d6f09
- Release FACM.exe SHA-256：E3B415375E204212EE2D7A36D4A038708DC75694CD9B6FD28F2761BBF1FD01CE
- published_at：2026-08-27T05:28:50.9137418+00:00
- release_notes：FACM 3.5.15：控制中心收束为“清理与修复 / LOL 工作台 / 个性化 / 更多设置”四个桌面式入口，新增清理与修复完整流程，LOL 工作台页面提示并入自绘标题栏，个性化主题升级为 FACM 全局主题。LOL 游戏修复同时完成原生现代化：不再启动旧 Fix-LCU-Window 外部 mode，窗口修复按客户端实际所在显示器与工作区处理多屏/负坐标，优先恢复合理尺寸；自动修复改为 WinEvent 事件驱动并带 debounce/cooldown，不再每 1500ms 常驻轮询；跳过卡结算复用既有 play-again writer，重启客户端界面使用专用最小权限 writer，并停止在正式 ToolBundle 中打包旧 Fix-LCU-Window EXE 与 mode scripts。
<!-- FACM_RELEASE_STATE_END -->

> 当前生产事实始终以上方发布工作流维护区块、GitHub Release 与 `online/version.json` 为准。更早版本记录仅作为历史回归证据。

## 3.5.15 产品变更基线

- PR #182 已完成控制中心与修复信息架构重构：控制中心只保留 `清理与修复 / LOL 工作台 / 个性化 / 更多设置` 四个桌面式入口；工作目录、环境状态与步骤说明进入 `清理与修复` 页面。
- `清理与修复` 统一环境级流程：游戏目录 → 驱动修复 / 环境清理（先后不限，建议各执行一次）→ 重启电脑 → WEGAME → 英雄联盟 → 修复游戏；FACM 不伪造 WEGAME 最终修复完成状态。
- LOL 工作台删除重复内容区提示条，当前页提示进入 FACM 自绘标题栏副标题；`ThemeCatalog + FacmThemeRuntime + FacmDesignSystem` 统一 FACM 自有窗口主题。
- 游戏运行期间的大厅/客户端异常归入 `LOL 工作台 → 自动化 → 游戏修复`。PR #183 将原 `fix-lcu-window` mode 1～4 正式迁为 FACM 原生实现，不再从 UI 启动旧 `Fix-LCU-Window.exe`。
- `立即修复窗口`：按 LeagueClientUx 实际所在显示器和 WorkingArea 处理多屏/负坐标；16:9 判断使用容差；优先恢复最近合理尺寸或保留可信宽/高，不再固定 `PrimaryScreen + 1280×720×zoom`。
- `自动修复窗口`：改为 WinEvent `EVENT_OBJECT_LOCATIONCHANGE` + 380ms debounce + 2s cooldown；默认关闭，仅本次 FACM 进程会话有效；不再启动独立 console，也没有 1500ms 常驻轮询。
- `跳过卡结算`：复用现有 Gate 6 `/lol-lobby/v2/play-again` writer；`重启客户端界面`：使用只暴露 `POST /riotclient/kill-and-restart-ux` 的专用窄 writer；二者都复用唯一 `LeagueClientModule + LeagueClientSessionProvider`。
- `一键结束游戏` 继续使用原进程级动作，与跳过卡结算、真实赛后自动回大厅保持独立语义。
- `FACM.ToolBundle` 不再嵌入旧 Fix-LCU-Window EXE 与 mode scripts；历史工具输入可以保留在源码仓库作为来源/回归证据，但不进入正式 FACM 游戏修复运行路径。
- `--facm-host-test` 增加原生修复纯离线回归：合理/异常窗口、可信宽度恢复、最近合理尺寸、负坐标显示器 clamp、Client UX writer allowlist 与既有 play-again writer 边界。
- 3.5.15 本身仍是 `.NET Framework 4.8 + WinForms` 主程序；下一阶段技术栈迁移不与 3.5.15 混做，当前规划目标已收束为 **FACM 4.0 / .NET 10 LTS + WinUI 3**，正式开工前由 Gate 0 再核实 Microsoft 当前支持生命周期与 Windows App SDK stable 版本。

## FACM 4.0 迁移交接 — NEXT TASK / NOT STARTED

> **新对话从这里继续。** 这是 3.5.15 正式发布并完成 `fix-lcu-window` 原生现代化后的下一主线。当前没有创建 4.0 代码分支、没有迁移项目、没有修改正式在线更新。先执行 Gate 0，不要直接开始画 WinUI 页面。

### 当前已完成 / 已验证

- PR #182 `重构控制中心、清理修复流程与全局主题` 已合并，merge commit `dc05558bfb63e3cf2b4db9bd5762c480c4198298`。
- PR #183 `原生现代化 LOL 客户端游戏修复` 已合并，merge commit `29793a05435b42644e4c407bf2ff6e75b3c8b437`。旧 Fix-LCU-Window 外部 mode 1～4 已退出正式运行路径，**这一项已经完成，4.0 不要再重做**。
- PR #184 `发布 FACM 3.5.15` 已合并，merge commit `908d5782e6eb5b30fee0e4d5794c312d70ac0e36`。
- 发布工作流 `FACM Publish Release` run #33 / run id `33042472043` 已 SUCCESS；正式 Release、签名/构建链、disabled manifest → enable manifest 事务均完成。
- Release tag `v3.5.15` 已发布；正式 `FACM.exe` SHA-256 为 `E3B415375E204212EE2D7A36D4A038708DC75694CD9B6FD28F2761BBF1FD01CE`。
- `online/version.json` 当前 `enabled=true / version=3.5.15 / minimum_version=3.0.0 / force_update=false`，在线更新已启用；公告 `popup=false`。
- 当前 canonical `main` 在发布工作流最终启用在线更新后为 `23556f19fc13ec0710f956a5b76ec9c8335739ce`。
- PR #183 CI：Windows Build #1286 SUCCESS、UI Text Contract #407 SUCCESS、Mayhem Source Probe #393 SUCCESS；合并 main 后 Windows Build #1287 / UI Text #408 / Mayhem #394 也通过。
- 发布 PR #184：Windows Build #1288 SUCCESS、UI Text Contract #409 SUCCESS；随后正式发布 run #33 SUCCESS。

### 当前代码 / 技术环境事实

- `src/FACM/FACM.csproj`：当前正式主程序仍为 `net48`、`UseWindowsForms=true`、`WinExe`、AnyCPU（Prefer32Bit=false）。正式 FACM.exe 继续内嵌 ToolBundle、无窗口 Updater 和 PetHost ZIP，现有单 EXE 在线更新链是 4.0 必须保护的资产。
- `src/FACM.PetHost/FACM.PetHost.csproj`：当前为 `net8.0-windows`、WPF + WinForms、x64，依赖 `VPet-Simulator.Core 1.1.0.66`；PetHost 是独立辅助进程并有现成 IPC / 生命周期边界。
- 主业务现有稳定资产包括：Modular Host、唯一 `LeagueClientModule + LeagueClientSessionProvider`、Gate2～Gate7 最小 writer、Bench Swap、OP.GG/Mayhem、原生 League Game Repair、Cleanup 安全边界、GameLocator、Updater、Single Instance、Hotkey、PetHost IPC、Performance Contract、UI Text Contract、事务式 Release。
- 用户本轮体验截图来自 Windows 10 桌面。截图时 LOL 当前状态页显示“未检测到客户端/等待连接”，因此这些截图只能证明 UI/Shell 布局问题，**不能据此判断 LCU/游戏修复功能失败**。当前未提供多屏/DPI 数值，4.0 Gate 0/后续兼容矩阵必须单独验证。

### 本轮实机观察到、明确暂不在 3.5.x 修的 UI 问题

1. LOL 工作台顶部偶发内容被遮挡/重叠。用户怀疑可能与内置 iFarm/标题栏冲突，但**根因尚未被代码证据确认**；更可能是当前自绘 Chrome、导航、嵌入子 Form/旧标题区域的布局/Z-order 所有权互相竞争。
2. 默认悬浮球不在桌面顶部时，点击后控制中心与悬浮球距离明显偏远；悬浮球靠近顶部时才偶然贴合。当前定位更像“绝对坐标 + WorkingArea clamp”，不是稳定 anchor placement。
3. “全局主题”目前未完整作用于默认 `F` 悬浮球，视觉上与控制中心/工作台割裂。4.0 应把默认 Shell/F 球纳入同一 Theme Resource。
4. 菜单/工作台偶发出现不明矩形或文字遮挡。第五张体验图能看到左上内容互相覆盖；当前不通过补丁式 `BringToFront`/坐标偏移去掩盖。

这些问题被视为 **FACM 3.x Legacy UI 已知缺陷**。除非演变为崩溃、无法启动、数据损坏、更新失效或严重核心功能不可用，否则 3.5.15 只做必要 Hotfix，不再投入大规模 WinForms UI 修补。

### 已否定 / 失败 / 不要重复的路线

- **不要恢复旧 Fix-LCU-Window EXE/mode runtime。** `PrimaryScreen + 1280×720×zoom`、独立 LCU client、1500ms 永久轮询已经由 PR #183 原生实现取代；4.0 只迁移/保留现有 FACM 原生 `LeagueGameRepair` 边界。
- **不要为当前遮挡继续堆 WinForms 补丁。** 不采用“再加 `BringToFront()` / `Top += N` / `Visible=false/true` / 新 Timer / 新反射 Enhancer”作为 4.0 前置修复。历史上首次绘制后 Idle+反射补布局已经出现旧像素/hover 后才恢复的问题；这条路线会继续扩大 Z-order 和生命周期债务。
- **不要继续保留 Form-in-Form 作为新 UI 架构。** 3.x `LeagueHub` 当前只保留一个子 Form 是低占用折中，不是 4.0 目标。WinUI 3 应使用一个 Shell + Frame/Navigation/Page visual tree，避免标题栏和内容区有多个 owner。
- **不要直接把当前 `FACM.csproj` 一把改成 WinUI 3 再修几百个编译错误。** 先并行建立新 Core/Platform/App 工程，抽离业务，再迁 runtime，再切 Shell；否则所有稳定业务与 UI 回归同时爆炸，定位困难。
- **不要创建一个长期 `rewrite-everything` 巨型分支。** 用户接受“产品层面大爆炸换代”，但工程实施必须 Gate 化：每 Gate 一个短分支/PR、可编译/可验证、合入 main；最终只在所有 Gate green 后切正式 4.0。
- **不要照搬 3.x 像素坐标/卡片尺寸。** 保留信息架构与行为契约，不迁移 `Rectangle(...)`、反射布局、手写像素定位历史债。
- **不要把页面变成新的 LCU owner。** WinUI Page/ViewModel 不创建第二套 discovery/auth/session、HttpClient、Gameflow monitor 或任意 writer；UI 只读 ViewModel/Product State、发送 Intent。
- **不要让各页面各自轮询当前状态。** 4.0 建统一 Product State/State Engine，页面订阅；避免重复 LCU/Timer/网络。
- **不要为了技术统一强行重写 PetHost。** PetHost 当前独立 WPF/VPet 边界有隔离价值；4.0 主 Shell 迁 WinUI 3 不等于必须把 PetHost 迁 WinUI 或塞回单进程。
- **不要把 .NET 8 作为新的 FACM 主程序长期基线。** 当前规划直接以 `.NET 10 LTS` 为目标；Gate 0 必须再次核实当时官方生命周期和依赖兼容性，若事实变化再记录决策。
- **不要为了 WinUI 3 让普通用户手工安装一串运行时。** Gate 0 必须做真实部署 prototype：优先维持自包含/低摩擦更新体验；若单 EXE + unpackaged/self-contained 方案不可靠，再选择单安装器 + 应用目录的方案，而不是让用户手动补 .NET/Windows App Runtime/DLL。
- **不要在 4.0 首发阶段追游戏内 Overlay 大全。** FACM 核心优势仍是腾讯服兼容、轻量 LCU 自动化、客户端/环境修复、海斗/ARAM、诊断与可靠性；OP.GG/Blitz/Mobalytics 主要用于学习状态驱动 UX，不照抄广告/Overlay 产品形态。

### FACM 4.0 已认可的产品 / 技术目标

产品定位继续向“腾讯英雄联盟桌面控制与智能辅助中心”推进，而不是回退成按钮工具箱。对标采用分层参考：

- League Akari：LCU 生命周期、League 功能模块化、客户端连接/重连、状态与能力结合；不照搬 Electron/TypeScript 技术栈。
- Microsoft PowerToys / FancyZones：Windows 工程、多显示器、DPI、窗口生命周期、模块/设置/诊断、可靠性；不把 FACM 做得同样重。
- Windows 11 / Fluent：主题、键盘、焦点、高对比度、文本缩放、Accessibility、现代设置体验。
- OP.GG / Mobalytics / Blitz：赛前→选人→游戏中→赛后/异常的状态驱动 Companion 思路；不追求 Overlay 功能堆砌。

4.0 目标架构：

```text
FACM.App            (.NET 10 LTS + WinUI 3 / Windows App SDK Stable)
  ├─ Shell / Navigation
  ├─ Pages / Dialogs / Flyouts
  ├─ ViewModels
  └─ Theme Resources
          ↓ intents/state
FACM.Core
  ├─ League Runtime
  ├─ Cleanup
  ├─ Settings
  ├─ Online
  ├─ Automation
  └─ Product State
          ↓
FACM.Platform.Windows / Infrastructure
  ├─ Process / Window / DPI / Monitor
  ├─ Hotkey / IPC / Registry / Filesystem
  ├─ HTTP / Update / Feature Flags
  └─ Diagnostics / Structured Logs

FACM.PetHost
  └─ 继续独立辅助进程，除非后续有独立证据要求改边界
```

核心原则：**UI 不拥有业务。** Page 只显示 ViewModel 并发送用户 Intent；LCU/session/writer、文件系统、进程、网络、更新、诊断由 Core/Platform owner 管理。

### FACM 4.0 Gate 规划（按顺序推进，最终一次正式切换）

**Gate 0 — Migration Contract / 全仓审计 / Prototype**
- 从最新 main 读取 `AGENTS.md` 与全部 canonical docs，创建一个 4.0 Gate0 短分支；不要先改正式项目。
- 建立“保留 / 抽离 / 重写 / 删除 / 延后”清单，覆盖 FACM、ToolBundle、Updater、PetHost、League、Cleanup、Online、Settings、资源嵌入、发布工作流和 deterministic smoke。
- 冻结 3.5.15 行为契约：唯一 League session、Gate2～Gate7 writer、Bench、Mayhem/OP.GG、Game Repair、Cleanup 安全、更新器、单实例、快捷键、PetHost、Performance/UI Text Contract。
- 核实 `.NET 10 LTS`、WinUI 3、Windows App SDK 当前 stable 支持矩阵，验证 Windows 10/11 最低版本。
- 做最小 WinUI 3 部署 prototype：验证 unpackaged/self-contained、单 EXE 可行性、Windows App SDK runtime、管理员/UAC、Updater 替换、资源嵌入、启动时间和 Defender 影响；如果单 EXE不稳，明确转为单安装器方案。
- 产出 4.0 Migration Contract、项目边界、验收矩阵和回滚线；Gate 0 **不发布版本、不修 3.x UI**。

**Gate 1 — 新 Solution / 并行骨架**
- 保留当前 `src/FACM` legacy 可构建，新增 `FACM.Core / FACM.Platform.Windows / FACM.Infrastructure / FACM.App`（最终命名在 Gate0 定）。
- 新旧结构短期并存；不能先删除 legacy，再追编译错误。
- 建立依赖方向和 analyzer/smoke，禁止 Core 引用 WinForms/WinUI UI 类型。

**Gate 2 — Core/UI 解耦**
- 从 Form/Enhancer 中抽离业务 orchestration；稳定 League/Cleanup/Online/Updater 能力尽量迁移而不是重写。
- 建立 Application Service / Intent / Result 边界；UI 不直接找进程、不直接读写 settings、不直接创建 LCU/HttpClient。

**Gate 3 — 主业务迁 `.NET 10`**
- 把可复用 Core/Platform/Infrastructure 移到 `net10.0-windows`（必要的纯逻辑库可用更通用 TFM，Gate0 决定）。
- 专项处理 `System.Drawing`、Win32/PInvoke、Registry、Process、NamedPipe、HttpClient、资源嵌入、Configuration、Threading/Timer、旧框架 API。
- 不在此 Gate 顺手重画产品 UI。

**Gate 4 — Settings 2.0**
- 引入 typed / versioned / validated / atomic-save / migration / defaults / module-owned settings。
- 3.5.15 `settings.ini` 必须有一次性无损迁移；用户升级 4.0 不重新配置。
- 配置格式（JSON/其它）是实现细节，Schema 与 Migration 才是契约。

**Gate 5 — Product State + Observability**
- 建统一 Application/League/Environment/Services 状态模型：League 至少覆盖 `NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError`。
- 页面订阅状态，不重复轮询。
- 日志升级为结构化记录：`ActionId / Module / Duration / Result / Reason / LeagueState / ClientVersion` 等，供诊断中心消费。

**Gate 6 — WinUI 3 Design System + Shell**
- 先建 semantic design tokens、ResourceDictionary、统一 Button/Card/Nav/Status/Dialog/TitleBar/EmptyState/SettingRow，再做页面。
- 一个 Window / 一个 TitleBar owner / 一个 Shell navigation visual tree；不复刻 Form-in-Form。
- 控制中心继续“我要打开什么”的四入口原则；LOL 工作台继续 `比赛 / 攻略 / 自动化` 信息架构。

**Gate 7 — Desktop Shell / F 悬浮球 / 全局 Theme**
- 默认 `F` 悬浮球纳入全局主题，和控制中心、LOL 工作台、清理与修复、设置、FACM 自有弹窗使用同一 Theme Resources。
- 新建 Anchor Placement Service：按悬浮球所在边/显示器选择菜单展开方向，先保持固定视觉间距，再做 WorkingArea clamp；支持负坐标/多屏/混合 DPI。
- 解决当前“悬浮球只有在顶部时菜单才贴近”的结构性问题，而不是移植旧坐标算法。

**Gate 8 — LOL 工作台状态驱动 UX**
- 保留唯一 League runtime，围绕 Gameflow 重组现有能力，而不是继续增加菜单层。
- NotRunning：启动/环境/攻略相关；Lobby：召唤师、战绩、段位、快捷工具；ChampSelect：英雄攻略、符文/技能/装备、Bench、一键应用；InGame：低占用；PostGame：赛后摘要/点赞/下一局；ClientError：修复建议。
- `fix-lcu-window` 已完成原生现代化，本 Gate 只消费 `LeagueGameRepair` 状态/动作，不再专项重写。

**Gate 9 — 诊断中心**
- 展示应用版本/权限/更新、LeagueClient/LCU/Gameflow/腾讯客户端、窗口/显示器/DPI、主要数据源、自动化 gate 状态。
- 提供“复制诊断摘要 / 导出脱敏诊断包”；不得包含 token、cookie、密码等秘密。
- 目标是以后“按钮没反应”优先由诊断证据定位，而不是截图猜测。

**Gate 10 — DPI / 多屏 / Accessibility 发布门槛**
- 覆盖 100/125/150/175/200% DPI、单屏/双屏、左右/上下/负坐标、混合 DPI。
- 覆盖 Keyboard-only、Tab/Enter/Esc、Focus、Light/Dark/High Contrast、Text Scaling、可访问名称/屏幕阅读器基础。

**Gate 11 — Recovery / Feature Flags / 更新保障**
- 保留并统一已有自动恢复：LCU reconnect、League restart session rebuild、数据源 fallback、PetHost 不拖垮 Shell、online last-known-good、更新失败保留旧版本。
- 增加服务端 Feature Kill Switch，只允许关闭/降级能力或切换数据源，不能远程扩大写权限或静默开启自动化。
- 4.0 架构预留 Stable / Preview 双通道，但首个 4.0 正式发布默认只向普通用户开放 Stable；不因为有 Preview 架构就强迫本轮制作候选版。

**Gate 12 — 全量兼容 / 性能 / 发布矩阵**
- Windows 10/11、不同 DPI/多屏、League 未启动/重启/Token 更新、网络断开、OP.GG/Mayhem 故障、PetHost、Updater、UAC、Single Instance 全覆盖。
- InGame 继续守 Performance Contract：network/image/disk/background CPU concurrency、prefetch、Timer/monitor 数量必须有明确预算。
- 所有现有 deterministic smoke 要么迁移并继续守门，要么以等价/更强测试替代，不能静默删除。

**Gate 13 — Legacy 删除与 FACM 4.0 正式切换**
- 只有前面 Gate 全部 green、3.5.15 配置迁移通过、正式更新链通过、Windows 兼容矩阵通过后，才删除/退休 Legacy WinForms Shell/Form/Enhancer。
- 最终用户层面是一次 FACM 4.0.0 大换代；工程层面不是长期巨型分支。
- 正式发布沿用事务式 Release 思路；是否仍为单 FACM.exe 取决于 Gate0 部署 prototype 结论。

### 下一对话第一步（严格顺序）

1. **不要修本轮截图里的 3.x UI 遮挡。**
2. **不要再做 fix-lcu-window。** 它已在 3.5.15 完成并发布。
3. 读取最新 `main`、`AGENTS.md`、`PROJECT_STATE.md`、`DECISIONS.md`、`PITFALLS.md`、`ARCHITECTURE.md`、`OPERATIONS.md`、`AI_WORKSTYLE.md`，确认没有新提交覆盖本交接。
4. 确认 3.5.15 Release/online 仍正常；若用户没有要求 Hotfix，生产基线冻结。
5. 开 **FACM 4.0 Gate 0 — Migration Contract / Repository Audit / WinUI Deployment Prototype** 独立短任务。
6. Gate0 先研究并产出决策，不发布 4.0、不切在线更新；完成 Gate0 后再进入 Gate1。

## 3.4.3 海克斯大乱斗可用英雄快速选择 — RELEASED

- Issue #134：`海克斯大乱斗：可用英雄快速选择（Bench Swap）`。
- PR #135：`海克斯大乱斗：可用英雄快速选择`，已合并到 `main`，merge commit `5d4cb6861d130ae6525a6f9ab1eb5a8ce61e551e`。
- PR #135 HEAD `033665701bc79f10f94b25768c9dc52468f8dfe7`：FACM UI Text Contract #239 SUCCESS；FACM Windows Build #1118 SUCCESS。
- 发布请求 PR #136 已合并，merge commit `3e816f33507e90fbacf0fcd74b136bcbfc91ac87`。
- 发布元数据 commit：`d13e5face98ea528699422112e53714f6e506c16`。
- 在线更新启用 commit：`956da4966e6500a57339922bae3f28c062b3e2c7`。
- GitHub Release：`v3.4.3`；`online/version.json` 当时启用 3.4.3，SHA-256 `4B477BDE7B8D4D99134A11A5D461E5DFA32CEA477A2133CA9D8B3CE00DB7FE47`。
- 功能位于 `比赛 → 实时对局`：读取现有 `/lol-champ-select/v1/session` 的 `benchEnabled` / `benchChampionIds`，不建立第二套 LCU discovery / auth / session。
- Bench 激活且页面可见时，使用 session-only 轻量刷新追踪可用英雄；正常 Live Champ Select 刷新保持原 2 秒节奏，InGame / 最小化继续节流。
- 英雄头像仅按需从本地 LCU `/lol-game-data/assets/v1/champion-icons/{id}.png` 读取并缓存，不请求外网、不做后台预取。
- 用户点击英雄后才执行一次 `POST /lol-champ-select/v1/session/bench/swap/{championId}`；写前重新确认目标仍在 Bench，目标已被别人拿走则不发送 POST。
- 每次点击最多一次 swap POST；2xx 后只做有界只读 settled verification，未真正切换到目标英雄不得误报成功。
- Bench swap 使用独立最小 writer；Gate2 writer 不放宽，仍拒绝 bench swap 与 `/lol-champ-select/v1/session/actions/{id}`。
- **不做自动抢英雄**：不监控指定目标后自动 swap，不做自动 pick / ban / reroll / dodge / skin；“抢英雄”只指用户在 FACM 里手动点击得更快。

## 3.4.2 发布与回归证据

- 3.4.1 腾讯 Windows 实机回归中，用户确认 `一键退出游戏` 已恢复可用；新触发链在日志中产生成功记录。
- 同一轮实机日志显示推荐中心装备集写入成功，但 Gate2 符文 / 召唤师技能缺少足够终态诊断，因此继续修复一键应用。
- PR #131：`修复一键应用：复用 FACM 符文页并补齐实机诊断`。
  - 不再每次无条件新建 `[FACM]` 符文页。
  - 优先复用当前同名 `[FACM]` 页。
  - 自定义页容量已满时，只允许复用 FACM 自有页，不覆盖普通用户符文页。
  - 保留 settled read-back；LCU 2xx 不直接等于真实成功。
  - 补齐 prepare / blocked / skip / rune / spell 终态日志。
- PR #131 HEAD `2ebfb9c2832184f545e68e74591165d0ccc6f09d`：FACM UI Text Contract #230 SUCCESS；FACM Windows Build #1109 SUCCESS。
- PR #131 已合并到 `main`，merge commit `49440ce4897b12fca062474098cc5e9c642f1782`。
- 发布请求 PR #132 已合并，merge commit `bc2603976dd9691172401778656b50429864dfed`。
- `FACM Publish Release` run `32055053102`：SUCCESS；正式 build、内嵌资源验证、签名、disabled manifest、版本元数据、Release 发布、启用 online manifest 全部成功。
- Release target / 发布元数据 commit：`252ae023428bfa0a57dcbbd4ec273953ebf49440`。
- 在线更新启用 commit：`ca462a4026a8368a63d4ed806359900c151084ae`。
- GitHub Release：`v3.4.2`。
- 正式下载：`https://github.com/xianyumht-cmd/facm/releases/download/v3.4.2/FACM.exe`。
- Release FACM.exe SHA-256：`B0F31DA0F158301507EFA6567F3115CF3893B34FD07717508E5743A2FF1FF5D1`。
- `online/version.json` 当时为 enabled=true / version=3.4.2 / minimum_version=3.0.0 / force_update=false。

## 当前 League 产品状态

### 单入口 LOL 工作台 — RELEASED

当前正式版已完成 League 产品入口收束；3.5.7 后持续做上下文化，3.5.14 已统一普通顶层窗口自绘外壳并利用稀疏页面宽屏空白：

- 托盘与控制中心对 League 只保留一个 `英雄联盟` 主入口。
- 点击后进入唯一的 `LOL 工作台`，不再把 Dashboard / Player / Live / OP.GG / Efficiency 分散为多个 Shell 按钮。
- 左侧用户概念为 **比赛 / 攻略 / 自动化**。
- 工作台右侧提供「接着做」上下文栏，按当前功能给出 3～4 个强相关下一步；海斗、出装、实时、战绩和快捷工具可在同一工作台连续切换，不额外打开一层功能窗口。
- 窗口较窄时相关栏自动隐藏，空间优先留给主内容。
- `LeagueHubModule` 只负责导航与页面组合，不拥有第二套 LCU session、gameflow monitor 或 writer。
- Hub 仍只保留当前子 Form；切页正常 Close/Dispose，避免 Timer / CancellationToken 在后台累积。
- 视觉使用静态蓝 / 青 / 紫灯带、描边和选中状态，不增加动画 RGB Timer 或新的高频常驻刷新。

### 海斗实战决策卡 — RELEASED in 3.5.8

- PR #168：`海斗升级为实战决策助手`，已 squash 合并到 `main`，merge commit `0125c69f6f3cd3d0fb38de93e995835996790b74`。
- PR HEAD `b15ad6d84fa457f71099811377f6675ddf0aa580`：FACM UI Text Contract #341 SUCCESS；FACM Windows Build #1220 SUCCESS；FACM Mayhem Source Probe #357 SUCCESS。
- 顶部「先看结论」只从真实 `Tier / Rank / WinRate`、单符排行统计和核心装备名称投影，不额外发明玩法标签。
- 首看强化存在真实胜率/选择率时使用既有稳定评分；统计缺失时仅退回榜单首位，不伪造胜率。
- 两套核心出装、出门、鞋子、召唤师技能同时显示文字名称和图标，图片慢或加载失败时仍可读。
- 强化 TOP10 显示 `优先级 #N`、真实单符胜率、热度、样本和效果说明。
- 三条方向改为 `稳定赢法 / 高上限玩法 / 热门好上手`，底层排序语义分别保持胜率+热度、单符胜率、选择率。
- 基础 ARAM 与 Mayhem 专属修正继续分层显示、不相加；页脚继续明确单符统计不代表三符组合胜率。
- 继续沿用 3.5.7 的公网/图片时间预算，不增加新请求、常驻 Timer 或后台预取。

### OP.GG / FACM 推荐 — RELEASED；3.4.2 加固

- Gate2：手动一键应用符文 + 召唤师技能。
- Gate3：FACM owned Recommended item set，腾讯游戏内商店已验收。
- Gate4：选人自动应用推荐，默认关闭、稳定 fingerprint exact-once。
- 手动应用前继续执行 Champ Select / champion / queue 上下文校验。
- 召唤师技能保留 Flash 槽位偏好并做写后读回验证。
- 符文优先复用同名 `[FACM]` 自有页；容量满时只复用 FACM 自有页，不修改普通用户符文页。
- 如果没有安全可复用页，继续 fail-closed，不扩大写权限。
- 装备集仍保持独立 FACM owned 文件边界。
- 3.4.2 已新增 Gate2 终态诊断日志，后续腾讯实机问题应优先依据日志定位，不再靠 UI 现象猜测。

### 游戏效率快捷键 — RELEASED；3.4.1 实机修复确认

当前动作目标：

- 一键结束国服 `League of Legends(TM)`（兼容旧 `League of Legends`）。
- 一键关闭 `LeagueClient / LeagueClientUx / LeagueClientUxRender`。

3.4.0 曾出现 FACM 启动后、未打开任何 FACM 界面时快捷键不响应的腾讯 Windows 实机回归。3.4.1 已补强后台触发链，用户随后确认 `一键退出游戏` 可用。当前不再把该问题描述为进行中回归。

### 赛后自动化 — DONE / 用户验收

- 随机点赞一名 eligible teammate；排除自己 / 对手 / 机器人。
- 自动返回大厅；点赞失败不阻止 `play-again`。
- 连续赛后 episode 最多执行一次。
- 默认关闭。

### 自动下一局 — DONE / 腾讯实机验收

- 自动寻找对局：保留 `Lobby + canStartActivity + local leader + real member` 核心安全门槛。
- 自动接受：以连续 `ReadyCheck` Gameflow episode 为主触发；`/lol-matchmaking/v1/search` 只 best-effort 判断已 Accepted / Declined。
- Gate7 writer 只允许 search + ready-check accept；同一 episode / fingerprint exact-once；InGame 零 Gate7 写入。
- 默认关闭。

## 明确取消：账号密码快捷输入

用户真实测试后明确要求“不搞这个了”。正式产品无 credential hotkey setting、无账号密码 UI、无 clipboard credential parser 入口、无 credential SendInput / UIAutomation 路径。未来除非用户重新提出独立需求，否则不得恢复。

## League 主线状态

原 League 五阶段：**5/5 DONE**。扩展 Gate2 / Gate3 / Gate4 / Gate5 / Gate6 / Gate7、手动 Bench quick-pick、LOL 工作台与海斗实战决策卡均已收口并进入正式版本。

若后续腾讯实机报告 Bench 快速选择异常，优先保留当前最小 writer 边界并读取实际 Champ Select session / 状态结果，再开新的独立 Issue；不得直接扩大到自动 pick/ban/actions writer。

## 性能与权限冻结边界

- 唯一 `LeagueClientModule + LeagueClientSessionProvider`，不新增第二套 discovery / auth connector。
- 自动化默认关闭。
- 不做游戏内 Overlay / 注入。
- 不做自动 pick / ban / 自动 Bench swap / reroll / dodge / skin；手动 Bench swap 是用户点击触发的独立能力。
- Gate2 / Bench swap / 赛后 / 匹配 / Client UX repair 继续使用最小 writer 边界，不互相放宽 allowlist。
- `LeagueEfficiencyModule` 复用 Dashboard gameflow，不新增第二个常驻 monitor。
- 游戏修复自动模式只监听 LeagueClient 窗口 location-change 事件并做 debounce/cooldown，不新增 LCU 网络轮询。
- League Hub 只保留当前内容页，不把访问过的旧页隐藏常驻。
- 全局快捷键不引入低级键盘钩子或高频键盘轮询。
- 静态霓虹视觉只在正常 WinForms Paint 中绘制，不新增高频动画 Timer。
- InGame Performance budget 继续优先：network / image / disk / background CPU concurrency 1，prefetch 0。

## 冻结的稳定系统

没有真实缺陷或新独立需求时，不重新设计：Modular Host、Performance Contract、UI Text Contract、Single-instance Ensure Open、Flying Runtime / VPet / PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务，以及已验收 League runtime / Gate2-Gate7 / Bench writer / service 边界。

旧 Issue #33 / Draft PR #35 机器猫继续暂停。历史任务分支不删除，除非用户另行明确授权。