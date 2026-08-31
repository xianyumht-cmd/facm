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

## 2026-08-31 Morphing Surface MS9 runtime stabilization

本轮只处理 Morphing Surface 的真实窗口呈现阻断，未开始 UI Upgrade，未改变 League、桌宠、托盘、outside-click 行为契约，也未移动正式 P7、PR #234、生产指针或 Gate13。

- MS9.1 诊断提交：`a321424`（`diag(p7): expand surface presentation failure forensics`）。MS8 失败证据目录 `D:\project2\facm-ms8-out-20260831` 保持不变；实际读取该目录日志得到 90 个 `facm.surface.presentation-failed`，全部为 `System.InvalidOperationException` / `0x80131509`，其中 `request:outside-click` 84 个、`request:collapse-to-orb` 5 个、`request:gameflow-lobby-restored` 1 个。
- 根因不是 League 或 UI Dispatcher：MS9.1 首个候选明确显示抛错操作为 `invariant-check`，实际 AppWindow 外框 `136×39`，目标 Orb `36×36`；XAML Orb 可见性正确，线程 2 具有 Dispatcher access，DispatcherQueue 可用。Win32 检查进一步确认窗口客户区仍被系统非客户区/最小跟踪尺寸限制。
- 窄修复提交：`c372388`（`fix(p7): honor compact surface minimum geometry`）验证了 PreferredMinimum 不能解除本机约束；最终修复提交：`e834763b09f69d7aaa0951af3bc8a0601d64edf3`（`fix(p7): allow morphing surface minimum track size`）。Windows 平台层只对唯一 Morphing MainWindow 的 HWND 子类化 `WM_GETMINMAXINFO`，保留原窗口过程，并将最小跟踪尺寸放宽到 1×1，未引入全局锁、重试、计时器或第二 owner。
- 最终候选：`D:\project2\facm-ms9.4-runtime-out-20260831-1305\FACM.App.exe`，SHA-256 `94AD1C97C93C32285A76F27E3CB3FE78FBE42B7D1BDEEC2DC18B789DD4E66412`，420,892,024 bytes，single-file 输出 0 DLL。真实窗口外框为 `36×36`、客户区 `30×30`，进程保持响应且精确匹配进程数为 1。
- 最终候选完成 100 次真实 Orb↔ControlMatrix 循环，100/100 成功；候选日志为 0 `facm.surface.presentation-failed`、0 operation-failed、0 invariant-failed、0 stale、0 unhandled、0 fatal，202 次转场全部是 101 次 Orb→ControlMatrix 与 101 次 ControlMatrix→Orb。Repair/FeatureSurface→Orb、LeagueSurface→Orb 在同一修复前候选中也已真实通过。
- 本轮没有注入桌面空白 outside-click，也没有制造 ChampSelect/Lobby 自然回归；因此 outside-click、ChampSelectStrip/Lobby restore、modal、tray、桌宠切换和多显示器真实验收仍标记为 `USER_MANUAL_VALIDATION_REQUIRED`。MS8 的 84 次 outside-click 失败与其它路径共享同一个尺寸 invariant 根因；失败后未能提交 Orb，watcher 继续收到后续物理边沿，形成失败洪水。
- 最终校验：`FACM4.sln` Debug x64 为 0 警告/0 错误；FoundationSmoke `--skip-gate13` SUCCESS；WindowsSmoke SUCCESS；27/27 非 cutover source gates 全部通过。未执行 Gate13、merge、push、release、正式 P7 移动或 production pointer 修改。

## 2026-08-31 Morphing Bench Swap Strip：BS1–BS6

本轮从代码基线 `f80a70e065c33b9ba650b09a2cebcc0088233bfc` 开始，产品提交为 `4b9fe1b`
（Bench candidate identity model）、`fea17fd`（同一 MainWindow 的 Morphing Bench Swap Strip、
Workbench 同源呈现与上下文生命周期）和 `d551a46`（点击后的权威状态回读）；测试/门禁提交为
`dc70c98`、`028268e`、`50f7026`。实现只发生在
`D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`；`D:\project2\Facm` 未修改。

