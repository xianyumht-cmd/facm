# FACM 3.5.x / UI 体验升级完整交接（2026-09-07 18:03 +08:00）

> 用途：新对话继续 FACM 时，先读本文件，再读取当前 `main` / `online/version.json` / 最新 Release。本文把本轮从 3.5.19 收口、4.x 清理、更新链修复、UI Round 1/2、重绘修复到 3.5.25 无边框外壳的完整状态集中记录。
>
> 注意：本文创建前 `main` HEAD 为 `ee8a3969de312cdecbeae70725dbd803ad6dba1a`（docs-only，已同步 3.5.25 状态）。创建本文会再产生一个 docs-only commit；实际产品功能基线、Release 和在线 manifest 以本文下面的固定 SHA 为准。

## 0. 下一位 GPT 的第一原则

1. **FACM 3.5.x lightweight 是唯一产品线。** 继续 WinForms / .NET Framework 4.8 / 单 EXE，不恢复 4.x WinUI/Core/Infrastructure/Platform/bootstrapper 架构。
2. 用户已经明确：**正式功能完成后直接发布更新，有问题再修，不要候选包流程。**
3. 当前最新正式版是 **3.5.25**。新对话先让用户实机看 3.5.25；如果有问题，按 3.5.26 bugfix 处理。
4. 不要为了 UI 改造破坏 League 写入逻辑、Gameflow ownership、Mayhem 快速数据链、Updater 原子替换、更新清单协议。
5. 不要再用“猜截图”的方式定位 UI。优先读当前代码、确认 layout/owner-draw 路径，再做最小修复并加 smoke/regression。

---

## 1. 当前仓库 / 版本 / 发布状态（已验证）

Repository:

- `xianyumht-cmd/facm`
- canonical branch: `main`
- 本文创建前 `main`: `ee8a3969de312cdecbeae70725dbd803ad6dba1a`
- 该提交消息：`docs: synchronize project state with FACM 3.5.25`

当前正式 Release:

- **FACM 3.5.25**
- tag: `v3.5.25`
- Release target commit: `58640aebe07d66113cb1151db5b841b8f0cca219`
- Release 非 draft、非 prerelease
- `FACM.exe` size: **1,872,792 bytes**
- SHA-256: `A5A86EA731DB37BFB22BF2B9F2C5AA611C38F18153672B9703CB5B0E7660043F`
- 在线更新：`online/version.json` 为 `enabled=true`, `version=3.5.25`, `force_update=false`
- online enable commit: `85d8eb193316895d42b976ce11bf39cbfcd1dea1`

3.5.25 发布链：

- request commit: `cc7489e16a5df80254f96e721461be17c8155edd`
- prepare/release metadata commit: `58640aebe07d66113cb1151db5b841b8f0cca219`
- enable online update commit: `85d8eb193316895d42b976ce11bf39cbfcd1dea1`
- post-release README sync: `acf620ebb1dbccb1215832b0fda974d87e32a4e5`
- post-release project state sync: `ee8a3969de312cdecbeae70725dbd803ad6dba1a`

CI for the exact 3.5.25 UI feature head `685c102f9009e69aab67566d7aee774ce859b7a4`:

- FACM Windows Build **#1591**, run id `34100406169` — **success**
- FACM UI Text Contract **#699**, run id `34100406213` — **success**
- PR #248 had **no unresolved review threads** before merge

3.5.25 release workflow:

- FACM 3.5 Lightweight Release run id `34100706996` — **completed success**
- build / lightweight verification / sign / publish / public bytes+signer verification / enable online update / final release evidence 全部 success

当前仍存在的远程任务分支（已合并，可后续删除但不是阻塞项）：

- `feat/3.5.25-borderless-integrated-shell-20260907`

---

## 2. 本轮总体产品决策

### 已确认长期方向

