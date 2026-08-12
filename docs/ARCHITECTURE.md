# FACM 3.1 / 3.2 架构

> FACM 3.1.3 仍是线上正式版；当前 `main` 已包含 FACM 3.2 modular-host Phase 1，Phase 2 正在 PR #58 迁移 Settings ownership。本文区分**已验证事实**与**后续目标**，避免把规划提前写成实现。

## 进程边界

```text
FACM.exe  (.NET Framework 4.8 / WinForms)
├─ FACM Shell / 控制中心 / 托盘
├─ 清理与内置工具
├─ 海斗查询与在线更新
└─ FACM.PetHost.exe  (.NET 8 x64 / WPF / VPet Core，仅启用对应桌面形态时启动)
```

`FACM.exe` 是产品主进程。VPet 继续由独立 PetHost 承载，通过 named pipe 通信；PetHost 加入 FACM 的 Job Object 并保留 parent-pid 守护。PetHost bundle 按内嵌 ZIP SHA-256 缓存/释放，默认 `AnimalPetEnabled=false` 不预热 PetHost。Shell 在 PetHost ready 前始终保持可见，失败时继续作为回退入口。

这条进程边界在 3.2 后端重构期间冻结，不为了模块化强制合并 CLR/UI 技术栈。

## 单实例边界

普通 FACM 仍由 Mutex 保持单实例；当前 Windows session 的命名 AutoResetEvent 只传递无参数“激活现有控制中心”信号：

```text
第二次 FACM.exe
  -> 普通 Mutex 已占用
  -> 最多约 1.6s 有限重试 activation event
  -> Set()
  -> 第一实例 MainForm.RequestExternalActivation()
       -> 未打开：创建控制中心
       -> 已打开：BringToFront / Activate
```

外部激活是 **Ensure Open**，不是 Toggle。`--cleanup` 与各 smoke/test 使用独立 Mutex，不参与普通实例 activation。

## Shell / UI 稳定契约

- `MainForm` 是 FACM Shell 的 WinForms 表现层；`CompactMenuForm` 是轻量控制中心。
- 默认 Shell 使用透明分层窗口；左键开控制中心、拖动保存位置、右键托盘菜单。
- `AnimalPetEnabled=false` 表示使用 FACM Shell；启用桌宠后才进入对应 Pets/PetHost 路线。
- 控制中心「主题」统一管理面板外观与桌面形态，但 `ThemeId` 与 `AnimalPetEnabled/PetStyleId` 继续保持独立配置语义。
- `CompactMenuEnhancer` 仍负责既有首帧兼容布局，Phase 1 只改变其初始化所有权，不重写表现行为。
- 后端架构重构默认不改变用户可见 UI/交互。

## 清理边界

`SafeCleanupService` 继续拥有：白名单、reparse point 阻止、预览、执行前二次校验和删除规则。耗时扫描/删除在 WinForms 调用路径中通过 `BackgroundOperationDialog` 离开 UI 线程；模块化不得复制或弱化安全算法。

## Mayhem 边界

海斗继续使用字段级多源合并：

```text
英雄名/别名
├─ Hexdata                  国内优先排行/胜率
├─ ARAMMayhem               完整当前平衡/备用排行
├─ OP.GG                    可选攻略字段
├─ 腾讯 LOL 官网            国服 Patch / 本版本增量
└─ LCU -> DataDragon/CDN    静态英雄/装备/图标
        -> MayhemChampionResult
        -> MayhemCardRenderer
```

单一第三方失败不能抹掉其它已获得字段；完整平衡数据 Patch 不匹配国服当前 Patch 时不得冒充最新状态。核心 CI 只跑 deterministic smoke，真实公网健康由独立 Mayhem Source Probe 负责。

## 发布边界

正式交付仍是单 `FACM.exe`，匹配 PetHost bundle 嵌入主 EXE。Release / online manifest 是独立事务；架构 PR、CI artifact、内部 Phase 合并都不等于发布授权。

---

# FACM 3.2 Modular Host

## 设计目标

FACM 学习 League Akari 的成熟架构原则，但不复制其 Electron/Vue/TypeScript/renderer IPC 技术栈：

- 稳定模块 ID；
- 显式依赖；
- 模块所有权；
- 统一 Initialize / Dispose 生命周期；
- 缺失、重复、循环依赖确定性失败；
- 依赖顺序初始化 / 反向释放；
- 成功和失败路径都有 timing / diagnostics；
- Settings / state / controller 逐步归属 feature；
- UI 逐步退回表现与命令转发层。

FACM 采用适合 net48 的 lightweight modular monolith，不默认引入大型 DI 容器。

## Phase 1：FacmHost 基础层（已合并）

Issue #55 / PR #56，merge commit `8bb44cfef3e9ac24c20390fc60fcd307b7dd612a`。

