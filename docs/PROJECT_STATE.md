# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.5.14
- GitHub Release：v3.5.14
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：5a517aef63c9cca5a48ed1dcc8fe1fbdf1ab0203
- 发布元数据提交：053b0f8ed3f6bc722c05ebfbbf46e1ec47c2b4f5
- Release FACM.exe SHA-256：0F63F634AEF2AC5AC74B78BE4DDAC430DDF7AF5D937D805BD8067057E60BD8ED
- published_at：2026-08-27T03:07:57.9488993+00:00
- release_notes：FACM 3.5.14：统一普通顶层窗口为 FACM 自绘外壳，移除系统原生标题栏按钮，统一关闭、最小化、最大化、拖动、缩放与圆角行为；临时窗口支持点击窗口外桌面或切换到其它程序时关闭，窗口内部空白不关闭，同一 FACM 进程的子对话框不会误关父窗口，并修复自绘最小化被外部关闭逻辑误判的问题。LOL 工作台进一步收紧为 1120×640 默认尺寸、900×580 最低尺寸，并把当前状态、快捷工具、在线状态等稀疏页面的宽屏空白改为真实上下文区，展示客户端连接、对局阶段与相关快捷入口。上下文状态只复用现有 LeagueDashboard gameflow 缓存，不新增 LCU session、轮询、writer、网络请求或动画 Timer。
<!-- FACM_RELEASE_STATE_END -->

> 当前生产事实始终以上方发布工作流维护区块、GitHub Release 与 `online/version.json` 为准。更早版本记录仅作为历史回归证据。

## 3.5.15 产品变更基线

- PR #182 已完成控制中心与修复信息架构重构：控制中心只保留 `清理与修复 / LOL 工作台 / 个性化 / 更多设置` 四个桌面式入口；工作目录、环境状态与步骤说明进入 `清理与修复` 页面。
- `清理与修复` 统一环境级流程：游戏目录 → 驱动修复 / 环境清理（先后不限，建议各执行一次）→ 重启电脑 → WEGAME → 英雄联盟 → 修复游戏；FACM 不伪造 WEGAME 最终修复完成状态。
- LOL 工作台删除重复内容区提示条，当前页提示进入 FACM 自绘标题栏副标题；`ThemeCatalog + FacmThemeRuntime + FacmDesignSystem` 统一 FACM 自有窗口主题。
- 游戏运行期间的大厅/客户端异常归入 `LOL 工作台 → 自动化 → 游戏修复`。PR #183 将原 `fix-lcu-window` mode 1～4 正式迁为 FACM 原生实现，不再从 UI 启动旧 `Fix-LCU-Window.exe`。
- `立即修复窗口`：按 LeagueClientUx 实际所在显示器和 WorkingArea 处理多屏/负坐标；16:9 判断使用容差；优先恢复最近合理尺寸或保留可信宽/高，不再固定 `PrimaryScreen + 1280×720×zoom`。
- `自动修复窗口`：改为 WinEvent `EVENT_OBJECT_LOCATIONCHANGE` + 380ms debounce + 2s cooldown；默认关闭，仅本次 FACM 进程会话有效；不再启动独立 console，也没有 1500ms 常驻轮询。
- `跳过卡结算`：复用现有 Gate 6 `/lol-lobby/v2/play-again` writer；`重启客户端界面`：使用只暴露 `POST /riotclient/kill-and-restart-ux` 的专用窄 writer；二者都复用唯一 `LeagueClientModule + LeagueClientSessionProvider`。
- `一键结束游戏` 继续使用原进程级动作，与跳过卡结算、真实赛后自动回大厅保持独立语义。
- `FACM.ToolBundle` 不再嵌入旧 Fix-LCU-Window EXE 与 mode scripts；历史工具输入可以保留在源码仓库作为来源/回归证据，但不进入正式 FACM 游戏修复运行路径。
- `--facm-host-test` 增加原生修复纯离线回归：合理/异常窗口、可信宽度恢复、最近合理尺寸、负坐标显示器 clamp、Client UX writer allowlist 与既有 play-again writer 边界。
- 下一阶段的 `.NET 8+ / WinUI 3` 技术栈升级不属于本变更，不与 3.5.15 混做。

## 3.4.3 海克斯大乱斗可用英雄快速选择 — RELEASED

- Issue #134：`海克斯大乱斗：可用英雄快速选择（Bench Swap）`。
- PR #135：`海克斯大乱斗：可用英雄快速选择`，已合并到 `main`，merge commit `5d4cb6861d130ae6525a6f9ab1eb5a8ce61e551e`。
- PR #135 HEAD `033665701bc79f10f94b25768c9dc52468f8dfe7`：FACM UI Text Contract #239 SUCCESS；FACM Windows Build #1118 SUCCESS。
- 发布请求 PR #136 已合并，merge commit `3e816f33507e90fbacf0fcd74b136bcbfc91ac87`。
- 发布元数据 commit：`d13e5face98ea528699422112e53714f6e506c16`。
- 在线更新启用 commit：`956da4966e6500a57339922bae3f28c062b3e2c7`。
- GitHub Release：`v3.4.3`；`online/version.json` 当时启用 3.4.3，SHA-256 `4B477BDE7B8D4D99134A11A5D461E5DFA32CEA477A2133CA9D8B3CE00DB7FE47`。
- 功能位于 `比赛 → 实时对局`：读取现有 `/lol-champ-select/v1/session` 的 `benchEnabled` / `benchChampionIds`，不建立第二套 LCU discovery / auth / session。
- Bench 激活且页面可见时，使用 session-only 轻量刷新追踪可用英雄；正常 Live Champ Select 刷新保持原 2 秒节奏，InGame / 最小化继续节流。
- 英雄头像仅按需从本地 LCU `/lol-game-data/assets/v1/champion-icons/{id}.png` 读取并缓存，不请求外网、不做后台预取。
- 用户点击英雄后才执行一次 `POST /lol-champ-select/v1/session/bench/swap/{championId}`；写前重新确认目标仍在 Bench，目标已被别人拿走则不发送 POST。
- 每次点击最多一次 swap POST；2xx 后只做有界只读 settled verification，未真正切换到目标英雄不得误报成功。
- Bench swap 使用独立最小 writer；Gate2 writer 不放宽，仍拒绝 bench swap 与 `/lol-champ-select/v1/session/actions/{id}`。
- **不做自动抢英雄**：不监控指定目标后自动 swap，不做自动 pick / ban / reroll / dodge / skin；“抢英雄”只指用户在 FACM 里手动点击得更快。

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
- `online/version.json` 当时为 enabled=true / version=3.4.2 / minimum_version=3.0.0 / force_update=false。

