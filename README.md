# FACM 3.5.x Lightweight

FACM 是面向 Windows 的轻量桌面悬浮控制中心。当前产品线固定为 **.NET Framework 4.8 + WinForms + 单 EXE**；FACM 4.x 已退出默认工作树，不再作为独立产品线维护。

## 当前状态

- 当前在线正式版：`3.5.25`（以 `online/version.json` 与 GitHub Release 为准）。
- 当前源码主线：3.5.x lightweight。
- 3.5.20 完成 P1 回灌与 4.x 工作树清理；3.5.21 修复更新清单旧缓存倒退；3.5.22 完成两轮 UI/交互升级；3.5.23 修复 LOL Hub 顶部布局遮挡；3.5.24 修复自绘开关重影；3.5.25 将共享窗口外壳收敛为视觉一体化无边框模式并移除 LOL Hub 顶部副标题层。
- 普通发布产物：单个 `FACM.exe`，CI 要求小于 10 MiB。
- `FACM.PetHost` 源码继续保留并 build/self-test，但普通 FACM.exe **不内嵌 self-contained PetHost bundle**。

## 主要能力

- 悬浮入口、托盘与紧凑控制中心；直接点击悬浮入口可按当前 LOL Gameflow 显示场景状态首页。
- 统一 WinForms 设计体系，Update Center、League Efficiency 与 Compact Launcher 共用主题/按钮/开关/状态语义。
- 普通顶层 WinForms 使用共享一体化无边框外壳：顶部交互区与页面同色，只保留品牌、拖动和窗口控制能力，不再显示独立标题栏层。
- 环境清理与内置工具资源。
- League Client 发现、概览/玩家/实时对局、推荐与一键应用、效率功能。
- Lobby 自动寻找、ReadyCheck 自动接受、赛后相关自动化。
- ChampSelect 紧凑助手与 Mayhem/海符攻略；保留 3.5 的快速缓存/网络链。
- 进入游戏时自动隐藏悬浮入口/桌宠，并按 ownership 在离开游戏后恢复。
- 桌面动物与可选 VPet PetHost。
- 公告、镜像、在线更新、SHA-256/发布校验与原子替换回滚。

League 自动化默认保持受控、去重和 best-effort；场景导航只消费现有共享 Gameflow 状态，不创建第二轮询器或第二 League session，不做游戏内注入或 Overlay。

## 仓库结构

```text
FACM.sln
src/FACM/             # WinForms/net48 主程序
src/FACM.ToolBundle/  # 内置工具资源 DLL
src/FACM.Updater/     # 3.5 单 EXE 更新替换器
src/FACM.PetHost/     # 可选桌宠运行时源码
online/               # 版本、公告、镜像
release/3.5-request.json
.github/workflows/    # 3.5 构建/发布/在线管理
scripts/              # 3.5 当前构建与签名辅助脚本
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
6. 验证 ToolBundle 已嵌入、PetHost ZIP 未嵌入、FACM.exe < 10 MiB。

GitHub Actions 主构建：**FACM Windows Build**。

## 发布与在线更新

正式 3.5 发布工作流：**FACM 3.5 Lightweight Release**（`.github/workflows/publish-3.5-lightweight.yml`）。

支持两种入口：

- Actions 手动 `workflow_dispatch`；
- 修改 `release/3.5-request.json` 并推送到 `main`。

发布工作流只接受尚未发布的新 3.5.x 版本号，构建并验证 lightweight FACM.exe，创建 GitHub Release，随后更新 `online/version.json`。清单只描述普通 3.5 更新，不包含 4.x migration 字段。旧的 heavyweight `release/request.json` / `publish-release.yml` 已退出产品线。

公告由 `online/announcement.json` 和 **FACM Online Management** 工作流维护。详细说明见 `docs/ONLINE-MANAGEMENT.md` 与 `docs/OPERATIONS.md`。

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

3.5.19 → 4.x 的历史审计只用于说明为什么保留/拒绝某些做法；当前实现事实以 `main` 源码、CI、Release 和 `online/version.json` 为准。