- 候选唯一来源是 `LeagueWorkbenchViewModel.Live.BenchChampionIds`；它由现有
  `LeagueWorkbenchDataSource` 的 Legacy/TeamBuilder 读取路径提供。Strip 与详细 Workbench 卡片
  共同使用 `LeagueBenchCandidatePresentation`，不再分别计算候选。
- 身份和头像继续使用现有 `LeagueBenchQuickPickService` 的
  `/lol-game-data/assets/v1/champion-summary.json` 与
  `/lol-game-data/assets/v1/champion-icons/{id}.png` 读取/缓存路径；本轮未增加 portrait 网络
  owner 或请求循环。未知 ID 显示 `Unknown champion` 紧凑占位，不以 `#37`/`#236` 为主标签。
- 同一 `MainWindow` 的 `ChampSelectStrip` 在 `ChampSelect + BenchEnabled + 候选数>0` 时才自动
  显示；目标高度 56 DIP、头像格 44 DIP、宽度 280–600 DIP。F 区为拖动区，头像按钮保留
  mouse/keyboard 激活、短提示和可访问名称。
- 点击复用既有 `TrySwapAsync`：一次 POST、35/70/140ms 有界只读回读、无写重试；成功/失败在
  strip 与详细卡片显示短反馈。桌面空白/显式折叠回 Orb，并只屏蔽当前上下文；候选实质变化
  或新 ChampSelect 会话重新允许自动显示。InGame 仍隐藏，Lobby 恢复 Orb。
- 定向 Bench smoke 已通过 37/236 双候选、未知回退、0/1/2/多候选 eligibility/geometry、上下文
  dismissal/reopen、一次写入、成功回读、验证失败不重试和 409 stale target；28/28 当前
  `check-facm4-*.ps1` source gates、FACM.App Debug x64、FACM4.sln Debug x64、FoundationSmoke
  `--skip-gate13`、WindowsSmoke 均通过，均为 0 警告/0 错误（smoke 本身无警告）。
- 新用户评审候选：`D:\project2\facm-bs6-review-out-20260831-1600\FACM.App.exe`，单文件目录
  仅 1 个文件、0 个 DLL，421,024,376 bytes，SHA-256
  `68766D9B9D2511B846F477FA658EF6573BC7197CBE94861D36BFE0481DF8CE9B`。

本轮没有执行 Gate13、merge、push、release、正式 P7 移动或 production pointer 修改。真实 LCU
ARAM/Bench、portrait 实际渲染、outside-click/modal、键盘/辅助功能、多 DPI 和完整 MS9
presentation 仍需在该新候选上由用户验收，不能据此宣称完整 P7 或 release-ready。

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

Batch X 的源代码阶段已记录；剩余的是 shell/window/input 的真实可见验收。Morphing Surface 行为基线随后已落地，但完整视觉迁移仍未完成。Do not rank further League performance causes or add a second limiter/cache/polling loop until the real Workbench phase-by-phase trace is reviewed.

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

## 历史 hosted targeted candidate（已被上方 MS9 本地候选 supersede）

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
- UI Upgrade 已进入“行为冻结基线”阶段：本地 Morphing Surface 候选已实现并通过确定性验证；完整视觉重构、真机视觉复核和正式产品切换仍未完成。
- PR #234 继续 Draft / open / unmerged。
- Gate13 release/cutover 是独立证据链。

## 新对话接续

1. 先读 `AGENTS.md`、`docs/FACM4-PLAN.md`、本文件、`docs/FACM4-P7-PARITY-CLOSEOUT.md`；
2. 核对 `main@269da6c751a8463542ed0d172300675deff9571e`；
3. 核对 Batch M fix `6ba8c917c73e9f7eee1229b29ba9ed243be8ae83`；
4. 核对 Foundation #632 / run `33233590075` / artifact `9709261625` / EXE SHA-256 `5d65bd3f...a9fea`；
5. 不重复已完成的 A-M 稳定性修复；
6. 从 targeted Win10 PetHost retest 继续；真实 evidence 回来前不得 cutover。Morphing Surface 已进入紧凑单表面 UX 收口阶段，但完整视觉迁移和真实视觉验收仍未完成。

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

