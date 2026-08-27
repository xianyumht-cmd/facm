# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（生产冻结线）

- 版本：FACM 3.5.15
- GitHub Release：`v3.5.15`
- 在线更新：已启用
- `minimum_version`：3.0.0
- `force_update`：false
- Release FACM.exe SHA-256：`E3B415375E204212EE2D7A36D4A038708DC75694CD9B6FD28F2761BBF1FD01CE`
- `published_at`：2026-08-27T05:28:50.9137418+00:00
<!-- FACM_RELEASE_STATE_END -->

> 生产事实以 GitHub Release 与 `online/version.json` 为准。FACM 4.0 在 Gate 13 release 前不得修改生产更新指向。

## FACM 4.0 总进度

- Gate 0：COMPLETE，#185 / PR #186，`main@4eda40956a8f7394c1f588d441993e7eb9a4e3e3`。
- Gate 1：COMPLETE，#187 / PR #188，`main@22c6f55d5c84ff3b55720653dacbac6d49aa0934`。
- Gate 2：COMPLETE，#189 / PR #190，`main@34578e688cfc85d8934b7dd14dd423e31e098e38`。
- Gate 3：COMPLETE，#191 / PR #192，`main@138940ccb1f0c4f72bb3325b64a3b94638ee2891`。
- Gate 4：COMPLETE，#193 / PR #195，`main@31f867f10f2019004695d5a696c1177a079cef20`。
- Gate 5：COMPLETE，#196 / PR #197，`main@2e42218685b7cecd49a16676c85bf093bcf1ce1b`。
- Gate 6：IMPLEMENTATION VERIFIED，#198 / PR #199，implementation head `f256bd60804a1f0f1e2818d8c7012303ad1d984c`；canonical 文档提交后需 latest-head CI 再确认再合入。
- Gate 7～13：按既定顺序连续推进，不要求用户逐 Gate 回复“继续”。

## 已冻结的 4.0 基线

- 技术栈：.NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，x64 first。
- `FACM4.sln` 与 legacy `FACM.sln` 并行；Gate 13 前 legacy 持续作为 rollback baseline。
- single-file 分发使用 `Environment.ProcessPath` 作为 distribution EXE identity；`AppContext.BaseDirectory` 可能是 `%TEMP%/.net/...`，不得作为 settings/cache/log/runtime/PetHost/update/Updater replacement 根目录。
- UI 只能通过 ViewModel -> Core intent/state contract；具体 Infrastructure / Platform.Windows adapter 只在 App composition root 组装。
- exactly one League discovery/auth/session owner；所有 writer 继续使用最小 capability allowlist。
- production `online/version.json` / `release/request.json` Gate 13 前保持 FACM 3.5.15。

## Gate 1～Gate 3

Gate 1 建立并行 .NET 10 solution、Core/Infrastructure/Platform.Windows/App/Smoke、Performance Contract、legacy settings codec、UI Text foundation、architecture gate。Gate 1 final：Foundation #12 / Windows Build #1300 / UI Text #421 SUCCESS。

Gate 2 建立 Cleanup/League/Online/Settings Core contracts、ViewModel intent boundary、League exact write target policy；Gate 2 final：Foundation #27 / Windows Build #1307 / UI Text #428 SUCCESS。

Gate 3 迁入 .NET 10 runtime/transport：唯一 `WindowsLeagueTransportSessionSource`、shared `LeagueHttpGateway`、secret-safe session parser、strict update metadata source、distribution-based `RuntimePathLayout`、`FACM.WindowsSmoke`。Gate 3 final：Foundation #34 / Windows Build #1310 / UI Text #431 SUCCESS。

## Gate 4 — Settings 2.0：COMPLETE

