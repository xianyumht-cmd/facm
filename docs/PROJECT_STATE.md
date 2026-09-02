# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.17 bridge
- GitHub Release：`v3.5.17`
- 在线更新：已启用
- `minimum_version`：3.0.0
- `force_update`：false
- 发布基础 main：`d8dae188372329c2eee8c4a30e4b385bb18e987c`
- Release FACM.exe SHA-256：`57361F0CD350E2888EB64C130EB3C0397A0F06276D8228BE3A705BA955CFC7D0`
- `published_at`：2026-09-02T08:35:02.0000000+00:00
- release_notes：旧版自动更新过渡版本；安装后自动迁移到 FACM 4.0。
<!-- FACM_RELEASE_STATE_END -->

> **当前旧版自动更新线为 FACM 3.5.17 bridge。** 原 `v4.0.0` 虽已发布但误用了旧 `v4.0.0-free-dist-test.2` CAB，已标记为过时；修正版 `v4.0.1` 已由最终本地代码重新构建并作为迁移目标。由于使用自签名，Windows 可能显示“未知发布者”，Gate 13 真机迁移/回滚证据仍需继续补齐。

## 当前 canonical / active line

- canonical `main`：`d8dae188372329c2eee8c4a30e4b385bb18e987c`，包含 3.5.17 bridge、P7 最终本地代码、4.0.1 修正版在线指针和本地发布工具。
- #218 Win10 `TabViewButtonBackground` / XamlParse startup issue 已修复并合入。
- #221 launcher-first F / compact launcher 行为迁移已通过对应 Win10 真机验证并合入。
- PR #234（P7）已合并到 `main`；FACM 4.0 的其它候选与 production cutover 仍按独立门禁执行。

| 阶段 | PR | Head | 状态 |
| --- | --- | --- | --- |
| P2 Cleanup | #223 | `6bf8956b61683c734b236fd8a38a539168e57918` | code-green / Draft |
| P3 Repair | #226 | `684dc94ee0beb02569a39e6fb5be19c5b1f8b359` | code-green / Draft |
| P4 Personalization | #228 | `2f1efa396cd9add76c96cdf38dee82fac7a16de7` | code-green / Draft |
| P5 League Workbench | #230 | `e3bac2e779e00051b51005e5b715196602c4982f` | code-green / Draft |
| P6 Settings / Maintenance | #232 | `d3801a0fa4276e74514a59a6c673c4cc4efbaff8` | code-green / Draft |
| P7 Unified parity closeout | #234 | merge `25d308b12b44e16d231dec3169a8486228b816d1`; release metadata `6239e1c055590e1f5af84dbe08838691184eae25` | **Merged to main / FACM 3.5.17 bridge; 4.0.0 superseded and 4.0.1 corrective release published; Gate 13 remains separately tracked** |

Tracking Issue：#233。

## 2026-09-02 3.x → 4.0 迁移桥接实现与 4.0.1 修正

当前任务分支：`codex/facm-4-latest-corrective`（基于远端 `main` `5e60a4c846d446af98f26bdced0291d285c1c901`）。

- 已实现 3.5.17 bridge：可选 `online/version.json.migration` 清单字段、4.0 目标 URL/SHA-256 校验、旧
  `settings.ini` 到 `.facm\settings.ini` 的保留式复制、原子写入 `bootstrap.json`。
- 已实现内置更新器 migration 模式：替换根 `FACM.exe` 前保留完整回滚镜像；只有目标 `active.json`、目标
  `FACM.App.exe` 和匹配运行进程同时出现才提交，否则恢复旧版并记录失败状态。
- Native Bootstrapper 已补充 `4.0.0.0` PE 文件版本；CMake 允许发布脚本注入最终 manifest URL。
- 用户已授权自签名发布：本地已生成新的 RSA-2048 detached manifest 签名密钥，私钥只保存在仓库外的
  `local-signing` 目录；对应公钥已写入本任务分支的 native bootstrapper，不能提交私钥。
- 原 `v4.0.0` 发布包复用了旧 `v4.0.0-free-dist-test.2` CAB，虽然清单和签名验证通过，但不是最后本地代码，现已视为过时。
- 已从包含 P7 最终修改的本地代码重新构建 `D:\project2\facm-release-4.0.1-selfsigned\release-assets`；
  新 `FACM.App.exe` 产品版本带源码提交 `5e60a4c`，应用 CAB SHA-256 为
  `77050d02dc6b5964c781b7065ec8972e9b7cc71b11fa1ca888dc821a95469bcb`，且 `Test-FacmReleaseBundle.ps1` 已通过。
- 本地验证：legacy `FACM.sln` Release 构建通过（保留既有 1 条 obsolete warning）；`--facm4-migration-test`
  和 `FACM.Updater.exe --self-test` 通过；`FACM4.sln` 使用 `D:\project2\dotnet10\dotnet.exe` Release 构建通过；
  native bootstrapper `--self-test` 通过。
- 4.0.1 修正版的 `online/version.json` 已合并到 `main`，指向新启动器 SHA-256
  `428CA6B4F2CE35AB0988B2E5E38FBAA9C29A549D477B1F5396552A72917685E6`；旧版客户端仍先安装 3.5.17 bridge，再迁移到 4.0.1。
  自签名包会保留 Windows“未知发布者”提示；Gate 13 真机迁移/回滚证据仍需继续补齐。

## FACM 4.0 当前里程碑

当前执行焦点已转为 BOOT3-C：在 BOOT3-B exact-byte signer boundary 之上建立 production-like HTTPS
origin/mirror、fail-closed 网络策略、更新恢复/磁盘状态安全和真实 Windows 验收 harness。实现位于隔离
worktree，尚未作为正式 P7 移动；正式生产仍保持 FACM 3.5.15。

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

## 2026-08-31 Morphing Bench Swap Strip：BS1–BS7

本轮从代码基线 `f80a70e065c33b9ba650b09a2cebcc0088233bfc` 开始，产品提交为 `4b9fe1b`
（Bench candidate identity model）、`fea17fd`（同一 MainWindow 的 Morphing Bench Swap Strip、
Workbench 同源呈现与上下文生命周期）、`d551a46`（点击后的权威状态回读）、`f0528fa`
（进程级 Bench context/latch 生命周期修复）；测试/门禁提交为 `dc70c98`、`028268e`、
`50f7026`、`f0528fa`、`b2218f6`、`742f75f`。实现只发生在
`D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`；`D:\project2\Facm` 未修改。

- Compact/Strip 自动呈现的候选唯一来源已提升为进程级 `LeagueBenchRuntimeSnapshot`；它由新的
  `LeagueBenchRuntimeObserver` 挂接现有唯一 `LeagueGameflowMonitor.Observed` 心跳，并复用唯一
  `LeagueBenchQuickPickService` 的 Legacy/TeamBuilder 读取路径。详细 Workbench 仍消费自己的
  `Live` 快照，但不再是 Compact/Strip 自动显示的前置条件。
- 根因已确认：BS6 的 Compact/Strip 事实只来自 `LeagueWorkbenchViewModel.RefreshAsync()`，实际
  只有进入 ChampSelect、打开/刷新 Workbench、交换后等 3 个调用入口；Workbench 未打开或候选
  在一次性刷新之后到达时，FACM 不会重新观察，故会停留 Orb。BS7 新增 1 个
  `LeagueBenchRuntimeObserver`，挂在现有唯一 Gameflow heartbeat 上，未新增 Gameflow、session、
  gateway 或 timer owner。
