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
7. scripts/check-facm4-diagnostics.ps1
8. dotnet restore FACM4.sln -p:Platform=x64
9. dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
10. FACM.FoundationSmoke
11. FACM.WindowsSmoke
12. publish FACM.App win-x64 self-contained single-file
13. verify FACM.App.exe + no DLL leaks
14. upload `facm4-x64`
```

`TreatWarningsAsErrors=true` 持续开启。warning/XAML error 修类型或实现，不降低门禁。

### Source gates

- architecture：拒绝 Core UI/platform dependency、错误 ProjectReference、ViewModel 越层、migration PR 改 production release controls。
- shell：守 one AppTitleBar / one NavigationView / one Frame / four product entries / semantic resources / UI Text。
- desktop：守 Core geometry、Windows work-area/DPI facts、F minimal ownership、Ensure Open/Activate、无 low-level hook/polling。
- League Workbench：守 exactly-one session/gameflow/performance owner、shared gateway、three-section IA、UI no raw LCU/polling/writer。
- diagnostics：守只读 input allowlist、exact ZIP allowlist、UI no File/Directory/ZipArchive、no League/Cleanup/Updater writer、no directory enumeration/network runtime。

## 3. 本地 4.0 验证

```powershell
pwsh ./scripts/check-facm4-architecture.ps1
pwsh ./scripts/check-facm4-shell.ps1
pwsh ./scripts/check-facm4-desktop.ps1
pwsh ./scripts/check-facm4-league-workbench.ps1
pwsh ./scripts/check-facm4-diagnostics.ps1
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
- settings / UI text / logs / cache / runtime / diagnostics / PetHost / updates / replacement target 只从 distribution EXE 推导。

若真实 Defender/SmartScreen/体积/更新 UX 证明 self-extract 不可接受，批准 fallback 是 signed installer EXE -> self-contained folder payload，不退回 WinForms。

## 5. Settings / UI Text

```text
<distribution>/settings.ini      legacy rollback/migration source
<distribution>/settings.v2.json  FACM 4.0 typed settings
<distribution>/ui-text.ini       optional UI copy overrides
```

legacy INI Gate 13 前不删除/覆盖。v2 malformed/future schema fail closed。保存使用 same-dir temp + flush-to-disk + replace/move。

Main Shell、desktop entry、Workbench、Diagnostics Center 用户 copy 必须通过 `IUiTextProvider`；override 失败使用 defaults。

## 6. Product State / League runtime

`ProductStateStore` 只聚合 facts，不拥有业务 runtime。相同 state 不产生无意义 revision；页面不新增第二 polling/state cache。

4.0 固定：

```text
one WindowsLeagueTransportSessionSource
one shared LeagueHttpGateway
one LeagueGameflowMonitor
one PerformanceBudgetProvider
```

Gameflow monitor 使用 shared read gateway，不创建第二 HttpClient/session source。MainWindow 关闭/重开只重建 ViewModel subscriptions。

Gameflow cadence：NotRunning/Connecting/error 10s；Lobby 5s；Matchmaking/ReadyCheck 3s；ChampSelect 2s；InGame 10s；PostGame 5s。Product State + Performance activity 必须同源。

Workbench 用户 IA only：`比赛 / 攻略 / 自动化`。Bench 继续手动；后续 actions 只能通过 Core capability/intent。

## 7. Desktop Surface / coordinate rule

- Core placement 单位 = Windows desktop physical pixels。
- `EnumDisplayMonitors/GetMonitorInfo` work-area 与 `AppWindow.MoveAndResize` 使用同一坐标空间。
- F nominal size 64 DIP，按目标 monitor DPI scale 转 physical pixels后交 Core placement。
- 负 X/Y 不 clamp 到 0；左/上方 monitor 保留负坐标；屏外 probe 选 nearest monitor 后 recovery。
- 关闭 MainWindow 只关主 Shell；F/runtime 继续。点击 F = create-or-activate；关闭 F = runtime shutdown。

## 8. Gate 9 Diagnostics Center runbook

### 输入 allowlist

只允许：

- 当前内存 `ProductStateSnapshot`；
- `<distribution>/logs/facm4-events.jsonl`；
- `<distribution>/logs/facm4-events.jsonl.1`（存在时）。

禁止默认读取/打包：settings、League lockfile、环境变量、Registry、browser cookies、用户目录递归、crash dump/raw memory。

### Bounds

默认：

