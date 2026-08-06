# FACM Desktop Rewrite

FACM 的 Windows 桌面重写版，目标是：

- 使用原生 .NET 8 WPF，适配 Windows 10/11；
- 重新设计界面、交互、状态提示和运行日志；
- 保留“内置工具按用户选择释放并执行”的能力；
- 使用固定、可审计的释放目录、SHA-256 校验和明确的用户确认；
- 提供标准发布与 Authenticode 签名脚本，避免压缩壳、随机临时文件和隐藏执行。

## 当前分支范围

当前代码提供可编译的新版骨架、现代 UI、工具释放/校验/执行框架、应用自身缓存清理、构建脚本和签名脚本。

环境维护功能只处理 FACM 自身创建的缓存、日志和临时目录，不删除或停用第三方保护/安全组件，也不会对外部程序目录执行大范围删除。

## 准备内置工具

把原程序中需要保留的 `.exe`、`.bat` 或 `.cmd` 放入：

```text
src/FACM.App/Payloads/
```

然后编辑：

```text
src/FACM.App/Payloads/payloads.manifest.json
```

为每个文件填写：

- `id`：界面内部标识；
- `displayName`：界面名称；
- `fileName`：文件名；
- `sha256`：文件 SHA-256；
- `arguments`：固定启动参数；
- `requiresElevation`：是否需要按需提权。

生成哈希：

```powershell
Get-FileHash .\src\FACM.App\Payloads\文件名.exe -Algorithm SHA256
```

## 本地构建

安装 .NET 8 SDK 后运行：

```powershell
.\scripts\build-release.ps1
```

默认生成框架依赖、非单文件发布包：

```text
artifacts\win-x64\
```

这种发布方式不会在运行时自解压整个应用，行为比单文件自解压和加壳更透明。

## 代码签名

安装 Windows SDK，并准备正式代码签名证书。然后运行：

```powershell
.\scripts\sign-release.ps1 `
  -InputDirectory .\artifacts\win-x64 `
  -PfxPath C:\secure\facm-signing.pfx
```

证书密码通过安全提示输入，不写入仓库。脚本使用 SHA-256 文件摘要和 RFC 3161 时间戳，并在签名后执行验证。

## 项目结构

```text
src/FACM.App/                  WPF 主程序
src/FACM.App/Payloads/         待嵌入的原有工具
src/FACM.App/Services/         工具释放、执行和安全维护逻辑
scripts/build-release.ps1      一键构建
scripts/sign-release.ps1       一键签名与验证
docs/SIGNING.md                签名与误报治理说明
```
