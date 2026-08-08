# FACM 3.1

FACM 3.1 是 Windows 桌面悬浮控制中心，包含开发者配置的清理流程、经过完整性校验的内置工具资源、VPet Core 桌宠、联网版本更新和公告管理。

## 界面与运行

- 68×68 桌面悬浮入口，支持拖动、边缘吸附和托盘恢复。
- 深色控制中心，包含工作目录、清理预览、内置工具和操作日志。
- 悬浮球右键菜单提供完整工具入口、桌面宠物、海斗排行榜、在线中心、检查更新和退出。
- 普通模式与管理员模式分离，只在需要时请求管理员权限。

## 清理配置

开发者在编译前修改：

```text
src/FACM/Configuration/CleanupProfile.cs
```

未替换任意 `REPLACE_...` 占位值时，清理功能保持禁用。详细字段说明见：

```text
docs/DEVELOPER-CLEANUP-CONFIG.md
```

## 内置工具资源 DLL

当前 `tools/` 中的工具输入会在构建时写入：

```text
FACM.ToolBundle.dll
```

随后该 DLL 作为资源嵌入最终的单文件 `FACM.exe`。运行时 FACM 会：

1. 从自身资源中释放版本化的 `FACM.ToolBundle.dll`。
2. 校验释放前后 DLL 的 SHA-256 一致。
3. 动态加载资源 DLL。
4. 按用户选择释放对应工具文件。
5. 对每个工具再次校验固定 SHA-256 后再启动。

构建不会执行 `tools/` 中的任何文件。输入文件清单位于：

```text
tools/EXTRACTED-TOOLS.json
```

## VPet PetHost

高精度桌宠运行在独立的 .NET 8 x64 WPF 进程 `FACM.PetHost.exe` 中，但正式发布不要求用户额外下载 `PetHost/` 目录。

构建流程会先 publish 并 self-test PetHost，再把完整 publish 目录压缩为 `FACM.Resources.PetHost.zip` 嵌入 `FACM.exe`。第一次启用 VPet Core 时，FACM 会把与当前构建匹配的 PetHost 安全释放到：

```text
runtime\pethost-host\<FACM-MVID>\
```

因此正式下载包和旧版在线更新仍只需要一个 `FACM.exe`，升级后也不会缺少匹配版本的 PetHost。详细设计见：

```text
docs/VPET-PETHOST.md
docs/PORTABLE-LAYOUT.md
```

## 在线版本更新

程序读取：

```text
online/version.json
```

支持：

- 启动时自动检查并提示更新。
- 在线中心手动检查和手动更新。
- 下载进度显示。
- 更新文件 SHA-256 校验。
- 退出当前进程后替换 EXE 并重新启动。
- 最低版本限制和强制更新。

发布新版本使用：

```text
Actions → FACM Publish Release → Run workflow
```

发布工作流会：

1. publish/self-test PetHost 并嵌入 FACM；
2. 编译并签名 `FACM.exe`；
3. 先把版本元数据以 `enabled=false` 安全提交到 `main`；
4. 再从这个精确提交创建不可见的 draft Release，使版本 tag 指向实际发布源码；
5. 公开 GitHub Release；
6. 最后启用在线版本清单。

发布或最终清单更新中途失败时，客户端不会收到半发布更新。

## 联网公告

公告配置位于：

```text
online/announcement.json
```

后台修改入口：

```text
Actions → FACM Online Management → Run workflow
```

发布流程与公告管理共享同一个 `main` 写入串行锁，避免两个 Actions 同时直接推送 `main`。完整操作说明见：

```text
docs/ONLINE-MANAGEMENT.md
```

## 自动构建 EXE

源码提交到 `main` 后，GitHub Actions 会自动：

1. 校验 `tools/` 输入文件大小和 SHA-256。
2. 校验真实 `CleanupProfile.cs` 路径和配置状态。
3. publish 并 self-test win-x64 self-contained PetHost。
4. 把完整 PetHost bundle 嵌入 `FACM.exe`。
5. 使用 Windows Runner 和 .NET Framework 4.8 编译 Release。
6. 由构建后的 FACM 自己释放内嵌 PetHost，并再次启动 PetHost self-test。
7. 检查 PE、版本信息、ToolBundle 和 PetHost 资源。
8. 在配置证书 Secrets 时执行 Authenticode 签名。
9. 生成单 EXE 下载包、SHA-256、签名状态和构建信息。

手动构建：

```text
Actions → FACM Windows Build → Run workflow → main
```

成功后在运行页面底部下载：

```text
FACM-Windows-x64-运行编号
```

## 本地构建

系统要求：Windows 10/11、Visual Studio 2022 Build Tools 或 Visual Studio 2022、.NET Framework 4.8 targeting pack，以及 .NET 8 SDK。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

本地脚本与 CI 使用相同顺序：PetHost publish/self-test → 压缩 bundle → FACM 内嵌 → FACM smoke tests → 打包。

## 代码签名

GitHub Actions 自动签名使用：

- `FACM_PFX_BASE64`
- `FACM_PFX_PASSWORD`

证书与签名说明见：

```text
docs/SIGNING.md
```

## 构建产物

每次成功构建上传：

- `FACM-Windows-x64.zip`
- `FACM.exe`
- `SHA256.txt`
- `SIGNATURE.txt`
- `BUILD-INFO.json`

正式下载包不再要求外置 PetHost sidecar；`FACM.exe` 自身包含匹配的 PetHost bundle。构建产物默认保留 90 天。
