# GGman（facm）客户端依赖与「注入客户端」可行性评估

评估对象：`https://github.com/xianyumht-cmd/facm`
落地位置：`D:\project2\ggman\facm`
评估提交：`4f34c41221b03f49774a5ea8fbaefe6e0932d670`（2026-11-23，"docs: record verified 3.5.89 signed release and field acceptance"）
代码体量：601 个受版本控制文件，459 个 `.cs`，约 141,446 行，其中 63 个 `*SmokeTest.cs`

---

## 0. 结论速览

| 问题 | 结论 |
|---|---|
| 这个项目注入客户端吗？ | 不注入。全仓 0 处进程内存读写 / 远程线程 / 全局钩子 / D3D Hook；全部功能走 LCU 官方 HTTP+WAMP 接口、局内 2999 接口、第三方站点 HTTP 和文件读写。 |
| 依赖本地客户端吗？ | 依赖。所有 LOL 相关功能要求 `LeagueClientUx` 正在运行并登录（拿 `lockfile` 里的端口与 token）；局内功能才需要 `Game\League of Legends.exe`。非 LOL 功能（悬浮球/主题/宠物/清理/更新/在线中心）不需要任何游戏文件。 |
| 能不能改成注入？ | 技术上可行（LCU 是 CEF/Chromium + Ember），现实有三条路：CEF DevTools 接管 → 窗口子类化/资源替换 → DLL 注入 renderer；游戏进程侧另有 ACE 驱动保护，成本最高。现有架构加注入 = 新写一条通道，不是改开关。 |
| 需不需要你下载游戏本体？ | 需要。装完整客户端（含 `Game\` 目录）最省事；只装大厅不给 `Game\` 时，局内那几项（重生倒计时、快捷发言、ESC 配置同步、游戏修复）用不了。 |

---

## 1. 技术栈与运行形态

- 主程序：`src/FACM`，`net48` + WinForms，`AssemblyName=GGman`，单 EXE（`src/FACM/FACM.csproj`）
- 可选子项目：`FACM.PetHost`（桌宠独立宿主）、`FACM.ToolBundle`（内嵌工具 DLL）、`FACM.Updater`（更新替换器）
- 清单：`asInvoker`，默认不提权（`src/FACM/app.manifest`），个别动作走 `ElevationService` 单独提权
- 可选 UI 线：`Presentation/Glass/**` 用 WebView2（`UseGlassWorkbenchPreview` / `UseUnifiedReleaseCandidate` 才编译，普通构建该目录被 `Compile Remove`）
- 本机工具链实测：**无 `dotnet` SDK、无 `msbuild`**；`.NET Framework 4.8 (Release 528372)` 已安装

---

## 2. 它到底怎么「跟客户端说话」——7 条真实通道

| # | 通道 | 地址 / 凭据 | 代码证据 |
|---|---|---|---|
| 1 | **LCU REST** | `https://127.0.0.1:<app-port>/`，Basic `riot:<remoting-auth-token>` | `League/LeagueClientRuntime.cs`：`:11` 解析 lockfile（`proc:pid:port:token:https`）、`:98` 解析命令行 `--app-port/--remoting-auth-token/--rso_platform_id`、`:23` `BaseUri`、`:196` 证书校验直接 `return true` |
| 2 | **会话发现** | 进程名 `LeagueClientUx`/`LeagueClient` + `MainModule.FileName` 定位 lockfile；主模块被拒时退到 WMI | `LeagueClientRuntime.cs:75`、`League/WmiProcessImagePathReader.cs`（`Win32_Process` 查 `ExecutablePath`/`CommandLine`）、`League/ResilientLeagueClientSessionDiscovery.cs` |
| 3 | **WAMP 事件流** | `wss://127.0.0.1:<port>/`，子协议 `wamp`，订阅帧 `[5,"OnJsonApiEvent"]` | `League/LeagueChatEventStream.cs:47-65`，解析 `[8,"OnJsonApiEvent",...]` |
| 4 | **局内 Live Client Data** | `https://127.0.0.1:2999/liveclientdata/playerlist` | `League/LeagueRespawnTimer.cs:129`；注意 `League/LeaguePublicDataTransport.cs:138` 明确**拒绝** loopback，说明 2999 只由专用通道访问 |
| 5 | **文件层** | `Game/Config/*.ini`（ESC 设置读写与备份）、`LeagueClient/*.log`（清理） | `Services/EscGameDirectoryLocator.cs`、`Configuration/CleanupProfile.cs`（`CleanupContainerRelativePath="Game"`、`LogFolderRelativePath="LeagueClient"`） |
| 6 | **输入层** | `SendInput` 合成按键，仅当前台窗口属于 `League of Legends` 进程且 gameflow=`InProgress` 时 | `League/LeagueInGameMessageSender.cs`（`SendInput`、`GetAsyncKeyState`、`GetForegroundWindow`；`:43` 起为 Unicode 注入按键） |
| 7 | **外部 HTTP + 云** | OP.GG / ARAMGG / Hexdata / 腾讯公告 / DataDragon；CloudBase 账号与配置同步 | `Mayhem/*Service.cs`、`Online/*CloudClient.cs`、`cloudbase/sql/*.sql` |

LCU 端点规模：20 个不同 `/lol-*` 路由族、70+ 具体路径，覆盖
`/lol-matchmaking/v1/ready-check/accept`、`/lol-lobby/v2/lobby`、`/lol-champ-select/v1/session/actions/*`、`/lol-perks/v1/pages`、`/lol-chat/v1/me`、`/lol-honor-v2/*`、`/lol-gameflow/v1/reconnect`、`/lol-replays/*`、`/lol-pre-end-of-game/*` 等。

也就是说：**它的能力上限完全等于 LCU 公开接口的能力上限**，没有第二形态。

---

## 3. 注入结论：负证据清单

对 459 个 `.cs` 做全量检索，下列符号**命中数全部为 0**：

`WriteProcessMemory`、`ReadProcessMemory`、`CreateRemoteThread`、`VirtualAllocEx`、`NtCreateThreadEx`、`SetWindowsHookEx`、`SetWindowLong(SetWindowLongPtr)`、`EasyHook`、`MinHook`、`Detours`、`IDXGISwapChain`、`D3D11`、`.sys` 加载

进程级接触只有 4 类，全部是「读路径 + 判窗口」级别：

1. `Process.GetProcessesByName("LeagueClientUx")` + `process.MainModule.FileName` → 拼 `lockfile` 路径（`LeagueClientRuntime.cs:75`）
2. WMI `SELECT ExecutablePath/CommandLine FROM Win32_Process`（`WmiProcessImagePathReader.cs`）—— 用 WMI 而不是 `OpenProcess`，就是为了避开权限与句柄
3. `GetForegroundWindow` + `GetWindowThreadProcessId` 判「游戏是否前台」（`League/LeagueRespawnOverlayHost.cs:14-33`）
4. `SendInput` 合成键盘事件（前台条件严格）

仓库自己把「注入」写成非目标：

- `README.md`：「不创建第二轮轮询或第二个 League session，**不做游戏内注入或 Overlay**」
- `docs/GGMAN-PRODUCT-DESIGN.md:16`：non-goals —— `add game injection/memory reading`
- `docs/DECISIONS.md:158`：用 **click-through WinForms overlay**，而不是 `injected game component`
- `docs/HANDOFF-20260815-TOOLS-GATE3.md:78`：「不 overlay / 注入」；`HANDOFF-20260826-LEAGUE-UX.md:16`：「不做游戏注入或 Hook」

内嵌的两个外部工具也不是注入器：

- `tools/Fix-LCU-Window.exe`（25,088 B，.NET，仅见 `SetWindowPos`）= 旧版修 LCU 窗口位置/尺寸；已被原生 `League/LeagueGameRepairService.cs` 取代，`Services/ToolRunner.cs:20` 现在直接抛 `NotSupportedException`
- `tools/clean driver.exe`（315,829 B，原生 PE32，含 `OpenProcess/LoadLibrary/GetProcAddress`，Delphi RTL 痕迹）= 旧版清理驱动残留的工具；`docs/LEGACY-ANALYSIS.md` 记录 2.1 版起「不保留涉及驱动、安全组件移除或批量游戏目录删除的旧工具」，它作为资源仍躺在 `FACM.ToolBundle.csproj:19` 里但没有调用点

---

## 4. 客户端依赖矩阵

| 功能 | 需要什么 | 客户端关掉能否用 |
|---|---|---|
| 悬浮球 / 托盘 / 主题 / UI 文字自定义 / 宠物 / 环境清理 / 诊断 / 更新 | 无 | ✅ 能 |
| 我的 GGman（账号、配置云同步） | 网络 | ✅ 能（与 LOL 无关） |
| 大厅类：战绩/玩家查询/BP 助手/自动接受/符文页/聊天状态/Mayhem/SGP 战绩 | `LeagueClientUx` 在跑且已登录 | ❌ 不能（每 750ms 重试发现会话） |
| 局内：重生倒计时 overlay、局内快捷发言 | 游戏进程 + `2999` | ❌ 不能 |
| ESC 配置同步 / 游戏修复 | `Game\Config`、`Game\` 目录 | ❌ 不能 |
| 赛后自动化（荣誉、再来一局等） | `LeagueClientUx` + gameflow | ❌ 不能 |

`EscGameDirectoryLocator.cs` 同时认 WeGame 布局（`WeGame\...\LeagueClient\LeagueClient.exe`）与腾讯独立版布局，两种安装都行。

---

## 5. 如果真要「注入客户端」：四条路线的成本与做法

### 路线 A：不注入（现状，成本最低）
LCU 已经给了事件（WAMP）、状态（REST）、动作（REST 写入）、局内数据（2999）。想加「客户端里出现的东西」，先确认是不是 A 就能做——项目 90% 需求都是这么实现的。

### 路线 B：CEF DevTools 接管渲染进程（中等成本，必须实测）
`LeagueClientUxRender.exe` 是 CEF（Chromium Embedded Framework），客户端 UI 是 Ember 应用。只要能连上 DevTools 协议，`Runtime.evaluate` 就等于在客户端里执行任意 JS（改 DOM、拦 fetch、注入样式），本质是「脚本级注入」，不进内存。

装好客户端后先做三行探测：

```powershell
# 1) 看渲染进程启动参数里有没有 devtools 端口
Get-CimInstance Win32_Process -Filter "name='LeagueClientUxRender.exe'" |
  Select-Object ProcessId, CommandLine | Format-List

# 2) 该进程在听哪些端口
$pidRender = (Get-Process LeagueClientUxRender).Id
netstat -ano | Select-String "LISTENING" | Select-String " $pidRender$"

# 3) 有端口就试 targets 列表
curl.exe -s http://127.0.0.1:<PORT>/json/list
```

- 出现 `/json/list` → 直接 `ws://127.0.0.1:<PORT>/devtools/page/<id>`，`Page.addScriptToEvaluateOnNewDocument` 注入常驻脚本，`Runtime.evaluate` 即时改 UI。这条路不需要 DLL，重启客户端失效，正好可控。
- 没有端口 → 因为 LCU 由 `RiotClientServices.exe` 拼参数启动，外部传 `--remote-debugging-port` 通常不生效，于是落到路线 C。

### 路线 C：DLL 注入 renderer / 窗口子类化（高成本，按版本维护）
两条分支，任选：

**(C1) 经典注入**
```
OpenProcess(PROCESS_ALL_ACCESS, PID_LeagueClientUxRender)
→ VirtualAllocEx(RWX)
→ WriteProcessMemory(WriteProcessMemory 写入 DLL 全路径)
→ CreateRemoteThread(LoadLibraryW)
```
位宽必须与目标一致（x64 renderer → x64 DLL）。DLL 里做两件事之一：
- `FindWindow("Chrome_WidgetWin_0"/"Chrome_RenderWidgetHostHWND")` 后 `SetWindowLongPtr(GWLP_WNDPROC)` 子类化，拦输入、改绘制、接管右键菜单；
- 或扫描 `libcef.dll` 的 JS 绑定/message loop，跑自己的脚本桥。

**(C2) 纯外部的「客户端内 UI」**（不改目标内存，风险最低）
把无边框窗口 `SetParent` 到客户端窗口 + `WS_EX_LAYERED`/`WS_EX_TRANSPARENT`，视觉上就在客户端里，进程还是你自己。项目现在的 overlay 就是这个思路的变体（topmost + 点击穿透 + 前台判定）。

维护点：LCU 每次大版本更新都会换资源与偏移，C1 需要重新定位；客户端带完整性/更新校验，改本地文件可能被覆盖或校验失败。

### 路线 D：游戏进程 overlay（成本最高）
`League of Legends.exe` 是 D3D11 渲染，做游戏内 overlay 常规做法是注入后 hook `IDXGISwapChain::Present`（vtable 换函数）画 UI。现实约束：国服安装目录里就有反作弊组件目录 `AntiCheatExpert`（`Configuration/CleanupProfile.cs:12-22` 引用 `Launcher\AntiCheatExpert`、`LeagueClient\AntiCheatExpert`），驱动级保护，这条要跟反作弊正面对抗。
现成替代：**外部 topmost 点击穿透窗口**（项目已在用），数据源 `2999`，不碰游戏进程。

---

## 6. 建议的下一步（可直接照做）

1. **装客户端**：WeGame 版或腾讯官网独立版，务必保留 `Game\` 目录（`Game\League of Legends.exe` + `Game\Config`）。
2. **先验证发现链路**（不依赖 GGman）：
   ```powershell
   $p = Get-Process LeagueClientUx -ErrorAction Stop
   $dir = Split-Path $p.MainModule.FileName
   $lock = Get-Content (Join-Path $dir 'lockfile') -Raw
   $parts = $lock.Trim().Split(':'); $port = $parts[2]; $token = $parts[3]
   $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("riot:$token"))
   curl.exe -sk -H "Authorization: Basic $b64" "https://127.0.0.1:$port/lol-summoner/v1/current-summoner"
   ```
   返回 JSON = LCU 通；再进一局游戏验证 `curl.exe -sk https://127.0.0.1:2999/liveclientdata/playerlist`。
3. **准备编译环境**：本机缺 `dotnet`/`msbuild`。装 VS 2022 Build Tools（.NET 桌面开发 + .NET Framework 4.8 目标包），然后
   `msbuild FACM.sln /p:Configuration=Release` 或 `dotnet build FACM.sln -c Release`；
   仓库自带 `FACM-本地一键配置并构建.bat`、`scripts/setup-local-build.ps1`、`scripts/build-release.ps1`。
   不想编译就直接用官方 Release 的 `GGman.exe`（`online/version.json` 有当前版本与镜像）。
4. **离线自检**（不装游戏也能跑，28 个开关，例如）：
   `--floating-ball-test`、`--game-locator-test`、`--league-dashboard-test`、`--facm-host-test`、`--cleanup`、`--performance-contract-test`
5. **要注入就先做路线 B 的三行探测**，把 `LeagueClientUxRender` 的 `CommandLine` 和监听端口发我，我据此给具体注入代码。

---

## 7. 工程维护风险（与合规无关，纯技术）

- LCU 是不保证兼容的私有接口：写入类端点（`ready-check/accept`、`champ-select/session/actions/*`、`perks/pages`）每赛季都要回归。
- 局内聊天用 `SendInput`，属于宏级输入合成，反作弊侧对「非人手输入模式」的判定是黑盒。
- `LeaguePublicDataTransport` 硬拒绝 loopback，说明项目做过流量约束；改注入时这条约束要先想清楚要不要动。
- 云同步把软件偏好、`ui-text.ini`、LOL ESC 设置发到 CloudBase（`cloudbase/sql/*.sql`），不含游戏安装路径与登录凭据（README 明示）。