- 身份和头像继续使用现有 `LeagueBenchQuickPickService` 的
  `/lol-game-data/assets/v1/champion-summary.json` 与
  `/lol-game-data/assets/v1/champion-icons/{id}.png` 读取/缓存路径；本轮未增加 portrait 网络
  owner 或请求循环。未知 ID 显示 `Unknown champion` 紧凑占位，不以 `#37`/`#236` 为主标签。
- 同一 `MainWindow` 的 `ChampSelectStrip` 在当前 ChampSelect context 首次观察到
  `BenchEnabled + 候选数>0` 后锁存显示；候选变化原位更新，暂时零候选/读取不可用保持 56 DIP
  waiting strip，不回退 Orb。目标头像格 44 DIP、宽度 280–600 DIP；F 区为拖动区，头像按钮
  保留 mouse/keyboard 激活、短提示和可访问名称。
- 点击复用既有 `TrySwapAsync`：一次 POST、35/70/140ms 有界只读回读、无写重试；成功/失败在
  strip 与详细卡片显示短反馈。桌面空白、League 客户端点击、候选点击和 F 句柄简单点击都
  保持已锁存 Strip；只有 InGame/Lobby 结束 context，InGame 仍直接 HiddenInGame，Lobby 恢复 Orb。
- BS7 定向 smoke 已通过 Workbench 未打开自动锁存、候选原位更新、零候选 waiting、新 context
  generation、InGame/Lobby 清理、一次写入、成功回读、验证失败不重试、409 stale target 和
  Strip 输入保持策略；Bench/Desktop source gates、FACM.App Release build、FoundationSmoke
  和 WindowsSmoke 均通过，均为 0 警告/0 错误。
- 新用户评审候选：`D:\project2\facm-bs6-review-out-20260831-1600\FACM.App.exe`，单文件目录
  仅 1 个文件、0 个 DLL，421,024,376 bytes，SHA-256
  `68766D9B9D2511B846F477FA658EF6573BC7197CBE94861D36BFE0481DF8CE9B`。
- BS7 中间 candidate：`D:\project2\facm-bs7-review-out-20260831-1319\FACM.App.exe`，单文件目录
  仅 1 个文件、0 个 DLL，420,921,000 bytes，SHA-256
  `4FABDC97FFD67E3403F93D6FCD2A78C1E1E4F60B51F88B6287A4298A2AE526D6`；旧 BS6 candidate 保持不变。
- BS7 最终用户评审候选：`D:\project2\facm-bs7-review-out-20260831-1324\FACM.App.exe`，单文件目录
  仅 1 个文件、0 个 DLL，420,921,000 bytes，SHA-256
  `130A13EF8163061B682B2DF3CAB4E8B8A810484B091D57EEB69F9CE7E459CAB0`；ZIP
  `D:\project2\facm-bs7-review-out-20260831-1324.zip`，252,333,304 bytes，SHA-256
  `C45A8B8B6FAE5C989203E2AB7A7BBDC83C3DD4C513F88D8857E5EEE39C02AD09`；由最终代码提交
  `742f75f` 构建。
- owner 计数：BS6 的自动呈现 Bench state owner 为 Workbench `Live` 1 个；BS7 为
  `LeagueBenchRuntimeObserver` 1 个，`LeagueBenchQuickPickService` 共享创建 1 个，且
  `WindowsLeagueTransportSessionSource` / `LeagueHttpGateway` / `LeagueGameflowMonitor` 仍各 1 个。

本轮没有执行 Gate13、merge、push、release、正式 P7 移动或 production pointer 修改。真实 LCU
ARAM/Bench、portrait 实际渲染、Strip 锁存全过程、modal、键盘/辅助功能、多 DPI 和完整 MS9
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

## 2026-08-31 BOOT-1 Native Bootstrapper + app-local Core candidate

本轮执行 BOOT-1：在独立 worktree `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`、分支
`tmp/p7-ipc-lifecycle-fix-20260830` 上，从本轮起始 HEAD `51a2ea45be7b24476d62da21a25dec17261ac2fd`
实现 Native Bootstrapper、app-local multi-file Core、无桌宠 Core profile、稳定 modular data root、
本地 manifest/pack/staging 原型和可选桌宠缺失时的 fail-soft 边界。正式 P7
`9744af848e4b888c1876e76e2cbf0c06d5c526bf`、PR #234、production pointer、merge/push/release 和
Gate13 均未移动或执行；`D:\project2\Facm` 未作为实现源，也未被修改。

- `FACM.exe` 是不依赖 .NET/WinUI/Windows App SDK 的原生 Win32 bootstrapper；它读取受控
  `.facm\state\active.json`，校验 active Core 位于 `.facm\versions`，设置 `FACM_ROOT`、
  `FACM_DATA_ROOT` 和启动 correlation，再以完整路径启动 `FACM.App.exe`。state 使用原子替换，
  stage 失败不覆盖已知 active 版本。
- `FACM.App` 默认仍保留 legacy 单文件 profile 和嵌入桌宠 payload；BOOT-1 `BootCore.pubxml`
  显式使用 `PublishSingleFile=false`、self-contained `win-x64` 和
  `FACMIncludeEmbeddedPetPayload=false`。无桌宠 Core 中 `FACM.App.exe`/`.dll` 均存在，独立宠物
  文件为 0，两个嵌入资源标记均未命中；可选桌宠组件缺失只记录
  `desktop.pet.component-unavailable`，恢复 launcher，不把用户持久化的 `Pets.Enabled` 静默改为关闭。
- 稳定数据根默认为 modular distribution root 下的 `.facm`，也支持 `FACM_ROOT`/`FACM_DATA_ROOT`；
  settings、logs、runtime/cache、state 和 update 数据不再要求写入 Core 版本目录。Core component id
  为 `facm-core-win-x64`，桌宠组件 id 为 `facm-pet-pethost-win-x64` 和
  `facm-pet-flying-win-x64`。
- 最终 review candidate：
  `D:\project2\facm-boot1-review-20260831-final`。根目录只有 `FACM.exe` 与 `.facm`；启动器
  `3,186,663` bytes，SHA-256
  `29472BEDB2C3DB1130EF97D32D2E2F29C89C49CD204BE702FCBA0F2D097E3B07`；active Core 共 600 个文件、
  `278,611,060` bytes；ZIP component pack `108,980,393` bytes，SHA-256
  `D9763FDD3ABF1983A6B991BDC8E02D5871CD7AA2B46D39461605E14C5E25054B`。manifest V1 和 active state
  均已实际生成。
- 本地 pack 校验：正确包返回 0，末字节损坏包返回 11。Bootstrapper version A/B provision、active
  switch、rollback、malformed state、failed staging preserve、Unicode argument、stable data root、
  optional pet availability 和 named single-instance smoke 全部通过。
- 验证结果：D 盘 `.NET SDK 10.0.400` 下 `FACM4.sln` Release / no-pet build 为 0 warnings / 0 errors；
  Release FoundationSmoke `--skip-gate13` SUCCESS（含 BOOT-1 contract、Gate12）；WindowsSmoke SUCCESS；
  27/27 非 cutover `check-facm4-*.ps1` source gates 通过，`check-facm4-cutover.ps1` 未运行。
