# 为什么开了 RayLink / 虚拟键鼠还能进大厅且不闪退

结论先说：**那些不是大厅闪退的原因。本次会话里客户端两次"闪退"都是客户端自己的单实例仲裁把我重复启动的进程关掉了，不是 ACE 检测。**

---

## 1. 现场事实

当前状态（2026-11-24 12:40）：

| 项 | 值 |
|---|---|
| 客户端启动时间 | 11:45:47（`LeagueClient.exe` pid 18968） |
| 已持续 | 55 分钟以上，期间注入 5 次 DLL、杀掉并重启过一次 |
| 屏幕共享 | `RayLink.exe`(8160)、`RayLinkCapturer.exe`(3152/16732)、`RayLinkService.exe`(3200/7540)、`RayLinkWatch.exe`(4028) 全程在跑 |
| 虚拟键鼠 | MouseWithoutBorders 系进程、`TextInputHost.exe`、QQ 远程组件全程在跑 |
| ACE 组件 | `ACE-Helper.exe` 在启动瞬间出现过又退出；`ACE-Tray.dat` 时间戳 = 客户端启动时刻；运行中客户端渲染进程里加载着 `ace-tips-render64.dll` |
| 注入 | `ggman_core.dll` 在 `LeagueClientUxRender.exe` 里跑了 20 分钟以上，期间的 `CreateRemoteThread` 没有引起任何反应 |

## 2. 那两次"闪退"到底发生了什么

有日志可查的三次客户端启动：

| 时间 | PID | 结局 | 日志证据 |
|---|---|---|---|
| 10:58:35 | 15924 | 8.8 s 时进程消失 | `Game/Logs/.../2026-11-24T10-58-35_15924_LeagueClient.log` 停在插件初始化；Ux 日志：「Failed to find app process pid 15924, terminating ux process」 |
| 11:08:47 | 6720 | 31 s 时**优雅退出** | 该日志结尾：`Unloading UX process` → `Waiting for the UX to quit` → `Ux state set to Quit`（这是客户端自己关的，不是被杀的） |
| 11:12:21 | 17972 | 我 11:45 手动 kill | 日志在 11:45:25 截断 |

`Unloading UX process` 是 **app 进程主动让 Ux 退出**的动作。触发原因：那一刻我为了验证「直接启动能不能行」跑过一次 `LeagueClient.exe`（pid 10752），它没有退出，成了僵尸实例；随后通过 WeGame 正常启动的新实例与之冲突，单实例仲裁把新实例关掉了。

换句话说：**是我自己的操作把客户端"弄闪退"了，ACE 没参与。**

11:45 那次干净重启前，我做了唯一一件正确的事：`Stop-Process` 掉所有 `LeagueClient* / RiotClientServices`，确保只有一个实例，然后走 WeGame 的 SSO 启动。之后 55 分钟没有任何异常。

## 3. ACE 在这台机器上到底保护什么

安装目录里有两套 ACE 载荷，用途不同：

| 位置 | 主要模块 | 谁加载 |
|---|---|---|
| `LeagueClient\AntiCheatExpert\` | `ACE-Base64.dll`、`ACE-Tips64.dll`、`ACE-Trace.dll`、`SGuard\SGuard64.exe` | 客户端进程 / 常驻服务。渲染进程里实际只看到 `ace-tips-render64.dll`（提示 UI），没有驱动级校验 |
| `Game\AntiCheatExpert\` | `ACE-Base64.dll`、`ACE-DRV64.dll`、`ACE-IDS64.dll`、`ACE-PBC-Game64.dll`、`ACE-Report64.dll`、`SGuard\Plugins\ACE-SSC-DRV64.sys` | **游戏进程**（`League of Legends.exe`）— 这才是带驱动、带完整性校验、会查虚拟输入/截图/调试器的那一层 |

所以我们在大厅里干什么（注入渲染进程、`CreateRemoteThread`、读 Ux 的 PEB）ACE 都不管；一旦进游戏，`ACE-DRV64` + `ACE-SSC-DRV64.sys` 才上场。

这也是为什么本机历史上"进游戏闪退"、而"进大厅"没事——两套保护强度差一个量级。

## 4. 稳定启动配方（以后照这个来）

```powershell
# 1) 清干净，保证单实例
Get-Process LeagueClient,RiotClientServices,LeagueClientUx,LeagueClientUxRender -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
# 2) 只用 WeGame 的「启动」（走 TCLS 的 SSO，别直接跑 LeagueClient.exe）
python tools\wegame_launch.py --wait 100
# 3) 起来之后不要再动进程树，注入用 CreateRemoteThread 即可
python tools\inject_watch.py --interval 2
```

要点：

1. **绝不直接 `LeagueClient.exe`**（会留下僵尸实例，导致后面的正规启动被单实例逻辑关掉）。
2. **杀干净再启动**：`LeagueClient / LeagueClientUx / LeagueClientUxRender / RiotClientServices` 四个名字一个不留。
3. **别在客户端初始化阶段（前 30 s）做进程操作**，那段时间正是 Ux 与 app 握手的窗口。
4. **注入只碰渲染进程**，不要碰 `LeagueClient.exe`（app 进程退出会让整个 Ux 树跟着散）。
5. 进游戏另说：那一步才需要面对 ACE 驱动层。

## 5. 与"注入被检测"的关系

本次观察到的边界：

- 注入 `LeagueClientUxRender.exe`：**不受阻**，`CreateRemoteThread` + `LoadLibraryW` + RWX 分配 + 远程线程回调全部成功，客户端继续稳定运行（期间 LCU 一直正常响应）。
- 渲染进程带 `--no-sandbox`，这本来就是客户端为了自己的 `rcp-fe-*` 插件体系开的口子。
- 唯一需要留意的是**渲染进程会被客户端重启**（换页、更新、`Ux state` 切换），所以必须有 `inject_watch.py` 这种补注入守护。

如果将来要在 `League of Legends.exe` 里做 overlay，那是完全不同的战场：`ACE-DRV64` + 内核驱动 + 完整性报告（`ACE-Report64.dll`）。到那一步需要单独做可行性验证，不能拿大厅的经验外推。
