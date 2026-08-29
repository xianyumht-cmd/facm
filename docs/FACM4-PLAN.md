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
- 新增 `Settings2UpdateResult` / `IAtomicSettings2Repository` / `UpdateAsync`，Recovering repository 串行执行 load-mutate-save-LKG transaction。
- 多个 feature writer 迁移到 `UpdateAsync`。
- PetHost 增加 ready 等待上限；修复 semaphore/async teardown 竞态；process-scoped PetHost/League 自动化增加显式释放。
- League global hotkey 持久化失败时回滚上一组注册。
- Settings2 smoke 增加并发窄更新压力场景。
- Foundation #618：Architecture gate 因注释 `settings file.` 被大小写不敏感 `File\.` regex 误命中；不是实际架构越界。

### Batch B — `05ab40708536d4b8e12ae6fdadb90de8a59219c8`

- 个性化 / Cleanup UI async-void 失败增加最终 containment。
- 修复 #618 注释误报，不放宽架构 gate。
- Foundation #619：Architecture SUCCESS；Cleanup gate 仍要求旧 `SaveAsync`，判定为 stale gate contract。

### Batch C — `0c4423d89732e77a8bd67456cefa8ac210e998b5`

- Cleanup gate 改为必须走 atomic `UpdateAsync`，并禁止 feature 层 whole-document `SaveAsync`。
- 继续补 Cleanup / Maintenance / UI async failure boundary。
- Foundation #620：Architecture / Shell / Desktop / Cleanup / Repair SUCCESS；Personalization gate 因仍要求旧 `SaveAsync` 失败。

### Batch D — `9d7a162788c5a33e2473c070bd040968938d6c6f`

- Personalization gate 升级为 `UpdateAsync` contract，并禁止直接 `SaveAsync`。
- F 拖动坐标持久化改为 atomic narrow `UpdateAsync`，recovery 模式保持 read-only。
- P7 Settings parity gate 升级：主要 feature settings writers 不允许绕过 atomic mutation boundary。
- Foundation Settings smoke 强制 concurrent narrow mutations + recovery read-only。
- Foundation #621 / run `33224469293`：Architecture/Shell SUCCESS；Desktop gate 仍要求旧 `settings.SaveAsync`，不是 F persistence 回归。

### Batch E — `b5c47def7ca8ae4f9570fcb5de0341eaf355548a`

- Desktop source gate 改为要求 F 坐标 atomic `UpdateAsync` + `allowRecoveryRebuild:false` + `updated.Persisted` + recovery-not-persisted 分支；禁止 `settings.SaveAsync`。
- League Efficiency gate 提前改为 `_settings.UpdateAsync` + recovery read-only + persistence-failure rollback；禁止 `_settings.SaveAsync`。
- 建立本文件作为每批必更的 4.0 实时计划账。
- Foundation #622 / run `33226878284`：Architecture / Shell / Desktop / Cleanup / Repair / Personalization 全部 SUCCESS；League Workbench gate 因 PostGame recovery-origin 语义仍绑定旧写法而停。

### Batch F — `856078e9f90cc4e13ee7bd09e7b0e09a7d57164a`

- PostGame / Recommended automation settings 保持 atomic `UpdateAsync`，显式以 `RecoveredLastKnownGood / RecoveryDefaults` + `!Persisted` 标记 recovery read-only。
- caller/lifetime cancellation 统一按 linked token 识别，避免正常取消落入 generic failure。
- Foundation #623 / run `33227071835`：Architecture、Shell、Desktop、Cleanup、Repair、Personalization、League Workbench、League Recommended、League Efficiency、League Bench Quick Pick、全部 Mayhem、P6 Maintenance、P6 Updater、P7 Settings parity 全部 SUCCESS；仅 P7 lifecycle gate 因旧 `_desktopPetRuntime?.Dispose()` 字面量检查停止。
- 复核确认桌宠实际 teardown 已封装进 `DisposePersonalizationRuntime()`，#623 属 stale lifecycle gate，不是 PetHost teardown 回归。

### Batch G — `cd8f3051780d4af1552cd06c91f050c871b3581e`

已完成的产品修复：

1. **Maintenance 初始化可重试**
   - 只有 preferences 成功加载后才 `_initialized=true`；取消/异常保持 false。
   - 新增 `initialization-failed` 状态。
   - `MaintenanceSettingsControl` 改为 fail-soft `RetryInitialization()`；每次进入“更多设置”显式触发重试，不再依赖 visual-tree 的二次 `Loaded`。
2. **更新下载/安装 teardown 安全**
   - linked download CTS 由正在执行的 async 操作持有，在 `finally` 释放；shutdown 只 cancel。
   - installer 增加 active-operation 计数；下载/replacement 未退出时延后 installer Dispose，避免销毁正在 await 的资源。
3. **Maintenance async-void containment**
   - Toggle / Check / Download / Install / Announcement / OpenLog handler 全部有最终异常边界。
   - install dialog 返回后重检当前 VM 与 PreparedUpdate。
4. **P7 centralized lifecycle gate**
   - 改为验证 `DisposePersonalizationRuntime()`、state hook 解绑、`runtime?.Dispose()`、引用清空和 `DisposeMaintenanceRuntime()` 的显式调用，不再绑定旧直接 Dispose 写法。
5. **Maintenance source regression gate**
   - 强制 retryable initialization、CTS ownership、deferred installer teardown、navigation retry 与 async-void containment。

Foundation #624 / run `33227351798`：在 **Architecture gate** 立刻失败。根因不是代码越界，而是 `MaintenanceViewModel` 注释中的英文 `same process.` 被大小写不敏感 `Process\.` 规则误识别为平台 API。与 #618 同类；不修改/放宽架构 gate，只改注释措辞。

### 当前 Batch H — 进行中

- 将 `same process.` 注释改为 `same app session`，消除 #624 假阳性，不改变任何运行逻辑。
- 继续审查 League caller cancellation：`LeagueWorkbenchViewModel` 的 Refresh/Advisor/ItemSet 路径目前只对 lifetime cancellation 特判，显式 caller cancellation 可能被 generic catch 写成 `refresh-failed / advisor-refresh-failed / prepare-failed / apply-failed`；列为下一批产品修复。
- 继续审查 League `ContentDialog.ShowAsync()`：确认推荐配置/装备集的部分 dialog await 位于 async-void handler 的最终异常边界之外，窗口关闭/XamlRoot teardown 期间存在 dispatcher 未观察异常风险；列为下一批修复。
- updater interruption 审查：现有 `MaintenanceWindowsSmoke` 覆盖 updater helper 的受控路径、参数、UAC cancel、包目录约束，但**没有模拟 helper 在 replacement 中途被终止**。Updater 本身有 `.facm-old` backup、hash mismatch rollback、5 秒 early-exit rollback；若 helper 自身在 replace 后到 restart/cleanup 之间被强制终止，当前 deterministic smoke 没有 resume/rollback 证据。保持 Gate13 `update.interrupted-replacement-rollback` 阻塞，后续作为稳定性审查的独立事务恢复任务处理，不能仅靠现有 updater green gate 宣称关闭。

下一检查点：提交 Batch H，跑 Foundation；若 source/build 继续向后推进，则先修 League cancellation/dialog teardown，再设计 updater interruption transaction/resume smoke。所有结论继续同步本文件。

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