- 在 `DOTNET_ROOT`/`DOTNET_ROOT_X64` 指向不存在目录时，最终 candidate 连续启动 3 次至
  `desktop-launcher-ready` 用时 `1088 ms / 1056 ms / 1046 ms`；每次均准确命中 active Core 的
  `FACM.App.exe`，通过窗口自身正常关闭，App 与 bootstrapper 均退出，未残留 candidate 进程。日志保留
  `app.bootstrap-launch` correlation、`main-window-created`、`desktop-launcher-ready` 和
  `shutdown-complete`。

## 2026-08-31 BOOT-2 网络组件供给与增量候选

BOOT-2 已在 `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`、临时分支
`tmp/p7-ipc-lifecycle-fix-20260830` 的提交 `693a762` 中实现；当前工作 HEAD 仍未作为正式 P7 移动。正式 P7
`9744af848e4b888c1876e76e2cbf0c06d5c526bf`、PR #234、`D:\project2\Facm`、生产指针、merge/push/release
和 Gate13 均保持不变。

- native bootstrapper 现包含 WinHTTP 清单/下载、显式 HTTPS 或本地 HTTP 开发策略、`.partial` + Range
  续传、主地址/镜像故障切换、完整包 SHA-256 校验、CAB FDI 原生解包、解包大小/文件数/内容摘要校验、
  组件状态、组合 staging、active 原子切换和离线 fast path。`Sha256Text` 已改为内存 CNG，BOOT-2
  不因摘要计算向 C 盘写临时文件。
- 组件实际分为三类：app 49 files / `57,598,388` raw / `23,134,258` CAB；managed runtime 262 /
  `119,712,016` / `47,631,441`；Windows runtime 289 / `101,300,664` / `32,881,795`。镜像与
  `ownership-report.json` 位于 `D:\project2\facm-boot2-mirror-20260831`，clean/pre-provisioned
  review roots 位于 `D:\project2\facm-boot2-review-20260831`。
- 本地 deterministic smoke 全部通过：首次网络供给、primary→mirror failover、4KB Range resume、
  无网络 fast path、无变化更新不下载 CAB、app-only 仅下载 app、runtime-only 下载 app+managed
  runtime 不下载 Windows runtime、pre-provisioned offline resolve、no-pet boundary。Bootstrap 日志
  包含 manifest/component evaluation、download start/resume/failover/complete、hash/extraction、
  composition/active failure milestones；不写入凭据或逐 chunk 日志。

BOOT-2 deterministic local candidate 及其独立回归已通过；production key custody、真实 HTTPS/CDN、真实 Win10/11
安装与更新、真实 League/桌宠回归、完整 release evidence 和 Gate13 仍未完成。正式生产仍为 FACM 3.5.15。

当前状态是 **BOOT-1 local review candidate ready / not release-ready**。ZIP 已生成并可校验，但当前
prototype 的本地 provisioning 仍消费 expanded local source；原生 ZIP extraction、网络 provisioning、
真实 Windows 10/11、桌宠切换、outside-click、modal、tray、League 自然 ReadyCheck、mixed-DPI、辅助
功能、最终签名和完整 Gate13 evidence 仍未完成，不能据此修改生产指针或宣称 cutover。

## 2026-08-31 BOOT3-A production trust candidate

BOOT3-A 已在隔离 worktree `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`、临时分支
`tmp/p7-ipc-lifecycle-fix-20260830` 上完成，起点 `8fd87b6`，当前实现提交为 `cc45295`，信任契约提交为
`56a694f`。正式 P7 `9744af848e4b888c1876e76e2cbf0c06d5c526bf`、PR #234、生产指针、Formal P7、merge/push/release
和 Gate13 均保持不变；当前工作树原有的 `src/FACM.Platform.Windows/FACM.Platform.Windows.csproj`、`out/`、
`setup.inf`、`setup.rpt` 未纳入提交。

- 生产模式为 schema 3 `production`：bootstrapper 内嵌固定 `facm-production-r1` RSA-2048 公钥，仅接受
  detached RSA-SHA256/PKCS#1 签名，签名覆盖应用/组件清单的精确 UTF-8 字节；应用清单认证组件清单地址、
  清单摘要、包 SHA-256、解包 size/fileCount/contentDigest，组件清单再次逐字段匹配。
- `unsigned-local` 保留为 schema 2 的显式 loopback HTTP 开发模式，必须同时显式打开 local unsigned 和
  insecure 开关；生产模式不接受这些开关、配置或任意第三方信任根，生产组件清单和包地址也必须 HTTPS。
- BOOT3-A focused smoke 已通过 valid signed bundle、altered application/component bytes、invalid signature、
  unknown/test-only key、unsigned production/downgrade、altered authenticated metadata、corrupted package hash
  及 failed-update active preservation；原生 CMake Release build 通过，29 个非 cutover source gates、BOOT-2
  regression smoke、Release x64（0 warnings / 0 errors）、FoundationSmoke 和 WindowsSmoke 均通过。
- 生产 EXE Authenticode 现有基础设施已审计但未复用为 JSON/CAB manifest trust；现有 Authenticode 仍仅负责
  PE 发布签名和托管 updater release identity。测试私钥不在仓库，测试材料和输出位于 `D:\project2`。

当前是 **BOOT3-A local cryptographic trust candidate green / not release-ready**。BOOT3-B 的治理、构建请求、
external signer response apply、offline validator 和 rotation/rejection fixture 已在本地完成，但真实 controlled
production key custody、HTTPS/CDN hosting、signed package publication、真实 Windows update/cutover 验收及完整
release evidence 仍未完成；不得由本候选自动进入 Gate13 或 production release。

## 2026-08-31 BOOT3-B release-key governance and signed artifact pipeline

BOOT3-B 已在隔离 worktree `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`、临时分支
`tmp/p7-ipc-lifecycle-fix-20260830` 的 BOOT3-A HEAD `d66caad` 之后完成，新增提交 `551c596`（治理）、
`a206e95`（native key table、downgrade guard、artifact/signing-request pipeline）和 `e8dd8e1`（validator、
rejection fixtures、source gate 与回归文档）。正式 P7、PR #234、
生产指针、Formal P7、merge/push/release 和 Gate13 均保持不变；`src/FACM.Platform.Windows/FACM.Platform.Windows.csproj`、
`out/`、`setup.inf`、`setup.rpt` 仍为原有未提交材料。

- `facm-production-r1` 被明确标记为 candidate-active、非正式 production credential；`facm-production-r2` 为
  planned rotation identity。native `ManifestTrust` 使用固定编译 key table，只有 Active/Overlap 可接受，planned/
  retired/revoked/unknown 关闭接受；配置、环境变量、远端 keyring 不能添加信任根。
- BOOT3-B builder 复用 BOOT-2 的三类 CAB 组件供给和 content digest，输出不含 Desktop Pet 的 core bundle、
  exact-byte schema-3 清单、release index、ownership report 和不含私钥的 external signing request；应用/组件
  manifest 与 package 的路径、字节数、SHA-256、installed size、fileCount 和 contentDigest 全部被串联记录。
- external response apply 只读取 request、重新校验 payload digest/size/index digest 和 Base64 signature，写入
  detached `.sig`；它不打开私钥，也不把私钥路径写入配置。`Test-FacmReleaseBundle.ps1` 额外检查 ownership、
  HTTPS、core composition、secret material，并调用 native trust bundle verifier。
- BOOT3-B focused test 已通过：unsigned request、确定性双构建、external response apply、signed validator、
  signature byte sensitivity、post-sign mutation、component signature replay、unknown/planned/test-only key、
  unsigned release、authenticated metadata、package hash 和 downgrade rejection。
