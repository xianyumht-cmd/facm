# FACM 3.5.x / UI 体验升级完整交接

更新时间：2026-09-07 18:29 +08:00

> **给下一段对话的 GPT：先完整读取本文件，再读取当前 `main`、`online/version.json`、`release/3.5-request.json` 与最近 PR #241–#248。不要从旧 4.x 路线重新规划，也不要重复已经失败/被否决的 UI 方案。**

---

## 0. 用户当前意图与工作方式

用户希望 FACM 继续保持 **3.5.x lightweight** 产品路线，并且采用“直接修、CI 通过后直接正式发布；实机发现问题再出下一个 patch”的节奏，不再要求候选包阶段。

本阶段用户明确偏好：

- UI 继续基于 WinForms，不切 WPF/WinUI。
- 保持 `.NET Framework 4.8 + WinForms + 单 FACM.exe`。
- 视觉上希望更现代、更像完整产品，而不是传统工具箱。
- 最新明确 UI 决策：**视觉无标题栏的一体化无边框模式**。
- 仍保留必要窗口能力：拖动、双击最大化、最小化、最大化、关闭、边缘缩放。
- 用户明确不要 LOL 工作台顶部那种额外副标题，例如“快捷工具 · 管理游戏快捷键与自动化”。
- 有真实问题就修，不要为了“可能有问题”持续堆候选包。
- 新对话继续时应直接执行，不需要重新解释产品历史。

---

## 1. 当前唯一产品线与硬边界

FACM 当前唯一维护产品线：

- WinForms
- .NET Framework 4.8
- 单 `FACM.exe`
- 3.5.x lightweight

4.x 已经退出默认工作树和正式发布链，不应重新恢复：

- 不恢复 WinUI / Morphing Surface
- 不恢复 `FACM.App/Core/Infrastructure/Platform.Windows`
- 不恢复 native bootstrapper / CAB / 多版本 runtime
- 不恢复 4.x migration
- 不恢复 heavyweight embedded PetHost publisher

保留组件：

- `src/FACM`
- `src/FACM.ToolBundle`
- `src/FACM.Updater`
- `src/FACM.PetHost` 源码（普通 FACM.exe 不内嵌 self-contained PetHost bundle）
- `online/`
- `release/3.5-request.json`
- 当前 3.5 build / UI contract / release / online management workflows

4.x 历史只用于审计参考，不是未来架构方向。

---

## 2. 当前远端/发布状态

### 2.1 当前 `main`

在写本交接文档之前，远端 `main` 最新 HEAD：

`ee8a3969de312cdecbeae70725dbd803ad6dba1a`

提交：

`docs: synchronize project state with FACM 3.5.25`

本交接文档提交会位于该提交之后，因此新对话必须先重新 fetch 最新 `main`，不要死锁到此 SHA。

### 2.2 当前正式版

正式线上版本：**FACM 3.5.25**

GitHub Release：`v3.5.25`

状态：

- draft = false
- prerelease = false
- online update = enabled
- force_update = false
- minimum_version = 3.0.0

正式 `FACM.exe`：

- size: `1,872,792 bytes`
- SHA-256: `A5A86EA731DB37BFB22BF2B9F2C5AA611C38F18153672B9703CB5B0E7660043F`

当前 `online/version.json`：

- `enabled: true`
- `version: 3.5.25`
- download URL 指向 `v3.5.25/FACM.exe`
- SHA 与 Release 一致

### 2.3 3.5.25 发布链提交

重要提交：

- `af063fcbea9fe34b810600ba233292d5ea618128` — merge PR #248，集成无边框视觉外壳
- `cc7489e16a5df80254f96e721461be17c8155edd` — `release: request FACM 3.5.25`
- `58640aebe07d66113cb1151db5b841b8f0cca219` — `release: prepare FACM 3.5.25`
- `85d8eb193316895d42b976ce11bf39cbfcd1dea1` — `release: enable FACM 3.5.25 online update`
- `acf620ebb1dbccb1215832b0fda974d87e32a4e5` — README 3.5.25/UI 状态同步
- `ee8a3969de312cdecbeae70725dbd803ad6dba1a` — PROJECT_STATE 文档同步

