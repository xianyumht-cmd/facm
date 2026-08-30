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
| P7 Unified parity closeout | #234 | formal P7 `9744af848e4b888c1876e76e2cbf0c06d5c526bf`; local parity candidate now includes Batch X `0eebe940b26edb3b4900587e54ff2f3b685c224a` | **Batch U/X source checks green / Full Product Parity audit recorded / Draft** |

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

## 2026-08-30 Batch P：Desktop Pet IPC lifecycle fix

Batch P is isolated in `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix` on temporary branch `tmp/p7-ipc-lifecycle-fix-20260830`; the formal P7 branch, PR #234, `online/version.json`, and `release/request.json` remain unchanged.

- Client activate/reset/stop writes now pass cancellation into both `StreamWriter.WriteLineAsync` and `FlushAsync`; timed-out transports are poisoned and cleaned without a second graceful stop write.
- Runtime cleanup now detaches the generation, disposes pipe/reader/writer, waits for the process, kills the full tree when needed, waits again, and disposes the process.
- FlyingHost and PetHost no longer pre-show a WPF window. `activate` is consumed on the Dispatcher, then `Show -> Loaded -> ready`; the server no longer pre-sends `connected`.
- Runtime stage diagnostics now preserve stage plus generation/PID/pipe/command/elapsed fields in App diagnostics.
- New deterministic Windows IPC smoke covers handshake order, cancellation without a pending write task, stop-send failure fail-soft cleanup, and serial Host sessions.
- Local solution Release build, both desktop-pet source gates, personalization source gate, FoundationSmoke, WindowsSmoke, Host self-tests, and the real FlyingHost IPC ordering check passed.
- Targeted candidate: `artifacts/facm4-win10-targeted-batch-p.zip`, 237,928,250 bytes, SHA-256 `a5508c6ab65e3c5c023e957a32e44cf41ece7871f996f4338aaefaa71c9f8c80`; App EXE 378,010,788 bytes, SHA-256 `662e1fb5b2df4c4d09bd5657059ba3f8086fbcb8a017380fbee76757a06046f0`; targeted directory has 4 files and 0 DLLs.

This is a local fix candidate, not a production release. The next acceptance action is the real Win10 minimal sequence `real-bee -> butterfly -> vpet -> real-bee -> Off`; only a clean result permits the longer six-pet / ten-round retest.

## 2026-08-30 local parity candidate: Batch Q-U and T1

The current local candidate continues from Batch P in `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix` on `tmp/p7-ipc-lifecycle-fix-20260830`. It remains local-only; formal P7 `9744af848e4b888c1876e76e2cbf0c06d5c526bf`, PR #234, production pointers, and Gate13 are unchanged.