- 3.5.x 继续作为 canonical product line。
- 4.x 只保留 Git 历史、旧 tag/release/remote branch；不再参与当前源码、构建、更新和发布。
- 保持 WinForms/net48/single-EXE 的轻量与响应速度。
- 未来品牌可改为 **GGman（鸡鸡侠）**，但不要做全仓字符串暴力替换；先处理用户可见品牌，内部 namespace/assembly/config ID/Release URL 等兼容标识后置审计。
- 用户不希望花时间在与正常功能无关的签名/安全/防篡改工作上；只有发布链正常要求时才维护。
- 用户要求实际使用中发现问题就发 patch，不做候选包等待。

### UI 设计方向

- 不是换 WPF/WinUI，而是在现有 WinForms 上统一设计系统。
- 顶层窗口视觉目标：**无标题栏感的一体化无边框窗口**。
- 功能上仍保留窗口拖动、双击最大化、最小化、最大化、关闭、边缘缩放。
- 用户明确不要 LOL 工作台顶部那条“当前页 · 说明”副标题，例如：
  - `快捷工具 · 管理游戏快捷键与自动化`
- 顶部交互带与正文同背景，视觉上不再是一条独立 title bar。

---

## 3. 已完成：3.5.19 → 3.5.20 主线收口与 4.x 清理

### P1 回灌 / PR #241

3.5.19 原本作为轻量基线，完成以下高价值回灌与修复：

- Mayhem/海符 53.5% 被 UI 错乘成 5350%：改为直接显示 percentage points。
- Mayhem UI 内容完整性：更高 guide panel、AutoScroll、更长技能/召唤师技能/装备/强化展示；保留原 3.5 快速数据链。
- Lobby 自动寻找：去掉固定 1500ms 初始等待，进入 Lobby 立即评估。
- ReadyCheck 自动接受：去掉固定 450ms 初始等待，立即评估。
- disconnected/null Gameflow cadence 统一 3s。
- Lobby 写失败/响应不明确时读取 `/lol-matchmaking/v1/search` 做 reconciliation；已入队则不重复写，否则 3s 可重试。
- ReadyCheck 失败后在同一 episode 500ms 短间隔 retry；读取 search 判断 Accepted/Declined final response，避免重复接受或覆盖用户明确拒绝。
- InGame 自动隐藏悬浮入口/桌宠，离开后只恢复 Gameflow 自己隐藏的对象。
- 修复“用户手动隐藏球/宠物但从托盘打开控制中心后进入游戏，控制中心没关”的 transient control center edge case。
- 保留“用户在同一 InGame 主动重新打开控制中心后，heartbeat 不会再次关掉”。
- PetHost startup desired visibility race 修复。
- 导航 owner-draw 残影修复。
- Compact control center 首次 full-height → crop 造成桌面残影修复：先 enhancer/crop，再 position/show。
- build-release.ps1 与 csproj 改为 lightweight；PetHost 只 build/self-test，不默认 embed self-contained bundle；FACM.exe <10MiB gate。

PR #241 最终已合并 `main`。

### 4.x 清理 / PR #242

已从当前工作树删除：

- `FACM4.sln`
- `FACM.App/Core/Infrastructure/Platform.Windows`
- Bootstrapper / FlyingHost / Foundation/Windows smokes / WinUI probe prototype
- 4.x foundation / WinUI workflows
- old heavyweight `publish-release.yml`
- `release/request.json`
- `online/facm4-version.json`
- 4.x evidence / BOOT / release / check-facm4 tooling
- 4.x migration bridge、CLI/model/updater migration/bootstrapper handoff

保留：

- `FACM.sln`
- `src/FACM`
- `src/FACM.ToolBundle`
- `src/FACM.Updater`
- `src/FACM.PetHost` 源码
- 3.5 Windows Build / UI Text / Mayhem probe / Online Management / 3.5 Lightweight Release

不要重复路线：**不要恢复 4.x product architecture，也不要为了“统一架构”重写 Mayhem 成 4.x 数据栈。**

---

## 4. 已完成：3.5.21 更新清单一致性 hotfix

用户实机截图出现：

- 当前版本 `3.5.20.0`
- 最新版本 `3.5.18`
- 同时 UI 仍说“当前已是最新版本”

根因不是“没推送”，而是旧更新实现把 Gitee / GitHub / GitHub 代理放进 metadata race，采用 **第一个合法 JSON 胜出**。旧缓存 3.5.18 如果响应更快，就会被接受。

