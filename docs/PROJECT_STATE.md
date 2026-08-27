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

> FACM 4.0 当前仍是迁移候选。没有 release evidence READY + fresh production/destructive authorization，不得修改 `online/version.json` / `release/request.json`、退休 legacy 或执行生产发布。

## FACM 4.0 总进度

- Gate 0：COMPLETE，#185 / PR #186。
- Gate 1：COMPLETE，#187 / PR #188。
- Gate 2：COMPLETE，#189 / PR #190。
- Gate 3：COMPLETE，#191 / PR #192。
- Gate 4：COMPLETE，#193 / PR #195。
- Gate 5：COMPLETE，#196 / PR #197。
- Gate 6：COMPLETE，#198 / PR #199。
- Gate 7：COMPLETE，#200 / PR #201。
- Gate 8：COMPLETE，#202 / PR #203。
- Gate 9：COMPLETE，#204 / PR #205。
- Gate 10：COMPLETE，#206 / PR #207，`main@c8eebe414f332cb524069395c3d74b51c12bdaa0`。
- Gate 11：COMPLETE，#208 / PR #209，`main@977da451c2cf67fdda7c161b4caf56222d96941f`。
- Gate 12：IMPLEMENTATION VERIFIED，#210 / PR #212；implementation candidate `cb7c928691977e464d2e52af28ac33bb8a7c2597`。canonical/evidence closeout 后需 latest-head CI 再确认再合入。
- Gate 13：NEXT，但当前 release evidence = **BLOCKED**；可以继续做 cutover guard，不能正式切生产。

## 已冻结的 4.0 基线

- .NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，x64 first。
- `FACM4.sln` 与 legacy `FACM.sln` 并行；3.5.15 仍是 production/rollback baseline。
- stable path 只从 `Environment.ProcessPath` 推导，不使用 single-file self-extract `AppContext.BaseDirectory`。
- UI -> ViewModel -> Core intent/state；Infrastructure / Platform.Windows adapter 只在 App composition root 组装。
- process-wide exactly one：League session source、shared gateway、gameflow monitor、performance provider、product-state store。
- Bench 仍为显式手动动作；writer 只通过最小 capability。
- Performance Contract、UI Text Contract、所有 deterministic smoke/source gates 不得静默删除。

## Gates 1～9 摘要

- Gate 1：并行 .NET 10 solution + architecture foundation。
- Gate 2：Cleanup/League/Online/Settings Core contracts + UI intent boundary。
- Gate 3：唯一 League session owner/shared transport/runtime path/WindowsSmoke。
- Gate 4：Settings 2.0 + legacy 15-key deterministic migration + atomic persistence。
- Gate 5：Product State + structured observability + bounded redacted JSONL。
- Gate 6：semantic WinUI Design System + one Shell + UI Text。
- Gate 7：negative-coordinate/multi-monitor desktop placement + F Ensure Open / Activate。
- Gate 8：one Gameflow owner + Product State/Performance same-source + `比赛 / 攻略 / 自动化`。
- Gate 9：只读、二次脱敏、bounded Diagnostics Center。

## Gate 10：DPI / 多屏 / Accessibility — COMPLETE

- manifest：`PerMonitorV2, PerMonitor` + legacy `true/pm`，仍 `asInvoker`。
- Core `DesktopDpi` 是唯一 DPI/DIP physical-pixel 计算 contract。
- deterministic 覆盖 100/125/150/175/200%、mixed DPI、负坐标/off-screen recovery。
- stable AutomationId；Name/HelpText 走 UI Text；keyboard-capable action；正文 Wrap；semantic platform theme resources。
- latest-head Foundation #188 / Windows Build #1352 / UI Text #473 SUCCESS。
- hosted runner 不替代真实 mixed-DPI / High Contrast / screen reader 证据。

## Gate 11：Recovery / Feature Flags — COMPLETE

