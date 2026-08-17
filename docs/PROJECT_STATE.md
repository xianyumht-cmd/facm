# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.4.2
- GitHub Release：v3.4.2
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：bc2603976dd9691172401778656b50429864dfed
- 发布元数据提交：252ae023428bfa0a57dcbbd4ec273953ebf49440
- Release FACM.exe SHA-256：B0F31DA0F158301507EFA6567F3115CF3893B34FD07717508E5743A2FF1FF5D1
- published_at：2026-08-17T18:30:02.4206560+00:00
- release_notes：FACM 3.4.2：继续修复英雄联盟推荐中心一键应用。符文不再每次都新建一个 FACM 自定义页，而是优先复用同名 [FACM] 页；自定义符文页容量已满时，也只复用 FACM 自有页，绝不覆盖普通用户符文页。同时补齐符文与召唤师技能的一键应用实机日志，可明确记录准备、跳过、阻止以及最终 rune/spell 状态。3.4.1 的游戏内一键退出修复继续保留。
<!-- FACM_RELEASE_STATE_END -->

> 2026-08-16：**FACM 3.4.0 已正式发布并启用在线更新。** 当前生产事实以 Release `v3.4.0` 与 `online/version.json` 为准，不再把 3.3.0 或 #120/#121 开发态描述成当前状态。
>
> 2026-08-17 腾讯 Windows 实机回归：3.4.0 的 `一键退出游戏 / 一键关闭大厅` 在 FACM 启动后、未打开任何 FACM 界面时可能不响应；打开任意 FACM 界面后才恢复。Issue #124 / Draft PR #129 正在修复。**因此 3.4.0 不能再描述成“后台全局快捷键已完全验收”。** 当前线上版本仍是 3.4.0，本任务尚未发布新版本或修改 online manifest。

## 3.4.0 发布证据

- Gate7 腾讯兼容修复：Issue #118 / PR #119；用户真实 Lobby → Queue → ReadyCheck 测试反馈“好使了，验收”。
- PR #119 最终 HEAD `026e3f51f16844406fb7f149c4e31953efe2ec74`：UI Text #180 / Windows #1059 SUCCESS；squash merge main `572a3738e57de78f7ad7b9399fef39fbabb257da`。
- League Hub：Issue #120 / PR #121；最终 HEAD `8f12e04e320c96a99346abf71e6cf64b81e71597`：UI Text #191 / Windows #1070 / Mayhem #295 SUCCESS，Windows 日志包含 `FACM performance contract smoke passed.`。
- PR #121 squash merge main `4a0fa0052b185f3f42680448d51d8d082a5e1053`；merge 后 main：UI Text #192 / Windows #1071 / Mayhem #296 SUCCESS。
- 发布请求 PR #122：HEAD `434965b85b1e7ced327a36f54b0bbccdf022f4da`，UI Text #193 / Windows #1072 SUCCESS；merge commit `65aa513d8b240ac7ce938cefef27ac3013b715fe`。
- `FACM Publish Release` #8 / run `31909049682`：SUCCESS；正式 build、内嵌资源验证、签名、disabled manifest、版本元数据、draft Release、公开 Release、启用 online manifest 全部成功。
- Release target / 发布元数据 commit：`97e7c6124a41eb072221eceb52df6bb8b27e8c64`。
- 在线更新启用 commit：`040af79a9c14084c94ecb74a358b98dbd791d02a`。
- GitHub Release：`https://github.com/xianyumht-cmd/facm/releases/tag/v3.4.0`。
- 正式下载：`https://github.com/xianyumht-cmd/facm/releases/download/v3.4.0/FACM.exe`。
- Release FACM.exe SHA-256：`F7BAA613A5B81E88A725F0ED7452EDEB4A98F5CD32EB34D1A58570164173F0A2`。
- `online/version.json`：enabled=true / version=3.4.0 / minimum_version=3.0.0 / force_update=false，下载地址与 SHA 和 Release asset 一致。

## 当前 League 产品状态

### 单按钮 / 单面板 League Hub — 3.4.0 DONE；#124 v2 候选中

3.4.0 已完成基础收束：

- 托盘与控制中心对 League 只保留一个 `英雄联盟` 主入口。
- 点击后进入唯一的 `英雄联盟中心`，不再把 Dashboard / Player / Live / OP.GG / Efficiency 分散成多个 Shell 按钮。
- `LeagueHubModule` 只拥有导航与页面组合，不拥有第二套 LCU session、gameflow monitor 或 writer。
- 旧业务模块继续复用已经验收的 Form/service，但不再自行注册 League Shell 入口。
- Hub 切页会正常关闭旧内容页，避免 Timer/CancellationToken 在后台累积。

2026-08-17 用户实机认为第一版 Hub 仍偏工程化、推荐功能过于分散，并指出推荐区域存在一个空白名称按钮。#124 / PR #129 的 v2 方向已经冻结为：

