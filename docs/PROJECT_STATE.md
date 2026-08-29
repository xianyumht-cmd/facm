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

> **生产仍是 FACM 3.5.15。** FACM 4.0 当前已完成代码侧功能等价与自动稳定性收口，但不存在 4.0 production cutover 授权。没有完整 release evidence READY + fresh production/destructive authorization，不得修改 `online/version.json` / `release/request.json`、发布 4.0.0、退休 legacy、deploy/restart 或删除历史分支/tag。

## 当前 canonical / active line

- canonical `main`：`269da6c751a8463542ed0d172300675deff9571e`，Merge PR #221。
- #218 已修复 Win10 22H2 `TabViewButtonBackground` / `XamlParseException` 启动故障并合入。
- #221 已完成 FACM 3.5 launcher-first / F / compact launcher 行为迁移并通过对应 Win10 真机验证后合入。
- 功能迁移继续采用 stacked P2-P7；**全部保持 Draft / 未合并到 main**。

| 阶段 | PR | Head | 状态 |
| --- | --- | --- | --- |
| P2 Cleanup | #223 | `6bf8956b61683c734b236fd8a38a539168e57918` | code-green / Draft |
| P3 Repair | #226 | `684dc94ee0beb02569a39e6fb5be19c5b1f8b359` | code-green / Draft |
| P4 Personalization | #228 | `2f1efa396cd9add76c96cdf38dee82fac7a16de7` | code-green / Draft |
| P5 League Workbench | #230 | `e3bac2e779e00051b51005e5b715196602c4982f` | code-green / Draft |
| P6 Settings / Maintenance | #232 | `d3801a0fa4276e74514a59a6c673c4cc4efbaff8` | code-green / Draft |
| P7 Unified parity closeout | #234 | code head `f3906b84dd0076411dcd8a4fd82610d1d6c2a179` | **AUTOMATED-STABILITY-GREEN / Draft** |

P7 canonical-doc reconciliation head：`b5f895cdbb30f32d834a7b697a0505548f858da1`。
Tracking Issue：#233。

## FACM 4.0 当前里程碑：自动稳定性层已收口

P7 在原功能等价收口之后又完成了一轮实际故障审查，而不是直接拿旧候选继续测。主要结果：

- **Settings2 lost-update 根因修复**：跨模块配置写入统一走 atomic narrow `UpdateAsync`，同一 repository transaction 内 load/mutate/save/LKG；recovery 默认只读。
- **Win10 主题启动根因修复**：FACM 不再修改 WinUI 平台拥有的系统 brush，运行时只修改 FACM 自有 semantic brush。
- **Personalization stale-disabled 修复**：桌宠异步初始化完成后显式回到 UI owner 刷新控件状态，不再留下永久灰色控件。
- **Maintenance lifecycle 修复**：初始化失败可同进程重试；下载 CTS 由 active operation 持有；installer 在活跃下载/replacement 结束后再 Dispose；async-void handlers 有最终 containment。
- **League lifecycle 修复**：caller/lifetime cancellation 不再误报 provider failure；ContentDialog/window-close teardown 竞态被 containment，同时保留 Primary-confirmation 写入门槛。
- **Updater interruption hardening**：fallback/rollback 不再用 `File.Copy(..., liveDestination, overwrite:true)` 流式覆盖正式 EXE；主路径保留 `File.Replace`，fallback/rollback 通过同目录 `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)` 原子交换完整 staging/backup；built helper 有实际 `--self-test`。
- **生命周期/事务压力**：Settings2 40 轮、single-instance 24 轮、UAC cancel 24 轮、PetHost cache 24 轮、League Recommended 24 周期、League Efficiency hotkey transaction 30 轮，均进入 deterministic smoke。

详细过程：`docs/FACM4-PLAN.md`。
详细 parity matrix：`docs/FACM4-P7-PARITY-CLOSEOUT.md`。

## 最新自动验收

Verified code head：`f3906b84dd0076411dcd8a4fd82610d1d6c2a179`。

FACM 4.0 Foundation **#628 / run `33230830272` = SUCCESS**。同一代码 head 已通过：

- controlled PetHost payload + self-test；
- controlled Updater payload + built helper atomic `--self-test`；
- P1-P7 全部 source/product gates；
- PowerShell 5.1 real-machine evidence collector self-test；
- Release x64 restore/build；
- deterministic FoundationSmoke，包括 Settings2 / League 重复压力；
- deterministic WindowsSmoke，包括 single-instance / UAC-cancel / PetHost cache 重复压力；
- WinUI x64 self-contained single-file publish；
- publish-output verification；
- artifact upload。

Canonical-doc reconciliation head `b5f895c...` 随后由 Foundation **#629 / run `33231064160` = SUCCESS** 再跑一次完整回归。#629 是 docs-only；不替代或重新定义 #628 的 code candidate。

## 最新统一候选与独立校验

GitHub artifact：

