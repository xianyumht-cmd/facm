# FACM 当前项目状态

> 2026-08-14：**FACM 3.2.0 已正式发布并启用在线更新。** 用户已接受当前产品基线。Release v3.2.0、在线清单、程序集版本和发布文件 SHA 已核对一致；当前只剩 PR #74 推送面向用户的新版本公告并完成本次发布文档收口。

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

## 当前发布收口

- Issue #72：`Release FACM 3.2.0`，在公告收口完成前保持 open。
- PR #73：正式发布请求，已合并；merge commit `c0609eb43d4127ace8445ecf97f750fda82245cd`。
- `FACM Publish Release` #6：SUCCESS。
- GitHub Release `v3.2.0`：已公开，非 prerelease。
- `online/version.json`：`enabled=true`、`version=3.2.0`、`minimum_version=3.0.0`、`force_update=false`。
- `src/FACM/Properties/AssemblyInfo.cs`：3.2.0 / 3.2.0.0。
- 发布后 main 在公告任务开始前为 `e6f3bd0aad55542462bbf6eebd12211235a215ac`。
- PR #74：推送 `FACM 3.2.0 正式发布` 新公告并同步本文件；公告启用、使用新 ID、启动时弹出，并链接 v3.2.0 Release。

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

1. PR #74 CI 全绿后合并，验证公告在 `main` 已启用并关闭 Issue #72。
2. 本次正式发布完成后不再继续做发布/架构清理。
3. 下一项新工作回到 FACM 上层产品升级；继续参考 League Akari 的成熟产品结构时，以源码机制与实际国服测试为准，不因其官网免责声明直接判定腾讯服技术不可用。
