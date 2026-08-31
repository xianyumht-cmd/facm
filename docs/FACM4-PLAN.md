# FACM 4.0 执行计划与实时进度

Status: **LOCAL-BS6-BENCH-STRIP-CANDIDATE / DETERMINISTIC-GREEN / REAL-LEAGUE-VALIDATION-PENDING**
Production baseline: **FACM 3.5.15（保持不变）**
Active line: `feat/facm4-function-parity-p7-closeout` / PR #234 / Issue #233
Canonical main: `269da6c751a8463542ed0d172300675deff9571e`
Latest code fix head: `e834763b09f69d7aaa0951af3bc8a0601d64edf3`
Latest code+plan PR head used by Foundation: `803e1ba5f9b671b0a787a8c77bb39912d4211b7d`
Latest code-bearing Foundation: **#632 / run `33233590075` = SUCCESS**
Latest canonical-doc full regression: **#633 / run `33233865204` = SUCCESS**
Historical cloud staging candidate: `e387295fd61c233f8e9892016a6e9917b448cd5b` (parent baseline `2730eda15dc28a801871b5a3d10b4eecbd03a656`, formal P7 `9744af848e4b888c1876e76e2cbf0c06d5c526bf`)

Current local MS9 candidate: `D:\project2\facm-ms9.4-runtime-out-20260831-1305` with
`FACM.App.exe` SHA-256 `94AD1C97C93C32285A76F27E3CB3FE78FBE42B7D1BDEEC2DC18B789DD4E66412`.

Current local BS6 review candidate: `D:\project2\facm-bs6-review-out-20260831-1500` with
`FACM.App.exe` SHA-256 `6C12C65988953AD01C258D8D712BEC7291CF82F773A1BE9F2D298CD8736BE7BB`.

> 本文件是 FACM 4.0 当前工作的实时计划账。每完成一批代码审查、修复、CI 结论、真机证据或正式交接，都要同步更新。生产/cutover/release 权限不从本文件自动产生。

## 当前结论

2026-08-31 MS9 诊断与修复已完成：MS8 失败日志实际包含 90 个 `facm.surface.presentation-failed`，全部是共享呈现 invariant 在 `136×39` 实际窗口与 `36×36` Orb 目标不一致时触发的 `System.InvalidOperationException` / `0x80131509`。失败发生在 UI thread 2 且 Dispatcher access 正常；不是 League owner、LCU、XAML 内容或后台线程根因。

MS9 最终修复在唯一 Morphing MainWindow 的 Windows 平台层适配 `WM_GETMINMAXINFO` 最小跟踪尺寸，保留原窗口过程和既有 `AppWindow.MoveAndResize` owner。最终候选真实窗口达到 `36×36`，100 次 Orb→ControlMatrix→Orb 全部成功；Repair/FeatureSurface→Orb 与 LeagueSurface→Orb 也已真实通过。候选日志为 0 presentation-failed、0 invariant-failed、0 stale、0 unhandled、0 fatal。outside-click、ChampSelect/Lobby 自然回归、modal、tray、桌宠、多屏/DPI 和视觉截图仍等待用户手动验证，不得据此宣称完整 P7 或 release-ready。

FACM 4.0 P7 的功能等价与自动稳定性层已完成，但 Win10 真机继续暴露了一个此前 smoke 未覆盖的跨进程 PetHost cache 性能缺陷：旧实现每个新 FACM 进程第一次启用桌宠时，先完整读取并 SHA-256 约 76.9 MB 内嵌 PetHost ZIP，随后才知道磁盘 cache 能否复用。

Batch M 已从根因修复：Foundation 构建 PetHost ZIP 后生成稳定 SHA identity，FACM 单文件同时嵌入 ZIP + tiny SHA resource；新进程优先用 SHA 直接检查 `runtime/pethost-host/<sha>`，完整 cache 命中时不再打开、更不再重新 hash 76.9 MB ZIP。跨进程 no-rehash smoke 已进入 WindowsSmoke。

Foundation #632 已全链路 SUCCESS，且实际日志确认 Release build 和 publish 都嵌入了 `FACM.Resources.PetHost.sha256`。随后 canonical docs head `b7bbb24bef5670196633f65ec2bbd5e441dd5b1e` 又通过 Foundation #633 全回归；这属于 MS9 之前的 Foundation 基线。当前执行焦点已转为 Morphing Bench Swap Strip 的真实 LCU/Win10 验证。

