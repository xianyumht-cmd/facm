# FACM 架构

> 当前架构基线：`.NET Framework 4.8 / WinForms` 主进程 + lightweight modular monolith。FACM 3.3 之后继续扩展 League 自动化，但不引入第二套 LCU 连接器、游戏内 Overlay、注入或低级键盘 Hook。

## 1. 进程边界

```text
FACM.exe  (.NET Framework 4.8 / WinForms)
├─ FACM Shell / 控制中心 / 托盘
├─ Settings / Online / Cleanup / Tools
├─ Theming / FacmThemeRuntime / FacmWindowChrome
├─ Performance Contract
├─ League Client / Dashboard / Player / Live / Build Advisor
├─ League Efficiency
├─ League Game Repair
├─ League Hub
└─ Mayhem

FACM.PetHost.exe  (.NET 8 x64 / WPF / VPet Core)
└─ 仅用户启用对应桌宠形态时启动
```

FACM Shell 必须先可见；默认桌面形态不因 PetHost 变成重型启动路径。普通二次启动仍采用本地 Mutex + AutoResetEvent 的 **Ensure Open / Activate** 语义。

## 2. Modular Host

稳定 namespace：

```text
FACM.AppHost
FACM.AppHost.Modules
```

`IFacmModule`：

```text
Id
Dependencies
Initialize()
Dispose()
```

`FacmHost` 负责依赖拓扑、重复/缺失/循环依赖拒绝、初始化失败 rollback、反向 Dispose 与 timing report。`FACM.exe --facm-host-test` 是 deterministic 架构门禁。

当前 League 关键模块关系：

```text
SettingsModule
├─ AppSettings
└─ FacmThemeRuntime  ← 初始化全局主题

ToolsModule            ← 仅保留驱动修复/独立工具；不再承载 LOL 游戏修复
PerformanceModule
LeagueClientModule     ← 唯一 LCU session owner
   ├─ LeagueDashboardModule + Performance
   ├─ LeaguePlayerModule + Performance
   ├─ LeagueLiveModule + Performance
   ├─ LeagueBuildAdvisorModule + Settings + Performance
   ├─ LeagueGameRepairModule
   ├─ MayhemModule
   └─ LeagueEfficiencyModule + Settings + LeagueDashboard

LeagueGameRepairModule
└─ LeagueGameRepairService
   ├─ Win32 League window detection / repair
   ├─ WinEvent auto-repair controller
   ├─ existing post-game writer → play-again
   └─ narrow client-UX repair writer → kill-and-restart-ux

LeagueHubModule
├─ LeagueDashboardModule
├─ LeaguePlayerModule
├─ LeagueLiveModule
├─ LeagueBuildAdvisorModule
├─ LeagueEfficiencyModule
├─ MayhemModule
└─ LeagueGameRepairModule

ShellModule
├─ LeagueHubModule
└─ MainForm / CompactMenuForm
```

`LeagueEfficiencyModule` 复用 `LeagueDashboardModule` 已有 gameflow 状态，不新增第二个常驻 gameflow monitor。`LeagueGameRepairModule` 复用 `LeagueClientModule` 的唯一 session/provider；窗口检测只读取本机 Win32 窗口/显示器状态。`LeagueHubModule` 仍只拥有 **导航/页面组合**，不拥有第二套 League runtime、LCU session 或自动化状态机。

## 3. Shell、清理修复与 League Hub 信息架构

FACM 的一级产品概念固定为“入口少、业务页完整”，不把内部模块边界直接暴露给普通用户。

### 控制中心

控制中心是桌面式启动器，只保留四个主入口：

```text
清理与修复
LOL 工作台
个性化
更多设置
```

图标按从左到右流式排列，默认宽度优先同排，空间不足才自然换行。游戏目录、清理状态和下一步说明不再占控制中心主页；这些信息进入对应产品页。

托盘仍承担系统级快捷入口与退出等后台 Shell 能力，但控制中心不再复制所有托盘动作。

### 清理与修复

环境级恢复流程统一为：

```text
驱动修复 ─┐
           ├─→ 重启电脑 → WEGAME → 英雄联盟 → 修复游戏
环境清理 ─┘
```

- 驱动修复和环境清理先后不限，建议两项各执行一次；
- 环境清理继续走 `CleanupModule → SafeCleanupService → CleanupReviewForm`，保留目录识别、相关进程阻止、UAC、精确预览、规则重验证与 reparse-point 防护；
- FACM 不把外部 WEGAME 最终修复伪装成 FACM 可验证状态；
- 游戏运行期间的客户端/大厅异常不属于此页。

### LOL 工作台

`LOL 工作台` 只有三个用户概念：

