# FACM 4.0 P5 — League Workbench 功能等价矩阵

> 基线：FACM 3.5.15。目标是行为/功能等价，不是像素级 UI 复刻。UI 2.0 在整体验收后单独进行。
>
> 架构硬约束：4.0 进程只保留一套 `WindowsLeagueTransportSessionSource`、`LeagueHttpGateway`、`LeagueGameflowMonitor`；页面不得创建第二套 League session/auth/polling owner。

## 功能矩阵

| 3.5.15 功能组 | 4.0 用户能力 | 4.0 实现/边界 | 验证 |
| --- | --- | --- | --- |
| Dashboard / 对局状态与比赛详情 | 当前账号、Queue/Lobby/ReadyCheck、阶段状态、Dashboard 摘要 | `LeagueWorkbenchDataSource` + 共享 `LeagueHttpGateway` / `LeagueGameflowMonitor`；只读 | `check-facm4-league-workbench.ps1` + Gate8/Foundation smoke |
| Player / 玩家资料与历史战绩 | 玩家资料、排位摘要、最近战绩 | Workbench read models；按需读取，不增加页面轮询 owner | Workbench source gate + Foundation smoke |
| Live / 实时对局 | ChampSelect、InGame、选人/禁用/候选/当前操作 | 阶段事实来自唯一 gameflow monitor；页面只消费 read model | Workbench source gate + Gate8 smoke |
| Build Advisor / OP.GG | 模式/位置推荐、符文、召唤师技能、出门装、鞋子、核心装、技能/Counter | `LeagueBuildAdvisorService`；10m 推荐缓存、30m 静态目录缓存；InGame cache-only | `LeagueBuildAdvisorSmoke` + Workbench gate |
| Item Sets / 推荐配置应用 | 明确确认后应用装备集；手动应用推荐符文/技能；`AutoApplyRecommended` | 写前重验 ChampSelect/champion/queue；只管理 `facm4-*` / FACM 自有符文页；闪现槽位保留；自动应用复用共享 heartbeat | `LeagueItemSetSmoke`、`LeagueRecommendedAutoApplySmoke`、`check-facm4-league-recommended.ps1` |
| Matchmaking / PostGame / 自动化 | 自动开始匹配、自动接受、自动点赞/荣誉、自动返回大厅 | 共享 heartbeat；成员 fingerprint 去重；ReadyCheck 每轮一次；PostGame 一次 cycle、V2→legacy fallback、写后验证 | Matchmaking/PostGame deterministic smoke + Workbench gate |
| Presence / 客户端状态与效率 | 在线/离开/请勿打扰/手机在线/离线/显示游戏中；结束游戏、关闭大厅 | Presence 每次用户操作一次窄 `PUT /lol-chat/v1/me` + 双读回验；效率功能用 Windows 进程白名单和 PID/进程名复验 | `LeaguePresenceSmoke`、`LeagueEfficiencySmoke`、`check-facm4-league-efficiency.ps1` |
| Bench quick-pick / swap | Bench 英雄快速选择/交换 | Legacy + TeamBuilder 两条精确 capability；一次点击最多一次 POST；35/70/140ms 有界回读；404/409 视为目标失效 | `LeagueBenchQuickPickSmoke` + `check-facm4-league-bench.ps1` |
| ARAM / Mayhem | 英雄别名查询、排行/胜率、腾讯版本校验、基础 ARAM + Mayhem 分层平衡、rich augments、三条决策路线、详细出装、中文本地化、Top10、取消/超时、保存 PNG/复制图片 | 固定 typed public-data transport；12MB cap、15m fresh/24h stale、single-flight；腾讯固定源；LCU-first + CommunityDragon fallback；WinUI 13s 总超时、Enter 查询、840px PNG export | Mayhem base/public-data/augment/build/base-balance/localization/WinUI gates + deterministic smoke |
| 热键与窄写操作 | 结束游戏/关闭大厅全局快捷键；推荐配置/匹配/荣誉/Presence/Bench 等窄写 | Core capability allowlist；裸字母/数字拒绝；冲突拒绝；注册失败不持久化；写入不允许页面构造任意 URL | `LeagueEfficiencySmoke` + League write capability smoke + 各专项 source gate |

## 写操作安全边界

1. WinUI 不直接持有 `ILeagueWriteGateway`、不构造 LCU URL、不开第二个 `HttpClient`/session owner。
2. 所有 League 写操作都由 Core capability 或更窄 intent 映射到固定 method/path。
3. 推荐符文/装备集在执行前重新读取 ChampSelect 上下文，英雄/队列变化则拒绝写入。
4. Bench 一次用户点击最多一次 POST，后续只做有界只读验证，不用写重试“追状态”。
5. Presence 一次用户操作只做一次 PUT；如果客户端覆盖回去，报告失败而不是抢写循环。
6. 自动化全部复用共享 gameflow observation/heartbeat，不创建第二套 phase polling。

## Mayhem 3.5 用户交互等价

- 输入英雄名称/俗称，Enter 或“查询”触发；
- 查询期间允许显式取消；
- UI 总查询预算 13 秒，手动取消与超时提示区分；
- 查询结果同时展示基础 ARAM 与 Mayhem 专属修正，不做数值叠加；
- rich augment 行必须带真实 icon 才可覆盖 fallback；
- “稳定赢法”评分固定为 `0.72 × 胜率 + 0.28 × 选择率`，并与“高上限玩法 / 热门好上手”去重；
- detailed build 上限保持：2 套核心方案、每套最多 5 件、出门 3、鞋子 1、召唤师 2、技能优先 3 且排除 R；
- 查询成功后可保存 PNG、复制攻略图；功能阶段固定导出宽度 840px，最终视觉留到 UI 2.0。

## 当前非目标

- 不把 3.5 WinForms/GDI 窗体或 `LeagueClientRuntime` 搬回 4.0。
- 不为页面创建独立 LCU 发现、认证、轮询或公共 HTTP owner。
- 不在 P5 做最终 Mica/动画/卡片密度/图片视觉重设计。
- 不修改 production 3.5.15，不发布 4.0.0，不执行 Gate 13 cutover。

## P5 工程验收结论

P5 的 9 个功能组已经在 4.0 单一 League runtime 架构下具备真实读取/操作入口和专项 source gate / deterministic smoke。Foundation 最新完整验证包含 Release build、FoundationSmoke、WindowsSmoke、self-contained single-file publish 与 artifact upload。P5 PR 仍保持 Draft，最终 merge 继续等待整套 3.5 功能等价版的一次性真机验收。
