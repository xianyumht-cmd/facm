# FACM 架构

## 1. 双轨迁移

FACM 3.5.15 WinForms 仍是生产/回滚基线；FACM 4.0 使用 .NET 10 + WinUI 3 并行迁移。Gate 13 前不退休 legacy、不修改 production release controls。

```text
FACM.App (.NET 10 + WinUI 3)
├─ MainWindow: one AppTitleBar + one NavigationView + one Frame
├─ FloatingWindow: narrow F desktop entry
├─ ViewModels: Core intents/state only
└─ composition root
        ↓
FACM.Core (platform/UI neutral)
├─ module lifecycle / performance
├─ Settings 2.0 / UI Text
├─ Product State / observability
├─ Desktop placement geometry
├─ Cleanup
├─ League gameflow/capability/session contracts
└─ Online/update contracts
        ↓
FACM.Infrastructure                 FACM.Platform.Windows
├─ settings/text/diagnostic IO      ├─ executable/runtime identity
├─ HTTP/League transport            ├─ League discovery/session owner
├─ one Gameflow monitor             ├─ monitor/work-area/DPI facts
└─ update metadata                  └─ UAC/hotkey/process integration
```

固定 project direction：App -> Core/Infrastructure/Platform.Windows；Infrastructure -> Core；Platform.Windows -> Core；Core 不引用 UI/platform implementation。

## 2. UI Intent / State Boundary

```text
Window/Page -> ViewModel -> Core intent/state
                            ↑
            Infrastructure / Platform adapters
            wired only in App composition root
```

禁止 ViewModel/Page 直接 new HttpClient、League runtime、settings/diagnostic file store、Process/Registry/Win32 implementation。页面不得自己维护 LCU polling/state cache。

`scripts/check-facm4-architecture.ps1` 守 project direction / ViewModel boundary / production release-control protection；Gate-specific source gates继续叠加更窄 ownership 约束。

## 3. Host / Performance

`FacmHost` 继续守拓扑初始化、缺失/重复/循环拒绝、timing、失败反向 rollback、正常反向 Dispose。

Performance owner 在 Core；优先级保持 `InGame > ChampSelect > hidden/background > Queueing > Client > Desktop`。隐藏窗口不能成为游戏中增加后台工作的理由。

Gate 8 起，League gameflow Product State 与 Performance activity 必须来自**同一个 gameflow mapping**；禁止 UI 根据自己轮询到的 phase 单独修改 performance budget。

## 4. Settings 2.0 / UI Text / Runtime Paths

稳定路径：

```text
<distribution>/settings.ini      legacy rollback/migration source
<distribution>/settings.v2.json  FACM 4.0 typed settings
<distribution>/ui-text.ini       optional text override
<distribution>/logs/...          diagnostics
```

所有稳定路径只从 distribution executable (`Environment.ProcessPath`) 推导，不使用 single-file self-extract `AppContext.BaseDirectory`。

Settings 2.0 schema v2 覆盖 Environment / Online / Appearance / Pets / League；legacy 15-key import 后旧 INI 仍保留。坏 JSON、非法值、future schema fail closed；atomic save 使用 same-directory temp + flush-to-disk + replace/move。

`IUiTextProvider` 是用户可见文字 contract。Main Shell、F desktop entry、LOL Workbench copy 均通过 `UiTextKeys`；`FileUiTextProvider` 失败 fallback defaults。

## 5. League runtime / capability ownership

### Session / transport owner

4.0 exactly one discovery/auth/session owner：`WindowsLeagueTransportSessionSource`。`LeagueHttpGateway` read/write 共用它；secret 不进入公共 descriptor/Product State/diagnostics；credential 只发 loopback。

当前 Core write policy：

```text
ApplyMySelection      -> PATCH /lol-champ-select/v1/session/my-selection
CreatePerkPage        -> POST  /lol-perks/v1/pages
UpdatePerkPage(id)    -> PUT   /lol-perks/v1/pages/{positive-id}
SetCurrentPerkPage    -> PUT   /lol-perks/v1/currentpage
```

Bench、Matchmaking、PostGame、Presence、Client UX Repair 等 legacy writer 仍保持窄 capability；Bench 仍为用户显式手动动作。4.0 Gate 8 没有扩大 writer 权限。

### One Gameflow owner

Gate 8 固定：

```text
WindowsLeagueTransportSessionSource     <- exactly one session/auth owner
              ↓
LeagueHttpGateway                       <- shared read/write transport
              ↓ read
LeagueGameflowMonitor                   <- exactly one polling owner
              ↓
LeagueGameflowPhaseMapper               <- pure Core mapping
        ┌─────┴─────┐
        ↓           ↓
ProductStateStore   PerformanceBudgetProvider
        ↓           ↓
        LeagueWorkbenchViewModel
```

`LeagueGameflowMonitor` 只依赖 `ILeagueReadGateway + ILeagueSessionAccessor + IProductStateWriter + PerformanceBudgetProvider`。它不 new HttpClient、不 new session source、不拥有 writer。

phase mapping：

- disconnected -> NotRunning；connecting -> Connecting；transport/read failure -> ClientError；
- Matchmaking -> Matchmaking / Queueing；ReadyCheck -> ReadyCheck / Queueing；
- ChampSelect -> ChampSelect；
- InProgress / WatchInProgress / Reconnect / GameStart -> InGame；
- WaitingForStats / PreEndOfGame / EndOfGame -> PostGame；
- 其它 connected idle/unknown -> Lobby / Client。

cadence 保持 legacy 性能基线：ChampSelect 2s、Matchmaking/ReadyCheck 3s、InGame 10s、connected other 5s、disconnected/connecting/error 10s。

同一 mapping 同源驱动 Product State + Performance；等价 snapshot 不重复发布 monitor Changed，ProductStateStore 本身也抑制相同状态 revision。