```text
比赛
├─ 当前状态
├─ 我的战绩
├─ 实时对局
└─ 海斗攻略

攻略
└─ 出装推荐

自动化
├─ 快捷工具
├─ 游戏修复
└─ 在线状态
```

`游戏修复` 已由 FACM 原生运行层接管：

```text
立即修复窗口   → LeagueGameRepairService 原生 Win32 修复
自动修复窗口   → WinEvent location-change + debounce/cooldown
跳过卡结算     → 复用既有 post-game play-again writer
重启客户端界面 → 专用窄 writer：POST /riotclient/kill-and-restart-ux
一键结束游戏   → 既有 LeagueEfficiency 进程级动作
```

旧 `Fix-LCU-Window.exe --mode 1..4` 不再是正式运行路径，ToolBundle 也不再嵌入该 EXE 与 mode scripts。历史工具文件可以暂留仓库作为来源/回归证据，但正式 FACM 不启动它们。真实赛后 `自动回大厅` 仍属于赛后自动化，不与手动 `跳过卡结算` 混淆。

### 原生窗口修复边界

- 只枚举本机 `LeagueClientUx` 所属的 `RCLIENT` 顶层窗口及其 `CefBrowserWindow`；不做进程注入；
- 以客户端**实际所在显示器**的 `WorkingArea` 为边界，不再强制使用 `Screen.PrimaryScreen`；支持负坐标和多显示器布局；
- DPI 只用于诊断和正确理解当前显示器环境，不通过补丁篡改 League 窗口过程；
- 16:9 判断使用容差，不再依赖精确浮点相等；同时拒绝极小、极大或大面积离屏的异常矩形；
- 恢复尺寸优先级：最近一次合理尺寸 → 保留当前可信宽度/高度推导另一边 → 当前显示器 + LCU zoom 的安全回退；不再无条件重置为 `1280×720×zoom`；
- 自动修复使用 `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)`，380ms debounce，修复后 2s cooldown；正常空闲时没有 1.5 秒常驻轮询，也没有第二个 LCU 网络轮询器；
- 自动修复为当前 FACM 进程会话状态，模块 Dispose 时解除 WinEvent hook 和 Timer。

### League UI ownership

- `LeagueHubModule` 是 Shell 层唯一的 League 导航 owner；
- Dashboard / Player / Live / Build Advisor / Efficiency / Game Repair 等业务模块只提供业务 Form/service，不自行向 Shell 注册入口；
- 旧 UiBridge 源码可以暂时保留兼容编译，但运行时不得重新建立 League 多按钮 submenu；`ShellMenuGroups.AddLeagueAction` 是明确的 no-op 防回归边界；
- League Hub 只懒加载当前页；切到另一页时先正常 `Close()` 旧页，让其 `FormClosed` 清理 Timer / CancellationToken，再释放控件；禁止把访问过的多个页面长期隐藏常驻；
- 不为“统一面板”重写已验收业务逻辑，也不把页面合并成一个新的万能 controller。

### FACM 视觉与全局主题系统

- `ThemeCatalog` 是唯一主题目录；`FacmThemeRuntime` 保存进程内当前主题并负责刷新已打开的 FACM 自有 WinForms 界面；
- `FACM.Theming.FacmDesignSystem` 从 `FacmThemeRuntime.Current` 读取 Canvas / Surface / Border / Accent / Text / Success / Warning / Error / Disabled 等语义 token；禁止另建平行主题引擎；
- `FacmWindowChrome` 使用相同全局主题绘制 FACM 自绘标题栏，并支持稳定标题 + 单行副标题。LOL 工作台当前页提示使用标题栏副标题，不再在内容区保留重复提示条；
- `FacmGlassPanel`、`FacmNavButton`、`FacmPillButton` 是轻量原生控件，不依赖桌面截图、DWM Acrylic、游戏 Overlay、Hook 或高频动画 Timer；
- `LeagueSoftGlassSkin` 保留为兼容入口：旧业务 Form 先走保守 `LeagueCompactDensity`，再由 `FacmDesignSystem` 统一材质；新 `LeagueHubForm` 本身已经按紧凑密度设计，不再被二次压缩；
- LOL 工作台默认 `1120×640`、最小 `900×580`。稀疏页在宽屏时使用真实上下文区展示已有客户端连接、gameflow 阶段和相关快捷入口；不为填空白新增 LCU session、网络请求、writer 或动画 Timer；
- 主题切换后已打开的 FACM 自有窗口刷新，新窗口继承当前主题；Windows 文件选择器、UAC 等系统拥有窗口继续使用系统外观；
- 视觉优化不得扩大 League 网络请求、LCU writer、磁盘预取或 InGame 后台预算；材质层必须保持纯 UI、无业务副作用。

