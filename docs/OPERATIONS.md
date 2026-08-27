# FACM 构建、验证与发布运行手册

## 1. 生产冻结线

FACM 3.5.15 是正式生产版本。Gate 13 前：

- 不因 4.0 migration 修改 `online/version.json` / `release/request.json`；
- legacy `FACM.sln` / Updater / ToolBundle / PetHost 必须持续可构建；
- 4.0 缺陷只在对应 task branch/PR 修，不拿生产线试验。

Legacy gates：`FACM Windows Build` + `FACM UI Text Contract`。

## 2. FACM 4.0 Foundation CI

`.github/workflows/facm4-foundation.yml` 当前顺序：

```text
1. checkout full history
2. setup .NET 10
3. scripts/check-facm4-architecture.ps1
4. scripts/check-facm4-shell.ps1
5. scripts/check-facm4-desktop.ps1
6. scripts/check-facm4-league-workbench.ps1
7. dotnet restore FACM4.sln -p:Platform=x64
8. dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
9. FACM.FoundationSmoke
10. FACM.WindowsSmoke
11. publish FACM.App win-x64 self-contained single-file
12. verify FACM.App.exe + no DLL leaks
13. upload `facm4-x64`
```

`TreatWarningsAsErrors=true` 持续开启。遇到 warning/XAML error 修类型或实现，不降低门禁。

### Architecture gate

必须拒绝：Core UI/platform dependency、错误 ProjectReference、ViewModel 越层、migration PR 改 production release controls。

### Shell design gate

`scripts/check-facm4-shell.ps1` 必须拒绝：MainWindow 不是 exactly one NavigationView + one Frame；四入口不是 `repair / league / personalization / settings` exactly once；恢复临时 home；没有 exactly one AppTitleBar；MainWindow 用户 copy 硬编码；Shell new League runtime/HttpClient/File IO；FACM.App XAML 硬编码产品色；semantic tokens/shared styles/UI Text defaults 缺失。

### Desktop surface gate

`scripts/check-facm4-desktop.ps1` 必须拒绝：Core placement 引用 WinUI/WinForms/Win32；Windows work-area/DPI adapter 缺失；FloatingWindow 复制业务 Shell、创建 League/HTTP/settings/diagnostic runtime、文件 IO、low-level hook/polling；F 不再使用 Ensure Open/Activate。

### League Workbench gate

`scripts/check-facm4-league-workbench.ps1` 必须拒绝：

- App composition 不是 exactly one `WindowsLeagueTransportSessionSource`；
- App composition 不是 exactly one `LeagueGameflowMonitor`；
- App composition 不是 exactly one `PerformanceBudgetProvider`；
- Gameflow owner 不再使用 shared `ILeagueReadGateway + ILeagueSessionAccessor`；
- Gameflow owner new HttpClient/session source 或获得 writer；
- Workbench/ViewModel/MainWindow 出现 raw `/lol-*`、HttpClient、Task.Delay polling、session discovery、GameflowMonitor ownership 或 `LeagueWriteCommand`；
- `比赛 / 攻略 / 自动化` 不再 exactly 3；
- phase baseline、League state UI Text defaults 缺失。

## 3. 本地 4.0 验证

```powershell
pwsh ./scripts/check-facm4-architecture.ps1
pwsh ./scripts/check-facm4-shell.ps1
pwsh ./scripts/check-facm4-desktop.ps1
pwsh ./scripts/check-facm4-league-workbench.ps1
dotnet restore FACM4.sln -p:Platform=x64
dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project src/FACM.FoundationSmoke/FACM.FoundationSmoke.csproj -c Release
dotnet run --project src/FACM.WindowsSmoke/FACM.WindowsSmoke.csproj -c Release
dotnet publish src/FACM.App/FACM.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=true -o artifacts/facm4
```

GitHub hosted runner 是 deterministic engineering evidence，不替代 Gate 10/12 真机矩阵。

## 4. Runtime path / single-file

- `Environment.ProcessPath` = distribution EXE。
- `AppContext.BaseDirectory` 可是 `%TEMP%/.net/...` self-extract 目录。
- settings / UI text / logs / cache / runtime / PetHost / updates / replacement target 只从 distribution EXE 推导。

若真实 Defender/SmartScreen/体积/更新 UX 证明 self-extract 不可接受，批准 fallback 是 signed installer EXE -> self-contained folder payload，不退回 WinForms。

## 5. Settings / UI Text

```text
<distribution>/settings.ini      legacy rollback/migration source
<distribution>/settings.v2.json  FACM 4.0 typed settings
<distribution>/ui-text.ini       optional UI copy overrides
```

legacy INI Gate 13 前不删除/覆盖。v2 malformed/future schema fail closed。保存使用 same-dir temp + flush-to-disk + replace/move。

Main Shell、desktop entry、LOL Workbench 用户 copy 必须通过 `IUiTextProvider`。`FileUiTextProvider` 读取失败使用 defaults；cosmetic text override 不能阻止启动。

## 6. Product State / Diagnostics

`ProductStateStore` 只聚合 facts，不拥有业务 runtime。相同 state 不产生无意义 revision；页面不新增第二 polling/state cache。

Diagnostics 默认 `<distribution>/logs/facm4-events.jsonl`，bounded + rotation；factory 和 sink 两层 redaction。不得写 token/password/cookie/authorization/LCU lockfile secret。日志 IO 是 best-effort，不得阻止产品启动/退出。

## 7. League / Workbench 操作规则

### Exactly one owner

4.0 当前固定：

```text
one WindowsLeagueTransportSessionSource
one shared LeagueHttpGateway
one LeagueGameflowMonitor
one PerformanceBudgetProvider
```

