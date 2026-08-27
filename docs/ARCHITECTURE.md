# FACM 架构

## 1. 双轨迁移

FACM 3.5.15 WinForms 仍是生产/回滚基线；FACM 4.0 使用 .NET 10 + WinUI 3 并行迁移。Gate 13 前不退休 legacy、不修改生产 release controls。

```text
FACM.App (.NET 10 + WinUI 3)
├─ MainWindow: one AppTitleBar + one NavigationView + one Frame
├─ FloatingWindow: narrow F desktop entry surface
├─ ViewModels: Core intents/state only
└─ composition root
        ↓
FACM.Core (platform/UI neutral)
├─ module lifecycle / performance
├─ Settings 2.0 / UI Text
├─ Product State / observability
├─ Desktop placement geometry
├─ Cleanup
├─ League capability/session contracts
└─ Online/update contracts
        ↓
FACM.Infrastructure                 FACM.Platform.Windows
├─ settings/text/diagnostic IO      ├─ executable/runtime identity
├─ HTTP/League transport            ├─ League discovery/session owner
└─ update metadata                  ├─ monitor/work-area/DPI facts
                                    └─ UAC/hotkey/process integration
```

固定 project direction：App -> Core/Infrastructure/Platform.Windows；Infrastructure -> Core；Platform.Windows -> Core；Core 不引用 UI/platform implementation。

## 2. UI Intent Boundary

```text
Window/Page -> ViewModel -> Core intent/state
                            ↑
            Infrastructure / Platform adapters
            wired only in App composition root
```

禁止 ViewModel/Page 直接 new HttpClient、League runtime、settings/diagnostic file store、Process/Registry/Win32 implementation。`scripts/check-facm4-architecture.ps1` 自动守依赖方向和 production release-control protection。

## 3. Host / Performance

`FacmHost` 继续守拓扑初始化、缺失/重复/循环拒绝、timing、失败反向 rollback、正常反向 Dispose。

Performance owner 在 Core；状态优先级保持 `InGame > ChampSelect > hidden/background > Queueing > Client > Desktop`。隐藏窗口不能成为游戏中增加后台工作的理由。

Gate 8 后 gameflow state 与 Performance activity 必须来自同一个 gameflow owner；禁止 UI 页面根据自己轮询到的 phase 单独改预算。

## 4. Settings 2.0 / UI Text

- legacy：`<distribution>/settings.ini`
- 4.0：`<distribution>/settings.v2.json`
- UI text override：`<distribution>/ui-text.ini`

所有稳定路径只从 distribution executable 推导，不使用 single-file self-extract `AppContext.BaseDirectory`。

Settings 2.0 schema v2 覆盖 Environment / Online / Appearance / Pets / League；legacy 15-key import 后旧 INI 仍保留。坏 JSON、非法值、future schema fail closed；atomic save 使用同目录 temp + flush-to-disk + replace/move。

`IUiTextProvider` 是用户可见文字 contract。Main Shell 与 F desktop entry 均通过 `UiTextKeys`；`FileUiTextProvider` 读取 optional `ui-text.ini`，失败 fallback defaults。

`Pets.BallX/BallY` 在 Gate 7 作为 F surface preferred top-left 输入，坐标语义是 Windows desktop physical pixels；`int.MinValue` 表示无偏好。placement 不会删除/重写 legacy migration source。

## 5. League runtime / capability ownership

4.0 exactly one 真实 discovery/auth/session owner：`WindowsLeagueTransportSessionSource`。`LeagueHttpGateway` read/write 共用它；secret 不进入公共 descriptor/diagnostic；credential 只发 loopback。

当前 write targets 只能由 capability policy 产生：

```text
ApplyMySelection      -> PATCH /lol-champ-select/v1/session/my-selection
CreatePerkPage        -> POST  /lol-perks/v1/pages
UpdatePerkPage(id)    -> PUT   /lol-perks/v1/pages/{positive-id}
SetCurrentPerkPage    -> PUT   /lol-perks/v1/currentpage
```

Bench、Matchmaking、PostGame、Presence、Client UX Repair 等继续保持窄 capability；Bench 仍是用户显式手动动作。

Gate 8 的 gameflow owner 必须复用同一个 `ILeagueReadGateway / LeagueHttpGateway / WindowsLeagueTransportSessionSource`。页面/ViewModel 不得创建第二 session、第二 HttpClient 或第二 phase polling loop。

## 6. Cleanup / Update

Cleanup Core 只拥有 preview/plan/confirm orchestration；Windows implementation 后续持续守 validated root、path allowlist、reparse/junction/symlink guard、UAC、执行前重验证和逐项 failure。

Update metadata 已是 bounded .NET 10 transport。正式 updater replacement 仍必须保留 size/hash/signature/package validation、validated receipt、等待退出、独立替换、失败保旧版和 rollback；replacement target 来自 distribution EXE。

## 7. Product State / Observability

`ProductStateStore` 是唯一 product-state 聚合 store，覆盖 Application / League / Environment / Services。相同状态不增加 revision；subscriber 在 lock 外调用。它本身不拥有 League runtime 或轮询器。

`DiagnosticEvent` 固定 `TimestampUtc / ActionId / Module / DurationMs / Result / Reason / LeagueState / ClientVersion / Data`。敏感 key/free-text assignment 在 factory 和 bounded JSONL sink 两层 redaction。Diagnostics 没有业务写权限。

Gate 8 的 gameflow owner 只把已解析事实发布进 Product State，不把 LCU secret/raw auth headers 放入 state 或 diagnostics。

