# FACM 架构

## 1. 当前双轨架构

FACM 正处于 3.5.15 -> 4.0 的受控迁移期。生产实现与 4.0 foundation 并行存在，直到 Gate 13 release 条件满足才允许退休 legacy。

```text
Production / rollback baseline
FACM.exe (.NET Framework 4.8 / WinForms)
├─ Shell / Floating Ball / Tray
├─ Settings / Online / Cleanup / Tools
├─ Theme / Window Chrome / UI Text
├─ Performance Contract
├─ League Client / Dashboard / Player / Live / OP.GG / Automation / Game Repair / Hub
└─ Mayhem

FACM.PetHost.exe (.NET 8 x64 / WPF / VPet Core)
└─ 独立辅助进程

FACM 4.0 parallel architecture
FACM.App (.NET 10 + WinUI 3)
├─ one Window / NavigationView / Frame
├─ Pages / dialogs
├─ ViewModels: Core intents/state only
└─ composition root: concrete adapter wiring
        ↓ contracts
FACM.Core (net10.0, UI/framework/platform neutral)
├─ module lifecycle
├─ performance policy
├─ settings/text contracts
├─ cleanup intents/results
├─ League session/read/write capability contracts
└─ update manifest/decision/install intents
        ↓
FACM.Infrastructure (net10.0)          FACM.Platform.Windows (net10.0-windows)
├─ settings persistence                ├─ distribution executable identity
├─ text/config persistence             ├─ process/window/monitor/DPI
├─ HTTP/public data                    ├─ filesystem/registry/WMI/UAC
├─ update metadata/download            ├─ single-instance/RegisterHotKey
└─ cache/diagnostic persistence         └─ child process/replacement integration
```

`FACM4.sln` 是 4.0 并行 solution；旧 `FACM.sln` 在 Gate 13 前持续作为可构建 rollback baseline。

## 2. 依赖方向与 UI Intent Boundary

固定 project direction：

```text
FACM.App -> FACM.Core
FACM.App -> FACM.Infrastructure
FACM.App -> FACM.Platform.Windows
FACM.Infrastructure -> FACM.Core
FACM.Platform.Windows -> FACM.Core
FACM.Core -> no UI/platform implementation
```

App 内再分一层：

```text
Page / Window
    ↓ bind / command
ViewModel
    ↓ Core intent/state interface only
Core application/domain contracts
    ↑ implemented by
Infrastructure / Platform.Windows adapters
    ↑ wired only at App composition root
```

禁止：

- Core -> WinUI / WinForms / WPF / System.Drawing；
- Core -> Windows process/filesystem/registry implementation；
- Infrastructure -> App/WinUI；
- Platform.Windows -> App/WinUI；
- ViewModel -> Infrastructure / Platform.Windows / HttpClient / File / Directory / Process / Registry / URL / concrete League session；
- Page/Form 自行创建第二套 League session、HttpClient、settings store 或 updater runtime。

`scripts/check-facm4-architecture.ps1` 自动检查 Core framework boundary、project references、ViewModel forbidden dependencies 与 migration branch production release-control changes。

## 3. Core 模块生命周期

`FACM.Core.Application.IFacmModule`：

```text
Id
Dependencies
Initialize()
Dispose()
```

`FacmHost` 保持 3.5.15 已验收语义：依赖拓扑、重复/缺失/循环拒绝、逐模块 timing、失败模块释放、已初始化模块反向 rollback、正常反向 Dispose。UI 不拥有模块生命周期。

## 4. Performance owner

`FACM.Core.Performance` 是 4.0 性能预算 owner：

| State | Network | Image | Disk | CPU | Prefetch | Poll |
|---|---:|---:|---:|---:|---:|---:|
| Desktop | 4 | 2 | 2 | 2 | 20 | 15s |
| League Client | 3 | 2 | 2 | 2 | 12 | 20s |
| Queueing | 2 | 1 | 1 | 1 | 4 | 30s |
| Champ Select | 2 | 1 | 1 | 1 | 0 | 45s |
| In Game | 1 | 1 | 1 | 1 | 0 | 60s |
| Background | 1 | 1 | 1 | 1 | 0 | 60s |

优先级：`InGame > ChampSelect > hidden/background > Queueing > Client > Desktop`。窗口不可见不能成为游戏中增加后台工作的理由。

## 5. Settings / UI Text

### Gate 2 compatibility state

Gate 2 继续使用 3.5.15 schema，不提前切 Settings 2.0：

- `LegacySettingsCodec` 读写 15 个稳定键；
- `ISettingsRepository` 是 Core persistence contract；
- `IniSettingsRepository` 是 Infrastructure adapter；
- 默认主题 `glass-blue`，默认宠物 `greenfly`；
- `IUiTextProvider` 是 framework-neutral 文字入口；
- Gate 4 才引入 versioned typed schema / validation / atomic save / migration。

3.5.15 当前 `settings.ini` 的正式路径语义是 **distribution EXE 同目录**；旧 `%LOCALAPPDATA%\FACM\settings.ini` 只是 legacy migration source。

WinUI single-file 下持久化路径必须这样推导：

```text
WindowsExecutablePathProvider.ExecutablePath
    -> Path.GetDirectoryName(distribution exe)
    -> settings.ini / future stable runtime layout
```

禁止：

```text
AppContext.BaseDirectory -> persistent settings/cache/runtime
```

因为 single-file 会把 `AppContext.BaseDirectory` 指到 `%TEMP%/.net/...` self-extract 目录。

## 6. League ownership / capability model

### 唯一连接所有者

整个迁移期继续只有一个 League discovery/auth/session owner。Gate 2 只建立 Core contract，没有建立第二实际 connector；当前实际 owner 仍为 legacy `LeagueClientModule + LeagueClientSessionProvider`。

