# 开发者清理配置

最终用户不会在界面中输入目标文件夹名。发布者在编译前修改：

```text
src/FACM/Configuration/CleanupProfile.cs
```

只需要替换：

```csharp
public const string TargetFolderName = "REPLACE_WITH_TARGET_FOLDER_NAME";
```

填写单级文件夹名称，不要填写完整路径，也不要包含 `\`、`/`、`.` 或 `..`。

如果仍保留占位符、填写空值、非法字符或受保护目录名称，FACM 会在扫描阶段拒绝继续，不会执行任何删除。

## 扫描规则

程序只生成以下精确候选项：

1. `%ProgramFiles%\<TargetFolderName>`
2. `%ProgramData%\<TargetFolderName>`
3. `<用户选择目录>\Launcher\<TargetFolderName>`
4. `<用户选择目录>\LeagueClient\<TargetFolderName>`
5. `<用户选择目录>\Game` 下除 `DATA` 外的直接子文件和直接子目录
6. `<用户选择目录>\LeagueClient` 下的顶层 `*.log` 文件

不会对整块磁盘或所选目录进行模糊名称搜索。

## 删除保护

- 每个候选项都会先显示在扫描结果中；
- 用户必须在确认框中核对所有完整路径；
- 删除前会再次验证路径、规则和项目类型；
- `Game\DATA` 始终保留；
- 不递归进入 junction、符号链接或其他 reparse point；
- 所选目录不能是盘符根目录或 Windows、Program Files、ProgramData 根目录；
- 运行中的相关程序会阻止清理流程；
- 日志保存在 `%LocalAppData%\FACM\Logs`。
