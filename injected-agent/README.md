# injected-agent — 注入式客户端增强（GGman Agent）

把原生 x64 DLL 注入 `LeagueClientUxRender.exe`（CEF 渲染进程），在客户端内部提供：
本局情报、选人自动化、条件秒退、换英雄处理、ARAM bench 换人、赛后操作、
以及一个只监听回环的本地控制台。与 `src/` 的 .NET 客户端（FACM 原版）并存，互不依赖。

## 目录

```
injected-agent/
├── dll/
│   ├── ggman_core.c        # 注入主体：LCU 客户端、面板、情报、自动化、HTTP 控制台（C99，约 3.5k 行）
│   ├── ggman_hud.c         # 早期纯 HUD 版本（保留参考）
│   ├── panel_block.c       # 面板绘制的独立片段
│   ├── build_hud.ps1       # 构建脚本
│   ├── agent.ini.example   # 配置模板（复制为 build/agent.ini）
│   └── tests/              # 选人/符文/精通 fixture（喂给单测与假 LCU）
├── tools/                  # 注入、守护、假 LCU、探测、瘦身等 Python 工具
├── assets/                 # 本地控制台页面（console.html / console.css）
└── docs/                   # 交付记录：情报面板、秒退、双排名单、UI 改造、事故复盘
```

## 构建

```powershell
# 需要 zig（交叉编译，产出原生 x64 PE，无 .NET 依赖）
zig cc -target x86_64-windows-gnu -shared -O2 -Wall `
  -o dll/build/ggman_core.dll dll/ggman_core.c `
  -luser32 -lgdi32 -lwinhttp -lws2_32
```

离线单测（不注入、不连真实客户端）：

```powershell
zig cc -target x86_64-windows-gnu -DGG_TEST -O2 -o dll/build/test_cs.exe dll/ggman_core.c -lwinhttp -luser32 -lgdi32
dll/build/test_cs.exe dll/tests/cs_my_pick.json 3 22 99
```

## 运行

1. 客户端由 WeGame 正常启动（不要直接跑 `LeagueClient.exe`）
2. `python tools/inject_watch.py`（常驻，渲染进程重启自动补注入）
3. 面板默认是贴客户端左下角的状态条；点条子展开完整面板
4. 本地控制台：`http://127.0.0.1:27888/`（`/api/state`、`/api/action?id=N`）

配置在 `dll/build/agent.ini`，热加载（10s）。所有自动化默认关闭。

## 验证状态

| 项 | 方式 | 结果 |
|---|---|---|
| 注入 / 守护 / 面板 / 按钮 | 真实客户端 | 已验证（LCU 返回 404/204） |
| 情报抓取、双排识别、踩坑名单 | 假 LCU（mock） | 已验证 |
| 按位置选人、ban 轮换、延迟锁定、符文/技能映射 | 假 LCU | 已验证 |
| 条件秒退、换英雄请求、bench 换人、邀请 | 假 LCU | 已验证 |
| 一键静音 | 假 LCU 200 | **真实端点未确认**（失败时提示"端点不可用"） |
| Cross 组件瘦身 | 真实安装目录 | 已验证（改名后已还原） |

真机仍需确认：陌生玩家战绩权限、符文/技能写入是否生效、秒退惩罚、静音端点。

## 已知取舍

- 只用 Win32 + LCU REST，不 hook 客户端逻辑，不碰游戏进程（ACE 风险面最小）
- 面板是独立 topmost 窗口，与客户端窗口锚定跟随；空白区域点击穿透给客户端
- 请求节流 700ms/轮，WinHTTP 超时 1.5s；循环停滞时面板自动隐藏并整体穿透
