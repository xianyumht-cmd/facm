# FACM UI Text Contract

FACM 的用户可见静态文案使用稳定 TextKey 管理。

## 用户侧规则

- `ui-text.ini` 的 `[Text]` 是正式配置入口。
- Key 保持稳定；默认中文可以随产品调整，但不能因为中文改名就重命名 Key。
- 新版本增加 Key 时，FACM 自动补到现有配置，不覆盖用户已经设置的值。
- 程序始终保留完整默认中文，配置缺项时仍可正常显示。
- `[Replace]` 继续保留，用于历史配置和全局替换，但不再承担新功能的主要文字管理职责。
- 配置保存后运行时自动重新读取；FACM 自有临时菜单在每次打开时重新应用当前配置。

## 开发侧规则

新增用户可见静态文案必须：

1. 在 `UiTextKeys` 注册稳定 Key；
2. 在 `UiTextCatalog` 注册默认中文；
3. 从 `UiTextRuntime.Text(UiTextKeys.Xxx)` 取显示值；
4. 临时菜单经过 `UiTextRuntime.Apply`；
5. 自绘文字显式走 TextKey，因为普通控件树不能自动接管 `TextRenderer` / `Graphics.DrawString`；
6. 不使用 `[Replace]` 掩盖新功能的硬编码 UI。

不要求 TextKey 管控用户输入、查询结果、游戏/服务器动态数据、在线公告正文、日志、内部异常、调试信息、测试断言和第三方数据。

## 自动门禁

`.github/workflows/ui-text-contract.yml` 在 PR 合入 `main` 前运行 `scripts/check-ui-text-contract.ps1`。

门禁检查本次变更新增的 UI 源码行。Form、Menu、Window、Picker、Renderer 等界面代码若新增静态显示文字而没有走 TextKey，会直接失败。确实不是用户可见文案的特殊情况需要显式标记例外。

这样后续新增玩家主页、战绩、客户端状态、工具中心、新桌宠或其他功能时，静态 UI 文案从进入代码的第一天起就是可配置、可追踪和可回归验证的。

## Issue #70 首批覆盖

本轮已经把以下高风险入口纳入契约：

- 主题临时菜单：面板外观、桌面形态、FACM 悬浮入口、桌宠选择、桌面位置复位；
- FACM 自有 `ContextMenuStrip`：每次打开重新应用文字配置；
- 桌宠选择器：窗口/标题/提示、当前状态、运行类型、交互说明、宠物名称、摘要、行为说明、描述和 VPet 预览文案；
- 既有 `[Text] / [Replace]` 读取、热重载和自动补 Key 行为保持兼容。
