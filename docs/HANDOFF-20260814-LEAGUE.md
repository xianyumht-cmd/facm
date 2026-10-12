# FACM League 主线完整交接（2026-08-14）

> 用途：新对话继续 FACM League 主线时，先读本文件，再读 `docs/PROJECT_STATE.md`，然后以 GitHub 当前 `main` / 活跃 Issue / PR 的实时状态为准。
>
> **当前不要从零重做 Dashboard / Player，也不要直接合并 Champ Select / Current Game。** 当前主线已经推进到 Windows 腾讯/国服候选验收阶段。

## 0. 一句话当前状态

- 正式生产版仍是 **FACM 3.2.0**，没有新 Release、没有改在线更新版本。
- 当前 `main`：`a7561d9203fb2d1a7df14bb0cab065cb0c933a10`。
- Performance Contract、League Dashboard Gate 1、Player Gate 1 已完成、Windows 腾讯/国服实测通过并合入 `main`。
- 当前活跃任务：Issue **#85** / Draft PR **#86**：`Champ Select / Current Game Gate 1：只读实时对局面板`。
- PR #86 当前行为 HEAD：`bf9ae52dbb814a5ac862c6671085e6ed0300d456`。
- PR #86 CI：Windows Build **#955 SUCCESS** / UI Text Contract **#76 SUCCESS** / Mayhem Source Probe **#237 SUCCESS**。
- Build #955 已有候选 artifact，但 **腾讯 Champ Select / Current Game 尚未用户实机验收，因此 PR #86 必须继续 Draft，不要合并。**

---

## 1. 正式生产与仓库状态

### 1.1 正式线上版本

`online/version.json` 当前仍是：

- `enabled=true`
- `version=3.2.0`
- `minimum_version=3.0.0`
- `force_update=false`
- Release EXE SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`

**本轮 League 工作没有发布新版本。** 后续任何 Release / online version / tag / announcement 都需要用户重新明确授权。

### 1.2 当前主线提交

- `main`：`a7561d9203fb2d1a7df14bb0cab065cb0c933a10`
  - `Merge PR #84: close Player Gate 1`
- Player 行为 merge commit：`28431338f915b60811c254581987cdd58e190dbe`
- Player 最终行为 HEAD：`22959ba75e65ba03efd87891864ce79bc46c13d9`
- Dashboard 行为 merge commit：`f8a37066d49cf3c86778b0ae525001fe8ace633b`
- Dashboard 最终行为 HEAD：`7c1401e2f3ee3f7309fe46efb51f20aa93e538ef`
- 当前 Live 候选 HEAD：`bf9ae52dbb814a5ac862c6671085e6ed0300d456`（**未合并**）

### 1.3 当前活跃 Issue / PR

- Issue #85：`Champ Select / Current Game Gate 1：只读实时对局面板`，OPEN。
- PR #86：同名，Draft / OPEN / mergeable，base=`main`，head=`feat/champ-current-gate1-85`。
- 旧机器猫 Issue #33 / Draft PR #35 仍是暂停实验，不是当前主线，不要自动恢复。

---

## 2. 已完成并验证：League Dashboard Gate 1

### 2.1 产品能力

已完成独立 `LeagueDashboardModule`，依赖：

- `LeagueClientModule`
- `PerformanceModule`

Dashboard 提供：

- 客户端连接状态；
- 当前召唤师 / 等级；
- 平台 / 区服；
- Gameflow phase；
- 当前 FACM Performance Budget；
- 最后更新时间。

托盘入口：`英雄联盟面板`。

### 2.2 Gameflow → Performance Contract

已完成常驻轻量 Gameflow monitor：

- 首次 WinForms `Application.Idle` 后启动，不拖慢 Host 初始化；
- ordinary client 约 5s；
- Queueing 约 3s；
- Champ Select 约 2s；
- In Game 约 10s；
- 关闭 Dashboard 后 monitor 仍存在，避免 Performance Budget 卡死在旧阶段；
- In Game 反而降低轮询频率，遵循“游戏优先，FACM 第二优先”。

Phase 映射已覆盖：

- `Matchmaking` / `ReadyCheck` → Queueing；
- `ChampSelect` → Champ Select；
- `GameStart` / `InProgress` / `WatchInProgress` / `Reconnect` → In Game；
- 其它已连接阶段 → League Client。

### 2.3 腾讯国服 discovery 最终方案

正常发现顺序：