- 左侧只保留 **对局 / 推荐 / 效率** 三个真正的用户概念；
- `对局` 的概览 / 玩家主页 / 实时对局 / 海斗改为顶部紧凑子导航；
- `推荐` 不再暴露 OP.GG 对局助手 / 一键应用 / 推荐装备集三个旧入口，合并成一个统一 `推荐中心`；
- 推荐中心同屏明确提供 **符文 / 召唤师技能 / 推荐装备集** 三个可选项、统一预览与 `应用已选推荐`；
- 自动应用仍使用完整推荐，手动勾选不会改变自动应用语义；
- Advisor / Gate2 / Gate3 继续复用已验收 service/writer，Hub 不扩大 LCU 权限；
- 统一推荐链复用现有 OP.GG cache，避免同一英雄重复抓取同一 payload；
- 视觉只增加静态蓝 / 青 / 紫灯带、描边和选中状态，不增加动画 RGB Timer 或新的常驻刷新。

### OP.GG / FACM 推荐 — 底层 DONE / 用户验收；v2 UI 候选中

- Gate 2：手动一键应用符文 + 召唤师技能。
- Gate 3：FACM owned Recommended item set，腾讯游戏内商店验收。
- Gate 4：选人自动应用推荐，用户验收；默认关闭、稳定 fingerprint exact-once。
- #124 只重组用户操作层，不重新设计上述 writer/service 安全边界。

### 游戏效率快捷键 — 3.4.0 RELEASED / 后台触发回归；#124 修复中

3.4.0 的动作目标本身仍已实机确认：

- 一键结束国服 `League of Legends(TM)`（兼容旧 `League of Legends`）。
- 一键关闭 `LeagueClient / LeagueClientUx / LeagueClientUxRender`。

但 2026-08-17 实机发现：FACM 启动后如果从未打开 FACM 任意界面，两个快捷键可能没有效果；一旦打开过界面又可正常触发。#124 / PR #129 不改变进程结束规则，而是只替换接收层：

- 专用原生 Windows 线程显式创建 thread message queue；
- 使用 `RegisterHotKey(NULL, ...)` 将热键绑定到该线程，而不是隐藏 WinForms `NativeWindow`；
- 通过 `GetMessage` 接收 `WM_HOTKEY`，设置变更 / 退出通过 `PostThreadMessage`；
- 不依赖 FACM 窗口前台、激活或是否打开；
- 继续 **0 keyboard polling / 0 low-level keyboard hook**。

在用户用腾讯 Windows 候选包重新验证“FACM 启动后完全不点任何界面也能触发”之前，不把此回归标为关闭。

### 赛后自动化 — DONE / 用户验收

- 随机点赞一名 eligible teammate；排除自己/对手/机器人。
- 自动返回大厅；点赞失败不阻止 `play-again`。
- 连续赛后 episode 最多执行一次。
- 默认关闭。

### 自动下一局 — DONE / 腾讯实机验收

- 自动寻找对局：腾讯兼容修复后只保留 `Lobby + canStartActivity + local leader + real member` 核心安全门槛；未经腾讯验证的可选字段不再阻断。
- 自动接受：以连续 `ReadyCheck` Gameflow episode 为主触发；`/lol-matchmaking/v1/search` 只 best-effort 判断已 Accepted/Declined。
- Gate7 writer 仍只允许 search + ready-check accept；同一 episode/fingerprint exact-once；InGame 零 Gate7 写入。
- 默认关闭。

## 明确取消：账号密码快捷输入

用户真实测试后明确要求“不搞这个了”。正式产品无 credential hotkey setting、无账号密码 UI、无 clipboard credential parser 入口、无 credential SendInput/UIAutomation 路径。未来除非用户重新提出独立需求，否则不得恢复。

## League 主线状态

原 League 五阶段：**5/5 DONE**。扩展 Gate2 / Gate3 / Gate4 / Gate5 / Gate6 / Gate7 的运行时能力均已收口。当前唯一进行中的 League 工作是 Issue #124 / PR #129：修复 3.4.0 后台全局快捷键回归，并把 3.4 Hub 的推荐/导航体验继续收束。该任务尚未合并、尚未发布。

## 性能与权限冻结边界

- 唯一 `LeagueClientModule + LeagueClientSessionProvider`，不新增第二套 discovery/auth connector。
- 自动化默认关闭。
- 不做游戏内 Overlay / 注入。
- 不做自动 pick / ban / swap / reroll / dodge / skin。
- Gate2 / 赛后 / 匹配继续使用各自最小 writer allowlist。
- `LeagueEfficiencyModule` 复用 Dashboard gameflow，不新增第二个常驻 monitor。
- League Hub 只保留当前内容页，不把访问过的旧页隐藏常驻。
- 全局快捷键使用 Windows 消息驱动，不新增键盘轮询。
- 静态霓虹视觉只在正常 WinForms Paint 中绘制，不新增高频动画 Timer。
- InGame Performance budget 继续优先：network/image/disk/background CPU concurrency 1，prefetch 0。

## 冻结的稳定系统

没有真实缺陷或新独立需求时，不重新设计：Modular Host、Performance Contract、UI Text Contract、Single-instance Ensure Open、Flying Runtime / VPet / PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务，以及已验收 League runtime / Gate2-Gate7 writer/service 边界。

旧 Issue #33 / Draft PR #35 机器猫继续暂停。历史任务分支不删除，除非用户另行明确授权。