## 6. LOL Workbench UX

用户 IA 固定：

```text
LOL 工作台
├─ 比赛
├─ 攻略
└─ 自动化
```

旧 dashboard/player/live/mayhem/recommendation/efficiency/repair/presence 八个 novice-facing view 不直接搬成八个 WinUI tab。

`LeagueWorkbenchCatalog` 是 exactly-three Core IA contract；`LeagueWorkbenchViewModel` 只消费 `IProductStateReader + PerformanceBudgetProvider`。

`MainWindow` 选择 LOL 入口后显示三分区 panel；后台 state 通过 DispatcherQueue 回 UI thread。MainWindow 关闭后只 Dispose ViewModel subscription；全进程唯一 League session/gameflow owner 不随页面重建。

页面/ViewModel 禁止 raw `/lol-*` path、HttpClient、Task.Delay polling、session discovery、`LeagueWriteCommand`。后续业务动作必须通过 Core intent/capability contract 暴露。

`scripts/check-facm4-league-workbench.ps1` 自动守 exactly-one session/gameflow/performance composition、phase baseline、three-section IA、UI Text coverage 与 UI no-raw-LCU boundary。

## 7. Cleanup / Update

Cleanup Core 只拥有 preview/plan/confirm orchestration；Windows implementation 持续守 validated root、path allowlist、reparse/junction/symlink guard、UAC、执行前重验证和逐项 failure。

Update metadata 已是 bounded .NET 10 transport。正式 updater replacement 仍必须保留 size/hash/signature/package validation、validated receipt、等待退出、独立替换、失败保旧版和 rollback；replacement target 来自 distribution EXE。

## 8. Product State / Observability

`ProductStateStore` 是唯一 product-state 聚合 store，覆盖 Application / League / Environment / Services。相同状态不增加 revision；subscriber 在 lock 外调用。它不拥有业务 runtime。

`DiagnosticEvent` 固定 `TimestampUtc / ActionId / Module / DurationMs / Result / Reason / LeagueState / ClientVersion / Data`。敏感 key/free-text assignment 在 factory 和 bounded JSONL sink 两层 redaction。Diagnostics 没有业务写权限。

Gate 8 gameflow 只发布枚举状态，不把 phase auth/LCU credential 写入 Product State 或 diagnostics。

### Gate 9 diagnostics target boundary

Gate 9 必须**复用**现有 observability：

```text
ProductStateStore + bounded diagnostic JSONL
            ↓ read-only
Diagnostics snapshot/summary service
            ↓
bounded redacted exporter
            ↓
summary text / sanitized ZIP
            ↓
Diagnostics Center ViewModel
```

规则：

- Diagnostics Center 不获得 League/Cleanup/Updater 等业务 writer；
- 导出前再次 `DiagnosticRedactor`，不能假设落盘内容一定安全；
- reader/exporter 只读明确 allowlist 文件，不递归打包 distribution 任意目录；
- 需要限制 event 数、单文件大小、总输入大小、ZIP entry 数与总输出大小；
- lockfile、settings secrets、raw auth header、cookies 不进入 bundle；
- 用户路径按稳定 placeholder 策略收敛，bundle 文件名不包含用户名/机器名。

## 9. Design System / Main Shell

`FacmTokens.xaml` 只定义 FACM semantic aliases/metrics，颜色 alias 到 WinUI platform theme resources；FACM.App XAML 不保存硬编码产品 palette。

`FacmControls.xaml` 统一 PageTitle / SectionTitle / CardTitle / Body / Muted / Card / StatusChip / PrimaryButton / NavigationItem。

Main Shell 固定：

```text
MainWindow
├─ AppTitleBar                <- sole main-shell TitleBar owner
└─ NavigationView             <- exactly one
   ├─ 清理与修复
   ├─ LOL 工作台
   ├─ 个性化
   ├─ 更多设置
   └─ Frame                   <- exactly one
```

MainWindow 不携带硬编码中文 copy；禁止 Form-in-Form / WindowsFormsHost / Z-order / timer/reflection patch。

## 10. Desktop Surface / Placement

`FACM.Core.Desktop.AnchorPlacementService` 是 desktop placement 的唯一纯几何 owner，支持 preferred point、负坐标、nearest monitor、edge/corner、margin/clamp、off-screen recovery。

`WindowsDesktopWorkAreaProvider` 提供 physical-pixel work-area + per-monitor effective DPI facts。64 DIP F surface 在 App 根据目标 monitor DPI 转 physical size，再交 Core placement；`AppWindow.MoveAndResize` 使用同一坐标系。

`FloatingWindow` 只依赖 `IDesktopWorkAreaProvider + IUiTextProvider + EnsureMainWindow callback`。关闭 MainWindow 不关闭 F；F 点击 create-or-activate MainWindow；关闭 F 才 shutdown runtime。

Single Instance 语义固定 Ensure Open / Activate；全局快捷键只能 RegisterHotKey，不引入 low-level hook/polling。PetHost 保持独立进程、IPC、parent/job 生命周期。

## 11. 测试与发布边界

持续维护：`FACM Windows Build`、`FACM UI Text Contract`、`FACM 4.0 Foundation`、`FACM.WindowsSmoke`、各 Gate deterministic smoke/source gate。已有 smoke 只能迁移或由更强验证替代。

Hosted Windows runner 是 engineering evidence，不替代 Gate 10/12 真机 multi-monitor/mixed-DPI/accessibility matrix。

Gate 13 前 production `online/version.json` / `release/request.json` 继续指向 3.5.15。正式 4.0 cutover 还需要 settings 真机迁移、Updater rollback、Win10/11、DPI/多屏/accessibility、Defender/SmartScreen 等真实证据。
