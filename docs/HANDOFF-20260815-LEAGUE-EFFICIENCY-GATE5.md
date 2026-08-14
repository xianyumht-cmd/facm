# League Efficiency Gate 5 交接

> Issue #109 / branch `feat/league-efficiency-gate5-109`。本 Gate 从 `main@a26dc7979bfeb3900bfa8b44bc228d0dd0831513` 开始，不依赖 Shell UX #105 或 Gate 4 #107。

## 目标

增加一个统一 `游戏效率` 入口，第一阶段提供三个用户主动快捷键：

- 一键退出游戏；
- 一键关闭大厅；
- 剪贴板租号账号密码快捷输入。

核心要求是 **待机 0 polling**。使用 Windows `RegisterHotKey` 事件，不安装键盘 hook，不截图、不 OCR、不 Overlay/注入。

## 设置与隐私

行为设置继续写 FACM 现有 `settings.ini`：

- `LeagueExitGameHotkey`
- `LeagueCloseLobbyHotkey`
- `LeagueCredentialHotkey`

空值代表关闭。`ui-text.ini` 只覆盖可见文字。

账号/密码不写任何配置、日志或诊断。只有用户按 credential hotkey 时读取一次当前剪贴板；前台不是受支持登录窗口则在读取剪贴板前直接阻断。

租号格式以**第一段连续一个或多个 `-`**作为账号/密码分隔符，例如 `123456789-----1316464saf`。密码后续若自身包含 `-` 会原样保留。CR/LF/TAB/NUL fail-closed。

输入序列是：当前字段 Ctrl+A -> 账号 -> Tab -> Ctrl+A -> 密码。第一版故意不发送 Enter，避免错误上下文直接提交登录。

## Hotkey contract

`LeagueHotkeyService` 是唯一全局热键实现：

- `RegisterHotKey / UnregisterHotKey`；
- `MOD_NOREPEAT`；
- F1-F12 可裸用；
- Ctrl/Alt/Shift/Win + key 可用；
- 裸 A-Z / 0-9 拒绝，避免聊天/账号输入误触；
- 三个 FACM action 不能绑定同一组合；
- 保存时是事务：先验证冲突，注册新组合失败则撤销本轮并恢复旧绑定；
- Dispose 全量 unregister。

## 进程动作边界

`一键退出游戏`：

- 只匹配 exact process name `League of Legends`；
- 先 `CloseMainWindow()`；
- 如果进程已经退出则不等待；
- 仍存活才短等待并对同一 PID `Kill()`；
- 不触碰 LeagueClient 或其它进程。

`一键关闭大厅`：

- 如果 `League of Legends` 仍运行，直接 blocked，零关闭；
- 只处理 `LeagueClient / LeagueClientUx / LeagueClientUxRender`；
- 同样正常关闭优先、精确 PID fallback；
- 不关闭 WeGame、浏览器或其它无关进程。

## UI

当前 main 的 Shell UX #105 尚未合并，所以 Gate 5 只新增**一个**托盘入口 `游戏效率`，不把三个动作各自塞到一级菜单。最终 Shell UX 合并后，应把同一个窗口迁入 `英雄联盟 > 游戏效率`，不要复制第二套窗口。

窗口只显示快捷键区，三行分别设置三个动作；支持直接编辑文本或点击 `录入` 捕获。保存失败（冲突/系统占用/非法键）保持旧有效绑定。

## deterministic smoke

`LeagueEfficiencySmokeTest` 已接入 Performance Contract，覆盖：

- hotkey parser/formatter；
- 裸字母/数字拒绝；
- duplicate binding；
- backend register 失败回滚；
- Dispose unregister；
- settings.ini parse/serialize 且不含 credential；
- 多横杠分隔和密码内横杠；
- 非登录前台零 SendInput；
- WeGame 登录前台账号/密码输入；
- 游戏存在时关闭大厅 blocked；
- 退出游戏只影响 exact game process；
- 退出游戏后关闭大厅不影响 unrelated process；
- no target = no-op；
- Module 只依赖 Settings，避免引入 League polling dependency；
- UI text defaults 非空。

## 腾讯实机验收

最终 Windows 候选需要验证：

1. 三个快捷键可设置、保存、重启恢复；
2. 重复快捷键/系统占用能清楚报错且旧快捷键仍有效；
3. 非登录窗口按 credential hotkey 零输入；
4. 登录页先点账号框，复制 `账号-----密码` 后按 hotkey，账号/密码正确且不自动 Enter；
5. 游戏仍在运行时 close-lobby hotkey 必须拒绝；
6. 水晶爆炸后 exit-game hotkey 能退出游戏并回到客户端；
7. 大厅时 close-lobby hotkey 只关闭 League 客户端；
8. 待机无明显 CPU / 网络增加。

## 未做 / 后续

- Gate 6：自动随机点赞 + 自动返回大厅；
- Gate 7：自动寻找对局 + 自动接受；
- 不在 Gate 5 中扩大 LCU writer allowlist；
- 不创建 Release/Tag，不修改线上更新配置。
