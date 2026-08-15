# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.3.0
- GitHub Release：v3.3.0
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：73d49964fdcc8e3a1441dfbcc605d07f7a4ce7c0
- 发布元数据提交：5877ffbe8d69eb1ed826186574daefce6d85b4e0
- Release FACM.exe SHA-256：74649FEC5153A3D47681529D892227F28EB395FC68460D0D12FC8B9D3B9C9C2F
- published_at：2026-08-15T05:13:06.9356240+00:00
- release_notes：FACM 3.3.0：控制中心与托盘菜单进一步收束，新增选人自动应用 FACM 推荐、全局一键结束游戏/关闭大厅、赛后随机点赞与自动返回大厅，以及可选的自动寻找和接受对局。自动化默认关闭，继续保持轻量、低占用、低打扰。
<!-- FACM_RELEASE_STATE_END -->

> 2026-08-15：**FACM 3.3.0 已正式发布并启用在线更新。** Release `v3.3.0`、在线 manifest 与 3.3.0 公告均为当前生产事实。不要再把 3.2.0 或 3.3.0 候选状态当作当前生产状态。
>
> 2026-08-16 腾讯真实使用回归：3.3.0 的 **自动寻找对局 / 自动接受对局确认无效果**。Issue #118 / Draft PR #119 正在修复；在用户重新实机验收前，不得把这两项描述成国服可用。当前生产 3.3.0 其它已验收功能不受该结论影响，也没有因此自动发布新的线上版本。

## 3.3.0 发布证据

- 3.3 集成 PR #115：merged，merge commit `778393b3096824f858919a82e4c0c6425dcd2024`。
- 集成最终 HEAD `3b77d78b391ac140121a7270ee6d24491829054e`：UI Text #164 / Windows #1043 / Mayhem #281 SUCCESS。
- merge 后 main `778393b3...`：UI Text #165 / Windows #1044 / Mayhem #282 SUCCESS。
- 发布请求 PR #116：merged，merge commit `73d49964fdcc8e3a1441dfbcc605d07f7a4ce7c0`。
- 发布请求 HEAD `e6295cf988282483f4dd273be5140f4bac33d0e8`：UI Text #166 / Windows #1045 SUCCESS。
- `FACM Publish Release` #7 / run `31866328728`：SUCCESS；build、embedded resource verify、sign、disabled manifest、metadata commit、draft release、publish release、enable online manifest 全部成功。
- GitHub Release：`https://github.com/xianyumht-cmd/facm/releases/tag/v3.3.0`。
- 正式下载：`https://github.com/xianyumht-cmd/facm/releases/download/v3.3.0/FACM.exe`。
- Release asset size：78,282,136 bytes。
- Release FACM.exe SHA-256：`74649FEC5153A3D47681529D892227F28EB395FC68460D0D12FC8B9D3B9C9C2F`。
- `online/version.json`：enabled=true / version=3.3.0 / minimum_version=3.0.0 / force_update=false，SHA 与 Release asset 一致。

## 3.3.0 正式功能范围

### 小白版 Shell UX — DONE / 用户验收

- 托盘一级固定 5 项：打开控制中心 / 清理环境 / 英雄联盟 / 更多 / 退出程序。
- League、OP.GG、游戏效率等业务功能只能进入二级菜单。
- 控制中心主页采用渐进披露，不再把大量低频按钮平铺。
- `ShellMenuGroups + ShellUxSmokeTest` 防止后续一级入口重新膨胀。

### OP.GG / FACM 推荐自动化 — DONE / 用户验收

- Gate 2 手动一键应用符文 + 召唤师技能：腾讯实机验收并冻结。
- Gate 3 Recommended item set：腾讯游戏内商店确认识别；品牌标题统一 `[FACM]`；`facm1-*` ownership 与原子写/读回验证冻结。
- Gate 4 选人自动应用推荐：用户验收通过；默认关闭；稳定 fingerprint 每个上下文只自动执行一次；符文、技能、推荐装备共用既有安全事务。
- Advisor 展示与自动应用共享 OP.GG raw payload cache，避免同路径重复请求。

### 游戏效率快捷键 — DONE / 用户实机验收

正式版只包含两项：

- 一键结束游戏：全局快捷键精确结束国服 `League of Legends(TM)`，兼容旧 `League of Legends`。
- 一键关闭大厅：全局快捷键精确关闭 `LeagueClient / LeagueClientUx / LeagueClientUxRender`。
- 使用独立 STA `RegisterHotKey` message thread；FACM 后台/最小化不影响触发；无键盘轮询/low-level hook。

### 赛后自动化 — DONE / 用户验收

- 自动随机点赞一名 eligible teammate；不点赞对手/自己/机器人。
- 自动返回大厅；点赞失败不阻止 `play-again`。
- 同一连续赛后 episode 最多执行一次，不无限重试。
- 默认关闭。

### 自动下一局 — 3.3.0 RELEASED / 腾讯回归确认失败 / #118 修复中

