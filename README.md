# FACM 2.0

FACM 是一个透明、可审计的 Windows 残留清理工具。本次重写不复用旧版二进制代码，也不捆绑任何第三方可执行文件。

## 当前功能

- 扫描并清理以下固定目录：
  - `C:\Program Files\AntiCheatExpert`
  - `C:\ProgramData\AntiCheatExpert`
- 用户可选择游戏安装目录，FACM 仅追加扫描：
  - `<游戏目录>\AntiCheatExpert`
  - `<游戏目录>\Game\AntiCheatExpert`
- 扫描与删除分离：先列出路径、文件数和估算大小，再由用户二次确认。
- 相关游戏或组件进程运行时拒绝清理，不强制结束进程。
- 拒绝递归进入目录链接、符号链接与其他重解析点。
- 操作日志保存在 `%LocalAppData%\FACM\Logs`。

## 明确不包含

- 网络请求、自动更新、远程配置或遥测
- 下载并执行文件
- 驱动安装、服务创建或服务删除
- 进程注入、进程内存修改或强制结束进程
- 开机自启、计划任务或隐藏命令行
- 捆绑第三方 EXE/DLL

## 构建

系统要求：Windows 10/11、Visual Studio 2022 Build Tools 或 Visual Studio 2022，安装 .NET Framework 4.8 targeting pack。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

输出：`artifacts\FACM.exe`。

## 数字签名

没有受信任的证书时，构建结果保持未签名。不要把自签名证书误认为 SmartScreen 信誉。

使用正式代码签名证书：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sign-release.ps1 `
  -ExePath .\artifacts\FACM.exe `
  -PfxPath C:\secure\facm-signing.pfx
```

GitHub Actions 也支持以下仓库 Secrets：

- `FACM_PFX_BASE64`：PFX 文件的 Base64 内容
- `FACM_PFX_PASSWORD`：PFX 密码

私钥和 PFX 文件不得提交到仓库。

## 安全说明

该功能定位为卸载或修复后的残留清理。清理后，游戏可能在下次启动时重新下载相关组件。FACM 不用于绕过运行中的保护程序。