```text
artifact: facm4-x64
artifact id: 9708452498
artifact ZIP bytes: 165,704,298
GitHub artifact digest: sha256:dcc5b93ae48508d73ce44e90f4f6600047090acddfef876e0a6d38cee0d92888
code head: f3906b84dd0076411dcd8a4fd82610d1d6c2a179
Foundation: #628 / 33230830272
```

从 GitHub 下载 artifact 后独立重算：

```text
ZIP SHA-256: dcc5b93ae48508d73ce44e90f4f6600047090acddfef876e0a6d38cee0d92888
ZIP bytes: 165,704,298
FACM.App.exe bytes: 305,912,996
FACM.App.exe SHA-256: d397b862fbe7ed30fd43ee758e3b6966d56ae72dba13e4058a94a3c22a7f6994
ZIP DLL entries: 0
```

ZIP SHA 与 GitHub artifact digest 完全一致；candidate 保持单文件 EXE，没有旁路 DLL。

## 已有真实 Win10 窄证据

本轮稳定性审查前后已经拿到部分真实 Win10 evidence：

- 旧平台 brush `E_ACCESSDENIED` 启动链修复后，应用可进入 recovery state `Running`，candidate version `4.0.0.0`，consecutive failures 0。
- launcher -> compact -> main-shell 生命周期日志正常，无 failure event。
- Personalization 修复后 Settings2 LKG 实际出现 `themeId=mono-emerald`、`styleId=greenfly`、pet enabled=true、F position `1665,381`，证明控件不再永久 disabled 且用户意图到达 Settings2 persistence。

这些 evidence **不是整个 Win10 22H2 Gate13 项通过的替代品**：主题视觉是否真实变化、PetHost 是否实际出现/移动、完整功能矩阵、DPI/accessibility、migration 等仍按对应真实验收记录。

## 当前真实边界：REAL-MACHINE / GATE13

```text
22 required / 12 Passed / 10 Blocked
ReleaseReady=false
CUTOVER BLOCKED
```

仍需真实 evidence 的 10 项：

1. non-admin 启动 + real UAC cancel；
2. Defender / SmartScreen；
3. Windows 10 1809；
4. Windows 10 22H2；
5. controlled real-user Windows 11；
6. real mixed-DPI / multi-monitor；
7. keyboard-only / High Contrast / text scaling / basic screen reader；
8. real FACM 3.5.15 -> 4.0 Settings2 migration / relaunch / rollback；
9. interrupted updater replacement / rollback；
10. final signing / package identity verification。

Hosted CI、source gate、deterministic pressure smoke 或普通“继续”都不能自动把这些 evidence 改为 Passed。

## 下一步：一次统一真机功能验收

使用 **#628 / artifact `9708452498`**：

1. 冷启动 launcher-first F；F 拖动/持久化；compact launcher；详细 Shell。
2. Cleanup preview/review/cancel；真实 UAC 点“否/取消”后原实例继续存在。
3. Repair 四个入口的安全/可逆部分；真实删除/驱动级动作另行授权。
4. Personalization：主题视觉变化、F restore/reset、greenfly/VPet 实际启动/移动。
5. 真实 LOL：Dashboard / Player / Live / Mayhem 读取；需要写入的推荐/自动化按明确边界验证。
6. Settings：auto-update toggle、manual check、announcement、diagnostics/log entry；不执行真实 updater replacement。
7. 二次启动只 signal 已有实例；不产生第二个 resident FACM runtime。
8. 正常退出后 PetHost / League / hotkeys / maintenance runtime 有序释放。
9. 收集 Settings2 / recovery state / JSONL 统一 evidence。

真实 LOL 删除、真实 updater kill/replacement、production pointer 修改、release publication、legacy retirement 都不属于默认首轮授权。

## 之后的阶段

- 统一真机功能等价验收通过后，再决定 stacked P2-P7 合并策略；CI 绿不会自动 merge。
- **UI 2.0 只在功能等价验收之后开始。**
- PR #234 目前仍 Draft / open / unmerged。
- Gate13 release/cutover 是独立证据链；10 个 blocker 全部真实闭环并获得 fresh production/destructive authorization 后，才允许讨论 production cutover。

## 新对话接续

1. 先读 `AGENTS.md`、`docs/FACM4-PLAN.md`、本文件、`docs/FACM4-P7-PARITY-CLOSEOUT.md`；
2. 核对 `main@269da6c751a8463542ed0d172300675deff9571e`；
3. 核对 P7 verified code head `f3906b84dd0076411dcd8a4fd82610d1d6c2a179`；
4. 核对 Foundation #628 / run `33230830272` / artifact `9708452498` / EXE SHA-256 `d397b862...f6994`；
5. 不重复 A-K 已完成的稳定性修复与压力工作；
6. 从统一真机功能验收继续；真实 evidence 回来前不得 cutover，也不得提前开始 UI 2.0。