- 最终回归已通过：BOOT3-A focused trust test、BOOT-2 network/incremental smoke、native CMake Release、
  `FACM4.sln` Release x64（0 warnings / 0 errors）、FoundationSmoke `--skip-gate13`、WindowsSmoke，以及
  `30/30` 非 cutover `check-facm4-*.ps1` source gates。Gate13/cutover、真实 CDN/signer、merge/push/release
  和正式 P7 移动均未执行。

当前是 **BOOT3-B local governance/pipeline/validator candidate green / not release-ready**。BOOT3-C 仍需真实
controlled signer、immutable HTTPS/CDN/mirror、生产发布证据、真实 Windows update/rollback 和后续授权；不要由
本候选自动继续到 BOOT3-C、Gate13、Formal P7 或 production cutover。

## 2026-08-31 BOOT3-B release-key governance baseline

BOOT3-B 已开始，当前先落地 release-key governance：`facm-production-r1` 明确标记为 candidate-active、非正式 production credential；`facm-production-r2` 仅作为 planned rotation identity，尚未进入 native acceptance。key policy 位于 `tools/release/facm-keyring-policy.json`，仅供 release tooling/review 使用，不能添加运行时信任根。

已核对 `facm-production-r1` 的外部 local validation public modulus 与 `src/FACM.Bootstrapper/ManifestTrust.cpp` 完全一致：RSA-2048、exponent `010001`、256-byte modulus；正式 production key custody 尚无仓库证据，因此不会伪造 HSM/KMS 或 signer service 已存在的结论。下一步是确定性 BOOT3-B artifact/signing-request pipeline、external signer response boundary 和 offline release bundle validator。

## 2026-08-31 BOOT3-C production-like HTTPS distribution candidate

BOOT3-C 当前实现仍在同一隔离 worktree `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`、临时分支
`tmp/p7-ipc-lifecycle-fix-20260830`，基线为 BOOT3-B 文档提交 `72972f6`。本轮只做本地候选和
production-like 测试基础设施，不修改 `online/version.json`、`release/request.json`、正式 P7、PR #234、
production CDN/DNS、release、merge、push 或 Gate13；正式生产仍为 FACM 3.5.15。

- schema-3 application manifest now carries signed `manifestMirrors`; schema-3 component metadata carries
  signed `componentManifestMirrors`, while existing package `mirrors` remain authenticated. `bootstrap.json`
  provides only the initial primary/mirror discovery list.
- native WinHTTP requests explicitly disable redirects. Manifest discovery and component-manifest retrieval use
  fixed primary-then-mirror order; production trust is still the embedded BOOT3-B key table plus exact detached
  signatures, never the origin certificate or an arbitrary mirror.
- package downloads keep `.partial` on transport interruption, resume with Range, verify exact package size and
  SHA-256 before promotion, and fail over on an unavailable or corrupted primary package. Existing active state is
  not deleted during network, hash, extraction, composition or active-state failure.
- update preflight checks target-volume free space against package/partial + extracted staging + composed version
  peak plus a 64 MiB margin. `--check-disk-space` is a bounded diagnostic only; it cannot bypass provisioning.
- added local TLS origin/mirror server and integration harness:
  `tools/release/Start-FacmBoot3CHttpsOrigin.js` and
  `tools/release/Test-FacmBoot3CHttpsDistribution.ps1`. Test certificate and validation private key are external
  temporary material and must not enter the repository.
- added real-machine evidence wrapper and explicit matrix:
  `tools/release/Test-FacmBoot3CRealMachineHarness.ps1`. It remains read-only and keeps all 16 Windows 10/11,
  UAC, Defender/SmartScreen, offline, interruption, rollback and data-root acceptance cases `manual_required`.
- 2026-08-31 verification is green for the local candidate: pinned native Release build (0 warnings/errors),
  BOOT3-A focused trust regression, BOOT3-B full release/signing-request regression, all 31 non-cutover
  `check-facm4-*.ps1` source gates, FoundationSmoke `--skip-gate13`, and WindowsSmoke.
- Production-like HTTPS evidence is recorded at
  `D:\project2\facm-boot3c-https-tests-20260831\results.json`: all 8 scenarios passed, including primary/mirror
  failover, corrupt-primary recovery, corrupt-mirror fail-closed preservation, `.partial` resume, redirect
  rejection, local rollback and disk-space guard. This is controlled local evidence, not production CDN evidence.
- Read-only real-machine evidence capture succeeded on OS build 19045 with candidate present at
  `D:\project2\facm-boot3c-native-build-20260831\FACM.exe`; the wrapper output is
  `D:\project2\facm-boot3c-real-machine-evidence-20260831\boot3c-acceptance.json`. All 16 acceptance rows remain
  `manual_required` and no `PASS_REAL_MACHINE` claim is made.

Current BOOT3-C readiness is **local implementation / production-like HTTPS candidate; not release-ready** until
the external signer response, release-owner publication authorization, production CDN/mirror controls, and reviewed
real-machine Win10 22H2 / controlled Win11 evidence exist. Gate13 is intentionally `NOT_RUN_GATE13`.

## 2026-08-31 FREE-DIST-1 GitHub Release and free HTTPS transport candidate

FREE-DIST-1 extends the BOOT3-C transport layer without changing the BOOT3-A/BOOT3-B trust boundary. The work is on
the same isolated worktree and task branch. Focused commits are `50101e6` (`feat(dist): add GitHub canonical proxy
transport candidates`), `5d91a7b` (`fix(dist): preserve resume and verification across proxy failover`), and `7929988`
(`test(dist): cover free proxy failure and GitHub fallback`); the architecture record is `73afa00`. No GitHub Release
was published, no push/merge or production pointer change was performed, and production remains FACM 3.5.15.

- Canonical signed metadata URLs use only
  `https://github.com/xianyumht-cmd/facm/releases/download/<release-tag>/<relative-artifact-path>`; proxy URLs
  are runtime transport candidates and never enter signed metadata or trust decisions.
- Canonical GitHub transport order is `ghfast.top`, `gh-proxy.com`, `gh.llkk.cc`, then direct GitHub. WinHTTP
  automatic redirects remain disabled; only bounded HTTPS redirects to canonical GitHub or GitHub-owned release
  asset hosts are accepted. HTTP, user-info, arbitrary-host and unsafe redirect chains fail closed.
- Resume behavior preserves `.partial` across candidate failover. `206 Content-Range` must match the requested
  offset and authenticated package total; a `200` response during resume restarts safely; package SHA-256 and
  extracted metadata verification remain mandatory for every candidate.
- The local signed release-compatible bundle is at
  `D:\project2\facm-free-dist-release-20260831\bundle`; the launcher-only review directory is
  `D:\project2\facm4-free-dist-review-20260831` and contains only `FACM.exe` plus `bootstrap.json`.
- Candidate figures are 103,775,138 total bundle bytes, 103,647,538 CAB bytes, and 3,919,603 launcher bytes;
  four detached signatures are present. The local evidence is
  `D:\project2\facm-free-dist-release-20260831\free-dist-evidence.json` and
  `D:\project2\facm-free-dist-probe-20260831\free-dist-test-results.json`.
- The focused FREE-DIST gate and test passed: canonical URL/proxy separation, signed-trust preservation,
  launcher-only shape, live candidate probing, unsafe URL rejection, and existing BOOT3-C HTTPS 8/8 evidence.
  Existing BOOT3-A/BOOT3-B/native/build/smoke evidence remains unchanged.