当前仍存在已合并功能分支：

`feat/3.5.25-borderless-integrated-shell-20260907`

它已经完成使命，后续可以作为普通远程分支清理候选，但不要误认为这是当前开发分支；正式权威是 `main`。

---

## 3. 本轮完整完成进度（按版本/PR）

## 3.1 P1 与 4.x 清理背景

### PR #241 — P1 backport

已合并。

核心结果：保留 3.5.19 lightweight 架构，只回灌少量高价值可靠性/体验修复。

包括：

- 导航 owner-draw 残影修复
- Compact Launcher 首次裁剪/残影修复
- Mayhem 53.5% 被错误显示为 5350% 的单位修复
- Mayhem 长内容展示完整性改进
- Lobby 自动寻找去掉固定 1500ms sleep
- ReadyCheck 自动接受去掉固定 450ms sleep
- disconnected Gameflow 10s → 3s
- Lobby 失败写入 queue reconciliation + retry
- ReadyCheck 失败 accept 同 episode 500ms retry + Accepted/Declined reconciliation
- InGame 桌面入口/桌宠 reason-owned hide/restore
- PetHost startup visibility race 修复
- lightweight 构建契约强化

验证：

- exact head `0c2017091fc44a0d7ff06c50035b5705377bf756`
- Windows Build #1542 green
- UI Text Contract #650 green
- 当时 4.0 Foundation #761 green（现已退出产品线）
- Windows 10 手工 gate：当时未发现阻塞问题

### PR #242 — 4.x working-tree cleanup

已合并。

完成：

- 删除 FACM4.sln
- 删除 4.x-only App/Core/Infrastructure/Platform/Bootstrapper/FlyingHost/smoke/prototype
- 删除 4.x workflows / old heavyweight publisher / release request
- 删除 4.x migration bridge、model、updater migration mode
- 删除 `online/facm4-version.json`
- 删除 4.x release/evidence tooling
- 保留正常 3.5 updater 原子替换/回滚/self-test
- 保留历史审计文档 `docs/3.5.19-4.0.6-BACKPORT-AUDIT.md`

验证 exact head `da63700b77d27f5bbc01b1f1bcab14431e39b80a`：

- Windows Build #1549 success
- UI Text Contract #657 success
- Mayhem Source Probe #464 success

**不要再恢复 4.x 产品线。**

---

## 3.2 FACM 3.5.20 — cleanup 后首个正式版本

已正式发布。

目的：把 P1 + 4.x cleanup 后的 lightweight 3.5 作为新的正式基线。

后续用户发现：更新中心显示：

- 当前版本 3.5.20.0
- 最新版本 3.5.18

该问题后来在 3.5.21 解决，见下一节。

---

## 3.3 FACM 3.5.21 / PR #243 — 更新元数据 stale manifest 修复

已合并并正式发布。

### 症状

3.5.20 客户端可能出现：

`当前版本 3.5.20.0 / 最新版本 3.5.18`

### 根因

旧实现让 Gitee/GitHub/代理源的 version manifest 进行“**最快合法响应胜出**”。

问题：

- stale 3.5.18 JSON 仍然结构合法
- 如果旧缓存比 GitHub fresh 3.5.20 返回更快，就会被采用
- “合法”并不等于“最新”

### 正确修复

PR #243：

- GitHub `main/online/version.json` 成为 canonical 3.5 release manifest
- 多传输源只用于获取同一个 canonical object
- 对多个有效 manifest 选最高版本
- 若拿到的 manifest 低于当前客户端，继续扫描 canonical transport pool
- UI 不允许“最新版本 < 当前版本”
- 修正 `3.5.20` vs `3.5.20.0` 的产品版本比较语义
- Gitee 只保留 mirror catalog，不再作为独立版本 truth source

### 回归

`--update-mirror-test` 覆盖：

- stale 3.5.18 vs fresh 3.5.20
- enabled tie breaking
- 3-part / 4-part equality
- UI anti-regression

**不要重新引入 fastest-valid-manifest-wins。**

---