- Batch Q tray shell parity: `681b6de` (`feat(p7): restore tray shell parity`); one native tray owner, legacy icon fallback, menu routing, double-click activation, and fail-soft disposal.
- Batch R outside-click parity: `60c8ae2` (`fix(p7): restore compact outside-click behavior`); release-armed physical left-button watcher with modal suppression and deterministic state coverage.
- Batch S desktop-entry parity: `03ceba4` (`fix(p7): restore desktop entry click semantics`); F/Flying/VPet left-click compact routing and right-click tray routing.
- WinUI/WinForms build compatibility: `7b084b0`; the App keeps WinUI XAML targets and references Windows Forms without enabling the conflicting WPF desktop target.
- Batch T1 League diagnostics: `5600d94` (`diag(p7): add League trace instrumentation`); shared Gateway request pairs, Gameflow poll pairs, Workbench stage pairs, correlation IDs, duration/status/outcome, in-flight peak, endpoint redaction, and cancellation/timeout classification. No T2 limiter/cache/debounce/dedup/timeout/session-invalidation/UI-thread behavior change was made.
- Batch U League session discovery: `7b92557` (`fix(p7): unblock League session discovery`); lockfile-first discovery with LeagueClientUx process-command-line fallback, bounded asynchronous process discovery, positive/negative cache, single-flight joining, reasoned session invalidation, and redacted discovery diagnostics. Deterministic smoke, full Debug x64 build, FoundationSmoke, WindowsSmoke, and live League HTTP/Gameflow evidence pass.
- Batch X outside-click lifecycle: `0eebe940b26edb3b4900587e54ff2f3b685c224a` (`fix(p7): unify outside-click shell lifecycle`); `DesktopSurfaceOutsideClickWatcher` is shared by MainWindow and CompactLauncherWindow, waits for the opening physical click to release, and protects cleanup, League, maintenance, FolderPicker, and ContentDialog modal flows with explicit suppression scopes. Desktop source gates, full Debug x64 rebuild, FoundationSmoke, WindowsSmoke, and all non-cutover source gates pass. Real visible Win10 interaction remains open.
- Local SDK `10.0.400` is installed under `D:\project2\dotnet10`; TEMP/TMP, NuGet packages, and CLI home used for this verification are under `D:\project2`.
- Post-commit `FACM4.sln` Debug x64 build: 0 warnings / 0 errors; FoundationSmoke with `--skip-gate13`: SUCCESS; WindowsSmoke: SUCCESS.
- The full 3.5.15 source audit is recorded in `docs/FACM35-FULL-PRODUCT-PARITY.md`: 41 behavior rows, 12 `EXACT`, 20 `PARTIAL`, 0 `MISSING`, 3 `4.0-ONLY`, and 6 `NEEDS-REAL-MACHINE`. The tray correction confirms that both 3.5.15 and current 4.0 preserve default single-left NotifyIcon behavior, with FACM actions bound to double-click and the right-click menu; the first compatibility gap remains the missing explicit mapping from the 3.5.15 UI-text key contract to the 4.0 role-scoped keys.

Batch X remains in progress for the remaining shell/window/input parity and visible acceptance. Do not rank further League performance causes or add a second limiter/cache/polling loop until the real Workbench phase-by-phase trace is reviewed.

## 2026-08-30 candidate 2730 本机 Foundation 等价验证

本轮验证使用全新隔离 worktree，云端 candidate `2730eda15dc28a801871b5a3d10b4eecbd03a656`，其父提交为正式 P7 `9744af848e4b888c1876e76e2cbf0c06d5c526bf`；本机收口提交为 `e387295fd61c233f8e9892016a6e9917b448cd5b`。正式 P7 未移动，PR #234 未合并。

- 便携式 .NET SDK：`10.0.400`；系统已有 .NET 9 未改动；
- FlyingHost publish/self-test：PASS，464 files，bundle `72,052,263` bytes，SHA-256 `63f94f2bd3fbd4908d0736c9067f26c90afcd7798bdc2abc1929f7b2771cabb5`；不含 `VPet-Simulator.Core.dll`；
- PetHost publish/self-test：PASS，472 files，bundle `76,915,115` bytes，SHA-256 `e295beec4035fe671b3e757b9b515668b8f7eca39178337a73c7c855424d00df`；含 `VPet-Simulator.Core.dll`；
- workflow 全部 28 个 source gates：PASS；`FACM4.sln` restore/build：PASS，Release x64，强制 bundles/updater，0 warnings / 0 errors；
- `FACM.FoundationSmoke`：PASS；`FACM.WindowsSmoke`：PASS；
- FACM.App publish：PASS，single-file 输出 4 files、DLL entries 0；EXE `377,994,404` bytes，SHA-256 `5aa53107fd8efcf67423c3b625908ec083ed6ff5c3effb6f3d80f613c1fe90d6`；artifact ZIP `237,924,305` bytes，SHA-256 `0132c3e4c3037741f0e1af017a377888a6cc23c57d5177da3d99c6a75`；
- `WFAC010` 已在 .NET 10 下正式复现，原因是 WPF/WinForms host manifest 使用旧 `dpiAware` / `dpiAwareness` 节点；已改为 `ApplicationHighDpiMode=PerMonitorV2`，并修正 FlyingHost assembly identity 为 `FACM.FlyingHost.app`；重跑无该 warning；
- 三个受保护文件相对本轮 candidate 父提交无变化：`online/version.json`、`release/request.json` 未修改；
- hosted runner 在 candidate 收口提交上的 run `33295151374` / job `99213419340` 仍是 `runner_id=0`、`steps=[]`；它不能作为代码失败结论。

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