1. 找 `LeagueClientUx` / `LeagueClient`；
2. 优先 `Process.MainModule.FileName` 找 EXE 目录；
3. MainModule 失败时，WMI 只读 `ExecutablePath` 作为路径 fallback；
4. 找同目录 Riot `lockfile`；
5. **活动 lockfile 使用 `FileShare.ReadWrite` 共享只读**，并对瞬时 IO / 客户端重写中的半截内容做短重试；
6. lockfile 仍失败时，仅针对 `LeagueClientUx` 用 WMI `CommandLine` fallback，交给既有 `LeagueClientSessionParser`；
7. 不新增第二套 token parser，不把完整命令行 / token 输出到日志或 UI。

### 2.4 Windows 腾讯/国服实测结果

用户实机已确认 Dashboard 正常读取：

- `已连接`；
- 当前召唤师 / 等级；
- 平台 / 区服：`CQ100`；
- Gameflow：`Lobby`；
- Performance：`league-client`。

因此 Dashboard / LCU discovery 在该腾讯环境已标记 **实测可用**。

### 2.5 CI / 候选

PR #79：

- Final behavior HEAD：`7c1401e2f3ee3f7309fe46efb51f20aa93e538ef`
- Windows Build #937 SUCCESS
- UI Text Contract #58 SUCCESS
- Mayhem Source Probe #222 SUCCESS
- Build #937 artifact ZIP SHA-256：`41257B877DD3F7DFC7247E1E3FD1C473104F0498A015BBC6D33D0BFDCCFA9577`
- EXE SHA-256：`2A0BCAEE031384A0338CFF78D1F2DEC1F81E4F075F904817A7CF2D2EC9AFF712`

---

## 3. 已完成并验证：Player Gate 1

### 3.1 模块与 UI

已完成独立 `LeaguePlayerModule`，依赖：

- `LeagueClientModule`
- `PerformanceModule`

托盘入口：`玩家主页`，位于 League Dashboard 附近。

页面采用轻量 WinForms `ListView` + `VirtualMode`，**不是一场战绩创建一套复杂控件**。

### 3.2 当前账号与最近战绩

当前行为：

- 打开页面先读取 / 校验当前账号；
- profile 短缓存约 15s；
- match page 内存缓存约 45s；
- PUUID 改变立即使旧账号战绩缓存失效；
- 第一次严格请求最近 10 场：`begIndex=0&endIndex=9`；
- 用户手动“再加载 10 场”才扩到最多 20 场；
- 不做千场预加载；
- 页面关闭立即取消；
- 页面本身没有后台 timer 持续刷新。

当前玩家 participant 关联：

1. PUUID 优先；
2. `summonerId` fallback。

战绩行已解析：

- 时间；
- 模式 / queue；
- champion ID；
- K / D / A；
- CS；
- 胜负；
- 时长。

### 3.3 摘要优先、详情渐进补全

正常优先只读取 match-history summary。

只有当摘要缺 participant / stats 且当前 Performance Budget 允许后台预取时，才可能串行请求：

`/lol-match-history/v1/games/{gameId}`

约束：

- 详情请求串行；
- 受 `MatchHistoryPrefetchCount` 上限；
- 共用约 5 秒 enrichment budget；
- 页面关闭立即取消；
- Queueing / Champ Select / In Game **禁止自动详情补全**；
- deterministic smoke 明确验证 In Game 缺字段时依然是 **0 个详情请求**。

### 3.4 腾讯数据证据

Akari 仓库存在真实 `2026-05-16-tencent-hn10` fixture，明确来自 logged-in Tencent League client。

该 fixture 的 match history 已包含：

- participant identity；
- champion ID；
- KDA；
- CS；
- win；
- 多种腾讯 queue。

因此 Player 采用“summary first”而不是强制每场详情 fan-out。

另一个重要结论：腾讯 snapshot 中的 `gameCount` 更像返回窗口数量，**不要把它当账号总历史局数**。当前“加载更多”判定使用“请求 10 场是否实际返回满 10 场”。

### 3.5 Windows 腾讯/国服验收

Build #951 用户实机反馈：**正常**。

随后 PR #82 已按精确 HEAD 合并，Issue #81 completed；main post-merge：

- Windows Build #952 SUCCESS
- UI Text Contract #73 SUCCESS
- Mayhem Source Probe #236 SUCCESS

纯文档 closeout PR #84 也已合并；closeout 后 main Windows Build #954 SUCCESS / UI Text #75 SUCCESS。

PR #82 candidate：

