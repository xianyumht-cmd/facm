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

当前仓库中已有的内置可执行资源和四个模式脚本继续保留。资源释放到固定的本地目录，运行前校验固定 SHA-256；校验失败时停止执行。

其他未出现在仓库中的原始二进制无法凭空恢复。后续加入额外内置工具时，应先确认发布权、单独签名，再采用固定资源名和固定哈希嵌入。

## 构建

系统要求：Windows 10/11、Visual Studio 2022 Build Tools 或 Visual Studio 2022，并安装 .NET Framework 4.8 targeting pack。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

输出：

```text
artifacts\FACM.exe
```

## 代码签名

正式发布建议使用受信任机构签发的 Authenticode 代码签名证书：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sign-release.ps1 `
  -ExePath .\artifacts\FACM.exe `
  -PfxPath C:\secure\facm-signing.pfx `
  -PfxPassword "你的PFX密码"
```

自签名证书可验证签名流程，但通常不能消除 SmartScreen 的“未知发布者”，也不能保证不被安全软件告警。完整说明见 `docs/SIGNING.md`。

## 构建产物

GitHub Actions 会生成：

- `FACM-3.0-windows-x64.zip`
- `FACM.exe`
- `SHA256.txt`
- `SIGNATURE.txt`

当仓库配置正式证书 Secrets 后，流水线会在打包前签名并验证主程序。
