# FACM 便携目录布局

FACM 3.1 默认使用程序目录保存运行文件，不再把新数据写入 `%LocalAppData%\FACM`。

首次启动后目录结构：

```text
FACM\
├─ FACM.exe
├─ FACM.ToolBundle.dll
├─ settings.ini
├─ ui-text.ini
├─ logs\
└─ runtime\
   ├─ FACM-Tool-A.exe
   ├─ FACM-Mode-Tool.exe
   ├─ FACM-Mode-1.cmd
   ├─ FACM-Mode-2.cmd
   ├─ FACM-Mode-3.cmd
   ├─ FACM-Mode-4.cmd
   └─ updates\
```

## 行为

- 启动时从 `FACM.exe` 的内嵌资源释放 `FACM.ToolBundle.dll` 到同一目录。
- DLL 与工具文件写入前后都会校验 SHA-256；发现旧文件或不匹配文件时会重新释放。
- 设置写入 `settings.ini`。
- 可自定义界面文字写入 `ui-text.ini`。
- 日志写入 `logs`。
- 工具和更新临时文件写入 `runtime`。
- 首次运行新版本时，会在便携配置不存在的情况下复制旧 `%LocalAppData%\FACM` 中的 `settings.ini` 与 `ui-text.ini`。

## 部署要求

整个 FACM 文件夹必须位于当前用户可写的目录，例如 `D:\FACM`。不要直接放入需要额外权限才能写入的系统目录。
