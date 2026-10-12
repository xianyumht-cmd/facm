# FACM 界面文字配置

FACM 会在程序目录自动创建：

```text
FACM\ui-text.ini
```

旧版本如果已经有 `%LocalAppData%\FACM\ui-text.ini`，首次迁移时会继续沿用；之后以 `FACM.exe` 同目录的 `ui-text.ini` 为准。

保存 `ui-text.ini` 后，FACM 运行中会自动重新读取，通常几百毫秒内就能看到变化，不需要重新编译。

## 1. 常用文字

`[Text]` 区域直接修改等号右侧：

```ini
[Text]
AppName=FACM
ControlCenter=控制中心
Cleanup=清理环境
ToolGroup=快捷工具
ToolA=工具 A
Mode1=模式 1
Mode2=模式 2
Mode3=模式 3
Mode4=模式 4
CheckUpdate=检查更新
OpenLog=操作日志
About=程序信息
EditText=界面文字
Exit=退出程序
PanelTheme=面板主题
ThemeSettings=主题设置
DesktopPet=桌面宠物
PetReset=宠物复位
RestoreFloatingBall=恢复默认悬浮球
MayhemRanking=海斗排行榜
WorkDirectory=工作目录
AutoDetect=自动识别
SelectDirectory=选择目录
RulesConfigured=规则已配置
WaitingConfiguration=等待配置
CleanupHint=先预览路径，再确认执行
StartCleanup=开始清理
UpdateAndAnnouncements=更新与公告
AutoCheckAtStartup=启动时自动检查
Ready=准备就绪
Administrator=管理员
StandardMode=标准模式
Close=关闭
ApplyPet=应用桌宠
PetSource=来源
Open=打开
```

程序升级后如果增加了新的常用键，FACM 会自动补到现有文件末尾，不会覆盖已经修改过的值。

## 2. 任意界面文字全局替换

没有单独键的文字直接写进 `[Replace]`：

```ini
[Replace]
原文=新文
```

它既可以替换整句，也可以替换关键词。例如：

```ini
[Replace]
FACM=我的程序
VPet Core=高精度桌宠
面向开发者=自定义文字
```

这层替换会应用到 FACM 窗口标题、标签、按钮、菜单、托盘菜单、列表显示文字、提示文字、普通弹窗和 PetHost 自己的加载状态文字。这样以后遇到新的硬编码界面文字，也可以直接在 `ui-text.ini` 里覆盖，不需要再改源码。

需要换行时写 `\n`；要显示反斜杠写 `\\`。把某项右侧留空，可以把对应文字隐藏。

用户输入框、查询结果数据、在线公告正文以及第三方资源自身的数据内容不会当成固定 UI 文案强行替换，避免误改用户输入和业务数据。
