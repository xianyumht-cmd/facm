# FACM 4.0 执行计划与实时进度

Status: **AUTOMATED-STABILITY-GREEN / REAL-MACHINE-VALIDATION-NEXT**
Production baseline: **FACM 3.5.15（保持不变）**
Active line: `feat/facm4-function-parity-p7-closeout` / PR #234 / Issue #233
Canonical main: `269da6c751a8463542ed0d172300675deff9571e`
Latest verified code head: `f3906b84dd0076411dcd8a4fd82610d1d6c2a179`
Latest full Foundation: **#628 / run `33230830272` = SUCCESS**

> 本文件是 FACM 4.0 当前工作的实时计划账。每完成一批代码审查、修复或 CI 结论，都必须在同一批次同步更新这里。`docs/PROJECT_STATE.md` 与 `docs/FACM4-P7-PARITY-CLOSEOUT.md` 在里程碑状态变化时做 canonical reconciliation。

## 当前结论

FACM 4.0 P7 的**自动化稳定性审查层已经收口**：代码级故障审查、根因修复、source gates、Release build、FoundationSmoke、WindowsSmoke、Updater built-helper self-test、20-50 次级重复操作压力 smoke、single-file publish 与 artifact verification 已在同一个代码 head 上全部通过。

这不等于 Gate13/release-ready。生产仍是 3.5.15，P2-P7 仍 Draft/未合并，4.0 production pointer、release、cutover、legacy retirement 均保持冻结。

下一工程阶段只做一件事：用最新统一候选进行**一次完整、非破坏优先的真实 Windows 功能验收**。UI 2.0 继续排在功能等价验收之后。

## 7 步执行顺序

1. **全面代码级故障审查** — COMPLETED
2. **在现有 4.0 架构内批量修复** — COMPLETED
3. **完整 FACM 3.5.15 parity 复核** — COMPLETED on code/source gates
4. **自动压力与重复操作 smoke** — COMPLETED on `f3906b84...`
5. **完整 Foundation** — COMPLETED, #628 SUCCESS
6. **只生成一个新的统一候选** — READY from #628 artifact
7. **统一真机功能验收** — NEXT / real-machine evidence pending

## 稳定性审查批次

### Win10 启动与个性化真机根因

- Win10 `E_ACCESSDENIED` 根因：运行时尝试修改 WinUI 平台拥有的系统 brush。已改成 FACM 自有可变 semantic brush；平台资源只读取，不写入。
- 个性化控件曾永久灰掉：桌宠异步初始化把 VM 置 Busy，MainWindow 手工同步 `IsEnabled` 后没有在异步完成时刷新。已在 UI owner/Dispatcher 上补完成刷新。
- 后续 Win10 evidence 已看到应用进入 `Running`、failure count 0，并成功持久化 `mono-emerald`、`greenfly`、pet enabled 与 F 坐标。该证据只关闭对应窄根因，不自动关闭整个 `compat.windows-10-22h2` Gate13 项。

### Batch A-D — Settings2 atomic mutation 与基础生命周期

- 找到跨功能 Settings2 lost-update 根因：feature writer 使用 `Load whole document -> 修改局部 -> Save whole document`，并发时会互相覆盖。
- 建立 atomic narrow `UpdateAsync` transaction boundary，Recovering repository 串行 load/mutate/save/LKG。
- Cleanup、Personalization、F 坐标及主要 feature settings writer 迁移到 atomic update；recovery 默认保持 read-only。
- 修复 PetHost ready timeout / async teardown、League hotkey persistence rollback、多个 UI async-void containment。
- source gates 从旧 `SaveAsync` contract 逐项升级为 atomic mutation contract，不用 stale gate 逼代码回退。

关键 heads：
- A `aca8aeb956a723fd0b48f77b89b747aa1cb3abd7`
- B `05ab40708536d4b8e12ae6fdadb90de8a59219c8`
- C `0c4423d89732e77a8bd67456cefa8ac210e998b5`
- D `9d7a162788c5a33e2473c070bd040968938d6c6f`