- behavior HEAD：`22959ba75e65ba03efd87891864ce79bc46c13d9`
- Windows Build #951 SUCCESS
- UI Text #72 SUCCESS
- Mayhem #234 SUCCESS
- artifact ZIP SHA-256：`7A70D9E8FF724E86B937C1F2FC8885E6F35E7C39B2B171932B99CFD5AE2FEEFB`
- EXE SHA-256：`C23A7298B66131EFCE40D437DC0225FA0B5A2E00348CD0EFEAA7040D42FC5DF2`

---

## 4. 当前未完成：Champ Select / Current Game Gate 1

### 4.1 当前任务位置

- Issue #85：OPEN
- Draft PR #86：OPEN
- branch：`feat/champ-current-gate1-85`
- base：`main` @ `a7561d9203fb2d1a7df14bb0cab065cb0c933a10`
- candidate HEAD：`bf9ae52dbb814a5ac862c6671085e6ed0300d456`
- changed files：12
- additions / deletions：约 `+1018 / -3`

**不要在 Windows 腾讯/国服实测通过前 Ready / merge #86。**

### 4.2 模块边界

新增 `LeagueLiveModule`：

- module id：`league-live`
- 依赖仅：`LeagueClientModule + PerformanceModule`
- 不新增第二套 LCU connector；
- 通过 `LeagueLiveUiBridge` 增加托盘入口 `实时对局`；
- `ShellModule` 只承担依赖所有权，不把 LCU 逻辑塞回 `MainForm`。

### 4.3 Champ Select 只读链

读取：

`GET /lol-champ-select/v1/session`

解析字段包括：

- gameId / queueId；
- `localPlayerCellId`；
- timer phase / adjusted time left；
- ally / enemy bans；
- myTeam / theirTeam；
- 玩家 cellId / PUUID / summonerId；
- assignedPosition；
- championId；
- championPickIntent；
- spell1Id / spell2Id；
- 当前本地玩家正在进行的 action（pick / ban 等只读显示）。

**没有调用任何写接口。**

明确不做：

- auto accept；
- 自动 pick / ban；
- swap；
- reroll；
- dodge；
- 改召唤师技能；
- 改皮肤；
- 其它客户端自动操作。

### 4.4 Current Game 只读链

先通过既有 Gameflow phase 判定 In Game，然后只读：

`GET /lol-gameflow/v1/session`

当前解析：

- phase；
- map id / map name；
- game mode；
- gameId；
- queue id / name；
- teamOne / teamTwo；
- player PUUID / summonerId / name；
- championId；
- position / role（有字段时）。

不做：

- match-history teammate fan-out；
- 千场分析；
- live timeline；
- champion image preload；
- 队友画像；
- SGP 扩展请求。

### 4.5 性能边界

`LeagueLiveDataService.RefreshAsync()`：

- 复用既有 `LeagueDashboardPhaseService`；
- 一个 `SemaphoreSlim` 串行 refresh，防重入；
- phase request 后：
  - Champ Select 最多再 1 个 champ-select session request；
  - In Game 最多再 1 个 gameflow session request；
- 单次详情链 4s linked timeout；
- 不碰 match history / scouting；
- 页面关闭取消 lifetime work。

Form 轮询底线：

- Champ Select：>= 2s；
- In Game：>= 10s；
- minimized / hidden：>= 10s；
- 非敏感阶段不做高频重活。

### 4.6 当前 Gate 的 deterministic smoke

已覆盖：

- Champ Select session 解析；
- local player 定位；
- timer / bans / action 解析；
- 每次可见 Champ Select refresh = 1 phase + 最多 1 champ session；
- Current Game map / mode / queue / team 解析；
- In Game refresh = 1 phase + 最多 1 gameflow session；
- Champ Select / In Game **0 match-history / scouting 请求**；
- Performance Budget 不被 live page 放宽；
- polling floor；
- form close cancellation；
- tray bridge；
- Host dependency contract。

### 4.7 当前 CI / artifact

HEAD `bf9ae52dbb814a5ac862c6671085e6ed0300d456`：

- Windows Build #955：SUCCESS
- workflow run id：`31768863231`
- UI Text Contract #76：SUCCESS
- Mayhem Source Probe #237：SUCCESS

Artifact：

- name：`FACM-Windows-x64-955`
- artifact id：`9207370712`
- size：154,635,137 bytes
- GitHub artifact digest：`sha256:E92F205D19F8672303FD9CE86166E5022DD33113D35C8BE3F9B99442433AC6F8`
- packaged `FACM.exe` SHA-256：`93B00EC31B6B90BEC2A6A44FE1C6109241DC220899FB16AF1DB3BD84C28507E4`
- EXE size：77,969,816 bytes
- file / product version 仍是 3.2.0 candidate；沿用现有自签开发证书行为。

