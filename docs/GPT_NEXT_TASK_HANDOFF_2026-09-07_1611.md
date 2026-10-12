# GPT Next Task Handoff — FACM 3.5.25 UI / Release State

更新时间：2026-09-07 16:11（UTC+8）

> 新对话请先读取本文件，再读取 `docs/PROJECT_STATE.md`、`docs/ARCHITECTURE.md`、`docs/DECISIONS.md`、`docs/PITFALLS.md`。本文件记录的是本轮 FACM 3.5.x 体验升级、3.5.23/3.5.24 UI 回归修复以及 3.5.25 无标题栏 UI 发布后的权威交接状态。

## 1. 当前权威状态

- Repository：`xianyumht-cmd/facm`
- Canonical branch：`main`
- 当前 `main` HEAD：`ee8a3969de312cdecbeae70725dbd803ad6dba1a`
  - commit：`docs: synchronize project state with FACM 3.5.25`
- 当前正式线上版本：**FACM 3.5.25**
- GitHub Release：`v3.5.25`
- `online/version.json`：`enabled=true`、`version=3.5.25`、`force_update=false`
- Release `FACM.exe`：1,872,792 bytes
- SHA-256：`A5A86EA731DB37BFB22BF2B9F2C5AA611C38F18153672B9703CB5B0E7660043F`
- Release 非 draft、非 prerelease。
- 当前 3.5.25 功能 PR：#248，已 merged。
- PR #248 功能分支仍存在：`feat/3.5.25-borderless-integrated-shell-20260907`；已不再是开发中的 source of truth，`main` 才是权威。
- 当前产品线只有 **3.5.x lightweight**：`.NET Framework 4.8 + WinForms + 单 FACM.exe`。
- 4.x 已退出默认源码、CI、更新、发布链。不得因为视觉升级重新引入 WPF/WinUI、Core/Infrastructure/Platform、bootstrapper/migration 等 4.x 架构。

## 2. 用户已明确的产品决策

1. 不再保留“候选包”阶段。功能/修复完成并经过 CI 后，按 patch 版本直接正式发布；后续实机发现问题再修下一个版本。
2. UI 保持 WinForms/net48/single-EXE，不为视觉效果更换 UI 框架。
3. 当前 UI 方向是**视觉无标题栏的一体化无边框模式**。
4. 顶部仍要保留窗口能力：拖动、双击最大化/还原、最小化、最大化、关闭、边缘缩放。
5. 用户明确不要顶部那类二级说明文字，例如：`快捷工具 · 管理游戏快捷键与自动化`。不要再恢复顶部动态 subtitle/hint。
6. 功能问题以“实际代码 + 实机截图/录像”定位，不能只根据视觉猜测。
7. 出现问题时直接定位根因，不用用 page-local padding、额外 Refresh、延时或其它一次性 workaround 遮盖共享 UI 根因。

## 3. 本轮主线完成进度

### 3.1 3.5.20 / 3.5.21 基线收口

此前已完成：

- PR #241：3.5.19 P1 回灌，修复 Mayhem 百分比、Lobby/ReadyCheck 固定等待、失败重试/reconciliation、InGame 悬浮入口/桌宠 ownership、轻量发布契约等。
- PR #242：删除当前工作树中的 4.x 源码/构建/发布/migration 链，3.5.x 成为唯一 canonical 产品线。
- PR #243：更新 metadata stale-source 问题。版本元数据不能再“最快合法响应即胜出”；GitHub main 的 3.5 manifest 是 canonical 版本基准，旧 Gitee/代理缓存不能让 `latest` 倒退到 3.5.18。
- 3.5.21 正式发布后，更新链不再接受旧镜像把服务器版本倒退。

这些决策仍然有效，不要回退。

### 3.2 UI Round 1 — PR #244

PR：`UI round 1: unify FACM design primitives and core surfaces`

最终功能 HEAD：`203b71edb33e64d0548ac485c29c45715f68a214`
Merge commit：`2101b30933f4aa57e892e9efe18cabe0cf396478`

完成：

