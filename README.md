# FACM 3.0

FACM 3.0 是一个 Windows 桌面悬浮控制中心。本次版本保留轻量悬浮入口与现有内置工具执行能力，重新设计了控制中心、清理预览和状态反馈，并新增开发者可配置的环境清理规则。

## 新界面

- 68×68 桌面悬浮入口，支持拖动、边缘吸附和托盘恢复。
- 重新设计的深色控制中心，统一圆角、层级、状态与交互反馈。
- 工作目录卡片支持自动识别和手动选择。
- 清理前显示独立预览窗口，列出完整路径、类别、状态和估算大小。
- 普通模式与管理员模式在界面中明确显示。

## 清理环境

清理规则不会由最终用户在界面中填写。开发者在编译前修改：

```text
src/FACM/Configuration/CleanupProfile.cs
```

未替换任何一个 `REPLACE_...` 占位值时，清理功能保持禁用，不会删除文件。

完成配置后，用户主动点击“清理环境”时，程序会：

1. 检查相关程序是否仍在运行。
2. 自动从当前进程和 Windows 卸载项读取安装位置；识别失败时打开系统文件夹选择器。
3. 扫描两个固定系统目录和开发者配置的安装目录规则。
4. 永久保留配置的保留文件夹，默认名称为 `DATA`。
5. 只匹配配置日志目录顶层的 `*.log`。
6. 展示所有精确路径并要求再次确认。
7. 重新校验路径后执行删除，并写入本地日志。

详细字段说明见 `docs/DEVELOPER-CLEANUP-CONFIG.md`。

## 路径与删除保护

- 不进行整盘关键词搜索。
- 动态路径必须保持在识别出的安装根目录内。
- 不进入 junction、符号链接或其他重解析点。
- 预览后、删除前再次核验每个目标所属规则。
- 相关进程未退出时拒绝清理。
- 系统目录只在用户主动确认后按需请求管理员权限。
- 日志保存在 `%LocalAppData%\FACM\Logs`。

## 内置工具

从旧版 `tools/FACM.exe` 中恢复出的原始资源保存在 `tools/`，并由 `tools/EXTRACTED-TOOLS.json` 记录文件大小和 SHA-256。自动构建与本地构建都会先校验这些文件，任何缺失或字节变化都会使构建失败。

资源不会在构建校验阶段执行。

## 自动构建 EXE

修改 `CleanupProfile.cs` 或其他源码并提交到 `main` 后，GitHub Actions 会自动：

1. 校验 `tools/` 中恢复出的文件完整性。
2. 检查清理配置是否仍包含 `REPLACE_...` 占位符；存在占位符时只警告，不阻止构建。
3. 使用 Windows Runner 和 .NET Framework 4.8 编译 Release 版本。
4. 检查生成文件的 PE 头、产品名称和版本信息。
5. 在配置证书 Secrets 时执行 Authenticode 签名。
6. 生成 EXE、ZIP、SHA-256、签名状态和构建信息。

下载方法：

1. 打开仓库的 **Actions** 页面。
2. 进入最新成功的 **FACM Windows Build**。
3. 在页面底部下载 `FACM-Windows-x64-运行编号`。
4. 压缩包内可直接找到 `FACM.exe`。

也可以在 Actions 页面手动运行 `FACM Windows Build`，无需再次修改代码。

## 本地构建

系统要求：Windows 10/11、Visual Studio 2022 Build Tools 或 Visual Studio 2022，并安装 .NET Framework 4.8 targeting pack。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

输出：

```text
artifacts\FACM.exe
FACM-Windows-x64.zip
```

## 代码签名

正式发布建议使用受信任机构签发的 Authenticode 代码签名证书：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sign-release.ps1 `
  -ExePath .\artifacts\FACM.exe `
  -PfxPath C:\secure\facm-signing.pfx `
  -PfxPassword "你的PFX密码"
```

GitHub Actions 自动签名使用以下仓库 Secrets：

- `FACM_PFX_BASE64`：PFX 文件的 Base64 内容。
- `FACM_PFX_PASSWORD`：PFX 密码；无密码时可留空。

自签名证书可验证签名流程，但通常不能消除 SmartScreen 的“未知发布者”，也不能保证不被安全软件告警。完整说明见 `docs/SIGNING.md`。

## 构建产物

每次成功构建都会上传：

- `FACM-Windows-x64.zip`
- `FACM.exe`
- `SHA256.txt`
- `SIGNATURE.txt`
- `BUILD-INFO.json`

Actions 构建产物保留 90 天。