Gate 3 移动实现时必须是“移动 owner”，不是复制 owner。Dashboard、Player、Live、OP.GG、Mayhem、Efficiency、Game Repair、Matchmaking/PostGame/Presence 等只能消费同一个 session/runtime。

### Core contracts

Gate 2 已建立：

```text
ILeagueSessionAccessor
ILeagueReadGateway
ILeagueWriteGateway
LeagueSessionDescriptor
LeagueWriteCommand
LeagueWriteCapability
LeagueWriteTargetPolicy
```

`LeagueWriteCommand` 不携带任意 URL/path。`LeagueWriteTargetPolicy` 根据 capability 产生 exact target；当前迁移范围：

```text
ApplyMySelection      -> PATCH /lol-champ-select/v1/session/my-selection
CreatePerkPage        -> POST  /lol-perks/v1/pages
UpdatePerkPage(id)    -> PUT   /lol-perks/v1/pages/{positive-id}
SetCurrentPerkPage    -> PUT   /lol-perks/v1/currentpage
```

后续 Bench、Matchmaking、PostGame、Presence、Client UX Repair 等继续各自窄 capability，不允许为了迁移方便变成 `Send(method, path, json)` 公共 API。

Bench 仍是用户点击触发的手动快速选择，不允许升级成后台自动抢英雄。

### Game Repair

正式实现保持 native Windows：实际显示器/working area、多屏/负坐标、WinEvent location-change + debounce/cooldown；play-again 复用 post-game writer；restart UX 使用专用最小 writer。禁止恢复第二个 Fix-LCU runtime。

## 7. Cleanup ownership

Gate 2 已把 UI-independent orchestration 抽入 Core：

```text
Page / ViewModel
    -> CleanupApplicationService
       -> ICleanupPlanner
       -> ICleanupExecutor
    <- CleanupPlan / CleanupResult / CleanupProgress
```

`CleanupApplicationService` 要求 explicit confirmation 才执行；Core 不引用 WinForms progress dialog 或 filesystem implementation。

Gate 3/后续 Windows adapter 仍必须保留：游戏根目录验证、path allowlist、reparse-point/junction 防护、UAC、执行前规则重验证、取消、failure per target。UI review dialog 只是展示/确认 owner，不拥有删除规则。

## 8. Online / Update ownership

Gate 2 Core 已建立：

```text
IUpdateManifestSource
IUpdateInstaller
UpdateManifestSnapshot
UpdateDecision
UpdateDecisionService
```

当前 WinUI composition root 使用 `UnavailableUpdateManifestSource`，表示 **transport 尚未迁入**；它不是静默关闭更新架构，也不允许 ViewModel 直接发网络请求。

目标所有权：

```text
Core
  manifest/decision/install intent
        ↓
Infrastructure
  HTTP metadata / mirror routing / bounded download / hash-package acquisition
        ↓
Platform.Windows + FACM.Updater
  UAC / wait / replace / rollback / keep old executable
```

Gate 0 已证明 single-file self-extract 下 `Environment.ProcessPath` = distribution EXE、`AppContext.BaseDirectory` = temporary extraction directory。Updater replacement target 只能来自前者。

更新安全持续保留：max size、SHA-256、signature/package validation、validated receipt、independent replacement、failure keeps old executable runnable。

## 9. WinUI composition root

`FACM.App/App.xaml.cs` 是具体 adapter composition root。Gate 2 当前负责：

- 创建 `WindowsExecutablePathProvider`；
- 从 distribution EXE directory 创建 `IniSettingsRepository`；
- 注入 update manifest source；
- 创建 `ControlCenterViewModel`；
- 创建唯一 `MainWindow`。

`MainWindow` 只消费 ViewModel state；它不解析 settings path、不 new HttpClient、不发现 League process。

后续 Gate 可以把 composition root 拆成更正式的 bootstrapper，但不得把 concrete adapter creation 下沉回 ViewModel/Page。

## 10. Shell 信息架构

控制中心固定四入口：

```text
清理与修复
LOL 工作台
个性化
更多设置
```

LOL 工作台用户分区保持 `比赛 / 攻略 / 自动化`。WinUI Shell 采用单 Window / 单 TitleBar owner / 单 navigation visual tree，禁止复制旧 Form-in-Form 模式。

## 11. Desktop Shell / PetHost

- 默认 `F` 悬浮入口后续由 Gate 7 纳入全局 Theme Resources；
- Anchor Placement Service 必须按所在显示器/边缘/working area 放置，支持负坐标和混合 DPI；
- Single Instance = **Ensure Open / Activate**，不是 toggle；
- 快捷键使用 RegisterHotKey，不引入低级键盘 Hook/永久轮询；
- PetHost 保持独立进程、IPC、Job Object/parent-pid 生命周期，不因 WinUI 迁移并入主 UI 进程。

## 12. 状态与可观测性目标

Gate 5 引入统一 Product State：Application / League / Environment / Services。League 至少覆盖：

`NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError`。

页面订阅 state，不重复轮询。结构化诊断至少携带 `ActionId / Module / Duration / Result / Reason / LeagueState / ClientVersion`，供 Gate 9 诊断中心消费。

## 13. 测试与发布边界

迁移期间同时维护：

- legacy `FACM Windows Build`；
- `FACM UI Text Contract`；
- `FACM 4.0 Foundation`；
- 各业务 deterministic smoke。

已有 smoke 只能迁移或被等价/更强验证替代，不能静默删除。

Gate 13 之前 `online/version.json` / `release/request.json` 保持生产 3.5.15。只有 Gates 0～12 全绿、3.5.15 配置迁移、Updater rollback、Windows 10/11 + DPI/多屏/accessibility 实机矩阵通过后，才允许退休 legacy 并切 FACM 4.0.0。
