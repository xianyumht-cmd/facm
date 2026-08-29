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

- 同一 head `bb9f8e88d4ed868adf602c2ae87f64663379496e` 上，所有 source gates SUCCESS。
- Release build SUCCESS。
- deterministic FoundationSmoke SUCCESS。
- deterministic WindowsSmoke SUCCESS。
- WinUI x64 self-contained single-file publish SUCCESS。
- publish output verification SUCCESS。
- `facm4-x64` artifact upload SUCCESS。
- 这是本轮稳定性审查第一次完整穿透 source gates -> Release build -> 两层 smoke -> publish -> artifact 的全绿 head。
- 仍不把它作为最终真机候选：Updater interruption 风险和后续 20-50 次压力批次还没闭环。

### 当前 Batch J — Updater interruption hardening 进行中

已确认的真实风险：

- `File.Replace(staging, destination, backup, true)` 主路径本身具备同卷替换语义，但旧 `FallbackReplace` 在 File.Replace 不可用/IOException 时会执行 `File.Copy(staging, destination, true)`，直接流式覆盖正式 FACM EXE。
- updater helper 如果恰好在该覆盖写期间被终止，正式 EXE 存在被截断/半写的窗口；虽然 `.facm-old` 可能存在，但损坏的新 EXE 本身可能无法再次启动去触发恢复。
- 旧 `TryRollback` 也用 `File.Copy(backup, destination, true)` 流式覆盖正式 EXE，rollback 自身同样存在中断窗口。

本批修复：

1. **正式 EXE 不再被 fallback 流式覆盖**
   - 保留 `File.Replace` 为主路径。
   - fallback 先把完整旧 EXE 复制到 `.facm-old`；在备份阶段若 helper 被终止，正式 EXE 仍完全未变。
   - 已完整 SHA-256 校验的 staging 只通过同目录 `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)` 做最终目标交换。
   - destination 原本不存在时也使用同一 atomic move primitive。

2. **rollback 改成原子交换完整 backup**
   - 不再 `File.Copy(backup, destination, true)`。
   - `.facm-old` 直接通过同一个 `MoveFileEx` primitive 替换 destination；中断前后目标只会是完整 candidate 或完整 rollback，不存在流式半写阶段。

3. **Updater helper 自身加入 `--self-test`**
   - 在临时目录验证：备份准备不改变 live destination；atomic swap 后 destination 等于完整 candidate；atomic rollback 后恢复完整旧版；实际 `FallbackReplace` 同时保留完整 backup。
   - Foundation 在“Prepare controlled updater payload”阶段直接执行编译出的 `FACM.Updater.exe --self-test`，失败则立即阻止后续 build/publish。

4. **Updater source gate 加强**
   - 强制 `MoveFileEx` / `REPLACE_EXISTING` / `WRITE_THROUGH` / atomic helper / self-test contract。
   - 明确禁止重新出现 `File.Copy(staging, destination, true)` 和 `File.Copy(backup, destination, true)`。
   - 强制 Foundation workflow 实际执行 built-helper self-test，而不是只做源码字符串检查。

边界：这批只能证明 updater 的 deterministic transaction primitive 和 CI 自检，不会自动关闭 Gate13 `update.interrupted-replacement-rollback`。该 blocker 仍需要真实 Windows 上的人为/受控中断证据。

下一检查点：提交 Batch J -> Foundation；若全绿，同步 Batch J commit/run/artifact，并进入 20-50 次重复操作压力批次。若红，按产品 defect / gate defect / environment 三类继续定位，结果继续同批写本文件。

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
