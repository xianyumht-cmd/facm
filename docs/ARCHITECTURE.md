# FACM 3.1 / 3.2 架构

> FACM 3.1.3 仍是线上正式版。FACM 3.2 后端采用适配现有 `.NET Framework 4.8 / WinForms` 的 lightweight modular monolith。Phase 1～4 已合并；Phase 5 LeagueClient foundation 当前行为 HEAD `244837f42c1e688fcd1a77d4d6c0b138b40d4031` 已通过 Windows Build #864 / Mayhem Probe #175，待 PR #65 canonical docs/review 收口后合并。正式 Release 仍需独立授权。

# 一、产品进程边界

```text
FACM.exe  (.NET Framework 4.8 / WinForms)
├─ FACM Shell / 控制中心 / 托盘
├─ 内置工具 / Cleanup
├─ Online / Mayhem / LeagueClient
└─ FACM.PetHost.exe  (.NET 8 x64 / WPF / VPet Core，仅启用对应桌面形态时启动)
```

`FACM.exe` 是产品主进程与应用 Host。VPet 继续由独立 PetHost 承载，保持 Job Object、parent-pid、bundle SHA、ready/fallback 等已验收边界。

默认 `AnimalPetEnabled=false` 不触碰 PetHost payload；FACM Shell 先可见，只有配置已启用桌宠或用户主动选择桌宠后才进入 PetHost/Flying 路线。

# 二、单实例与进程级职责

`Program` 只保留真正属于进程入口的 concern：

- command-line / smoke mode；
- ordinary / cleanup / test Mutex；
- `SingleInstanceActivation`；
- WinForms runtime 初始化；
- RuntimePaths 最小启动准备；
- fatal exception boundary；
- FacmHost composition root。

普通实例继续使用 Mutex 作为所有权；当前 Windows session 的 AutoResetEvent 只表示无参数 activation。第二次普通启动语义是 **Ensure Open/Activate**，不是 Toggle。`--cleanup` 与各 smoke/test 继续独立 Mutex。

# 三、FacmHost

稳定 namespace：

```text
FACM.AppHost
FACM.AppHost.Modules
```

不要使用 `FACM.Application`；Build #821 已证明它会遮蔽根 namespace 中的 `System.Windows.Forms.Application`。

`IFacmModule`：

```text
Id
Dependencies
Initialize()
Dispose()
```

`FacmHost` 已负责：

- duplicate module ID 拒绝与日志；
- missing dependency 拒绝与日志；
- circular dependency 检测与 dependency chain；
- dependency-topological initialization；
- failing module 自 Dispose；
- prior-module reverse rollback；
- normal reverse Dispose；
- success/failure total timing；
- per-module timing；
- slowest attempted module；
- failed module / exception diagnostics。

`FACM.exe --facm-host-test` 是 deterministic 架构门禁。

# 四、当前完整模块图

Phase 5 的目标图已经落到代码：

```text
Program
└─ FacmHost
   ├─ CompactMenuEnhancerModule
   ├─ SettingsModule
   ├─ ToolsModule
   ├─ OnlineModule
   ├─ PetsModule
   ├─ LeagueClientModule
   ├─ MayhemModule
   │    └─ depends on LeagueClientModule
   ├─ CleanupModule
   └─ ShellModule
        ├─ depends on CompactMenuEnhancer
        ├─ depends on Settings
        ├─ depends on Tools
        ├─ depends on Online
        ├─ depends on Pets
        ├─ depends on Mayhem
        └─ depends on Cleanup
             ↓
           MainForm
             ↓
        CompactMenuForm
```

Shell **不直接依赖 LeagueClient**。因为 Mayhem 依赖 LeagueClient，Host topology 会自然保证：

```text
LeagueClient -> Mayhem -> Shell
```

这避免 Shell 为每个底层基础设施模块继续扩张 direct dependencies。

# 五、Phase 2 Settings ownership

Settings 只由模块层初始化：

```text
SettingsModule.Initialize()
  -> AppSettings.Load()
  -> UiTextCatalog.Load()
  -> ShellModule
  -> MainForm(settings, uiText, ...)
```

MainForm 不再自行加载。已有 settings.ini key/default/migration/write-back 保持。

# 六、Phase 3 feature facade ownership

MainForm 当前显式持有：

```text
ToolsModule
OnlineModule
PetsModule
MayhemModule
CleanupModule
```

这些 facade 替代 MainForm 对以下后端具体实现的 direct static/direct-new ownership：

