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

> 当前生产仍是 **3.3.0**。2026-08-16 用户要求先完成 League Hub 单入口/单面板，再把已经验收的 Gate7 腾讯兼容修复一起正式发布和推送更新。

## 已验收并进入 main、待下一版本发布

### Gate7 国服自动找局 / 自动接受修复 — DONE / 腾讯实机验收

- Issue #118 / PR #119。
- 3.3.0 初版把未经腾讯验证的可选字段提升为硬门槛，导致两项开关无效果。
- 修复后自动找局只保留 `Lobby + canStartActivity + local leader + real member` 核心门槛；`partyId / allowedStartActivity / queueId / warnings / restrictions` 不再作为腾讯兼容硬门槛。
- 自动接受以连续 `ReadyCheck` Gameflow episode 为主触发；`/lol-matchmaking/v1/search` 只 best-effort 判断已 Accepted/Declined，缺字段或读取失败不阻止 accept。
- 保持匹配 writer exact allowlist、默认 OFF、single-episode exactly-once、InGame 零 Gate7 写入。
- Build #1050：UI Text #171 / Windows #1050 / Performance Contract SUCCESS；用户真实 Lobby → Queue → ReadyCheck 测试反馈“好使了，验收”。
- PR #119 最终 HEAD `026e3f51f16844406fb7f149c4e31953efe2ec74`：UI Text #180 / Windows #1059 SUCCESS。
- PR #119 已 squash merge，main commit：`572a3738e57de78f7ad7b9399fef39fbabb257da`。

## 当前开发任务

### League Hub — Issue #120 / Draft PR #121

用户明确要求：英雄联盟功能不再分散在多个 Shell 按钮/独立入口，收束为 **一个「英雄联盟」按钮 + 一个「英雄联盟中心」窗口**。

当前实现方向：

- 托盘和控制中心对 League 只保留一个入口；旧 League submenu 不再作为产品入口。
- `LeagueHubModule` 统一拥有 League UI navigation；Dashboard / Player / Live / Build Advisor / Efficiency 等模块继续拥有各自已验收 runtime/service，但不再自行向 Shell 注册按钮。
- `英雄联盟中心` 只有三组：
  - 对局：概览 / 玩家主页 / 实时对局 / 海斗；
  - 推荐：OP.GG 对局助手 / 一键应用 / 推荐装备集；
  - 效率：游戏效率（结束游戏、关闭大厅、点赞、返回大厅、自动找局、自动接受）。
- Hub 只懒加载当前页；切页先正常 `Close()` 旧页再 Dispose，确保旧页的 Timer/CancellationToken 清理生效，不让访问过的页面后台累积。
- 不新增第二套 LCU session / gameflow monitor / writer，也不重新设计已验收 League 功能。
- `ShellMenuGroups.AddLeagueAction` 作为 no-op 边界，防止旧 UiBridge 或未来模块重新长出多个 League Shell 按钮。
- PR #121 分支：`feat/league-hub-120`。当前 Windows CI 正在收编译/宿主问题；正式发布前以最新 HEAD 的 UI Text + Windows + Performance Contract 和 Windows UI 实机结果为准。

## 3.3.0 已验收功能基线

### 小白 Shell UX — DONE

- 托盘一级固定 5 项：打开控制中心 / 清理环境 / 英雄联盟 / 更多 / 退出程序。
- 控制中心主页采用渐进披露，不再平铺低频按钮。

### OP.GG / FACM 推荐 — DONE

- Gate 2：手动一键符文 + 召唤师技能，腾讯验收。
- Gate 3：FACM owned Recommended item set，腾讯游戏内商店验收。
- Gate 4：选人自动应用推荐，用户验收；默认关闭、稳定 fingerprint exact-once。

### 游戏效率快捷键 — DONE

- 一键结束国服 `League of Legends(TM)`（兼容旧 `League of Legends`）。
- 一键关闭 `LeagueClient / LeagueClientUx / LeagueClientUxRender`。
- 独立 STA `RegisterHotKey` message thread；无 keyboard polling / low-level hook。

### 赛后自动化 — DONE

- 随机点赞一名 eligible teammate；不点赞自己/对手/机器人。
- 自动返回大厅；点赞失败不阻止 `play-again`。
- 连续赛后 episode 最多执行一次。

## 明确取消：账号密码快捷输入

用户真实测试后明确要求“不搞这个了”。正式产品无 credential hotkey setting、无账号密码 UI、无 clipboard credential parser 入口、无 credential SendInput/UIAutomation 路径。未来除非用户重新提出独立需求，否则不得恢复。

## League 主线状态

原 League 五阶段：**5/5 DONE**；扩展 Gate2/Gate3/Gate4/Gate5/Gate6 已验收；Gate7 修复已验收并进入 main；当前只剩 Issue #120 League Hub UX 收口后统一发下一正式版本。

## 性能与权限冻结边界

- 唯一 `LeagueClientModule + LeagueClientSessionProvider`。
- 自动化默认关闭。
- 不做游戏内 Overlay / 注入。
- 不做自动 pick / ban / swap / reroll / dodge / skin。
- Gate2 / 赛后 / 匹配继续使用各自最小 writer allowlist。
- `LeagueEfficiencyModule` 复用 Dashboard gameflow，不新增第二个常驻 monitor。
- InGame Performance budget 继续优先：network/image/disk/background CPU concurrency 1，prefetch 0。

## 发布计划

- 先完成并验证 PR #121 League Hub。
- 合入 main 后验证 main Windows/UI CI。
- 用户已经授权：**League Hub 完成后，把 #119 + #120 一起正式发布并启用在线更新推送**。
- 由于包含明显产品 UX 收口，不按 3.3.1 小补丁处理；正式版本在 Hub 完成后按实际发布范围确定。
- 发布仍走 `.github/workflows/publish-release.yml` 事务，不用普通 CI artifact 冒充正式包。

## 冻结的稳定系统

没有真实缺陷或新独立需求时不重新设计：Modular Host、Performance Contract、UI Text Contract、Single-instance Ensure Open、Flying Runtime / VPet / PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务，以及已验收 League runtime。

旧 Issue #33 / Draft PR #35 机器猫继续暂停。