## 3.4 UI Round 1 / PR #244 — 统一设计基础

已合并。

Exact final head：

`203b71edb33e64d0548ac485c29c45715f68a214`

CI：

- FACM Windows Build #1566: success
- FACM UI Text Contract #674: success

### 已完成

新增统一 WinForms UI primitives：

- `FacmActionButton`
- `FacmToggleSwitch`
- `FacmStatusBadge`

统一来源：

- `ThemeCatalog`
- `FacmThemeRuntime`
- `FacmDesignSystem`

已迁移/收拢：

- Update Center
- League Efficiency / 自动化设置
- Compact Launcher tiles
- 主题动态刷新

保留原业务语义：

- Button 仍是 Button
- Toggle 仍是 CheckBox
- 不改变 League polling/write
- 不改变 updater protocol
- 不改变 Compact Launcher routing

### 一次 CI 失败及原因

早期 Round1 给 Update Center 增加了“检查中 / 获取失败 / 发现更新”等直接硬编码文案，UI Text Contract 失败。

修正：

- 改走既有 `UiTextKeys + UiTextRuntime`

**不要为了方便重新把运行时用户文案直接散落硬编码进 Form。**

---

## 3.5 UI Round 2 / PR #245 — 场景首页与悬浮入口导航

已合并。

Exact final head：

`75028086ade079b98e42dc470c98c30743d8aaa7`

CI：

- Windows Build #1578: success
- UI Text Contract #686: success
- 无 unresolved review thread

### 已完成

新增纯场景路由：`LeagueShellContextRouter`

直接消费现有共享 Gameflow 状态，不新增 polling：

- LOL 未运行 → Home
- 普通客户端 → Dashboard / 当前状态
- Lobby → 下一局/自动化
- Matchmaking → 下一局/自动化
- ReadyCheck → 下一局/自动化
- ChampSelect → Live
- InGame → Live
- EndOfGame / WaitingForStats → 下一局/自动化

悬浮球直接左键：

- 可显示当前 LOL 状态
- 显示下一局自动化摘要
- 显示场景提示
- 场景化 LOL shortcut 进入统一 `LeagueHub`

托盘/普通控制中心/右键完整菜单保持原逻辑。

### 失败路线：standalone UI bridge

Round2 初版试图调用旧 standalone：

- Dashboard bridge
- Live bridge
- Efficiency bridge

静态审查发现：正常 host 并未安装这些独立 module fields，因此场景点击可能无反应。

修复：全部委托到**现有统一 `LeagueHubUiBridge`**，然后选择对应 Hub view。

**不要再创建第二套 LOL 页面入口层级，也不要恢复 standalone bridge 路由作为正常入口。**

### 失败风险：在 Click 调用栈内同步关闭菜单

Round2 初版场景跳转可能在 Compact Launcher tile 自己的 Click 栈里同步关闭窗口。

修复：使用 `BeginInvoke` 延迟进入 Hub，避免 active Click callback 所属控件被同步 Dispose。

**不要在当前 launcher tile Click 栈里同步销毁其宿主后继续使用同一控件链。**

---

## 3.6 FACM 3.5.22 — Round1 + Round2 正式发布

已直接正式发布，无候选阶段。

之后用户实际运行发现两个独立 UI 问题：

1. LOL 工作台顶部内容被自绘 title band 遮挡
2. 自动化 toggle rows 出现重复文字/黑色横条残影

注意：这两个不是同一根因，分别在 3.5.23 / 3.5.24 修复。

---

## 3.7 FACM 3.5.23 / PR #246 — 顶部遮挡修复

已合并并正式发布。

### 用户实机症状

LOL 工作台顶部：

- 二级导航被裁掉/遮住
- 左侧顶部区域也被切
- 看起来像 repaint glitch

### 真正根因

不是单纯 `Invalidate/Refresh`。

`FacmWindowChrome` 原来依赖 sibling：

- title bar `Dock.Top`
- content `Dock.Fill`
- 再靠 Z-order

在实际 WinForms layout 中，Dock.Fill 可能先占满 client rectangle，page content 继续从 y=0 绘制，title band 后盖在上面。

