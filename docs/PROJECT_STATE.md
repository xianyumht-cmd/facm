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

> 当前生产事实以 GitHub Release `v3.4.2` 与 `online/version.json` 为准。3.4.0 / 3.4.1 的回归与修复记录属于历史，不再描述为当前进行中状态。

## 当前开发：海克斯大乱斗可用英雄快速选择（Issue #134 / Draft PR #135）

- 目标：在 `对局 → 实时对局` 内提供类似 OP.GG Champion Select 的 **可用英雄快速选择**，直接映射客户端 Champ Select Bench。
- 读取现有 `/lol-champ-select/v1/session` 的 `benchEnabled` / `benchChampionIds`，不建立第二套 LCU discovery / auth / session。
- Bench 激活且页面可见时，使用 session-only 轻量刷新追踪可用英雄；正常 Live Champ Select 刷新保持原 2 秒节奏，InGame / 最小化继续节流。
- 英雄头像仅按需从本地 LCU `/lol-game-data/assets/v1/champion-icons/{id}.png` 读取并缓存，不请求外网、不做后台预取。
- 用户点击英雄后才执行一次 `POST /lol-champ-select/v1/session/bench/swap/{championId}`；写前重新确认目标仍在 Bench，目标已被别人拿走则不发送 POST。
- 每次点击最多一次 swap POST；2xx 后只做有界只读 settled verification，未真正切换到目标英雄不得误报成功。
- Bench swap 使用独立最小 writer；Gate2 writer 不放宽，仍拒绝 bench swap 与 `/lol-champ-select/v1/session/actions/{id}`。
- **不做自动抢英雄**：不监控指定目标后自动 swap，不做自动 pick / ban / reroll / dodge / skin；“抢英雄”只指用户在 FACM 里手动点击得更快。
- 当前仍是 Draft 候选，未合并 `main`、未修改正式 Release / online manifest；CI 全绿后先做腾讯/国服真实 ARAM Mayhem 实机验收。

## 3.4.2 发布与回归证据

- 3.4.1 腾讯 Windows 实机回归中，用户确认 `一键退出游戏` 已恢复可用；新触发链在日志中产生成功记录。
- 同一轮实机日志显示推荐中心装备集写入成功，但 Gate2 符文 / 召唤师技能缺少足够终态诊断，因此继续修复一键应用。
- PR #131：`修复一键应用：复用 FACM 符文页并补齐实机诊断`。
  - 不再每次无条件新建 `[FACM]` 符文页。
  - 优先复用当前同名 `[FACM]` 页。
  - 自定义页容量已满时，只允许复用 FACM 自有页，不覆盖普通用户符文页。
  - 保留 settled read-back；LCU 2xx 不直接等于真实成功。
  - 补齐 prepare / blocked / skip / rune / spell 终态日志。
- PR #131 HEAD `2ebfb9c2832184f545e68e74591165d0ccc6f09d`：FACM UI Text Contract #230 SUCCESS；FACM Windows Build #1109 SUCCESS。
- PR #131 已合并到 `main`，merge commit `49440ce4897b12fca062474098cc5e9c642f1782`。
- 发布请求 PR #132 已合并，merge commit `bc2603976dd9691172401778656b50429864dfed`。
- `FACM Publish Release` run `32055053102`：SUCCESS；正式 build、内嵌资源验证、签名、disabled manifest、版本元数据、Release 发布、启用 online manifest 全部成功。
- Release target / 发布元数据 commit：`252ae023428bfa0a57dcbbd4ec273953ebf49440`。
- 在线更新启用 commit：`ca462a4026a8368a63d4ed806359900c151084ae`。
- GitHub Release：`v3.4.2`。
- 正式下载：`https://github.com/xianyumht-cmd/facm/releases/download/v3.4.2/FACM.exe`。
- Release FACM.exe SHA-256：`B0F31DA0F158301507EFA6567F3115CF3893B34FD07717508E5743A2FF1FF5D1`。
- `online/version.json`：enabled=true / version=3.4.2 / minimum_version=3.0.0 / force_update=false，下载地址与 SHA 和 Release asset 一致。

## 当前 League 产品状态

### 单按钮 / 单面板 League Hub — RELEASED

当前正式版已完成 League Hub 收束：

- 托盘与控制中心对 League 只保留一个 `英雄联盟` 主入口。
- 点击后进入唯一的 `英雄联盟中心`，不再把 Dashboard / Player / Live / OP.GG / Efficiency 分散为多个 Shell 按钮。
- 左侧用户概念收束为 **对局 / 推荐 / 效率**。
- `对局` 下保留概览、玩家主页、实时对局、海斗等子入口。
- `推荐` 使用统一推荐中心，不再要求用户分别理解 OP.GG 对局助手 / 一键应用 / 推荐装备集三个旧入口。
- 推荐中心同屏提供 **符文 / 召唤师技能 / 推荐装备集**，并统一预览与应用。
- `LeagueHubModule` 只负责导航与页面组合，不拥有第二套 LCU session、gameflow monitor 或 writer。
- Hub 切页正常释放旧内容页，避免 Timer / CancellationToken 在后台累积。
- 视觉使用静态蓝 / 青 / 紫灯带、描边和选中状态，不增加动画 RGB Timer 或新的高频常驻刷新。

