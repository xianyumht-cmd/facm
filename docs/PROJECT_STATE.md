# FACM 当前项目状态

> 2026-08-12：FACM 3.1.3 已正式发布并启用在线更新；PR #40 的 FACM Shell / 主题 / 桌面形态 / PetHost 启动体验已进入正式版。当前进入桌宠素材质量优化。

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
- PR #40 最新候选 Windows Build #767 与 Mayhem Source Probe #107 均成功；用户完成 Windows 实机验收并明确授权正式发布。
- 默认启动现在立即显示 56×56 FACM Shell；默认未启用桌宠时不加载/预热 PetHost。
- 「面板主题 / 桌面宠物」已统一为「主题」入口，并保留 `ThemeId`、`AnimalPetEnabled`、`PetStyleId` 兼容。
- 修复主题二级菜单 outside-click / Dispose 竞态、Shell 首帧 layered window 时序、桌面位置复位逻辑。
- VPet 首次资源准备使用真实 `x/N` 进度，后续无可信百分比阶段使用 indeterminate 进度；PetHost bundle 缓存按 payload SHA-256 复用。
- Sprite 桌宠取消 WorkingArea 硬边界和反弹；用户实机确认复位位置与自由跑出屏幕两项均正常。
- 正式发布工作流 run #5 全部步骤成功：PetHost publish/self-test、Release build、内嵌资源验证、Authenticode 签名、disabled manifest、GitHub Release、最终在线清单启用均完成。

## 面向用户更新说明

FACM 3.1.3：新增轻量 FACM 悬浮入口并整合主题与桌面形态；优化 VPet 启动和加载体验，修复桌面形态菜单与位置复位问题；轻量桌宠支持更自然的自由移动。

## 当前任务：Issue #43 苍蝇素材清晰度

- 分支：`feat/fly-sprite-quality-0812`，从 3.1.3 发布后的最新 `main` 创建。
- 保持 `greenfly` 配置 ID、`Fly` 运动类型、`Speed=1.36`、`VisualScale=0.56` 以及现有 `_vx/_vy + jitter` 飞行轨迹不变。
- 不再把原 16×16 × 3 帧贴图最近邻放大；改为 FACM 内置、运行时生成的 **96×96 × 4 帧**精细 Sprite Sheet。
- 新素材身体锚点固定，仅翅膀在四个振翅状态间变化；包含头部、复眼、胸腹、六条腿、半透明翅膀和翅脉，使用抗锯齿/高质量缩放。
- `PixelArt=false`，当前约 92px 的实际显示尺寸接近源素材 1:1，避免旧 16px 素材约 6 倍放大造成的低清感。
- 素材由 FACM 代码程序化生成，不依赖运行时网络下载；`AssetLicense=CC0`，配置兼容不变。
- `--animal-pet-test` 增加守卫：源帧必须保持 96px、4 帧 grid、高清缩放模式，并确保已验收的 speed/scale 参数没有因换素材被误改。
- 先出测试构建实机验收画质；未验收前不进入下一正式版。

## 下一步

- 苍蝇素材实机验收通过后，再处理蜘蛛 8 方向 spritesheet 行序校准，解决偶发“倒着走”。
- Issue #33 的 Q 版蜘蛛 Gate 方案仍是独立长期路线；本次只做现有 Sprite 方向映射校准，不把它扩成新的蜘蛛行为引擎。
- VPet 后续自主行为排在苍蝇/蜘蛛之后；其它低质量候选暂不投入。

## 已知发布文档问题

- `.github/workflows/publish-release.yml` 当前最终状态模板仍残留旧 `Build #495 / Issue #28 / 3.1.0` 的硬编码文字；它不影响发布产物和在线更新，但会在每次发布后覆盖 `PROJECT_STATE.md` 为陈旧内容。
- 3.1.3 发布后已手工恢复正确状态；后续应把发布工作流状态写入改成基于实际 release 参数/当前验收信息，避免再次覆盖正确知识。