```text
MaxEvents          500
MaxInputFileBytes  4 MiB
MaxTotalInputBytes 8 MiB
MaxZipEntries      3
MaxEntryBytes      4 MiB
MaxBundleBytes     8 MiB
MaxSummaryChars    64 Ki chars
```

输入文件超过 bound：skip + counter；事件超过 bound：truncate + receipt/summary flag。不能无限读或无限压缩。

### Redaction

落盘 JSONL 始终视为 untrusted input。Exporter 再次执行更严格 sanitizer：

- token/password/passwd/cookie/authorization/secret/credential/auth；
- Basic/Bearer credentials；
- Windows absolute paths；
- UNC paths；
- Product State distribution directory。

malformed JSONL 只增加 `MalformedLinesSkipped`，**不能**把原始脏行放进 summary/bundle。

### ZIP contract

ZIP entries exactly：

```text
summary.txt
events.jsonl
manifest.json
```

Exporter 写 `<distribution>/runtime/diagnostics`，先 temp，再 final move。UI 不提供任意输出路径。文件名不包含用户名/机器名。

### UI

`更多设置 -> Diagnostics Center` 提供：刷新摘要、复制摘要、导出 bundle。ViewModel 只调用 `IDiagnosticsSnapshotSource / IDiagnosticsBundleExporter`；Clipboard 是 MainWindow 窄 WinUI 动作；UI 不直接 File/Directory/ZipArchive。

### Gate 9 deterministic evidence

implementation head `26d049bdd99dba20c85039d3a3980aeadd8ae05d`：Foundation #162 / Windows Build #1344 / UI Text #465 SUCCESS。Gate9Smoke 验证 valid/malformed JSONL、secret/path 二次脱敏、summary determinism、bounds、ZIP exact allowlist、bundle 无原始 secret/path。

## 9. Gate 10 DPI / 多屏 / Accessibility runbook

当前 Gate 9 基线 manifest 尚未显式声明 PerMonitorV2。Gate 10 工程顺序：

```text
manifest PerMonitorV2 contract
-> DPI scale pure mapping tests (96/120/144/168/192)
-> mixed-DPI synthetic placement tests
-> WinUI accessibility source contract
-> AutomationProperties + keyboard focus
-> text scaling/wrap/high-contrast source checks
-> WindowsSmoke manifest/API evidence
```

Hosted runner 可以证明 manifest/source/API/synthetic geometry，但不能替代真实双屏/混合 DPI/Accessibility 体验。

真实 Gate 10/12 matrix 必须记录：

- Windows 10 1809 / 22H2 + Windows 11；
- 100/125/150/175/200% DPI；
- 左右/上下双屏、负坐标、不同 DPI 屏间移动；
- keyboard-only/tab/focus；
- High Contrast；
- text scaling；
- basic screen reader。

没有真实证据时记录 blocker，不得把 hosted runner 标成真机通过。

## 10. Cleanup / Updater / Recovery

Cleanup：validated root -> preview -> explicit confirm -> UAC if needed -> allowlist/reparse guard -> execution-time revalidation -> per-target result。

Updater 必须持续保持 size limit、mirror fallback、SHA-256、signature/package validation、validated receipt、wait-exit、独立提升替换、失败保旧版、rollback/recovery。

Gate 11 feature flags/kill switch 只能减少/禁用功能，不能扩大 writer permission。

## 11. Single Instance / Hotkey / PetHost

Single Instance = Ensure Open/Activate。Hotkey = RegisterHotKey，不使用 low-level hook/GetAsyncKeyState/polling。PetHost 保持独立进程。

## 12. Gate 13 前真实矩阵

GitHub runner 不能替代：non-admin UAC + cancel、Win10 1809/22H2、Win11、100/125/150/175/200% DPI、dual/mixed DPI/negative coordinates、keyboard/focus/high contrast/text scaling/screen reader、Defender/SmartScreen、3.5.15 -> 4.0 settings migration、interrupted updater replacement/rollback。

这些可不阻塞早期 engineering Gate，但未关闭不得声称 Gate 12/13 release-ready。

## 13. 每个 Gate 关闭流程

1. latest `main` -> Issue + short-lived branch + PR；
2. 同 branch 完代码、tests、canonical docs；
3. legacy + 4.0 latest-head gates 全绿；
4. merge `main` 并 verify；
5. 直接进入下一 Gate，不要求用户回复“继续”。

branch/tag 删除、production deploy/restart 属于 destructive/production 操作，仍需 `AGENTS.md` fresh safety check，不自动执行。