`ShellMenuGroups.ValidateRootContract()`、`ShellUxSmokeTest` 与 `LeagueHubNavigation.ValidateForSmokeTest()` 共同守住单入口/三分区和当前 8 个 novice-facing view 的边界。

## 4. League Client 单一连接边界

`LeagueClientModule` 继续唯一拥有：

- Tencent/Riot LeagueClient session discovery；
- protocol/port/auth session；
- 共享 read transport；
- 各能力专用的最小 write transport。

不得为 Dashboard、OP.GG、赛后、匹配自动化、游戏修复或 League Hub 创建第二套 LCU discovery/auth/session。

### 写权限分离

不同产品能力使用不同 allowlist writer：

- Gate 2 符文/召唤师技能 writer：只允许 `my-selection` 与 FACM 自建 rune page/current page 路径；
- ARAM / ARAM Mayhem 手动 Bench writer：调用者只提交正整数 `championId`，transport 自行构造并只允许 `POST /lol-champ-select/v1/session/bench/swap/{championId}`；
- 赛后 writer：只允许 honor / honor ballot / `play-again`；手动“跳过卡结算”复用其中既有 `play-again` 路径；
- 匹配 writer：只允许 matchmaking search / ready-check accept；
- Client UX repair writer：不接受任意 path，只暴露 `TryRestartUxAsync()`，内部唯一目标是 `POST /riotclient/kill-and-restart-ux`。

Gate 2 writer 继续硬拒绝 ready-check、Bench swap 与 Champ Select action 路径；Bench writer 不接受任意 path，因此不能被借用执行 `/lol-champ-select/v1/session/actions/{id}`、pick / ban / reroll / dodge / skin；Client UX repair writer 同样不能被借用执行其它 LCU 写操作。

### Champ Select Bench 快速选择

`LeagueLiveModule` 在现有 `比赛 → 实时对局` 页面内提供可用英雄快速选择，不新增 Shell / Hub 顶级入口，也不新增第二个 LCU session owner：

- `LeagueLiveDataService` 继续读取同一个 `/lol-champ-select/v1/session`，解析 `benchEnabled`、`benchChampionIds` 与本地玩家当前英雄；
- 正常 Live Champ Select 刷新仍保持 2 秒节奏；仅 Bench 条可见且激活时增加 session-only 轻量读取，目标周期约 250ms；未激活时降到约 750ms，InGame / 最小化继续节流；
- Bench 轻量读取、正常 Live 读取和英雄头像读取共用同一个 service request gate，不在该模块制造并行 LCU 请求；
- 英雄头像仅在真实出现在 Bench 后按需读取本地 LCU `/lol-game-data/assets/v1/champion-icons/{id}.png` 并缓存，不走外网、不后台预取；
- 每次用户点击前重新读取 Bench；目标已消失则 fail-closed，不发送写请求；
- 一次点击最多一次 swap POST；LCU 2xx 后最多进行两次有界只读 settled verification，本地英雄未真正变成目标英雄就不能报告成功；
- 不实现指定目标自动监控 / 自动 swap。“抢英雄”是降低用户手动点击路径长度，不是自动化抢占。

## 5. Build Advisor / 自动应用

只读 `OP.GG 对局助手` 仍是数据展示能力。手动 `OP.GG 一键应用` 与自动应用复用同一 Gate 2/3 事务能力。

自动应用开关：`LeagueAutoApplyRecommended`，默认 `False`。

自动模式：

- 只在全局 Performance 已确认 `champ-select` 后观察；
- 稳定 champion/queue/mode/position/version/recommendation fingerprint 约 1.5 秒后执行一次；
- 同 fingerprint 不自动重试，避免重复符文页/写盘；
- 换英雄或推荐上下文变化后才形成下一次机会；
- runes/spells 仍遵守 Gate 2 安全边界；
- item set 仍遵守 `facm1-*` ownership、Tencent sibling `Game` 路径验证、temp/atomic/readback 事务；
- In Game 不执行推荐写入。

Advisor 展示与自动应用共享 OP.GG raw payload cache，避免同一路径重复网络请求。

## 6. 游戏效率

`LeagueEfficiencyModule` 是用户效率聚合模块，但底层仍拆成独立控制器。

### 全局快捷键

使用 Windows `RegisterHotKey / UnregisterHotKey + MOD_NOREPEAT`，由独立后台 STA 消息线程与隐藏 `NativeWindow` 接收 `WM_HOTKEY`：

- 不依赖 FACM 窗口焦点/最小化状态；
- 不轮询键盘；
- 不使用 low-level keyboard hook。

