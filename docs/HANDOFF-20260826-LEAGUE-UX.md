# FACM League UX 交付记录（2026-08-26）

## 本轮目标

- 统一 League 工作台内联页面视觉，降低硬边、锐利高对比与“叠 HUD”观感。
- 保留现有单一 League Hub 与按需创建业务页，不把稳定业务逻辑揉成万能控制器。
- 进入 `ChampSelect` 时自动提供实时对局/Bench 快速换英雄入口，方便大乱斗/海克斯模式抢英雄。
- 修复已是最新版时，普通正式版公告仍让启动流程打开“更新与公告”完整窗口的问题。

## 最终实现

### 1. 轻磨砂视觉层

新增 `LeagueSoftGlassSkin`：

- 只做 WinForms 原生轻量视觉适配，不做桌面截图、DWM Acrylic、游戏注入或 Hook。
- 统一弱化按钮/面板边框与 Hover 对比，按钮采用柔和圆角，列表去掉硬质 FixedSingle 边框。
- Hub 本体与所有内联 League 子页复用同一层视觉适配；业务窗体与数据服务不重写。
- 目标是“低对比、柔和、轻磨砂”，优先保证 Windows 10 和旧显卡环境的稳定与响应速度。

### 2. Champ Select 实时面板

自动面板由 `LeagueHubModule` 负责，避免让 `LeagueLiveModule` 反向依赖 Dashboard：

- 读取 `LeagueDashboardModule.CurrentGameflowState` 的现有缓存，不新增第二套 LCU/gameflow 轮询。
- 第一次进入 `ChampSelect` 时，在当前鼠标所在屏幕工作区右上角弹出实时对局窗并置顶。
- 自动弹窗直接复用现有 `LeagueLiveForm`、`LeagueBenchQuickPickService` 和写入安全校验。
- 用户手动关闭后，本轮 Champ Select 不再反复弹出；退出选人阶段后状态复位，下一局可再次弹出。
- 若用户已经打开 League Hub 且当前就在“实时对局”页，则优先把现有 Hub 提到前台，不创建第二个重复实时窗。
- 离开 Champ Select 后自动关闭由 FACM 自动创建的快捷实时窗。

### 3. 已是最新版仍弹更新中心

根因不是版本比较错误：3.5.10 的 `online/announcement.json` 设置了 `popup=true`，启动流程会把“未读且要求 popup 的公告”也视为需要打开完整 Online Center。

本轮将普通 3.5.10 正式版公告改为 `popup=false`：

- 真正有新版本/强制更新时仍按原逻辑打开更新中心。
- 普通正式版公告不再让已经是最新版的用户启动即看到“检查更新”完整窗口。
- 手动点击检查更新仍可正常打开完整更新页面。

## 架构边界

- 不新增游戏注入 Overlay。
- 不新增第二套 LCU session/gameflow monitor。
- `LeagueLiveModule` 继续只负责实时数据和 Bench 快速换英雄；其依赖图保持原样。
- 自动浮出属于 League Hub 的页面编排/导航体验，不进入底层业务服务。
- `Program.cs` 与原有模块构造依赖保持不变。

## 实机验收清单

正式发布前至少验证：

1. 已安装当前最新版时连续启动 FACM 两次，不再自动打开“更新与公告”完整窗口。
2. League Hub 各内联页面切换正常，按钮/列表无裁切、黑边、闪烁或明显卡顿。
3. 进入 Champ Select 时快捷实时窗只出现一次；Bench 英雄可正常点击换取，关闭后本轮不重复弹。
4. 离开 Champ Select 后快捷实时窗自动收起；下一局重新进入 Champ Select 能再次出现。
5. Hub 已经停留在“实时对局”页时，不额外创建重复实时窗。

## 发布门禁

代码可在 CI 通过后并入 `main`。正式在线版本仍遵循仓库发布门禁：完成 Windows 实机验收并获得发布确认后，再从已合并的 `main` 生成下一正式版 Release 与 `online/version.json`。