The public repository has FACM 3.5.15 but does not yet publish the local `v4.0.0-free-dist-1` release. Therefore
clean-machine first-run, public-release proxy failover and second-launch zero-download are not claimed. The remaining
action is explicit release-owner authorization to publish the reviewed bundle, followed by real-machine acceptance;
Gate13 and production cutover remain out of scope.

## 2026-09-01 FREE-DIST-2 toolchain revalidation and non-production test candidate

The final task revision was revalidated in the same isolated worktree and task branch; its exact commit is recorded in
the final candidate evidence. `D:\project2\dotnet10\dotnet.exe` is present and reports SDK `10.0.400`; the full `FACM4.sln` Release build
completed with 0 warnings and 0 errors. The current shell still resolves `dotnet` to the machine .NET 9 installation,
so this task explicitly uses the .NET 10 executable and keeps temporary/cache paths under `D:\project2`.

The native bootstrapper Release build, 32 non-cutover source gates, BOOT3-A, BOOT3-B, BOOT-2 (13/13), BOOT3-C (8/8),
FREE-DIST, FoundationSmoke `--skip-gate13`, and WindowsSmoke all passed. Gate13 and the cutover gate were not run.

The initial candidate layout exposed a GitHub Release compatibility defect: nested component manifests reused the same
asset basename. The preparation tool now emits a flat bundle with unique ASCII-safe asset names, rewrites signed
canonical URLs and release-index paths, and the focused FREE-DIST test enforces flat unique asset names. The exact final
local test candidate is:

- Bundle: `D:\project2\facm-free-dist-final-candidate-flat4-20260901\bundle`
- Launcher-only review: `D:\project2\facm4-free-dist-final-review-flat4-20260901`
- Evidence: `D:\project2\facm-free-dist-final-candidate-flat4-20260901\free-dist-evidence.json`
- BOOT3-C evidence: `D:\project2\facm-free-dist-final-candidate-flat4-boot3c-20260901\results.json`
- FREE-DIST evidence: `D:\project2\facm-free-dist-final-candidate-flat4-probe-20260901\free-dist-test-results.json`

The proposed remote identity is non-production tag `v4.0.0-free-dist-test.1`, title `FACM 4.0.0 FREE-DIST test.1`,
with `prerelease=true`. No remote tag, Release, push, merge or production pointer change occurred. The public first-run,
second-launch zero-download and real Windows-machine acceptance remain waiting for a separately authorized test Release.

## 2026-09-01 FREE-DIST-3 single-launcher candidate

FREE-DIST-3 continues from the verified `aa08a89bc5c5b2f6347b313024bc93c59d20132e` baseline in the isolated
worktree `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix` on branch `tmp/p7-ipc-lifecycle-fix-20260830`.
The native bootstrapper now embeds the schema-1 default manifest URL for the non-production test release, while a
valid explicit `bootstrap.json` remains an optional discovery override. Invalid configuration falls back to the
compiled default and cannot add trust roots, enable unsigned production, or downgrade HTTPS. The fixed transport order
remains `ghfast.top`, `gh-proxy.com`, `gh.llkk.cc`, then `github-direct`.

The focused single-launcher evidence passed locally with `D:\project2\dotnet10\dotnet.exe` (SDK `10.0.400`):

- review root before first launch: `D:\project2\facm4-single-launcher-review-20260901`, exactly one file, `FACM.exe`;
- candidate launcher: 3,930,039 bytes, SHA-256 `24b30bed79352fe60b110eae1a242a3c217b6924d18078b14dc33971b9cf2a99`;
- test evidence: `D:\project2\facm4-single-launcher-tests-20260901\results.json`;
- local signed HTTPS provisioning composed all 3 core components without creating `bootstrap.json`;
- the real `FACM.App.exe` Orb process was observed and closed normally;
- all 4 transport candidates were probed against the public GitHub CLI checksum asset;
- valid override, malformed-config fallback, HTTP downgrade rejection, arbitrary trust-key injection ignore, and
  unsigned-manifest rejection all passed.

The local test certificate was removed from the current-user Root store and temporary WinHTTP proxy settings were
restored to Direct access. The exact existing FREE-DIST-2 signed flat bundle was reused unchanged; no release asset,
GitHub Release, tag, push, merge, production pointer, or Gate13 action was performed. Production remains FACM 3.5.15.

The local implementation is ready for review as a single-launcher prerelease candidate. Public publication, clean-machine
first run against the published test Release, second-launch zero-download proof, real Windows acceptance, and any
production/cutover action still require separate authorization.

## 2026-09-01 FREE-DIST-4 public prerelease validation — blocked

The explicitly authorized non-production GitHub prerelease was briefly published as tag `v4.0.0-free-dist-test.1`,
title `FACM 4.0.0 FREE-DIST test.1`, `prerelease=true`, `draft=false`, with exactly 13 flat assets. It was withdrawn
and its release/tag deleted after the blocker below; no public test.1 release currently exists. The release targeted
remote `main` SHA `269da6c751a8463542ed0d172300675deff9571e`; the approved local launcher/bundle candidate was built
from local commit `2b93e5545598cbd006dab16d7c7b66519b723fd4` and was not pushed. The historical URL was:

```text
https://github.com/xianyumht-cmd/facm/releases/tag/v4.0.0-free-dist-test.1
```

Before withdrawal, public release evidence passed for anonymous download and exact-byte comparison: 13/13 assets, size and SHA-256
identical to `D:\project2\facm-free-dist-final-candidate-flat4-20260901\bundle`. A clean directory containing only
the reviewed `FACM.exe` completed real public first-run provisioning in 30.3 seconds, downloaded 103,647,538 CAB
bytes, and started the real Orb. The same directory passed second launch in 2.1 seconds with no manifest fetch,
component download, or extraction events. A temporary unavailable WinHTTP proxy also passed the offline third launch
in 4.3 seconds; WinHTTP was restored to Direct access. The public first-run log is at
`D:\project2\facm4-public-first-run-test-20260901\.facm\logs\bootstrapper.jsonl`.

The public proxy observations were: manifest/component metadata returned 200 from all four candidates;
`gh-proxy.com` returned CAB Range 206/65,536 bytes; `ghfast.top`, `gh.llkk.cc`, and direct GitHub had transient TLS
failures for the bounded CAB probe. Existing deterministic BOOT3-C evidence remains 8/8 PASS, including proxy
failover and corruption/active-preservation cases.

The remaining blocker is a real interrupted-download boundary defect. If the process is terminated after a CAB's
last byte is written but before the `.partial` file is promoted, the file size equals the authenticated package size.
The current bootstrapper treats that file as a Range offset, receives HTTP 416 from the public transports, preserves
the full-size `.partial`, and cannot recover automatically. A controlled 1 MiB valid-prefix Range resume did complete
all three downloads and extractions, and the subsequent Orb launch passed, but this full-size `.partial` case prevents
`v4.0.0-free-dist-test.1` from being marked public-ready. The failing test root is
`D:\project2\facm4-public-interrupted-range-test-20260901`; its full-size partial SHA-256 matched the approved CAB.

Do not create the final user directory, merge PR #234, push source, move formal P7, run Gate13, change production
pointers, or retire FACM 3.5.15. Next task: fix full-size partial recovery, add a regression test, rebuild/re-sign a new
candidate, and use a new non-production `test.2` release identity after review. Production remains FACM 3.5.15.

## 2026-09-01 FREE-DIST-5 full-size partial recovery and public test.2 acceptance

