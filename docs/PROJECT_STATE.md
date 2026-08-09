# FACM 当前项目状态

> 2026-08-09：FACM 3.1.1 正式主线保持稳定；Issue #33 已从蜘蛛方案整理推进为独立机器猫 Gate 1 原型，当前等待真实 Windows 视觉验收。

## 当前正式版

- 版本：FACM 3.1.1。
- GitHub Release：v3.1.1。
- 在线更新：已启用。
- `minimum_version=3.0.0`。
- `force_update=false`。
- 3.1.1 发布基础 main：`e53c45773b224e4d8f670f44381e394457fdf660`。
- 3.1.1 发布元数据提交：`04f7cbae702d6dd136ab278f72938cff2a8c26ef`。
- 3.1.1 在线更新启用提交：`de632e2832e6d227aa570082601b33ed8f99a0b9`。

## 已验证完成

- FACM 3.1 的发布前稳定性收口已完成：海斗多源容灾、当前平衡版本校验、控制中心首帧布局、桌宠 outside-click、PetHost 生命周期/流畅性、图片热缓存和清理流程后台化均已进入正式链路。
- 正式 Release 流程已完成签名、SHA-256、PetHost self-test/内嵌验证、disabled manifest → Release → enabled manifest 的事务式发布验证。
- 自签名证书 GitHub Secrets 已更新，普通 Build 与正式 Release 统一使用 `FACM_PFX_BASE64` / `FACM_PFX_PASSWORD`。
- 3.1.0 已正式发布；随后发布 3.1.1 作为纯在线更新验证版。
- 用户已在真实 Windows 环境中确认：现有 3.1.0 客户端能够自动检测、下载、校验、替换并重新启动到 3.1.1，在线更新链路实机验证成功。
- Issue #28（3.1.0 正式发布）与 Issue #31（3.1.1 在线更新验证）均已完成关闭。

## 当前正式架构状态

FACM 当前正式主线仍保持：

`FACM.exe (.NET Framework 4.8 / WinForms)` → `FACM.PetHost.exe (.NET 8 x64 / WPF / VPet Core)`。

本轮机器猫 Gate 1 **没有修改** `src/FACM`、`src/FACM.PetHost`、VPet 正式路线或 `FACM.sln`。现有 3.1.1 继续作为稳定基线。

新任务仍按 `AGENTS.md`：从最新 `main` 开始，一任务一短分支 + PR；不要从旧本地快照或已合并任务分支继续开发。

## 当前进行中：Issue #33 / PR #35 机器猫 Gate 1

- Issue #33：`机器猫桌宠 Gate 1 原型（保留蜘蛛失败基线）`。
- 任务分支：`codex/machine-cat-gate1`，从 `main` `877fd6706e12b3558ef1524de862ea648e189b2b` 创建。
- Draft PR：#35 `Gate 1：独立机器猫桌宠原地动作原型`。
- 原型路径：`prototypes/FACM.MachineCatPrototype/`。
- 独立验证工作流：`.github/workflows/machine-cat-prototype.yml`。

用户已确认当前机器猫角色外形方向，后续**不再为本阶段生成角色图片**。Gate 1 使用代码绘制的 WPF 分层矢量 Rig，只验证动作系统本身。

当前原型状态：

- 8 个原地状态：Idle / Walk / Run / Turn / Observe / Raised / Recover / Sleep；
- `Stopwatch + CompositionTarget.Rendering + deltaTime`，frame gap 最大按 50ms clamp；
- 状态输出连续身体、头部、四肢、眼神、铃铛和阴影参数，不使用固定 FPS Sprite 切帧；
- Walk / Run 使用不同步频与幅度；Turn 不靠瞬间镜像；Raised / Recover 有独立悬空和落地恢复运动；
- 点击与拖动有阈值区分；透明区域使用 Gate 1 级别的近似 `HTTRANSPARENT` 穿透；
- 当前不做自动漫游，不实现 Gate 2 MotionController。

### 已验证到的 CI 证据

在 PR #35 中，独立 `FACM Machine Cat Prototype` 工作流已经验证：

- Release build 成功，0 warning / 0 error；
- deterministic `--self-test` 成功；
- win-x64 self-contained publish 成功；
- artifact 上传成功；
- 后续提交继续增加真实 `--window-smoke-test`，要求实际创建透明 WPF 窗口并收到至少 3 帧 `CompositionTarget.Rendering` 后才通过。

上述自动化只能证明工程、动画数学和窗口运行链没有坏，**不能替代用户视觉验收**。

## Gate 1 当前完成条件

只有以下两层都完成，才允许进入 Gate 2：

1. 自动层：Release build、自检、真实 WPF window smoke、自包含 artifact 全部通过；
2. 人工层：用户在真实 Windows 桌面观察 8 个原地状态，明确确认动作自然、不是“图片在动/PPT 感”。

在 Gate 1 人工验收前：

- 不实现自动桌面漫游；
- 不接入 FACM 正式桌宠选择器；
- 不修改/替换 VPet；
- 不把 CI 绿色当作视觉效果通过。

## Gate 2 预定边界（尚未实现）

Gate 1 通过后才进入运动轨迹验证，并先用调试图形而不是正式角色验证：

- BehaviorController 只决定行为/目标，不直接改窗口坐标；
- MotionController 使用 position / velocity / desiredVelocity / acceleration / deceleration / heading / targetHeading / angularVelocity / arrival；
- Random 只用于行为或目标决策，禁止每帧随机位置/速度；
- 屏幕边缘不能使用 `vx = -vx` / `vy = -vy` 的弹球反射作为正式行为；
- `actualSpeed` 必须驱动 Walk / Run 与步频，静止时不能继续走路动作。
