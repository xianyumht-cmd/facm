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

> 当前生产事实以上方发布工作流维护区块、GitHub Release 与 `online/version.json` 为准。FACM 4.0 Gate 0/1 不得修改生产更新指向。

## 当前 canonical / 生产冻结线

- canonical branch：`main`。
- Gate 0 开始时 `main`：`1f7d5d5f9e4a16daac68673d8ce387241af4417d`，提交信息 `docs: record FACM 4.0 migration handoff`；它只是在 3.5.15 发布后记录迁移交接，没有覆盖新的正式业务代码。
- 生产仍冻结在 **FACM 3.5.15**：GitHub Latest Release 为 `v3.5.15`，`online/version.json` 为 `enabled=true / version=3.5.15 / minimum_version=3.0.0 / force_update=false`，Release 资产 SHA-256 与在线 manifest 一致。
- 发布工作流 `FACM Publish Release` run #33 / run id `33042472043` 已 SUCCESS。
- 除非出现崩溃、无法启动、数据损坏、更新失效或严重核心功能不可用，3.5.15 不再做大规模 WinForms UI 修补。

## 当前主线：FACM 4.0 Gate 0 — COMPLETE

### Tracking

- Issue：#185 `FACM 4.0 Gate 0：迁移契约、全仓审计与 WinUI 部署原型`
- task branch：`feat/facm-4-gate0`
- delivery PR：#186 `FACM 4.0 Gate 0：迁移契约与 WinUI 部署探针`
- Gate 0 基线：`main@1f7d5d5f9e4a16daac68673d8ce387241af4417d`
- Gate 0 不发布 4.0，不修改 `online/version.json` / `release/request.json`，不删除 legacy，不把 WinUI 原型加入正式 `FACM.sln`。

### 已完成的 Gate 0 工作

- 已完成全仓迁移分类，并写入 `docs/FACM-4-MIGRATION-CONTRACT.md`：使用 `KEEP / EXTRACT / REWRITE / DELETE-LATER / DEFER` 明确旧 FACM、League、Cleanup、Online、Settings、Updater、ToolBundle、PetHost、资源与 CI 的去向。
- 已冻结 3.5.15 迁移不变量：唯一 League session owner、Gate2～Gate7 等窄 writer、Bench 只做用户手动快速选择、Mayhem/OP.GG 取消/超时/缓存预算、原生 Game Repair、Cleanup 安全、Updater 校验/失败保旧版、Single Instance=Ensure Open、RegisterHotKey、PetHost 独立进程、Performance Contract、UI Text Contract、deterministic smoke。
- 已确定目标项目边界：
  - `FACM.App`：.NET 10 + WinUI 3，只负责 Shell / Window / Navigation / ViewModel adapter / visual resources。
  - `FACM.Core`：框架无关的模块图、settings/text/performance/League capability/cleanup/update contract；不得引用 WinUI/WinForms/WPF/GDI。
  - `FACM.Infrastructure`：HTTP、缓存、持久化、update download/mirror routing。
  - `FACM.Platform.Windows`：Win32、WMI/process/lockfile、single-instance、RegisterHotKey、UAC、Windows filesystem、child-process/job、replacement integration。
  - `FACM.PetHost`：继续独立进程；不因主 Shell 换 WinUI 而强制重写。
  - `FACM.Updater`：继续独立替换进程；实现可以改，但 hash/signature/receipt/wait/replace/rollback 语义不可退化。
- 已新增隔离原型 `prototypes/FACM.WinUI.DeploymentProbe`；正式 `src/FACM` 与 `FACM.sln` 未被 WinUI 原型污染。
- 已新增 `.github/workflows/winui-deployment-probe.yml`，用 Windows runner 真正 restore / publish / 启动，而不是只做静态配置检查。

## Gate 0 已核实的技术事实（2026-08-27）

### .NET / Windows App SDK

- FACM 4.0 主目标继续采用 **.NET 10 LTS**。
- Gate 0 CI 实际运行时为 **.NET 10.0.11**。
- .NET 10 支持至 **2028-11-14**。
- Gate 0 时 Microsoft 当前 Windows App SDK Stable 为 **2.4.0**（2026-08-13），不是早期计划中曾提到的 1.8。
- Windows App SDK 最低支持线继续覆盖 Windows 10 1809 / build 17763；Gate 0 probe 使用 `net10.0-windows10.0.19041.0` + `TargetPlatformMinVersion=10.0.17763.0`。
- Gate 1 先做 **x64**，不同时承担 x86 / Arm64 迁移。

### WinUI single-file 的真实语义

Microsoft 支持 unpackaged + self-contained WinUI 3 使用 `PublishSingleFile`，但其含义是：

> 一个可分发 EXE + 首次运行把依赖自解包到临时目录。

它不是零解包的原生单二进制。

Gate 0 probe 使用：