### Batch E-F — Desktop / League settings contract

- Desktop F persistence gate、League Efficiency gate 对齐 atomic update。
- PostGame / Recommended settings 显式保留 `RecoveredLastKnownGood / RecoveryDefaults` recovery-origin 语义。
- caller/lifetime cancellation 开始统一按 linked token 判断。

Heads：
- E `b5c47def7ca8ae4f9570fcb5de0341eaf355548a`
- F `856078e9f90cc4e13ee7bd09e7b0e09a7d57164a`

### Batch G — Maintenance 真缺陷修复

Head `cd8f3051780d4af1552cd06c91f050c871b3581e`

- 初始化失败不再永久 latch `IsInitialized=true`；同一 app session 可以重试。
- More Settings 每次重新进入可 `RetryInitialization()`，不依赖 visual-tree 二次 `Loaded`。
- update download linked CTS 由 active async operation 持有并在 finally 释放；shutdown 只 cancel。
- installer 增加 active-operation 计数，下载/replacement 未退出时延后 Dispose。
- Maintenance async-void handlers 全部保留最终异常 containment。
- P7 centralized personalization/League/maintenance teardown contract 固化到 gate。

### Batch H-I — 编译、League cancellation/dialog teardown

Heads：
- H `84bf4d97589d90b578e8fdc6526691556f8741d5`
- I `bb9f8e88d4ed868adf602c2ae87f64663379496e`

- 清除 architecture regex 对英文注释的假阳性，不放宽架构门禁。
- Foundation #625 首次穿过全部 source gates 后发现真实 C# 编译错误：`finally` 内 `return` (`CS0157`)；已修复。
- League Refresh / Advisor / ItemSet / automation settings 全部按 linked token 识别 caller/lifetime cancellation；正常取消不再伪装成 provider failure。
- League `ContentDialog.ShowAsync()` 与 async-void handler 增加 Window/XamlRoot teardown containment；`ContentDialogResult.Primary` 写入确认门槛保持不变。
- Foundation #626：同一 Batch I head 上 source gates -> Release build -> FoundationSmoke -> WindowsSmoke -> publish -> artifact 首次全链路 SUCCESS。

### Batch J — Updater interrupted-replacement hardening

Head `4755c40c6c3ec751d27bf9cab31d74581f58f3d3`

已确认旧 fallback 风险：`File.Copy(staging, destination, true)` 会流式覆盖正式 EXE，helper 如果在写入中被终止，可能留下半文件；旧 rollback 同样使用流式覆盖。

修复：

- `File.Replace` 继续作为主路径。
- fallback 先保存完整 `.facm-old`，live destination 在备份期间不变。
- 已完成 SHA-256 校验的 staging 只通过同目录 `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)` 做最终交换。
- rollback 也使用同一 atomic move primitive，不再流式覆盖正式 EXE。
- `FACM.Updater.exe --self-test` 验证 backup-before-swap、atomic candidate swap、atomic rollback、fallback backup 完整性。
- Foundation workflow 实际执行 built helper self-test；source gate 禁止重新出现两个 live-EXE stream-copy 路径。

Foundation #627 / run `33230658026` = SUCCESS；artifact `9708400694`。Gate13 的真实 interrupted-update evidence 仍保持 Blocked，因为 hosted deterministic self-test 不能代替真实 Windows 受控终止。

### Batch K — 20-50 次级重复操作压力

Head `f3906b84dd0076411dcd8a4fd82610d1d6c2a179`

实际执行的压力项：