FREE-DIST-5 fixed the interrupted-download boundary in `src/FACM.Bootstrapper/main.cpp`: a `.partial` whose size equals
the authenticated package size is now verified by exact SHA-256 before promotion to the complete cache. A valid full-size
partial is promoted without an EOF Range request; an invalid full-size partial is removed and restarted from byte zero;
the existing nonzero-prefix Range resume and safe oversized-partial rejection remain unchanged. The deterministic
regression was added to `tools/release/Test-FacmBoot3CHttpsDistribution.ps1`, including valid and invalid full-size cases.

Local verification for the new candidate:

- native Release bootstrapper: `D:\project2\facm-free-dist5-native-test2-20260901\FACM.exe`, 3,364,691 bytes,
  SHA-256 `887386803d33215304a21c5e55fcf84c1fef0b7bfa273d7feb828f711425edb5`;
- flat signed bundle: `D:\project2\facm-free-dist5-test2-candidate-20260901\bundle`;
- bundle evidence: `D:\project2\facm-free-dist5-test2-candidate-20260901\free-dist-evidence.json`;
- BOOT3-C evidence: `D:\project2\facm-free-dist5-test2-boot3c-background4-20260901\results.json`, 10/10 PASS;
- FREE-DIST evidence: `D:\project2\facm-free-dist5-test2-proxy-probe2-20260901\free-dist-test-results.json`;
- BOOT2, BOOT3-A, BOOT3-B, BOOT3-C, FREE-DIST and all 32 non-cutover source gates passed;
- `D:\project2\dotnet10\dotnet.exe` SDK 10.0.400, `FACM4.sln` Release x64: 0 warnings / 0 errors;
  FoundationSmoke `--skip-gate13` and WindowsSmoke: SUCCESS.

The new non-production GitHub Release is public as tag `v4.0.0-free-dist-test.2`, title
`FACM 4.0.0 FREE-DIST test.2`, `draft=false`, `prerelease=true`, targeted at remote `main` SHA
`269da6c751a8463542ed0d172300675deff9571e`. Anonymous download and local comparison passed for all 13 flat assets,
with exact sizes and SHA-256 values. Download evidence is retained at
`D:\project2\facm-free-dist5-test2-public-assets-20260901`.

Public single-launcher acceptance passed with the fresh one-file launcher:

- first run: `D:\project2\facm-free-dist5-test2-public-first-run2-20260901`, exactly one `FACM.exe` before launch,
  19.7 seconds, 103,647,538 CAB bytes, real Orb started;
- second launch: 0.1 seconds, no new manifest fetch, component download, or extraction events;
- offline third launch: 0.1 seconds with temporary invalid WinHTTP proxy, no new network/extraction events, proxy restored
  to Direct access;
- nonzero Range resume: `D:\project2\facm-free-dist5-test2-public-range-resume-20260901`, 1 MiB prefix, PASS;
- valid full-size partial: `D:\project2\facm-free-dist5-test2-public-fullsize-valid-20260901`, exact app package promoted,
  no app CAB download event, PASS;
- invalid full-size partial: `D:\project2\facm-free-dist5-test2-public-fullsize-invalid-20260901`, invalid partial rejected,
  three CAB downloads including app restart from byte zero, final exact hash and Orb, PASS.

The final user review directory is `D:\project2\FACM-4.0-FREE-DIST-TEST` and contains exactly one `FACM.exe`:
3,364,691 bytes, SHA-256 `887386803d33215304a21c5e55fcf84c1fef0b7bfa273d7feb828f711425edb5`. The local single-launcher
harness had an environment-only CurrentUser Root certificate-store stall during one rerun; its core provisioning/Orb/
transport checks passed, and the independent public one-file acceptance above is the authoritative release evidence.
No source push, PR #234 merge, Gate13, Formal P7 move, production pointer change, or production restart occurred.
Production remains FACM 3.5.15.

## 2026-09-01 P7 UX-CLOSEOUT-1 local implementation state

The local P7 UX closeout is implemented on the active task branch at final UX HEAD `0a7179c` after the verified
FREE-DIST-5 baseline commits `7edabf5`, `446c08f`, and `907105c` (starting HEAD
`2b93e5545598cbd006dab16d7c7b66519b723fd4`). The scope is local
review only: ten-theme semantic contrast hardening, ControlMatrix footer geometry correction, compact League Workbench
guide presentation, removal of only the duplicate Repair/Cleanup exit-game button, preservation of the League efficiency
shortcut, OP.GG icon-guide decoration with Tencent-first/CommunityDragon-fallback cached assets, and user-facing GGman
(`鸡鸡侠`) branding while retaining FACM internal identifiers and data roots.

Verified locally with `D:\project2\dotnet10\dotnet.exe`: FACM.App Release x64 build succeeded with 0 warnings/errors;
FoundationSmoke `--skip-gate13` passed including theme contrast and guide-asset-route checks; WindowsSmoke passed for
desktop pet IPC lifecycle and FACM 4.0 Windows runtime. Targeted shell, accessibility, League, repair, personalization,
and P7 closeout source gates passed after the dependency-boundary correction. The local user-review candidate is now
`D:\project2\GGman-UX-CLOSEOUT-REVIEW-20260901`: root `GGman.exe` is 3,364,691 bytes with SHA-256
`887386803d33215304a21c5e55fcf84c1fef0b7bfa273d7feb828f711425edb5`; `.facm\state\active.json` points to the
pre-provisioned current Core at `.facm\versions\4.0.0-ux-closeout-1`, whose launcher-side app file is 420,946,212
bytes. Final native-launcher-to-Core startup returned a responsive `GGman（鸡鸡侠） 4.0` window and closed normally.
Rendered UI evidence is retained at `D:\project2\ggman-ux-closeout1-evidence-20260901` for ControlMatrix, League
Workbench, League guide, Repair/Cleanup, and dark/light Personalization review. No source push, PR #234 merge, Gate13,
Formal P7 move, production change, or production restart was performed. Protected dirty paths remain untouched:
`src/FACM.Platform.Windows/FACM.Platform.Windows.csproj`, `out/`, `setup.inf`, and `setup.rpt`.

## 2026-09-02 P7 integration checkpoint

The automatic icon-first ChampSelect guide changes are present in the active local task worktree and
have not been pushed or merged. The current local source validation is green: `FACM4.sln` Release x64
build has 0 warnings/0 errors, FoundationSmoke `--skip-gate13` and WindowsSmoke are SUCCESS, and all
32 non-cutover `check-facm4-*.ps1` gates pass under PowerShell 7.6.4. The detailed-build source gate
now records the verified 4.5-second OP.GG budget used by the implementation.

Fresh read-only live probes on 2026-09-02 found no League client process: discovery returned
`process-not-found`, LCU audit returned `no-session`, and the ChampSelect observer returned
`no-session`. The active local candidate starts r4 and remains responsive, but the native screenshot
helper still fails with `SetIsBorderRequired` / `E_NOINTERFACE`. Consequently final real ChampSelect,
late League startup reacquisition, and close/reopen lifecycle acceptance are `BLOCKED_BY_CLIENT_STATE`,
not PASS. PR #234 remains Draft with base P6 and head `9744af8…`; production remains FACM 3.5.15,
Gate13 and production pointers remain untouched, and the protected dirty paths remain un-staged.

## 2026-09-01 P7 UX-CLOSEOUT-2 manual HaiDou guide state

