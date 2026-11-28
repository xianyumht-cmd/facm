# GGman 全功能内嵌 + 客户端瘦身方案

日期：2026-11-24
背景：现有 `ggman_core.dll` 注入 `LeagueClientUxRender.exe` 后能做到「进程内 agent + 浮层面板 + 可点按钮」。
本文回答三件事：**现在是什么注入方式**、**能否把 GGman 全部功能搬进客户端面板**、**能不能做客户端广告拦截/瘦身**。

---

## 1. 现在的注入方式（以及为什么你还能看到一个"外部窗口"）

### 1.1 注入链

```
Python (tools/lol_inject_poc.py, ctypes)
  OpenProcess(PROCESS_ALL_ACCESS, LeagueClientUxRender.exe)
  VirtualAllocEx(RWX) → WriteProcessMemory(DLL 路径)
  CreateRemoteThread(LoadLibraryW)            ← 经典远程线程注入
  └─ ggman_core.dll 的 DllMain → CreateThread(init_thread)
```

- 目标进程：`LeagueClientUxRender.exe`（CEF 108 渲染进程，带 `--no-sandbox`，本身就是客户端自己的插件宿主）
- 注入的是**我们自己编译的 x64 DLL**（Zig + mingw-w64，无 VS）
- 每次注入用唯一文件名（Windows 不允许同路径模块重复加载）

### 1.2 面板窗口是什么

DLL 在**目标进程内部**创建两个窗口：

| 窗口 | 类名 | 样式 | 作用 |
|---|---|---|---|
| IPC | `GGmanHudIPC` | 隐藏顶层窗口 | 接收外部 `WM_COPYDATA`（推文本/开关/会话覆盖） |
| 面板 | `GGmanHudOverlay` | `WS_POPUP` + `WS_EX_LAYERED｜NOACTIVATE｜TOOLWINDOW｜TOPMOST`，colorkey 透明 | 绘制 HUD、接收按钮点击 |

**它不是客户端窗口的子窗口**，原因很实际：

- 客户端的窗口树（`RCLIENT` 主窗口、CEF 子窗口）属于 **LeagueClientUx.exe**，不在渲染进程里；
- Windows 不允许跨进程 `SetParent`/创建 `WS_CHILD`（Win8+ 对跨进程子类化也有限制）；
- 所以只能做成**顶层浮层**，用 300 ms 定时器跟随客户端客户区、并在客户区内做边界裁剪（v13 已加：面板不会越出客户区）。

**结论**：这套东西是"**进程内 agent + 进程级 overlay**"。
比外部 overlay 强的地方：同进程能直接调 LCU、能 hook 自己的消息、随 renderer 生命周期；
仍然是"浮在客户端上的一层"，不是客户端页面的一部分 —— 这就是你在截图里看到的那个"外部窗口"。

### 1.3 要让 UI 真正在客户端"里面"，只有两条路

| 方式 | 做法 | 代价 |
|---|---|---|
| **CEF 内部 JS/DOM** | hook `libcef.dll`（或注入 renderer 后拿到 CEF 的 JS 执行入口），往页面 `Page.addScriptToEvaluateOnNewDocument` 等价地注入 JS/CSS | 需要逆向偏移，客户端大版本要维护；但 UI 完全复用客户端 CEF，内存开销最小 |
| **改客户端资源（WAD）** | `LeagueClient/Plugins/*/assets.wad` 是 Riot WAD（路径哈希 + 压缩块），替换其中的 JS/CSS/图片 | 需要写 WAD 读写器；版本升级即失效 |

---

## 2. 全功能迁移：能，但要分三层承载

GGman 的功能本质只有两类：**（a）LCU 数据/动作**、**（b）桌面侧东西（托盘/悬浮球/文件清理/自更新）**。
(a) 全部可以搬进注入进程 —— 因为注入进程本来就在跟 LCU 说话；
(b) 天生不属于客户端，留在外部更合理（也不该塞进 renderer）。

### 2.1 三层 UI 承载

| 层 | 承载方式 | 适合 | 成本 | 资源占用 |
|---|---|---|---|---|
| **L1** | 现有 GDI 自绘面板 + 按钮 | HUD、快捷操作、关键状态（≤20 个交互） | 已完成 | 极低（一个定时器 + 小位图） |
| **L2** | 注入进程内起 WebView2，跑完整 HTML UI | 战绩页、玩家查询、攻略页等"整页"内容 | 中（WebView2Loader + Runtime，进程内 COM/线程要验证） | +80~150 MB |
| **L3** | CEF 内部 JS 注入，把 UI 放进客户端页面 | 真·内嵌（广告拦截也靠这条） | 高（hook libcef，按版本维护） | 最低（复用客户端 CEF） |

推荐组合：**L1 承载高频操作 + L3 承载完整页面/广告拦截，L2 只在 L3 未打通前做过渡。**

### 2.2 模块迁移清单（按 GGman 现有模块）

