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

> FACM 4.0 的工程迁移已推进到 Gate 13 Cutover Guard，但**生产仍是 3.5.15**。没有 release evidence READY + fresh production/destructive authorization，不得修改 `online/version.json` / `release/request.json`、退休 legacy、deploy/restart 或发布 4.0.0。

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
- Gate 12：COMPLETE，#210 / PR #212，merge `main@4be7d6c38a8a59c6ff437a1352b8c0c4a5d2a798`。
- Gate 13：**GUARD VERIFIED / CUTOVER BLOCKED**，Issue #213 保持 OPEN，PR #214 为 guard engineering PR；正式 4.0.0 cutover 尚未完成。

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
- latest-head Foundation #210 / Windows Build #1357 / UI Text #478 SUCCESS。

## Gate 12：Release Evidence / Performance Matrix — COMPLETE

Canonical matrix：`evidence/facm4-release-evidence.json`。

- status 仅允许 `Passed / Blocked / NotRun / Failed`。
- `Passed` 必须有 evidence；required 非 Passed 必须有 blocker notes。
- JSON 不存可手改的 `releaseReady`；Core `ReleaseEvidenceEvaluator` 从 required statuses 推导 readiness。
- candidate identity 必须是 full 40-char Git SHA + positive artifact id/size + SHA-256 digest。
- Gate12Smoke 逐字段锁定 6 套 Performance Budget、Gameflow cadence 与 readiness 语义。
- release-evidence source gate 锁定 mandatory evidence、runtime owner construction count、Performance/cadence 与累计 smoke。

Gate 12 implementation candidate：

```text
head: cb7c928691977e464d2e52af28ac33bb8a7c2597
Foundation: #223 SUCCESS
Windows Build: #1360 SUCCESS
UI Text: #481 SUCCESS
FACM.App.exe: 227,786,375 bytes
artifact: 9666206475
artifact ZIP: 88,319,814 bytes
digest: sha256:9a1274592e891c8fc3c5c21dfc522fe315179331933d11d61ab63f0758ded559
```

Gate 12 final docs/evidence head 也通过 Foundation #231 / Windows Build #1364 / UI Text #485，并已合入 `main@4be7d6c38a8a59c6ff437a1352b8c0c4a5d2a798`。

## Gate 13：Cutover Guard — GUARD VERIFIED / CUTOVER BLOCKED

Tracking：Issue #213（保持 OPEN），branch `feat/facm-4-gate13-cutover-guard`，PR #214。

### 双门规则

Core `CutoverDecisionService` 固定：

```text
CutoverAllowed = ReleaseEvidenceEvaluator.ReleaseReady
                 AND FreshProductionDestructiveAuthorization
```

授权必须同时满足：

- `Granted=true`；
- scope 精确为 `FACM4ProductionCutover`；
- candidate SHA 与 evidence candidate 精确匹配；
- issued time 不在未来；
- 未过期；
- freshness <= 30 分钟；
- authorization window <= 30 分钟。

**Evidence 先判断。** 当前 evidence BLOCKED 时，即使提供形式上有效的 authorization，也返回 `ReleaseEvidenceBlocked`。

授权对象不落盘、不含 token；source gate 禁止 application source 硬编码 `ProductionCutoverAuthorization`。

### Guard engineering evidence

Gate 13 implementation head `71d82ea060f393f271048102bc4eff77d0707305`：

- Foundation #240：SUCCESS；
- Windows Build #1366：SUCCESS；
- UI Text #487：SUCCESS；
- Gate13Smoke：SUCCESS；
- WindowsSmoke：SUCCESS；
- cutover source guard：SUCCESS，并输出 `CUTOVER BLOCKED`；
- `FACM.App.exe`：227,794,567 bytes；
- engineering artifact：9666591196；ZIP 88,321,030 bytes；
- digest：`sha256:dc6a80aa80f1032af7dbb55721a1d19a02c72d1b4a01b49530c48252ffc4ab69`。

`gate13.cutover-guard` 已在 evidence matrix 中晋升 Passed。

### 当前 readiness

Matrix 当前：**22 required / 12 Passed / 10 Blocked**，所以 `ReleaseReady=false`。

剩余 10 个 required blockers：

1. non-admin 启动 + UAC cancel；
2. Defender / SmartScreen；
3. Win10 1809；
4. Win10 22H2；
5. controlled real-user Win11；
6. real 100/125/150/175/200% mixed-DPI multi-monitor；
7. keyboard-only/focus + High Contrast + text scaling + basic screen reader；
8. real 3.5.15 -> 4.0 Settings migration/relaunch/rollback；
9. interrupted updater replacement/rollback；
10. final signing/package verification。

因此 **正式 Gate 13 没有完成，FACM 4.0.0 没有发布**。工程 guard 可以合入 main，但 Issue #213 必须保持 OPEN/BLOCKED，直到这 10 项真实 evidence 闭环，并获得 fresh、明确的 production/destructive authorization。

## 当前禁止自动执行

- production `online/version.json` / `release/request.json` 修改；
- production deploy/restart；
- 发布/切换 4.0.0；
- legacy retirement/deletion；
- branch/tag 删除。

当前用户的普通“继续”只代表继续工程，不等于上述授权。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md` 后先核对 main、PR #214、Issue #213 与 latest-head CI。若 guard PR 已合入，则后续工作不是继续写迁移代码，而是补齐 matrix 中 10 个真实 release blockers；在它们全部 Passed 之前不得 cutover。
