<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.21
- GitHub Release：v3.5.21
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：de43006f9943f9f7f5af59f810849b94777c0cb1
- 发布元数据提交：912be35c9816dc95c2b68887f63afcfcced81f9a
- Release FACM.exe SHA-256：EE86DA07E7723C7952056C604A4961FBA9434F06FAAD36A738BF9DCFFFD93D5D
- release_notes：FACM 3.5.21：修复检查更新可能读取到过期镜像清单的问题。更新元数据现在以 GitHub main 的 3.5 清单为唯一版本基准，并在多个传输源中选择最高有效版本；旧镜像不能再把“最新版本”显示成低于当前客户端的版本。同时修正 3 段发布版本与 4 段程序集版本的比较语义，并加入 3.5.18/3.5.20 旧清单竞争回归测试。
<!-- FACM_RELEASE_STATE_END -->

# FACM Project State

更新时间：2026-09-05

## 当前产品线

FACM 只维护 **3.5.x lightweight**：WinForms / .NET Framework 4.8 / 单 EXE。4.x 已完成能力审计并退出默认工作树；历史实现保留在 Git 历史中，不再参与当前构建或发布。

当前在线正式版为 `3.5.20`。P1 轻量回灌已合并到 `main`，4.x 工作树清理 PR #242 也已合并；3.5.20 随后由 lightweight publisher 构建、签名、公开验证并启用在线更新。

## 3.5.20 已交付行为

- Mayhem 百分比单位修正，长内容/装备/强化展示完整性改善；3.5 快速数据链未重写。
- Lobby 进入后立即评估自动寻找，不再固定等待 1500 ms。
- ReadyCheck 立即评估自动接受，不再固定等待 450 ms；失败可在同一 episode 内短间隔重试并做最终状态 reconciliation。
- Matchmaking 写失败/结果不明确时读取 queue state，避免“已生效但响应丢失”造成重复 POST。
- disconnected/null Gameflow cadence 为 3 秒；ChampSelect 2 秒级、Queue/ReadyCheck 3 秒、InGame 10 秒。
- InGame 自动隐藏悬浮入口/桌宠，离开 InGame 后只恢复由 Gameflow 自己隐藏的入口。
- PetHost 启动过程中保留 desired visibility，避免游戏中晚启动闪现。
- 导航 owner-draw 残影与紧凑控制中心首次裁剪残影已修复。
- 普通构建不内嵌 self-contained PetHost；轻量 FACM.exe 体积 gate <10 MiB。

P1 合并 PR：#241。cleanup 合并 PR：#242。cleanup exact head 的 Windows Build #1549、UI Text Contract #657、Mayhem Source Probe #464 均通过。无法由 CI 完整模拟的 Windows/League 实机交互仍应在后续真实使用中作为普通 3.5.x 回归观察，而不是恢复 4.x 产品线。

## 当前保留组件

- `src/FACM`：主程序与 3.5 runtime。
- `src/FACM.ToolBundle`：内置工具资源。
- `src/FACM.Updater`：3.5 单 EXE 更新替换、校验、回滚。
- `src/FACM.PetHost`：可选桌宠 runtime 源码与 IPC。
- `online/version.json`、`online/announcement.json`、`online/mirrors.json`。
- 3.5 Windows Build、UI Text Contract、Mayhem probe、Online Management、3.5 Lightweight Release workflows。

## 已退出的 4.x 范围

不再维护或构建：WinUI/Morphing Surface、`FACM.App/Core/Infrastructure/Platform.Windows`、native bootstrapper、CAB、多版本 `.facm/versions`、4.x migration、4.x foundation/smoke/probe、旧 heavyweight embedded-PetHost publisher。

3.5 Updater 中曾保留的 4.x migration CLI/model/bootstrapper handoff 已移除；普通 3.5 原子替换/回滚/self-test 继续保留。当前 `online/version.json` 与 3.5 lightweight publisher 都保持 migration-free。

## 当前发布状态

- `online/version.json`：3.5.20，enabled=true，force_update=false。
- GitHub Release：`v3.5.20`，非 draft、非 prerelease。
- Release `FACM.exe`：1,850,776 bytes。
- Release SHA-256：`60A93CB9D3A17199487D1B3C40DD750F986C9B92B65D9B194A31E736CCB2A026`。
- 发布工作流 `FACM 3.5 Lightweight Release` run #2 全步骤通过，包括构建、签名、公开制品回读验证和在线启用。
- 下一版本不预占固定号；需要发布时使用尚未存在的新 3.5.x patch 版本。

## 当前维护 Gate

后续修改继续满足：

1. `FACM.sln` 只引用当前 3.5 项目。
2. Windows Build PASS。
3. UI Text Contract PASS。
4. ToolBundle 嵌入正常；PetHost ZIP 不嵌入；FACM.exe <10 MiB。
5. Updater self-test PASS。
6. retained source/workflow 不依赖已删除 4.x 项目/脚本。
7. `online/version.json` 和 publisher 不重新引入 4.x migration 配置。

后续若发现实机问题，按普通 3.5.x bugfix 处理并发布新的 patch 版本，不恢复 4.x 产品线。
