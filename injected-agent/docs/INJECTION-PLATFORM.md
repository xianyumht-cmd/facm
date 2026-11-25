# GGman 注入平台（ggman_hud.dll / ggman_core.dll）

两个层次的产物：

| 文件 | 说明 |
|---|---|
| `dll/ggman_hud.c` | v1–v4：客户端内 HUD，数据靠外部推送（已完成，保留作参考） |
| `dll/ggman_core.c` | **v5：自给自足 agent**——自己读 LCU 端口与 token、自己走 REST、自动接受状态机、热键、面板。详见 [AGENT-DESIGN.md](AGENT-DESIGN.md) |

把「客户端内渲染 + 实时数据」这条线做成可复用的平台，替代纯外部涂层。当前实现已跑通：
**把 DLL 注入 `LeagueClientUxRender.exe`，在英雄联盟客户端窗口内画一块 HUD，并接收外部推送的实时数据。**

```
GGman.exe / 脚本                     注入的 DLL（客户端渲染进程内）
┌──────────────────────┐  WM_COPYDATA   ┌────────────────────────────┐
│ LCU REST + WAMP 取数 │ ─────────────► │ ggman_core.dll             │
│ tools/hud_feed.py    │                │  · 自读 port/token (PEB)    │
└──────────────────────┘                │  · 自驱 REST + 自动接受      │
        ▲                                │  · 点击穿透 HUD + 热键      │
        │ HTTPS 127.0.0.1:PORT           │  · F8/F9/F10               │
        └──────────── LCU ───────────────┴────────────────────────────┘
```

## 1. 工具链（已装在工作目录，无需管理员/VS）

| 组件 | 位置 | 说明 |
|---|---|---|
| Zig 0.17.1 | `toolchain/zig-x86_64-windows-0.17.1/zig.exe` | 当 C 编译器用：`zig cc -target x86_64-windows-gnu`，内置 mingw-w64 头与导入库，产出标准 x64 PE DLL |
| Python 3.14 | `C:\Users\Administrator\AppData\Local\Programs\Python\Python314\python.exe` | 所有注入/取数脚本只用标准库（ctypes/ssl/urllib） |

编译：

```powershell
powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1                 # 只编译
powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1 -Run            # 编译并注入当前 renderer
powershell -ExecutionPolicy Bypass -File dll\build_hud.ps1 -Out ggman_hud_v5.dll
```

## 2. 运行链路

```powershell
# 0) 启动 WeGame 里的英雄联盟（会真的点右下角「启动」并等到 LCU 起来）
python tools\wegame_launch.py

# 1) 注入（没有 HUD 时自动挑一个 renderer，用唯一文件名避免重复加载）
python tools\inject_watch.py --once
python tools\inject_watch.py --interval 2      # 常驻：renderer 重启后自动补注入

# 2) 持续喂数据（召唤师/阶段/房间/段位，3 秒一轮）
python tools\hud_feed.py --interval 3

# 辅助
python tools\hud_send.py "自定义第一行" "第二行"     # 手推文本
python tools\hud_send.py --toggle                   # 显隐
python tools\hud_cleanup.py --list                  # 看实例
python tools\hud_cleanup.py                         # 全部拆掉
```

关键细节：

- **不能重复加载同路径 DLL**：Windows 对已加载模块直接返回旧句柄、不跑 `DllMain`。所以
  `inject_watch.py` / `build_hud.ps1 -Run` 每次都复制成 `ggman_hud_<pid>_<hash>.dll` 再注入。
- **单实例**：DLL 用命名互斥体 `Local\GGmanHudOwner` 保证只有一个实例在画；旧实例释放后新实例接管。
- **注入目标**：任何 `LeagueClientUxRender.exe --type=renderer` 进程都行。客户端会重启 renderer
  （页面刷新、客户端更新、退出重进都会换 pid），`inject_watch.py` 以「桌面上有没有可见的
  `GGmanHudOverlay` 窗口」为判据，缺了就补。
- **canonical DLL**：`dll/build/ggman_hud.dll` 永远是最新版，watcher 默认注入它；改代码后
  `build_hud.ps1 -Out ggman_hud.dll` 覆盖即可（老进程没释放时先 `hud_cleanup.py`）。

## 3. IPC 协议（外部 → DLL）

窗口类 `GGmanHudIPC`（隐藏顶层窗口），`FindWindowW(L"GGmanHudIPC", NULL)` 后发
`WM_COPYDATA`，负载是：

```c
typedef struct {
    DWORD kind;       // 1=设置文本行，2=显隐，3=清空
    DWORD flags;      // kind 2 时：0 隐藏 / 1 显示 / 2 切换
    WCHAR text[2048]; // kind 1 时的行文本，'\n' 分隔，最多 8 行
} GGmanHudCommand;
```

Python 侧现成的封装：`tools/hud_send.py`（`send(kind, flags, text)`）。

