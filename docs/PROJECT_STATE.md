# FACM 当前项目状态

> 2026-08-14：FACM 3.2.0 是当前正式生产基线。Performance Contract 已完成并合入 `main`。下一项主线：**League Dashboard / Client Status + Current Account**。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 当前轮次没有创建新 Release，也没有改变线上版本。

## 最新程序行为基线

- PR #76：`feat(performance): establish FACM Performance Contract`，已 merged。
- 行为 merge commit：`80b13b85f32479b369a3077f1cbfec8afa5e56b6`。之后的纯文档提交不改变程序行为。
- Issue #75：completed。
- PR 最终行为 HEAD：`4d39fe310c118391b8e615d909dab7d4c330f5e9`。
- PR checks：Windows Build #898 / UI Text Contract #19 / Mayhem Probe #186 全部 SUCCESS。
- main checks：Windows Build #899 / UI Text Contract #20 / Mayhem Probe #187 全部 SUCCESS。

## 已完成并冻结

- Modular Host Phase 1～5：Settings / Tools / Online / Pets / LeagueClient / Mayhem / Cleanup / Shell 模块所有权与依赖已稳定。
- Real Pet Gate 1：写真级真实蜜蜂已验收；素材、轨迹和身体姿态分离。
- UI Text Contract：稳定 TextKey、`[Text]` 主协议、`[Replace]` 兼容层和 CI 防回归已完成。
- Performance Contract：共享性能预算、Host 所有权和 deterministic smoke 已完成。

没有真实缺陷或新需求时，不要顺手重做以上基础，也不要重做单实例激活、Flying Runtime、VPet/PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务或现有配置兼容。

## Performance Contract

原则：**高配要快，普通机要顺；游戏优先，FACM 第二优先。**

当前预算上限：

- Desktop：network 4 / image 2 / disk 2 / CPU 2 / prefetch 20 / poll >=15s
- League Client：3 / 2 / 2 / 2 / 12 / >=20s
- Queueing：2 / 1 / 1 / 1 / 4 / >=30s
- Champ Select：2 / 1 / 1 / 1 / 0 / >=45s
- In Game：1 / 1 / 1 / 1 / 0 / >=60s；后台预取、维护和非必要视觉增强关闭
- Hidden/background：1 / 1 / 1 / 1 / 0 / >=60s

组成：`PerformanceBudget`、`LeagueActivityLevel`、`PerformanceBudgetProvider`、`PerformanceModule`、`--performance-contract-test`、`docs/PERFORMANCE-CONTRACT.md`。

从 Desktop → Client → Queueing → Champ Select → In Game，数字预算和功能开关只能更保守，不能反向放宽。

本阶段不主动轮询 Gameflow，也不为了统一而重写现有 LeagueClient/Mayhem 的 timeout/cache。League Dashboard 将成为第一个正式 Performance Budget consumer。

## 产品方向：Akari / OP.GG 参照

FACM 不复制 Akari 的 Electron/Vue 技术栈，也不以 Chromium 重 UI 为目标。继续使用当前 C#/.NET Windows 基础，学习成熟产品的功能边界、数据能力和客户端联动。

顺序：

1. League Dashboard：客户端状态、当前账号、平台/区服、Gameflow
2. Player：玩家主页、最近对局、英雄表现与统计
3. Champ Select / Current Game
4. Tools / Automation

Akari 官网“不支持腾讯服务器”只视为官方免责声明。腾讯/国服兼容按功能以源码机制 + 实际国服测试判断。

## 下一步

League Dashboard 第一版必须：

- 显式依赖 `LeagueClientModule` 与 `PerformanceModule`；
- 读取客户端连接状态、当前账号、平台/区服和真实 Gameflow；
- 将 Gameflow 映射到 `LeagueActivityLevel` 并驱动 Performance Budget；
- 从第一版采用渐进加载、取消、timeout、受限并发和不可见页面降级；
- 不在 UI thread 做网络、图片解码或批量统计；
- 不同时塞入完整战绩页、Champ Select 自动化或重型动态图表；
- 不发布新版本，除非用户再次明确授权。

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不是当前主线。
