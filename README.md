# GGman 3.5.x Lightweight

GGman（鸡鸡侠）是面向 Windows 的轻量桌面悬浮控制中心。当前产品线固定为 **.NET Framework 4.8 + WinForms + 单 EXE**。仓库、解决方案、命名空间和部分兼容资源仍保留历史内部标识 `FACM`，这是有意的兼容策略，不代表对外产品名仍为 FACM。

## 当前状态

- 当前在线正式版：**`3.5.71`**（2026-10-11 核对；长期以 [`online/version.json`](online/version.json) 和 [GitHub Releases](https://github.com/xianyumht-cmd/facm/releases) 为准）。
- 当前对外产品名：**GGman**。
- 正式主程序为单个 `GGman.exe`（小于 10 MiB）；Release 同时提供内容相同的 `FACM.exe`，用于兼容旧版一键更新。
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
- ChampSelect Runtime Companion、Mayhem（海克斯大乱斗）攻略、ARAM 数据与 Bench 辅助。
- 进入游戏时自动隐藏悬浮入口/桌宠，并按 ownership 在离开游戏后恢复。
- 桌面动物与可选 VPet PetHost。
- 公告、镜像、在线更新、SHA-256/签名校验与原子替换回滚。

League 自动化默认保持受控、去重和 best-effort；场景导航只消费现有共享 Gameflow 状态，不创建第二轮询器或第二 League session，不做游戏内注入或 Overlay。

## 我的 GGman：注册账号与配置同步

- 登录入口：**LOL 工作台 → 我的 GGman**。邮箱账号是可选功能；软件本地能力不依赖登录。
- 云端配置中心只保留默认勾选的 **自动同步配置**。登录邮箱后后台检查变更，无需打开页面，也无需逐项手动上传/恢复。
- 同步范围：GGman 可携带的软件偏好、`ui-text.ini` 文字自定义和 LOL `Game/Config` 中允许备份的游戏内 ESC 设置；三类配置分别维护云端版本。
- 仅变更时同步；首次关联本地已有自定义内容、账号切换或双方同时修改导致冲突时，会暂停冲突项并提示 **保留本机配置 / 采用云端配置**，不会静默覆盖。
- 游戏安装路径、窗口位置、登录凭据、个人统计/隐私授权等不随此功能同步；`LeagueClient/Config` 大厅 YAML 暂未纳入。
- 勾选框设置仅在当前设备生效；取消勾选会停止后续自动同步。登录凭据不落盘，重新启动 GGman 后需要再次登录邮箱。

**验收状态：** 自动同步从 3.5.67 上线，后续 3.5.69、3.5.70 已分别修复账号令牌续期与同步异常恢复，并经 Windows CI、正式签名发布。实际双设备同步、真实账号切换、令牌过期恢复及运行中的 ESC 文件恢复仍需真实用户环境验收，不能仅凭 CI 视为已通过。详见 [`docs/CLOUD-SETTINGS-SYNC.md`](docs/CLOUD-SETTINGS-SYNC.md)、[`docs/PROJECT_STATE.md`](docs/PROJECT_STATE.md)。

## 海克斯大乱斗排行榜与数据源

- 榜单：Hexdata（国内英雄统计）、ARAMGG 和 ARAMMayhem 作为不同的公开获取渠道。优先使用**补丁较新且前十名完整**的单一来源；名次必须连续、英雄不能重复，不能拼接不同网站的排名和胜率。国内来源未必总是最新。
- 攻略：OP.GG 继续提供英雄出装、强化等补充信息；腾讯官网公告用于独立核对国服补丁/平衡信息，**不是英雄胜率排行榜接口**。
- 版本：识别同一补丁的 `16.x` / `26.x` 表示法。榜单来源和补丁会在结果中标注；缺失或与官网不一致时不把它冒充为已验证的国服最新榜。
- 监测：GitHub Actions 的 [FACM Mayhem Source Probe](.github/workflows/mayhem-source-probe.yml) 每六小时运行一次，分别报告来源补丁、前十名数量、完整性和官网补丁核对结果。少于两个健康来源时给出降级警告；最终攻略链本身不完整则保留失败判定。
- 已知限制（2026-10-11 实测）：Hexdata 未返回可解析的前十名（0/10）；ARAMGG 与 ARAMMayhem 都返回 26.20 的完整前十（各 10/10）；腾讯官网版本核对未成功，显示 `unknown`。两个网站可用不代表两份相互独立的比赛统计样本。Blitz 尚未确认有当前补丁的可用榜单，因此未加入自动切换。

维护排查和判断标准见 [运营与监测手册](docs/OPERATIONS.md#海斗榜来源健康监测)；最新验证状态见 [PROJECT_STATE](docs/PROJECT_STATE.md)。用户使用现有 3.5.71 即可，监测调整无需发布新的 EXE。

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

`3.5.40` 是首个完成 GGman 对外品牌统一的正式版本；**当前在线版本以 `online/version.json` 为准（本次文档收尾时为 `3.5.71`）**。内部 FACM 兼容标识不属于待清理残留，除非后续有独立迁移计划和完整兼容验证。

## 维护文档

```text
AGENTS.md
docs/PROJECT_STATE.md
docs/CLOUD-SETTINGS-SYNC.md
docs/ARCHITECTURE.md
docs/DECISIONS.md
docs/PITFALLS.md
docs/OPERATIONS.md
docs/PERFORMANCE-CONTRACT.md
docs/3.5.19-4.0.6-BACKPORT-AUDIT.md
```

历史文档中的 FACM 名称用于描述当时版本，不需要为品牌迁移机械回写。当前产品事实以 `main` 源码、CI、Release 和 `online/version.json` 为准。
