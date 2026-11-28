# LoL 客户端注入实测报告（GGman / facm 项目上下文）

时间：2026-11-24 11:00–11:20（Asia/Shanghai）
机器：Windows，session 1（console，Administrator）
安装：`D:\WeGameApps\英雄联盟`（WeGame 版，客户端 `16.23.840.6595`，CEF 108，patchline `Tencent`，region `NJ100` / `TENCENT`）

---

## 0. 结论（先看这一段）

| 项目 | 实测结果 |
|---|---|
| 大厅能不能跑 | **能**。11:12 启动后稳定在线，账号 `360安全卫士#43899`，`gameflow=None`（主界面）。之前 10:58 / 11:08 的两次全树退出发生在登录前后，之后这次没有再复现。 |
| LCU 接口通道 | **通**。`app-port=56680`，`--remoting-auth-token` 可读；REST 返回 200；WAMP `[5,"OnJsonApiEvent"]` 订阅成功并收到实时事件。 |
| 能否注入客户端 | **能，已实测成功**。`LeagueClientUxRender.exe`（CEF 108 渲染进程）里执行了任意原生代码：`CreateRemoteThread` → `VirtualAllocEx`+`WriteProcessMemory` → 本地验证到 `cmd.exe` 的父进程就是该渲染进程。 |
| 注入后被 ACE 处理了吗 | **本次没有**。3 次注入（shellcode 执行、LoadLibrary 加载、MessageBox）之后客户端所有进程存活，LCU 仍正常响应。注意：`ace-tips-render64.dll` 确实加载在渲染进程里，所以「不拦」是当前行为，不是架构保证。 |
| DevTools 可用吗 | **不可用**。渲染进程命令行里没有 `--remote-debugging-port`，端口不存在，无法走 CDP 直接注入 JS。渲染进程带 `--no-sandbox`，这也是注入成本低的原因。 |
| 游戏内进程（`League of Legends.exe`） | 未测试。它是 ACE 主要保护对象；本项目现成的方案是外部点击穿透 overlay，不碰游戏进程。 |

一句话：**「注入客户端」这条路在 LCU 侧是通的且不需要对抗（本次实测），但真正要做功能，成本最低的仍然是「LCU 接口 + 自有窗口」；注入只有在你需要改客户端内部 DOM/JS 时才值得动。**

---

## 1. 客户端进程拓扑（实测）

```
RiotClientServices.exe (6096)
└─ LeagueClient.exe (17972)
   └─ LeagueClientUx.exe (14256)        ← CEF 宿主 / LCU 服务端
      ├─ LeagueClientUxRender.exe (13812)  --type=gpu-process
      ├─ LeagueClientUxRender.exe (5576)   --type=utility storage
      ├─ LeagueClientUxRender.exe (1844)   --type=utility network
      ├─ LeagueClientUxRender.exe (3944)   --type=utility audio
      ├─ LeagueClientUxRender.exe (14916)  --type=renderer  --first-renderer-process   ← 主 UI
      └─ LeagueClientUxRender.exe (13184)  --type=renderer
```

渲染进程加载模块里可见 `ace-tips-render64.dll`（ACE 在客户端 UI 里也有模块）。渲染进程 81 个模块，含 `libcef.dll`（183 MB）、`chrome_elf.dll`、`d3d9.dll`、`dxgi.dll`。

关键启动参数（`LeagueClientUx.exe`）：

```
--app-port=56680  --remoting-auth-token=<REDACTED>
--riotclient-app-port=56581  --riotclient-auth-token=djINwWTh1N6_WVScG5ioxQ
--region=TENCENT --locale=zh_CN --rso_platform_id=NJ100 --ignore-certificate-errors
```

渲染进程参数（节选，注意**没有** debugging 开关）：

```
--type=renderer --first-renderer-process --no-sandbox --ignore-certificate-errors
--user-agent-product="LeagueOfLegendsClient/16.23.840.6595 (CEF 108)"
--user-data-dir="C:\Users\Administrator\AppData\Local\CEF\User Data" --app-port=56680
```

---

## 2. LCU 三条通道实测

### 2.1 REST（HTTPS + Basic `riot:<token>`）

```
GET /lol-summoner/v1/current-summoner   -> 200 {"accountId":16056685666,"gameName":"360安全卫士...
GET /lol-gameflow/v1/gameflow-phase     -> 200 "None"
GET /lol-chat/v1/me                     -> 200 {"availability":"chat","gameTag":"43899",...}
GET /riotclient/region-locale           -> 200 {"locale":"zh_CN","region":"TENCENT"...}
GET :56581/riotclient/region-locale     -> 200 （Riot Client 侧）
```

### 2.2 写操作（真的能驱动客户端）

