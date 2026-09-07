<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.25
- GitHub Release：v3.5.25
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：cc7489e16a5df80254f96e721461be17c8155edd
- 发布元数据提交：58640aebe07d66113cb1151db5b841b8f0cca219
- Release FACM.exe SHA-256：A5A86EA731DB37BFB22BF2B9F2C5AA611C38F18153672B9703CB5B0E7660043F
- release_notes：FACM 3.5.25：将共享 WinForms 窗口外壳调整为视觉无标题栏的一体化无边框模式。顶部仅保留紧凑的品牌、拖动区和最小化/最大化/关闭能力，并与页面内容使用同一背景，不再呈现独立标题栏层。LOL 工作台同时移除顶部二级说明文字及悬停时动态切换的副标题，只保留必要的窗口名称与页面内导航。继续保留 3.5.23 的标题区/内容区显式不重叠布局和 3.5.24 的自绘开关完整重绘修复，并新增一体化顶部区域背景与 34-38px 高度回归测试。
<!-- FACM_RELEASE_STATE_END -->

# FACM Project State

更新时间：2026-09-07

## 当前产品线

FACM 只维护 **3.5.x lightweight**：WinForms / .NET Framework 4.8 / 单 EXE。4.x 已退出默认工作树、当前 CI 与发布链；历史实现只保留在 Git 历史、旧 tag/release/remote branch 中。

当前在线正式版为 `3.5.22`。3.5.20 是 P1 回灌和 4.x 工作树清理后的首个正式版；3.5.21 修复更新元数据竞速会接受旧缓存清单的问题；3.5.22 合并 UI Round 1 + Round 2，完成共享 WinForms Design System 和悬浮入口场景首页/导航。当前更新已启用且不强制更新。

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
- UI Round 1 已合并：共享 `FacmActionButton` / `FacmToggleSwitch` / `FacmStatusBadge`、统一 ThemeCatalog/FacmDesignSystem 视觉语义，Update Center / League Efficiency / Compact Launcher 共用设计 token。
- UI Round 2 已合并：直接左键点击内置悬浮入口时可显示当前 LOL 状态、自动下一局摘要和场景提示；Lobby / Matchmaking / ReadyCheck / 结算后指向下一局设置，ChampSelect / InGame 指向实时对局，普通客户端状态指向当前状态。
- 场景导航只消费 `LeagueDashboardModule` 的共享 Gameflow 状态，不新增 LCU polling、第二 League session 或新的 League 写入路径；托盘/第二实例普通控制中心和右键完整菜单保持原行为。

P1 合并 PR：#241；4.x working-tree cleanup 合并 PR：#242；3.5.21 更新一致性修复 PR：#243；UI Round 1 合并 PR：#244；UI Round 2 合并 PR：#245。

## 当前产品体验状态

3.5.22 已把 Round 1 + Round 2 作为正式版直接发布，不再保留候选包阶段。后续若实机发现 DPI、主题、鼠标交互或其它问题，按普通 3.5.x patch bugfix 处理并发布新的版本，不回滚到候选流程。

当前体验方向：

- 继续保持 WinForms/net48/single-EXE，不为视觉升级引入第二 UI 框架。
- `ThemeCatalog` 为 palette source，`FacmThemeRuntime` 为 process-wide active theme owner，`FacmDesignSystem`/共享控件承载公共视觉语义。
- 悬浮入口场景化仅复用唯一 Gameflow owner；不得为了“更快”再建轮询器。
- 高频用户操作优先收敛到状态首页、统一 LOL Hub 与自动化设置，不再增加重复入口。

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

- `online/version.json`：3.5.22，enabled=true，force_update=false。
- GitHub Release：`v3.5.22`，非 draft、非 prerelease。
- Release `FACM.exe`：1,869,208 bytes。
- Release `FACM.exe` SHA-256：`6091A6A3F08FA7BCE01CC4901C5291A851670F3F0235222AA4EB7197404B3465`。

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
