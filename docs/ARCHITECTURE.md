# FACM 3.1 / 3.2 架构

> FACM 3.1.3 仍是线上正式版。FACM 3.2 正在按 modular-host 路线重构后端：Phase 1 Host 基础层、Phase 2 Settings ownership、Phase 3 Tools / Online / Pets / Mayhem facade ownership 已合并；Phase 4 Cleanup ownership 的完整修正位于 PR #64，行为代码已通过 Build #858 / Probe #169。本文区分已验证事实与后续目标。

## 进程边界

```text
FACM.exe  (.NET Framework 4.8 / WinForms)
├─ FACM Shell / 控制中心 / 托盘
├─ 清理与内置工具
├─ 海斗查询与在线更新
└─ FACM.PetHost.exe  (.NET 8 x64 / WPF / VPet Core，仅启用对应桌面形态时启动)
```

`FACM.exe` 是产品主进程。VPet 继续由独立 PetHost 承载，通过 named pipe 通信；PetHost 加入 FACM 的 Job Object 并保留 parent-pid 守护。PetHost bundle 按内嵌 ZIP SHA-256 缓存/释放，默认 `AnimalPetEnabled=false` 不预热 PetHost。Shell 在 PetHost ready 前始终保持可见，失败时继续作为回退入口。

## 单实例边界

普通 FACM 由 Mutex 保持单实例；当前 Windows session 的命名 AutoResetEvent 只传递无参数“激活现有控制中心”信号。外部激活是 **Ensure Open**，不是 Toggle。`--cleanup` 与各 smoke/test 使用独立 Mutex，不参与普通实例 activation。

## Shell / UI 稳定契约

- `MainForm` 是 FACM Shell 的 WinForms 表现层；`CompactMenuForm` 是控制中心。
- 默认 Shell 左键开控制中心、拖动保存位置、右键托盘菜单。
- `AnimalPetEnabled=false` 表示使用 FACM Shell；启用桌宠后才进入 Pets/PetHost 路线。
- `CompactMenuEnhancer` 仍负责既有首帧兼容布局；模块化只改变初始化所有权，不重写表现行为。
- 后端架构重构默认不改变用户可见 UI/交互。

## Cleanup 稳定边界

`SafeCleanupService` 继续拥有路径白名单、reparse point 阻止、预览、执行前二次校验和删除规则。其公开 `CreatePlan/Execute` 在 WinForms message loop 下通过 `BackgroundOperationDialog.Run(...)` 把 `CreatePlanCore / ExecuteCore` 放到 worker thread；非 UI/test 调用继续走同步 core。`GameLocator` 自身同样负责搜索预算、取消和 WinForms 进度窗口。

Phase 4 只迁移调用所有权到 `CleanupModule`，**不复制、不下沉 UI，也不弱化这些安全/线程语义**。

## Mayhem 稳定边界

海斗继续使用字段级多源合并：Hexdata 国内优先排行/胜率，ARAMMayhem 完整当前平衡/备用排行，OP.GG 可选攻略，腾讯 LOL 官网提供国服 Patch/本版本增量，LCU/DataDragon/CommunityDragon 提供静态元数据。单一第三方失败不能抹掉其它字段；Patch 不匹配时不得冒充最新完整状态。核心 CI 与 live source probe 保持分离。

## 发布边界

正式交付仍是单 `FACM.exe`，匹配 PetHost bundle 嵌入主 EXE。Release / online manifest 是独立事务；架构 PR、CI artifact、内部 Phase 合并都不等于发布授权。

---

# FACM 3.2 Modular Host

## 设计原则

FACM 学习 League Akari 的成熟架构原则，但不复制 Electron/Vue/TypeScript/renderer IPC：

- 稳定模块 ID；
- 显式依赖；
- feature 所有权；
- 统一 Initialize / Dispose 生命周期；
- 缺失、重复、循环依赖确定性失败；
- 依赖顺序初始化 / 反向释放；
- success/failure timing 与日志；
- Settings / state / controller 逐步归属 feature；
- UI 逐步退回表现与命令转发层。

稳定 namespace：`FACM.AppHost` / `FACM.AppHost.Modules`。不要使用 `FACM.Application`，Build #821 已证明它会遮蔽 `System.Windows.Forms.Application`。

## Phase 1：FacmHost 基础层（已合并）

Issue #55 / PR #56，merge commit `8bb44cfef3e9ac24c20390fc60fcd307b7dd612a`。

`IFacmModule` 提供 `Id / Dependencies / Initialize / Dispose`。`FacmHost` 已负责 duplicate/missing/circular dependency 检测、topological init、失败模块自 Dispose、prior-module rollback、reverse Dispose，以及成功/失败的 per-module/total/slowest timing。