正式能力只保留两个已验收动作：

- 一键结束游戏：精确匹配 `League of Legends(TM)`（兼容旧 `League of Legends`）并结束目标 PID；
- 一键关闭大厅：只结束 `LeagueClient / LeagueClientUx / LeagueClientUxRender`。

**账号密码快捷输入已按产品决定取消，不属于正式架构：无设置、无 UI、无输入注入路径。**

### 赛后自动化

设置默认关闭：

- 随机从 eligible allies 中选择最多一名队友点赞，排除自己/对手/机器人；
- 当前 honor 类型固定 `HEART`；
- 同一连续赛后 episode 最多执行一次；
- 点赞失败不能阻止自动 `POST /lol-lobby/v2/play-again`；
- `WaitingForStats / PreEndOfGame / EndOfGame` 使用 bounded wait，不无限等待/重试。

### 自动下一局（腾讯兼容修复后）

设置默认关闭：

- 自动寻找对局只把 `Lobby + canStartActivity + 本地房主 + 至少一个真实成员` 作为核心安全门槛；
- `partyId / allowedStartActivity / queueId / warnings / restrictions` 等字段可以参与 best-effort 诊断/fingerprint，但**不得因为腾讯缺失或形态不同就成为未验证的硬兼容门槛**；
- 同一稳定 Lobby fingerprint 最多一次 search，失败不形成定时 POST storm；
- 自动接受以连续 `ReadyCheck` Gameflow episode 为主触发，约 450ms 后最多一次 accept；
- `/lol-matchmaking/v1/search` 只做 best-effort 状态确认：明确 Accepted/Declined 时抑制写入，缺字段/读取失败不能阻止本次 ReadyCheck accept；
- ChampSelect / InGame 不执行匹配 writer。

Issue #118 / PR #119 的这一行为已由用户在腾讯真实 Lobby → Queue → ReadyCheck 流程验收通过。

## 7. Settings ownership

行为设置继续只存在 `runtime/settings.ini`，`ui-text.ini` 只负责显示文案。

League 相关设置：

```text
LeagueAutoApplyRecommended=False
LeagueExitGameHotkey=
LeagueCloseLobbyHotkey=
LeagueAutoHonorTeammateEnabled=False
LeagueAutoReturnLobbyEnabled=False
LeagueAutoMatchmakingEnabled=False
LeagueAutoAcceptEnabled=False
```

主题设置同样由 `settings.ini` 中的 `ThemeId` 持久化；运行时映射由 `ThemeCatalog + FacmThemeRuntime` 负责。游戏修复的“自动修复窗口”当前只保持在本次 FACM 进程会话中，不新增持久化默认开启项。所有自动化默认关闭。正式 settings 不存储账号、密码或 credential hotkey。

## 8. Performance Contract

核心 CI `--performance-contract-test` / `--facm-host-test` 共同验证：

- 既有 Desktop / Client / Queueing / Champ Select / In Game budgets；
- Dashboard / Player / Live / Build Advisor；
- Champ Select Bench 解析、手动 swap 事务、一次点击最多一次 POST、stale target fail-closed 与 settled verification；
- Bench writer 与 Gate 2 writer 的权限隔离，以及 active / inactive / InGame / minimized 的快速读取节流边界；
- Gate 2 手动应用；
- Gate 3 item-set filesystem transaction；
- Gate 4 auto apply state machine/cache；
- Shell 一级 contract；
- League Hub 单入口、8 个 accepted view、3 个 novice-facing section 导航 contract；
- 游戏效率全局 hotkey contract；
- 原生游戏修复的尺寸规划、负坐标多屏 clamp、Client UX writer allowlist 与既有 play-again writer 复用边界；
- 赛后 automation；
- Tencent-style 缺失 partyId/lobbyId/queueId 的 matchmaking automation fixture。

In Game 预算仍优先于窗口可见性：network/image/disk/background CPU 并发 1、prefetch 0、非必要后台维护/视觉增强关闭。游戏修复自动模式不建立 LCU 网络轮询；只有真实窗口 location-change 事件经过 debounce 后才执行一次必要检查。

## 9. 发布边界

正式 Release 只由 `.github/workflows/publish-release.yml` 完成事务式发布：

1. 校验 release request；
2. PetHost publish/self-test；
3. FACM Release build + deterministic smoke；
4. 内嵌资源验证；
5. Authenticode 签名；
6. 生成 `enabled=false` online manifest；
7. 确认 main 未移动；
8. 提交版本元数据；
9. 创建并公开 GitHub Release；
10. 最后启用 online manifest。

功能分支、PR artifact 和普通 CI 候选都不等于正式发布。
