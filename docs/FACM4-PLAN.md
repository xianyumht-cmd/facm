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

已完成：

- 找到跨功能 Settings2 lost-update 根因：多个模块使用 `Load whole document -> 修改一个字段 -> Save whole document`，并发时后写可能覆盖主题、桌宠、F 坐标、League 开关、维护设置等其它模块刚写入的值。
- 新增 atomic narrow Settings2 mutation contract：`Settings2UpdateResult` / `IAtomicSettings2Repository` / `UpdateAsync`，Recovering repository 串行执行 load-mutate-save-LKG transaction。
- 多个 feature writer 开始迁移到 `UpdateAsync`。
- PetHost 增加 ready 等待上限，避免已连接 pipe 后永久 Busy。
- 修复 async finally 与 semaphore Dispose 的生命周期竞态。
- process-scoped PetHost / League 自动化增加显式正常退出释放。
- League global hotkey 在“注册成功但 Settings 持久化失败”时回滚上一组注册。
- Settings2 smoke 增加并发窄更新压力场景。

CI：Foundation #618 被 Architecture gate 拦下；根因是大小写不敏感 `File\.` regex 误命中注释中的 `settings file.`，不是 ViewModel 真正越界。未放宽架构 gate，只修正注释。

### Batch B — `05ab40708536d4b8e12ae6fdadb90de8a59219c8`

已完成：

- 个性化 / Cleanup UI async-void 失败增加最终 containment，避免未观察异常直接穿透 UI dispatcher。
- 修复 #618 的注释误报，不改变架构规则。

CI：Foundation #619 Architecture SUCCESS，随后 Cleanup gate 失败，因为旧 gate 仍硬编码要求 `SaveAsync`。判定为 gate contract 落后于新的 atomic mutation contract。

### Batch C — `0c4423d89732e77a8bd67456cefa8ac210e998b5`

已完成：

- Cleanup gate 改为必须走 atomic `UpdateAsync`，并禁止 feature 层重新出现 whole-document `SaveAsync`。
- 继续补 Cleanup / Maintenance / UI async failure boundary。

CI：Foundation #620 的 Architecture / Shell / Desktop / Cleanup / Repair 均 SUCCESS；Personalization gate 随后因仍要求旧 `SaveAsync` 失败。再次判定为旧 gate contract。

### Batch D — `9d7a162788c5a33e2473c070bd040968938d6c6f`

已完成：

- Personalization gate 升级为 `UpdateAsync` contract，并禁止直接 `SaveAsync`。
- F 拖动坐标持久化改为 atomic narrow `UpdateAsync`，recovery 模式保持 read-only。
- P7 Settings parity gate 升级：主要 feature settings writers 不允许绕过 atomic mutation boundary。
- Foundation Settings smoke 强制包含 concurrent narrow mutations 与 recovery read-only 场景。

CI：Foundation #621 / run `33224469293`：Architecture SUCCESS、Shell SUCCESS；Desktop gate 失败。实际 F 代码已经使用 `settings.UpdateAsync(... allowRecoveryRebuild:false)` 并检查 `updated.Persisted`，失败来自 Desktop gate 仍要求旧 `settings.SaveAsync`，不是 F persistence 回归。

### 当前 Batch E — 进行中

本批处理：

- Desktop source gate 跟随真实新 contract：要求 F 坐标通过 atomic `UpdateAsync`、`allowRecoveryRebuild:false`、`updated.Persisted` 和 recovery-not-persisted 分支；禁止重新出现 `settings.SaveAsync`。
- League Efficiency source gate 提前清理同类旧假设：要求 `_settings.UpdateAsync` + recovery read-only + persistence failure rollback；禁止 `_settings.SaveAsync`。
- 本文件从本批起成为 4.0 实时计划账；每个后续 batch 与代码/CI 结论同批更新。

下一检查点：跑 Foundation 到更深的 gate；同时继续审查 MainWindow/League 后台刷新关闭竞态、Maintenance async handlers、single-instance/shutdown、PetHost/Updater 失败路径，并继续补重复操作压力 smoke。

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