修复原则：

- GitHub main 的 3.5 `online/version.json` 是版本基准。
- 多个 transport 候选可以加速，但必须在有效候选中选择最高版本，不能被 stale manifest 降级。
- 当前客户端版本与 manifest 版本比较兼容 `3.5.20` vs `3.5.20.0`。
- 增加 stale 3.5.18 + fresh 3.5.20 regression。

已发布 **3.5.21**。

不要重复路线：

- 不要再恢复“最快合法 response 即真相”。
- Gitee/代理只能作为传输路径，不能让旧缓存拥有版本真实性优先权。

---

## 5. 已完成：UI Round 1 / PR #244

目标：建立共享 WinForms Design System，不改业务语义。

新增/统一：

- `FacmActionButton`
- `FacmToggleSwitch`
- `FacmStatusBadge`
- `FacmThemeRuntime` 对共享控件热刷新
- Update Center 迁移共享视觉体系
- League Efficiency/自动化页迁移共享 Button/Toggle/status color
- Compact Launcher 四个入口 Tile 使用统一 Design System token
- 控件 smoke contract，确保 Button/CheckBox 原生行为没有因 owner-draw 丢失

过程中 UI Text Contract 曾失败：

- 原因：新增“检查中/获取失败/发现更新”等直接硬编码 UI 文本。
- 正确修复：接入既有 `UiTextKeys + UiTextRuntime`，之后 UI Text Contract 恢复 green。

不要重复路线：

- 不要在新页面继续私自写一整套 `Color.FromArgb(...)` 视觉体系。
- 不要为了自绘破坏原生 Button/CheckBox Click / Checked / CheckedChanged / keyboard/focus contract。

Round 1 已合并 `main`。

---

## 6. 已完成：UI Round 2 / PR #245

目标：场景首页 + 悬浮入口按 Gameflow 导航。

新增纯路由：`LeagueShellContextRouter`

场景语义：

- LOL 未运行 → FACM 普通首页
- 客户端普通状态 → 当前状态
- Lobby / Matchmaking / ReadyCheck → 下一局/自动化页
- ChampSelect → 实时对局
- InGame → 实时上下文
- EndOfGame / WaitingForStats → 下一局/自动化

重要修正：

最初场景跳转使用 Dashboard/Live/Efficiency standalone bridge，静态审查发现正常 host 并没有安装所有这些 bridge module，实机可能点击无反应。

最终正确路线：

- 统一进入**已经安装的 LOL Hub**，再选择目标 view。
- 用 `BeginInvoke` 延迟场景跳转，避免 Compact Launcher 按钮 Click 栈内关闭/dispose menu。
- 状态卡透明背景补 `SupportsTransparentBackColor`，避免 WinForms 透明背景运行时问题。
- 场景导航只消费现有 Gameflow state，**没有增加 poller、League session 或写入路径**。

Round 2 已合并并发布 **3.5.22**。

不要重复路线：

- 不要为场景导航再建第二个 LCU/Gameflow polling loop。
- 不要重新走未安装的 standalone bridge。
- 不要在当前按钮 Click 同步栈里直接销毁其宿主后继续导航。

---

## 7. 已完成：3.5.23 顶部遮挡修复

用户实机截图：LOL 工作台顶部导航仍被一条自绘标题区域覆盖/切掉。

根因：

- `FacmWindowChrome` 原本 `_titleBar=Dock.Top`、`_content=Dock.Fill`。
- 依赖 WinForms sibling Dock/Z-order 自动给 title band 腾空间不可靠。
- 某些实际 layout pass 中 content 会从 y=0 消费整个 client rect，title bar 再盖上去，形成真实区域重叠，不只是“刷新不及时”。

修复：

- title bar / content 都改 `Dock=None`
- `LayoutChrome()` 每次显式 SetBounds：
  - title = top...
  - content.Top = title.Bottom
- resize 时重新计算
- smoke 检查 initial + resized：
  - `content.Top == title.Bottom`
  - 无 overlap
  - horizontal bounds 一致

已发布 **3.5.23**。

不要重复路线：