所以实际是**真实几何重叠**，不是单纯旧像素。

### 正确修复

共享 `FacmWindowChrome` 根因修复：

- 不再依赖 sibling Dock/Z-order 分配 title/content 几何
- `LayoutChrome()` 明确设置互不重叠 bounds
- 每次 resize 重算
- title/content 水平 bounds 一致
- 保留 1px border 和原页面 Padding
- 初始化 title width 后再放右侧窗口按钮

Regression：

- 初始 attach：`content.Top == title.Bottom`
- resize 后仍然成立
- 不允许 overlap/gap

**不要重新改回 `Dock.Top + Dock.Fill + BringToFront` 作为空间分配方案。**

这是重要的“不要重复路线”。

---

## 3.8 FACM 3.5.24 / PR #247 — toggle 文字重复 / 黑条残影修复

已合并并正式发布。

### 用户实机症状

自动化/快捷工具页：

- “随机点赞队友”之类文字重复
- 多行 Toggle 左侧出现黑色横条
- 开关本体在新位置正常，但旧轨道/旧文字像素仍留在旧坐标

### 真正根因

`FacmToggleSwitch` 是完全 owner-draw，并用了 `AllPaintingInWmPaint`，但旧绘制路径没有在每次 paint 前清空整个 client area。

LeagueHub compact/embedded layout 改变 dock-filled toggle width 后：

- 新文字/新轨道画在新坐标
- 旧文字/旧轨道像素未被擦掉

### 正确修复

PR #247：

- resize 时完整 repaint
- owner-draw surface 设 opaque
- 每次 `OnPaint` 先按当前父容器背景色清空整个 `ClientRectangle`
- Parent background 改变时也 invalidate
- bitmap smoke：先画旧宽度，再扩大，验证旧轨道坐标恢复成背景色

**不要把 3.5.24 的黑条再误诊成 3.5.23 的 title/content overlap。它们是两个不同层面的 bug。**

---

## 3.9 FACM 3.5.25 / PR #248 — 视觉无标题栏的一体化无边框模式

这是当前最新任务，也是新对话最重要的上下文。

PR：#248

状态：merged

功能分支：

`feat/3.5.25-borderless-integrated-shell-20260907`

final feature head：

`685c102f9009e69aab67566d7aee774ce859b7a4`

merge commit：

`af063fcbea9fe34b810600ba233292d5ea618128`

### 用户明确决策

用户认可：

- 无边框
- 视觉上不要“标题栏”感

并明确拒绝/不要：

- 顶部额外副标题，如“快捷工具 · 管理游戏快捷键与自动化”

### 当前实现

`FacmWindowChromeOptions.TitleBarHeight`：

- 42 → **36px** 默认

`FacmWindowChrome`：

- 仍然是功能性的窗口外壳 owner
- 视觉上 title interaction band 与正文 `content` 使用**同一 `FacmDesignSystem.Canvas` 背景**
- 不再用明显不同的 `CanvasRaised` 做一条独立 title strip
- interaction band 仅承担：brand/drag/window controls

顶部元素：

- `F` badge：24x24，紧凑化
- 标题文字：更小、更靠左
- window buttons：32x28
- 保留 `— / □ / ×`
- close hover 仍可按 Error tone 表现

交互保留：

- 左键拖动窗口
- 双击顶部区域最大化/还原
- 最小化
- 最大化
- 关闭
- 7px borderless resize hit-test
- 最大化时 Region 取消圆角
- 普通状态继续共享 WindowRadius

### 副标题层移除

`FacmWindowChrome.SetSubtitle(...)` 现在保留为兼容 API，但实际是 no-op。

`LeagueHubForm` 已移除：

- constructor 初始 SetSubtitle
- section MouseEnter/MouseLeave subtitle wiring
- view MouseEnter/MouseLeave subtitle wiring
- `UpdateChromeSubtitle()` 调用链

因此 LOL Hub 顶部不会再显示“当前页面 · 页面说明”这一类二级 copy。

### 重要：保留 3.5.23 几何安全

虽然视觉上变成一体化，但**没有**取消 title/content 的明确几何隔离。

