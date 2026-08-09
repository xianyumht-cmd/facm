# FACM 当前项目状态

> 2026-08-10：正式 3.1.1 保持稳定；Issue #33 / Draft PR #35 正在独立验证机器猫桌宠 Gate 1 第二版。

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

## 当前阶段

FACM 当前正式主阶段仍为 **基本完成 / 可暂停**。现有 3.1.1 是后续实验的稳定基线；Issue #33 的桌宠原型不改变正式版本和现有 VPet/PetHost 路线。

新任务继续按 `AGENTS.md`：从最新 `main` 开始，一任务一短分支 + PR；不要从旧本地快照或已合并任务分支继续开发。

## Issue #33 / PR #35：机器猫桌宠 Gate 1

- Issue：#33 `机器猫桌宠 Gate 1 原型（保留蜘蛛失败基线）`。
- Draft PR：#35 `Gate 1：独立机器猫桌宠原地动作原型`。
- 任务分支：`codex/machine-cat-gate1`。
- 原型路径：`prototypes/FACM.MachineCatPrototype/`。
- 当前仍不修改 `src/FACM`、`src/FACM.PetHost`，不替换 VPet，不进入自动漫游 Gate 2。

### Gate 1 第一版：视觉验收失败

第一版采用程序绘制的 WPF 矢量机器猫。它曾通过 Release build、deterministic self-test、真实 WPF window smoke、自包含 publish，核心 FACM Windows Build 也保持全绿；但用户随后用真实 Windows 录屏确认：

- 程序重画出来的扁平角色与已经认可的圆润 2.5D 机器猫外形差距明显；
- Walk / Run / Turn / Raised / Sleep 等动作有“纸片变形/程序图形在动”的感觉；
- 因此自动化成功不能视为 Gate 1 视觉成功。

第一版已明确判定 **Gate 1 失败**，没有进入 Gate 2，也没有合并 PR #35。

### Gate 1 第二版：当前进行中

第二版保留用户已经认可的机器猫视觉，不再由程序重新画角色：

- 从本轮已经认可的 Identity / Action Sheet 提取透明动作/视角素材；没有再次生成角色图片；
- 当前包含 Idle / Walk / Run / Observe / Raised / Recover / Sleep，以及 Turn 的正面 / 3⁄4 / 侧面 / 背面，共 11 个透明素材；
- 程序只负责状态、`deltaTime`、轻微 translate / scale / rotate、短时 crossfade、镜像换步和鼠标交互；
- Walk / Run 大部分周期保持单个清晰轮廓，仅在换步瞬间短暂交叉淡化；
- Turn 使用 `正面 → 3/4 → 侧面 → 背面 → 镜像侧面 → 镜像3/4 → 正面`，不再把一张正面图直接旋转或瞬间翻面；
- 原型资源暂以 `Assets/*.b64` 嵌入，启动时一次解码并缓存为冻结的 `BitmapImage`；正式集成前可再收口资源格式。

第二版仍必须经过：Release build → asset/self-test → 真实 WPF window smoke → win-x64 self-contained artifact → **用户真实 Windows 肉眼验收**。

只有用户明确确认第二版 Gate 1 的角色外形和原地动作合格，才能设计/实现 Gate 2 MotionController。CI 不能替代这一视觉门禁。