- **Settings2：40 轮**；每轮并发 Theme / F 坐标 / League setting 三路 atomic mutation，再回读验证无 lost update。
- **Single-instance：24 轮**；primary -> secondary signal -> exactly-one callback -> primary dispose -> replacement primary。
- **Updater UAC cancel：24 轮**；每轮 Win32 1223 都必须返回 false，不误走成功 Process.Start。
- **PetHost bundle/cache：24 轮**重复 `PrepareAsync()`；SHA/path 不变，embedded bundle `openCount` 恒为 1。
- **League Recommended：24 个** Lobby -> ChampSelect stabilize -> apply -> repeated observation 周期；每周期写入恰好一次，重复 observation 必须 `already-attempted`。
- **League Efficiency：30 轮** hotkey configuration transaction；runtime 与 persisted settings 每轮一致，最终 30 次 persist / 31 次 register apply（含初始化）。

Foundation #628 / run `33230830272`：**SUCCESS**。

同一代码 head 上通过：

- built PetHost self-test；
- built Updater atomic self-test；
- 全部 P1-P7/source/product gates；
- PowerShell 5.1 real-machine collector self-test；
- Release restore/build；
- deterministic FoundationSmoke（含上述 Settings/League 压力）；
- deterministic WindowsSmoke（含 single-instance/UAC/PetHost 压力）；
- WinUI x64 self-contained single-file publish；
- publish output verification；
- artifact upload。

最新统一候选 artifact：

```text
artifact: facm4-x64
artifact id: 9708452498
artifact ZIP bytes: 165,704,298
GitHub artifact digest: sha256:dcc5b93ae48508d73ce44e90f4f6600047090acddfef876e0a6d38cee0d92888
code head: f3906b84dd0076411dcd8a4fd82610d1d6c2a179
Foundation: #628 / 33230830272
```

### Milestone L — canonical docs reconciliation（本批）

- `docs/FACM4-PLAN.md`：状态从稳定性审查进行中改为 automated-stability-green / real-machine-next。
- `docs/PROJECT_STATE.md`：移除旧 `3956/#595/artifact 9695331632` 作为当前候选的描述，改指向 `f390/#628/artifact 9708452498`。
- `docs/FACM4-P7-PARITY-CLOSEOUT.md`：补齐 Win10 brush、个性化 stale Busy、atomic Settings2、Maintenance lifecycle、League cancel/dialog、Updater atomic fallback、压力 smoke 等本轮稳定性结果。
- Issue #233 将记录同样的里程碑结论。
- 本批只改文档/追踪状态；不改变 production pointer、release、cutover 或 stacked PR 合并状态。

## 下一步：统一真机功能验收

使用 #628 的统一候选做一轮完整非破坏验收，优先顺序：

1. 冷启动 launcher-first F；F 拖动/位置持久化；compact/detailed shell 生命周期。
2. Cleanup preview/review/cancel；真实 UAC 点“否/取消”后当前实例继续存活。
3. Repair 四项真实入口只做安全可逆/只读部分，破坏性动作另行授权。
4. Personalization：主题视觉实际变化、greenfly/VPet 实际出现与移动、F restore/reset。
5. 真实 LOL：Dashboard / Player / Live / Mayhem 读取链；需要写入的推荐/自动化按明确测试边界执行。
6. Settings：auto-update toggle/manual check/announcement/log/diagnostics；不执行真实 replacement。
7. 二次启动只 signal 现有实例；正常退出后 PetHost/League/hotkeys/maintenance runtime 清理。
8. 收集 Settings2/recovery/event JSONL 作为统一 evidence。

真实 updater kill、真实删除、production pointer、release publication、legacy retirement 仍不属于这一轮默认授权范围。

## Gate13 边界保持不变

Canonical release evidence：

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

自动稳定性全绿不会自动关闭任何仍需真实 evidence 的 blocker。

## 每批更新规则

每完成一个可描述的 batch：

- 同批更新本文件的 head、发现、修复、CI 结果、仍未解决项；
- 状态跨越里程碑时同步更新 `docs/PROJECT_STATE.md` 与 `docs/FACM4-P7-PARITY-CLOSEOUT.md`；
- 失败必须写清楚是产品 defect、test/gate contract defect，还是 environment/infrastructure；
- 不用 CI 绿冒充真实机器、签名或 production-ready。