2026-08-31 的下一阶段 BS1–BS6 已完成本地代码事务：候选身份模型、同源 Workbench/Strip
呈现、自动显示 gate、一次既有 swap 路由、上下文 dismissal、详细卡片复用和回归门均已落地。
代码提交为 `4b9fe1b`、`fea17fd`；当前候选使用同一 Morphing `MainWindow` 的既有
`ChampSelectStrip`，并通过现有 LCU metadata/icon cache 读取头像。28 个 source gates、App 和
solution Debug x64、FoundationSmoke `--skip-gate13`、WindowsSmoke 均通过。自然 ARAM/LCU
交互和真实可视化仍是手动验收，不改变 P7/cutover 状态。

2026-08-30 在全新 candidate worktree 完成了 .NET `10.0.400` Foundation 等价链：FlyingHost/PetHost publish + self-test、28 个 source gates、`FACM4.sln` Release x64 restore/build、FoundationSmoke、WindowsSmoke、FACM.App single-file publish 均通过。FlyingHost 464 files / `72,052,263` bytes / SHA-256 `63f94f2bd3fbd4908d0736c9067f26c90afcd7798bdc2abc1929f7b2771cabb5`，PetHost 472 files / `76,915,115` bytes / SHA-256 `e295beec4035fe671b3e757b9b515668b8f7eca39178337a73c7c855424d00df`。FACM.App.exe 为 `377,994,404` bytes / SHA-256 `5aa53107fd8efcf67423c3b625908ec083ed6ff5c3effb6f3d80f613c1fe90d6`，输出 DLL entries 为 0。

本机还正式复现并修复了 .NET 10 `WFAC010`：旧 manifest DPI 节点与 WPF/WinForms analyzer 冲突；两个 host 改用 `ApplicationHighDpiMode=PerMonitorV2`，FlyingHost identity 改为 `FACM.FlyingHost.app`。三处 stacked-PR 生产控制 diff gate 改为比较 PR 基线直接父提交；生产控制文件本身未改动。candidate 收口提交的 hosted run `33295151374` / job `99213419340` 仍为 `runner_id=0`、`steps=[]`。

Gate13 不变：

```text
22 required / 12 Passed / 10 Blocked
ReleaseReady=false
CUTOVER BLOCKED
```

## 当前 7 步状态

1. 全面代码级故障审查 — **COMPLETED**
2. 现有 4.0 架构内批量修复 — **COMPLETED through Batch M**
3. 3.5.15 parity 复核 — **COMPLETED on source/code gates**
4. 自动压力与重复操作 smoke — **COMPLETED + cross-process PetHost coverage**
5. 完整 Foundation — **#632 SUCCESS**
6. 新统一候选 — **#632 artifact ready and independently hashed**
7. 统一真机功能验收 — **IN PROGRESS; MS9 real shell cycles green, manual Win10/League/pet validation pending**

## 稳定性批次账

| Batch | Head | 主要内容 | 结果 |
| --- | --- | --- | --- |
| A | `aca8aeb956a723fd0b48f77b89b747aa1cb3abd7` | Settings2 atomic mutation 起点 | closed |
| B | `05ab40708536d4b8e12ae6fdadb90de8a59219c8` | feature writers 迁移 | closed |
| C | `0c4423d89732e77a8bd67456cefa8ac210e998b5` | recovery-safe mutation/lifecycle | closed |
| D | `9d7a162788c5a33e2473c070bd040968938d6c6f` | PetHost/League/async containment | closed |
| E | `b5c47def7ca8ae4f9570fcb5de0341eaf355548a` | Desktop F atomic persistence | closed |
| F | `856078e9f90cc4e13ee7bd09e7b0e09a7d57164a` | League settings/recovery contract | closed |
| G | `cd8f3051780d4af1552cd06c91f050c871b3581e` | Maintenance retry/CTS/installer lifecycle | closed |
| H | `84bf4d97589d90b578e8fdc6526691556f8741d5` | source-gate/compiler cleanup | closed |
| I | `bb9f8e88d4ed868adf602c2ae87f64663379496e` | League cancellation/dialog teardown | #626 green |
| J | `4755c40c6c3ec751d27bf9cab31d74581f58f3d3` | Updater atomic fallback/rollback + helper self-test | #627 green |
| K | `f3906b84dd0076411dcd8a4fd82610d1d6c2a179` | repeated lifecycle/settings/League stress | #628 green |
| M | `6ba8c917c73e9f7eee1229b29ba9ed243be8ae83` | PetHost cross-process cache no-rehash + Busy UX | #632 green |
| MS9.1 | `a321424` | Morphing Surface failure forensics and operation telemetry | closed |
| MS9.2 | `c372388` | Preferred minimum geometry probe; superseded after runtime evidence | superseded |
| MS9.3 | `e834763b09f69d7aaa0951af3bc8a0601d64edf3` | HWND minimum-track-size adaptation for the one Morphing host | local real-cycle green; manual validation pending |