Gameflow monitor 使用 shared read gateway，不创建第二 HttpClient/session source。MainWindow 关闭/重开只重建 Workbench ViewModel subscription，不重建上面四个 owner。

### Gameflow mapping / cadence

```text
NotRunning                  -> Product NotRunning, 10s
Connecting                  -> Product Connecting, 10s
transport/read error        -> Product ClientError, 10s
connected idle / Lobby      -> Product Lobby, 5s
Matchmaking                 -> Product Matchmaking / Queueing, 3s
ReadyCheck                  -> Product ReadyCheck / Queueing, 3s
ChampSelect                 -> Product ChampSelect, 2s
InProgress/Watch/Reconnect  -> Product InGame, 10s
GameStart                   -> Product InGame, 10s
WaitingForStats/PreEnd/End  -> Product PostGame, 5s
```

Product State 和 Performance activity 必须由同一个 mapping 更新。页面不得直接轮询 `/lol-gameflow/v1/gameflow-phase`。

### Workbench

用户只看 `比赛 / 攻略 / 自动化` 三分区，不暴露 legacy 八标签/内部模块树。Workbench ViewModel 只消费 Core state/performance。

Gate 8 不增加 writer 权限。Bench 仍为用户显式手动动作；后续 actions 只能通过 Core capability/intent。

## 8. Desktop Surface 操作规则

### 坐标与 DPI

- Core placement 单位 = Windows desktop physical pixels。
- `EnumDisplayMonitors/GetMonitorInfo` work-area 与 `AppWindow.MoveAndResize` 使用同一坐标空间。
- F nominal size = 64 DIP；按目标 monitor DPI scale 转 physical pixels后交 Core placement。
- 不把负 X/Y clamp 到 0；左/上方 monitor 必须保留负坐标。
- probe 在所有 work-area 外时选择 nearest monitor，再 deterministic recovery。

### 生命周期

- 启动时 MainWindow + F surface 可同时存在。
- 关闭 MainWindow：只关主 Shell；F/runtime 继续。
- 点击 F：create-or-activate MainWindow，不是 toggle。
- 关闭 F：真正 runtime shutdown/dispose，Gameflow monitor 先停止，再 Dispose League gateway。

Hosted runner 不是 mixed-DPI 双屏证明。Gate 10/12 仍需真实：100/125/150/175/200%、左右/上下多屏、负坐标、不同 DPI 屏间移动。

## 9. Gate 9 Diagnostics Center runbook

Gate 9 必须复用 Gate 5 observability，不另建第二日志系统。

建议实现顺序：

```text
Core diagnostics snapshot/summary/export contracts
-> Infrastructure bounded event reader
-> sanitize/redact again
-> deterministic summary formatter
-> bounded ZIP exporter
-> Diagnostics Center ViewModel/UI
-> source gate + smoke
```

### 输入 allowlist

默认只允许：

- 当前 `ProductStateSnapshot`（内存 facts）；
- `<distribution>/logs/facm4-events.jsonl`；
- 可选同级 bounded rotation `.1`；
- 明确生成的 diagnostics metadata，不递归扫 distribution。

禁止默认打包：settings.ini/settings.v2.json、League lockfile、任意浏览器 cookies、环境变量、registry dump、用户目录全路径、任意 crash dump/raw memory。

### Bounds

Exporter 必须显式限制：

- 最大读取事件数；
- 单输入文件最大字节；
- 总输入字节；
- ZIP entry 数；
- 单 entry 与总输出大小。

超过限制必须 truncate/skip 并在 summary 写 reason，不允许无限读/无限压缩。

### Redaction

落盘日志已脱敏也必须视为 untrusted input。导出时再次应用 `DiagnosticRedactor` 或更严格 sanitizer；检测 token/password/passwd/cookie/authorization/secret/credential/auth、Basic auth、lockfile secret、用户路径/用户名。malformed JSONL 不能绕过 text scrub。

### UI

Diagnostics Center 放 `更多设置` 产品入口内，至少提供：

- 状态摘要；
- 复制摘要；
- 导出脱敏 bundle。

ViewModel 调 Core service/intent，不直接 File/Directory/ZipArchive。Diagnostics 不获得 League/Cleanup/Updater writer。

## 10. Cleanup / Updater / Single Instance

Cleanup：validated root -> preview -> explicit confirm -> UAC if needed -> allowlist/reparse guard -> execution-time revalidation -> per-target result。UI dialog 不拥有删除规则。

Updater 必须持续保持 size limit、mirror fallback、SHA-256、signature/package validation、validated receipt、wait-exit、独立提升替换、失败保旧版、rollback/recovery。

Single Instance = Ensure Open/Activate；Hotkey = RegisterHotKey，不使用 low-level hook/GetAsyncKeyState/polling。PetHost 保持独立进程。

## 11. Gate 13 前真实矩阵

GitHub runner 不能替代：non-admin UAC + cancel、Win10 1809/22H2、Win11、100/125/150/175/200% DPI、dual/mixed DPI/negative coordinates、keyboard/focus/high contrast/text scaling/screen reader、Defender/SmartScreen、3.5.15 -> 4.0 settings migration、interrupted updater replacement/rollback。

这些可不阻塞早期 engineering Gate，但未关闭不得声称 Gate 12/13 release-ready。

## 12. 每个 Gate 关闭流程

1. latest `main` -> Issue + short-lived branch + PR；
2. 同 branch 完代码、tests、canonical docs；
3. legacy + 4.0 latest-head gates 全绿；
4. merge `main` 并 verify；
5. 直接进入下一 Gate，不要求用户回复“继续”。

branch/tag 删除、production deploy/restart 属于 destructive/production 操作，仍需 `AGENTS.md` fresh safety check，不自动执行。