- 新增共享 `FacmActionButton`
- 新增共享 `FacmToggleSwitch`
- 新增共享 `FacmStatusBadge`
- `ThemeCatalog` / `FacmThemeRuntime` / `FacmDesignSystem` 成为统一视觉 token/source
- Update Center 迁移到共享设计体系
- League Efficiency/自动化页面迁移到共享按钮/Toggle/状态色
- Compact Launcher 四个入口 Tile 改用共享设计 token
- 共享控件接入 `--facm-host-test` smoke contract
- 保留原生 Button/CheckBox 语义和 `CheckedChanged` 语义

验证：

- Windows Build #1566：success
- UI Text Contract #674：success

失败过一次的路线：最初新增 Update Center 状态文字时直接硬编码了新的中文 UI copy，UI Text Contract 正确失败；随后全部改到现有 UiText 体系。**不要再在受 UI Text Contract 管理的 surface 上随手添加硬编码 UI 文案。**

### 3.3 UI Round 2 — PR #245

PR：`UI round 2: make the floating home context-aware`

最终功能 HEAD：`75028086ade079b98e42dc470c98c30743d8aaa7`
Merge commit：`f393632c6045134bb7dcf31eecae874d544904ac`

完成：

- 新增纯逻辑 `LeagueShellContextRouter`
- 只消费现有 `LeagueDashboardModule` 的共享 Gameflow state，不新增 poller/session
- 悬浮球直接左键打开时根据 Gameflow 显示场景状态首页
- Lobby / Matchmaking / ReadyCheck / 结算后 → 指向下一局/自动化相关入口
- ChampSelect / InGame → 指向实时对局入口
- 普通客户端状态 → 指向 Dashboard/当前状态
- tray / second-instance 打开保持普通四入口控制中心
- 右键菜单保持完整
- context route 最终统一进入已安装的 `LeagueHubUiBridge`
- 使用 `BeginInvoke` 将 Hub 跳转从 CompactMenu tile click 调用栈中延后，避免在 click 回调中关闭/Dispose menu
- 透明 context card 显式打开 `SupportsTransparentBackColor`

静态审查中否决的错误路线：

- 第一版尝试直接调用旧 standalone Dashboard/Live/Efficiency bridge；正常 Host 并没有安装这些 bridge module，可能点击无反应。已经改成统一 `LeagueHubUiBridge`。**不要恢复 standalone bridge route。**
- 不要为了悬浮球场景导航新增第二 Gameflow poller、第二 League session 或新的 League write owner。

验证：

- Windows Build #1578：success
- UI Text Contract #686：success
- 无 unresolved review thread

### 3.4 3.5.22 正式发布

Round 1 + Round 2 合并后直接发布 3.5.22，之后用户明确决定：**今后不做候选包，正式使用后有问题继续 patch。**

## 4. 3.5.23 顶部遮挡回归及根因修复

用户实机截图显示 LOL Hub 自动化页顶部 secondary navigation 被自绘标题区域盖住，看起来像“重绘问题”。

PR #246：`Fix custom chrome overlapping LOL Hub content`

Merge commit：`b7a300234dadd50f114a2415aded173a9560846b`

真正根因：

- `FacmWindowChrome` 将原标题页 controls 移入 wrapper 后，依赖 sibling `DockStyle.Top` + `DockStyle.Fill` + z-order。
- WinForms 不保证 fill content 在这种处理顺序下自动排除 title band。
- 页面实际上可从 y=0 绘制，随后 chrome 再盖上去，所以顶部导航被切掉。

最终正确修复：

- 不再依赖 sibling Dock/Z-Order 分配顶部/正文空间。
- `LayoutChrome()` 每次 attach/resize 都用显式、互斥 bounds：
  - top interaction/title region
  - content region 从 top region.Bottom 开始
- 保证 `content.Top == title.Bottom`
- 保留 1px outer border 与原 page padding
- 初次创建时先有明确 bar width，再放右侧 chrome buttons
- smoke test 检查初次和 resize 后 title/content 都不 overlap

**不要重复的失败路线：**

- 不要给 `LeagueHubForm` 单独加 `Padding.Top = 42` 之类 workaround。
- 不要恢复 `Dock.Top + Dock.Fill` 让 Z-Order“碰巧正确”。
- 不要把这个问题归类为纯 `Invalidate/Refresh` 视觉残影；它是实际 geometry overlap。

3.5.23 已正式发布。

## 5. 3.5.24 Toggle 重影/黑条回归及根因修复

3.5.23 后用户实机截图仍显示自动化四个 toggle row：

- 文字重复
- 黑色横条
- 旧轨道/旧文字像素残留

