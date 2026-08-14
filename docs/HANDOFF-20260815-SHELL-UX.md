# FACM Shell UX 收束交接（2026-08-15）

## 任务

- Issue #104：`Shell UX 收束：托盘一级菜单固定分组 + 控制中心渐进式二级入口`
- Draft PR #105：`Shell UX：收束托盘一级菜单与控制中心入口`
- branch：`feat/shell-ux-104`
- base：`main` @ `641691108b8eca47c21c2b9b893c651f1ce957b7`
- exact 行为候选 HEAD：`6f3d8330127546327830048d06db89df0ae44a02`
- 当前生产仍是 FACM 3.2.0 / `force_update=false`；本任务没有 Release / Tag / online update。

## 用户目标

FACM 面向电脑小白。目标不是把现有按钮缩小，而是减少每一层的选择数量：

- 看起来功能很多、实现很强，但第一次打开不需要理解所有内部能力；
- 高频动作直接可见；
- 低频/高级能力按任务类别进入二级菜单；
- 最多两层，不做三级迷宫；
- 后续新增模块不能再把一级入口数量堆回去。

## 冻结后的 Shell 信息架构

### 托盘 / 悬浮球右键一级

一级固定为 5 个可操作入口：

1. `打开控制中心`
2. `清理环境`
3. `英雄联盟 >`
4. `更多 >`
5. `退出程序`

业务模块不得直接往根菜单插第 6 个入口。

### `英雄联盟 >`

当前 `main` 能力按固定顺序注册：

1. 英雄联盟面板
2. 玩家主页
3. 实时对局
4. OP.GG 对局助手
5. OP.GG 一键应用
6. 海斗排行榜

为仍在 Draft PR #103 的 Gate 3 预留 `ItemSetOrder=60`；当 #103 独立完成腾讯实机验收并合入后，`OP.GG 装备集` 应注册在一键应用之后、海斗排行榜之前。

**本 Shell UX 候选基于当前 `main`，故意不包含尚未合并的 Gate 3 装备集实现。不要把“候选里没有 OP.GG 装备集”误判为回归。**

### `更多 >`

承载低频维护/桌面能力：

- 面板主题
- 桌面宠物
- 恢复默认悬浮球
- 宠物复位
- 检查更新
- 操作日志

## 控制中心首页

旧首页的一层平铺已经收束：

- 游戏/工作目录不再同时展示“自动识别 + 选择目录”两个日常按钮，而是状态行 + 一个 `管理`；二级才出现自动识别/手动选择。
- `清理环境` 是唯一高强调主动作，原有预览/确认/管理员权限/安全语义不变。
- `功能中心` 只显示三个类别：
  - `修复工具`：二级包含驱动清理 ToolA + 原有 4 个修复模式；
  - `英雄联盟`：直接复用托盘同一组 League action，不维护第二份功能清单；
  - `个性化`：面板主题 / 桌面宠物 / 恢复默认悬浮球 / 宠物复位。
- `更多设置`：启动自动检查 / 检查更新 / 操作日志 / 退出程序。
- 旧 `CompactMenuEnhancer` 不再通过反射往首页动态注入“主题/海斗”等按钮，只保留 UI Text、首帧和外部点击关闭兼容基础设施。
- 原控制中心硬编码 `3.1` 已改为读取 `Application.ProductVersion` 的 major.minor，避免以后版本升级后首页仍显示旧版本。

## 长期约束

`ShellMenuGroups` 是 Shell 一级信息架构边界：

- 根菜单必须恰好包含 5 个固定角色；
- Dashboard / Player / Live / OP.GG / Mayhem 等业务动作只能注册到既有 group；
- 同一 action name 重复注册不得增长菜单；
- League 顺序由稳定 order 控制；
- 不允许为了新模块新增三级菜单；
- 运行时每次业务 action 注册都会校验真实 root contract；
- CI 的 `ShellUxSmokeTest` 使用纯定义校验，不实例化 WinForms 对象，因为 `--performance-contract-test` 在 `Application.EnableVisualStyles()` / `Application.Run()` 之前执行。

## UI Text Contract

本轮新增的 `英雄联盟 / 更多 / 功能中心 / 修复工具 / 个性化 / 更多设置 / 管理 / 目录状态 / 状态格式` 等可见文案全部进入正式 `UiTextKeys + UiTextCatalog`。

早期 UI Text run 曾精确抓出 7 个新增硬编码文案；全部改为正式 Key，没有用 allow-list 绕过。

`UiTextRuntime.Apply` 参数放宽为标准 `System.Windows.Forms.ContextMenuStrip`，因此 FACM 自有菜单子类和 NotifyIcon 暴露的基类菜单都能走同一实时文字刷新。

## 候选验证

exact behavior HEAD：`6f3d8330127546327830048d06db89df0ae44a02`

- UI Text Contract #128：SUCCESS
- Windows Build #1007：SUCCESS
- Windows #1007 日志明确输出：`FACM performance contract smoke passed.`
- Release compile：SUCCESS
- FACM.exe verify：SUCCESS
- self-signed Authenticode：SUCCESS（预期开发根证书不受系统信任，但文件 digest 完整）
- package/upload：SUCCESS
- FACM.exe version：`3.2.0.0`
- build output size：`78,091,776` bytes
- signed FACM.exe SHA-256：`97BDF787C3F2E6DCEFF42240BEE3D824C672C98F16280A380CBDAB96E2241E61`
- artifact：`FACM-Windows-x64-1007`
- artifact id：`9232122863`
- artifact size：`154,717,686` bytes
- artifact ZIP SHA-256：`40FDACE9D29BBDAE3DD48E3AD13EC0B161A91AC45EC1FD55DA27BDF0C1BF3FD4`
- artifact run：`31834855429`

## Windows 人工验收重点

不要重测所有已验收业务逻辑；重点验证 Shell UX 和入口可达性：

1. 右键一级只看到：打开控制中心 / 清理环境 / 英雄联盟 / 更多 / 退出程序。
2. `英雄联盟 >` 中当前 main 的 Dashboard / Player / Live / OP.GG Advisor / OP.GG Apply / Mayhem 都可打开。
3. `更多 >` 中主题、桌宠、更新、日志仍可达。
4. 控制中心首页不再平铺 4 个修复模式、2 个目录按钮、更新、日志、主题、海斗、退出等同权按钮。
5. `管理` 能打开自动识别 / 选择目录。
6. `修复工具` 能看到原 ToolA + 4 个模式。
7. `英雄联盟` 能打开与右键菜单相同的业务入口。
8. `个性化` 与 `更多设置` 二级菜单正常，不会点击后异常把控制中心直接关掉。
9. 从二级菜单打开现有 League 窗口时，焦点/窗口层级正常。
10. 控制中心版本显示应与程序版本 major.minor 一致，不再固定为 3.1。

## 未授权动作

在用户明确确认这套 Shell UX 前：

- PR #105 保持 Draft；
- 不合并；
- 不发布 Release / Tag；
- 不改 `online/version.json`；
- 不删除 task branch；
- 不把 Gate 3 #99 / PR #103 混入本 PR。
