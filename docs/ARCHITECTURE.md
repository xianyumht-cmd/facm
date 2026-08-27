# FACM 架构

## 1. 当前双轨架构

FACM 正处于 3.5.15 -> 4.0 的受控迁移期。生产 WinForms 与 4.0 WinUI 并行存在，直到 Gate 13 release 条件满足才允许退休 legacy。

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
└─ independent helper process

FACM 4.0
FACM.App (.NET 10 + WinUI 3)
├─ one main Window / NavigationView / Frame
├─ Pages / dialogs
├─ ViewModels: Core intents/state only
└─ composition root: concrete adapter wiring
        ↓ contracts
FACM.Core (net10.0, UI/platform neutral)
├─ module lifecycle / performance policy
├─ Settings 2.0 + legacy migration contracts
├─ Product State + observability contracts
├─ UI text contracts
├─ cleanup intents/results
├─ League session/read/write capability contracts
└─ online/update contracts
        ↓
FACM.Infrastructure (net10.0)          FACM.Platform.Windows (net10.0-windows)
├─ settings/text persistence           ├─ distribution executable identity
├─ bounded diagnostic persistence      ├─ League process/lockfile discovery
├─ HTTP/public data                    ├─ window/monitor/DPI/filesystem/UAC
├─ League HTTP transport               ├─ single-instance/RegisterHotKey
└─ update metadata/download             └─ child process/replacement integration
```

`FACM4.sln` 是 4.0 并行 solution；旧 `FACM.sln` 在 Gate 13 前持续作为 rollback baseline。

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

App 内：

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

禁止：Core 引用 WinUI/WinForms/WPF/GDI；ViewModel 直接引用 Infrastructure/Platform/HttpClient/File/Process/Registry/具体 League session/URL；Page/Form 自行创建第二套 League session、settings store、diagnostic file reader 或 updater runtime。

`scripts/check-facm4-architecture.ps1` 自动守 project/UI boundary，并禁止迁移 PR 修改生产 release controls。

## 3. Host / Performance

`FACM.Core.Application.FacmHost` 保持拓扑初始化、缺失/重复/循环拒绝、timing、失败反向 rollback、正常反向 Dispose。UI 不拥有模块生命周期。

`FACM.Core.Performance` 是性能预算 owner；优先级固定 `InGame > ChampSelect > hidden/background > Queueing > Client > Desktop`，窗口不可见不能成为游戏中增加后台工作的理由。

## 4. Settings 2.0 / UI Text

### 文件与路径

3.5.15 legacy：distribution EXE 同目录 `settings.ini`。

4.0 Settings 2.0：distribution EXE 同目录 `settings.v2.json`。

两者都必须从 `WindowsExecutablePathProvider.ExecutablePath -> RuntimePathLayout` 推导。禁止使用 `AppContext.BaseDirectory` 作为持久路径，因为 WinUI single-file 可把它指到 `%TEMP%/.net/...`。

### Schema ownership

当前 schema version = `2`：

```text
Environment -> GamePath
Online      -> AutoUpdateEnabled / LastAnnouncementId
Appearance  -> ThemeId
Pets        -> BallX / BallY / StyleId / Enabled
League      -> AutoApplyRecommended / ExitGameHotkey / CloseLobbyHotkey /
               AutoHonorTeammate / AutoReturnLobby / AutoMatchmaking / AutoAccept
```

`Settings2Document / Settings2Validator / Settings2Migration / ISettings2Repository` 在 Core；JSON/file implementation 在 Infrastructure。

### Migration contract

```text
no settings.v2.json
    ├─ settings.ini exists -> parse legacy 15 keys -> typed v2 -> validate -> atomic save
    └─ no legacy          -> validated defaults -> atomic save

settings.v2.json exists
    -> deserialize -> exact schema validation -> use
```

已有 v2 损坏、section 缺失、非法 value 或 future/unknown schema 时 **fail closed**；禁止静默生成默认值覆盖原文件。Gate 13 前 legacy INI 只读保留，迁移成功也不删除，以保证 3.5.15 rollback。

### Atomic persistence

`PhysicalSettings2FileStore` 在目标同目录创建唯一 temp：写入 -> flush -> flush-to-disk -> `File.Move(... overwrite:true)`；失败清理 temp 且不主动破坏旧目标。所有 save 在 IO 前先经过 validator。

### UI Text

`IUiTextProvider` 仍是 framework-neutral 文字 contract；legacy `ui-text.ini` 稳定 key 不因 Settings 2.0 改变。

## 5. League runtime / capability ownership

4.0 只有一个真实 League discovery/auth/session owner：`WindowsLeagueTransportSessionSource`。`LeagueTransportSession` 内部持有 transport secret，公共 `LeagueSessionDescriptor`/诊断不含 password/token。

`LeagueHttpGateway` 的 read/write 共用同一 source；credential 只发给 loopback；read 拒绝 absolute URL；write target 必须由 `LeagueWriteTargetPolicy` 从 capability 产生。

当前迁移 capability：

```text
ApplyMySelection      -> PATCH /lol-champ-select/v1/session/my-selection
CreatePerkPage        -> POST  /lol-perks/v1/pages
UpdatePerkPage(id)    -> PUT   /lol-perks/v1/pages/{positive-id}
SetCurrentPerkPage    -> PUT   /lol-perks/v1/currentpage
```

Bench、Matchmaking、PostGame、Presence、Client UX Repair 等继续保持各自窄 capability。Bench 仍是用户显式手动动作，不变成后台自动抢英雄。

Game Repair 继续保持 native Win32、实际 monitor/working area、多屏/负坐标、WinEvent debounce/cooldown；不恢复 Fix-LCU 第二 runtime。

## 6. Cleanup

```text
Page/ViewModel -> CleanupApplicationService
                  -> ICleanupPlanner / ICleanupExecutor
               <- CleanupPlan / Result / Progress