- 不要重新用 `Dock.Top + Dock.Fill + Z-order` 来保证 window chrome 与 page 不重叠。
- 不要只在 LeagueHub 页面加 magic `Padding.Top=42` 掩盖共享根因。

---

## 8. 已完成：3.5.24 Toggle 重影/黑横条修复

用户实机截图显示：

- “随机点赞队友 / 自动帮你下一局 / 自动开始排队 / 自动接受对局”文字重影
- Toggle 左侧出现黑色横条
- 新轨道在右边正常，新旧像素并存

根因：

- `FacmToggleSwitch` 完全 owner-draw + `AllPaintingInWmPaint`
- LeagueHub 紧凑布局/嵌入页面会改变 Toggle 宽度
- 自绘只画新文字/轨道/圆点，没有先完整清空旧客户区
- 宽度改变后旧轨道和文字像素留在旧坐标

修复：

- Resize 后完整 repaint
- owner-draw 每次 paint 开始先使用当前父容器背景色清空整个 `ClientRectangle`
- 父背景变化也刷新
- 位图 regression：先窄宽度绘制 checked track，再扩大宽度，旧轨道坐标必须恢复成背景色

PR #247 合并，已发布 **3.5.24**。

不要重复路线：

- owner-draw 控件尺寸会变化时，不能只画“新像素”而不清全客户区。
- 不要把这种问题误判为 League automation 数据重复；这是纯绘制残留。

---

## 9. 已完成：3.5.25 视觉无标题栏一体化外壳 / PR #248

### 用户明确要求

用户认可“无边框模式，视觉上没有传统标题栏”的方向，并明确表示顶部中间那种副说明可以不要。

### 最终实现

文件：

- `src/FACM/Theming/FacmWindowChrome.cs`
- `src/FACM/Theming/FacmControlPrimitivesSmokeTest.cs`
- `src/FACM/League/LeagueHubForm.cs`

关键变化：

1. `FacmWindowChromeOptions.TitleBarHeight` 默认 **42 → 36px**。
2. 顶部 interaction band 与正文 `_content` 使用同一个 `FacmDesignSystem.Canvas` 背景。
3. 视觉上不再有明显独立 title bar。
4. F badge：28 → 24px，更紧凑。
5. title text 更紧凑。
6. window controls：34x30 → 32x28，top 根据 36px interaction band 居中。
7. 继续保留：
   - 左键拖动窗口
   - 双击顶部最大化/还原
   - minimize / maximize / close
   - borderless resize hit-test
   - maximized region handling
8. **彻底移除 LeagueHub 顶部副标题实际显示链**：
   - 删除初始 `SetSubtitle(...)`
   - 删除 sidebar hover subtitle
   - 删除 subnav hover subtitle
   - 删除 view change `UpdateChromeSubtitle()`
9. `SetSubtitle()` API 暂保留为 compatibility no-op，避免其它潜在 caller 编译/运行断裂。
10. 3.5.23 的 explicit non-overlap bounds 继续保留。
11. 3.5.24 的 toggle full-clear repaint 继续保留。

新增 smoke：

- interaction band 与 content host 背景必须相同
- 默认 interaction band 必须在 **34-38px**
- initial + resize 后都检查
- 原有 content/title non-overlap contract 继续检查

PR #248：

- branch: `feat/3.5.25-borderless-integrated-shell-20260907`
- functional branch HEAD: `685c102f9009e69aab67566d7aee774ce859b7a4`
- merge commit: `af063fcbea9fe34b810600ba233292d5ea618128`
- merged successfully
- no unresolved review threads

已发布 **3.5.25**，详见第 1 节。

---

## 10. 本轮失败方案 / 操作失误 / 原因

### A. 误创建 `noop` 占位文件到 main

发生在准备 3.5.25 分支时：

- accidental commit: `a4487c6050085060130047660faeb87ab66edfa1` (`noop`)
- 立即普通反向删除 commit：`d0edeba7fb4b2ef6485281e9b8655d0995567ee4` (`revert: remove accidental placeholder`)

没有 reset、没有 force push、没有历史改写；最终 tree 恢复。

