# FACM 3.1 / 3.2 架构

> FACM 3.1.3 仍是线上正式版。FACM 3.2 后端采用适配现有 `.NET Framework 4.8 / WinForms` 的 lightweight modular monolith。**Phase 1～5 已全部合并到 `main`**；Phase 5 merge commit 为 `56a3130febc059aae035124ee51041f037fe0993`，该 main 基线已经通过 Windows Build #866 / Mayhem Probe #177。当前不再新增架构 Phase，只做 post-merge canonical docs 收口与单一 Windows candidate 集中验收。正式 Release 仍需独立授权。

# 一、产品进程边界

```text
FACM.exe  (.NET Framework 4.8 / WinForms)
├─ FACM Shell / 控制中心 / 托盘
├─ 内置工具 / Cleanup
├─ Online / Mayhem / LeagueClient
└─ FACM.PetHost.exe  (.NET 8 x64 / WPF / VPet Core，仅启用对应桌面形态时启动)
```

`FACM.exe` 是主进程与应用 Host。VPet 继续由独立 PetHost 承载，保持 Job Object、parent-pid、bundle SHA、ready/fallback 等已验收边界。

默认 `AnimalPetEnabled=false` 不触碰 PetHost payload；FACM Shell 先可见，只有配置已启用桌宠或用户主动选择桌宠后才进入 PetHost/Flying 路线。

# 二、Program 与单实例边界

`Program` 只保留真正属于进程入口的 concern：

- command-line / smoke mode
- ordinary / cleanup / test Mutex
- `SingleInstanceActivation`
- WinForms runtime 初始化
- `RuntimePaths` 最小启动准备
- fatal exception boundary
- FacmHost composition root

普通实例继续使用 Mutex 作为所有权；当前 Windows session 的 AutoResetEvent 只表示无参数 activation。第二次普通启动语义是 **Ensure Open/Activate**，不是 Toggle。

`--cleanup` 与各 smoke/test 使用独立 Mutex，不参与普通实例激活。

# 三、FacmHost 基础层

稳定 namespace：

```text
FACM.AppHost
FACM.AppHost.Modules
```

不要恢复 `FACM.Application`。Build #821 已证明该 namespace 会遮蔽根 namespace 中的 `System.Windows.Forms.Application`，造成大面积 `Application.Run/OpenForms/MessageLoop/...` 编译错误。

`IFacmModule` 契约：

```text
Id
Dependencies
Initialize()
Dispose()
```

`FacmHost` 负责：

- duplicate module ID 拒绝与日志
- missing dependency 拒绝与日志
- circular dependency 检测与 dependency chain
- dependency-topological initialization
- failing module 自 Dispose
- prior-module reverse rollback
- normal reverse Dispose
- success/failure total timing
- per-module timing
- slowest attempted module
- failed module / exception diagnostics

`FACM.exe --facm-host-test` 是 deterministic 架构门禁。

# 四、最终模块依赖图

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

这避免 Shell 随底层基础设施扩展而不断增加直接依赖。

# 五、Settings ownership

Settings 只由模块层初始化：

```text
SettingsModule.Initialize()
  -> AppSettings.Load()
  -> UiTextCatalog.Load()
  -> ShellModule
  -> MainForm(settings, uiText, ...)
```

MainForm 不再自行 Load。已有 `settings.ini` key/default/migration/write-back 保持。

# 六、Tools / Online / Pets / Mayhem ownership

MainForm 当前显式持有 feature facade：

```text
ToolsModule
OnlineModule
PetsModule
MayhemModule
CleanupModule
```

这些 facade 替代 MainForm 对后端具体实现的 direct static/direct-new ownership：

```text
ToolRunner / ToolBundleLoader
OnlineService
AnimalPetManager / PetHostBundleLoader
new MayhemLookupForm()
CleanupProfile / ElevationService / ProcessGuard / GameLocator / SafeCleanupService backend
```