稳定 namespace：

```text
FACM.AppHost
FACM.AppHost.Modules
```

不要使用 `FACM.Application`：Build #821 已证明它会遮蔽根 namespace 中的 `System.Windows.Forms.Application`。

`IFacmModule`：

```text
Id
Dependencies
Initialize()
Dispose()
```

`FacmHost` 已负责：

- duplicate ID 检测 + 日志；
- missing dependency 检测 + 日志；
- circular dependency chain + 日志；
- topological initialization；
- 初始化失败模块自身 Dispose；
- prior modules reverse rollback；
- 正常退出 reverse Dispose；
- success/failure 总耗时、每模块耗时、slowest module 和失败详情。

`FACM.exe --facm-host-test` 是 deterministic 门禁，覆盖依赖图、rollback、first-module failure 和 timing/report。

Phase 1 后的正常启动：

```text
Program
  -> FacmHost
       -> CompactMenuEnhancerModule
       -> ShellModule
            -> MainForm
  -> SingleInstanceActivation listener
  -> Application.Run(shell.MainForm)
  -> Host reverse Dispose
```

## Phase 2：Settings ownership（PR #58 当前实现）

Phase 2 将 Settings/UiText 从 MainForm 的隐式全局加载变成 Host 模块图中的显式依赖。

当前实现：

```text
Program
  -> create SettingsModule
  -> create ShellModule(startCleanup, settingsModule)
  -> FacmHost.Register(
       CompactMenuEnhancerModule,
       SettingsModule,
       ShellModule)
  -> FacmHost.Initialize()
       -> CompactMenuEnhancerModule
       -> SettingsModule
            -> AppSettings.Load()
            -> UiTextCatalog.Load()
       -> ShellModule
            -> MainForm(settings, uiText, startCleanup)
```

`ShellModule.Dependencies` 明确包含：

```text
shell.compact-menu-enhancer
settings
```

`MainForm` 当前构造契约：

```text
MainForm(AppSettings settings, UiTextCatalog ui, bool startCleanup = false)
```

因此正常产品路径中：

- MainForm 不再决定配置如何加载；
- Settings 只由 `SettingsModule.Initialize()` 加载一次；
- Shell 只有在 Settings 初始化成功后才能创建；
- MainForm 继续使用同一个 `AppSettings` 实例执行原 `_settings.Save()`；
- `settings.ini` 的 key/default/migration/write-back 语义没有改变。

`FloatingBallSmokeTest` 等测试构造点同样必须显式提供依赖，禁止为了方便测试恢复 `MainForm(bool)` 隐式加载重载。

Phase 2 当前行为代码 HEAD `235299eda170835d13c4035efa617d433db306a3`：Build #846 SUCCESS，Mayhem Source Probe #159 SUCCESS。

## 已建立、待接管所有权的 facade

```text
ToolsModule
PetsModule
OnlineModule
MayhemModule
```

这些 facade 在 Phase 1 已建立，当前仍主要包住现有成熟实现。下一阶段将由 Host 注册，并由 Shell/MainForm 显式接收：

```text
Shell
├─ Settings
├─ Tools
├─ Online
├─ Pets
└─ Mayhem
```

### MainForm 下一步要迁出的 direct dependencies

```text
ToolRunner / ToolBundleLoader
OnlineService
AnimalPetManager / PetHostBundleLoader
new MayhemLookupForm()
```

迁移时保持现有业务时序，尤其：

- Shell 先可见；
- 后台 warmup 保留约 180ms head-start；
- ToolBundle 可以后台准备；
- 只有启动时配置已经启用桌宠才预热 PetHost；
- Pets ready/fallback、Online prompt、Mayhem UI 行为不变。

## 后续结构

完成现有 ownership 迁移后的目标：

```text
Program
└─ FacmHost
   ├─ Settings
   ├─ Tools
   ├─ Online
   ├─ Pets
   ├─ Mayhem
   ├─ Cleanup
   ├─ Shell
   └─ LeagueClient
```

后续顺序：

1. Tools / Online / Pets / Mayhem facade 接管 MainForm direct dependencies；
2. Cleanup ownership；
3. 建立真正的 LeagueClient module foundation，统一客户端发现/连接/session/API 边界；
4. 架构重构整体收口后生成一个 Windows 候选包集中实机验收；
5. 候选接受后，再在新架构上增加账号 / Gameflow / ChampSelect / 战绩等产品能力。

## 迁移期间冻结契约

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

技术实现仍按依赖顺序分层，但**内部 Phase 不逐轮要求用户 Windows 实机测试**。每层通过 compile + deterministic smoke + AppLog + Actions 后继续推进；整轮既定后端架构重构完成后再提供一个单一 Windows 候选包进行集中验收。