- schema version = `2`；typed sections：Environment / Online / Appearance / Pets / League，完整覆盖 3.5.15 15-key INI。
- 新文件：distribution EXE 同目录 `settings.v2.json`；legacy `settings.ini` Gate 13 前只读保留。
- legacy -> v2 deterministic migration；malformed/invalid/future schema fail closed。
- atomic save：same-directory temp -> write -> flush -> flush-to-disk -> replace/move；失败保留旧文件。
- ViewModel 只依赖 `ISettings2Repository`。

Gate 4 final：Foundation #56 / Windows Build #1318 / UI Text #439 SUCCESS；merge `main@31f867f10f2019004695d5a696c1177a079cef20`。

## Gate 5 — Product State + Observability：COMPLETE

- `ProductStateStore` 聚合 Application / League / Environment / Services；snapshot 带 revision + UTC timestamp；相同状态不发无意义 event，subscriber 在 lock 外通知。
- League state vocabulary：`NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError`。
- `DiagnosticEvent` 固定 `TimestampUtc / ActionId / Module / DurationMs / Result / Reason / LeagueState / ClientVersion / Data`。
- `DiagnosticRedactor` 对 token/password/passwd/cookie/authorization/secret/credential/auth 等敏感 key 和自由文本 assignment 做 `[redacted]`。
- `BoundedJsonLinesDiagnosticSink` 默认 4 MiB、`.1` rotation、并发串行写、落盘前二次 redaction。
- Product State / diagnostics 只观察与发布事实，不拥有 League runtime/writer。

Gate 5 final latest head `9883a70c...`：Foundation #73 / Windows Build #1323 / UI Text #444 SUCCESS；随后 squash merge 到 `main@2e42218685b7cecd49a16676c85bf093bcf1ce1b`。

## Gate 6 — WinUI 3 Design System + Shell：IMPLEMENTATION VERIFIED

Tracking：Issue #198，branch `feat/facm-4-gate6-design-shell`，PR #199。

### Design System

- `FacmTokens.xaml` 不再保存 FACM 产品硬编码 hex palette；semantic brushes alias WinUI platform theme resources，随 Light/Dark/High Contrast resource system变化。
- 新增统一 spacing/radius/layout tokens。
- 新增 `FacmControls.xaml`，统一 PageTitle / SectionTitle / CardTitle / Body / Muted / Card / StatusChip / PrimaryButton / NavigationItem styles。
- `App.xaml` 只在 application resource root 合并 tokens + shared controls；页面不复制主题资源。

### 正式 Shell contract

`MainWindow` 当前固定：

```text
one main Window
one AppTitleBar owner
one NavigationView
one Frame
exactly four product entries:
  清理与修复
  LOL 工作台
  个性化
  更多设置
```

- 移除 Gate 1 临时 `控制中心/home` 导航项。
- LOL 面向用户 IA 继续写为 `比赛 / 攻略 / 自动化`，不暴露内部模块边界。
- `ExtendsContentIntoTitleBar=true` + `SetTitleBar(AppTitleBar)`，TitleBar 属于同一 Shell visual tree。
- 不引入 Form-in-Form / WindowsFormsHost / Z-order / timer / reflection patch。

### UI Text / boundary

- MainWindow XAML/code-behind 不含硬编码中文用户文案；四入口、subtitle、status、overview/state 卡片均通过 `IUiTextProvider + UiTextKeys`。
- `FileUiTextProvider` 在 Infrastructure 读取稳定 `ui-text.ini`；IO/权限失败使用 defaults，不让 cosmetic override 阻断启动。
- `ControlCenterViewModel` 输出 status key，不输出硬编码 UI copy。
- Shell/ViewModel 不创建第二 League runtime、不 new HttpClient、不直接操作 settings/diagnostic file。

### Gate 6 deterministic evidence

implementation head `f256bd60804a1f0f1e2818d8c7012303ad1d984c`：