The local manual HaiDou closeout is implemented on the active task branch. The existing manual
query remains the fallback/detail surface; it now renders one Core-level `MayhemGuidePresentation`
projection in the same Workbench card and uses that same projection for the PNG export. Empty or
unverified optional sections are omitted instead of rendered as internal diagnostics or fabricated
values. The OP.GG detailed source budget is 4.5 seconds because the real `zh-cn/lol/modes/aram-mayhem`
page can take about 4.5 seconds to return; the parser now keeps the real skill-order fallback and
the existing two-path/limited item behavior. The real page did not expose a verified Runes table,
so the manual guide does not claim a verified rune recommendation for that source.

Verified locally with `D:\project2\dotnet10\dotnet.exe`: FACM.App Release x64 build succeeded with 0
warnings/errors; FoundationSmoke `--skip-gate13` passed including the manual guide projection and
secondary fixtures; WindowsSmoke passed. The manual review candidate remains
`D:\project2\GGman-HAIDOU-TEXT-REVIEW-20260901\GGman.exe`. Real UI review succeeded for 洛、光辉、石头人、
琴女 and for 寒冰 after one bounded retry. Evidence is retained under
`D:\project2\ggman-haidou-text-evidence-20260901`, including the verified copied PNG
`洛-guide-share.png` and the four secondary-champion screenshots. No source push, PR #234 merge,
Gate13, Formal P7 move, production pointer change, or production restart occurred. Production remains
FACM 3.5.15.

## 2026-09-01 P7 LIVE-LCU-FIRST lobby audit

The standalone read-only audit reuses the same `WindowsLeagueTransportSessionSource` and
`LeagueHttpGateway` contracts used by the product; it does not add a production owner or polling
loop. Evidence is retained at `D:\project2\ggman-live-lcu-guide-audit-20260901\lobby-audit.json`.
The observed live session was HTTPS, source `process-command-line`, process ID 5816, port 61944;
the command-line credential itself was never written to evidence. `LeagueClientUx` was present at
file version `16.17.812.490`. The actual phase response was `None`, so no ChampSelect payload was
claimed.

Observed endpoint facts: gameflow phase `200` with `None`; gameflow session, lobby, legacy
ChampSelect, and team-builder ChampSelect were `404 expected-unavailable`; current summoner was
`200` with 469 bytes but all identity values were redacted from the evidence; champion summary was
237 entries; items 868; summoner spells 39; perks 103; Cherry Augments 657; Rakan detail
`/champions/497.json` `200`; Rakan icon `/champion-icons/497.png` `200`. No current local champion,
hover/selected/locked state, or ChampSelect-specific augment ranking is verified yet.

The audit runner is `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\src\FACM.WindowsSmoke\LeagueLcuAuditSmoke.cs`
and is invoked with `--league-lcu-audit-live`. The next required live step is a normal ChampSelect;
until then the automatic guide remains intentionally fail-closed.

The shared Gameflow cadence now retries `NotRunning`, `Connecting`, and `ClientError` every 3
seconds, reducing the late-start/restart reacquisition window without adding an owner. Deterministic
Gate8/Gate12 cadence assertions were updated and passed in the latest full App Release x64 build,
FoundationSmoke `--skip-gate13`, and WindowsSmoke run. The real
GGman-first/League-later and close/reopen League sequences remain pending because they require the
user's normal desktop lifecycle; League was not closed or restarted by this task.

## 2026-09-01 P7 LEAGUE-GUIDE-MORPH-1 automatic icon-first Mayhem guide

The automatic ChampSelect guide is implemented on the active local task branch, but remains a review
candidate rather than a production or release change. The process-level `LeagueBenchRuntimeSnapshot`
now carries the observed local champion ID, so the existing single Gameflow/Bench observer can detect
the current champion and champion changes without adding another League session, gateway, timer, or
polling owner. `MainWindow.ChampSelectGuide.cs` renders an icon-first guide below the existing
horizontal ChampSelect strip in the same MainWindow. It reuses the shared `MayhemProductQueryService`
and shared read-only `ILeagueReadGateway`; no automatic League write or configuration application is
performed.

The guide keeps the full rich `AugmentRows` result and paginates only the presentation at six icons per
rarity page. It supports the available `棱彩`/`黄金`/`白银` tabs, per-rarity page state, champion-change
reset, stale-generation cancellation, champion/skill/summoner/item icons, hover/focus inspection, and
the existing manual HaiDou query as fallback/detail. The OP.GG numeric rarity values `8/4/1` are now
normalized to `棱彩/黄金/白银`; if the LCU champion summary is incomplete, the identity loader falls
back to the current champion's typed detail endpoint.

The user's real candidate screenshots exposed both defects before this correction: one ChampSelect
case showed the existing strip but no champion name, and a Kled (`暴怒骑士`) case loaded champion,
skill, spell, and item icons while the augment area incorrectly said no graded icons were available.
The numeric-rarity parser regression and ID-detail identity fallback are now covered by deterministic
FoundationSmoke fixtures. A reflection check against the cached real Hecarim OP.GG page now parses
190 augment rows, including the three normalized rarity values; this also fixed the nested escaped
HTML-attribute case that previously caused the real payload to parse as zero rows. The candidate
active pointer is `D:\project2\GGman-AUTO-GUIDE-REVIEW-20260901\.facm\state\active.json`, targeting
`.facm\versions\4.0.0-auto-guide-20260901-r4`; r4 contains the current Release assemblies and was
activated only after the prior candidate exited normally. No process was forcibly terminated by this task.

The latest live event evidence separates the remaining latency from the lookup failures. LCU
`/lol-gameflow/v1/gameflow-phase` and `/lol-champ-select/v1/session` calls were successful and generally
completed in 0–3 ms (the largest observed sample was 93 ms). The first ChampSelect observation briefly
had zero actionable candidates for about eight seconds while the client session was hydrating; this is a
client lifecycle timing gap, not a slow LCU request. The visible first-query delay is instead the public
OP.GG pipeline: the uncached augment/build responses were approximately 830 KB/605 KB/740 KB and arrived
around 18:28:41, while the product query intentionally waits for its bounded public-source and enrichment
stages before rendering the guide. Repeat lookups should use the existing local cache.

The concrete lookup fixes in r4 are: carry the stable champion alias from the LCU summary/detail payload,
retry a temporarily empty identity for a bounded period, and avoid the previous one-shot display-name
query that failed for names such as Volibear (`不灭狂雷`). Augment icons now also have a strict allowlisted
OP.GG static-image fallback; the observed LCU augment icon requests themselves returned HTTP 200, so any
remaining grey tiles require manual confirmation of paint timing versus the source asset.

Post-fix evidence: FACM.App Release build with `D:\project2\dotnet10\dotnet.exe` succeeded with 0
warnings/errors; FoundationSmoke `--skip-gate13` and WindowsSmoke succeeded; League Bench, Mayhem
Augment, Mayhem WinUI, and P7 closeout source gates succeeded. The screenshot helper could launch the
candidate and observe a responsive `GGman（鸡鸡侠） 4.0` window, but its native window-state capture
failed with the environment compatibility error `SetIsBorderRequired failed: 不支持此接口
(0x80004002)`. Therefore source/runtime checks are PASS, the user's screenshots are real pre-fix
symptom evidence, and post-fix automatic UI acceptance is still manual-required.

No source push, PR #234 merge, Gate13, Formal P7 move, production pointer change, deployment, or
League restart occurred. Production remains FACM 3.5.15. Protected dirty paths remain untouched:
`src/FACM.Platform.Windows/FACM.Platform.Windows.csproj`, `out/`, `setup.inf`, and `setup.rpt`.

