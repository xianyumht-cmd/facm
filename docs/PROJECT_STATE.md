# FACM 当前项目状态

> 2026-08-14：**FACM 3.2.0 已正式发布并作为当前生产基线。** 3.2 后端地基、真实蜜蜂和 UI Text Contract 已完成并冻结。当前主线是 Issue #75 / PR #76：在 League Dashboard 开工前建立 Performance Contract，确保后续向 Akari / OP.GG 级产品能力扩展时以“游戏优先，FACM 第二优先”为硬规则。

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

## 3.2.0 用户可见基线

- 桌面宠物体系升级：绿苍蝇、蜜蜂、蜻蜓、蝴蝶、飞蛾等轻量 Flying Runtime 保留各自飞行性格；新增写真级真实蜜蜂。
- 桌宠选择器已产品化，包含预览、当前状态、飞行特点和交互说明。
- 普通模式第二次启动会唤醒并置前已有 FACM 控制中心。
- `ui-text.ini` 文字自定义使用稳定 TextKey 主协议；`[Replace]` 继续作为兼容/全局替换层。
- 海斗与 League Client 本地数据读取共享统一客户端基础，并保留公网 fallback 与容灾策略。
- VPet/PetHost、Cleanup 安全流程、Online 更新事务和默认 FACM Shell 保持已验收行为。

## 已完成并冻结的基础

- Modular Host Phase 1～5 已完成：Settings / Tools / Online / Pets / LeagueClient / Mayhem / Cleanup / Shell 有明确模块所有权和依赖关系。
- Issue #68 / PR #69：真实蜜蜂已验收并合并；写真素材与飞行轨迹/身体姿态解耦。
- Issue #70 / PR #71：UI Text Contract 已验收并合并；新用户可见静态文案以稳定 TextKey 为主协议，并有 CI 防回归门禁。
- 不再为了架构形式继续拆层；后续只在真实产品需求或缺陷暴露边界问题时调整对应模块。

## 当前任务：Performance Contract

- Issue #75：`Performance Contract foundation`
- Draft PR #76：`feat(performance): establish FACM Performance Contract`
- branch：`feat/performance-contract-75`

当前已经落地：

- `PerformanceBudget`：统一网络、图片解码、磁盘 I/O、后台 CPU、战绩预取、非关键刷新预算。
- `LeagueActivityLevel`：None / Client / Queueing / ChampSelect / InGame。
- `PerformanceBudgetProvider`：线程安全维护当前预算，并在预算发生变化时发出事件。
- `PerformanceModule`：成为 FacmHost 中正式的性能预算所有者，但本身不轮询、不扫描硬件、不创建后台负载。
- `--performance-contract-test`：deterministic smoke，并加入 `ContinuousIntegrationBuild`。
- `docs/PERFORMANCE-CONTRACT.md`：长期性能契约。

首轮行为 HEAD `8b225aca290c670ce8d68d894eb517a59832a341` 已通过 Windows Build #894、UI Text Contract #15、Mayhem Probe #182。最终以 PR #76 最新 HEAD checks 为准。

当前预算基线：

- Desktop：network 4 / image 2 / disk 2 / CPU 2 / prefetch 20 / non-critical poll >=15s。
- League Client：3 / 2 / 2 / 2 / 12 / >=20s。
- Queueing：2 / 1 / 1 / 1 / 4 / >=30s。
- Champ Select：2 / 1 / 1 / 1 / 0 / >=45s。
- In Game：1 / 1 / 1 / 1 / 0 / >=60s；后台预取、维护工作和非必要视觉增强关闭。
- FACM hidden/background：1 / 1 / 1 / 1 / 0 / >=60s。

Performance Contract 当前只建立共享预算，不主动读取 Gameflow，也不改现有 Mayhem/LeagueClient 的稳定 timeout/cache 行为。下一项 League Dashboard 才负责读取真实客户端状态并成为第一个正式预算消费者。

## 产品方向：Akari / OP.GG 参照

FACM 不复制 Akari 的 Electron/Vue 技术栈，也不以网页式重 UI 为目标。继续使用当前 C#/.NET Windows 基础，学习成熟产品的功能边界和客户端联动能力。

目标方向：

1. League Dashboard：客户端状态、当前账号、平台/区服、Gameflow；
2. Player：玩家主页、最近对局、英雄表现与统计；
3. Champ Select / Current Game：选人和当前对局信息；
4. Tools / Automation：在稳定基础上逐项增加实用自动化。

Akari 官网“不支持腾讯服务器”继续视为官方免责声明，不直接推导技术能力。FACM 的腾讯/国服兼容按功能逐项以源码机制与实际国服测试判定。

性能定位是 FACM 的差异化要求：高配要快，普通机要顺；一台本身能够正常运行 League 的电脑，同时运行 FACM 后不应因为非必要后台工作明显恶化游戏体验。

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

1. PR #76 最新 HEAD Windows Build / UI Text Contract / Mayhem Probe 全绿后收口 Performance Contract；
2. 不创建新 Release，线上继续 FACM 3.2.0；
3. 下一项产品任务从最新 `main` 开始做 League Dashboard / Client Status + Current Account；
4. League Dashboard 显式依赖 `LeagueClientModule` 与 `PerformanceModule`，并负责把真实 Gameflow 映射到共享 Performance Budget；
5. Dashboard 从第一版就使用渐进加载、取消、timeout、受限并发和可见区域优先，不允许先做重页面再补性能优化。