**不要重复：** 创建 feature branch 前先用 `create_branch`；不要为了“触发/占位”向 main 创建无意义文件。

### B. 尝试向尚不存在的 branch 写文件，连续得到 404

原因：branch 还没通过 GitHub `create_branch` 创建，就调用 contents create_file。

最终正确动作：

- 先 `create_branch feat/3.5.25-borderless-integrated-shell-20260907` from clean main
- 再 sequential update files

**不要重复：** contents API 不会自动创建 branch。

### C. 3.5.23 前把顶部问题只理解成 repaint

截图看起来像遮挡/残影，但真实根因是 title/content bounds overlap。

**不要重复：** UI 看到覆盖时先检查真实 Bounds/Dock/Z-order，再判断是否是 repaint。

### D. 3.5.24 前自绘 Toggle 只 invalidating、不清旧区域

`Invalidate()` 并不保证旧坐标的自绘像素被正确恢复，特别是在 owner-draw + resize 时。

**不要重复：** 尺寸变化 owner-draw 控件必须有确定性背景清除。

---

## 11. 当前环境 / 构建契约

当前产品：

- Windows desktop
- WinForms
- .NET Framework 4.8
- single lightweight `FACM.exe`

Solution 当前保留 4 project：

- `src/FACM`
- `src/FACM.ToolBundle`
- `src/FACM.Updater`
- `src/FACM.PetHost`

构建规则：

- ToolBundle 输入校验
- optional PetHost build/self-test
- 普通 FACM.exe 不内嵌 self-contained PetHost ZIP
- build `FACM.sln`
- host/League/performance/update/floating-ball/pet/Mayhem/shared-control smokes
- FACM.exe <10MiB
- UI Text Contract

发布：

- `.github/workflows/publish-3.5-lightweight.yml`
- `release/3.5-request.json`
- 发布 workflow 会先生成 disabled online manifest，创建/验证 GitHub Release public bytes，再 `enabled=true`
- 当前不包含任何 4.x migration 字段

用户实机环境：Windows 10 为主；过去截图证实 LOL Hub 在真实 WinForms/GDI+ 下会暴露 CI 难以复现的 owner-draw/layout 问题，因此**实机截图仍是最终视觉事实来源**。

---

## 12. 当前仍未完成 / 需要新对话继续确认

### 最高优先级：用户实机验证 3.5.25

3.5.25 刚发布后，用户还没有在本对话发送更新后的新截图。

需要确认：

1. LOL 工作台顶部是否已经达到“视觉无标题栏”的效果。
2. 顶部是否仍有多余 36px 厚度感；如果用户仍认为明显，可进一步压到 34px，但不要低于当前 smoke contract 下限，除非同步修改 regression。
3. `F + 窗口名称 + — □ ×` 是否保留合理；用户明确不要的是副标题，不是已确认不要品牌/窗口名。
4. 100/125/150/200% DPI 下是否有裁切。
5. maximize/restore、drag、edge resize 是否正常。
6. 3.5.24 的 Toggle 黑条/文字重影在 3.5.25 是否仍完全消失。
7. theme switch、cover/uncover、minimize/restore 后是否出现旧像素残影。

### 可选代码清理（不是现在的阻塞项）

`src/FACM/League/LeagueHubSubtitleText.cs` 目前仍存在，但 LeagueHub 已不再使用它。不要立刻凭感觉删除；下一次先全仓 search ref：

- 若确实零 runtime/test reference，可作为 3.5.26/cleanup 的小型 dead-code 删除。
- 如果 UI Text/其它 smoke 仍引用，则保留。

### 文档状态

README / PROJECT_STATE 已在 3.5.25 发布后同步，但下次读取时仍应以 `online/version.json` + Release 为真实版本源，避免硬编码文档再次滞后。

### 远程分支清理

`feat/3.5.25-borderless-integrated-shell-20260907` 已合并但仍存在；可后续删除。不是功能阻塞项，不要为了“干净”做危险 history rewrite。

---

## 13. 新对话下一步操作（建议严格按顺序）