```text
ToolRunner / ToolBundleLoader
OnlineService
AnimalPetManager / PetHostBundleLoader
new MayhemLookupForm()
CleanupProfile / ElevationService / ProcessGuard / GameLocator / SafeCleanupService backend
```

MainForm/CompactMenuForm 仍然可以拥有真正的表现层职责：窗口、MessageBox、FolderBrowserDialog、CleanupReviewForm、状态文字、用户事件、错误反馈。

# 七、启动与 warmup

稳定启动顺序：

```text
Program
  -> RuntimePaths.Initialize()      // tiny writable skeleton only
  -> create/register modules
  -> FacmHost.Initialize()
  -> Shell shown
  -> Application.Run(MainForm)
```

Shell shown 后才开始 optional/background warmup：

```text
~180ms head-start
  -> ToolsModule.WarmupAsync()
  -> only if startup AnimalPetEnabled
       -> PetsModule.WarmupAsync()
```

LeagueClientModule Initialize 也保持轻量：**不会在 Host startup 枚举进程或要求 LOL 已启动**。真正 LCU discovery 只在 feature consumer 请求本地 LCU 数据时按需发生。

# 八、Cleanup ownership 与线程边界

`CleanupModule` 当前 facade：

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

UI 路线：

```text
CompactMenuForm
  -> CleanupModule
       -> GameLocator
       -> SafeCleanupService
```

`GameLocator` 自己保留搜索预算、取消和 `GameLocatorSearchDialog` worker；`SafeCleanupService.CreatePlan/Execute` 在 WinForms message loop 下继续使用 `BackgroundOperationDialog.Run(...)` 承载同步 core 到 worker thread。Phase 4 只迁移 ownership，没有复制安全算法，也没有把重工作搬回 UI thread。

# 九、LeagueClient foundation（Phase 5）

## 9.1 旧所有权问题

Phase 5 前，`Mayhem/RiotGameDataService.cs` 私有承担：

```text
LeagueClientUx / LeagueClient process discovery
  -> process.MainModule.FileName
  -> executable directory
  -> lockfile
  -> port/password/protocol
  -> loopback BaseUri
  -> Basic Auth riot:<password>
  -> local certificate tolerance
  -> new HttpClient per request
```

它能工作，但如果账号/Gameflow/ChampSelect/战绩继续各自复制，就会形成新的横向耦合。

## 9.2 Session 模型

`LeagueClientSession` 只表示 consumer 需要的连接事实：

- process name/id；
- port；
- protocol；
- password/token；
- source；
- optional platformId / region；
- fixed loopback BaseUri。

Session 类型不绑定 discovery source。

## 9.3 当前 runtime discovery

Phase 5 保留 FACM 已实际工作的 lockfile provider：

```text
ProcessLockfileLeagueClientSessionDiscovery
  -> LeagueClientUx / LeagueClient process
  -> process MainModule directory
  -> lockfile
  -> LeagueClientSessionParser.TryParseLockfile
```

Lockfile parser fail-closed：

- 至少 5 个字段；
- port 1..65535；
- password 非空；
- protocol 只允许 http/https。

**不在这个架构 Phase 同时替换成 WMI/native command-line discovery。**

## 9.4 Akari 的借鉴方式

League Akari dev 将 LeagueClientUx observation、command-line credential reader/parser、client installation 检测拆成独立 shard。其 parser 能读取：

```text
--app-port
--remoting-auth-token
--app-pid
--rso_platform_id / --rso-platform-id
--region
```

FACM 吸收的是：

```text
discovery source
   ↓
session
   ↓
authenticated API boundary
   ↓
feature consumer
```

Phase 5 提供 `TryParseCommandLine` deterministic contract，给未来 command-line provider 预留格式边界；但当前运行时仍只启用 lockfile provider，避免在 ownership 重构时同时引入 WMI/native/elevation 新变量。

Akari 官网“不支持腾讯服务器”按官方免责声明处理，不推导技术不可用；国服兼容性按源码机制 + 用户实际国服测试逐项判断。

## 9.5 Session provider

`LeagueClientSessionProvider`：

- on-demand discovery；
- healthy session reuse；
- disconnected/invalidated 后短 retry interval；
- invalidate 只针对 expected session，避免并发旧请求清掉已切换的新 session；
- 只记录 source/protocol/port/platform，**不记录 password/token**。

如果 League 未运行，返回 null 是正常状态，不传播成 Host initialization failure。

## 9.6 Authenticated local API

`LeagueClientApiClient`：

