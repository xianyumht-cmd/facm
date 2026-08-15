# League Efficiency Gate 5 交接

> Issue #109 / Draft PR #110 / branch `feat/league-efficiency-gate5-109`。基于 `main@a26dc7979bfeb3900bfa8b44bc228d0dd0831513`。腾讯实机快捷键修正版未验收前保持 Draft；不创建 Release/Tag、不修改线上更新、不删除分支。

## 目标

在一个 `游戏效率` 页面提供三项可配置全局快捷键：

- 一键结束游戏；
- 一键关闭大厅；
- 剪贴板账号密码快捷输入。

三项默认均未绑定。设置写现有 `settings.ini`；账号和密码本身永不落盘。

## 2026-08-15 腾讯实机反馈后的行为修正

原 #1018 候选在用户腾讯/国服实机暴露：

1. 快捷键不能满足 FACM 后台/最小化/游戏内随时触发；
2. 国服游戏目标明确为任务管理器中的 `League of Legends(TM).exe`；
3. 一键关闭大厅不应因为游戏仍在运行而拒绝；
4. 账号密码输入的“登录窗口白名单”导致真实登录环境按快捷键零输入。

因此 Gate5 当前行为按实机结果重新冻结为：

### 全局快捷键

- 继续使用 Win32 `RegisterHotKey / UnregisterHotKey` + `MOD_NOREPEAT`；
- 不使用低级键盘 Hook，不轮询键盘；
- `LeagueHotkeyService` 使用独立后台 STA 消息线程 + 隐藏 `NativeWindow`，独立 WinForms message loop 接收 `WM_HOTKEY`；
- FACM 主窗口是否前台、最小化、隐藏不参与 hotkey 接收链；
- 保存绑定通过隐藏消息窗串行注册，重复绑定/系统占用仍失败并回滚旧绑定。

参考 Microsoft PowerToys 当前 GitHub 源码的两个原则：global hotkey 不依赖当前 context；键盘自动化序列给 UI 焦点切换留短间隔。FACM 保持 RegisterHotKey，不引入低级 Hook。

### 一键结束游戏

用户明确目标：水晶爆炸后立即结束国服游戏进程以快速返回大厅。

- 目标 `Process.ProcessName`：`League of Legends(TM)`；同时保留 `League of Legends` 兼容名；
- 按快捷键后直接精确 PID `Kill`，不再先 `CloseMainWindow` + grace wait；
- 不碰 `LeagueClient / LeagueClientUx / LeagueClientUxRender`；
- 不碰 WeGame 或其它进程。

### 一键关闭大厅

- 不再检查游戏是否正在运行；
- 按快捷键即直接精确 PID 结束 `LeagueClient / LeagueClientUx / LeagueClientUxRender`；
- 不碰 `League of Legends(TM)` 游戏进程；
- 不碰 WeGame 或其它进程。

### 账号密码快捷输入

用户先把焦点放到账号输入框；FACM **不再判断窗口标题或进程名**。

剪贴板格式：`账号-----密码`

- 第一段连续一个或多个 `-` 作为分隔符；
- 账号不能为空且账号自身不含 `-`；
- 密码不能为空；
- 密码后续出现 `-` 原样保留；
- CR/LF/TAB/NUL fail closed。

按全局快捷键后：

1. 只读取一次剪贴板；
2. 格式合法则 `Ctrl+A`；
3. Unicode SendInput 输入账号；
4. 短等待；
5. `Tab`；
6. 给焦点切换留约 50ms；
7. `Ctrl+A`；
8. Unicode SendInput 输入密码；
9. 不发送 Enter。

输入采用分阶段 `SendInput`，而不是把 Ctrl+A/账号/Tab/密码一次性塞进一个巨大 INPUT 数组。账号密码只存在于本次调用内存，不写日志；日志只记 success / invalid-format / failed。

## 设置

现有 `settings.ini`：

- `LeagueExitGameHotkey`
- `LeagueCloseLobbyHotkey`
- `LeagueCredentialHotkey`

`ui-text.ini` 只负责可见文案，不保存功能状态或凭据。

## deterministic smoke

`LeagueEfficiencySmokeTest` 随 Performance Contract，当前锁定：

- F-key / modifier hotkey parse；
- 裸 A-Z/0-9 拒绝；
- duplicate registration 拒绝且旧绑定保持；
- OS registration failure 回滚；
- dispose/unregister；
- dedicated-message-thread architecture marker；
- settings parse/serialize 且没有 credential 字段；
- `account-----password`、单 `-`、密码后续 hyphen；
- 空账号/空密码/control-character fail closed；
- **不做进程/窗口白名单**：合法剪贴板必须调用一次输入事务；
- invalid clipboard => zero additional SendInput；
- 腾讯 `League of Legends(TM)` fixture 能被一键结束；
- 一键关闭大厅在游戏进程仍存在时也必须直接关闭 Lobby family；
- 两项进程动作使用直接 precise PID kill，不走 graceful close；
- unrelated process 不受影响；
- UI copy defaults 非空。

## 腾讯实机重新验收

新候选必须验证：

1. 保存三个快捷键后，无论 FACM 前台/后台/最小化都能触发；
2. 游戏进行中按“一键结束游戏”能直接结束 `League of Legends(TM).exe`；
3. 同时存在游戏进程时按“一键关闭大厅”也会直接关闭 LeagueClient family，游戏进程本身不受影响；
4. 在当前可输入窗口中先聚焦账号框，复制 `123456-----abc123`，按账密快捷键能输入账号 -> Tab -> 密码；
5. 账密快捷键不自动 Enter；
6. 格式错误时零输入；
7. FACM 主窗焦点不应成为任何快捷键工作的前提；
8. idle CPU/network 基本不变。

腾讯实机确认前 PR #110 保持 Draft。