### 4.8 关键代码位置

当前 Live Gate 核心新增/修改包括：

- `src/FACM/Application/Modules/LeagueLiveModule.cs`
- `src/FACM/League/LeagueLiveDataService.cs`
- `src/FACM/League/LeagueLiveForm.cs`
- `src/FACM/League/LeagueLiveModels.cs`
- `src/FACM/League/LeagueLiveSmokeTest.cs`
- `src/FACM/League/LeagueLiveUiBridge.cs`
- `src/FACM/Application/Modules/ShellModule.cs`
- `src/FACM/Application/FacmHostSmokeTest.cs`
- `src/FACM/Performance/PerformanceContractSmokeTest.cs`
- `src/FACM/Program.cs`
- `src/FACM/Services/UiTextKeys.cs`
- `src/FACM/Services/UiTextCatalog.cs`

---

## 5. 已失败方案及原因（必须保留，避免重复）

### 5.1 Dashboard Build #918：只靠旧 discovery，国服“未检测到客户端”

现象：LOL 已登录，FACM Dashboard 仍显示未检测到客户端。

原因：正式 discovery 仍主要依赖：

`Process.MainModule -> EXE 目录 -> lockfile`

当时 Akari 风格 parser 虽存在，但没有真正解决活动国服 lockfile 的读取问题。

**不要把问题再归因于“腾讯不支持 LCU”。**

### 5.2 Build #922：增加 WMI ExecutablePath 后仍失败

改动：MainModule 失败时用 WMI 只读 `ExecutablePath` 找 LeagueClient 目录。

结果：仍然“未检测到客户端”。

原因：这一步只修了“找目录”，**没有修活动 lockfile 的共享读取方式**；实际目录和 lockfile 都能找到，但读取仍失败。

### 5.3 诊断 v1：CMD / PowerShell 转义错误

报错：PowerShell 表达式出现意外 `^`。

原因：把 CMD 转义字符带进 PowerShell 单行表达式。

结论：不要再写“CMD 内嵌超长 PowerShell 单行”的诊断。

### 5.4 诊断 v2：启动闪退

原因：CMD 动态拼接临时 PowerShell 脚本仍存在转义/生成阶段脆弱性，甚至来不及进入最后 pause。

结论：诊断应使用真实 `.cmd + .ps1` 双文件，CMD 只负责启动，不动态拼 PowerShell 源码。

### 5.5 诊断 v3 / v4 最终定位

v3 已确认：

- `LeagueClientUx.exe` 存在；
- EXE 路径可读；
- 同目录 lockfile 存在；
- CommandLine 可读；
- Akari 所依赖的 `app-port / remoting-auth / app-pid / Tencent` 类参数存在。

v4 最终确认根因：

> 同目录 `lockfile` 存在，但旧 `File.ReadAllText(lockfile)` 因 LeagueClient 正持有写句柄触发 **sharing violation**。

最终正确修复不是“换安装路径”，而是：

- 活动 lockfile 共享只读 `FileShare.ReadWrite`；
- 短重试应对客户端瞬时重写；
- 读取失败时才用 CommandLine fallback。

### 5.6 不要把 Akari 官网“不支持腾讯服务器”当技术结论

用户已亲自验证 Akari 可在腾讯国服工作；FACM Dashboard / Player 也已国服实测成功。

工作规则：

> Akari 官网的“不支持腾讯服务器”是官方免责声明 / 支持承诺边界，不等于代码层技术不兼容。

腾讯兼容判断必须按：

- Akari / FACM 源码机制；
- 真实 Tencent fixture；
- Windows 腾讯客户端实测。

### 5.7 不要把 Akari TS / Electron 机械翻译成 C#

当前路线一直是：

`研究 Akari 的接口/模块/数据边界 -> 提取思路 -> 用 FACM 自己的 C#/.NET Framework 4.8/WinForms 架构重写`

不是：

`Akari TypeScript -> 自动翻译 C# -> 塞进 FACM`

这是明确产品/架构决策。

### 5.8 Player：不要把 `gameCount` 当总局数

真实腾讯 snapshot 已证明该字段不能安全当“账号总历史战绩数量”。

当前正确分页策略是：

- 请求固定窗口；
- 本页实际返回满 10 条才允许继续加载；
- 第一 Gate 最多 20 条。