## 4. 当前能力 vs 外部涂层

| 能力 | 外部涂层 | 本方案（注入） |
|---|---|---|
| 客户端窗口内绘制 | 需要自己跟踪 rect/置顶 | 同进程窗口，跟随客户端 client rect，天然贴合 |
| 生命周期 | 独立 | 跟 renderer 同生共死（renderer 崩溃即消失，反之亦然） |
| 输入拦截 | 做不到（客户端窗口在别的进程） | 同进程内可以 `SetWindowLongPtr` 子类化自己创建的窗口/挂钩消息 |
| CEF / JS 上下文 | 无 | 同进程，后续可挂钩 Chromium（需逆向偏移） |
| 游戏内（`League of Legends.exe`） | topmost 覆盖，独占全屏会失效 | 需另做游戏进程注入（DXGI Present hook），见路线图 |

## 5. 已知坑与对策

| 现象 | 原因 | 对策 |
|---|---|---|
| `RegisterHotKey(F8)` 返回 1409 | 同一进程内旧实例仍注册着 | DLL 内置回退链：F8 → Alt+F8 → Ctrl+Alt+F8 → Ctrl+Shift+F8，并写日志 |
| 同路径 DLL 注入后没反应 | 模块已加载 | 每次唯一文件名注入 |
| HUD 不显示 | 客户端窗口被最小化/尺寸太小（<400×300） | `reposition()` 自动隐藏，恢复窗口后 250ms 定时器重新显示 |
| 国服没有 `lockfile` | 该构建不写锁文件 | 从 `LeagueClientUx.exe` 命令行读 `--app-port` / `--remoting-auth-token` |
| 注入后客户端闪退 | ACE / 环境（屏幕共享、虚拟键鼠） | 本次实测注入未被拦；但 `League of Legends.exe` 侧保护更硬 |

## 6. 路线图

**阶段 1（已完成）**：注入 renderer + HUD 渲染 + IPC + LCU 实时数据 + 单实例 + 热键 + 自动重注入。

**阶段 2（下一步，成本中）**：让 HUD 有交互
- 在 DLL 里注册自己的窗口消息循环，接收点击（取消 `WS_EX_TRANSPARENT`），做可点击小面板
- 用 `RegisterHotKey` + `SendInput` 组合实现「一键接受对局 / 一键 ban/pick」，全部走 LCU REST，HUD 只做交互壳

**阶段 3（成本高）**：CEF 内部
- 目标：在客户端页面里执行 JS（等价 CDP）。路线是 hook `libcef.dll` 的
  `CefBrowserHost` 相关导出或 Blink 的脚本执行入口；需要每个客户端版本重新定位偏移
- 收益：直接改客户端 DOM/CSS，做真正的「客户端内 UI」，而不是浮一层窗口

**阶段 4（成本最高，风险最高）**：游戏进程
- 注入 `League of Legends.exe`，hook `IDXGISwapChain::Present` 画对局内 HUD
- 数据源仍可用 `https://127.0.0.1:2999/liveclientdata/*`
- 风险：ACE 对游戏进程保护最硬，本机屏幕共享/虚拟键鼠已触发过闪退

## 7. 实测记录（2026-11-24）

| 时间 | 事件 | 结果 |
|---|---|---|
| 11:12 | WeGame 点击启动 | `RiotClientServices → LeagueClient → LeagueClientUx → 6×Render` 全部起来 |
| 11:19 | LCU 探针 | REST 200 / WAMP 101 + 事件 / 用 REST 建出极地大乱斗房间 |
| 11:25 | `CreateRemoteThread` + `WinExec` 注入 renderer | 写出文件；`cmd.exe` 父进程 = `LeagueClientUxRender.exe` |
| 11:26 | `LoadLibraryW(msimg32.dll)` | 目标模块表新增该 DLL |
| 11:36 | Zig 工具链就绪 | `zig cc` 产出 225 KB x64 DLL |
| 11:36 | 第一个 HUD 版本 | 客户端窗口内画出面板（截图 `shots/hud-v4.png`） |
| 11:41 | 客户端 Ux 自行重启（pid 全换） | 注入的 DLL 随之消失，验证了「必须靠 watcher 补注入」 |
| 11:45 | 杀掉客户端并由 `wegame_launch.py` 重启 | 新 LCU 端口 61038，watcher 自动注入新 renderer（pid 4428），HUD 恢复（截图 `shots/hud-after-relaunch.png`） |
| 全程 | ACE | 期间 `ACE-Helper.exe` 出现过又退出，注入未触发踢线/闪退 |

截图清单：`shots/hud-v4.png`、`shots/hud-final.png`、`shots/hud-after-relaunch.png`、
`shots/client-lobby-aram.png`（纯 REST 造房间）、`shots/lcu-msgbox-proof.png`（注入证明阶段）。