仍然：

- interaction band 有独立 bounds
- content 从 interaction band.Bottom 开始
- 两者背景一样，因此“看起来”是一块
- 两者坐标不重叠，因此不会重新引入 3.5.22 遮挡 bug

这是当前最重要的设计方式：

> **视觉一体化 ≠ 几何上让页面画到窗口最顶部。**

### 新 regression

`FacmControlPrimitivesSmokeTest` 增加：

- default top interaction band 高度必须 34–38px
- top band background == content background
- initial / resized 均成立
- 原有 `content.Top == title.Bottom` regression 保留

### CI

PR exact head `685c102...`：

- FACM Windows Build #1591: success
- FACM UI Text Contract #699: success
- PR 无 unresolved review threads

### 3.5.25 release workflow

`FACM 3.5 Lightweight Release` run `34100706996`：全部成功，包括：

- resolve release request
- freeze main
- apply version
- build + release smokes
- lightweight identity/resource verification
- sign
- prepare disabled manifest
- commit release metadata
- create public GitHub Release
- re-download/verify public release bytes and signer
- enable online update
- final release evidence

正式 v3.5.25 信息见本文件第 2 节。

---

## 4. 已验证的关键安全契约

新对话继续修改时，至少保持：

1. `FACM.sln` 仍只属于当前 3.5 lightweight 产品。
2. Windows Build 必须通过。
3. UI Text Contract 必须通过。
4. 普通 `FACM.exe < 10 MiB`。
5. ToolBundle 正常嵌入。
6. PetHost 源码 build/self-test，但普通 FACM.exe 不嵌入 PetHost ZIP。
7. Updater self-test 保持。
8. `online/version.json` 不恢复 migration 字段。
9. 版本 manifest 不允许 stale transport 把 latest 倒退。
10. 场景导航复用唯一 shared Gameflow，不新建第二 poller / session。
11. UI 重构不能改变 matchmaking / ReadyCheck write ownership。
12. `FacmToggleSwitch` 必须先完整清背景再 owner-draw。
13. `FacmWindowChrome` title/content 必须使用明确不重叠 bounds。
14. “无标题栏视觉”通过相同背景实现，不通过把正文实际画到顶部 interaction 区实现。

---

## 5. 已失败方案、原因、以后不要重复

### 5.1 更新 manifest “最快合法者胜”

失败原因：旧缓存也是合法 JSON，会抢赢 fresh manifest。

不要重复：

- 不再把 Gitee/代理当独立 version truth source
- 传输源只能传输 canonical manifest
- 必须比较版本

### 5.2 title `Dock.Top` + content `Dock.Fill` + Z-order

失败原因：WinForms sibling docking/Z-order 不能保证 Fill 永远排除 top band，真实环境可产生几何重叠。

不要重复：

- 不用 `BringToFront` 当作空间布局保证
- 保持 `LayoutChrome()` 显式 SetBounds

### 5.3 Toggle owner-draw 不清 client rectangle

失败原因：resize 后旧轨道/文字坐标像素残留。

不要重复：

- owner-draw control 每次 paint 必须完整覆盖/清理它拥有的像素
- 不要只画“新位置”

### 5.4 Round2 standalone Dashboard/Live/Efficiency bridge

失败原因：正常 host 没安装这些 standalone module fields，可能点击无反应。

不要重复：

- 继续通过统一 `LeagueHubUiBridge` + view selection

### 5.5 在 launcher tile Click 栈内同步 Dispose 宿主

风险：callback 所属控件被销毁，后续调用不可靠。

不要重复：

- 场景打开继续用 `BeginInvoke` / 延迟 transition

### 5.6 顶部副标题层

用户明确不要。

不要重复：

- 不恢复 LOL Hub 顶部“快捷工具 · 管理游戏快捷键与自动化”
- 不恢复 section/view hover 自动写 chrome subtitle
- 页面解释放页面内部或 context dock，需要时再显示

### 5.7 为了“真正无标题栏”彻底取消 interaction band

当前未采用，也不建议。

原因：仍需可靠保留 drag / maximize / minimize / close / resize owner。

