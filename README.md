# FACM 2.1 悬浮球版

FACM 是一个轻量 Windows 桌面悬浮球。程序启动后只显示约 64×64 的悬浮球，不打开传统大窗口。

## 交互

- 单击悬浮球：展开紧凑功能菜单
- 拖动悬浮球：移动位置；松手后自动贴近屏幕左右边缘
- 右击悬浮球：展开、打开日志或退出
- 双击托盘图标：重新显示悬浮球
- 位置与游戏目录保存在 `%LocalAppData%\FACM\settings.ini`

## 功能

- 从正在运行的客户端和常见卸载/WeGame 注册表位置识别游戏目录
- 也可以通过系统文件夹选择器手动选择游戏根目录
- 安全清理 `LeagueClient` 顶层 `.log` 文件和 FACM 自身临时文件
- 内置原 FACM 使用的 `Fix-LCU-Window.exe` 1.1.2，并提供四种运行模式
- 内置工具释放前固定校验 SHA-256，校验失败不会执行
- 本地日志保存在 `%LocalAppData%\FACM\Logs`

## 安全边界

本版本不包含或执行删除驱动、安全程序、反作弊组件的工具，也不批量删除 `Game` 目录。此类行为既容易破坏安装，也会显著提高安全软件告警概率。

FACM 不联网、不注入进程、不创建服务、不设置开机启动，不隐藏执行命令。

## 构建与签名

Windows 10/11，Visual Studio 2022 Build Tools 或 Visual Studio 2022，安装 .NET Framework 4.8 targeting pack：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

正式发布应使用受信任机构颁发的代码签名证书：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sign-release.ps1 `
  -ExePath .\artifacts\FACM.exe `
  -PfxPath C:\secure\facm-signing.pfx
```

自签名证书通常不会消除 SmartScreen 的“未知发布者”提示，也不能保证安全软件不报毒。