- `WindowsPackageType=None`
- `WindowsAppSDKSelfContained=true`
- `SelfContained=true`
- `EnableMsixTooling=true`
- `IncludeAllContentForSelfExtract=true`
- `PublishSingleFile=true`
- Windows App SDK `2.4.0`

## Gate 0 WinUI deployment probe：真实 CI 证据

### Run

- workflow：`FACM 4.0 WinUI Deployment Probe`
- 首次取证 run #1 / run id：`33044929707`
- 最终 PR head 门禁：WinUI Deployment Probe #3 SUCCESS、FACM Windows Build #1292 SUCCESS、FACM UI Text Contract #413 SUCCESS。
- Windows runner：`Microsoft Windows 10.0.26100` / X64
- restore：SUCCESS
- publish：SUCCESS
- EXE 连续启动两次：SUCCESS
- 内嵌 marker resource：SUCCESS
- artifact：`FACM-4-Gate0-WinUI-Deployment-1`

### Publish 结果

- publish 目录运行时文件数：**1**
- 唯一文件：`FACM.WinUI.DeploymentProbe.exe`
- EXE 大小：**227,426,290 bytes**（约 216.9 MiB）
- EXE SHA-256：`467944A1054E075F436996AD89E048D1A918276071BF3DF1E2F6E6C237ADD6CD`
- artifact ZIP digest：`sha256:cb5c6012067c4a189c22420a3594651afe1e9be4131c5e4a035188dabeff4586`

### 启动证据

CI smoke（不是正式产品性能预算）：

- first launch：**2340 ms**
- second launch：**293 ms**
- 两次 `Environment.ProcessPath` 都指向分发目录中的 `FACM.WinUI.DeploymentProbe.exe`。
- `AppContext.BaseDirectory` 则位于 `%LOCALAPPDATA%/Temp/.net/FACM.WinUI.DeploymentProbe/.../` 自解包目录。
- embedded marker：`FACM-4-GATE0-WINUI-EMBEDDED-RESOURCE-OK`。

### 由证据确定的关键约束

- **Updater 只能把 `Environment.ProcessPath` 对应的分发 EXE 作为替换目标；不得把 `AppContext.BaseDirectory` 当作安装目录或长期资源目录。**
- Runtime/config/cache/update/package/PetHost 数据目录不得依赖 `.net/...` 自解包路径稳定存在。
- FACM 自有内嵌资源继续通过 assembly/resource API 访问，不能通过自解包目录相对路径猜测。
- single-file 技术上可进入 Gate 1，但约 217 MiB 的空壳体积和首次自解包成本已经证明“单 EXE”并不等于轻量；Gate 1 必须继续测真实主程序体积、首启/二启、Defender/SmartScreen 与下载/更新体验。
- GitHub hosted runner 本次 `is_elevated=true`，因此它**不能替代普通用户桌面的 UAC 提升验证**。`--request-elevation-probe` 只证明代码路径可构建；普通权限 -> runas -> elevated child 必须在后续集中 Windows 实机验收中验证。

## Gate 0 结论 / 当前默认路线

### 接受进入 Gate 1 的路线

默认继续：

> .NET 10 LTS + WinUI 3 + Windows App SDK Stable + unpackaged + self-contained + single-file x64

理由：Gate 0 已证明它能在 Windows CI 环境 restore、publish、单 EXE 输出、启动、自解包并读取内嵌资源；同时分发路径与 temp extraction 路径可明确区分。

### 已批准 fallback

如果真实机器证明 single-file 的首启解包、体积、Defender/SmartScreen、更新替换或稳定性不可接受，fallback 是：

> **一个签名安装器 EXE -> self-contained 应用目录 payload**

不因为部署问题退回 WinForms，也不把 MSIX 当默认路线。

## Gate 0 未解决但已显式隔离的风险

这些不是 Gate 0 blocker，但 Gate 1/后续 release gate 必须拿真实证据关闭：

1. 普通非管理员用户下 `runas` UAC 提升与取消路径。
2. Defender / SmartScreen 对约 217 MiB self-extract 单 EXE 的首次扫描、误报和冷启动影响。
3. Windows 10 1809 / 22H2 与 Windows 11 的真实首启、二启、DPI、多屏行为；CI 只覆盖了 runner OS。
4. 4.0 主程序加入真实资源、League、Cleanup、Updater/PetHost 后的最终体积和更新带宽。
5. 旧 3.5.15 `settings.ini` / `ui-text.ini` 到新 Core 的真实升级兼容 fixture。
6. 新 Updater 对 single-file 分发 EXE 的 interrupted replacement / rollback。

## 3.5.15 必须继续保护的稳定资产

### 主程序 / 业务