MainForm/CompactMenuForm 仍负责真正的表现层职责，例如窗口、MessageBox、FolderBrowserDialog、CleanupReviewForm、状态文字、用户事件和错误反馈。

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

LeagueClientModule Initialize 同样保持轻量：**不会在 Host startup 枚举进程或要求 LOL 已启动**。真正 LCU discovery 只在 feature consumer 请求本地 LCU 数据时按需发生。

# 八、Cleanup ownership 与线程安全边界

`CleanupModule` facade：

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

`GameLocator` 自己保留搜索预算、取消和 `GameLocatorSearchDialog` worker；`SafeCleanupService.CreatePlan/Execute` 在 WinForms message loop 下继续通过 `BackgroundOperationDialog.Run(...)` 将同步 core 放到 worker thread。

Phase 4 只迁移 ownership，没有复制删除算法、弱化 whitelist/reparse/revalidation，也没有把重工作搬回 UI thread。

# 九、LeagueClient foundation

## 9.1 Phase 5 前的问题

原 `Mayhem/RiotGameDataService.cs` 私有承担：

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

该实现能工作，但如果账号 / Gameflow / ChampSelect / 战绩继续复制，就会重新形成横向耦合。

## 9.2 Session 模型

`LeagueClientSession` 表示 consumer 需要的连接事实：

- process name / id
- port
- protocol
- password/token
- discovery source
- optional platformId / region
- fixed loopback BaseUri

Session 类型不绑定具体 discovery source。

## 9.3 当前 runtime discovery

运行时继续保留 FACM 已实际工作的 lockfile provider：

```text
ProcessLockfileLeagueClientSessionDiscovery
  -> LeagueClientUx / LeagueClient process
  -> process MainModule directory
  -> lockfile
  -> LeagueClientSessionParser.TryParseLockfile
```

Lockfile parser fail-closed：

- 至少 5 个字段
- port 1..65535
- password 非空
- protocol 只允许 http/https

Phase 5 **没有同时替换成 WMI/native command-line discovery**。

## 9.4 League Akari 的借鉴边界

League Akari dev 将 LeagueClientUx observation、command-line credential reader/parser、client installation 检测拆成独立 shard。其 parser 能识别：

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

FACM 提供 deterministic `TryParseCommandLine` 作为未来 provider 格式契约，但当前 runtime 仍只启用 lockfile provider，避免在 ownership 重构时同时引入 WMI/native/elevation 等新变量。

Akari 官网“不支持腾讯服务器”按官方免责声明处理，不推导技术不可用；国服兼容性按源码机制 + 实际国服测试逐项判断。

## 9.5 Session provider

`LeagueClientSessionProvider`：

- on-demand discovery
- healthy session reuse
- disconnected/invalidated 后短 retry interval
- invalidate 只针对 expected session，避免并发旧请求清掉已切换的新 session
- 日志只记录 source/protocol/port/optional platform，**不记录 password/token**

League 未运行返回 null 是正常状态，不传播成 Host initialization failure。

## 9.6 Authenticated local API

`LeagueClientApiClient`：

- BaseAddress 固定 parser 生成的 `http(s)://127.0.0.1:<port>/`
- Basic Auth `riot:<password>`
- local League Client certificate tolerance 只存在于 loopback client
- 2 秒 timeout
- caller cancellation 保持可区分
- healthy session 复用同一个 HttpClient
- session 改变时创建新 client；旧 client 延迟到 module Dispose，避免并发请求中途被强制 Dispose
- 401/403、连接失败、非 caller cancellation 的 timeout 使当前 session invalid
- 普通非成功响应返回 null，由 feature 决定 fallback

不设置伪造的 `FACM/3.2` User-Agent；线上正式产品版本仍是 3.1.3。

## 9.7 Mayhem consumer

`MayhemModule` 构造时接收 `LeagueClientModule`，Dependencies 显式包含 `league-client`。

