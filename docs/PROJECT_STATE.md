# FACM 当前项目状态

> 2026-08-14：**FACM 3.2.0 是当前正式生产基线。Performance Contract 已完成并合入 `main`。** 3.2 后端地基、真实蜜蜂、UI Text Contract 与性能预算基础均已冻结。下一项主线正式进入 League Dashboard / Client Status + Current Account。

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

- `main`：`80b13b85f32479b369a3077f1cbfec8afa5e56b6`
- Issue #75：completed
- PR #76：merged
- PR #76 最终行为 HEAD：`4d39fe310c118391b8e615d909dab7d4c330f5e9`
- PR Windows Build #898：SUCCESS
- PR UI Text Contract #19：SUCCESS
- PR Mayhem Probe #186：SUCCESS
- main Windows Build #899：SUCCESS
- main UI Text Contract #20：SUCCESS
- main Mayhem Probe #187：SUCCESS
- `online/version.json`：仍为 3.2.0，`enabled=true`、`force_update=false`
- 本轮没有创建 tag / Release，也没有改变在线正式版本。

## 已完成并冻结的基础

- Modular Host Phase 1～5 已完成：Settings / Tools / Online / Pets / LeagueClient / Mayhem / Cleanup / Shell 有明确模块所有权和依赖关系。
- Issue #68 / PR #69：写真级真实蜜蜂已验收并合并；素材、轨迹和身体姿态分离。
- Issue #70 / PR #71：UI Text Contract 已验收并合并；稳定 TextKey + `[Text]` 主协议 + `[Replace]` 兼容层 + CI 防回归。
- Issue #75 / PR #76：Performance Contract 已完成；共享性能预算已注册为 Host 所有权，并有 deterministic smoke。
- 不再为了架构形式继续拆层；以后只在真实产品需求或缺陷暴露边界问题时调整对应模块。

## Performance Contract 基线

核心原则：

> 高配要快，普通机要顺；游戏优先，FACM 第二优先。

当前统一预算：

- Desktop：network 4 / image 2 / disk 2 / CPU 2 / prefetch 20 / non-critical poll >=15s。
- League Client：3 / 2 / 2 / 2 / 12 / >=20s。
- Queueing：2 / 1 / 1 / 1 / 4 / >=30s。
- Champ Select：2 / 1 / 1 / 1 / 0 / >=45s。
- In Game：1 / 1 / 1 / 1 / 0 / >=60s；后台预取、维护工作和非必要视觉增强关闭。
- FACM hidden/background：1 / 1 / 1 / 1 / 0 / >=60s。

实现组成：

- `PerformanceBudget`
- `LeagueActivityLevel`
- `PerformanceBudgetProvider`
- `PerformanceModule`
- `FACM.exe --performance-contract-test`
- `docs/PERFORMANCE-CONTRACT.md`

数字预算和功能开关都遵守 monotonic rule：从 Desktop → Client → Queueing → Champ Select → In Game 只能更保守，不能反向放宽。

本阶段没有主动轮询 Gameflow，也没有为了统一而重写现有 Mayhem/LeagueClient 的 timeout/cache 行为。下一项 League Dashboard 才负责读取真实客户端状态，并成为第一个正式 Performance Budget consumer。

## FACM 3.2.0 用户可见基线

- 桌面宠物体系升级；新增写真级真实蜜蜂。
- 桌宠选择器包含预览、当前状态、飞行特点和交互说明。
- 普通模式第二次启动会唤醒并置前已有 FACM 控制中心。
- `ui-text.ini` 使用稳定 TextKey 主协议，更多界面文案可控。
- 海斗与 League Client 本地数据读取共享统一客户端基础，并保留公网 fallback 与容灾策略。
- VPet/PetHost、Cleanup 安全流程、Online 更新事务和默认 FACM Shell 保持已验收行为。

## 产品方向：Akari / OP.GG 参照

FACM 不复制 Akari 的 Electron/Vue 技术栈，也不以 Chromium 重 UI 为产品目标。继续使用当前 C#/.NET Windows 基础，学习成熟产品的功能边界、数据能力和客户端联动能力。

目标顺序：

1. League Dashboard：客户端状态、当前账号、平台/区服、Gameflow；
2. Player：玩家主页、最近对局、英雄表现与统计；
3. Champ Select / Current Game：选人和当前对局信息；
4. Tools / Automation：在稳定基础上逐项增加实用自动化。

Akari 官网“不支持腾讯服务器”继续视为官方免责声明，不直接推导技术能力。FACM 的腾讯/国服兼容按功能逐项以源码机制与实际国服测试判定。

性能是 FACM 的明确差异化要求：一台本身能够正常运行 League 的电脑，同时运行 FACM 后不应因为非必要后台工作明显恶化游戏体验。

## 冻结边界

没有真实缺陷或新产品需求时，不要顺手重做：

- Modular Host Phase 1～5；
- Performance Contract 预算模型和游戏优先原则；
- 普通实例 Mutex + AutoResetEvent 二次启动激活；
- 已验收 Flying Runtime/Profile 与 Real Bee 姿态；
- VPet/PetHost 独立运行边界；
- Cleanup whitelist / reparse / execute revalidation 等安全语义；
- Mayhem 多源容灾和 LeagueClient 共享 LCU 基础；
- Online Release/manifest 事务；
- `settings.ini` 与 `ui-text.ini [Text]/[Replace]` 兼容。

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不是当前主线。

## 下一步

下一项正式产品任务：**League Dashboard / Client Status + Current Account**。

要求：

1. 从最新 `main` 新开独立 Issue / branch / PR；
2. Dashboard 显式依赖 `LeagueClientModule` 与 `PerformanceModule`；
3. 第一版读取客户端连接状态、当前账号、平台/区服和真实 Gameflow；
4. 将 Gameflow 映射到 `LeagueActivityLevel`，驱动共享 Performance Budget；
5. 从第一版就采用渐进加载、取消、timeout、受限并发和页面不可见降级；
6. 不把数据请求、图片解码或批量统计放到 UI thread；
7. 先完成基础 Dashboard，不同时塞入完整战绩页、Champ Select 自动化或大规模动态图表；
8. 不创建新 Release，除非用户再次明确授权正式发布。