`FACM.exe --facm-host-test` 是 deterministic 门禁。

## Phase 2：Settings ownership（已合并）

Issue #57 / PR #58，merge commit `64182dddeaa8a89f8d70a31e5ca3307dd2098ba7`。

正常产品 Settings 数据流：

```text
SettingsModule.Initialize()
  -> AppSettings.Load()
  -> UiTextCatalog.Load()
  -> ShellModule
  -> MainForm(settings, uiText, ...)
```

MainForm 不再自行加载 Settings/UiText；继续使用同一个 `AppSettings` 实例按原时机 Save。`settings.ini` key/default/migration/write-back 不变。

## Phase 3：Shell feature facade ownership（已合并）

Issue #59 / PR #60，merge commit `974d2bbde73fe78b25052392adc9258c7c20493e`。

Phase 3 将 MainForm 对具体后端 static service/direct-new 的依赖迁入四个 module facade：

```text
_tools
_online
_pets
_mayhem
```

替代：

```text
ToolRunner / ToolBundleLoader
OnlineService
AnimalPetManager / PetHostBundleLoader
new MayhemLookupForm()
```

MainForm 仍可引用 UI/模型类型，例如 `AnimalPetPickerForm`、`AnimalPetCatalog`、`OnlineCenterForm`、`OnlineSnapshot`。目标是迁移 backend ownership，不是禁止 UI 层引用任何 feature 类型。

Warmup 时序保持：

```text
Shell shown
  -> BeginBackgroundWarmup()
       -> background task
       -> ~180ms head-start
       -> _tools.WarmupAsync()
       -> if startup AnimalPetEnabled
            -> _pets.WarmupAsync()
```

因此默认 Shell 路线仍不预热 PetHost。Pets ready/fallback、Online prompt、Mayhem modal、Tool error UI 没有因 facade 迁移改变。

Phase 3 最终 docs-only Build #853 / Probe #166 SUCCESS。

## Phase 4：Cleanup ownership（PR #64 完整候选）

### 超时恢复边界

聊天 UI 超时期间，远端操作继续执行，导致 PR #62 以 HEAD `e15877ac...` 提前合并到 `main`；该 PR 只包含最早的 `CleanupModule.cs` 草稿，却因 `Closes #61` 自动关闭了 Issue #61。它不构成 Phase 4 完成状态。

恢复时没有回滚 main、没有 reset/rebase/force-push，而是：

- 重开 Issue #61；
- 继续原任务分支；
- 用 PR #64 只提交 #62 之后的完整修正。

### 当前完整依赖图

```text
Program
  -> create SettingsModule
  -> create ToolsModule
  -> create OnlineModule
  -> create PetsModule
  -> create MayhemModule
  -> create CleanupModule
  -> create ShellModule(...all modules...)
  -> FacmHost.Register(
       CompactMenuEnhancer,
       Settings,
       Tools,
       Online,
       Pets,
       Mayhem,
       Cleanup,
       Shell)
  -> FacmHost.Initialize()
       -> CompactMenuEnhancerModule
       -> SettingsModule
       -> ToolsModule
       -> OnlineModule
       -> PetsModule
       -> MayhemModule
       -> CleanupModule
       -> ShellModule
            -> MainForm(settings, uiText, tools, online, pets, mayhem, cleanup)
                 -> CompactMenuForm(..., cleanup)
  -> SingleInstanceActivation listener
  -> Application.Run(shell.MainForm)
  -> Host reverse Dispose
```

`ShellModule.Dependencies` 当前锁定：

```text
shell.compact-menu-enhancer
settings
tools
online
pets
mayhem
cleanup
```

### CleanupModule 的真实 facade

```text
IsConfigured
IsAdministrator
RestartElevatedForCleanup()
GetRunningRelatedProcesses()
FindGameRoot()
ResolveGameRoot(path)
IsValidGameRoot(path)
CreatePlan(gameRoot)
Execute(plan) -> CleanupResult
```

`CompactMenuForm` 继续负责用户交互：

- MessageBox；
- `FolderBrowserDialog`；
- `CleanupReviewForm`；
- 状态文字和按钮状态。

但不再自己访问 `CleanupProfile / ElevationService / ProcessGuard / GameLocator / SafeCleanupService` backend。

### 保留的服务语义

```text
CompactMenuForm
   -> CleanupModule
       -> GameLocator
            -> WinForms message loop: GameLocatorSearchDialog + worker search
       -> SafeCleanupService.CreatePlan
            -> WinForms message loop: BackgroundOperationDialog + CreatePlanCore worker
       -> SafeCleanupService.Execute
            -> WinForms message loop: BackgroundOperationDialog + ExecuteCore worker
```

