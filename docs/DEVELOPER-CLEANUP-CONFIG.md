# FACM 3.0 开发者清理配置

清理功能默认处于禁用状态。发布前只修改：

```text
src/FACM/Configuration/CleanupProfile.cs
```

只替换文件顶部“开发者只需要修改这一段”中的占位值。不要改动后方的路径验证与删除保护代码。

## 配置字段

| 字段 | 填写内容 |
|---|---|
| `ProgramFilesFolderName` | `C:\Program Files` 下需要处理的单级文件夹名 |
| `ProgramDataFolderName` | `C:\ProgramData` 下需要处理的单级文件夹名 |
| `GameRootMarkerFolderName` | 用来识别安装根目录的标记文件夹名 |
| `CleanupContainerRelativePath` | 安装根目录下的目标容器相对路径；其直接子项中只保留 `DATA` |
| `PreservedChildFolderName` | 固定保留目录，默认 `DATA` |
| `ExtraFolderRelativePath1` | 安装根目录下第一个额外目标文件夹相对路径 |
| `ExtraFolderRelativePath2` | 安装根目录下第二个额外目标文件夹相对路径 |
| `LogFolderRelativePath` | 顶层 `.log` 所在文件夹的相对路径 |
| `RegistryDisplayNameKeyword` | Windows 卸载项显示名称中用于自动识别目录的关键词 |
| `RelatedProcessNames` | 清理前必须退出的进程名，不写 `.exe` |

## 填写示例

下面只演示格式，不代表真实路径：

```csharp
public const string ProgramFilesFolderName = "VendorRuntime";
public const string ProgramDataFolderName = "VendorRuntime";
public const string GameRootMarkerFolderName = "MarkerFolder";
public const string CleanupContainerRelativePath = @"Runtime\Content";
public const string PreservedChildFolderName = "DATA";
public const string ExtraFolderRelativePath1 = @"Runtime\CacheA";
public const string ExtraFolderRelativePath2 = @"Runtime\CacheB";
public const string LogFolderRelativePath = @"Runtime\Logs";
public const string RegistryDisplayNameKeyword = "Product Display Name";
public static readonly string[] RelatedProcessNames =
{
    "ProductClient",
    "ProductLauncher"
};
```

## 实际清理规则

点击“清理环境”后，FACM 会先自动识别或要求用户选择目录，再生成以下精确候选项：

1. `%ProgramFiles%\<ProgramFilesFolderName>`
2. `%ProgramData%\<ProgramDataFolderName>`
3. `<安装根目录>\<CleanupContainerRelativePath>` 的直接子文件和直接子文件夹，但永久跳过 `<PreservedChildFolderName>`
4. 两个 `ExtraFolderRelativePath` 指向的文件夹
5. `LogFolderRelativePath` 顶层的 `*.log`

程序不会进行全盘关键词搜索，也不会根据相似名称删除其他位置。

## 强制保护

- 任何占位符未替换时，删除功能不会启用。
- 所有相对路径必须保持在识别出的安装根目录内。
- 删除前展示每个完整路径、文件数、文件夹数和估算大小。
- 用户必须在预览窗口中再次确认。
- `DATA` 或开发者指定的保留目录不会加入删除列表。
- 遇到 junction、符号链接或其他重解析点时阻止该目标。
- 相关进程仍在运行时拒绝清理。
- 删除前再次验证目标与规则，避免预览后路径被替换。
- 系统目录操作仅在用户确认后按需请求管理员权限。
- 每次成功、失败与阻止结果写入 `%LocalAppData%\FACM\Logs`。

## 编译

完成配置后，在仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

输出文件：

```text
artifacts\FACM.exe
```
