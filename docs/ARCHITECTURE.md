# FACM 架构

> 当前架构基线：`.NET Framework 4.8 / WinForms` 主进程 + lightweight modular monolith。FACM 3.3 在 3.2 已验收 Modular Host 上继续扩展 League 自动化，不引入第二套 LCU 连接器、游戏内 Overlay 或低级键盘 Hook。

## 1. 进程边界

```text
FACM.exe  (.NET Framework 4.8 / WinForms)
├─ FACM Shell / 控制中心 / 托盘
├─ Settings / Online / Cleanup / Tools
├─ Performance Contract
├─ League Client / Dashboard / Player / Live / Build Advisor
├─ League Efficiency
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

3.3 关键模块关系：

```text
SettingsModule
PerformanceModule
LeagueClientModule  ← 唯一 LCU session owner
   ├─ LeagueDashboardModule + Performance
   ├─ LeaguePlayerModule + Performance
   ├─ LeagueLiveModule + Performance
   ├─ LeagueBuildAdvisorModule + Settings + Performance
   ├─ MayhemModule
   └─ LeagueEfficiencyModule + Settings + LeagueDashboard

ShellModule
└─ MainForm / CompactMenuForm
```

`LeagueEfficiencyModule` 复用 `LeagueDashboardModule` 已有 gameflow 状态，不新增第二个常驻 gameflow monitor。

## 3. 小白 Shell 信息架构

FACM 的功能数量允许增长，但托盘一级决策数量固定。

一级菜单契约恰好 5 项：

```text
打开控制中心
清理环境
英雄联盟 >
更多 >
退出程序
```

业务模块只能注册到固定二级组。`ShellMenuGroups.ValidateRootContract()` 在运行时守住这一边界，`ShellUxSmokeTest` 在 CI 以纯结构方式守住定义，避免 pre-message-loop WinForms 对象造成 smoke 卡死。

`英雄联盟 >` 当前顺序：

```text
英雄联盟面板
玩家主页
实时对局
OP.GG 对局助手
OP.GG 一键应用
FACM/OP.GG 推荐装备集
游戏效率
海斗排行榜
```

控制中心同样使用渐进披露：主页只保留目录状态/管理、清理主动作、修复工具、英雄联盟、个性化、更多设置，不允许模块再次动态插入一级按钮。

## 4. League Client 单一连接边界

`LeagueClientModule` 继续唯一拥有：

- Tencent/Riot LeagueClient session discovery；
- protocol/port/auth session；
- 共享 read transport；
- 各能力专用的最小 write transport。

不得为 Dashboard、OP.GG、赛后或匹配自动化创建第二套 LCU discovery/auth/session。

### 写权限分离

不同产品能力使用不同 allowlist writer：

- Gate 2 符文/召唤师技能 writer：只允许 `my-selection` 与 FACM 自建 rune page/current page 路径；
- 赛后 writer：只允许 honor / honor ballot / `play-again`；
- 匹配 writer：只允许 matchmaking search / ready-check accept。

Gate 2 writer 继续硬拒绝 ready-check 与 Champ Select action 路径；Gate 7 不能借 Gate 2 writer 越权。

## 5. Build Advisor / 自动应用

只读 `OP.GG 对局助手` 仍是数据展示入口。手动 `OP.GG 一键应用` 与 3.3 自动应用复用同一 Gate 2/3 事务能力。

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

`LeagueEfficiencyModule` 是 3.3 的用户效率聚合模块，但底层仍拆成独立控制器。

### 全局快捷键

使用 Windows `RegisterHotKey / UnregisterHotKey + MOD_NOREPEAT`，由独立后台 STA 消息线程与隐藏 `NativeWindow` 接收 `WM_HOTKEY`：

- 不依赖 FACM 窗口焦点/最小化状态；
- 不轮询键盘；
- 不使用 low-level keyboard hook。

正式 3.3 只保留两个已验收动作：

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

### 自动下一局

设置默认关闭：

- 自动寻找对局只在 Lobby、房主、队伍可启动且没有阻塞 restriction/warning 时执行；
- fingerprint 包含 party / queue / members，同 fingerprint 最多一次 search；失败不形成 3 秒 POST storm；
- 自动接受只在 ReadyCheck `InProgress` 且本地未 Accepted/Declined 时执行一次；
- 用户主动 Decline 后 FACM 不反向接受；
- ChampSelect / InGame 不执行匹配 writer。

## 7. Settings ownership

行为设置继续只存在 `runtime/settings.ini`，`ui-text.ini` 只负责显示文案。

3.3 League 相关设置：

```text
LeagueAutoApplyRecommended=False
LeagueExitGameHotkey=
LeagueCloseLobbyHotkey=
LeagueAutoHonorTeammateEnabled=False
LeagueAutoReturnLobbyEnabled=False
LeagueAutoMatchmakingEnabled=False
LeagueAutoAcceptEnabled=False
```

所有自动化默认关闭。正式 settings 不存储账号、密码或 credential hotkey。

## 8. Performance Contract

核心 CI `--performance-contract-test` 同时验证：

- 既有 Desktop / Client / Queueing / Champ Select / In Game budgets；
- Dashboard / Player / Live / Build Advisor；
- Gate 2 手动应用；
- Gate 3 item-set filesystem transaction；
- Gate 4 auto apply state machine/cache；
- Shell 一级 5 项 contract；
- 游戏效率全局 hotkey contract；
- 赛后 automation；
- matchmaking automation。

In Game 预算仍优先于窗口可见性：network/image/disk/background CPU 并发 1、prefetch 0、非必要后台维护/视觉增强关闭。

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
