# FACM 当前项目状态

> 2026-08-14：FACM 3.2.0 是当前正式生产基线。Performance Contract 与 League Dashboard Gate 1 已完成并合入 `main`。下一项主线：**Player / 玩家主页 + 最近战绩渐进加载**。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 当前轮次没有创建新 Release，也没有改变线上版本。

## 最新程序行为基线

- PR #79：`League Dashboard Gate 1：客户端状态与当前账号`，已 merged。
- 行为 merge commit：`f8a37066d49cf3c86778b0ae525001fe8ace633b`。
- PR 最终行为 HEAD：`7c1401e2f3ee3f7309fe46efb51f20aa93e538ef`。
- PR checks：Windows Build #937 / UI Text Contract #58 / Mayhem Source Probe #222 全部 SUCCESS。
- Windows 腾讯/国服实测：已连接；召唤师、等级、平台/区服 `CQ100`、Gameflow `Lobby`、性能档 `league-client` 正确读取。

## 已完成并冻结

- Modular Host Phase 1～5：模块所有权和依赖已稳定。
- Real Pet Gate 1：写真级真实蜜蜂已验收。
- UI Text Contract：稳定 TextKey、`[Text]`、`[Replace]` 兼容层与 CI 防回归已完成。
- Performance Contract：共享性能预算、Host 所有权与 deterministic smoke 已完成。
- League Dashboard Gate 1：客户端连接、当前账号、平台/区服、Gameflow、性能档联动、腾讯国服 discovery 已完成。

没有真实缺陷或新需求时，不要顺手重做以上基础，也不要重做单实例激活、Flying Runtime、VPet/PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务或现有配置兼容。

## Performance Contract

原则：**高配要快，普通机要顺；游戏优先，FACM 第二优先。**

预算上限：
- Desktop：network 4 / image 2 / disk 2 / CPU 2 / prefetch 20
- League Client：3 / 2 / 2 / 2 / 12
- Queueing：2 / 1 / 1 / 1 / 4
- Champ Select：2 / 1 / 1 / 1 / 0
- In Game：1 / 1 / 1 / 1 / 0；后台预取、维护和非必要视觉增强关闭
- Hidden/background：1 / 1 / 1 / 1 / 0

从 Desktop → Client → Queueing → Champ Select → In Game，数字预算和功能开关只能更保守。

League Dashboard 已成为第一个正式 Performance Budget consumer：常驻 Gameflow monitor 在首次 WinForms Idle 后启动，client 约 5s / queue 3s / champ-select 2s / in-game 10s。LCU 正常时不额外枚举游戏进程；LCU 暂不可用时才用 LeagueClient / League of Legends 进程做性能兜底。

## League / 腾讯国服基线

- 继续复用唯一 `LeagueClientModule`，不要新增平行 LCU connector。
- 正常 discovery：进程路径 → 同目录 Riot `lockfile`。
- 活动 lockfile 必须 `FileShare.ReadWrite` 共享只读，并对瞬时 IO/半写入做短重试。
- `MainModule.FileName` 失败时可用 WMI `ExecutablePath` 补路径。
- lockfile 仍失败时，仅对 `LeagueClientUx` 使用 WMI `CommandLine` fallback，并交给已有 parser；凭据只在内存使用，禁止日志/UI 输出。
- `LeagueClientSmokeTest` 保留活动 lockfile 共享句柄回归 fixture。
- Akari 官网“不支持腾讯服务器”只视为官方免责声明；腾讯兼容按源码机制 + 实机功能测试判断。

## 产品方向

FACM 不复制 Akari 的 Electron/Vue 技术栈。继续使用 C#/.NET Windows 基础，学习其功能边界、数据能力和客户端联动，同时把低资源占用作为产品差异。

顺序：
1. League Dashboard：已完成
2. Player：玩家主页、最近对局、英雄表现与统计
3. Champ Select / Current Game
4. Tools / Automation

## 下一步：Player Gate 1

第一版只做当前账号玩家主页与最近对局基础链：
- 复用 `LeagueClientModule + PerformanceModule`；
- 先显示缓存/账号头部，再加载最近 10～20 场；
- 页面离开立即取消；不可见页面不持续刷新；
- 受 Performance Budget 限制并发；In Game 禁止后台预取；
- 不创建“一场一个复杂 WinForms 控件”的无限列表，采用轻量行/回收思路；
- 不在第一版加入完整千场统计、队友侦察、自动 ban/pick 或动态图表；
- 所有静态可见文案继续走 UI Text Contract；
- 不发布新版本，除非用户再次明确授权。

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不是当前主线。
