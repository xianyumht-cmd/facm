# GGman 3.5.x Lightweight

GGman（鸡鸡侠）是面向 Windows 的轻量桌面悬浮控制中心。当前产品线固定为 **.NET Framework 4.8 + WinForms + 单 EXE**。仓库、解决方案、命名空间和部分兼容资源仍保留历史内部标识 `FACM`，这是有意的兼容策略，不代表对外产品名仍为 FACM。

## 当前状态

- 当前在线正式版：`3.5.40`（以 `online/version.json` 与 GitHub Release 为准）。
- 当前对外产品名：**GGman**。
- 当前正式发布产物：单个 `GGman.exe`，CI 要求小于 10 MiB。
- 当前源码主线：3.5.x lightweight。
- `FACM.PetHost`、`FACM.ToolBundle`、`FACM.Updater`、`FACM.sln`、`namespace FACM.*` 与 `FACM.Resources.*` 暂时作为内部兼容标识保留，不做全局重命名。
- `FACM.PetHost` 源码继续 build/self-test，但普通 `GGman.exe` **不内嵌 self-contained PetHost bundle**。

## 主要能力

- 悬浮入口、托盘与紧凑控制中心；直接点击悬浮入口可按当前 LOL Gameflow 显示场景状态首页。
- 统一 WinForms 设计体系，Update Center、League Efficiency 与 Compact Launcher 共用主题、按钮、开关和状态语义。
- 普通顶层 WinForms 使用共享一体化无边框外壳。
- 环境清理与内置工具资源。
- League Client 发现、概览/玩家/实时对局、推荐与一键应用、效率功能。
- Lobby 自动寻找、ReadyCheck 自动接受、赛后相关自动化。
- ChampSelect Runtime Companion、Mayhem/海符攻略、ARAM 数据与 Bench 辅助。
- 进入游戏时自动隐藏悬浮入口/桌宠，并按 ownership 在离开游戏后恢复。
- 桌面动物与可选 VPet PetHost。
- 公告、镜像、在线更新、SHA-256/签名校验与原子替换回滚。

League 自动化默认保持受控、去重和 best-effort；场景导航只消费现有共享 Gameflow 状态，不创建第二轮询器或第二 League session，不做游戏内注入或 Overlay。

## 品牌与兼容边界

自 `3.5.40` 起，对外品牌统一为 GGman：

- Windows 产品名、文件属性和用户可见界面：`GGman`
- 正式 Release 可执行文件：`GGman.exe`
- CI/本地打包产物：`GGman-Windows-x64.zip`
- 更新下载文件名和 updater User-Agent：GGman

以下内部技术标识继续保留，避免为了品牌改名破坏更新、资源加载、PetHost、ToolBundle、签名和历史兼容链路：

- 仓库：`xianyumht-cmd/facm`
- 解决方案：`FACM.sln`
- 主源码目录：`src/FACM/`
- 命名空间：`FACM.*`
- 嵌入资源逻辑名：`FACM.Resources.*`
- 内部组件：`FACM.ToolBundle`、`FACM.Updater`、`FACM.PetHost`
- 现有签名密钥/CI secret 名称

不要对这些内部标识做机械式全局 `FACM -> GGman` 替换。

## 仓库结构

```text
FACM.sln
src/FACM/             # GGman WinForms/net48 主程序；目录名保留兼容标识
src/FACM.ToolBundle/  # 内置工具资源 DLL
src/FACM.Updater/     # 3.5 单 EXE 更新替换器
src/FACM.PetHost/     # 可选桌宠运行时源码
online/               # 版本、公告、镜像
release/3.5-request.json
.github/workflows/    # 3.5 构建/发布/在线管理
scripts/              # 当前构建与签名辅助脚本
```

4.x 的 WinUI、Core/Infrastructure/Platform.Windows、bootstrapper/CAB、多版本运行时和 migration 链不属于当前产品。

## 构建

Windows 10/11 + Visual Studio 2022 Build Tools/.NET Framework 4.8 targeting pack + .NET 8 SDK：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

本地与 GitHub 的 lightweight 契约一致：

1. 校验 ToolBundle 输入。
2. build/self-test 可选 PetHost。
3. 不生成或嵌入 self-contained PetHost bundle。
4. 构建 `FACM.sln`。
5. 运行 host、League、性能、更新、悬浮球、桌宠、Mayhem 等 smoke tests。
6. 验证 ToolBundle 已嵌入、PetHost ZIP 未嵌入、`GGman.exe` < 10 MiB。

GitHub Actions 主构建：**GGman Windows Build**。

## 发布与在线更新

正式 3.5 发布工作流：**GGman 3.5 Lightweight Release**（`.github/workflows/publish-3.5-lightweight.yml`）。

支持两种入口：

- Actions 手动 `workflow_dispatch`；
- 修改 `release/3.5-request.json` 并推送到 `main`。

发布工作流只接受尚未发布的新 3.5.x 版本号，构建并验证 lightweight `GGman.exe`，完成签名、GitHub Release、公开产物哈希/签名复验后，才启用 `online/version.json` 在线更新。

当前正式版本 `3.5.40` 是首个完成 GGman 对外品牌统一的正式版本；内部 FACM 兼容标识不属于待清理残留，除非后续有独立迁移计划和完整兼容验证。

## 维护文档

```text
AGENTS.md
docs/PROJECT_STATE.md
docs/ARCHITECTURE.md
docs/DECISIONS.md
docs/PITFALLS.md
docs/OPERATIONS.md
docs/PERFORMANCE-CONTRACT.md
docs/3.5.19-4.0.6-BACKPORT-AUDIT.md
```

历史文档中的 FACM 名称用于描述当时版本，不需要为品牌迁移机械回写。当前产品事实以 `main` 源码、CI、Release 和 `online/version.json` 为准。
