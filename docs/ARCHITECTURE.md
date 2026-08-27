# FACM 架构

## 1. 双轨迁移

FACM 3.5.15 WinForms 仍是生产/回滚基线；FACM 4.0 使用 .NET 10 + WinUI 3 并行迁移。Gate 13 前不退休 legacy、不修改生产 release controls。

```text
FACM.App (.NET 10 + WinUI 3)
├─ MainWindow: one AppTitleBar + one NavigationView + one Frame
├─ optional later desktop surfaces (Gate 7+)
├─ ViewModels: Core intents/state only
└─ composition root
        ↓
FACM.Core (platform/UI neutral)
├─ module lifecycle / performance
├─ Settings 2.0 / UI Text
├─ Product State / observability
├─ Cleanup
├─ League capability/session contracts
└─ Online/update contracts
        ↓
FACM.Infrastructure                 FACM.Platform.Windows
├─ settings/text/diagnostic IO      ├─ executable/runtime identity
├─ HTTP/League transport            ├─ League discovery/session owner
└─ update metadata                  └─ Win32 monitor/DPI/UAC/hotkey/process
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

## 4. Settings 2.0 / UI Text

- legacy：`<distribution>/settings.ini`
- 4.0：`<distribution>/settings.v2.json`
- UI text override：`<distribution>/ui-text.ini`

所有稳定路径只从 distribution executable 推导，不使用 single-file self-extract `AppContext.BaseDirectory`。

Settings 2.0 schema v2 覆盖 Environment / Online / Appearance / Pets / League；legacy 15-key import 后旧 INI 仍保留。坏 JSON、非法值、future schema fail closed；atomic save 使用同目录 temp + flush-to-disk + replace/move。

`IUiTextProvider` 是用户可见文字 contract。Gate 6 起 Main Shell 的标题、四入口、subtitle、status/card copy 都通过 `UiTextKeys`；`FileUiTextProvider` 读取 optional `ui-text.ini`，失败 fallback defaults。

## 5. League runtime / capability ownership

4.0 exactly one真实 discovery/auth/session owner：`WindowsLeagueTransportSessionSource`。`LeagueHttpGateway` read/write 共用它；secret 不进入公共 descriptor/diagnostic；credential 只发 loopback。

当前 write targets 只能由 capability policy 产生：

```text
ApplyMySelection      -> PATCH /lol-champ-select/v1/session/my-selection
CreatePerkPage        -> POST  /lol-perks/v1/pages
UpdatePerkPage(id)    -> PUT   /lol-perks/v1/pages/{positive-id}
SetCurrentPerkPage    -> PUT   /lol-perks/v1/currentpage
```

Bench、Matchmaking、PostGame、Presence、Client UX Repair 等仍保持窄 capability；Bench 仍是用户手动动作。

## 6. Cleanup / Update

Cleanup Core 只拥有 preview/plan/confirm orchestration；Windows implementation 后续持续守 validated root、path allowlist、reparse/junction/symlink guard、UAC、执行前重验证和逐项 failure。

Update metadata 已是 bounded .NET 10 transport。正式 updater replacement 仍必须保留 size/hash/signature/package validation、validated receipt、等待退出、独立替换、失败保旧版和 rollback；replacement target 来自 distribution EXE。

## 7. Product State / Observability

`ProductStateStore` 是唯一 product-state 聚合 store，覆盖 Application / League / Environment / Services。相同状态不增加 revision；subscriber 在 lock 外调用。它不拥有 League runtime 或轮询器。

`DiagnosticEvent` 固定 `TimestampUtc / ActionId / Module / DurationMs / Result / Reason / LeagueState / ClientVersion / Data`。敏感 key/free-text assignment 在 factory 和 bounded JSONL sink 两层 redaction。Diagnostics 没有业务写权限。

## 8. Gate 6 Design System / Main Shell

### Semantic resources

`FacmTokens.xaml` 只定义 FACM semantic aliases/metrics；颜色 alias 到 WinUI platform theme resources，不在 FACM.App XAML 中保存产品 hex palette。这样 Light/Dark/High Contrast 由 WinUI theme resource system提供基础适配。

`FacmControls.xaml` 是共享 visual contract，统一：PageTitle / SectionTitle / CardTitle / Body / Muted / Card / StatusChip / PrimaryButton / NavigationItem。

### Main Shell visual tree

`MainWindow` 固定：

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
- Gate 1 临时 `home/控制中心` navigation item 已退休。
- MainWindow XAML/code-behind 不携带中文 UI literals；用户 copy 由 `IUiTextProvider` 注入。
- Shell 不创建第二 League runtime、HttpClient、settings/diagnostic IO。
- 禁止 Form-in-Form / WindowsFormsHost / Z-order / timer/reflection UI patch。

`scripts/check-facm4-shell.ps1` 自动验证四入口、单 TitleBar/NavigationView/Frame、semantic token/shared style presence、UI Text defaults、无 hardcoded FACM.App XAML hex colors、无 legacy Form host。

### Gate 7 surface boundary

Gate 6 的“one Window”是 **one main Shell window owner**，不是永久禁止辅助 Window。Gate 7 的 F 悬浮入口可作为独立 desktop surface，但不得复制 Main Shell navigation/TitleBar owner，也不得创建业务 runtime。它必须共享 application semantic resources。

## 9. Desktop / Single Instance / PetHost

Gate 7 将引入 pure Core Anchor Placement + Windows monitor/DPI adapter。必须支持负坐标、多屏 work area，并把 mixed-DPI 真机证据留到 Gate 10/12。

Single Instance = Ensure Open / Activate；全局快捷键 = RegisterHotKey，不引入 low-level keyboard hook/polling。PetHost 保持独立进程、IPC、parent/job 生命周期。

## 10. 测试与发布边界

持续维护：`FACM Windows Build`、`FACM UI Text Contract`、`FACM 4.0 Foundation`、`FACM.WindowsSmoke`、各 Gate deterministic smoke。已有 smoke 只能迁移或由更强验证替代。

Gate 13 前 production `online/version.json` / `release/request.json` 继续指向 3.5.15。正式 4.0 cutover 还需要 settings 真机迁移、Updater rollback、Win10/11、DPI/多屏/accessibility、Defender/SmartScreen 等真实证据。
