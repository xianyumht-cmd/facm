# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.4.0
- GitHub Release：v3.4.0
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：65aa513d8b240ac7ce938cefef27ac3013b715fe
- 发布元数据提交：97e7c6124a41eb072221eceb52df6bb8b27e8c64
- Release FACM.exe SHA-256：F7BAA613A5B81E88A725F0ED7452EDEB4A98F5CD32EB34D1A58570164173F0A2
- published_at：2026-08-15T21:19:24.9354659+00:00
- release_notes：FACM 3.4.0：英雄联盟功能收束为单一入口和统一「英雄联盟中心」，对局、推荐、效率集中管理；同时修复国服自动寻找对局与自动接受兼容性。自动化仍默认关闭，继续保持轻量、低占用、低打扰。
<!-- FACM_RELEASE_STATE_END -->

> 2026-08-16：**FACM 3.4.0 已正式发布并启用在线更新。** 当前生产事实以 Release `v3.4.0` 与 `online/version.json` 为准，不再把 3.3.0 或 #120/#121 开发态描述成当前状态。

> 2026-08-17：3.4.0 在线更新已在用户 Windows 实机完成。随后发现一组**发布后真实体验问题**，已进入 Issue #125 / Draft PR #127 修正：启动后全局快捷键必须先打开 FACM 任意界面才生效；League Hub/推荐页信息密度偏低；Item Set 导航出现空白项；更新窗口显示 `3.4.0.0 / 3.4.0` 不一致。该任务是 3.4 发布后 corrective，不推翻已验收 Gate 1-7 安全模型。

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

### 3.4 发布后体验修正 — ACTIVE / Issue #125 / Draft PR #127

- 快捷键：独立 STA hidden-window 线程不再以“HWND 已创建”视为 ready；必须先由其自身消息循环实际 dispatch startup probe，再允许启动阶段注册已保存快捷键。目标是 FACM 启动后无需打开任何界面即可使用退出游戏/关闭大厅快捷键。
- 推荐：`OP.GG 一键应用` 增加三个真实操作范围：
  - `完整套装`：一次确认后复用 Gate 2 + Gate 3，应用符文、召唤师技能、FACM owned 装备集；
  - `符文与技能`：仅复用 Gate 2；
  - `装备集`：仅复用 Gate 3。
- 三个模式不伪造 OP.GG 没有提供的第二/第三套“高胜率、针对性”构筑；差异来自真实写入范围，所有既有 Champ Select / champion / queue 二次校验继续生效。
- League Hub 与 Dashboard / 一键应用页增加静态 cyan/blue/purple 高亮、响应式信息卡和选中边框；不引入持续动画或新后台 timer。
- Item Set Hub 导航改走其 own fallback，旧 `ui-text.ini` 缺键也不再出现空白按钮。
- 更新窗口仅统一版本显示为三段形式（如 `3.4.0`），版本对象、比较逻辑、manifest 与 Release 链均不变。
- 合并门槛：Windows Build / UI Text / 相关 smoke 全绿，并完成 Windows 实机复测：**程序启动后不打开任何 FACM 页面直接按快捷键** + 三种手动应用模式。

### 单按钮 / 单面板 League Hub — DONE（3.4 基线）

- 托盘与控制中心对 League 只保留一个 `英雄联盟` 主入口。
- 点击后进入唯一的 `英雄联盟中心`，不再把 Dashboard / Player / Live / OP.GG / Efficiency 分散成多个 Shell 按钮。
- Hub 只有三个用户概念：
  - 对局：概览 / 玩家主页 / 实时对局 / 海斗；
  - 推荐：OP.GG 对局助手 / 一键应用 / 推荐装备集；
  - 效率：游戏效率。
- `LeagueHubModule` 只拥有导航与页面组合，不拥有第二套 LCU session、gameflow monitor 或 writer。
- 旧业务模块继续复用已经验收的 Form/service，但不再自行注册 League Shell 入口。
- Hub 懒加载当前页；切页先正常 `Close()` 旧页，再释放页面，确保原有 Timer/CancellationToken 清理执行，避免访问过的页面后台累积。
- `ShellMenuGroups.AddLeagueAction` 保持 no-op，防止旧 UiBridge 或未来模块重新长出多个 League submenu 按钮。

### OP.GG / FACM 推荐 — DONE / 用户验收（写入安全基线）

- Gate 2：手动一键应用符文 + 召唤师技能。
- Gate 3：FACM owned Recommended item set，腾讯游戏内商店验收。
- Gate 4：选人自动应用推荐，用户验收；默认关闭、稳定 fingerprint exact-once。
- #125 只在既有 Gate 2/3 上增加手动作用域组合，不修改其 writer allowlist / owned-file / context recheck 安全语义。

### 游戏效率快捷键 — 3.4 功能已验收 / 启动生命周期修正 ACTIVE

- 一键结束国服 `League of Legends(TM)`（兼容旧 `League of Legends`）。
- 一键关闭 `LeagueClient / LeagueClientUx / LeagueClientUxRender`。
- 使用独立 STA `RegisterHotKey` message thread；无 keyboard polling / low-level hook。
- 2026-08-17 实机反馈确认：3.4.0 冷启动后若尚未打开 FACM 界面，已保存快捷键不会立即工作；打开任意 FACM 界面后恢复。Issue #125 / PR #127 修正 ready handshake，待实机复验后才能重新标记该启动场景 DONE。

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

原 League 五阶段：**5/5 DONE**。扩展 Gate2 / Gate3 / Gate4 / Gate5 / Gate6 / Gate7 的功能/安全模型均已收口。当前唯一 League 活跃任务是 3.4 实机反馈 corrective Issue #125 / Draft PR #127；完成 CI + 用户 Windows 实机复验后再收口，不把它描述成新的 Gate。

## 性能与权限冻结边界

- 唯一 `LeagueClientModule + LeagueClientSessionProvider`，不新增第二套 discovery/auth connector。
- 自动化默认关闭。
- 不做游戏内 Overlay / 注入。
- 不做自动 pick / ban / swap / reroll / dodge / skin。
- Gate2 / 赛后 / 匹配继续使用各自最小 writer allowlist。
- `LeagueEfficiencyModule` 复用 Dashboard gameflow，不新增第二个常驻 monitor。
- League Hub 只保留当前内容页，不把访问过的旧页隐藏常驻。
- InGame Performance budget 继续优先：network/image/disk/background CPU concurrency 1，prefetch 0。
- #125 的视觉增强保持静态，不加入为了“光污染”而常驻刷新/高频动画。

## 冻结的稳定系统

没有真实缺陷或新独立需求时，不重新设计：Modular Host、Performance Contract、UI Text Contract、Single-instance Ensure Open、Flying Runtime / VPet / PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务，以及已验收 League runtime / League Hub。Issue #125 属于用户 3.4 实机反馈的真实缺陷/独立 UX 修正，因此仅开放对应最小边界。

旧 Issue #33 / Draft PR #35 机器猫继续暂停。历史任务分支不删除，除非用户另行明确授权。