```
POST /lol-lobby/v2/lobby {"queueId":450}  -> 200，客户端界面切到「极地大乱斗 1/5」，出现「寻找对局」
GET  /lol-gameflow/v1/gameflow-phase      -> "Lobby"
DELETE /lol-lobby/v2/lobby                -> 204，回到 "None"
```

界面截图：`shots/client-lobby-aram.png`（房间由纯 REST 创建，证明无需注入即可完全控制大厅）。

### 2.3 WAMP 事件流（`wss://127.0.0.1:56680/`，子协议 `wamp`）

```
handshake: HTTP/1.1 101 Switching Protocols
subscribe: [5,"OnJsonApiEvent"]
events  : [8,"OnJsonApiEvent",{"eventType":"Update","uri":"/lol-settings/v1/account/lol-notifications",...}]
          共 4 条 / 10 秒，涉及 /lol-settings/*、/data-store/v1/install-settings/lcu-settings
```

---

## 3. 注入实测（4 项证据）

工具：`tools/lol_inject_poc.py`（纯 ctypes，零第三方依赖，零编译器）

### 3.1 前置：先在自己进程上验证 shellcode

```
python tools/lol_inject_poc.py --pid <notepad> --text "..." --caption GGman-DryRun
→ OpenProcess OK / VirtualAllocEx(RWX) OK / 51 字节 shellcode / CreateRemoteThread OK
→ 对话框窗口出现在 notepad 进程内（截图 shots/dryrun-msgbox.png）
```

### 3.2 证据 A：在渲染进程内执行 `WinExec`

```
python tools/lol_inject_poc.py --pid 14916 --exec "cmd.exe /c echo GGman-injection-ok > ...\inject-proof.txt"
→ CreateRemoteThread tid=3448
→ 文件生成：captures/inject-proof.txt = "GGman-injection-ok"
```

### 3.3 证据 B：子进程父子关系（最硬的一条）

```
python tools/lol_inject_poc.py --pid 14916 --exec "cmd.exe /c ping -n 8 127.0.0.1 >nul"
→ WMI: ProcessId=17492 ParentProcessId=14916 Name=cmd.exe
       14916 = LeagueClientUxRender.exe
```

`cmd.exe` 的父进程就是这个渲染进程，只可能由它内部代码创建。

### 3.4 证据 C：`LoadLibraryW` 远程加载

```
python tools/lol_inject_poc.py --pid 13184 --dll "C:\Windows\System32\msimg32.dll"
→ CreateRemoteThread tid=15496，线程返回值 0x850B0000
→ 模块表新增：('msimg32.dll', 0x7FFE850B0000, 28672)
```

结论：`CreateRemoteThread(LoadLibraryW)` 这条路在渲染进程里可用 → **任何自编 x64 DLL 都能落地**。

### 3.5 证据 D：`MessageBoxW` 窗口落在渲染进程

```
python tools/lol_inject_poc.py --pid 14916 --text "..." --caption GGman-Injection --flags 0x50040
→ EnumWindows(pid=14916, title="GGman-Injection") 命中 hwnd 1247736 / 852772
→ 事后可用 --close-dialog 关闭，客户端无异常
```

### 3.6 注入后客户端状态

三次注入后：

```
LeagueClient.exe / LeagueClientUx.exe / 6× LeagueClientUxRender.exe / RiotClientServices.exe 全部存活
GET /lol-summoner/v1/current-summoner -> 200
```

没有观察到踢线、闪退、ACE-Helper 重新拉起。

---

## 4. 为什么没走 DevTools（CEF 的「正规」注入口）

- 渲染进程命令行无 `--remote-debugging-port`，`LeagueClientUx.exe` 的硬编码 CEF 开关表里也只有 `--disable-*`、`--allow-*`，没有 debugging 项（`LeagueClientUx.exe` 里能搜到 `--app-port`、`--enable-logging=stderr`、`--disable-features`、`--allow-file-access-from-files` 等）。
- 因此 `http://127.0.0.1:<port>/json/list` 无处可连，CDP（`Runtime.evaluate` / `Page.addScriptToEvaluateOnNewDocument`）用不了。
- 想拿到 CDP：必须自己开端口 → 需要改 CEF 初始化 → 回到「注入一个 DLL 去 hook CEF」这条路。既然注入已经通，这条就变成可选项而不是前提。

---

## 5. 真要「改客户端内部」的三条落地路径

### 路径 1（推荐，零对抗）：LCU + 自有窗口
- 数据/动作：REST + WAMP（本报告第 2 节已全部打通）
- UI：外部 topmost 点击穿透窗口（GGman 现在就是这么做的：`LeagueRespawnOverlayHost`）
- 优点：随客户端版本升级只需适配端点；不会被 ACE 关注；崩溃不影响客户端
- 局限：只能显示你自己的窗口，改不了客户端原生界面