PR #247：`Fix duplicated toggle text and stale repaint bars`

Merge commit：`cd17550415ab78e700ba2f85ebbd713beb4166e9`

根因：

- `FacmToggleSwitch` 完全 owner-draw + `AllPaintingInWmPaint`
- LeagueHub compact/embed layout 会改变 Dock.Fill toggle 的 width
- 原 paint path 没有每次先清空整个 ClientRectangle
- 新轨道/文字在新坐标画出来后，旧宽度对应的旧像素仍残留

正确修复：

- resize 时完整 repaint
- owner-draw surface 按 opaque 处理
- 每次 `OnPaint` 首先使用当前 parent background 清空整个 `ClientRectangle`
- parent background 变化时 invalidate
- bitmap smoke：旧宽度先画 checked track，再扩大宽度，验证旧 track 中心位置被完整恢复为背景色

**不要重复的失败路线：**

- 不要只加 `Refresh()` / `Invalidate()`；如果 paint path 不清空旧区域，刷新次数再多也会保留错误像素。
- 不要针对 LeagueHub 四行 toggle 单独盖背景块；根因在共享 `FacmToggleSwitch`。

3.5.24 已正式发布。

## 6. 3.5.25 — 当前最新“视觉无标题栏”任务

用户明确认可：整个 UI 改成视觉无传统标题栏的 borderless shell；同时明确“顶部二级说明文字可以不要”。

### 6.1 PR / branch / commits

PR #248：`UI: integrate borderless window shell and remove League chrome subtitle`

Branch：`feat/3.5.25-borderless-integrated-shell-20260907`

Branch commits：

- `2676b751de7f7b4215b5251bcf2b34eaedbd6399` — `feat: integrate borderless window chrome into page canvas`
- `e4358ae082dec6b7f243788041be355365d26ae3` — `refactor: remove League chrome subtitle layer`
- `685c102f9009e69aab67566d7aee774ce859b7a4` — `test: lock integrated borderless chrome visual contract`

PR merge commit：

- `af063fcbea9fe34b810600ba233292d5ea618128`

### 6.2 实现内容

`src/FACM/Theming/FacmWindowChrome.cs`：

- shared chrome 默认高度 42 → **36px**
- 顶部交互区与正文都使用 `FacmDesignSystem.Canvas`
- 视觉上不再出现单独的 `CanvasRaised` 标题栏条带
- 顶部依旧保留 drag / double-click maximize / minimize / maximize / close / borderless resize
- F badge 28 → 24px，位置/圆角/字体压缩
- title font/position 变紧凑
- window controls 34x30 → 32x28，间距收紧
- `SetSubtitle(Form,string)` 保留 API，但变为 compatibility no-op
- `_subtitleLabel` 和 subtitle layout 已从 shared chrome 实际绘制路径删除
- **3.5.23 显式不重叠 geometry 没有删除**；top band 与 content 仍然是两个互斥 bounds，只是视觉颜色相同，所以看起来是一体的

`src/FACM/League/LeagueHubForm.cs`：

- 删除初始 `SetSubtitle(...)`
- 删除 sidebar hover subtitle
- 删除 subnav hover subtitle
- 删除 `UpdateChromeSubtitle()` 及 ShowView 中的 subtitle 刷新
- 用户明确不要的“当前页 · 说明”不再出现在顶部 shell

`src/FACM/Theming/FacmControlPrimitivesSmokeTest.cs`：

- 保留 3.5.23 title/content 不 overlap gate
- 新增 `TryGetVisualForSmokeTest`
- 新增 integrated visual contract：top background == content background
- default top interaction band 要保持 34–38px
- 初次 layout + resize 两阶段都验证

### 6.3 3.5.25 验证

PR 精确功能 HEAD：`685c102f9009e69aab67566d7aee774ce859b7a4`

- FACM Windows Build **#1591**：success
- FACM UI Text Contract **#699**：success
- PR #248：mergeable，最终无 unresolved inline review thread
- PR #248 已合并

Release request：

- `cc7489e16a5df80254f96e721461be17c8155edd` — `release: request FACM 3.5.25`

Release workflow：

- FACM 3.5 Lightweight Release run **#7**
- run id：`34100706996`
- conclusion：success
- build / lightweight identity / signing / prepare disabled manifest / create Release / public bytes+signer verify / enable online update / final evidence 全部 success