- BaseAddress 固定为 parser 生成的 `http(s)://127.0.0.1:<port>/`；
- Basic Auth `riot:<password>`；
- local League Client certificate tolerance 只存在于该 loopback HttpClient；
- 2 秒 timeout；
- caller cancellation 保持可区分；
- healthy session reuse 同一个 HttpClient；
- session 改变时创建新 client；旧 client 延迟到 module Dispose，避免并发请求中途被强制 Dispose；
- 401/403、连接失败、非 caller cancellation 的 timeout 使当前 session invalid；
- 普通非成功响应返回 null，由 feature 决定 fallback。

不设置伪造的 `FACM/3.2` User-Agent；正式版本仍是 3.1.3。

## 9.7 Mayhem consumer

`MayhemModule` 构造时接收 `LeagueClientModule`，Dependencies 显式包含 `league-client`。

同一个 `ILeagueClientApi` 沿整个本地资源链传递：

```text
MayhemModule
  -> MayhemLookupForm
      -> RiotGameDataService
      -> MayhemCardRenderer
          -> MayhemImageCache
              -> RiotGameDataService.DownloadImageAsync
```

`RiotGameDataService` 已删除私有：

- process discovery；
- lockfile reading/parsing；
- LCU session type；
- Basic Auth construction；
- per-request local HttpClient construction。

`lcu:` 图片链同样不再偷偷重新 discovery。

LCU 返回 null/不可用后继续走既有 public fallback，不改变 Mayhem 多源策略。

# 十、LeagueClient deterministic coverage

`LeagueClientSmokeTest` 并入 `--facm-host-test`：

- valid lockfile；
- malformed lockfile；
- invalid/out-of-range port；
- unsupported protocol；
- loopback BaseUri；
- Basic Auth parameter；
- command-line parser contract；
- healthy session caching；
- invalidation + refresh；
- disconnected module non-fatal。

`MayhemSourceSmokeTest` 则明确使用 `NoLeagueClientApi`：它是公网 source health probe，不应依赖 GitHub Runner 上存在 League Client。这样公网 probe 同时证明“本地 LCU 缺席时 public fallback 仍可完成”。

# 十一、Mayhem 数据边界

Mayhem 继续按字段降级，不让单一第三方决定整次查询。国内优先排行、完整平衡、腾讯 Patch、攻略补充、静态图标分别有自己的边界。外部正文取消/大小限制、图片缓存并发、live probe 与 deterministic core CI 的分离全部保持。

LeagueClientModule 只接管本地 LCU transport/session，不改变排名/平衡/腾讯版本/public provider 的业务策略。

# 十二、UI 与桌宠冻结契约

架构阶段默认冻结：

- FACM Shell 外观与控制中心交互；
- 二次启动 Ensure Open；
- Flying Runtime 已验收轨迹、朝向、Profile、自由出屏；
- VPet/PetHost 进程边界与 ready/fallback；
- settings.ini；
- Cleanup 安全边界；
- Online Release/manifest transaction；
- Mayhem 用户可见卡片与多源容灾。

架构重构不得借机“顺手优化”这些已验收行为。

# 十三、验证记录

Phase 5：

- Build #862：FAILED，只因 `MayhemSourceSmokeTest` 两个旧调用点缺新 `ILeagueClientApi` 参数；产品 runtime 代码无其他编译错误。
- 修复为 `NoLeagueClientApi` 后：Build #863 / Probe #174 SUCCESS。
- 清理未发布 User-Agent 与 Renderer 无关格式噪声后，最终行为 HEAD `244837f42c1e688fcd1a77d4d6c0b138b40d4031`：
  - FACM Windows Build #864 SUCCESS；
  - FACM Mayhem Source Probe #175 SUCCESS。

# 十四、整轮架构收口与实机验收

Phase 5 是本轮既定后端架构重构最后一层。PR #65 完成 canonical docs-only CI/review 并合并后：

1. fresh-read main / Issue #63 / online version；
2. 等 main merge commit 的 Windows Build + Mayhem Probe 全绿；
3. 使用 main workflow artifact 作为**单一 Windows candidate**；
4. 再让用户集中实机测试一次；
5. candidate 通过后，不自动 Release，等待用户单独发布授权。

集中测试应覆盖 Shell、二次启动、Flying/VPet、Mayhem（League 开/关两种状态）、Cleanup、Online 入口、退出/子进程回收。

如果 candidate 出现真实回归，按具体 defect 开新 Issue/branch；不再为了“架构更漂亮”继续拆层。