## 关键已修根因

### Win10 theme brush ownership

真实 Win10 曾在 `ABI.Microsoft.UI.Xaml.Media.ISolidColorBrushMethods.set_Color` 触发 `E_ACCESSDENIED`。平台/system brush 现在只读；FACM 运行时只修改 app-owned semantic brush。Personalization startup 保持 fail-soft。

### Personalization stale Busy

个性化控件过去依赖手工 `SyncPersonalizationSurface()`，async `IsBusy` 完成时可能没有 UI refresh。现通过 PropertyChanged + DispatcherQueue owner refresh；Busy 时状态条明确显示“正在处理，请稍候…”，不再显示“准备就绪但所有控件灰掉”。

### Settings2 lost update

feature writer 统一走 atomic narrow `UpdateAsync` transaction boundary；recovery 默认只读。40 轮 Theme/F/League concurrent narrow mutation + read-back 已进入 smoke。

### Maintenance / League lifecycle

Maintenance 初始化允许同进程重试；active async operation 自己持有 CTS/installer lifetime。League caller/lifetime cancellation 与 Window/ContentDialog teardown 有明确 containment，不把正常取消伪装 provider failure。

### Updater interrupted-replacement primitive

`File.Replace` 为主路径；fallback/rollback 使用同目录 `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)` 原子交换完整 staging/backup，不再 stream-copy over live EXE。built `FACM.Updater.exe --self-test` 在 Foundation 实际执行。

### Batch M：PetHost cross-process cache

2026-08-29 Win10 evidence：

- recovery state `Running` / 4.0.0.0 / `consecutiveFailures=0`；
- LKG `glass-blue` / `moth` / `enabled=false` / F=`1569,576`；
- pure pet selection greenfly -> dragonfly -> moth 完成；
- 点击启用 moth 后进入 `pet-enable-start -> IsBusy=true -> payload-preparing`；证据窗口超过 13 秒没有 `host-starting / ready / failed / finish`；
- 同期 F drag-save 仍继续，证明主 UI/message loop 没死，长耗时点在 payload prepare。

旧 #628 PetHost ZIP 约 76.9 MB。旧 bundle store 新进程必须先 `SHA256.HashData(bundle)` 再查 disk cache；同进程 24 次 prepare smoke 被 `_cachedPreparation` 掩盖了这一点。

Batch M 修复：

- workflow 生成 `out/PetHostBundle.sha256`；
- App 同时嵌入 `FACM.Resources.PetHost.zip` + `FACM.Resources.PetHost.sha256`；
- `RequirePetHostBundle=true` 时两者缺一即 build fail；
- 新进程先用 build-time SHA identity 查完整 disk cache；cache hit 时 `openBundle` 必须为 0；
- local/lightweight build 缺 identity 时保留安全 hash-on-demand fallback；
- WindowsSmoke 新建第二个 `WindowsPetHostBundleStore` 模拟新进程，强制验证 cross-process no-rehash；
- Personalization source gate 固化以上 contract。

## Foundation #632 实际证据

Run：`33233590075`，PR merge ref 包含 `803e1ba5f9b671b0a787a8c77bb39912d4211b7d`，结果 **SUCCESS**。

PetHost bundle 构建：

```text
PetHostBundle.zip bytes=76,924,303
PetHostBundle SHA-256=48e24e9a67f7f75dffc4bef56eeadee9c13d9cc028c38679c8fab0c651141fc4
```

Release build 与 publish 均实际输出：

```text
Embedding FACM 4.0 PetHost bundle as FACM.Resources.PetHost.zip
Embedding FACM 4.0 PetHost identity as FACM.Resources.PetHost.sha256
```

Personalization gate 明确通过：

```text
Personalization PropertyChanged/Dispatcher busy feedback: OK
Controlled PetHost build identity + extraction/cache/timeout boundary: OK
Cross-process PetHost cache no-rehash boundary: OK
FACM 4.0 Personalization foundation contract: SUCCESS
```

同 run 还通过：P1-P7 source/product gates、PowerShell 5.1 collector self-test、Release build 0 warnings/0 errors、FoundationSmoke、WindowsSmoke、single-file publish、publish-output verification、artifact upload。

## #633 canonical-doc regression

Canonical docs reconciliation head：`b7bbb24bef5670196633f65ec2bbd5e441dd5b1e`。