### 5.9 不要为了“架构更漂亮”重写已稳定 Dashboard poll/monitor

Dashboard 打开时可见 form 和常驻 monitor 有少量重复 loopback phase GET；此前已评估：为省这点请求而重写 UI 事件链，Gate 1 风险大于收益。

没有真实性能问题前，不要为了去重而重做稳定代码。

---

## 6. 明确不要重复 / 不要做的路线

1. **不要新增第二套 LeagueClient / LCU connector。** 所有 League 功能继续复用 `LeagueClientModule`。
2. 不要回退到仅 `Process.MainModule` discovery。
3. 不要回退到 `File.ReadAllText(active lockfile)`。
4. 不要在日志/UI 输出 LCU token、完整 CommandLine 或凭据。
5. 不要把 Akari 官方兼容声明当国服技术判定。
6. 不要把 Akari Electron/Vue/TS 逐文件翻成 C#。
7. 不要在 In Game 开 match-history 预取、队友侦察、图片预加载或后台维护。
8. 不要在 Champ Select Gate 1 做自动接受 / ban / pick / swap / reroll / dodge / 改技能等写操作。
9. 不要为了功能数量去做千场一次加载、每个战绩一套复杂控件、重型动态图表。
10. 不要重新设计已经用户 Windows 验收通过的 Dashboard / Player UI 与交互，除非出现真实缺陷。
11. 不要恢复旧机器猫 #33/#35 作为当前主线。
12. 不要自动 Release / 改 `online/version.json` / 打新 tag；需要用户新授权。
13. 不要删除任务分支；分支删除属于破坏性操作，需要用户明确允许。

---

## 7. 当前 Windows / 腾讯国服环境状态

已知实测环境：

- Windows；
- 腾讯英雄联盟客户端，通过 WeGame 路径体系运行；
- 诊断时 League Client 目录位于类似 `E:\WeGameApps\英雄联盟\LeagueClient`；
- `LeagueClientUx.exe` 可见；
- EXE path / CommandLine 可读；
- 同目录 lockfile 存在且运行时可能被 LeagueClient 持有写句柄；
- Dashboard Build #937 已连接成功；
- 平台 / 区服实测 `CQ100`；
- Player Build #951 实机反馈“正常”。

当前尚未验证：

- 腾讯 Champ Select `/lol-champ-select/v1/session` 的具体实际字段完整度；
- 腾讯 In Game `/lol-gameflow/v1/session` team/player 字段完整度；
- #955 在真实选人阶段和实际对局阶段 UI/性能表现。

注意：Akari `2026-05-16-tencent-hn10` fixture 真实来自已登录腾讯客户端，但它的 manifest 主要覆盖 match-history / SGP，**没有 Champ Select / gameflow-session fixture**。因此 PR #86 不能提前标记“国服实测可用”。

---

## 8. 下一步精确操作（新对话直接从这里继续）

### 8.1 首先 fresh-inspect，不要猜状态

新对话第一步：

1. 读 `docs/HANDOFF-20260814-LEAGUE.md`；
2. 读 `docs/PROJECT_STATE.md`；
3. fresh-inspect：
   - `main` HEAD；
   - Issue #85；
   - PR #86；
   - PR #86 head 是否仍为 `bf9ae52dbb814a5ac862c6671085e6ed0300d456`；
   - CI 是否仍为 #955 / #76 / #237 全绿；
4. 如果 head 已变化，以最新 PR 内容和 checks 为准，不使用本文旧 SHA 强行合并。

### 8.2 如果当前仍是候选 HEAD `bf9ae52d...`

下一步不是继续编码，而是给用户 **Build #955 Windows 腾讯/国服集中验收**。

需要验收的核心：

1. **Lobby / 普通客户端阶段**
   - 托盘出现 `实时对局`；
   - 打开不卡顿；
   - 当前 phase / budget 正常；
   - 不应产生战绩/图片重请求。

2. **进入 Champ Select**
   - phase 正确进入 Champ Select；
   - timer 有变化；
   - ally/enemy / bans 能显示多少显示多少；
   - 本地玩家能正确定位；
   - champion intent / 当前 action / spell IDs 有数据时正常展示；
   - FACM 不应导致选人卡顿。

3. **进入实际游戏**
   - phase 切 In Game；
   - map / mode / queue / gameId 可读；
   - team/player/champion 数据能读多少显示多少；
   - Performance 必须保持 `in-game`；
   - 重点观察 FPS、点击、切换、输入无额外卡顿；
   - 不允许后台战绩详情 / scouting / 图片预取。

