# FACM 当前项目状态

> 2026-08-14：**FACM 3.2.0 已正式发布，在线更新与 3.2.0 用户公告均已启用。** 用户已接受当前产品基线。本轮 Release / 公告任务完成后没有遗留发布动作，下一项新工作应回到 FACM 上层产品升级，而不是继续为架构本身拆层。

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

## 3.2.0 发布事实

- Issue #72 / PR #73：正式发布请求已完成。
- PR #73 merge commit：`c0609eb43d4127ace8445ecf97f750fda82245cd`。
- `FACM Publish Release` #6：SUCCESS。
- GitHub Release `v3.2.0`：已公开，非 prerelease。
- `online/version.json`：`enabled=true`、`version=3.2.0`、`minimum_version=3.0.0`、`force_update=false`。
- `src/FACM/Properties/AssemblyInfo.cs`：3.2.0 / 3.2.0.0。
- Release `FACM.exe` 与在线清单 SHA-256 一致：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`。
- PR #74：完成 3.2.0 用户公告与 canonical 状态收口；公告使用新 ID、`enabled=true`、`popup=true`，并链接 v3.2.0 Release。

## FACM 3.2.0 用户可见基线

- 桌面宠物体系升级：绿苍蝇、蜜蜂、蜻蜓、蝴蝶、飞蛾等轻量 Flying Runtime 保留各自飞行性格；新增写真级真实蜜蜂。
- 桌宠选择器已产品化，包含预览、当前状态、飞行特点和交互说明。
- 普通模式第二次启动会唤醒并置前已有 FACM 控制中心，不再只是提示“已经在运行”。
- `ui-text.ini` 文字自定义升级为稳定 TextKey 主协议；主题临时菜单和桌宠选择器静态文字均可配置，`[Replace]` 继续作为兼容/全局替换层。
- 海斗与 League Client 本地数据读取共享统一客户端基础，并保留既有公网 fallback 与容灾策略。
- VPet/PetHost、Cleanup 安全流程、Online 更新事务和默认 FACM Shell 继续保持已验收行为。

## 已完成的 3.2 地基

- Modular Host Phase 1～5 已完成并冻结：Settings / Tools / Online / Pets / LeagueClient / Mayhem / Cleanup / Shell 有明确模块所有权和依赖关系。
- Issue #68 / PR #69：真实蜜蜂已验收并合并。
- Issue #70 / PR #71：UI Text Contract 已验收并合并；最终行为 HEAD `f2af1e0261752ea5d0073b2ff49ac0e3ce26d7d9`，Windows Build #885 与 UI Text Contract #6 均 SUCCESS。
- 不再为了架构形式继续拆层；后续只有真实新功能或缺陷需要时才调整对应模块。

## 冻结边界

没有真实缺陷或新产品需求时，不要顺手重做：

- 普通实例 Mutex + AutoResetEvent 二次启动激活；
- 已验收 Flying Runtime/Profile 与 Real Bee 姿态；
- VPet/PetHost 独立运行边界；
- Cleanup whitelist / reparse / execute revalidation 等安全语义；
- Mayhem 多源容灾和 LeagueClient 共享 LCU 基础；
- Online Release/manifest 事务；
- `settings.ini` 与 `ui-text.ini [Text]/[Replace]` 兼容。

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不是当前主线。

## 下一步

- 当前没有待执行的 Release / 在线更新动作。
- 新任务从最新 `main` 开始，先检查 active Issue / PR / branch，再选择真实产品需求或缺陷。
- 产品升级继续参考 League Akari 的成熟产品结构时，以源码机制与实际国服测试为准，不因其官网免责声明直接判定腾讯服技术不可用。