Foundation **#633 / run `33233865204` = SUCCESS**。这是 docs-only full regression，用于确认 Batch M canonical state / PR 状态记录没有破坏任何 gate/build/smoke；它不替代 #632 executable candidate。

## #632 新 targeted candidate

```text
artifact: facm4-x64
artifact id: 9709261625
artifact ZIP bytes: 165,704,303
GitHub digest: sha256:32331020c0c1c3fc93ebf70991ddff99a6349deede41e7374ae063da0aa9cb0a
code fix head: 6ba8c917c73e9f7eee1229b29ba9ed243be8ae83
PR head used by run: 803e1ba5f9b671b0a787a8c77bb39912d4211b7d
Foundation: #632 / 33233590075
```

下载后独立重算：

```text
ZIP SHA-256: 32331020c0c1c3fc93ebf70991ddff99a6349deede41e7374ae063da0aa9cb0a
FACM.App.exe bytes: 305,912,996
FACM.App.exe SHA-256: 5d65bd3f3e64a2520cb0c9514627a42e97781396d9e21013f04499fb464a9fea
ZIP DLL entries: 0
```

ZIP SHA 与 GitHub artifact digest 完全一致。

## #628 候选状态

Artifact `9708452498` 的完整性与 #628 自动化证据仍然有效，但已被 Batch M 真机 PetHost 缺陷 **supersede**，不得继续作为当前桌宠验收候选。

## 下一步：Win10 targeted retest

只用 artifact `9709261625`：

1. 解压到一个稳定目录，运行 `FACM.App.exe`。
2. 第一次启用任意桌宠：如果这个 SHA 从未在机器上缓存，允许发生一次真实 extraction；必须最终出现 `host-starting -> ready` 或明确 timeout/failure，不能无限 Busy。
3. 正常退出 FACM，再从**同一目录、同一个 EXE**重启。
4. 第二个 FACM 进程再次启用桌宠：完整 cache 已存在时不应再长时间停在 `payload-preparing`。
5. 在 enabled 状态连续切换至少 5-10 次（例如 greenfly/dragonfly/moth 往返）；每次 Busy 必须回到可交互状态。
6. Busy 时 UI 应显示“正在处理，请稍候…”，不应显示“准备就绪”。
7. 结束后上传 `facm4-events.jsonl`、`settings.v2.lkg.json`、`state.json`。

Targeted retest 通过后，再继续完整非破坏统一验收：Cleanup UAC cancel、四大入口、真实 League read paths、Settings、second launch、normal shutdown。

## Gate13 仍需真实 evidence 的 10 项

1. non-admin + real UAC cancel
2. Defender / SmartScreen
3. Windows 10 1809
4. Windows 10 22H2
5. controlled real-user Windows 11
6. real mixed-DPI / multi-monitor
7. real accessibility
8. real FACM 3.5.15 -> 4.0 Settings2 migration
9. interrupted updater replacement / rollback
10. final signature / package identity

Hosted CI、source gate、smoke、targeted bug fix 都不能自动把这些 blocker 改成 Passed。

## 合并前文档动作

在任何 merge-ready claim 前，把以下长期规则补进 `docs/PITFALLS.md`：

- WinUI 平台 ThemeResource brush 只能读；FACM 只改 app-owned semantic brush。
- first-chance UnauthorizedAccess 可能是 caught/nonfatal，必须结合 state/stack/lifecycle 判断。
- async Busy 驱动的 UI 必须有 PropertyChanged/Dispatcher completion refresh。
- Updater fallback/rollback 禁止 stream-copy over live executable。
- 大型内嵌 payload 的跨进程 disk cache 必须有构建期稳定 identity，禁止为了判断 cache key 每个新进程先完整 hash 数十 MB payload。
- 持久化 `enabled=true` 必须发生在外部/runtime ready 成功以后，不能预写成功意图。

## 2026-08-29 交接检查点

- `docs/HANDOFF-20260807.md` 已改写为当前 P7 / Batch M 完整交接，旧 #218 / XamlParse 状态不再作为新对话起点。
- 交接覆盖：已完成、已验证、失败/不足方案及原因、禁止重复路线、当前代码/CI/artifact、测试环境、未完成问题、下一步操作和日志判定分支。
- 新对话继续时优先读取：`AGENTS.md` -> 本计划 -> `PROJECT_STATE.md` -> `FACM4-P7-PARITY-CLOSEOUT.md` -> `HANDOFF-20260807.md`。
- 当前唯一应继续验证的 executable 是 #632 artifact `9709261625`；#628 artifact 不再用于桌宠验收。
