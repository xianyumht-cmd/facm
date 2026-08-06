# FACM 界面文字配置

FACM 首次启动时会自动创建：

```text
%LocalAppData%\FACM\ui-text.ini
```

控制面板中的“界面文字”按钮会直接打开该文件。修改等号右侧文字并保存，重新启动 FACM 后生效。

可配置项目：

```ini
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
```

左侧键名不要修改。删除配置文件后，FACM 会在下次启动时恢复默认内容。
