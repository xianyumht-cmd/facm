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
6. dotnet restore FACM4.sln -p:Platform=x64
7. dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
8. FACM.FoundationSmoke
9. FACM.WindowsSmoke
10. publish FACM.App win-x64 self-contained single-file
11. verify FACM.App.exe + no DLL leaks
12. upload `facm4-x64`
```

`TreatWarningsAsErrors=true` 持续开启。遇到 warning/XAML error 修类型或实现，不降低门禁。

### Architecture gate

必须拒绝：Core UI/platform dependency、错误 ProjectReference、ViewModel 越层、migration PR 改 production release controls。

### Shell design gate

`scripts/check-facm4-shell.ps1` 必须拒绝：MainWindow 不是 exactly one NavigationView + one Frame；四入口不是 `repair / league / personalization / settings` exactly once；恢复 Gate 1 临时 home item；没有 exactly one AppTitleBar；MainWindow 用户 copy 硬编码；Shell 直接 new League runtime/HttpClient/File IO；FACM.App XAML 硬编码产品色；semantic tokens/shared styles/UI Text defaults 缺失。

### Desktop surface gate

`scripts/check-facm4-desktop.ps1` 必须拒绝：

- Core placement 引用 WinUI/WinForms/Win32；
- Windows work-area adapter 缺 `EnumDisplayMonitors / GetMonitorInfo / GetDpiForMonitor`；
- FloatingWindow 复制 NavigationView/Frame 或硬编码产品色；
- FloatingWindow 创建 League runtime、HttpClient、Settings2Repository、diagnostic sink、File/Directory IO；
- FloatingWindow 引入 low-level keyboard hook/GetAsyncKeyState/polling Timer；
- App 不再保持 exactly one `WindowsLeagueTransportSessionSource`；
- F surface 不再使用 Ensure Main Window / Activate 语义。

注意：Gate 6 的 one Window 是 one **main Shell owner**。F 是允许的窄 desktop surface，但不能变成第二业务 Shell。

## 3. 本地 4.0 验证

```powershell
pwsh ./scripts/check-facm4-architecture.ps1
pwsh ./scripts/check-facm4-shell.ps1
pwsh ./scripts/check-facm4-desktop.ps1
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

Main Shell 与 desktop entry 用户 copy 必须通过 `IUiTextProvider`。`FileUiTextProvider` 读取失败时使用 defaults；cosmetic text override 不能阻止启动。

Gate 7 使用 `Pets.BallX/BallY` 作为 F preferred top-left。`int.MinValue` = no preference；越界时 Core placement recovery，不覆盖旧 INI。

## 6. Product State / Diagnostics

`ProductStateStore` 只聚合 facts，不拥有业务 runtime。页面不新增第二 polling/state cache。

Diagnostics 默认 `<distribution>/logs/facm4-events.jsonl`，bounded + rotation；factory 和 sink 两层 redaction。不得写 token/password/cookie/authorization/LCU lockfile secret。日志 IO 是 best-effort，不得阻止产品启动/退出。

## 7. League / Cleanup

League：exactly one discovery/auth/session owner；read/write share source；credential loopback-only；writer capability exact allowlist；Bench manual only；InGame 工作不超过 Performance Contract。

Gate 8 gameflow owner 必须复用 shared `ILeagueReadGateway`，不允许 Page/ViewModel 直接轮询 LCU phase。Gameflow state 和 Performance activity 必须同源更新。

Cleanup：validated root -> preview plan -> explicit confirm -> UAC if needed -> allowlist/reparse guard -> execution-time revalidation -> per-target result。UI dialog 不拥有删除规则。

## 8. Desktop Surface 操作规则

### 坐标与 DPI

- Core placement 单位 = Windows desktop physical pixels。
- `EnumDisplayMonitors/GetMonitorInfo` 的 work-area 与 `AppWindow.MoveAndResize` 使用相同坐标空间。
- F nominal size = 64 DIP；先按目标 monitor DPI scale 转 physical pixels，再传 Core placement。
- 不把负 X/Y clamp 到 0；左/上方 monitor 必须保留负坐标。
- probe 在所有 work-area 外时选择 nearest monitor，再执行 deterministic recovery。

### 生命周期

- 启动时 MainWindow + F surface 可同时存在。
- 关闭 MainWindow：仅关闭主 Shell；F/runtime 继续。
- 点击 F：create-or-activate MainWindow，**不是 toggle**。
- 关闭 F：进入真正 runtime shutdown/dispose。

### CI 与真机边界

`Gate7Smoke` 覆盖 synthetic left/top/negative/off-screen/edge/corner geometry；`FACM.WindowsSmoke` 在 hosted runner 上验证真实 work-area/primary/DPI API。

Hosted runner 不是 mixed-DPI 双屏证明。Gate 10/12 仍需真实：100/125/150/175/200%、左右/上下多屏、负坐标、不同 DPI 屏间移动。

## 9. Gate 8 Gameflow / Workbench 操作规则

3.5.15 的 `LeagueGameflowMonitor` 是单循环 owner；当前 legacy cadence 是：

```text
disconnected/unknown  10s
ChampSelect             2s
Queueing                3s
InGame                 10s
other connected         5s
```

4.0 可调整实现方式，但必须保持“一个 owner + state-based cadence + Performance Contract”，不允许三块 Workbench 页面各自轮询。

目标链：

```text
shared LeagueHttpGateway
-> one gameflow owner
-> Core deterministic phase mapper
-> ProductStateStore + Performance activity
-> Workbench ViewModel subscribes
-> 比赛 / 攻略 / 自动化 UI
```

Raw `/lol-gameflow/...` path 只允许在 adapter/owner 内部；Page/ViewModel 不知道 LCU path/auth。Bench 继续只允许用户显式动作。

## 10. Single Instance / Hotkey / PetHost

Single Instance = Ensure Open/Activate。Gate 7 已建立 F -> MainWindow create-or-activate 行为；后续 process-wide activation broker 也必须遵守该语义。

Hotkey = RegisterHotKey；不使用 low-level hook/GetAsyncKeyState/polling。PetHost 保持独立进程。

## 11. Updater / 发布事务

必须持续保持：size limit、mirror fallback、SHA-256、signature/package validation、validated receipt、wait-exit、独立提升替换、失败保旧版、rollback/recovery。

Gate 13 cutover 前必须 fresh safety check，且 Gates 0～12 + settings migration + real-machine matrix + updater rollback evidence 全成立后才允许改 production pointer。

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