4. **退出游戏返回客户端**
   - Gameflow / Performance 能恢复；
   - 页面关闭后请求能停止；
   - 无卡死 / stale in-game budget。

### 8.3 用户报告“正常/通过”时

把它当作 **PR #86 Windows 腾讯/国服 acceptance**：

1. fresh-inspect PR #86 head / checks；
2. 确认没有新 commit、checks 全绿；
3. 将 PR #86 Ready for Review；
4. 按精确 expected head SHA merge；
5. 验证 main push CI；
6. 更新 `docs/PROJECT_STATE.md` / canonical closeout，记录腾讯 Champ Select / Current Game 实测结果；
7. Issue #85 completed；
8. **不要 Release**，除非用户另行授权；
9. 不删除 branch，除非用户明确允许。

### 8.4 用户报告 bug 时

- PR #86 保持 Draft；
- 先判断是：
  - endpoint 返回差异；
  - 腾讯字段 shape 差异；
  - phase timing；
  - UI 显示；
  - cancellation / polling；
  - 性能问题；
- 只修 scoped bug；
- 不趁机加入自动 ban/pick 或队友战绩；
- CI 全绿后再给一个新候选做一次集中复测。

---

## 9. 后续产品方向（PR #86 通过以后）

建议顺序继续保持：

1. League Dashboard：DONE
2. Player Gate 1：DONE
3. Champ Select / Current Game Gate 1：CURRENT
4. 后续再根据用户选择扩：
   - champion id → 名称/轻量本地元数据；
   - 当前对局更清晰的 team row；
   - Player 英雄表现统计；
   - 工具中心 / automation；
   - 更高级 teammate/scouting 需单独 Gate + 性能设计。

不要直接跳到：

- 全自动 ban/pick；
- 千场队友扫描；
- OP.GG 式常驻重型 browser runtime；
- 大量图片与实时动画。

产品目标仍是：

> **OP.GG 式数据能力 + Akari 式客户端联动 + FACM 腾讯/国服适配 + 明确低资源策略。**

非正式表达：

> “OP.GG 的数据 + Akari 的客户端能力，但比两者更轻。”

---

## 10. 固定架构 / 约束（不要顺手改）

当前后端基线是轻量 modular monolith / `FacmHost`：

```text
Program
  ↓
FacmHost
  ├─ CompactMenuEnhancerModule
  ├─ SettingsModule
  ├─ ToolsModule
  ├─ OnlineModule
  ├─ PetsModule
  ├─ PerformanceModule
  ├─ LeagueClientModule
  ├─ LeagueDashboardModule
  ├─ LeaguePlayerModule
  ├─ LeagueLiveModule      # 当前仅在 PR #86，未进 main
  ├─ MayhemModule
  ├─ CleanupModule
  └─ ShellModule
       └─ MainForm
            └─ CompactMenuForm
```

稳定契约：

- namespace 不要重新引入 `FACM.Application`；曾与 WinForms `Application` 冲突，现用 `FACM.AppHost`。
- single-instance AutoResetEvent / Ensure Open-Activate 不动。
- `--cleanup` 与 smoke/test mutex 独立。
- VPet PetHost / Job Object / ready-fallback 不动。
- `settings.ini` 兼容不动。
- Cleanup whitelist/reparse/revalidation 不动。
- Mayhem 多源 fallback / Tencent patch 逻辑不动。
- Online release transaction 不动。
- UI Text Contract 不绕过：新静态可见文案继续走稳定 TextKey。
- Performance Contract 不放宽：越接近游戏阶段预算只能更保守。

---

## 11. 新对话可直接使用的开场指令

可直接发：

> 继续 FACM League。先读取 `docs/HANDOFF-20260814-LEAGUE.md` 和 `docs/PROJECT_STATE.md`，以当前 main / Issue #85 / Draft PR #86 为准。不要重做 Dashboard/Player，不要提前合并 #86。先 fresh-inspect #86 的 HEAD 和 CI；如果候选仍是 `bf9ae52dbb814a5ac862c6671085e6ed0300d456` 且 checks 全绿，就直接进入 Build #955 腾讯国服 Champ Select / Current Game 集中验收。

---

## 12. 本交接文档本身的边界

本文记录的是 2026-08-14 当前状态。新对话仍必须以 GitHub 实时状态为最终事实源；如果 `main`、Issue #85、PR #86 或 HEAD 已发生变化，以最新仓库状态覆盖本文相应旧值。
