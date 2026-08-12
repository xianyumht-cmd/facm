# FACM 3.1 / 3.2 架构

> FACM 3.1.3 仍是线上正式版。FACM 3.2 正在按 modular-host 路线重构后端：Phase 1 Host 基础层和 Phase 2 Settings ownership 已合并；Phase 3 PR #60 已把 Tools / Online / Pets / Mayhem 变成 Shell 的显式模块依赖。本文区分已验证事实与后续目标。

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

`SafeCleanupService` 继续拥有路径白名单、reparse point 阻止、预览、执行前二次校验和删除规则。耗时扫描/删除在 WinForms 调用路径中通过 `BackgroundOperationDialog` 离开 UI 线程。Phase 4 只迁移调用所有权，不复制或弱化安全算法。

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

## Phase 3：Shell feature facade ownership（PR #60 当前实现）

Phase 3 将 MainForm 对具体后端 static service/direct-new 的依赖迁入四个已存在 module facade。

### 当前正常启动图

```text
Program
  -> create SettingsModule
  -> create ToolsModule
  -> create OnlineModule
  -> create PetsModule
  -> create MayhemModule
  -> create ShellModule(...all modules...)
  -> FacmHost.Register(
       CompactMenuEnhancer,
       Settings,
       Tools,
       Online,
       Pets,
       Mayhem,
       Shell)
  -> FacmHost.Initialize()
       -> CompactMenuEnhancerModule
       -> SettingsModule
       -> ToolsModule
       -> OnlineModule
       -> PetsModule
       -> MayhemModule
       -> ShellModule
            -> MainForm(settings, uiText, tools, online, pets, mayhem)
  -> SingleInstanceActivation listener
  -> Application.Run(shell.MainForm)
  -> Host reverse Dispose
```

`ShellModule.Dependencies` 当前按以下顺序锁定：

```text
shell.compact-menu-enhancer
settings
tools
online
pets
mayhem
```

### MainForm 后端入口

MainForm 现在使用：

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

### Warmup 时序保持

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

MainForm 退出/关闭时仍按原时机调用 `_pets.Stop()`，Host 的 `PetsModule.Dispose()` 只承担最终生命周期兜底，避免为了“去重漂亮”改变现有时序。

Phase 3 行为代码 HEAD `10a81d38a530e99eb77eab1a7d2f1c19c46e9279`：FACM Windows Build #851 SUCCESS，Mayhem Source Probe #164 SUCCESS。

---

# 后续架构

## Phase 4：Cleanup ownership

目标建立 `CleanupModule`，把 `ProcessGuard / ElevationService / SafeCleanupService / GameLocator` 等后端调用从 `CompactMenuForm` 收进明确 facade。

UI 仍负责：

- 用户确认；
- `FolderBrowserDialog`；
- `CleanupReviewForm`；
- 状态文字与按钮状态。

CleanupModule 只承接 backend ownership，不能复制 `SafeCleanupService` 安全规则。

## Phase 5：LeagueClient foundation

现有 LCU 客户端发现逻辑主要散在 Mayhem 的 `RiotGameDataService`：通过 `LeagueClientUx.exe` command line 解析 app-port/remoting-auth-token，并构造 localhost LCU HttpClient。

Phase 5 应建立真正的 `LeagueClientModule`：

```text
LeagueClientModule
├─ client discovery
├─ LCU credentials/session
├─ connection state
├─ reusable authenticated HTTP/API boundary
└─ diagnostics
```

然后让 Mayhem 等现有功能逐步复用该模块，而不是继续各自发现客户端。Phase 5 暂不新增账号/Gameflow/ChampSelect/战绩 UI；先把客户端连接所有权做对。

## 架构重构收口

完成 Cleanup + LeagueClient foundation 后，当前这一轮“学习 Akari 的成熟后端架构”可进入整体候选阶段：

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
- Mayhem 字段级多源容灾；
- Online Release/manifest 事务；
- 无独立产品需求时不改变现有 UI；
- 不自动发布正式版本。

## 验收节奏

内部 Phase 不逐轮要求用户 Windows 实机测试。每层通过 compile + deterministic smoke + AppLog + Actions 后继续推进；整轮既定后端架构重构完成后再提供单一 Windows 候选包集中验收。