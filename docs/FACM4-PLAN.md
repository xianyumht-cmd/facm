# FACM 4.0 执行计划与实时进度

Status: **STABILITY-AUDIT-IN-PROGRESS**
Production baseline: **FACM 3.5.15（保持不变）**
Active line: `feat/facm4-function-parity-p7-closeout` / PR #234 / Issue #233
Canonical main: `269da6c751a8463542ed0d172300675deff9571e`

> 本文件是 FACM 4.0 当前工作的实时计划账。每完成一批代码审查、修复或 CI 结论，都必须在同一批次同步更新这里。`docs/PROJECT_STATE.md` 与 `docs/FACM4-P7-PARITY-CLOSEOUT.md` 在里程碑状态变化时做 canonical reconciliation。

## 当前原则

- 当前不是 UI 2.0 阶段；先把 FACM 4.0 的 3.5.15 功能等价与稳定性做干净。
- 当前不继续向用户连续投递中间测试包；代码审查、压力 smoke 与完整 Foundation 全绿之前不生成新的真机候选。
- P2-P7 stacked PR 继续保持 Draft / 未合并。
- 不修改 `online/version.json`、`release/request.json`，不发布 4.0.0，不做 production cutover，不退休 legacy。
- Gate 13 仍是独立证据链；Hosted CI/source gate 不能替代真实 Windows 证据。

## 7 步执行顺序

1. **全面代码级故障审查**：Settings、async/Busy/cancel、窗口生命周期、single-instance、PetHost、League shared runtime、updater、Cleanup/UAC。
2. **在现有 4.0 架构内批量修复**：不回退架构，不用临时补丁掩盖根因。
3. **完整 FACM 3.5.15 parity 复核**：确保四个主入口和全部已迁移行为不是占位/死链。
4. **自动压力与重复操作 smoke**：针对并发 Settings、重复开关窗口、重复切主题/桌宠、League 配置、取消/失败路径等做 20-50 次级别验证。
5. **完整 Foundation**：全部 source gates + Release build + FoundationSmoke + WindowsSmoke + publish + artifact verification 同一 head 全绿。
6. **只生成一个新的统一候选**：前五步没有未解释失败后再产出。
7. **统一真机功能验收**：通过后才讨论 stacked merge；UI 2.0 在功能等价之后；Gate13/cutover 仍需独立授权与证据。

## 稳定性审查进度

### 已完成：Win10 主题资源启动根因闭环

- 真实 Win10 曾出现 `E_ACCESSDENIED`：应用运行时尝试修改 WinUI 平台拥有的系统 brush。
- 已改成 FACM 自有可变 semantic `SolidColorBrush`；平台 brush 仅用于读取/复制 fallback/High Contrast，不再写入。
- 个性化 startup 增加 fail-soft。
- 后续真机 evidence 已确认旧 brush 启动崩溃链不再出现，应用可进入 `Running`。
- 这只关闭了该启动根因，不代表整个 Win10/Gate13 已通过。

### Batch A — `aca8aeb956a723fd0b48f77b89b747aa1cb3abd7`

- 找到跨功能 Settings2 lost-update 根因：多个模块使用 `Load whole document -> 修改一个字段 -> Save whole document`，并发时会互相覆盖。
- 新增 atomic narrow Settings2 mutation contract，Recovering repository 串行执行 load-mutate-save-LKG transaction。
- 多个 feature writer 迁移到 `UpdateAsync`；PetHost ready timeout、异步 teardown、League hotkey rollback、Settings concurrent smoke 同批进入。
- Foundation #618：Architecture gate 因注释 `settings file.` 被大小写不敏感 `File\.` 误命中；不是实际架构越界。

### Batch B — `05ab40708536d4b8e12ae6fdadb90de8a59219c8`

- Personalization / Cleanup async-void 增加最终 containment。
- #619 Architecture SUCCESS；Cleanup stale gate 仍要求旧 `SaveAsync`。

### Batch C — `0c4423d89732e77a8bd67456cefa8ac210e998b5`

- Cleanup gate 改为必须走 atomic `UpdateAsync`，并禁止 feature whole-document `SaveAsync`。
- #620 Architecture / Shell / Desktop / Cleanup / Repair SUCCESS；Personalization stale gate 仍要求旧 Save。

### Batch D — `9d7a162788c5a33e2473c070bd040968938d6c6f`

- Personalization gate、F 拖动坐标、P7 Settings parity 全面迁移 atomic narrow update；Foundation Settings smoke 增加 concurrent narrow mutations。
- #621 Architecture/Shell SUCCESS；Desktop stale gate 仍要求旧 Save。

### Batch E — `b5c47def7ca8ae4f9570fcb5de0341eaf355548a`

- Desktop / League Efficiency gate 对齐 atomic update。
- 建立本文件作为每批必更的 4.0 实时计划账。
- #622 通过 Architecture / Shell / Desktop / Cleanup / Repair / Personalization；League Workbench gate 因 PostGame recovery-origin 旧语义停止。

