# FACM 3.4.2

FACM 是面向 Windows 的桌面悬浮控制中心。当前正式版为 **3.4.2**，已启用在线更新，最低兼容版本为 **3.0.0**，当前不是强制更新。

FACM 目前包含：桌面悬浮入口、控制中心、开发者配置的清理流程、经过完整性校验的内置工具资源、VPet Core 桌宠、桌面动物、联网公告与版本更新，以及已经收口到单一入口的英雄联盟工具中心。

## 当前正式版

- 版本：`3.4.2`
- Release：`v3.4.2`
- 在线更新：`enabled=true`
- minimum_version：`3.0.0`
- force_update：`false`
- Release FACM.exe SHA-256：`B0F31DA0F158301507EFA6567F3115CF3893B34FD07717508E5743A2FF1FF5D1`
- 正式下载：`https://github.com/xianyumht-cmd/facm/releases/download/v3.4.2/FACM.exe`

3.4.2 继续修复英雄联盟推荐中心的一键应用：符文不再每次都新建一个 FACM 自定义页，而是优先复用同名 `[FACM]` 页；自定义符文页容量已满时，也只复用 FACM 自有页，不覆盖普通用户符文页。同时补齐符文与召唤师技能的一键应用实机日志。3.4.1 已加入的游戏内全局快捷键修复继续保留。

当前生产状态以以下文件为准：

```text
online/version.json
docs/PROJECT_STATE.md
```

## 界面与运行

- 68×68 桌面悬浮入口，支持拖动、边缘吸附和托盘恢复。
- 深色控制中心，包含工作目录、清理预览、内置工具、英雄联盟入口和操作日志。
- 悬浮球与托盘菜单提供主要功能入口、桌面宠物、在线中心、检查更新和退出。
- 普通模式与管理员模式分离，只在需要时请求管理员权限。
- 主程序仍以单 EXE 形式分发，匹配版本的 PetHost 资源内嵌在 `FACM.exe` 中。

## 英雄联盟中心

英雄联盟功能已经收口到单一 `英雄联盟` 入口，进入统一的 `英雄联盟中心`，而不是把各个 League 工具散落在多个 Shell 菜单中。

当前中心按用户概念组织为：

- **对局**：概览、玩家主页、实时对局、海斗相关信息。
- **推荐**：统一推荐中心，集中展示并应用符文、召唤师技能和推荐装备集。
- **效率**：游戏 / 大厅快捷操作与自动化能力。

### OP.GG / FACM 推荐

推荐链复用同一套 League Client session 与已经验收的 service / writer 边界：

- 读取 OP.GG 推荐数据并展示当前英雄、模式、位置与推荐内容。
- 手动应用符文与召唤师技能前要求用户确认。
- 符文应用会读回验证，LCU 返回 2xx 不等于直接判定成功。
- 优先复用同名 `[FACM]` 自有符文页；容量满时只允许复用 FACM 自有页，不覆盖普通用户符文页。
- 推荐装备集写入 League 的 `Recommended` 目录，并保持 FACM 自有文件边界。
- 自动应用默认关闭，保持 exact-once / 上下文校验语义。

### 游戏效率与自动化

- `一键退出游戏`：结束国服 League 游戏进程。
- `一键关闭大厅`：关闭 LeagueClient / LeagueClientUx / LeagueClientUxRender。
- 全局快捷键不依赖先打开 FACM 面板；3.4.1 起补强后台触发链。
- 赛后自动化：随机点赞一个 eligible teammate，并可自动返回大厅。
- 自动下一局：支持自动寻找对局与自动接受 ReadyCheck。
- League 自动化默认关闭，不做游戏内 Overlay / 注入，也不做自动 pick / ban / swap / reroll / dodge / skin。

League 当前状态、性能边界与已验收 Gate 详见：

```text
docs/PROJECT_STATE.md
docs/ARCHITECTURE.md
docs/PERFORMANCE-CONTRACT.md
```

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

## VPet PetHost 与桌面宠物

高精度桌宠运行在独立的 .NET 8 x64 WPF 进程 `FACM.PetHost.exe` 中，但正式发布不要求用户额外下载 `PetHost/` 目录。

构建流程会先 publish 并 self-test PetHost，再把完整 publish 目录压缩为 `FACM.Resources.PetHost.zip` 嵌入 `FACM.exe`。第一次启用 VPet Core 时，FACM 会把与当前构建匹配的 PetHost 安全释放到：

```text
runtime\pethost-host\<FACM-MVID>\
```

因此正式下载包和旧版在线更新仍只需要一个 `FACM.exe`。桌面动物与相关资源说明见：

```text
docs/DESKTOP-PETS-AND-MAYHEM.md
docs/ANIMAL-PET-ASSETS.md
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

### 发布新版本

当前发布工作流支持两种入口：

1. 修改 `release/request.json` 并合并到 `main`，`FACM Publish Release` 会由该文件的 main push 自动触发；
2. 在 GitHub Actions 手动运行 `FACM Publish Release` 并填写版本、最低版本、强制更新、预发布状态与更新说明。

发布工作流会：

1. 解析发布请求并应用版本号；
2. publish/self-test PetHost 并嵌入 FACM；
3. 编译并签名 `FACM.exe`；
4. 先把版本元数据与 `enabled=false` 清单安全提交到 `main`；
5. 从精确发布提交创建并公开 GitHub Release；
6. 最后把 `online/version.json` 更新为 `enabled=true`。

发布或最终清单更新中途失败时，客户端不会收到半发布更新。

完整操作说明见：

```text
docs/ONLINE-MANAGEMENT.md
docs/OPERATIONS.md
```

## 联网公告

公告配置位于：

```text
online/announcement.json
```

后台修改入口：

```text
Actions → FACM Online Management → Run workflow
```

发布流程与公告管理共享同一个 `main` 写入串行锁，避免两个 Actions 同时直接推送 `main`。

## 自动构建 EXE

源码提交到 `main` 后，GitHub Actions 会自动执行 Windows 构建与契约测试。主流程包括：

1. 校验 `tools/` 输入文件大小和 SHA-256。
2. 校验真实 `CleanupProfile.cs` 路径和配置状态。
3. publish 并 self-test win-x64 self-contained PetHost。
4. 把完整 PetHost bundle 嵌入 `FACM.exe`。
5. 使用 Windows Runner 和 .NET Framework 4.8 编译 Release。
6. 运行 FACM 自身的 modular host / performance / League / floating ball 等 smoke tests。
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

每次成功构建会上传单 EXE 包及校验/构建信息，包括：

- `FACM-Windows-x64.zip`
- `FACM.exe`
- `SHA256.txt`
- `SIGNATURE.txt`
- `BUILD-INFO.json`

正式下载包不要求外置 PetHost sidecar；`FACM.exe` 自身包含匹配的 PetHost bundle。Actions 构建产物默认保留 90 天。

## 项目文档

面向维护和后续 AI 接管的核心文档：

```text
AGENTS.md
docs/PROJECT_STATE.md
docs/ARCHITECTURE.md
docs/DECISIONS.md
docs/PITFALLS.md
docs/OPERATIONS.md
docs/AI_WORKSTYLE.md
```

其中 `online/version.json` 与 GitHub Release 是当前生产版本的最终事实来源；`docs/PROJECT_STATE.md` 用于记录当前已验证状态、未完成任务和后续动作。
