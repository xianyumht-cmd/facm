<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.20
- GitHub Release：v3.5.20
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：8ecd56500807a2a314a341c6803a6afe2eb8fc9e
- 发布元数据提交：72f9d28793c9f9c93d8f1287a70241e83ffff7b3
- Release FACM.exe SHA-256：60A93CB9D3A17199487D1B3C40DD750F986C9B92B65D9B194A31E736CCB2A026
- release_notes：FACM 3.5.20：继续保持 .NET Framework 4.8 / WinForms 轻量单 EXE 架构；自动下一局改为 Lobby/ReadyCheck 立即响应，并补齐失败重试与写入结果回读确认，减少等待且避免重复操作；修复海克斯乱斗百分比显示、信息展示不完整、导航文字与紧凑控制面板残影；游戏进入 InGame 时可按归属自动隐藏桌面入口并在离开游戏后恢复；同时正式清理 4.x 工作树、迁移桥、bootstrap/CAB 与旧发布链，后续 3.5 更新清单保持 migration-free。
<!-- FACM_RELEASE_STATE_END -->

# FACM Project State

更新时间：2026-09-05

## 当前产品线

FACM 只维护 **3.5.x lightweight**：WinForms / .NET Framework 4.8 / 单 EXE。4.x 已完成能力审计并退出默认工作树；历史实现保留在 Git 历史中，不再参与当前构建或发布。

当前在线正式版为 `3.5.19`。P1 轻量回灌已通过 CI 和 Windows 10 实机 Gate 并合并到 `main`。当前 cleanup 工作将 4.x-only 项目、迁移链、旧重型发布链和历史操作资产从工作树移除；清理通过后下一正式版本为 `3.5.20`。

## 已验证的 P1 行为

- Mayhem 百分比单位修正，长内容/装备/强化展示完整性改善；3.5 快速数据链未重写。
- Lobby 进入后立即评估自动寻找，不再固定等待 1500 ms。
- ReadyCheck 立即评估自动接受，不再固定等待 450 ms；失败可在同一 episode 内短间隔重试并做最终状态 reconciliation。
- Matchmaking 写失败/结果不明确时读取 queue state，避免“已生效但响应丢失”造成重复 POST。
- disconnected/null Gameflow cadence 为 3 秒；ChampSelect 2 秒级、Queue/ReadyCheck 3 秒、InGame 10 秒。
- InGame 自动隐藏悬浮入口/桌宠，离开 InGame 后只恢复由 Gameflow 自己隐藏的入口。
- PetHost 启动过程中保留 desired visibility，避免游戏中晚启动闪现。
- 导航 owner-draw 残影与紧凑控制中心首次裁剪残影已修复。
- 普通构建不内嵌 self-contained PetHost；轻量 FACM.exe 体积 gate <10 MiB。

P1 合并 PR：#241。Windows Build 1542、UI Text Contract 650 通过；用户实机未发现阻塞问题。

## 当前保留组件

- `src/FACM`：主程序与 3.5 runtime。
- `src/FACM.ToolBundle`：内置工具资源。
- `src/FACM.Updater`：3.5 单 EXE 更新替换、校验、回滚。
- `src/FACM.PetHost`：可选桌宠 runtime 源码与 IPC。
- `online/version.json`、`online/announcement.json`、`online/mirrors.json`。
- 3.5 Windows Build、UI Text Contract、Mayhem probe、Online Management、3.5 Lightweight Release workflows。

## 已退出的 4.x 范围

不再维护或构建：WinUI/Morphing Surface、`FACM.App/Core/Infrastructure/Platform.Windows`、native bootstrapper、CAB、多版本 `.facm/versions`、4.x migration、4.x foundation/smoke/probe、旧 heavyweight embedded-PetHost publisher。

3.5 Updater 中曾保留的 4.x migration CLI/model/bootstrapper handoff 已移除；普通 3.5 原子替换/回滚/self-test 继续保留。

## 当前发布状态

- `online/version.json`: 3.5.19，enabled=true，force_update=false。
- 3.5.20 尚未发布。
- 发布入口：`.github/workflows/publish-3.5-lightweight.yml` 或 `release/3.5-request.json`。
- cleanup 合并前不触发 3.5.20 发布。

## 当前 Gate

清理 PR 必须满足：

1. `FACM.sln` 只引用当前 3.5 项目。
2. Windows Build PASS。
3. UI Text Contract PASS。
4. ToolBundle 嵌入正常；PetHost ZIP 不嵌入；FACM.exe <10 MiB。
5. Updater self-test PASS。
6. retained source/workflow 不再依赖已删除 4.x 项目/脚本。
7. `online/version.json` 不再包含 4.x migration 配置。

Gate 全绿后合并 cleanup，再发布 3.5.20。后续若发现实机问题，按普通 3.5.x bugfix 处理，不恢复 4.x 产品线。