- `.NET Framework 4.8 + WinForms` 当前生产实现继续作为 rollback baseline。
- `FacmHost` 的显式依赖、拓扑初始化、启动计时、失败反向释放必须迁移到 Core，不在 WinUI 页面里重做一套生命周期。
- 唯一 `LeagueClientModule + LeagueClientSessionProvider` 继续是 LCU discovery/auth/session owner。
- Gate2～Gate7、Bench、Matchmaking、PostGame、Presence、Client UX Repair 等 writer 继续保持最小能力边界。
- Game Repair 继续使用 3.5.15 原生 Win32 / monitor-aware / WinEvent debounce+cooldown 方案，不恢复旧 Fix-LCU-Window 外部 runtime。
- `docs/PERFORMANCE-CONTRACT.md` 和 `docs/UI-TEXT-CONTRACT.md` 仍是 4.0 迁移必须通过的产品契约。

### 部署 / 辅助进程

- 当前主 EXE 内嵌 ToolBundle、Updater 和 PetHost ZIP 的行为只在 4.0 替代方案有等价证据后下线。
- PetHost 当前 `net8.0-windows`、WPF + WinForms、x64、`VPet-Simulator.Core 1.1.0.66`，保持独立进程隔离。
- Updater 当前已具备多源下载、大小上限、SHA-256、signature/package validation、validated receipt、独立 elevated replacement；4.0 必须保留这些语义。
- deterministic smoke 只能迁移/增强，不能因为主 EXE 换框架而静默删除。

## 3.x Legacy UI 已知问题（冻结，不在 Gate 0 修）

- LOL 工作台顶部偶发内容遮挡/重叠；现阶段不再用 `BringToFront / Top += N / Timer / 反射 Enhancer` 堆补丁。
- 悬浮球与控制中心的 anchor placement 不稳定，靠屏幕顶部时偶然看起来正确；4.0 用新的 Shell placement 模型解决。
- 主题未完整作用于默认 `F` 悬浮入口；4.0 统一到 WinUI theme resources/tokens。
- 不继续保留 Form-in-Form 作为新 UI 架构；4.0 使用一个 Shell visual tree。

## Gate 0 完成条件

- [x] Issue / 独立短分支 / Draft PR 已建立。
- [x] 3.5.15 Production / Release / online manifest 已复核并冻结。
- [x] 全仓迁移分类与 Migration Contract 已完成。
- [x] .NET 10 / Windows App SDK / Windows 最低版本已按当前官方资料核实。
- [x] 隔离 WinUI deployment probe 已完成。
- [x] unpackaged + self-contained + single-file publish 已在 Windows CI 通过。
- [x] 单 EXE真实输出、首次/二次启动、分发路径/解包路径、embedded resource 已取证。
- [x] rollback line 与 single-installer fallback 已写明。
- [x] PR #186 最终 head 的 FACM Windows Build / UI Text Contract / WinUI Deployment Probe 全部 green。
- [x] Gate 0 状态、迁移契约、风险与 Gate 1 入口已收束，可直接合入并进入 Gate 1。

## 下一步：Gate 1（Gate 0 delivery PR 合入后直接开始，不需要用户逐步回复“继续”）

Gate 1 的目标不是“马上做完整 UI”，而是建立可长期迁移的并行 4.0 solution foundation：

1. 从合并后的最新 `main` 开新的 Gate 1 短分支；不要继续堆 Gate 0 分支。
2. 创建并行的新项目边界 `FACM.Core / FACM.Infrastructure / FACM.Platform.Windows / FACM.App`，旧 `src/FACM` 继续保留可编译。
3. 第一批抽离 `FacmHost` / module contracts、settings compatibility、UiText contract adapter、Performance Budget；先让 Core 不引用任何 UI framework。
4. 建最小 WinUI Shell / navigation / theme resources，先解决单 visual tree、window ownership、placement/titlebar/theme foundation，不批量迁业务页面。
5. 建 4.0 deterministic tests/CI；旧 smoke 在新 coverage 到位前继续保留。
6. 不切 `online/version.json`，不发布 4.0；生产继续是 3.5.15。

## 明确不要重复的路线

- 不恢复旧 Fix-LCU-Window EXE/mode runtime。
- 不继续对 3.x 遮挡问题堆 WinForms/Z-order/坐标/Timer 补丁。
- 不把当前 `FACM.csproj` 一把改成 WinUI 3 再修几百个错误。
- 不创建长期 `rewrite-everything` 巨型分支。
- 不照搬 3.x 像素坐标、Form-in-Form、GDI shell。
- 不让 WinUI Page/ViewModel 创建第二套 LCU discovery/auth/session、HttpClient/Gameflow monitor 或任意 writer。
- 不让各页面各自轮询当前状态。
- 不为了技术统一强行把 PetHost 塞回主进程。
- 不为了“单 EXE”依赖 self-extract temp 路径。
- 不在 Gate 0/1 触碰正式生产更新。