### Batch F — `856078e9f90cc4e13ee7bd09e7b0e09a7d57164a`

- PostGame / Recommended automation settings 保持 atomic `UpdateAsync`，显式恢复 recovery-origin 语义；linked cancellation 语义统一。
- #623 通过 Architecture、Shell、Desktop、Cleanup、Repair、Personalization、League Workbench、Recommended、Efficiency、Bench、全部 Mayhem、P6 Maintenance、P6 Updater、P7 Settings；仅旧 P7 lifecycle 字面量检查停止。
- 复核确认桌宠真实 teardown 已集中到 `DisposePersonalizationRuntime()`，因此更新 gate 而不是回退生命周期实现。

### Batch G — `cd8f3051780d4af1552cd06c91f050c871b3581e`

- Maintenance 初始化只有成功后才 latch `IsInitialized`，失败可在同一 app session 重试。
- More Settings 每次重新进入可 `RetryInitialization()`，不依赖 visual-tree 二次 Loaded。
- update download linked CTS 由 active async operation 持有并在 finally 释放；shutdown 只 cancel。
- installer 增加 active-operation 计数，下载/replacement 未退出时延迟 Dispose。
- Maintenance 所有 async-void handler 增加最终异常 containment，install dialog 返回后重检当前状态。
- P7 lifecycle gate 改为检查 centralized personalization teardown；Maintenance gate 强制 retry/CTS/deferred teardown。
- #624 在 Architecture gate 因注释 `same process.` 被 `Process\.` regex 误报而停止；运行代码没有 Process API。

### Batch H — `84bf4d97589d90b578e8fdc6526691556f8741d5`

- 注释改为 `same app session`，不放宽架构 gate、不改变运行逻辑。
- updater interruption 审查确认：现有 helper 有 staging / `.facm-old` / hash mismatch rollback / 新进程 5 秒 early-exit rollback，但 deterministic smoke 没有模拟 updater helper 自身在 replace 中途被强制终止；Gate13 `update.interrupted-replacement-rollback` 继续保持 Blocked。

Foundation #625 / run `33227469666`：

- 所有 source gates SUCCESS，Release Evidence 正确保持 `22 required / 12 passed / 10 blocking`，Cutover Guard 正确 BLOCKED。
- Restore SUCCESS。
- Release build 发现真实编译缺陷：`MainWindow.LeagueWorkbenchRuntime.cs(105,26) CS0157`，原因是 `finally` 内 `return`；后续 WMC9999 属伴随错误。

### Batch I — `bb9f8e88d4ed868adf602c2ae87f64663379496e`

产品修复：

1. `RefreshLeagueWorkbenchRuntimeAsync()` 的 `finally` 不再离开 finally，改为仅在窗口仍存活时 enqueue UI refresh。
2. League Refresh / Advisor / ItemSet / automation settings 全部按 linked token 识别 caller/lifetime cancellation；正常取消不再伪装成 provider failure。
3. 推荐符文/技能和装备集的 `ContentDialog.ShowAsync()`、结果 dialog、async-void handler 增加 close/XamlRoot teardown containment；`ContentDialogResult.Primary` 写入门槛保持不变。

Foundation #626 / run `33227662540`：**SUCCESS**。

- 同一 head 上所有 source gates、Release build、FoundationSmoke、WindowsSmoke、single-file publish、publish verification、artifact upload 全部 SUCCESS。
- 这是本轮稳定性审查第一次完整穿透 source gates -> Release build -> 两层 smoke -> publish -> artifact 的全绿 head。

### Batch J — `4755c40c6c3ec751d27bf9cab31d74581f58f3d3`

Updater interruption hardening：

1. **正式 EXE 不再被 fallback 流式覆盖**
   - 保留 `File.Replace` 为主路径。
   - fallback 先保存完整 `.facm-old`，live destination 在备份阶段完全不变。
   - 已完整 SHA-256 校验的 staging 只通过同目录 `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)` 做最终交换；destination 原本不存在时也使用同一 atomic primitive。
2. **rollback 原子化**
   - 删除 `File.Copy(backup, destination, true)` 流式覆盖路径。
   - `.facm-old` 通过同一 `MoveFileEx` primitive 原子替换 destination；中断点只可能留下完整 candidate 或完整 rollback。
3. **built helper `--self-test`**
   - 验证备份阶段不改变 live destination、atomic swap 得到完整 candidate、atomic rollback 恢复完整旧版、实际 fallback 保留完整 backup。
   - Foundation 在 updater payload build 后直接执行编译出的 helper self-test。
4. **source contract**
   - 强制 MoveFileEx / REPLACE_EXISTING / WRITE_THROUGH / atomic helper / self-test。
   - 禁止重新出现 `File.Copy(staging, destination, true)` 与 `File.Copy(backup, destination, true)`。
   - 强制 workflow 实际执行 built helper self-test。