```

Core 要求 preview + explicit confirmation；Windows adapter 最终必须继续守游戏根目录验证、path allowlist、reparse/junction guard、UAC、执行前重验证、取消与逐项 failure。UI dialog 不拥有删除规则。

## 7. Online / Update

`HttpUpdateManifestSource` 已迁到 .NET 10 Infrastructure：有限 timeout/cancellation、128 KiB metadata cap、strict GitHub Release URL/version/SHA-256 validation。

所有权：

```text
Core: manifest / decision / install intent
Infrastructure: HTTP metadata / mirror / bounded download / validation acquisition
Platform.Windows + Updater: UAC / wait / replace / rollback
```

Updater replacement target 只能来自 distribution executable path。正式更新继续保留 max size、SHA-256、signature/package validation、validated receipt、独立替换、失败保留旧 EXE。

## 8. Product State / Observability

### Product State owner

`FACM.Core.State.ProductStateStore` 是 4.0 唯一 product-state 聚合 store。它不发现 League、不发 HTTP、不写 LCU，只接收其他 owner 发布的事实。

```text
ProductStateSnapshot
├─ Revision
├─ TimestampUtc
├─ Application: Starting / Ready / Degraded / ShuttingDown
├─ League: NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck /
│          ChampSelect / InGame / PostGame / ClientError
├─ Environment: DistributionDirectory / IsElevated / NetworkAvailable
└─ Services: UpdateMetadata / LeagueTransport / PetHost health
```

相同状态不增长 revision、不发 event。state mutation 在 lock 内完成，但 `Changed` subscriber 必须在 lock 外调用，避免 UI/logging 回调形成 lock-order coupling。Page/ViewModel 只消费 `IProductStateReader`；后续 Gate 8 将现有唯一 League runtime/gameflow 的事实映射进 store，不建立第二轮询器。

### Observability owner

Core `DiagnosticEvent` 固定字段：

```text
TimestampUtc / ActionId / Module / DurationMs / Result /
Reason / LeagueState / ClientVersion / Data
```

`DiagnosticEventFactory` 创建事件后立即 redaction；Infrastructure sink 落盘前再次 redaction。`DiagnosticRedactor` 对 token/password/passwd/cookie/authorization/secret/credential/auth 等敏感 key 直接 `[redacted]`，并处理自由文本 `key=value` assignment。

`BoundedJsonLinesDiagnosticSink` 只拥有诊断持久化能力：默认 4 MiB current JSONL，超限 rotate 到 `.1`，并发写通过 `SemaphoreSlim` 串行化；单条事件超过容量时 fail closed。Diagnostics sink 不获得 League writer、网络控制、settings mutation 或 updater 权限。

Gate 9 诊断中心只能消费这些脱敏 contract/文件，不重新发明一套未脱敏日志源。

## 9. WinUI composition root

`FACM.App/App.xaml.cs` 是 concrete adapter composition root，目前创建：

- `WindowsExecutablePathProvider` + `RuntimePathLayout`；
- `Settings2Repository(layout.Settings2Path, layout.SettingsPath)`；
- `HttpUpdateManifestSource`；
- exactly one `WindowsLeagueTransportSessionSource`；
- one shared `LeagueHttpGateway`；
- one `ProductStateStore`；
- one bounded diagnostic sink under stable logs directory；
- ViewModels/MainWindow。

具体 adapter 不得下沉回 ViewModel/Page。Product State 与 diagnostics 只观察/发布事实，不复制 League owner。

## 10. Shell / Desktop / PetHost

控制中心固定四入口：`清理与修复 / LOL 工作台 / 个性化 / 更多设置`；LOL 工作台面向用户固定 `比赛 / 攻略 / 自动化`。

Gate 6 继续构建单 main Window / 单 TitleBar owner / 单 navigation visual tree 的 Design System；Gate 7 的桌面浮动入口属于独立 desktop surface，不恢复 Form-in-Form。

Single Instance = Ensure Open/Activate；快捷键 = RegisterHotKey，不引入低级键盘 hook/永久轮询。PetHost 保持独立进程、IPC、Job Object/parent-pid 生命周期。

## 11. 测试与发布边界

迁移期间持续维护：legacy `FACM Windows Build`、`FACM UI Text Contract`、`FACM 4.0 Foundation`、`FACM.WindowsSmoke` 与各业务 deterministic smoke。已有 smoke 只能迁移或被等价/更强验证替代。

workflow 上传稳定 artifact 名 `facm4-x64`；具体 digest/id 记录在 `PROJECT_STATE.md`，不通过分支名承担历史归档职责。

Gate 13 之前 `online/version.json` / `release/request.json` 保持生产 3.5.15。只有 Gates 0～12、settings 迁移、Updater rollback、Windows 10/11 + DPI/多屏/accessibility 真机矩阵成立后，才允许退休 legacy 并切 FACM 4.0.0。
