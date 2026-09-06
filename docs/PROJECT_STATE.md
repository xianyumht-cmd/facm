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

更新时间：2026-09-07

## 当前产品线

FACM 只维护 **3.5.x lightweight**：WinForms / .NET Framework 4.8 / 单 EXE。4.x 已退出默认工作树、当前 CI 与发布链；历史实现只保留在 Git 历史、旧 tag/release/remote branch 中。

当前在线正式版为 `3.5.21`。3.5.20 是 P1 回灌和 4.x 工作树清理后的首个正式版；3.5.21 随后修复更新元数据竞速会接受旧缓存清单的问题。当前 `main` 发布指针为 3.5.21，更新启用且不强制更新。

## 当前已交付行为

- Mayhem 百分比单位修正，长内容/装备/强化展示完整性改善；3.5 快速数据链未重写。
- Lobby 进入后立即评估自动寻找，不再固定等待 1500 ms。
- ReadyCheck 立即评估自动接受；失败可在同一 episode 内短间隔重试并做最终状态 reconciliation。
- Matchmaking 写失败/结果不明确时读取 queue state，避免“已生效但响应丢失”造成重复 POST。
- disconnected/null Gameflow cadence 为 3 秒；ChampSelect 约 2 秒、Queue/ReadyCheck 3 秒、InGame 10 秒。
- InGame 自动隐藏悬浮入口/桌宠，离开 InGame 后只恢复由 Gameflow 自己隐藏的入口；用户在同一 InGame 显式重开控制中心不会被 heartbeat 重复关闭。
- PetHost 启动过程中保留 desired visibility，避免游戏中晚启动闪现。
- 导航 owner-draw 残影与紧凑控制中心首次裁剪残影已修复。
- 普通构建不内嵌 self-contained PetHost；轻量 FACM.exe 体积 gate <10 MiB。
- 更新 manifest 以 GitHub main 的 3.5 清单为唯一版本基准；多个传输候选选择最高有效版本，旧镜像不能把服务器版本倒退到当前客户端以下。
- UI Round 1 已合并：共享 `FacmActionButton` / `FacmToggleSwitch` / `FacmStatusBadge`、统一 ThemeCatalog/FacmDesignSystem 视觉语义、Update Center / League Efficiency / Compact Launcher 共享设计 token，并保留原业务交互语义。

P1 合并 PR：#241；4.x working-tree cleanup 合并 PR：#242；3.5.21 更新一致性修复 PR：#243；UI Round 1 合并 PR：#244。

## 当前进行中的产品体验任务

Round 2 正在 PR **#245**、分支 `feat/3.5.22-context-home-navigation-20260906` 上进行。目标是让悬浮入口从“固定四个快捷方式”升级成轻量的场景首页，同时继续复用唯一 Gameflow owner：

- `LeagueShellContextRouter` 只消费现有 `LeagueDashboardModule` 的 Gameflow 状态，不新增 LCU polling 或写入。
- 直接左键点击内置悬浮入口时，在原四个快捷方式上方显示当前 LOL 状态、自动下一局设置摘要与场景提示。
- Lobby / Matchmaking / ReadyCheck / 结算后场景指向“下一局设置”；ChampSelect / InGame 指向“实时对局”；普通客户端状态指向“当前状态”。
- 场景跳转进入现有统一 LOL Hub 的对应 view，不建立第二套 League 窗口所有权或 session。
- 托盘/第二实例唤起“控制中心”仍打开普通四快捷方式首页；右键完整菜单保持原行为。
- 工作目录不再占普通首页主要空间，只在缺失时作为小提示出现。

Round 2 在 CI、静态交互审查和必要的实机视觉检查完成前保持 Draft，不修改线上 3.5.21 manifest，也不预先发布 3.5.22。

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

- `online/version.json`：3.5.21，enabled=true，force_update=false。
- GitHub Release：`v3.5.21`，非 draft、非 prerelease。
- Release `FACM.exe` SHA-256：`EE86DA07E7723C7952056C604A4961FBA9434F06FAAD36A738BF9DCFFFD93D5D`。
- 当前开发中的 UI Round 2 尚未发布，不修改线上 3.5.21 manifest。

## 当前维护 Gate

后续修改继续满足：

1. `FACM.sln` 只引用当前 3.5 项目。
2. Windows Build PASS。
3. UI Text Contract PASS。
4. ToolBundle 嵌入正常；PetHost ZIP 不嵌入；FACM.exe <10 MiB。
5. Updater self-test PASS。
6. retained source/workflow 不依赖已删除 4.x 项目/脚本。
7. `online/version.json` 和 publisher 不重新引入 4.x migration 配置。
8. UI 重构保留原 WinForms 交互语义；不得为了视觉一致性改变 League 写入、更新协议或现有业务所有权。
9. 场景首页必须复用共享 Gameflow 状态；不得为导航速度新建轮询器或第二 League session。

后续若发现实机问题，按普通 3.5.x bugfix 处理并发布新的 patch 版本，不恢复 4.x 产品线。