因此 Phase 4 只是显式所有权迁移，没有把耗时 core 搬回 UI 线程，也没有复制删除算法。

### Phase 4 自动验证

- Build #857：FAILED；原因仅为 `CompactMenuForm` 构造参数 `cleanup` 与旧 UI 局部变量 `var cleanup` 同名，CS0841 / CS0136。
- 修复：构造参数改为 `cleanupModule`，UI 局部变量和行为不动。
- 行为 HEAD `16cefad9162de302de68478cde2a3d6ed9b49d0c`：
  - FACM Windows Build #858 SUCCESS；
  - FACM Mayhem Source Probe #169 SUCCESS。

---

# Phase 5：LeagueClient foundation

## 当前真实 LCU 技术债

现有 LCU ownership 主要藏在 `Mayhem/RiotGameDataService.cs`，不是独立应用模块。

当前 `DiscoverLcuSession()` 的真实实现是：

```text
Process.GetProcessesByName("LeagueClientUx" / "LeagueClient")
  -> process.MainModule.FileName
  -> executable directory
  -> directory/lockfile
  -> split lockfile fields
       port = parts[2]
       password = parts[3]
       protocol = parts[4] (fallback https)
  -> protocol://127.0.0.1:<port>/
```

当前 authenticated request：

```text
HttpClientHandler
  -> allow League Client local self-signed certificate
HttpClient
  -> BaseAddress = local LCU URL
  -> Timeout = 2s
Authorization = Basic base64("riot:" + password)
GET <LCU path>
```

Mayhem 目前用这条路线读取 `/lol-game-data/assets/v1/...`；LCU 不可用时继续回退 CommunityDragon/DataDragon。

问题不在于这段逻辑“不能用”，而在于：

- discovery/auth/session 所有权藏在 Mayhem；
- 每次 bytes 请求都重新发现 session；
- 每次请求都新建 handler/client；
- 后续账号、Gameflow、ChampSelect、战绩如果继续各自复制，会重新形成横向耦合。

## Phase 5 目标结构

```text
LeagueClientModule
├─ LeagueClientLocator / lockfile parser
├─ LeagueClientSession
├─ authenticated local HTTP/API client
├─ bounded timeout + cancellation
├─ connection/session diagnostics
└─ deterministic discovery/parser/API smoke
```

关键设计：

- League Client 未运行是正常状态，不能让 FACM Host 启动失败；
- credentials 不向 UI/普通 feature 广泛暴露；
- local certificate tolerance 只封装在本地 LCU client；
- session 可按需发现/刷新，不把过期 lockfile 结果永久缓存；
- Mayhem 现有 LCU-first → public fallback 行为必须保持；
- `MayhemModule` 应显式依赖 `LeagueClientModule`；
- 依赖继续传给 `MayhemLookupForm / RiotGameDataService` 或新的实例化 metadata service；
- **禁止为了少改代码新增全局 static LeagueClient singleton**。

Phase 5 暂不新增账号/Gameflow/ChampSelect/战绩 UI；先把连接所有权做对。后续产品能力全部复用这个边界。

腾讯/国服兼容性以实际客户端机制和国服实测为准，不根据 League Akari 官网“不支持腾讯服务器”的免责声明推导技术不可用。

---

# 架构重构收口

完成 Phase 5 后，当前这一轮“学习 Akari 的成熟后端架构”进入整体候选阶段：

1. canonical docs 与真实依赖图收口；
2. 全部 deterministic smoke + Windows Build + Mayhem Probe；
3. 生成一个单一 Windows candidate artifact；
4. 用户集中做一次 Shell、二次启动、桌宠、海斗、清理、更新入口等实机验收；
5. 候选接受后，再在新架构上增加账号 / Gameflow / ChampSelect / 战绩等产品能力。

## 冻结契约

- 单实例 Mutex + AutoResetEvent Ensure Open；
- `--cleanup` / smoke 独立 Mutex；
- Flying Runtime 已验收行为；
- VPet/PetHost 独立进程与 ready/fallback；
- `settings.ini` 兼容；
- Cleanup 安全算法 / BackgroundOperationDialog / GameLocator 预算语义；
- Mayhem 字段级多源容灾；
- Online Release/manifest 事务；
- 无独立产品需求时不改变现有 UI；
- 不自动发布正式版本。

## 验收节奏

内部 Phase 不逐轮要求用户 Windows 实机测试。每层通过 compile + deterministic smoke + AppLog + Actions 后继续推进；整轮既定后端架构重构完成后再提供单一 Windows 候选包集中验收。
