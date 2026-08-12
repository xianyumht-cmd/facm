# FACM 当前项目状态

> 2026-08-12：FACM 3.1.3 已正式发布并启用在线更新；PR #44 的高清绿苍蝇已实机验收并合并。当前任务是 Issue #45 / PR #46：统一轻量 Flying Runtime。

## 当前正式版

- 版本：FACM 3.1.3。
- GitHub Release：v3.1.3。
- 在线更新：已启用。
- `minimum_version=3.0.0`。
- `force_update=false`。
- 发布基础 main：`3402aa69821178ff816f8f971bf9c85b60598c48`。
- 发布元数据提交：`06b01ee7099b8c4d759e34b251d84b708a6e9ec1`。
- 在线更新启用提交：`c1fe46019a32acb84e7a979bf35ee0b657bc1778`。
- Release FACM.exe SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`。

## 3.1.3 已验证内容

- PR #40 合并提交：`a63be62c93207790804faf6b730b9b22234ac45d`。
- 默认启动立即显示 56×56 FACM Shell；默认未启用桌宠时不加载/预热 PetHost。
- 「面板主题 / 桌面宠物」已统一为「主题」入口，并保留 `ThemeId`、`AnimalPetEnabled`、`PetStyleId` 兼容。
- VPet 首次资源准备使用真实 `x/N` 进度，后续无可信百分比阶段使用 indeterminate 进度；PetHost bundle 缓存按 payload SHA-256 复用。
- Sprite 桌宠取消 WorkingArea 硬边界和反弹；用户实机确认复位位置与自由跑出屏幕两项均正常。
- 正式发布工作流 run #5 全部步骤成功：PetHost publish/self-test、Release build、内嵌资源验证、Authenticode 签名、disabled manifest、GitHub Release、最终在线清单启用均完成。

## PR #44：高清绿苍蝇基线

- Issue #43 / PR #44 已完成 Windows 实机画质验收，用户确认效果不错；PR #44 已合并到 main，合并提交 `c8f4a0a4a4a847682845fef0cca3c64c49a8948d`。
- `greenfly` ID、`Fly` 运动类型、`Speed=1.36`、`VisualScale=0.56` 和既有 `_vx/_vy + jitter` 轨迹保持。
- 原 16×16 × 3 帧网络贴图已升级为 FACM 内置 **96×96 × 4 帧**程序化精细 Sprite；身体锚点固定，仅翅膀变化。
- Build #773 完整成功并用于实机验收。

## 当前任务：Issue #45 / PR #46 Flying Runtime

### 产品方向

- 轻量桌宠主路线收敛为会飞的动物/昆虫；贴地猫狗蜘蛛等旧 Sprite 只保留配置兼容，不再作为新用户推荐项。
- 推荐选择器现在只展示：**绿苍蝇 / 蜜蜂 / 蜻蜓 / 蝴蝶 / 飞蛾 / VPet Core**。
- 旧 `cat` / `dog` / `spider` / `ant` / `greyfly` / `wasp` / `bird` ID 仍可通过既有 `settings.ini` 解析和启动。

### 统一运行层

- 新增 `FlyingPetProfiles`，把 **运动轨迹 / 360° 身体朝向 / 翅膀动画** 三层解耦。
- 所有 managed flying 素材以朝右为 0° 母版；运行时根据实际 `_vx/_vy` 计算 `atan2` 目标角度，并用最短角度差平滑旋转。
- 不再需要 8 方向 Sprite 行；转向视觉由连续角度负责。
- 每种飞行动物只通过 Profile 定义速度区间、改向时长、停悬概率、VelocityResponse、HeadingResponse 和 jitter。
- 自由出屏规则不变；Flying Runtime 不增加屏幕硬边界。

### 绿苍蝇回归基线

- 为避免“重构运行层把满意的轨迹改坏”，`greenfly` Profile 明确锁定 FACM 3.1.3 / PR #44 参数：
  - 基础速度 `82~140`，再乘 `Speed=1.36`；
  - 移动段 `0.55~1.80s`；
  - idle 概率 `0.02`；
  - velocity response `7.5`；
  - `sin(17t) × 10` / `cos(13t) × 8` jitter。
- 新增的仅是视觉朝向平滑，不改变桌面位移公式。

### 新飞行动物

- 蜜蜂：中速、圆滑转向、短暂停悬；FACM 内置 96px × 4 帧程序化素材。
- 蜻蜓：高速长距离冲刺、快速改向；FACM 内置 112px × 4 帧程序化素材。
- 蝴蝶：慢速大曲线、明显上下漂浮、低频大幅振翅；FACM 内置 96px × 4 帧素材。
- 飞蛾：短距离随机游走、轨迹紧凑；FACM 内置 96px × 4 帧素材。
- 四套新素材均不依赖运行时网络下载，身体锚点稳定，`PixelArt=false`。

### 验证状态

- PR #46 代码 HEAD `fdd617d58342035f6da5d14f9c50549ba09aa8ea` 的 Windows Build #776 已完整成功。
- 加入正式技术决策与项目状态文档后的 HEAD `0a659efcfa9207f08dc24f6d9e4101c3170ff02d`，Windows Build #778 也已完整成功；artifact `FACM-Windows-x64-778`，digest `sha256:b1f4786cc4fc50a2e73c1fce202cdd32a59e0742d1699da2bd2db8ab7dcb065e`。
- 两轮均包含 PetHost self-test、FACM Release build、`--animal-pet-test`、签名步骤和 artifact 上传。
- `--animal-pet-test` 已新增：主选择器组成、Legacy ID 兼容、五套 Flying Profile、96px 高精度下限、greenfly 原轨迹参数、0/90/180/270° heading、350↔10° 最短角度环绕、旋转后渲染差异等守卫。
- 当前阶段：**等待 Windows 实机验收 Flying Runtime；未验收前 PR #46 不合并、不发布。**

## 实机验收重点

- 绿苍蝇轨迹体感不能比 PR #44 / FACM 3.1.3 基线退化；允许视觉朝向更自然，但不能变成新的移动算法。
- 五只飞行动物转向时不能倒着飞、瞬间镜像或绕 360° 长路转向。
- 蜜蜂 / 蜻蜓 / 蝴蝶 / 飞蛾的 Profile 性格差异应肉眼可辨，而不是仅换皮。
- 四帧翅膀动画切换时身体锚点不能跳动。
- 继续允许飞出所有屏幕；“复位桌面位置”仍是找回入口。

## 后续

- Flying Runtime 实机通过后，再根据观感逐个做素材二次精修和 Profile 调参。
- Issue #33 的 Q 版蜘蛛 Gate 方案保留为独立长期探索，不是当前轻量桌宠主路线。
- VPet 自主行为继续排在 Flying Runtime 稳定之后。

## 已知发布文档问题

- `.github/workflows/publish-release.yml` 当前最终状态模板仍残留旧 `Build #495 / Issue #28 / 3.1.0` 的硬编码文字；它不影响发布产物和在线更新，但会在每次发布后覆盖 `PROJECT_STATE.md` 为陈旧内容。
- 3.1.3 发布后已手工恢复正确状态；后续应单独修复发布工作流的状态写入模板。