| GGman 模块 | 内容 | 可迁入注入进程 | 建议承载 |
|---|---|---|---|
| `LeagueEfficiencyModule`(72KB) | 自动接受/自动找房/热键 | ✅ 已做（accept 部分） | L1 |
| `LeagueDashboardModule` | 阶段/战绩概览 | ✅ | L1/L3 |
| `LeaguePlayerModule` / `LeaguePersonalStatsModule` | 玩家查询、个人统计 | ✅（LCU `/lol-summoner`、`/lol-ranked`、match-history） | L2/L3 |
| `LeagueLiveModule` | 实时对局数据 | ✅（`2999` liveclientdata） | L1（对局内 HUD 需要游戏进程 overlay） |
| `LeagueBuildAdvisorModule` | 出装/符文建议 | ✅（OP.GG/ARAMGG HTTP） | L2/L3 |
| `MayhemModule` | 海克斯大乱斗攻略 | ✅（HTTP + 图片缓存） | L2/L3 |
| `LeagueHubModule` | 九个页面的路由 | ✅（业务在 C#，UI 需重画） | L2/L3 |
| `LeagueGameRepairModule` / `CleanupModule` | 窗口修复、日志/残留清理 | ⚠️ 需要文件系统与提权 | 留外部（或面板按钮触发外部 helper） |
| `SettingsModule` / `GgmanAutoSyncModule` / `EscSettings` | 配置同步、ESC 设置 | ⚠️ 读写 `Game/Config` 与云端 | 留外部或做 IPC 代理 |
| `PetsModule` / `ShellModule` / `CompactMenuEnhancer` | 桌宠、托盘、悬浮球 | ❌ 桌面级 | 留外部 |
| `OnlineModule` / `CloudSyncModule` | 账号、云同步 | ✅ 技术上可在 DLL 里（WinHTTP+TLS） | 留外部（隔离风险） |
| `UsageTelemetryModule` | 使用统计 | 建议直接砍掉 | — |

**一句话**：客户端相关的 18 个模块里，约 12 个可以 100% 进注入进程，4 个需要文件/提权（可做 IPC 代理），3 个是桌面级（本就该在外面）。

---

## 3. 客户端广告拦截 / 瘦身

客户端"臃肿"的来源不是业务逻辑，而是 **CEF 页面**：主页推荐位、活动中心、商城 banner、通行证弹窗、云顶新客户端提示，都是远程 web 内容（插件 `rcp-fe-lol-activity-center`、`-store`、`-pass` 等拉取）。

### 3.1 四条路，按低配机收益排序

| 路线 | 做法 | 收益 | 风险 | 现在能否做 |
|---|---|---|---|---|
| **A. 落地页替换（配置层）** | `LeagueClient/system.yaml` 的 `custom_landing_url` 指向本地极简页（或 `about:blank`） | 主页不再加载 QQ 营销页，**低配机收益最大** | 客户端更新可能覆盖；需重启客户端 | ✅ 马上可测 |
| **B. DOM/CSS 注入（CEF JS）** | 注入 JS：`display:none` 掉推荐位/活动中心/商城 banner；`MutationObserver` 处理动态插入；hook `window.fetch`/`XMLHttpRequest` 把营销接口返回空 | 高，且能只保留想看的模块 | 需 L3 能力（hook libcef） | ⏳ 需先打通 L3 |
| **C. 网络层拦截** | 在 network service 进程 hook 请求，直接丢掉营销域名 | 高 | 改动面大，易连带页面报错 | ⏳ |
| **D. 资源层（WAD）** | 替换/删除活动资源包 | 中 | 版本敏感、需要 WAD 工具 | ⏳ |

**建议顺序：A（今天就能验证）→ B（打通 L3 后）→ C/D 视情况。**

### 3.2 A 路线的具体验证步骤（不需要新代码）

```powershell
# 1) 备份
copy "D:\WeGameApps\英雄联盟\LeagueClient\system.yaml" "$env:TEMP\system.yaml.bak"
# 2) 改 custom_landing_url 指向极简页（本地文件或 data: URL）
#    custom_landing_url: file:///D:/project2/ggman/assets/blank.html
# 3) 完全退出客户端后经 WeGame 重启，观察：主页是否不再加载营销内容、CPU/内存是否下降
```

需要一个极简 `blank.html`（只放一个 GGman 字样 + 深色底）。若客户端拒绝 `file://`，退回 `data:text/html,...` 或本地 HTTP（我们有 mock 服务器的现成代码可复用）。

### 3.3 低配机的其它减负（与广告无关但有效）

1. **事件驱动替代轮询**：现在 agent 300 ms 轮询 phase/session，可换成 WAMP `[5,"OnJsonApiEvent"]` 订阅（LCU 自带 WebSocket），CPU 从"每秒几次请求"降到"有变化才动"。
2. **面板重绘节流**：现在每次 timer 都 `InvalidateRect`，可改成"内容变才重绘"。
3. **客户端自身设置**：`/lol-settings` 里有动画/特效类开关（逐项探测确认后再动）。
4. **少加载插件**（不推荐）：改 `plugin-manifest.json` 停用 `rcp-fe-lol-store` 等，风险高于收益。

---

## 4. 下一步建议（我按这个顺序做）

1. **A 路线实测**：极简落地页 + 重启对比 CPU/内存/首屏时间（低配机最直接收益）。
2. **事件驱动改造**：agent 从轮询改 WAMP 订阅（顺带把面板重绘节流）。
3. **L3 打通**：hook `libcef.dll` → 注入 JS → 先做"隐藏推荐位/活动中心"，再做完整内嵌 UI。
4. **L2 过渡（可选）**：若 L3 卡住，用 WebView2 承载完整页面。
5. 桌面级功能（托盘/悬浮球/清理/更新）继续留在 GGman 外部，用 IPC 与注入 agent 通信。
