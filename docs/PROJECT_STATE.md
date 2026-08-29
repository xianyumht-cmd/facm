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

> **生产仍是 FACM 3.5.15。** FACM 4.0 当前只处于 stacked P7 真机验收阶段，不存在 4.0 production cutover 授权。没有完整 release evidence READY + fresh production/destructive authorization，不得修改 `online/version.json` / `release/request.json`、发布 4.0.0、退休 legacy、deploy/restart 或删除历史分支/tag。

## 当前 canonical / active line

- canonical `main`：`269da6c751a8463542ed0d172300675deff9571e`，Merge PR #221。
- #218 Win10 `TabViewButtonBackground` / XamlParse startup issue 已修复并合入。
- #221 launcher-first F / compact launcher 行为迁移已通过对应 Win10 真机验证并合入。
- P2-P7 继续 stacked，全部保持 Draft / 未合并到 `main`。

| 阶段 | PR | Head | 状态 |
| --- | --- | --- | --- |
| P2 Cleanup | #223 | `6bf8956b61683c734b236fd8a38a539168e57918` | code-green / Draft |
| P3 Repair | #226 | `684dc94ee0beb02569a39e6fb5be19c5b1f8b359` | code-green / Draft |
| P4 Personalization | #228 | `2f1efa396cd9add76c96cdf38dee82fac7a16de7` | code-green / Draft |
| P5 League Workbench | #230 | `e3bac2e779e00051b51005e5b715196602c4982f` | code-green / Draft |
| P6 Settings / Maintenance | #232 | `d3801a0fa4276e74514a59a6c673c4cc4efbaff8` | code-green / Draft |
| P7 Unified parity closeout | #234 | code fix `6ba8c917c73e9f7eee1229b29ba9ed243be8ae83` | **Batch M CI-green / targeted Win10 retest next / Draft** |

Tracking Issue：#233。

## FACM 4.0 当前里程碑

代码侧功能等价、自动稳定性审查与重复压力层已完成。最新真实 Win10 evidence 又发现一个跨进程 PetHost cache 性能缺陷，并已在 Batch M 根因修复：

- 旧实现每个新 FACM 进程第一次启用桌宠时，会先完整 SHA-256 约 76.9 MB 内嵌 PetHost ZIP，之后才检查 disk cache；
- 同进程重复 prepare smoke 因 `_cachedPreparation` 无法覆盖这个真实“关闭再打开 FACM”的路径；
- 新实现由 Foundation 构建期生成 `PetHostBundle.sha256`，并与 ZIP 一起嵌入单文件；
- 新进程优先按该稳定 SHA 检查 `runtime/pethost-host/<sha>`；完整 cache 命中时不再打开/rehash 大 ZIP；
- WindowsSmoke 新增 fresh store 模拟新进程，要求 cross-process cache hit 的 `openBundle` 次数严格为 0；
- Busy UI 同时改为显示“正在处理，请稍候…”，避免“准备就绪 + 全控件灰掉”的误导。

详细实时账：`docs/FACM4-PLAN.md`。

## 最新真机证据（Batch M 触发原因）

2026-08-29 Win10 22H2 evidence：

- recovery state：`Running`，版本 4.0.0.0，`consecutiveFailures=0`；
- Settings2 LKG：theme `glass-blue`，pet `moth`，`enabled=false`，F=`1569,576`；
- greenfly -> dragonfly -> moth 的 disabled-selection 流程完成；
- 点击启用 moth 后日志到达 `pet-enable-start -> IsBusy=true -> payload-preparing`，超过 13 秒没有 `host-starting / ready / failed / finish`；
- 同期仍有 F drag-save，说明 FACM 主 UI/message loop 没死，长耗时点位于 PetHost payload prepare。

这是针对一个窄缺陷的证据，不是整个 `compat.windows-10-22h2` Gate13 PASS。

## Batch M 自动验收

Code fix head：`6ba8c917c73e9f7eee1229b29ba9ed243be8ae83`。
PR head used by run：`803e1ba5f9b671b0a787a8c77bb39912d4211b7d`（只比 fix 多实时计划记录）。

FACM 4.0 Foundation **#632 / run `33233590075` = SUCCESS**。

实际 CI 证据：

- PetHost bundle：`76,924,303` bytes；SHA-256 `48e24e9a67f7f75dffc4bef56eeadee9c13d9cc028c38679c8fab0c651141fc4`；
- Release build 与 publish 均明确嵌入 `FACM.Resources.PetHost.zip` 和 `FACM.Resources.PetHost.sha256`；
- Personalization gate：PropertyChanged/Dispatcher Busy feedback、build identity、cross-process no-rehash 全部 OK；
- P1-P7 source/product gates 全部通过；
- Release x64 build 0 warnings / 0 errors；
- FoundationSmoke SUCCESS；
- WindowsSmoke SUCCESS；
- WinUI x64 self-contained single-file publish SUCCESS；
- publish-output verification SUCCESS；
- artifact upload SUCCESS。