Release commits：

- `58640aebe07d66113cb1151db5b841b8f0cca219` — `release: prepare FACM 3.5.25`
- `85d8eb193316895d42b976ce11bf39cbfcd1dea1` — `release: enable FACM 3.5.25 online update`

发布后文档：

- `acf620ebb1dbccb1215832b0fda974d87e32a4e5` — README sync 3.5.25
- `ee8a3969de312cdecbeae70725dbd803ad6dba1a` — PROJECT_STATE sync 3.5.25（本 handoff 创建前 main HEAD）

## 7. 本轮操作失误 / 失败方案 / 原因

### 7.1 accidental `noop` on main

在 3.5.25 开始时，为了试图建立工作落点，误通过 `create_file` 在 `main` 写入了一个占位 `noop` 文件：

- `a4487c6050085060130047660faeb87ab66edfa1` — `noop`

发现后立即用正常 forward commit 删除：

- `d0edeba7fb4b2ef6485281e9b8655d0995567ee4` — `revert: remove accidental placeholder`

文件树恢复，没有 reset、force-push 或历史重写。

**以后不要用“写一个占位文件”建立 GitHub branch/work context。必须先 `create_branch`，确认 branch 存在后再写文件。**

### 7.2 向不存在 branch 写文件的 404

在正确创建 `feat/3.5.25-borderless-integrated-shell-20260907` 之前，数次尝试向不存在的 branch 写文件，GitHub Contents API 返回：

- `Branch fix not found` / 404

这不是代码问题，是工具使用顺序错误。

**不要重复：先 `create_branch(base_ref=main)`，再 `update_file/create_file(branch=...)`。**

### 7.3 “完全去掉 top region”不是当前实现

当前用户要的是**视觉上没有标题栏**，并不是删除所有 top interaction geometry。

完全取消 top interaction region 会损失 drag / double-click / window controls，并且可能重新制造正文与 window controls 争抢空间的问题。

当前正确路线是：

- 仍保留 36px 明确 top interaction geometry
- 与 content 使用同一背景
- 视觉成为单一 surface
- 功能上仍由 `FacmWindowChrome` 管窗口 shell

不要误解成下一步把 `_titleBar` / interaction band 全删掉。

## 8. 不要重复/不要回退的技术路线

- 不要恢复 4.x / WinUI / WPF 作为主 FACM UI。
- 不要把 WinForms UI 回归当成理由重做架构。
- 不要恢复 `Dock.Top + Dock.Fill` 的 chrome/content 布局。
- 不要用 LeagueHub-local 顶部 Padding 修共享 chrome overlap。
- 不要用重复 `Refresh()` 替代 owner-draw surface 的完整背景清除。
- 不要恢复顶部 subtitle/hint；用户明确不要。
- 不要为了场景导航建第二 Gameflow poller / League session。
- 不要恢复旧 standalone Dashboard/Live/Efficiency bridge 作为正常 Host route。
- 不要改变 3.5 Mayhem 快速数据链，只因 UI 改版重写 data stack。
- 不要重新让 Gitee/代理 stale manifest 以“最快响应”决定 latest version。
- 不要恢复 4.x migration 字段或 updater migration mode。
- 不要做全局 FACM→GGman 字符串替换；品牌改名必须先盘点兼容 ID/namespace/resource/config/update URL。

## 9. 当前代码/环境状态

### Repository / branch

- repo：public GitHub `xianyumht-cmd/facm`
- canonical：`main`
- 本 handoff 创建前 main：`ee8a3969de312cdecbeae70725dbd803ad6dba1a`
- branch protection 当前不是本轮安全保证来源；此前 API 读取显示 main 未启用 protection，所以写操作必须主动遵守 branch-before-write / PR-first。

### Build/runtime

- Windows desktop application
- .NET Framework 4.8 / WinForms
- `FACM.sln`
- 普通发布单 `FACM.exe`，CI gate <10 MiB
- `FACM.PetHost` 仅源码 + build/self-test，普通 EXE 不嵌 self-contained PetHost ZIP
- `FACM.ToolBundle` 正常嵌入
- canonical publisher：`.github/workflows/publish-3.5-lightweight.yml`
- release request：`release/3.5-request.json`

### 当前核心 UI files