本轮仍不得 merge、push、release、Gate13、移动正式 P7；自然 ReadyCheck/Auto Accept 证据和剩余真实机器验收仍未完成。Morphing Surface 的紧凑 UX 收口不等同于 release-ready 或完整视觉升级完成。

## 2026-08-30 Morphing Surface / UI Upgrade behavior baseline（历史基线，当前 MS9 见上）

本节记录 MS8.6 时代的 Morphing Surface 行为基线；当前实现和候选已由上方 2026-08-31 MS9 条目更新。该历史基线不移动正式 P7 `9744af848e4b888c1876e76e2cbf0c06d5c526bf`，不修改 PR #234、production pointer 或 Gate13。

- 默认 `FACM.App` 使用一个持久 `MainWindow` 主宿主，由 `FacmSurfaceStateMachine` 管理 `Orb / ControlMatrix / FeatureSurface / LeagueSurface / ChampSelectStrip / HiddenInGame` 展示模式；这表示一个状态驱动宿主，不表示保留传统大 MainWindow 布局。旧 `FloatingWindow` / `CompactLauncherWindow` 路由保留为 `FACM_SHELL_EXPERIENCE=legacy` fallback。
- Orb 空闲时为 36 DIP 自定义 F；瞬时状态条仅在有信息时显示并使用一次性计时器，点击可执行与 Orb 相同的主激活动作。ControlMatrix 目标为 360x176 DIP，绿色按钮直接回 Orb，红色按钮保持既有关闭语义，Feature/League surface 通过同一宿主改变实际窗口几何。
- Morphing 下关闭 NavigationView 左 pane，不保留左侧导航占位；永久解释性文案隐藏到 Inspector，清理操作的安全警告仍保留。Repair/Cleanup/League 当前是紧凑宿主适配层，尚不是所有功能的完整视觉重构。
- ChampSelect 进入时由现有 Workbench owner 做一次事件驱动刷新；Live 快照更新后直接绑定 ChampSelectStrip。候选通过 Legacy/TeamBuilder 既有路由到达，身份来自现有 champion-summary，头像复用 LCU 图标端点，点击仍走既有单次 bench swap + 有界回读。
- Outside-click、modal suppression、single-instance、tray、桌宠生命周期、InGame 隐藏和 Lobby 回 Orb 的行为契约保持冻结；MainWindow 的共享 outside-click watcher 仅在 ControlMatrix、Feature/League、ChampSelect 等可关闭展开态运行，切回 Orb/Hidden 时停止并重置；没有新增 Gateway、Gameflow monitor、session owner、永久 Orb/Hidden UI 轮询或第二套 cache。
- 本阶段相关本地提交包含前序 Morphing commits，以及 `9960c9e`（Orb presentation invariant）、`518067a`（compact morphing chrome）、`2997198`（matrix inspector）、`7ddf8ae`（maintenance/logs consolidation）、`1b19f00`（compact League Workbench）和 `a760daf`（outside-click lifecycle hardening）。MS0 审计备份位于 `D:\project2\facm-backups\morphing-surface-ms0-20260830-210542`，未纳入仓库。
- 已完成本历史基线的代码校验：FACM.App Debug x64、FACM4.sln Debug x64、FoundationSmoke `--skip-gate13`、WindowsSmoke，以及全部 `27/27` 非 cutover source gates 均为 0 警告 / 0 错误或 SUCCESS。MS8 候选 `D:\project2\facm-ms8-out-20260831` 现作为不可变失败证据，当前 MS9 候选和真实运行结果见上方条目。Release evidence 仍为 `22 required / 12 Passed / 10 Blocked`，因此 cutover 仍 blocked。
- 视觉截图和真实多屏/DPI/辅助功能仍需用户在目标机器复核；WinUI capture 当前受系统 `SetIsBorderRequired` 接口错误阻断，不能把未截图视为视觉 PASS。