## 当前 targeted candidate

```text
artifact: facm4-x64
artifact id: 9709261625
artifact ZIP bytes: 165,704,303
GitHub digest: sha256:32331020c0c1c3fc93ebf70991ddff99a6349deede41e7374ae063da0aa9cb0a
Foundation: #632 / 33233590075
```

从 GitHub 下载后独立重算：

```text
ZIP SHA-256: 32331020c0c1c3fc93ebf70991ddff99a6349deede41e7374ae063da0aa9cb0a
FACM.App.exe bytes: 305,912,996
FACM.App.exe SHA-256: 5d65bd3f3e64a2520cb0c9514627a42e97781396d9e21013f04499fb464a9fea
ZIP DLL entries: 0
```

ZIP SHA 与 GitHub artifact digest 完全一致。

旧 #628 artifact `9708452498` 的完整性证据仍有效，但因 Batch M 真机缺陷已被 supersede，不再作为当前桌宠验收候选。

## 之前已关闭的主要稳定性根因

- Settings2 feature writes 使用 atomic narrow `UpdateAsync`，解决 cross-feature lost update；
- Win10 theme runtime 不再修改平台拥有的 system brush；
- Personalization async Busy 通过 PropertyChanged/Dispatcher refresh 回到可交互状态；
- Maintenance 初始化可重试，download CTS / installer teardown 不从 active await 下提前 Dispose；
- League caller/lifetime cancellation 与 Window/ContentDialog teardown 有 containment；
- Updater fallback/rollback 使用完整 staging/backup + atomic move，不再 stream-copy over live EXE；
- built Updater helper `--self-test` 实际进入 Foundation；
- 重复压力：Settings2 40 轮、single-instance 24 轮、UAC cancel 24 轮、PetHost same-process 24 轮、League Recommended 24 周期、League Efficiency hotkey 30 轮；Batch M 又补 cross-process PetHost cache smoke。

## 当前真实边界：REAL-MACHINE / GATE13

```text
22 required / 12 Passed / 10 Blocked
ReleaseReady=false
CUTOVER BLOCKED
```

仍需真实 evidence：

1. non-admin + real UAC cancel；
2. Defender / SmartScreen；
3. Windows 10 1809；
4. Windows 10 22H2；
5. controlled real-user Windows 11；
6. real mixed-DPI / multi-monitor；
7. keyboard-only / High Contrast / text scaling / basic screen reader；
8. real FACM 3.5.15 -> 4.0 Settings2 migration / relaunch / rollback；
9. interrupted updater replacement / rollback；
10. final signing / package identity verification。

Hosted CI、source gate、deterministic pressure smoke、targeted fix 或普通“继续”都不能自动把这些 evidence 改为 Passed。

## 下一步：targeted Win10 PetHost 复测

使用 artifact `9709261625`：

1. 第一次启用任意桌宠；新 SHA 无 cache 时允许一次 extraction，但必须有终态。
2. 正常退出 FACM，再从同一目录运行同一 EXE。
3. 第二进程再次启用桌宠；已有完整 cache 时不得再次长时间停在 `payload-preparing`。
4. enabled 状态连续切换 5-10 次；每次 Busy 都必须恢复可交互。
5. Busy 时显示“正在处理，请稍候…”。
6. 上传新 `facm4-events.jsonl`、`settings.v2.lkg.json`、`state.json`。

通过 targeted retest 后，再继续完整非破坏功能验收：Cleanup UAC cancel、四大入口、真实 League read paths、Settings、second launch、normal shutdown。

真实 LOL 删除、真实 updater kill/replacement、production pointer 修改、release publication、legacy retirement 都不属于默认授权。

## 之后的阶段

- targeted + 统一真机功能等价验收通过后，再决定 stacked P2-P7 合并策略；CI 绿不会自动 merge。
- UI 2.0 只在功能等价验收之后开始。
- PR #234 继续 Draft / open / unmerged。
- Gate13 release/cutover 是独立证据链。

## 新对话接续

1. 先读 `AGENTS.md`、`docs/FACM4-PLAN.md`、本文件、`docs/FACM4-P7-PARITY-CLOSEOUT.md`；
2. 核对 `main@269da6c751a8463542ed0d172300675deff9571e`；
3. 核对 Batch M fix `6ba8c917c73e9f7eee1229b29ba9ed243be8ae83`；
4. 核对 Foundation #632 / run `33233590075` / artifact `9709261625` / EXE SHA-256 `5d65bd3f...a9fea`；
5. 不重复已完成的 A-M 稳定性修复；
6. 从 targeted Win10 PetHost retest 继续；真实 evidence 回来前不得 cutover，也不得提前开始 UI 2.0。