1. **先读取本文件**。
2. Fetch 当前 `main` HEAD，确认没有用户/Codex 在交接后又推了新提交。
3. Fetch `online/version.json` 和 Release `v3.5.25`，确认线上仍为 3.5.25。
4. 问用户/看用户最新 3.5.25 实机截图；不要重新让用户解释此前 bug。
5. 如果 UI 正常：
   - 继续下一轮体验任务（自动化总控 / 状态历史 / ChampSelect+Mayhem 融合 / 更新诊断），但一次只做一个独立 PR。
6. 如果 UI 仍有问题：
   - 根据截图精确定位到 `FacmWindowChrome` / `LeagueHubForm` / `FacmToggleSwitch` / specific child form。
   - 建 `fix/3.5.26-...` branch。
   - 最小修复 + regression。
   - Windows Build + UI Text Contract green。
   - 合并。
   - 按用户规则**直接正式发布 3.5.26，不做候选包**。
7. 不要一次把所有 UI 页面重新布局；先解决用户能看到的具体问题，再继续体验升级轮次。

---

## 14. 后续产品体验路线（尚未全部执行）

之前规划的下一阶段仍可继续：

### 自动化体验升级

- 增加“自动下一局”总控概念：荣誉 → 返回大厅 → 自动寻找 → 自动接受。
- 允许单项展开微调。
- 增加“暂停本局”。
- 显示最近一次自动动作结果。
- 不改变底层现有 dedupe/retry/reconciliation ownership。

### ChampSelect + Mayhem 融合

- ChampSelect 自动识别模式/英雄。
- Mayhem 模式自动显示当前英雄攻略。
- 尽量减少用户手动打开“海符查询”。
- **保留 3.5 快速缓存/网络链，不重写 data stack。**

### 更新 / 公告 / 诊断

- Update Center 明确：当前版本、服务器版本、实际 metadata source、最后检查时间。
- 区分“暂无公告”与“公告获取失败”。
- 一键复制诊断信息。
- 不恢复 stale-mirror fastest-wins 逻辑。

### 最后才做 GGman 品牌改名

- 用户可见品牌/标题/icon/tray/About/docs/User-Agent 优先。
- repo rename 与 GitHub release URL inventory 后置。
- 保留 FACM namespace/assembly/resource/config/internal IDs，除非确认没有兼容依赖。
- 禁止全仓 global search/replace。

---

## 15. 关键“不要重复”清单

- **不要恢复 4.x。**
- **不要换 WPF/WinUI。**
- **不要重写 Mayhem 快速数据链。**
- **不要提高 Gameflow polling 频率来追求速度**；此前对比 4.x 已确认当前 3.5 cadence 基本相同，速度问题主要来自固定等待和失败语义。
- **不要再用最快旧 manifest 赢 metadata race。**
- **不要用 Dock.Top + Dock.Fill/Z-order 作为 title/content 不重叠保证。**
- **不要给单个 League 页面塞 magic top padding 盖住共享 chrome bug。**
- **不要 owner-draw 后只 Invalidate 不清旧 client area。**
- **不要恢复 LOL Hub 顶部 subtitle/hover hint。用户明确不要。**
- **不要建立第二 Gameflow/LCU poller 或 League session。**
- **不要在不存在的 branch 上直接 contents write。**
- **不要向 main 放占位 noop 文件。**
- **不要 reset/force-push/重写历史来清理小失误。普通 revert 即可。**
- **不要走候选包等待流程。用户要求完成后直接正式发布。**

---

## 16. 重要 PR / Release 时间线

- #241 — P1: 4.x runtime best practices → lightweight 3.5.19
- #242 — retire 4.x working-tree / migration residue
- #243 — update manifest stale-source consistency hotfix → 3.5.21
- #244 — UI Round 1 shared WinForms design primitives
- #245 — UI Round 2 context home / scene navigation
- 3.5.22 — Round 1 + Round 2 正式版
- 3.5.23 — explicit shared chrome/content non-overlap fix
- #247 / 3.5.24 — Toggle owner-draw resize residue fix
- #248 / 3.5.25 — integrated borderless visual shell + remove League chrome subtitle

当前应从 **3.5.25 实机验证结果**继续，不要回到前面的规划阶段。
