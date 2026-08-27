# FACM 统一窗口外壳与 LOL 空间利用 — 2026-08-27

## 本轮目标

本轮来自 Windows 实机界面反馈，目标不是继续机械缩小窗口，而是统一 FACM 的产品外壳，并把 LOL 工作台原本长期空置的区域用于真实上下文信息。

1. FACM 自有的普通顶层 WinForms 不再使用系统原生标题栏/最小化/最大化/关闭按钮，改用统一 FACM 自绘 Window Chrome。
2. 复用控制中心的桌面交互语义：点击窗口外桌面或切到其它进程时关闭当前临时窗口；点击窗口内部空白区域不关闭。
3. 同一 FACM 进程内打开子对话框、文件选择器或其它 FACM 窗口时，不因为父窗口 Deactivate 而误关父窗口。
4. 已经拥有专用无边框渲染的 MainForm、控制中心和桌宠不再套第二层标题栏；可关闭的无边框临时窗口只复用 outside-close / Esc 行为。
5. LOL 工作台在 Dashboard / 快捷工具 / 在线状态等稀疏页面的宽屏布局里增加上下文区，显示真实客户端状态、对局阶段和相关快捷入口。
6. 不新增第二套 LCU session、gameflow monitor、writer、网络请求或动画 Timer。

## 统一窗口外壳

新增 `src/FACM/Theming/FacmWindowChrome.cs`：

- `FormBorderStyle.None` 自绘外壳；
- FACM `F` 品牌标识与统一标题文字；
- 自绘关闭 / 最小化 / 最大化按钮；
- 标题栏拖动；
- 可调整大小窗口保留边缘 resize hit-test；
- 最大化时取消圆角 Region，普通状态恢复设计系统圆角；
- `Esc` 关闭正常临时窗口；
- `Deactivate` 后延迟检查 Windows 前台 HWND：如果前台窗口仍属于 FACM 当前进程，不关闭；如果切到桌面或其它进程，关闭。

`ControlBox=false` 的窗口默认不获得关闭按钮、Esc/outside-close，避免强制更新或不可中断流程被统一行为破坏。

新增 `FacmBorderlessOutsideClose` 给已有专用无边框临时 Form 补 outside-close / Esc，但明确排除：

- `MainForm` 常驻 Shell；
- `CompactMenuForm` 控制中心（继续使用自身 `_dialogOpen` 保护）；
- `FACM.Pets.*Window` 常驻桌宠窗口。

## LOL 工作台空间利用

`LeagueHubForm` 改为：

- 默认内容尺寸 1120×640，最低 900×580；
- 内部 Hero/Header 收到 52px；
- 左侧分区栏 130px；
- 二级页签 42px；
- Dashboard / 快捷工具 / 在线状态且窗口宽度 >=1040 时显示 232px 上下文栏；
- Player / Live / Mayhem / Recommendation 等高信息密度页面继续把宽度完整让给主内容，不强塞右栏。

上下文栏使用 `TableLayoutPanel`，不是固定绝对 Y 坐标，因此 resize / DPI 下底部提示不会漂移。它显示：

- 当前页面；
- 客户端：已连接 / 已发现进程 / 等待连接；
- 当前 Gameflow Activity；
- 现有 `LeagueHubNavigation.RelatedViews` 提供的 3～4 个下一步；
- ChampSelect 自动实时面板说明。

状态数据只由 `LeagueHubModule` 现有 650ms UI observer 读取 `_dashboard.CurrentGameflowState` 缓存后投影到 UI。没有新建 LCU 请求或第二个监控器。

## 行为边界

- **窗口内部空白点击：无动作。**
- **桌面 / 其它程序：关闭当前普通 FACM 临时窗口。**
- **同进程 FACM 子窗口 / 原生子对话框：父窗口不因失焦自动关闭。**
- **控制中心：继续使用自身成熟 Deactivate + `_dialogOpen` 规则，不被全局层替换。**
- **桌宠 / MainForm：常驻，不参与 outside-close。**
- **强制更新 / ControlBox=false：默认不可被统一 outside-close / Esc 绕过。**

## Windows 实机验收清单

正式发布前仓库现有发布门禁仍要求 Windows 真实交互验收。重点检查：

1. LOL 工作台、在线中心、主题/个性化、桌宠选择、海斗独立窗口等 FACM 自有普通 Form 不再出现 Windows 原生标题栏按钮。
2. 自绘标题栏可拖动；LOL 工作台最小化 / 最大化 / 还原正常；可缩放窗口的四边和四角 resize 正常。
3. 点击窗口内部空白不关闭。
4. 点击窗口外桌面或切到其它应用，普通临时 FACM 窗口关闭。
5. 从 FACM 窗口打开 MessageBox / FolderBrowserDialog 等同进程子交互时，父窗口不会被 Deactivate 误关。
6. 控制中心自身点击桌面关闭逻辑仍正常，打开目录选择/清理确认时 `_dialogOpen` 保护仍正常。
7. MainForm 悬浮球、轻量桌宠、VPet 不因为点击桌面而退出。
8. Dashboard / 快捷工具 / 在线状态在宽屏下右侧空白变为真实上下文区；窄窗口自动隐藏该区。
9. Gameflow 状态更新不产生第二套 LCU 请求；ChampSelect 自动实时面板仍每局只弹一次，离开阶段自动收起。
10. 1366×768、100% DPI 下无标题遮挡、黑边、裁切、错位或明显闪烁；高 DPI / 多显示器至少验证窗口拖动与缩放。

## 发布目标

目标版本：`3.5.14`。

本轮不发送候选包给用户；代码仍先通过 PR 自动化门禁。正式 Release / 在线 manifest 的启用必须遵守仓库 `docs/OPERATIONS.md` 当前发布事务与 Windows 实机验收门禁。