### OP.GG / FACM 推荐 — RELEASED；3.4.2 加固

- Gate2：手动一键应用符文 + 召唤师技能。
- Gate3：FACM owned Recommended item set，腾讯游戏内商店已验收。
- Gate4：选人自动应用推荐，默认关闭、稳定 fingerprint exact-once。
- 手动应用前继续执行 Champ Select / champion / queue 上下文校验。
- 召唤师技能保留 Flash 槽位偏好并做写后读回验证。
- 符文优先复用同名 `[FACM]` 自有页；容量满时只复用 FACM 自有页，不修改普通用户符文页。
- 如果没有安全可复用页，继续 fail-closed，不扩大写权限。
- 装备集仍保持独立 FACM owned 文件边界。
- 3.4.2 已新增 Gate2 终态诊断日志，后续腾讯实机问题应优先依据日志定位，不再靠 UI 现象猜测。

### 游戏效率快捷键 — RELEASED；3.4.1 实机修复确认

当前动作目标：

- 一键结束国服 `League of Legends(TM)`（兼容旧 `League of Legends`）。
- 一键关闭 `LeagueClient / LeagueClientUx / LeagueClientUxRender`。

3.4.0 曾出现 FACM 启动后、未打开任何 FACM 界面时快捷键不响应的腾讯 Windows 实机回归。3.4.1 已补强后台触发链，用户随后确认 `一键退出游戏` 可用。当前不再把该问题描述为进行中回归。

### 赛后自动化 — DONE / 用户验收

- 随机点赞一名 eligible teammate；排除自己 / 对手 / 机器人。
- 自动返回大厅；点赞失败不阻止 `play-again`。
- 连续赛后 episode 最多执行一次。
- 默认关闭。

### 自动下一局 — DONE / 腾讯实机验收

- 自动寻找对局：保留 `Lobby + canStartActivity + local leader + real member` 核心安全门槛。
- 自动接受：以连续 `ReadyCheck` Gameflow episode 为主触发；`/lol-matchmaking/v1/search` 只 best-effort 判断已 Accepted / Declined。
- Gate7 writer 只允许 search + ready-check accept；同一 episode / fingerprint exact-once；InGame 零 Gate7 写入。
- 默认关闭。

## 明确取消：账号密码快捷输入

用户真实测试后明确要求“不搞这个了”。正式产品无 credential hotkey setting、无账号密码 UI、无 clipboard credential parser 入口、无 credential SendInput / UIAutomation 路径。未来除非用户重新提出独立需求，否则不得恢复。

## League 主线状态

原 League 五阶段：**5/5 DONE**。扩展 Gate2 / Gate3 / Gate4 / Gate5 / Gate6 / Gate7 的运行时能力均已收口并进入正式版本。当前没有把 3.4.0 的旧 #124 / #129 开发态当作进行中工作。

若 3.4.2 后续腾讯实机仍报告一键应用异常，优先读取 3.4.2 新增的 Gate2 prepare / blocked / skip / rune / spell 日志，再决定是否开新的独立 Issue / task branch。

## 性能与权限冻结边界

- 唯一 `LeagueClientModule + LeagueClientSessionProvider`，不新增第二套 discovery / auth connector。
- 自动化默认关闭。
- 不做游戏内 Overlay / 注入。
- 不做自动 pick / ban / 自动 Bench swap / reroll / dodge / skin；Issue #134 的手动 Bench swap 是用户点击触发的独立例外。
- Gate2 / Bench swap / 赛后 / 匹配继续使用彼此独立的最小 writer 边界，不互相放宽 allowlist。
- `LeagueEfficiencyModule` 复用 Dashboard gameflow，不新增第二个常驻 monitor。
- League Hub 只保留当前内容页，不把访问过的旧页隐藏常驻。
- 全局快捷键不引入低级键盘钩子或高频键盘轮询。
- 静态霓虹视觉只在正常 WinForms Paint 中绘制，不新增高频动画 Timer。
- InGame Performance budget 继续优先：network / image / disk / background CPU concurrency 1，prefetch 0。

## 冻结的稳定系统

没有真实缺陷或新独立需求时，不重新设计：Modular Host、Performance Contract、UI Text Contract、Single-instance Ensure Open、Flying Runtime / VPet / PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务，以及已验收 League runtime / Gate2-Gate7 writer / service 边界。

旧 Issue #33 / Draft PR #35 机器猫继续暂停。历史任务分支不删除，除非用户另行明确授权。
