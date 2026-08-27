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
5. dotnet restore FACM4.sln -p:Platform=x64
6. dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
7. FACM.FoundationSmoke
8. FACM.WindowsSmoke
9. publish FACM.App win-x64 self-contained single-file
10. verify FACM.App.exe + no DLL leaks
11. upload `facm4-x64`
```

`TreatWarningsAsErrors=true` 持续开启。遇到 warning/XAML error 修类型或实现，不降低门禁。

### Architecture gate

必须拒绝：Core UI/platform dependency、错误 ProjectReference、ViewModel 越层、migration PR 改 production release controls。

### Shell design gate

`scripts/check-facm4-shell.ps1` 必须拒绝：

- MainWindow 不是 exactly one NavigationView + one Frame；
- 四入口不是 `repair / league / personalization / settings` exactly once；
- 恢复 Gate 1 临时 `home` item；
- 没有 exactly one `AppTitleBar` + `SetTitleBar(AppTitleBar)`；
- MainWindow XAML/code-behind 出现硬编码中文用户文案；
- Shell 直接 new League runtime/HttpClient 或直接 File/Directory IO；
- FACM.App XAML 出现硬编码 hex product color；
- semantic tokens/shared styles/App merged dictionaries 缺失；
- Shell UI Text key 缺 default。

注意：该 gate 只约束 **Main Shell owner**。Gate 7 允许新增独立 floating desktop surface，不应把 source gate 扩成“整个 App 永远只能有一个 Window”。

## 3. 本地 4.0 验证

```powershell
pwsh ./scripts/check-facm4-architecture.ps1
pwsh ./scripts/check-facm4-shell.ps1
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

Gate 6 Main Shell 用户 copy 必须通过 `IUiTextProvider`。`FileUiTextProvider` 读取失败时使用 defaults；cosmetic text override 不能阻止启动。

## 6. Product State / Diagnostics

`ProductStateStore` 只聚合 facts，不拥有业务 runtime。页面不新增第二 polling/state cache。

Diagnostics 默认 `<distribution>/logs/facm4-events.jsonl`，bounded + rotation；factory 和 sink 两层 redaction。不得写 token/password/cookie/authorization/LCU lockfile secret。日志 IO 是 best-effort，不得阻止产品启动/退出。

## 7. League / Cleanup

League：exactly one discovery/auth/session owner；read/write share source；credential loopback-only；writer capability exact allowlist；Bench manual only；InGame 工作不超过 Performance Contract。

Cleanup：validated root -> preview plan -> explicit confirm -> UAC if needed -> allowlist/reparse guard -> execution-time revalidation -> per-target result。UI dialog 不拥有删除规则。

## 8. Main Shell / Gate 7 desktop surface

Gate 6 Main Shell 已固定：one AppTitleBar + one NavigationView + one Frame + 四入口。

Gate 7 floating surface：

- 必须共享 application semantic resources；
- 不复制 Main Shell navigation/titlebar；
- 不创建 League/HTTP/settings runtime；
- placement 算法先在 Core 纯几何 deterministic 测试；
- Windows monitor/work-area/DPI 只在 Platform.Windows adapter；
- 负坐标、多屏几何自动验证，mixed-DPI 真机证据留 Gate 10/12。

Single Instance = Ensure Open/Activate；hotkey = RegisterHotKey；不使用 low-level hook/polling。PetHost 保持独立进程。

## 9. Updater / 发布事务

必须持续保持：size limit、mirror fallback、SHA-256、signature/package validation、validated receipt、wait-exit、独立提升替换、失败保旧版、rollback/recovery。

Gate 13 cutover 前必须 fresh safety check，且 Gates 0～12 + settings migration + real-machine matrix + updater rollback evidence 全成立后才允许改 production pointer。

## 10. Gate 13 前真实矩阵

GitHub runner 不能替代：non-admin UAC + cancel、Win10 1809/22H2、Win11、100/125/150/175/200% DPI、dual/mixed DPI/negative coordinates、keyboard/focus/high contrast/text scaling/screen reader、Defender/SmartScreen、3.5.15 -> 4.0 settings migration、interrupted updater replacement/rollback。

这些可不阻塞早期 engineering Gate，但未关闭不得声称 Gate 12/13 release-ready。

## 11. 每个 Gate 关闭流程

1. latest `main` -> Issue + short-lived branch + PR；
2. 同 branch 完代码、tests、canonical docs；
3. legacy + 4.0 latest-head gates 全绿；
4. merge `main` 并 verify；
5. 直接进入下一 Gate，不要求用户回复“继续”。

branch/tag 删除、production deploy/restart 属于 destructive/production 操作，仍需 `AGENTS.md` fresh safety check，不自动执行。
