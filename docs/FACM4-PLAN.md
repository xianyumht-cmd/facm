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

已完成：

- `LeaguePostGameAutomationSettingsViewModel` 保持 atomic `UpdateAsync`，并显式以 `RecoveredLastKnownGood / RecoveryDefaults` + `!Persisted` 标记 recovery read-only。
- `LeagueRecommendedAutoApplySettingsViewModel` 同样补齐显式 recovery-origin 语义。
- caller/lifetime cancellation 统一按 linked token 识别，避免正常取消落入 generic failure。

Foundation #623 / run `33227071835`：

- Architecture SUCCESS
- Shell SUCCESS
- Desktop SUCCESS
- Cleanup SUCCESS
- Repair SUCCESS
- Personalization SUCCESS
- League Workbench SUCCESS
- League Recommended SUCCESS
- League Efficiency SUCCESS
- League Bench Quick Pick SUCCESS
- 全部 Mayhem source gates SUCCESS
- P6 Maintenance SUCCESS
- P6 Updater SUCCESS
- P7 Settings parity SUCCESS
- 仅在 **P7 entry/lifecycle closeout** 停止：旧 gate 硬编码要求 `_desktopPetRuntime?.Dispose()`。

根因复核：当前桌宠 teardown 已封装进 `DisposePersonalizationRuntime()`：先解绑 floating/state hooks，再对捕获的 `runtime?.Dispose()`，然后清空 `_desktopPetRuntime/_desktopPetPreferences/_petHostBundleStore`；`DisposeMaintenanceRuntime()` 正常退出时又显式调用 `DisposePersonalizationRuntime()`。因此 #623 是 stale lifecycle gate，不是 PetHost teardown 回归。

### 当前 Batch G — 进行中

本批已确认的产品缺陷与修复：

1. **Maintenance 初始化失败会永久锁死重试**
   - 旧 `InitializeAsync` 在 `finally` 无论成功失败都 `_initialized=true`。
   - 改为只有 preferences 成功加载后才标记 initialized；取消/异常保持 false，并给出 `initialization-failed` 状态。
   - `MaintenanceSettingsControl` 不再依赖一次性 Loaded latch；增加 fail-soft `RetryInitialization()`。
   - 每次重新进入“更多设置”都会触发重试，因此短暂 Settings/磁盘/provider 故障不需要重启 FACM。

2. **更新下载/安装 teardown 资源竞态**
   - 活跃下载使用的 linked CTS 改由该 async 操作自己持有并在 `finally` 释放；App shutdown 只 cancel，不在 await 尚未退出时提前 Dispose CTS。
   - installer 操作增加 active-operation 计数；窗口/F 关闭触发 shutdown 时，若下载或 replacement 仍在执行，installer 的 Dispose 延后到最后一个操作 `finally`，避免把底层 Http/launcher 资源从正在执行的 await 下面销毁。

3. **Maintenance async-void containment**
   - Toggle / Check / Download / Install / Announcement / OpenLog UI handler 全部保留 final catch/finally，不让 provider/launcher 异常穿透 dispatcher。
   - 安装确认 dialog 返回后重新检查当前 VM/PreparedUpdate，避免 dialog 等待期间窗口状态变化后继续使用陈旧状态。

4. **P7 lifecycle gate 更新**
   - 不再要求旧的 `_desktopPetRuntime?.Dispose()` 字面量。
   - 改为验证 centralized `DisposePersonalizationRuntime()`、状态 hook 解绑、`runtime?.Dispose()`、引用清空，以及 `DisposeMaintenanceRuntime()` 对 personalization/League product teardown 的显式调用。
   - 这是加强当前真实 lifecycle contract，不是放宽 gate。

5. **Maintenance source regression gate 加强**
   - 强制初始化成功后才 latch `IsInitialized`。
   - 强制下载 CTS ownership/finally dispose。
   - 强制 installer active-operation deferred disposal。
   - 强制 More Settings navigation retry 和 async-void containment。

下一检查点：提交 Batch G，跑 Foundation 进入 Diagnostics / DPI / Recovery / Release Evidence / Gate13 guard / build / smokes / publish；同时继续审查 League caller cancellation、ContentDialog close race、updater interrupted rollback 和重复开关窗口的压力 smoke。

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
