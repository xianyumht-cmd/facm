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

> **生产仍是 FACM 3.5.15。** FACM 4.0 当前只有代码侧功能等价候选，不存在 4.0 production cutover 授权。没有 release evidence READY + fresh production/destructive authorization，不得修改 `online/version.json` / `release/request.json`、发布 4.0.0、退休 legacy、deploy/restart 或删除历史分支/tag。

## 当前 canonical / active line

- canonical `main`：`269da6c751a8463542ed0d172300675deff9571e`，Merge PR #221。
- #218 已修复 Win10 22H2 `TabViewButtonBackground` / `XamlParseException` 启动故障并合入。
- #221 已完成 FACM 3.5 风格 launcher-first / F / compact launcher 行为迁移；Win10 22H2 R4 真机已通过并合入。
- 功能迁移采用 stacked P2-P7 线，**全部保持 Draft / 未合并到 main**，用于先完成整体 3.5.15 功能等价再统一真机验收。

Active stacked PRs：

| 阶段 | PR | Head | 状态 |
| --- | --- | --- | --- |
| P2 Cleanup | #223 | `6bf8956b61683c734b236fd8a38a539168e57918` | code-green / Draft |
| P3 Repair | #226 | `684dc94ee0beb02569a39e6fb5be19c5b1f8b359` | code-green / Draft |
| P4 Personalization | #228 | `2f1efa396cd9add76c96cdf38dee82fac7a16de7` | code-green / Draft |
| P5 League Workbench | #230 | `e3bac2e779e00051b51005e5b715196602c4982f` | code-green / Draft |
| P6 Settings / Maintenance | #232 | `d3801a0fa4276e74514a59a6c673c4cc4efbaff8` | code-green / Draft |
| P7 Unified parity closeout | #234 | candidate code head `3956e1414e22cf8bf24fd654ab66a795e52d7723` | **CODE-GREEN / real-machine validation pending** |

Tracking Issue：#233。

## FACM 4.0 P7：代码侧整体功能等价已收口

P7 已完成以下代码侧审计与修复：

- Settings2 migration 直接以 production FACM 3.5.15 `AppSettings.BuildLines()` 的真实 15-key 集合和顺序为 contract；
- legacy `settings.ini` 迁移保持 byte-for-byte 不改写，ExistingV2 不重复迁移；损坏/新版本 V2 与 atomic-write failure 保持 fail-safe；
- Repair / League / Personalization / Settings 四个主入口全部落到真实功能；primary surfaces 无用户可见开发占位；
- Cleanup 继续要求 explicit confirmation，blocked-target presentation 与 Core contract 一致；
- launcher-first / F / compact / MainWindow 生命周期 owner 保持唯一；
- League 保持一个 session source、一个 shared gateway、一个 gameflow loop；
- PetHost、global hotkeys、maintenance、single-instance、updater helper 均保留清晰 process/runtime ownership；
- P7 收口中误用的旧 League / Diagnostics API 已恢复到 P6 已验证 contract，MainWindow 不错误 dispose League runtime。

详细 parity matrix：`docs/FACM4-P7-PARITY-CLOSEOUT.md`。

## 统一候选证据

Candidate code head：`3956e1414e22cf8bf24fd654ab66a795e52d7723`。

FACM 4.0 Foundation **#595 / run `33194723681` = SUCCESS**，同一 candidate head 已通过：

- P1-P7 全部 source gates；
- controlled PetHost payload + self-test；
- controlled updater payload + security contract；
- Release x64 build；
- deterministic FoundationSmoke；
- deterministic WindowsSmoke；
- WinUI x64 self-contained single-file publish；
- publish-output verification；
- artifact upload。

统一候选：

```text
artifact: facm4-x64
artifact id: 9695331632
artifact ZIP bytes: 165,696,693
artifact ZIP sha256: 12ac16496ff76918d1aa05167ebb30250005d429a274d44422ef46a96d255524
FACM.App.exe bytes: 305,879,700
FACM.App.exe sha256: d2ebddbf109c3525668c11a12598bef85f7aba79126eb3b25c08b168856e3c40
```

ZIP digest 已与 GitHub Actions artifact metadata 对上；EXE hash 已从下载后的 artifact 重新计算。

## 当前真实边界：REAL-MACHINE / GATE13

P7 code-green **不等于 release-ready**。Canonical release evidence 仍是：

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

这些证据不能用 hosted CI、source gate 或用户普通“继续”替代。

## 下一步：唯一一次统一真机功能验收

当前工程下一动作不是继续堆新功能，也不是 UI 2.0，而是用 artifact `9695331632` 对整体功能等价做一次统一真机验收。

第一轮只做非破坏验证：

1. 冷启动只出现 launcher-first F，不回归 giant Shell；
2. F 拖动、位置持久化、compact launcher 打开/关闭、进入详细 Shell；
3. Repair / League / Personalization / Settings 四个入口均可用；
4. Cleanup preview/review/cancel；UAC 点“否/取消”后原实例继续存在；
5. 主题 / F reset / pet selection / PetHost 启停；
6. 真实 LOL 客户端下 Dashboard / Player / Live / Mayhem 等读取链；
7. Settings 中 auto-update toggle、manual check、announcement、diagnostics/log entry；
8. 二次启动只 signal 已有实例，不产生第二个常驻 FACM runtime；
9. 正常退出后 PetHost / League / hotkeys / maintenance runtime 有序释放。

首轮不要执行真实 LOL 目录删除、真实 updater replacement、production pointer 修改、release publication 或 legacy retirement。破坏性验证必须单独明确授权并在执行前重新做 safety check。

## 之后的阶段

- 统一真机功能等价验收通过后，再决定 stacked P2-P7 的合并策略；不能因为 CI 绿自动 merge。
- **UI 2.0 只在功能等价验收后开始。**
- Gate 13 release/cutover 是另一条证据链；只有 10 个 blocker 全部真实闭环并获得 fresh production/destructive authorization 后，才允许讨论 production cutover。

## 新对话接续

未来 AI 进入仓库后：

1. 先读 `AGENTS.md`、本文件和 `docs/FACM4-P7-PARITY-CLOSEOUT.md`；
2. 核对 `main@269da6c751a8463542ed0d172300675deff9571e`；
3. 核对 PR #234 candidate code head `3956e1414e22cf8bf24fd654ab66a795e52d7723`、Foundation #595、artifact `9695331632`；
4. 不重复 P2-P7 已 code-green 的迁移工作；
5. 从统一真机功能验收继续；在真实 evidence 回来前不得 cutover，也不得提前开始 UI 2.0。
