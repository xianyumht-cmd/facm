# FACM 2.0 清理规格

## 固定目标

- `%ProgramFiles%\AntiCheatExpert`
- `%ProgramData%\AntiCheatExpert`

## 用户选择游戏目录后的附加目标

- `<selected>\AntiCheatExpert`
- `<selected>\Game\AntiCheatExpert`

不会进行包含关键字的全盘模糊搜索，也不会删除名称类似但不完全匹配的目录。

## 安全约束

- 用户选择的路径不能是盘符根目录、Windows、Program Files、Program Files (x86) 或 ProgramData 根目录。
- 动态生成的目标必须保持在用户选择目录之内。
- 最终叶子目录必须精确等于 `AntiCheatExpert`。
- 遇到 reparse point、junction 或 symbolic link 时停止并标记为跳过。
- 检测到相关游戏/组件进程运行时，整个删除流程不启动。
- 不停止服务、不结束进程、不修改注册表中的第三方项目。
- 删除是永久删除，确认框必须展示所有完整路径。

## 日志

日志只记录：启动/退出、签名状态、扫描目标及结果、删除结果与异常。不会上传日志。