### 路径 2：注入 x64 DLL 到 `LeagueClientUxRender.exe`
- 落地方式已验证（证据 C）。DLL 里能做：
  - 子类化 CEF 窗口（`Chrome_WidgetWin_0` / `Chrome_RenderWidgetHostHWND`）的 `WndProc`，拦输入/自绘
  - 或者扫 `libcef.dll` 的导出/模式，拿到 `CefBrowserHost` 相关调用后 `ExecuteJavaScript`（成本最高，需要逆向 + 每版本维护偏移）
- 需要编译环境：本机**没有** `cl.exe`/`msbuild`/`dotnet`，只有 `csc.exe`（.NET Framework 4.8）。要产出原生 DLL，需装 VS 2022 Build Tools（桌面 C++），或先用现有 shellcode 路线做小功能（shellcode 能直接调 Win32，做不了复杂逻辑）。
- 代价：客户端每次大版本更新都可能失效；被 ACE 盯上是概率问题（本次 20 分钟窗口内未触发）。

### 路径 3：游戏内 overlay（`League of Legends.exe`）
- 需要 hook `IDXGISwapChain::Present`，注入游戏进程。
- 国服 `LeagueClient\AntiCheatExpert`、`Game\AntiCheatExpert`、`ACE-Helper.exe`、`ace-tips-render64.dll` 都在，游戏进程是保护重点，这条路成本/风险最高。
- 现成替代：外部 overlay + `https://127.0.0.1:2999/liveclientdata/playerlist`（GGman 已实现）。

---

## 6. 复现工具（`D:\project2\ggman\tools\`）

| 脚本 | 用途 |
|---|---|
| `lol_launch_click.py` | 找到 WeGame 窗口（`CefTopWindow`/`WeGame`），点右下角「启动」 |
| `lol_capture_session.py` | 启动后 250ms 采样，抓进程生命周期 / 命令行 / 端口 / 锁文件 |
| `lol_probe_lcu.py` | 同上 + Ux 一出现就读 `--app-port`/`--remoting-auth-token` 并打 REST |
| `lcu_probe_live.py` | 已知 port/token 时直接打 REST（summoner / gameflow / chat / region） |
| `lcu_wamp_probe.py` | 裸 TLS WebSocket 实现 WAMP 订阅，收 `OnJsonApiEvent` |
| `lol_inject_poc.py` | 注入器：`--exec`(WinExec) / `--dll`(LoadLibraryW) / 无参(MessageBox shellcode) / `--modules` / `--close-dialog` |
| `strscan.py` | 二进制 ASCII/UTF-16 字符串检索 |

常用命令：

```powershell
$py = "C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe"
# 1) 启动（会真的点击 WeGame 的启动按钮）
& $py tools\lol_launch_click.py
# 2) 读会话并打接口
& $py tools\lcu_probe_live.py
# 3) 事件流
$env:LCU_PORT="56680"; $env:LCU_TOKEN="<token>"; & $py tools\lcu_wamp_probe.py --seconds 10
# 4) 注入（先 --modules 看目标，再 --exec 验证）
& $py tools\lol_inject_poc.py --pid <renderer_pid> --exec "cmd.exe /c echo ok > C:\temp\ok.txt"
```

**注意**：`--app-port` / token 每次启动都变，必须从 `LeagueClientUx.exe` 命令行实时读取（本项目 `LeagueClientRuntime.cs` 也是这么做的，只是它读 `lockfile` 或 `CommandLine`；本次 `LeagueClient\lockfile` 一直是 0 字节，说明国服这个构建**不写 lockfile**，只能走命令行读取）。

---

## 7. 本次没做 / 待确认

1. **游戏进程注入**：未测试（需要真正进一局，本机 ACE + 屏幕共享/虚拟键鼠有闪退历史）。
2. **注入的持久性**：渲染进程重启（换页面、客户端更新）后注入失效，需要常驻监视器重新注入。
3. **ACE 是否事后追封**：无法在本地验证；本次只是「20 分钟内没有即时反应」。
4. **用 DLL 做真功能**：需要 x64 编译器。要的话我先装 VS 2022 Build Tools，再写一个「找 CEF 窗口 + 自绘 + 读 LCU 数据」的 DLL 骨架。
5. **WAD/插件注入**：`LeagueClient\Plugins\*` 是 `description.json` + `assets.wad`（Riot WAD 格式，路径哈希 + 压缩块），要改客户端原生 UI 就得写 WAD 读写器，属于路径 2 的替代方案，成本高于 DLL 注入。

---

## 8. 一句话建议

要「客户端里出现 GGman 的东西」，先用 **LCU 数据 + 自有穿透窗口**（成本最低、已验证可完全驱动大厅）；只有当你需要**改客户端原生界面**或**在 CEF 内部拿 JS 上下文**时，才用已验证的 `CreateRemoteThread` 注入一个自编 x64 DLL —— 那一步的编译环境需要先补上。