- `src/FACM/Theming/FacmWindowChrome.cs`
- `src/FACM/Theming/FacmControls.cs`
- `src/FACM/Theming/FacmControlPrimitivesSmokeTest.cs`
- `src/FACM/Theming/FacmDesignSystem.cs`
- `src/FACM/Theming/FacmThemeRuntime.cs`
- `src/FACM/League/LeagueHubForm.cs`

### 可能的 dead/compat residue

- `src/FACM/League/LeagueHubSubtitleText.cs` 仍存在；3.5.25 已删除 LeagueHub 对它的实际 subtitle route。新对话若要清理，先全仓查引用；无引用可作为小 cleanup 删除。
- `FacmWindowChrome.SetSubtitle(Form,string)` 当前是 compatibility no-op。它可能还有其它 caller，因此不要直接删除 API，除非先完成全仓引用审查并跑 UI/host tests。

## 10. 当前未完成 / 尚未验证的问题

1. **3.5.25 已正式发布，但用户尚未发 3.5.25 实机截图确认最终视觉。**
2. CI 已验证 geometry、背景 contract、build、UI text，但不能替代真实 Windows GPU/GDI/DPI/主题/双显示器行为。
3. 新对话应优先接收用户对 3.5.25 的实机结果，重点看：
   - 顶部是否真的看起来没有独立标题栏
   - 顶部是否仍有任何 secondary subtitle 残留
   - secondary nav 是否完整、不被遮挡
   - toggle 是否没有重影/黑条
   - 100/125/150/200% DPI
   - resize / maximize / restore
   - theme switch
   - 双显示器拖动
   - 最小化/最大化/关闭按钮 hover/点击
4. 若 3.5.25 有 UI 问题，直接开 `fix/3.5.26-<problem>-20260907`（或实际日期）修复并正式发布 3.5.26；不要做 candidate 包。
5. 若 3.5.25 实机正常，则继续此前 6 轮体验计划的下一阶段：**Round 3 自动化体验升级**。

## 11. 下一轮建议任务（若 3.5.25 无新 bug）

Round 3 的方向已经定过，不需要重新做大规划：

### 自动化体验升级

目标不是重写自动化 backend，而是让用户更清楚“当前开了什么、刚刚做了什么”。

建议顺序：

1. 审查当前 League Efficiency/automation 设置与现有状态 owner。
2. 设计一个“自动下一局”聚合状态/总控，但**不能破坏原四个独立 toggle 的持久化与行为**。
3. 将：
   - 自动荣誉/点赞
   - 自动返回大厅
   - 自动寻找
   - 自动接受
   作为一条用户可理解的链显示。
4. 增加“暂停本局/暂停本 episode”的 UX 时，先定义 ownership，避免通过直接改 global setting 实现临时暂停。
5. 增加最近一次自动动作结果（例如“自动接受成功”）时，复用已有 automation/gameflow event，不新建 polling。
6. 增加状态历史/诊断时，保持 bounded memory，不把 UI 变成无限日志窗口。
7. Windows Build + UI Text Contract + automation smoke 必须全绿。
8. 合并后按用户当前发布策略直接发布下一个 patch/minor（版本号由当时线上最新版本决定）。

## 12. 新对话第一条操作

用户在新对话只需说类似：

`继续 FACM，先读取 docs/GPT_NEXT_TASK_HANDOFF_2026-09-07_1611.md`

下一任 GPT 应：

1. 先读取本 handoff。
2. 再 fetch 当前 `main` HEAD、`online/version.json`、latest GitHub Release，确认没有在交接后发生新提交/发布。
3. 如果用户带了 3.5.25 截图/录像，优先处理实机 bug。
4. 如果没有新 bug且用户说继续计划，直接进入 Round 3，不重复 Round 1/2/3.5.23/3.5.24/3.5.25 已完成工作。

## 13. 一句话交接

**当前 FACM 已正式上线 3.5.25：WinForms/lightweight 主线已完成 UI Round 1、场景首页 Round 2、chrome overlap 3.5.23、toggle repaint 3.5.24，并在 3.5.25 把 shared chrome 做成 36px 一体化视觉无标题栏，同时删除 LOL Hub 顶部动态副标题；CI/Release 全绿。下一步先看 3.5.25 实机反馈，有 bug 直接 3.5.26，没 bug 就继续 Round 3 自动化体验。**