Foundation #627 / run `33230658026`：**SUCCESS**。

- Built `FACM.Updater.exe --self-test` SUCCESS。
- Architecture / Shell / Desktop / Cleanup / Repair / Personalization / 全部 League / 全部 Mayhem / P6 Maintenance / P6-P7 Updater / P7 Settings / P7 lifecycle / Diagnostics / DPI+Accessibility / Recovery / Release Evidence / Cutover Guard / real-machine collector 全部 SUCCESS。
- Windows PowerShell 5.1 evidence collector self-test SUCCESS。
- Restore + Release build SUCCESS。
- deterministic FoundationSmoke SUCCESS。
- deterministic WindowsSmoke SUCCESS。
- WinUI x64 self-contained single-file publish + verification SUCCESS。
- artifact `facm4-x64` id `9708400694`，GitHub digest `sha256:8a0029416182c55b7d16351b9b91b5ea16f490cb5ee950b7c2b7765de3229b5c`。
- Gate13 `update.interrupted-replacement-rollback` **仍保持 Blocked**：Hosted self-test 只证明事务 primitive，不替代真实 Windows 受控中断证据。

### 当前 Batch K — 20-50 次级稳定性压力 smoke 进行中

本批不做空循环，全部复用现有真实 service/runtime：

1. **Settings2 并发事务**
   - 已有 `Settings2Smoke.ConcurrentNarrowMutationsPreserveUnrelatedFieldsAsync` 连续 **40 轮**，每轮并发 Theme / F 坐标 / League setting 三路 atomic `UpdateAsync`，并在每轮后回读验证无 lost update。
   - 本批保留为跨功能 settings 压力基线。

2. **Single-instance 生命周期压力**
   - `MaintenanceWindowsSmoke` 将正常 primary -> secondary signal -> primary dispose -> replacement primary 的完整周期执行 **24 轮**。
   - 每轮要求 secondary 只触发一次 callback，primary dispose 后 mutex 可立即被下一实例取得。

3. **Updater UAC cancel 压力**
   - 同一受控 package/launcher 连续 **24 次**模拟 Win32 1223 UAC 取消。
   - 每次必须返回 false、保持当前 FACM 可继续运行，且不能误走成功 Process.Start 路径。

4. **PetHost bundle/cache 压力**
   - 首次受控 extraction + cache hit 后，再连续 **24 次** `PrepareAsync()`。
   - 所有重复调用必须保持同一 SHA/executable path 且 `openCount` 恒为 1，防止重复 rehash/re-extract 或 process cache 失效。

5. **League 推荐自动应用周期压力**
   - 连续 **24 个** Lobby -> ChampSelect stabilizing -> stable apply -> repeated observation 周期。
   - 每周期只能写一次 loadout + item set；重复稳定 observation 必须保持 `already-attempted`，进入新周期后才释放上一 fingerprint。

6. **League 热键配置事务压力**
   - 连续 **30 轮**有效 ExitGame / CloseLobby hotkey 组合更新。
   - 每轮必须 registration 成功、runtime state 与 Settings2 持久化一致；最终要求 30 次持久化、31 次 apply（含初始化）。

边界：这些自动压力 smoke 能覆盖事务、lifecycle、cache、at-most-once 和 UAC-cancel 语义；它们仍不能替代 WinUI 真机上的视觉/输入/DPI/辅助功能与真实 updater kill 证据。

下一检查点：提交 Batch K -> Foundation。若全绿，则稳定性审查的自动层进入收口：更新 `PROJECT_STATE.md` / `FACM4-P7-PARITY-CLOSEOUT.md`，再决定是否生成**唯一一次**新的统一真机候选；Gate13/cutover 仍独立保持阻塞。

## 当前 Gate13 边界

Canonical release evidence 仍保持：

```text
22 required / 12 Passed / 10 Blocked
ReleaseReady=false
CUTOVER BLOCKED
```

10 个真实 evidence blocker：

1. non-admin + real UAC cancel；
2. Defender / SmartScreen；
3. Windows 10 1809；
4. Windows 10 22H2；
5. controlled real-user Windows 11；
6. real mixed-DPI / multi-monitor；
7. real accessibility；
8. real FACM 3.5.15 -> 4.0 Settings2 migration；
9. interrupted updater replacement / rollback；
10. final signature / package identity。

这些项目在真实 evidence 完成前保持阻塞，不得因为当前稳定性审查或 CI 绿而自动降级/关闭。

## 下一批更新规则

每完成一个可描述的 batch：

- 在本文件追加 commit/head、发现、修复、CI 结果、仍未解决项；
- 如果状态跨越里程碑（例如稳定性审查结束、产生唯一候选、统一真机验收通过），同时更新 `docs/PROJECT_STATE.md` 与 `docs/FACM4-P7-PARITY-CLOSEOUT.md`；
- 任何失败必须写清楚是产品 defect、测试/gate contract defect，还是环境/基础设施问题，不能用“CI 红/绿”代替根因判断。