- 3.3.0 已包含自动寻找对局与自动接受 ReadyCheck，且两项默认关闭。
- 发布前只有 deterministic smoke，用户当时明确授权“先发布，真实使用发现问题再修”，没有腾讯实机验收结论。
- 2026-08-16 用户在国服真实使用确认：两项开关均无效果；因此当前状态是 **生产已发布但国服不可视为可用**。
- 根因初步确认是 FACM 第一版把未经腾讯验证的可选字段提升成硬门槛：自动找局强依赖 `partyId / allowedStartActivity / queueId / warnings/restrictions`；自动接受强依赖 `/lol-matchmaking/v1/search` 的 `lobbyId / queueId / readyCheck.state`。
- Issue #118 / Draft PR #119 从生产 main 独立修复：找局只保留 `canStartActivity + isLeader + real member` 核心门槛；自动接受改为以 Gameflow `ReadyCheck` episode 为主触发，search state 仅用于 best-effort 检查已 Accepted/Declined。
- 修复继续保持唯一 League session、专用 Gate7 writer exact allowlist、默认 OFF、single-episode exactly-once、InGame 零 Gate7 写入。
- 首轮修复行为 HEAD `965ca170a766369dd341e3a47ae406975c102199`：UI Text #170 SUCCESS / Windows #1049 SUCCESS；Windows 日志明确 `FACM performance contract smoke passed.`。文档提交后以最新 HEAD CI 为最终候选依据。
- **PR #119 不合并、不发布，直到用户用腾讯真实 Lobby → Queue → ReadyCheck 重新验证。**

## 明确取消：账号密码快捷输入

用户在真实腾讯/QQ 登录页测试后明确要求 **“不搞这个了”**。3.3.0 已物理剔除该能力：

- 无 credential hotkey setting；
- 无账号密码输入 UI；
- 无 clipboard credential parser 入口；
- 无 credential SendInput path；
- 无 UIAutomation credential focus 依赖。

未来除非用户重新提出为新独立需求，否则不得顺手恢复。

## League 主线状态

原 League 五阶段：**5/5 = 100% DONE**。

1. League Dashboard Gate 1 — DONE / 腾讯验收
2. Player Gate 1 — DONE / 腾讯验收
3. Champ Select / Current Game Gate 1 — DONE / 腾讯验收
4. Player Gate 2 — DONE / 腾讯验收
5. Tools / Automation Gate 1（只读 OP.GG 对局助手）— DONE / 腾讯验收

5/5 之后的扩展：

- Gate 2：手动一键应用符文 + 召唤师技能 — DONE / merged / 腾讯验收
- Gate 3：Recommended item set — DONE / 腾讯商店验收
- Gate 4：自动应用推荐 — DONE / 3.3.0
- League Efficiency Gate 5：结束游戏 + 关闭大厅快捷键 — DONE / 3.3.0
- Gate 6：随机点赞 + 自动返回大厅 — DONE / 3.3.0
- Gate 7：自动找局 + 自动接受 — 3.3.0 已发布，但腾讯实机确认无效果；Issue #118 / PR #119 修复中

## 3.3 性能与权限冻结边界

- 唯一 `LeagueClientModule + LeagueClientSessionProvider`，不新增第二套 discovery/auth connector。
- 自动化默认关闭。
- 不做游戏内 Overlay/注入。
- 不做自动 pick / ban / swap / reroll / dodge / skin。
- Gate 2、赛后、匹配分别使用最小 writer allowlist；Gate 2 writer 继续硬拒绝 ready-check accept。
- `LeagueEfficiencyModule` 复用 Dashboard 已有 gameflow，不新增第二个常驻 gameflow monitor。
- In Game Performance budget：network/image/disk/background CPU concurrency 1，prefetch 0，非必要后台维护/视觉增强关闭。

## 仓库收口

- PR #115 是 3.3.0 最终功能集成来源。
- 旧并行 PR #105（Shell UX）、#110（Gate5）、#112（Gate6）、#114（Gate7）已标记为 **superseded by #115 / v3.3.0**，不得再分别合并。
- 对应 Issue #104 / #109 / #111 / #113 / 总览 #108 已在记录 3.3.0 吸收关系后关闭 completed；#109 已注明 credential 子需求明确取消。
- Gate7 的生产回归使用新 Issue #118 / PR #119 独立追踪，不重开旧 stacked PR。
- 不删除这些分支，除非用户另行明确授权。

## 冻结的稳定系统

没有真实缺陷或新独立需求时，不重新设计：

- Modular Host
- Performance Contract
- UI Text Contract
- Single-instance Ensure Open / Activate
- Flying Runtime / VPet / PetHost
- Cleanup 安全语义
- Mayhem 多源容灾
- Online Release 事务
- 已验收 League Dashboard / Player / Live / OP.GG Read Advisor / Gate2 Apply / item-set ownership

旧 Issue #33 / Draft PR #35 机器猫继续暂停，不随 3.3.0 恢复。