正确原则：

- **视觉无标题栏**
- **功能上仍有无边框 window chrome owner**

### 5.8 硬编码运行时 UI 文案

Round1 UI Text Contract 已经证明这条路线会破坏文本治理。

不要重复：

- 用户可见新文案继续走 UiText contract / keys / runtime

### 5.9 本轮工具操作失误：用占位文件试图制造/探测分支

发生过一次：

- `a4487c6050085060130047660faeb87ab66edfa1` — accidental `noop` file commit to `main`
- `d0edeba7fb4b2ef6485281e9b8655d0995567ee4` — 立即普通 revert/delete，把 placeholder 从树中移除

还发生过对不存在 branch 的 `create_file` 404。

最终代码树没有残留 placeholder，但历史中保留这两个普通提交。

**不要重复：**

- 永远先 `create_branch`
- 确认 branch 存在后才 `update_file/create_file`
- 不要向 `main` 写临时占位文件来“创建分支/试权限”
- 不 force push / 不改写历史

---

## 6. 当前代码重点位置

### 6.1 Window chrome

`src/FACM/Theming/FacmWindowChrome.cs`

当前关键事实：

- default interaction band = 36px
- title/content explicit bounds
- top band + content same `FacmDesignSystem.Canvas`
- `SetSubtitle` compatibility no-op
- brand + title + window controls only
- borderless resize via `WM_NCHITTEST`
- drag via `WM_NCLBUTTONDOWN / HTCAPTION`
- maximize normal/restore preserved

修改这个文件时必须同时跑 `FacmControlPrimitivesSmokeTest`。

### 6.2 Shared control repaint

`src/FACM/Theming/FacmControls.cs`

重点：`FacmToggleSwitch`

必须保持 3.5.24 修复：

- resize repaint
- opaque
- parent background clear entire client rect
- parent background change invalidation

### 6.3 Chrome/toggle regression

`src/FACM/Theming/FacmControlPrimitivesSmokeTest.cs`

现有关键 gate：

- primitive native behavior
- toggle resize old-pixel erase
- window chrome no overlap initial/resized
- integrated chrome same background
- interaction band 34–38px

### 6.4 LOL Hub

`src/FACM/League/LeagueHubForm.cs`

当前：

- 不再调用 `FacmWindowChrome.SetSubtitle`
- section/view hover 不再改变 top subtitle
- sidebar + subnav + context dock 仍存在
- embedded child form 仍 `TopLevel=false / FormBorderStyle=None / Dock=Fill`

### 6.5 可能的死代码

`src/FACM/League/LeagueHubSubtitleText.cs`

文件目前仍存在。

`LeagueHubForm` 已经完全移除对它的使用；从当前已检查代码看它很可能已成为 orphan helper。

但**不要直接删除**：下一次先做全仓 reference search，确认其它 smoke/test/hidden caller 没有使用，再单独 cleanup。

---

## 7. 当前环境状态

本轮主要通过 GitHub 远端工具执行，未把某台本地 Windows 工作树作为 source of truth。

权威状态：GitHub `main` + Actions + Release + `online/version.json`。

CI/build 环境契约：

- Windows runner
- Visual Studio/MSBuild + .NET Framework 4.8 targeting pack
- .NET 8 用于 optional PetHost build/self-test
- FACM 主程序为 net48 WinForms
- 发布为单 lightweight EXE

没有在本轮中读取/修改用户本地 D:\project2 的某个工作树；新对话如果用户要求本地 Codex 操作，需要重新确认本地仓库是否已同步到最新 `main`。

当前远端 repository description 仍是 FACM 悬浮球工具；GGman/鸡鸡侠品牌改名尚未进入本轮实施。

---

## 8. 当前未完成/待观察问题

### 8.1 3.5.25 视觉实机反馈尚未收到

3.5.25 已直接上线，但在本交接前，用户还没有发回更新后的最终截图确认：

- 36px 顶部 interaction band 是否达到“视觉无标题栏”预期
- top brand/title/button 是否仍显得过厚
- 3.5.24 toggle duplicate/black bar 是否在最新版本仍稳定消失
- 3.5.23 top overlap 是否在 3.5.25 没有回归