- `FACM 4.0 Foundation` #91：SUCCESS；architecture gate / Shell source gate / restore / WinUI build / FoundationSmoke / WindowsSmoke / single-file publish 全 SUCCESS。
- `FACM Windows Build` #1325：SUCCESS。
- `FACM UI Text Contract` #446：SUCCESS。
- artifact `facm4-x64` id `9639633952`，ZIP `88,217,477` bytes，digest `sha256:f6bf0f03a16160873d576fecab7d93d13b4a079e5b380a3dc3db36b84f9ba9cd`。
- `scripts/check-facm4-shell.ps1` 自动守四入口、单 TitleBar/NavigationView/Frame、semantic tokens/shared styles、UI Text default coverage、无 legacy Form host、FACM.App XAML 无硬编码 hex 色。
- `Gate6Smoke` 验所有 Shell text defaults 与 `ui-text.ini` override/fallback。

## Gate 7 — NEXT：Desktop Shell / F 悬浮入口 / Theme / Anchor Placement

Gate 6 合入后从最新 main 新开 Issue/branch/PR。固定目标：

1. Core 建纯几何 `AnchorPlacementService`，输入 desktop/work-area/anchor size/margin，输出可验证 position；支持负坐标、四边/角、clamp。
2. Platform.Windows 提供 monitor/work-area/DPI adapter；几何算法不依赖 WinUI/WinForms。
3. 新建独立 floating desktop surface，共用 Gate 6 application semantic theme resources；它是辅助 surface，不复制 Main Shell NavigationView/TitleBar owner。
4. Single Instance 保持 Ensure Open / Activate；全局快捷键只使用 RegisterHotKey，不引入 low-level hook/polling。
5. floating surface 不创建 League runtime、network client、settings store；只发 Core intent/激活 Main Shell。
6. deterministic smoke 覆盖负坐标、左右/上下多屏 work area、edge anchoring、margin/clamp、theme/resource sharing。
7. Gate 10/12 再补真实 mixed-DPI/multi-monitor 硬件证据；Gate 7 不伪造真实 DPI 验收。
8. legacy Build/UI Text/4.0 Foundation latest-head 全绿；production release controls 不动。

## Gate 8 → Gate 13 固定顺序

- Gate 8：LOL 工作台状态驱动 UX。
- Gate 9：诊断中心与脱敏诊断包。
- Gate 10：DPI / 多屏 / Accessibility。
- Gate 11：Recovery / Feature Flags / 更新保障。
- Gate 12：全量兼容 / 性能 / 发布矩阵。
- Gate 13：legacy 退休与 FACM 4.0 cutover；真实 release blockers 未关闭时不得发布 4.0.0。

## 持续保护的不变量

- exactly one League discovery/auth/session owner；所有 writer 保持最小 capability；Bench 仍为手动动作。
- Mayhem/OP.GG 保留 fallback、timeout、body cancellation、cache、Performance Budget。
- Game Repair 保留 native Win32、多屏/负坐标、WinEvent debounce/cooldown、窄 restart-ux writer；不恢复 Fix-LCU runtime。
- Cleanup 保留 preview、explicit confirm、UAC、path allowlist、reparse guard、执行前重验证。
- Updater 保留 size limit、SHA-256、signature/package validation、validated receipt、独立 replacement、失败保旧版。
- Single Instance = Ensure Open；快捷键 = RegisterHotKey；PetHost 保持独立进程。
- Performance Contract、UI Text Contract、deterministic smoke 不得静默删除。

## Gate 13 前仍需真实 Windows 证据

普通非管理员 UAC/取消、Defender/SmartScreen、Windows 10 1809/22H2 + Windows 11、100/125/150/175/200% DPI、双屏/负坐标/混合 DPI、keyboard/focus/high contrast/text scaling/basic screen reader、Updater interrupted replacement/rollback、3.5.15 -> 4.0 settings 真机升级。未关闭时不得声称 Gate 12/13 release-ready。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md`，核对最新 main / 当前 Gate Issue+PR+CI 后直接继续；不要要求用户逐 Gate回复“继续”。生产 release 与 destructive Git 操作仍遵守 `AGENTS.md` 的 fresh safety check。