同一个 `ILeagueClientApi` 沿本地资源链传递：

```text
MayhemModule
  -> MayhemLookupForm
      -> RiotGameDataService
      -> MayhemCardRenderer
          -> MayhemImageCache
              -> RiotGameDataService.DownloadImageAsync
```

`RiotGameDataService` 已删除私有：

- process discovery
- lockfile reading/parsing
- LCU session type
- Basic Auth construction
- per-request local HttpClient construction

`lcu:` 图片链同样不再偷偷重复 discovery。

LCU 返回 null/不可用后继续走既有 CommunityDragon/DataDragon/public fallback，不改变 Mayhem 排名、平衡、腾讯 Patch 等多源业务策略。

# 十、LeagueClient deterministic coverage

`LeagueClientSmokeTest` 并入 `--facm-host-test`：

- valid lockfile
- malformed lockfile
- invalid/out-of-range port
- unsupported protocol
- loopback BaseUri
- Basic Auth parameter
- command-line parser contract
- healthy session caching
- invalidation + refresh
- disconnected module non-fatal

`MayhemSourceSmokeTest` 使用显式 `NoLeagueClientApi`：公网 source health probe 不依赖 GitHub Runner 本机存在 League Client，同时证明“本地 LCU 缺席时 public fallback 仍能完成”。

# 十一、Mayhem 数据边界

Mayhem 继续按字段降级，不让单一第三方决定整次查询。国内优先排行、完整平衡、腾讯 Patch、攻略补充、静态图标分别有独立边界。

LeagueClientModule 只接管本地 LCU transport/session，不改变排行/平衡/腾讯版本/public provider 业务策略。

外部正文取消/大小限制、图片缓存并发、live probe 与 deterministic core CI 分离保持不变。

# 十二、UI / Pets / Online 冻结契约

架构重构已结束，候选验收前冻结：

- FACM Shell 外观与控制中心交互
- 二次启动 Ensure Open
- Flying Runtime 已验收轨迹、朝向、Profile、自由出屏
- VPet/PetHost 进程边界与 ready/fallback
- `settings.ini`
- Cleanup 安全与 worker-thread 边界
- Mayhem 多源容灾与用户可见卡片
- Online Release/manifest transaction

不要为了“架构再漂亮一点”继续拆层或顺手修改已验收行为。

# 十三、Phase 1～5 合并与验证事实

- Phase 1：PR #56 → `8bb44cfef3e9ac24c20390fc60fcd307b7dd612a`
- Phase 2：PR #58 → `64182dddeaa8a89f8d70a31e5ca3307dd2098ba7`
- Phase 3：PR #60 → `974d2bbde73fe78b25052392adc9258c7c20493e`
- Phase 4：PR #64 → `58c27db74d5d9e794872615ad1b78569a040f99b`
- Phase 5：PR #65 → `56a3130febc059aae035124ee51041f037fe0993`

Phase 5 合并后的 canonical main 行为整合已通过：

- FACM Windows Build #866 — SUCCESS
- FACM Mayhem Source Probe #177 — SUCCESS

Issue #66 是纯 post-merge docs 收口，不再改变上述架构或运行时行为。

# 十四、最终候选与集中实机验收

当前不再新增架构 Phase。

Issue #66 合并后：

1. fresh-read main / Issue #66 / online version
2. 等该 docs merge commit 自己的 main Windows Build + Mayhem Probe 全绿
3. 下载该 main workflow artifact 作为**单一 Windows candidate**
4. 再让用户集中实机测试一次
5. candidate 通过后仍不自动 Release，等待单独发布授权

集中验收覆盖 Shell、第二次启动、Flying/VPet、Mayhem（League 开/关两种状态）、Cleanup 识别/预览流程、Online 入口、退出/子进程回收。

如果 candidate 出现真实 Windows 回归，按具体 defect 新开 Issue/branch；**不再为了架构形式继续拆层。**
