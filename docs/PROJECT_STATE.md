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
- Gate 13：**GUARD VERIFIED / CUTOVER BLOCKED**，#213 保持 OPEN；Guard PR #214 已合入 `main@c54cc1f87cb7069daf9e045008320a7d0ac7feac`。
- Gate 13 evidence harness：#215 / PR #216，active；目标是把 10 个真实 blocker 的采集流程压缩为 Windows 一键 evidence bundle，不自动把 blocker 改为 Passed。

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

Tracking：Issue #213 保持 OPEN。Guard PR #214 已 squash 合入 `main@c54cc1f87cb7069daf9e045008320a7d0ac7feac`。

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

### Guard final evidence

PR #214 final head `756add72129c091e31beedf6ca6b33983dbdc759`：

- Foundation #247 SUCCESS；
- Windows Build #1370 SUCCESS；
- UI Text #491 SUCCESS；
- Gate1-13 cumulative FoundationSmoke SUCCESS；
- WindowsSmoke SUCCESS；
- final evidence evaluator：22 required / 12 Passed / 10 Blocked；
- cutover source guard：`CUTOVER BLOCKED`；
- `FACM.App.exe`：227,794,567 bytes；
- artifact 9667132912；ZIP 88,321,043 bytes；
- digest `sha256:2aecd9b5c7b69b80b93b3e37d042c180586f2152cfd6aa6b7bd7a655a7512945`。

Merge 后 main push 也再次通过 Foundation #249 / Windows Build #1371 / UI Text #492。

### 当前 readiness

Matrix：**22 required / 12 Passed / 10 Blocked**，所以 `ReleaseReady=false`。

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

因此 **正式 Gate 13 没有完成，FACM 4.0.0 没有发布**。Issue #213 必须保持 OPEN/BLOCKED，直到这 10 项真实 evidence 闭环，并获得 fresh、明确的 production/destructive authorization。

## Gate 13：一键真机 Evidence Harness — ACTIVE

Tracking：Issue #215，branch `feat/facm-4-gate13-real-machine-evidence`，PR #216。

目标不是替代真机验证，而是把真机材料采集标准化：

- 根目录 `FACM-4.0-真机证据采集.bat`，显式调用 Windows PowerShell 5.1；
- collector 只读，不联网、不提权、不写注册表、不执行 update/restart/delete；
- 自动采集 OS/build/UAC/Defender/SmartScreen 配置、candidate SHA-256/version/AuthentiCode、display bounds/DPI、High Contrast/text scale、settings/recovery 文件元数据与哈希；
- bundle 默认脱敏 username/UserProfile/Windows/UNC path/Basic/Bearer/token/password/secret/cookie/authorization；
- UAC cancel、SmartScreen 实际 UI、mixed-DPI 拖动、keyboard/screen-reader、Settings migration、Updater rollback、final signing review 保持 `manual_required` / review 状态；
- collector 不修改 canonical matrix，automatic observation 不等于 Passed；
- CI 必须用 Windows PowerShell 5.1 跑 `-SelfTest`，验证脱敏、8 个 evidence slots、JSON roundtrip、ZIP 创建和固定 ZIP entries。

第一版 source gate + PS5.1 self-test 已在 PR #216 的早期 head 真实通过；v1.1 将 ZIP 正常路径也纳入 self-test，待 latest-head CI 通过后再合并。

## 当前禁止自动执行

- production `online/version.json` / `release/request.json` 修改；
- production deploy/restart；
- 发布/切换 4.0.0；
- legacy retirement/deletion；
- branch/tag 删除。

当前用户的普通“继续”只代表继续工程，不等于上述授权。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md` 后先核对 `main@c54cc1f87cb7069daf9e045008320a7d0ac7feac`、Issue #213、Issue #215 / PR #216 与 latest-head CI。Evidence harness 合入后，工程侧就进入真实机器边界；后续只能采集/审核 10 个真实 blocker，在它们全部 Passed 前不得 cutover。
