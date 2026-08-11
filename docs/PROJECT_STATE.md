# FACM 当前项目状态

> 2026-08-12：FACM 3.1.3 已正式发布并启用在线更新；PR #40 的 FACM Shell / 主题 / 桌面形态 / PetHost 启动体验已进入正式版。下一项进入桌宠素材质量优化。

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

## 当前后续：桌宠素材质量

- 保留已经验收通过的运动引擎与自由移动轨迹，不因更换素材重做轨迹系统。
- 优先级：苍蝇素材清晰度 > 蜘蛛 8 方向行序校准 > VPet 后续自主行为；其它低质量候选暂不投入。
- 苍蝇目标不是简单把 16×16 最近邻放大，而是寻找/制作更高源分辨率的小型 Sprite，保持现有高速随机飞行轨迹。
- 蜘蛛现有清晰度可接受，但偶发“倒着走”；在确认 spritesheet 实际 8 行方向顺序前不凭猜测重排。
- 素材实验从最新 `main` 建新的短分支，不混入 3.1.3 Release。

## 已知发布文档问题

- `.github/workflows/publish-release.yml` 当前最终状态模板仍残留旧 `Build #495 / Issue #28 / 3.1.0` 的硬编码文字；它不影响发布产物和在线更新，但会在每次发布后覆盖 `PROJECT_STATE.md` 为陈旧内容。
- 本次已手工恢复正确状态；后续应把发布工作流状态写入改成基于实际 release 参数/当前验收信息，避免再次覆盖正确知识。
