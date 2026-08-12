# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是当前正式版；PR #44 高清绿苍蝇、PR #46 Flying Runtime、PR #48 Flying Runtime 二次精修、PR #52 产品化桌宠选择器均已完成 Windows 实机验收并进入 main。Issue #49 / PR #50 的发布状态写入修复也已完成。当前没有新的正式发布动作。

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.1.3
- GitHub Release：v3.1.3
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`
<!-- FACM_RELEASE_STATE_END -->

## 已进入 main、尚未发布的新能力

### PR #44：高清绿苍蝇基线

- Issue #43 / PR #44 已实机验收，用户确认效果不错；合并提交 `c8f4a0a4a4a847682845fef0cca3c64c49a8948d`。
- `greenfly` ID、`Speed=1.36`、`VisualScale=0.56` 和原 `_vx/_vy + jitter` 轨迹保持。
- 原 16×16 × 3 帧网络贴图升级为 FACM 内置 96×96 × 4 帧程序化精细 Sprite；身体锚点固定，仅翅膀变化。
- Build #773 完整成功并用于实机验收。

### PR #46：统一 Flying Runtime

- Issue #45 / PR #46 已于 2026-08-12 完成 Windows 实机验收，用户反馈“没什么问题”。
- PR #46 合并提交：`d6cdc2e860b488204348d8158c4da24a899d4aa2`。
- 最新候选 Build #779 成功；合并后的 main Build #780 也完整成功。
- 轻量桌宠主路线收敛为：**绿苍蝇 / 蜜蜂 / 蜻蜓 / 蝴蝶 / 飞蛾**；VPet Core 保留为独立高精度路线。
- 猫、狗、蜘蛛、蚂蚁、旧灰苍蝇、旧胡蜂、小鸟等旧 Sprite ID 不删除，既有 `settings.ini` 继续兼容，但新选择器不再推荐。
- 运行层把 **桌面运动轨迹 / 360° 身体朝向 / 翅膀动画** 三层解耦；素材统一朝右为 0°，运行时按真实速度向量平滑旋转。
- Flying Runtime 不增加屏幕硬边界；桌宠允许自然飞出所有屏幕，恢复入口仍是“复位桌面位置”。
- `greenfly` 继续锁定既有轨迹基线：基础速度 82~140 × 1.36、移动段 0.55~1.80s、idle=0.02、velocity response=7.5、`sin(17t)×10 / cos(13t)×8` jitter。

### PR #48：Flying Runtime 二次精修

- Issue #47 / PR #48 已于 2026-08-13 完成 Windows 实机验收，用户反馈“没什么问题”。
- PR #48 合并提交：`0126789dc6274a90068aaedafa7f8d2ca71b8361`；Issue #47 已自动关闭为 completed。
- 本轮不改已经实机通过的 Flying Runtime 架构，只精修素材和既有 Profile 参数；绿苍蝇继续作为轨迹回归基线。
- FACM Windows Build #781 完整成功并用于实机验收；验收后的分支提交仅同步项目状态文档，不改变 EXE 行为。

#### 素材精修

- 蜜蜂源帧提高到 104px：强化腹部黄黑层次、独立胸部、复眼高光、半透明翅膀和翅脉。
- 蜻蜓源帧提高到 128px：强化大复眼、胸部、长腹节、四片翅膀、翅脉和翼痣，保持身体锚点不动。
- 蝴蝶源帧提高到 112px：增加前/后翅层次、翅脉和眼斑，开合仅改变翅膀，不移动身体。
- 飞蛾源帧提高到 112px：使用更低饱和的厚翅、翼带、绒感胸部和羽状触角。
- 四套素材继续由 FACM 内置程序化生成，不依赖运行时网络下载；`PixelArt=false`。

#### 飞行性格调优

- 蜜蜂：48~82 基础速度，idle 18%，停悬 0.35~1.10s，突出巡航 + 悬停。
- 蜻蜓：120~205 基础速度，移动段 2.20~4.60s，局部 jitter 0.5/0.8，突出长直线高速冲刺 + 短停。
- 蝴蝶：18~38 基础速度，移动段 2.80~5.60s，低响应 + 14px 低频纵向漂浮，突出慢速大曲线。
- 飞蛾：36~68 基础速度，移动段 0.65~1.55s；X/Y jitter 同频同幅 4.8Hz / 7px，形成小范围绕圈感。
- 不增加新的运动状态机，不改 `SpritePetWindow` 核心算法。

#### 自动验证

- `--animal-pet-test` 继续锁定绿苍蝇原轨迹参数、360° 朝向、四种精修 Profile 性格约束和各自源帧尺寸。
- PR #48 已合并到 main，但**未触发正式发布**；FACM 3.1.3 继续作为线上正式版。

### PR #52：产品化桌宠选择器

- Issue #51 / PR #52 已于 2026-08-13 完成 Windows 实机验收，用户反馈“没问题”。
- PR #52 测试候选 Build #790 完整成功；验收记录提交后的 Build #791 也完整成功。
- PR #52 合并提交：`eef449652b82519acada4d4bd91aa3876c9fdca9`；Issue #51 已自动关闭为 completed。
- 本轮未改五种 Flying Runtime 桌宠的速度、VisualScale、FPS、素材或 `SpritePetWindow` 运动算法，也未新增动物。
- 左侧桌宠列表改为产品化双行卡片：名称 + 简短飞行性格，并对当前正在使用的桌宠显示“当前”标识。
- 右侧详情区区分“轻量 · 自主飞行”和“高精度 · 独立桌宠”，用普通用户文案说明飞行性格、资源取舍与拖动/自由出屏/复位交互。
- 当前正在使用的选项会明确显示并禁用重复“应用”，其它项目继续支持单击预览、双击应用和按钮应用。
- VPet 预览使用用户视角说明动作与首次资源成本；主界面不再暴露 `Flying Runtime / CC0` 等实现术语。
- `--animal-pet-test` 已增加选择器呈现守卫：6 个可见选项需保持独立性格摘要/行为文案，轻量飞虫与 VPet 的运行层标签必须正确。

## Issue #49 / PR #50：发布状态写入已修复

- 根因：正式发布工作流最终步骤曾整份重建 `docs/PROJECT_STATE.md`，并硬编码历史 `Build #495 / Issue #28 / 3.1.0`；3.1.3 发布时实际发生过状态文档被旧内容覆盖，但二进制、Release 和在线 manifest 本身未受影响。
- 修复后发布工作流只维护 `<!-- FACM_RELEASE_STATE_BEGIN -->` / `<!-- FACM_RELEASE_STATE_END -->` 包围的机器发布状态区块，保留区块之外的开发、验收与后续任务内容。
- 当前 3.1.3 正式版段落已经迁入该 marker 区块；下一次正式发布会原位替换，不会生成第二份“当前正式版”。
- 机器区块只写 workflow 能直接证明的版本、Release tag、online enabled、`minimum_version`、`force_update`、发布基础/元数据 SHA、FACM.exe SHA-256、`published_at` 和 release notes；不再写 Build/Issue/用户验收等推断信息。
- PR #50 HEAD `ebb518c9d14071278398d46acfae71b9b77b88f6` 的 FACM Windows Build #787 完整成功；PR #50 合并提交 `56f2ac14b1b405852e6b919584529c7e0b0166a8`，Issue #49 已关闭 completed。
- 合并后再次核对 `online/version.json`：仍为 FACM 3.1.3、`enabled=true`、`minimum_version=3.0.0`、`force_update=false`，本维护任务没有触发或改写正式发布。

## 当前状态与下一步

- PR #52 已完成实机验收并进入 main；第三轮飞行桌宠选择器任务已关闭。
- 合并后再次核对 `online/version.json`：仍为 FACM 3.1.3、`enabled=true`、`minimum_version=3.0.0`、`force_update=false`，没有触发正式发布。
- 当前没有必须继续修复的桌宠阻塞项；下一步可从最新 main 重新选择新的高收益任务，而不是为了继续迭代而强行改已验收稳定的 Flying Runtime。
