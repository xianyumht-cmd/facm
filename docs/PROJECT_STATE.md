# FACM 当前项目状态

> 2026-08-13：FACM 3.2 后端 Modular Host 地基已经完成并通过集中 Windows 实机验收。Real Pet Gate 1 写真实蜜蜂也已验收并合并。当前唯一主线任务是 Issue #70 / PR #71：建立 UI Text Contract，保证以后新增用户可见静态文字统一可由 `ui-text.ini` 控制。线上正式版仍是 FACM 3.1.3，没有 Release 授权。

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.2.0
- GitHub Release：v3.2.0
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：c0609eb43d4127ace8445ecf97f750fda82245cd
- 发布元数据提交：b4dff7ffab2914c5c1213f09893de80ee67efcb6
- Release FACM.exe SHA-256：D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9
- published_at：2026-08-13T15:55:35.7289037+00:00
- release_notes：FACM 3.2.0：桌面宠物、控制中心、界面文字自定义与整体稳定性更新。
<!-- FACM_RELEASE_STATE_END -->

## 当前 main 基线

- `main`：Real Bee PR #69 合并后的 `a1fd91079524d51984dd6870ee99be295ec7887f`
- Issue #68：completed
- 用户实机验收 Real Bee 行为 HEAD：`6b73e885b5fa3d89f8b6c82cd8a8efaafab208e4`
- FACM Windows Build #878：SUCCESS
- 用户对 Build #878 反馈：“这一版不错 很好”
- `online/version.json`：仍为 3.1.3
- 没有新 tag / Release / online 发布

Real Bee 的最终行为是：写真素材沿用已验收 Bee 飞行轨迹；身体姿态改为左右镜像、有限俯仰和转向滞后，不再机械 360° 旋转。其他既有 Flying pet 不受影响。

## FACM 3.2 后端地基

Phase 1～5 已完成并冻结：

- `FacmHost` 负责模块注册、显式依赖、初始化、失败回滚、反向释放和启动耗时诊断；
- Settings / Tools / Online / Pets / Mayhem / Cleanup 已有明确模块所有权；
- `LeagueClientModule` 统一拥有 League Client discovery、session 和共享 LCU HTTP；
- Mayhem 依赖 LeagueClient，再由 Shell 消费；
- `Program/MainForm` 不再作为全部业务后端的隐式所有者。

用户已完成整轮集中实机测试，因此不再为了架构形式继续拆层。以后只在真实新功能或缺陷暴露边界问题时调整对应模块。

## 当前任务：UI Text Contract

- Issue #70：`UI Text Contract：统一用户可见文案 Key 与防回归门禁`
- Draft PR #71：`feat(ui-text): establish stable UI Text Contract`
- branch：`feat/ui-text-contract-70`

已经落地：

- `UiTextKeys`：稳定 Key 表；旧 Key 保持兼容，新 Key 按 UI 角色命名。
- `UiTextCatalog`：内置默认中文，缺 Key 自动补进现有配置且不覆盖用户值。
- `UiTextRuntime.Text(key)`：显式 Key 取值入口。
- FACM 自有 `ContextMenuStrip`：每次打开重新应用文字配置。
- `ThemeMenu`：面板外观、桌面形态、FACM 悬浮入口、桌宠选择、桌面位置复位全部改为 Key。
- `AnimalPetPickerForm`：窗口、状态、自绘名称、摘要、行为、说明、运行类型、交互说明和预览静态文字改为 Key。
- `scripts/check-ui-text-contract.ps1` + `.github/workflows/ui-text-contract.yml`：阻止以后新增 UI 代码再次直接写静态文案。
- `docs/UI-TEXT-CONTRACT.md`：长期规则文档。

首轮自动证据：FACM UI Text Contract #1 SUCCESS；FACM Windows Build #880 SUCCESS。最终以 PR #71 最新 HEAD checks 为准。

## 长期文字规则

- `[Text]` 是正式主协议；Key 稳定，默认中文可以演进。
- `[Replace]` 保留历史/全局兼容，不再作为新增功能的主实现。
- 新用户可见静态文案必须先注册 `UiTextKeys` 和 `UiTextCatalog` 默认值，再由 UI 从 Runtime 取值。
- 临时菜单和自绘静态文字必须显式走文字 Runtime。
- 日志、内部异常、测试断言、用户输入、查询/游戏/服务器动态数据不强行套静态 UI Key。
- 新 UI 直写由 `FACM UI Text Contract` workflow 防回归。

## 冻结边界

当前不要顺手重构：

- Modular Host Phase 1～5；
- 单实例二次启动激活；
- 既有 Flying Runtime/Profile；
- Real Bee Build #878 已验收姿态；
- VPet/PetHost；
- Cleanup 安全语义；
- Mayhem 多源容灾；
- Online 发布事务；
- `settings.ini` 与既有 `ui-text.ini [Text]/[Replace]` 兼容。

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不是当前主线。

## 下一步

1. 等 PR #71 最新 HEAD 的 Windows Build 与 UI Text Contract 全绿；
2. review diff，确认没有夹带视觉布局、桌宠物理、online/version 或发布变化；
3. 形成最终 Windows candidate，让用户一次性确认截图对应主题菜单和文字配置行为；
4. 用户确认后合并 #71、关闭 #70；
5. 不发布，线上继续 3.1.3。

完成 UI Text Contract 后，再回到 FACM 上层产品升级，不继续为架构本身重构。