## 2026-09-02 P7 League discovery fallback fix

The reported GGman-first/League-later symptom was traced to discovery before HTTP: the running League
client exposed a valid `LeagueClientUx` command line through WMI, but the WinUI candidate's native
`NtQueryInformationProcess` and dynamic COM fallback returned no command line. The stale empty
`LeagueClient\lockfile` was not a usable credential source. A strongly typed `System.Management`
reader now runs in the App host and is injected into the existing Platform process snapshot provider;
the single session owner, gateway, and Gameflow heartbeat remain unchanged.

The local GUI candidate `D:\project2\GGman-AUTO-GUIDE-REVIEW-20260901\.facm\versions\4.0.0-auto-guide-20260901-r6`
was started while the real League processes were running. Its sanitized event log records
`process-fallback-success` for the League process, an LCU `200` response, and `Connected / Lobby`.
The fix is committed locally as `bbe7dad` (`fix(p7): make League discovery WMI fallback host-aware`).
Release x64 build, FoundationSmoke `--skip-gate13`, WindowsSmoke, and all 32 non-cutover source gates
pass. During the user's requested restart check, the candidate first recorded `process-not-found`, then
rediscovered the new League process with `process-fallback-success`; after one bounded LCU hydration
timeout, subsequent gameflow requests returned HTTP 200 and `Connected / Lobby`. This verifies the
late-start/restart reacquisition path. ChampSelect UI acceptance still requires the user's normal
selection sequence.

Production remains FACM 3.5.15. The fix was not pushed or merged; Gate13, production pointers,
deployment, restart, and the protected dirty paths remain untouched.

## 2026-09-02 GGman/FACM icon refresh

The selected first-style icon is implemented as a deep-navy electric-cyan double-G orbit. FACM 3.x,
FACM.App 4.0, and the Native Bootstrapper use the shared multi-resolution `FACM.ico` for EXE/taskbar
identity. FACM.App embeds separate 16/20/24/32 tray resources with a reserved upper-right status area:
gray for League not running, yellow for connecting/unavailable, and green for connected. The status is
projected from the existing Gameflow snapshot and does not add a second League discovery or polling path.

Current verification: FACM.App Release x64 builds with 0 warnings/errors and exposes all four expected
embedded icon resources; its EXE and the legacy FACM Release EXE both expose associated Windows icons.
The legacy net48 build succeeds with its pre-existing obsolete API warning. Native Bootstrapper resource
source is wired through CMake; a native compiler/CMake installation is not available on this machine for
a fresh bootstrapper binary build. Production and the active candidate pointer remain unchanged.

## 2026-09-02 Gitee distribution path and local release publishing

The public Gitee mirror `https://gitee.com/xymhtcmd/facm` is reachable anonymously, and the local
credential manager now contains an authenticated Gitee credential for release operations. No Gitee
Release or asset existed before this task. The source change adds Gitee as the first metadata origin,
keeps GitHub as fallback, prevents public GitHub proxy prefixes from being applied to Gitee release URLs,
and extends the legacy bridge/native redirect allowlists to the exact FACM Gitee repository paths.

FACM 4.0.2 (`v4.0.2`) was published, but Gitee's second attachment redirect host exposed a bootstrapper
allowlist gap. The corrective FACM 4.0.3 (`v4.0.3`) bundle is built from fixed source commit
`14bac2d64deecfd9e9d10b8844661cabfdb3ebd4`, with
bootstrapper SHA-256 `FC09650F0818E0FF44BB3B3D97EBEB3730AB3424547153D807FE494AEFE77FDA` and
detached manifest SHA-256 `669E1297C382FF69B2A7E6E0C93E0ACAB70813CDE6E5CF72D853946C4AEC308A`. Its freshly
rebuilt application CAB is 23,500,520 bytes with SHA-256
`9A41CBF3283AECA0F65CBDCF186235A6499397F9A612B60BC514599FFABE349A`. The
signed 3.5.18 bridge copy is `D:\project2\facm-release-3.5.18-gitee\FACM.exe` with SHA-256
`78D606CF7C2AB3F6F0F177F91F512FFDB214D313F58C445A7D7C25858F8791B0`.

The local publisher is `scripts/release/publish-gitee-release-local.ps1`; it reads the Gitee token only
from the OS credential manager, never writes it to the repository, and supports preview before upload.
The source is pushed to both GitHub and Gitee `main` at commit `90e50b2` (the release binary provenance remains
the explicit `14bac2d` source commit above).
Gitee Releases `v4.0.2` (15 bundle files), corrected `v4.0.3` (15 bundle files), and `v3.5.18` (bridge plus SHA256 record) are published; the
GitHub `v3.5.18` bridge Release is also published for the one-time legacy hop. The online manifest now
offers 3.5.18 from GitHub and the working tree now targets the Gitee-first 4.0.4 migration release. Existing installed 3.5.17
clients still contain the old GitHub-only URL allowlist, so they must receive the signed 3.5.18 bridge once
before the Gitee-first 4.0.3 migration path can be used automatically.

The isolated Gitee first-run probe is currently paused for user gameplay. It fetched the signed manifest
and all three 4.0.3 CABs through the direct Gitee route (the log records `manifest-validated` and three
`component-download-complete` events), but the native FDI extraction step ended with `FDI 8:0` after the
Windows runtime files had been written. The downloaded Windows CAB matches the local SHA-256 and its
file count/installed bytes match the manifest; this is therefore an unresolved native extraction/runtime
verification item, not evidence of a bad Gitee download. Test root: `D:\project2\facm-gitee-4.0.3-e2e-20260902`.
No production installation or user process was changed by this probe.

## 2026-09-02 Gitee 首次启动问题已定位，4.0.4 候选待发布

复现诊断已确认此前的 `FDI 8:0` 不是 Gitee 网络或 CAB 下载损坏。隔离目录中的清单和三个 CAB
均可通过 Gitee 直连下载；失败发生在 app CAB 解包时，`wuceffectsi.dll` 被截断。当前 app CAB
实际为 49 个文件、58,001,209 字节、contentDigest `8158e15f8ab9c6683770460bab736a2bafa02c5a41c110c86d5103c3edc2401a`，
而已发布 4.0.3 清单错误沿用了旧种子值 58,000,621 和 `53ea...`，原生解包器按错误的 installedSize
上限主动拒绝了剩余写入，最终显示 FDI 8:0。旧 `v4.0.3` 保留为历史资产，不覆盖其附件。

发布脚本 `Build-Facm4SelfSignedBundle.ps1` 现在会先用系统 `expand.exe` 解包每个 CAB，再从实际文件
重新计算 installedSize、fileCount、contentDigest，并同步 ownership-report；`Test-FacmReleaseBundle.ps1`
也会执行同样的 CAB 内容校验，旧 4.0.3 已能被该校验明确拦截。native bootstrapper 保留受限的写入/关闭
诊断信息。基于当前实际 CAB 和本地 RSA-2048 detached key 已生成 4.0.4 候选包，bundle validator、
BOOT-2/BOOT3-A/BOOT3-B 检查、.NET build、mirror/migration smoke 均通过；候选 bootstrapper SHA-256
为 `650B263DBF0B8D43208FC35471850DA276A7BAB8CCED23069CF2D23E0948DE0F`。Gitee v4.0.4 Release、
源代码推送和无 VPN 远端首次启动复测仍待本轮提交后完成。