## 下一步：hosted Foundation 后进入 Win10 targeted 复测

先使用本轮通过本机 Foundation 等价链的 staging candidate，等待 hosted Foundation 实际执行；runner 未分配时不得误判为代码失败。Foundation 真正 SUCCESS 后再生成/确认唯一 Win10 验收包：

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

## 2026-08-30 Live LCU Reliability + practical feature expansion

本轮在 `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`、分支 `tmp/p7-ipc-lifecycle-fix-20260830` 从起始 HEAD `54a134b071e40d0730b09d5dcaece496d6038417` 继续。正式 P7 `9744af848e4b888c1876e76e2cbf0c06d5c526bf`、PR #234、生产指针和 Gate13 均未移动。

- Workbench 卡顿根因已由真实日志定位为：后台刷新线程同步触发 `PropertyChanged`，订阅器在非 UI 线程读取 WinUI 导航对象，抛出 `COMException`；不是 FACM 进程退出。修复为先回到 Dispatcher，再读取导航和更新界面；ViewModel、Gameflow observer、Bench loop 和推荐配置通知均增加故障边界。
- FACM 进程生命周期诊断已覆盖 startup、main-window-created/closed、compact-opened/closed、shutdown-requested/start/complete、unhandled-ui-exception、unobserved-task-exception、fatal-process-exit；异常字段包含类型、HResult、线程和最近阶段，未记录凭据。
- LCU 404 现在按唯一 Gameflow 快照做阶段分类：已知可选会话端点在非所属阶段为 `ExpectedUnavailable`，所属阶段或未知端点为 `UnexpectedFailure`；不增加第二套 Gateway、session 或 polling loop。
- Diagnostics Center 增加只读 League Runtime Snapshot：连接状态、当前 phase/product state、PID、端口、session source、当前/最高并发；数据来自现有 session/Gateway/Gameflow owner。
- 可靠性 smoke 新增 404 分类、Gameflow observer 故障边界、Auto Accept 写入失败可观察边界；已有 FoundationSmoke 全套（跳过 Gate13）继续通过。

当前真实 LCU 证据：LeagueClient PID `8812`、LeagueClientUx PID `20504` / LCU `61101`，最新 FACM PID `16436` 保持 Responding；最新 Gameflow 记录为 `Lobby / Connected`。手工操作后的 App 日志累计 387 个 HTTP completed：340 个成功、83 个 ExpectedUnavailable、0 个 UnexpectedFailure，最大并发 2，HTTP p50/p95/max 为 `0/10/374 ms`。两次 automation `POST /lol-lobby/v2/lobby/matchmaking/search` 分别耗时 `109 ms`、`127 ms`，evaluation 分别为 `118 ms`、`129 ms`；未出现新的 COMException、未观察异常或 FACM 进程退出。尚未自然进入 ReadyCheck，因此 Auto Accept 的真实 ReadyCheck PASS 仍未宣称。

本次自然手工操作经过 `Lobby -> ChampSelect -> Lobby -> ChampSelect -> Lobby`，没有产生 `POST /lol-matchmaking/v1/ready-check/accept`。Lobby Gameflow observation 约每 5 秒，因此秒级匹配感知延迟目前优先怀疑 detection delay，而非 HTTP 写请求；在加入精确时序证据前不得直接改变 polling cadence。当前输出目录 settings.v2.json 读到 `autoMatchmakingEnabled=false`、`autoAcceptEnabled=true`，但日志仍有两次 automation matchmaking POST，运行时配置与持久化文件的一致性仍待核对。

候选功能评估：LCU Runtime Snapshot（高价值、只读、低风险，已实现）；ReadyCheck/Auto Accept outcome history（高价值、需要自然 ReadyCheck，暂不实现）；Lobby/queue session summary（中价值、已有 Dashboard 部分重复，暂不实现）；match-history performance drilldown（中价值、已有 Player 数据重复，暂不实现）；live phase health timeline（中高价值、需要持续采样，暂不引入第二轮询，暂不实现）。

本轮仍不得 merge、push、release、Gate13、移动正式 P7，也不得开始完整 UI Upgrade；必须先完成自然 ReadyCheck/Auto Accept 证据和剩余真实机器验收。