- Feature baseline 为 Core 手写 approved list；kill switch 只有 disable set，不存在 remote/local enable override。
- unknown field/capability、坏 schema/JSON、读取异常均 fail closed。
- League/Cleanup/Update/Diagnostics gated wrappers 在底层调用前拒绝 disabled capability。
- Recovery：Clean/Starting/Running/Failed/Recovering；bounded atomic metadata；previous-start-incomplete 检测。
- strict Settings2 不放宽；外层 validator-backed LKG；无 LKG 使用 `AutoUpdateEnabled=false` 的安全内存默认；坏 primary 保留。
- Update recovery 继续要求 validated receipt + old-version preservation；replacement failure keeps old。
- latest-head Foundation #210 / Windows Build #1357 / UI Text #478 SUCCESS；merge `main@977da451c2cf67fdda7c161b4caf56222d96941f`。

## Gate 12：Release Evidence / Performance Matrix — IMPLEMENTATION VERIFIED

Tracking：Issue #210，branch `feat/facm-4-gate12-release-evidence`，PR #212。

### Machine-readable evidence

- canonical matrix：`evidence/facm4-release-evidence.json`。
- status 仅允许 `Passed / Blocked / NotRun / Failed`。
- `Passed` 必须有 evidence；required 非 Passed 必须有 blocker notes。
- JSON **不存 `releaseReady` 布尔值**；Core `ReleaseEvidenceEvaluator` 每次从 required item 推导 readiness，无法靠手改 summary 绕过 blocker。
- candidate identity 必须是 full 40-char Git SHA + positive artifact id/size + SHA-256 digest。

当前 implementation candidate：

```text
head: cb7c928691977e464d2e52af28ac33bb8a7c2597
Foundation: #223 SUCCESS
Windows Build: #1360 SUCCESS
UI Text: #481 SUCCESS
FACM.App.exe: 227,786,375 bytes
artifact facm4-x64: 9666206475
artifact ZIP: 88,319,814 bytes
digest: sha256:9a1274592e891c8fc3c5c21dfc522fe315179331933d11d61ab63f0758ded559
```

### Performance / ownership regression

Gate12Smoke + `check-facm4-release-evidence.ps1` 现在锁定：

- Desktop：4/2/2/2，history 20，poll 15s；
- Client：3/2/2/2，history 12，poll 20s；
- Queueing：2/1/1/1，history 4，poll 30s；
- ChampSelect：2/1/1/1，history 0，poll 45s；
- InGame / Background：1/1/1/1，history 0，poll 60s；
- ChampSelect 2s、Matchmaking/ReadyCheck 3s、InGame 10s、connected other 5s、disconnected/error 10s；
- App composition 中五个 process-wide owner 各恰好构造一次。

### 当前 readiness

matrix 当前是 **21 required / 11 Passed / 10 Blocked**，因此 `ReleaseReady=false`。

仍未闭环的 required evidence：

1. non-admin 启动 + UAC cancel；
2. Defender / SmartScreen；
3. Win10 1809；
4. Win10 22H2；
5. Win11 controlled real-user evidence；
6. real 100/125/150/175/200% mixed-DPI multi-monitor；
7. keyboard-only/focus + High Contrast + text scaling + basic screen reader；
8. real 3.5.15 -> 4.0 Settings migration/relaunch/rollback；
9. interrupted updater replacement/rollback；
10. final signing/package verification。

Gate 12 engineering 完成不等于 release-ready；source gate 正确允许 CI SUCCESS 同时输出 `RELEASE BLOCKED`。

## Gate 13：Cutover boundary

下一步可以安全实现 **cutover guard**：只有 `ReleaseEvidenceEvaluator.ReleaseReady == true` 且同时存在 fresh production/destructive authorization，才允许进入 production pointer / legacy retirement / deploy transaction。

当前 matrix 不 READY，所以 Gate 13 必须拒绝正式 cutover。当前用户的普通“继续”只授权继续工程工作，不等于生产发布/破坏性授权。

禁止自动执行：branch/tag 删除、production deploy/restart、production pointer 修改、legacy 删除。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md`，核对 latest main / 当前 Gate Issue+PR+CI 后直接继续，不要求用户逐 Gate回复“继续”。
