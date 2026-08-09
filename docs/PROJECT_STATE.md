# FACM 当前项目状态

> 2026-08-10：正式 3.1.1 保持稳定；Issue #33 / Draft PR #35 正在独立验证机器猫桌宠 Gate 1 第四版。

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

### Gate 1 第二版：外形恢复，但整图 crossfade 失败

第二版改为直接使用用户已经认可的透明机器猫动作/视角素材，角色视觉方向恢复正确；但真实 Windows 录屏确认：

- Walk / Run 左右换步时，完整角色原图与镜像图 alpha-crossfade 会同时出现；
- Turn 多视角交接同样会出现明显的“双机器猫/鬼影”；
- 因此整图 crossfade 被明确淘汰。

### Gate 1 第三版：鬼影解决，但整图步态仍不够真实

第三版禁止完整角色 alpha 叠加，任意时刻只显示一只机器猫；Walk / Run 改为单图镜像换步并在换面附近做极短水平收窄，Turn 改为单图多视角切换。

用户随后提交两段真实 Windows 录屏，结论：

- Walk / Run / Turn 的半透明双影已经消失，外形仍保持认可的 2.5D 机器猫；
- 但 Walk / Run 本质仍是一张完整跑姿在整体弹动/翻面，四肢没有真正连续运动；
- Turn 虽然干净，但仍能看出正面 / 3⁄4 / 侧面 / 背面几个离散视角在切换。

因此第三版只通过“无鬼影”，**没有通过 Gate 1 的真实动作标准**。

### Gate 1 第四版：当前候选

第四版继续坚持“不重新生成角色图片”，并保留现有 11 个认可透明素材；本轮只替换 Walk / Run 的动作渲染方式：

- `MachineCatAnimator` 不再通过 `PrimaryMirror` 翻完整角色模拟左右步；
- Walk / Run 输出连续 `GaitPhase`；
- `ProceduralGaitFrames` 从现有 Walk / Run PNG 在内存中生成 32 个局部形变相位；
- 局部位移场只重点影响左右手、前后脚和很小范围的下半身，脸和主体大部分像素保持锁定；
- 两侧手脚使用相反 phase，形成交替抬落；Run 的位移幅度和节奏高于 Walk；
- 形变帧第一次进入 Walk / Run 时生成并冻结缓存，之后直接复用，不每帧重新计算；
- 不新增角色图片文件、不整图 crossfade、不整图左右翻面。

Turn 暂时保留第三版的单图多视角切换；第四版首先验证 Walk / Run 能否从“整图弹跳”提升为可信的局部四肢运动。如果局部位图形变仍有明显橡皮感，则应停止继续堆参数，并把“现有扁平 PNG 已到上限，需要真正分层源素材/骨骼源”作为后续结论。

第四版仍必须经过：Release build → procedural gait/self-test → 真实 WPF window smoke → win-x64 self-contained artifact → **用户真实 Windows 肉眼验收**。

只有用户明确确认 Gate 1 的原地动作合格，才能设计/实现 Gate 2 MotionController。CI 不能替代这一视觉门禁。