## 8. Design System / Main Shell

### Semantic resources

`FacmTokens.xaml` 只定义 FACM semantic aliases/metrics；颜色 alias 到 WinUI platform theme resources，不在 FACM.App XAML 中保存产品 hex palette。Light/Dark/High Contrast 由 WinUI theme resource system提供基础适配。

`FacmControls.xaml` 是共享 visual contract，统一 PageTitle / SectionTitle / CardTitle / Body / Muted / Card / StatusChip / PrimaryButton / NavigationItem。

### Main Shell visual tree

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

- `ExtendsContentIntoTitleBar=true`，`SetTitleBar(AppTitleBar)`。
- MainWindow XAML/code-behind 不携带中文 UI literals；用户 copy 由 `IUiTextProvider` 注入。
- Shell 不创建第二 League runtime、HttpClient、settings/diagnostic IO。
- 禁止 Form-in-Form / WindowsFormsHost / Z-order / timer/reflection UI patch。

`scripts/check-facm4-shell.ps1` 自动验证四入口、单 TitleBar/NavigationView/Frame、semantic token/shared style presence、UI Text defaults、无 hardcoded FACM.App XAML hex colors、无 legacy Form host。

## 9. Gate 7 Desktop Surface / Placement

### Core geometry

`FACM.Core.Desktop.AnchorPlacementService` 是 desktop placement 的唯一几何 owner：

```text
DesktopPoint / DesktopSize / DesktopRect / DesktopWorkArea
DesktopAnchor
AnchorPlacementRequest -> AnchorPlacementResult
```

Core 只处理 physical desktop coordinate facts，支持：

- 主屏 fallback；
- preferred top-left；
- 负坐标与位于主屏左/上方的 work-area；
- probe 不在任何屏时 nearest monitor；
- left/right/top/bottom + four corners；
- margin / clamp；
- off-screen recovery。

Core 不引用 Win32/WinUI，避免 monitor API 与 placement policy 相互污染。

### Windows facts

`WindowsDesktopWorkAreaProvider` 在 Platform.Windows 使用 `EnumDisplayMonitors + GetMonitorInfo` 获取 work-area physical pixel bounds，使用 `GetDpiForMonitor` 获取 effective DPI scale；96 DPI 是 API 不可用时的安全 fallback。

64 DIP F surface 在 App 层根据目标 monitor DPI 转成 physical pixel size，再交给 Core placement；`AppWindow.MoveAndResize` 使用相同 physical coordinate space，避免二次缩放。

### FloatingWindow ownership

```text
FloatingWindow
├─ one F button
├─ shared Gate 6 semantic resources
├─ IDesktopWorkAreaProvider
├─ IUiTextProvider
└─ EnsureMainWindow callback
```

它不得拥有 League runtime、HttpClient、Settings2Repository、diagnostic sink、文件 IO、NavigationView/Frame、low-level keyboard hook 或 polling timer。

MainWindow 关闭后 F surface 可继续存在；点击 F = create-or-activate MainWindow。关闭 F 才触发 4.0 runtime shutdown。这个行为固定为 **Ensure Open / Activate**，不是 toggle。

`scripts/check-facm4-desktop.ps1` 自动守 Core/platform boundary、Win32 fact adapter、FloatingWindow 最小 ownership、shared Design System、单 League owner 和无 low-level hooks。

## 10. Gate 8 Workbench target

3.5.15 legacy Hub 曾有 dashboard/player/live/mayhem/recommendation/efficiency/repair/presence 八个 novice-facing view，并已经内部归类为 match/recommendation/tools。4.0 不照搬八标签，而是收口为固定三分区：

```text
LOL 工作台
├─ 比赛
├─ 攻略
└─ 自动化
```

旧 `LeagueGameflowMonitor` 的关键职责是唯一循环 owner，并按状态调整 cadence（ChampSelect 2s、Queueing 3s、InGame 10s、connected default 5s、disconnected 10s）。Gate 8 应迁移**职责和不变量**，不是复制 WinForms class。

4.0 target：

```text
one gameflow owner
    -> shared ILeagueReadGateway
    -> deterministic phase mapper
    -> ProductStateStore.League
    -> Performance activity/budget
    -> Workbench ViewModel subscribes state
```

Page/ViewModel 只消费 Core state/intents；不得直接 GET `/lol-gameflow/...` 或自己计时 polling。

## 11. Single Instance / Hotkey / PetHost

Single Instance 语义固定为 Ensure Open / Activate。Gate 7 F surface 已保持 UI 侧 create-or-activate；若后续补 process-wide activation broker，也必须保持同一语义，不得变 toggle。

全局快捷键只能 RegisterHotKey，不引入 low-level keyboard hook/GetAsyncKeyState/polling。PetHost 保持独立进程、IPC、parent/job 生命周期。

## 12. 测试与发布边界

持续维护：`FACM Windows Build`、`FACM UI Text Contract`、`FACM 4.0 Foundation`、`FACM.WindowsSmoke`、各 Gate deterministic smoke。已有 smoke 只能迁移或由更强验证替代。

Hosted Windows runner 能验证 Win32 work-area/DPI API 与 placement contract，但不能替代真实 multi-monitor/mixed-DPI matrix。Gate 10/12 仍需要 Windows 10/11、负坐标、100～200% DPI、左右/上下双屏等真实硬件证据。

Gate 13 前 production `online/version.json` / `release/request.json` 继续指向 3.5.15。正式 4.0 cutover 还需要 settings 真机迁移、Updater rollback、DPI/多屏/accessibility、Defender/SmartScreen 等真实证据。