所以新对话第一优先级不是盲目继续改，而是：

- 如果用户发 3.5.25 截图/录像，直接基于截图定位
- 若仍有 UI 问题，出 3.5.26 patch

### 8.2 `LeagueHubSubtitleText.cs` 可能是 orphan

如上，先全仓搜索再决定是否删。

### 8.3 canonical 文档存在历史内容可能未完全同步

虽然发布工作流顶部 block 已是 3.5.25，且 README/PROJECT_STATE 已做同步提交，但此前 `PROJECT_STATE.md` 长正文曾长期保留 3.5.22 描述。

新对话如果要继续文档维护，应重新 fetch 当前文件并检查：

- 正式版本是否都为 3.5.25
- PR #246/#247/#248 是否已被写入“已交付行为”
- 当前 release SHA/size 是否一致

不要仅信旧对话摘要。

### 8.4 其它后续产品体验任务仍未做

更早规划但未进入当前版本的项目：

- “自动下一局”总控体验进一步收拢
- 自动化最近动作历史/诊断信息
- ChampSelect + Mayhem 更深融合
- Update Center 区分“暂无公告”与“公告获取失败”
- 更新中心显示实际版本源 / 最后检查时间 / server version
- 一键复制诊断
- 更系统的 DPI / multi-monitor 体验审计
- GGman（鸡鸡侠）用户可见品牌改名

这些都不是 3.5.25 未完成 bug；不要与当前 borderless 任务混为一谈。

---

## 9. 下一步建议（新对话直接执行）

### 若用户先发 3.5.25 实机截图/视频

1. 先确认截图确实是 3.5.25。
2. 区分：
   - 几何 overlap
   - owner-draw stale pixels
   - 纯视觉密度/层级问题
3. 不要把不同问题重新混成“都是 repaint”。
4. 找到明确根因后建 `fix/3.5.26-*`。
5. 改共享根因优先，不给 LeagueHub 单页打补丁，除非根因确实只存在该页。
6. 补 smoke/regression。
7. Windows Build + UI Text Contract 全绿。
8. 按用户当前策略直接发布 3.5.26，不走候选包。

### 若用户只说“继续任务”但没有新实机问题

建议顺序：

1. fetch 最新 `main` / release / online manifest。
2. 检查 3.5.25 文档同步完整性。
3. 全仓搜索 `LeagueHubSubtitleText`，确认是否 orphan；若是，做最小 cleanup。
4. 不要继续无依据调整 window chrome 尺寸。
5. 然后进入下一轮产品体验：优先“自动下一局总控 + 最近自动动作状态”，而不是继续堆视觉装饰。

### 如果用户明确要求继续 UI

保持当前方向：

- 一体化无边框
- 顶部只保留必要窗口控制
- 不恢复副标题
- 不增加第二层/第三层标题区
- 尽量让页面内导航承担信息结构

---

## 10. 关键 PR 索引

- #241 — P1: backport 4.x runtime best practices to lightweight 3.5.19
- #242 — retire 4.x working-tree assets
- #243 — stale update manifest selection fix / 3.5.21
- #244 — UI Round1 unified design primitives
- #245 — UI Round2 context-aware floating home
- #246 — custom chrome/content overlap fix / 3.5.23
- #247 — toggle repaint residue fix / 3.5.24
- #248 — integrated borderless shell / remove League chrome subtitle / 3.5.25

全部已经 merged。

---

## 11. 一句话交接状态

**FACM 当前已正式发布 3.5.25；4.x 已退出；Round1/2 UI 已合并；3.5.23 修复真实 title/content 几何覆盖，3.5.24 修复 owner-draw toggle 旧像素残影，3.5.25 将共享 WinForms chrome 改成 36px、与正文同背景的视觉无标题栏一体化模式，并移除用户不要的 LOL Hub 顶部副标题；下一段对话应先看 3.5.25 实机反馈，有明确问题就直接出 3.5.26，不要重走 Dock/Z-order、fastest-manifest、standalone bridge 或顶部副标题路线。**