## 当前 League 产品状态

### 单入口 LOL 工作台 — RELEASED

当前正式版已完成 League 产品入口收束；3.5.7 后持续做上下文化，3.5.14 已统一普通顶层窗口自绘外壳并利用稀疏页面宽屏空白：

- 托盘与控制中心对 League 只保留一个 `英雄联盟` 主入口。
- 点击后进入唯一的 `LOL 工作台`，不再把 Dashboard / Player / Live / OP.GG / Efficiency 分散为多个 Shell 按钮。
- 左侧用户概念为 **比赛 / 攻略 / 自动化**。
- 工作台右侧提供「接着做」上下文栏，按当前功能给出 3～4 个强相关下一步；海斗、出装、实时、战绩和快捷工具可在同一工作台连续切换，不额外打开一层功能窗口。
- 窗口较窄时相关栏自动隐藏，空间优先留给主内容。
- `LeagueHubModule` 只负责导航与页面组合，不拥有第二套 LCU session、gameflow monitor 或 writer。
- Hub 仍只保留当前子 Form；切页正常 Close/Dispose，避免 Timer / CancellationToken 在后台累积。
- 视觉使用静态蓝 / 青 / 紫灯带、描边和选中状态，不增加动画 RGB Timer 或新的高频常驻刷新。

### 海斗实战决策卡 — RELEASED in 3.5.8

- PR #168：`海斗升级为实战决策助手`，已 squash 合并到 `main`，merge commit `0125c69f6f3cd3d0fb38de93e995835996790b74`。
- PR HEAD `b15ad6d84fa457f71099811377f6675ddf0aa580`：FACM UI Text Contract #341 SUCCESS；FACM Windows Build #1220 SUCCESS；FACM Mayhem Source Probe #357 SUCCESS。
- 顶部「先看结论」只从真实 `Tier / Rank / WinRate`、单符排行统计和核心装备名称投影，不额外发明玩法标签。
- 首看强化存在真实胜率/选择率时使用既有稳定评分；统计缺失时仅退回榜单首位，不伪造胜率。
- 两套核心出装、出门、鞋子、召唤师技能同时显示文字名称和图标，图片慢或加载失败时仍可读。
- 强化 TOP10 显示 `优先级 #N`、真实单符胜率、热度、样本和效果说明。
- 三条方向改为 `稳定赢法 / 高上限玩法 / 热门好上手`，底层排序语义分别保持胜率+热度、单符胜率、选择率。
- 基础 ARAM 与 Mayhem 专属修正继续分层显示、不相加；页脚继续明确单符统计不代表三符组合胜率。
- 继续沿用 3.5.7 的公网/图片时间预算，不增加新请求、常驻 Timer 或后台预取。

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

原 League 五阶段：**5/5 DONE**。扩展 Gate2 / Gate3 / Gate4 / Gate5 / Gate6 / Gate7、手动 Bench quick-pick、LOL 工作台与海斗实战决策卡均已收口并进入正式版本。

若后续腾讯实机报告 Bench 快速选择异常，优先保留当前最小 writer 边界并读取实际 Champ Select session / 状态结果，再开新的独立 Issue；不得直接扩大到自动 pick/ban/actions writer。

## 性能与权限冻结边界

- 唯一 `LeagueClientModule + LeagueClientSessionProvider`，不新增第二套 discovery / auth connector。
- 自动化默认关闭。
- 不做游戏内 Overlay / 注入。
- 不做自动 pick / ban / 自动 Bench swap / reroll / dodge / skin；手动 Bench swap 是用户点击触发的独立能力。
- Gate2 / Bench swap / 赛后 / 匹配 / Client UX repair 继续使用最小 writer 边界，不互相放宽 allowlist。
- `LeagueEfficiencyModule` 复用 Dashboard gameflow，不新增第二个常驻 monitor。
- 游戏修复自动模式只监听 LeagueClient 窗口 location-change 事件并做 debounce/cooldown，不新增 LCU 网络轮询。
- League Hub 只保留当前内容页，不把访问过的旧页隐藏常驻。
- 全局快捷键不引入低级键盘钩子或高频键盘轮询。
- 静态霓虹视觉只在正常 WinForms Paint 中绘制，不新增高频动画 Timer。
- InGame Performance budget 继续优先：network / image / disk / background CPU concurrency 1，prefetch 0。

## 冻结的稳定系统

没有真实缺陷或新独立需求时，不重新设计：Modular Host、Performance Contract、UI Text Contract、Single-instance Ensure Open、Flying Runtime / VPet / PetHost、Cleanup 安全语义、Mayhem 多源容灾、Online Release 事务，以及已验收 League runtime / Gate2-Gate7 / Bench writer / service 边界。

旧 Issue #33 / Draft PR #35 机器猫继续暂停。历史任务分支不删除，除非用户另行明确授权。
