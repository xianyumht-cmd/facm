# LCU 大厅底层技术分析（按可控层级分档）

本目录是从本机国服客户端（`D:\WeGameApps\英雄联盟`，`LeagueClient 16.23 Tencent`）实测逆向得到的
完整能力面。所有结论都带实测证据；标注「推导」的部分只从函数名解析，未逐条验证。

数据来源：

- `GET /help`（245 KB）→ 1452 functions / 692 events / 3569 types
- 本目录：`endpoints-full.md`（151 KB）、`events-full.md`（87 KB）、`types-full.md`（124 KB）、`endpoints.csv`
- 实测脚本：`tools/lcu_dump.py`、`tools/lcu_quit_probe.py`、`tools/test_request_lobby.py`、`tools/frida_mem_scan.py`

---

## 0. 分档模型

按"这个能力被什么挡住"划分，而不是按功能划分。同一档内的实现手段、风险、验收方式一致。

| 档 | 名字 | 手段 | 风险 | 谁能做 |
|---|---|---|---|---|
| **T0** | 只读观测 | `GET` + WAMP `OnJsonApiEvent` 订阅 | 零 | 任何外部程序 |
| **T1** | 状态写入（可恢复） | `PUT/POST` 设置类端点 | 低 | 任何外部程序 |
| **T2** | 流程控制 | 排队/接受/选人动作/邀请 | 中（影响他人） | 任何外部程序 |
| **T3** | 破坏性动作 | 退出对局/取消选人 | 高（记惩罚） | 任何外部程序 |
| **T4** | 服务端门禁 | 端点存在但被服务端拒绝 | 无法绕过 | 无人能做（客户端侧） |
| **T5** | 注入层 | 进程内 DLL / CEF / DOM / 内存 | 中（可被反作弊注意） | 需注入 |
| **T6** | 游戏进程 | `League of Legends.exe` 内 overlay | 最高（ACE 驱动） | 需对抗驱动 |

关键判据：**T4 与 T3 的区别不在端点，在服务端**。同一个 `session/quit` 在训练模式是 T3（可用），
在 PvP 是 T4（400 拒绝）。所以任何功能设计都必须先确认它落在哪一档。

---

## 1. T0 只读观测（零风险，已全部实测可用）

| 端点 | 返回 | 官方 UI 是否展示 |
|---|---|---|
| `/lol-chat/v1/friends` | `lol.gameStatus`/`gameMode`/`gameQueueType`/`championId`/`mapId`/`level`/`pty`/`ptyType` | 只显示模糊状态 |
| `/lol-gameflow/v1/gameflow-phase` | 阶段字符串 | 部分 |
| `/lol-lobby-team-builder/champ-select/v1/session` | `showQuitButton`/`isCustomGame`/`queueId`/`timer`/`myTeam[]`/`theirTeam[]`/`actions[]` | 只显示一部分 |
| `/lol-champ-select/v1/muted-players` | 已静音 cellId 列表 | 不显示 |
| `/lol-leaver-buster/v1/notifications` | 裁决之镰剩余时间 | **不显示**（只有弹窗） |
| `/lol-matchmaking/v1/search` | `estimatedQueueTime`/`timeInQueue`/`lowPriorityData.penaltyTimeRemaining` | 只给模糊文案 |
| `/lol-end-of-game/v1/eog-stats-block` | 每人伤害/经济/补刀 | 只给基础表 |
| `/lol-loot/v1/player-loot`、`/lol-missions/v1/missions` | 战利品、任务状态 | 分散在多页 |
| `/lol-ranked/v1/current-ranked-stats`、`/lol-ranked/v1/current-lp-change-notification` | 段位、LP 变化 | LP 变化一闪而过 |
| `/riotclient/machine-id`、`/riotclient/system-info/v1/basic-info` | 设备标识、系统信息 | 不显示 |
| `/performance/v1/system-info` | CPU/GPU | 不显示 |

**结论：T0 已经被榨干**。玩家在日常动线里抱怨"看不到"的东西，几乎全在这一档。
实现成本最低，收益最直接。

---

## 2. T1 状态写入（可恢复，实测）

| 操作 | 端点 | 实测 | 注意 |
|---|---|---|---|
| 改签名/状态 | `PUT /lol-chat/v1/me` | **201** | merge 语义：只传 `statusMessage` 不会丢 `availability` |
| 屏蔽/取消屏蔽 | `POST`/`DELETE /lol-chat/v1/blocked-players` | 读 200 | body `{"id":"<puuid>"}` |
| 改皮肤偏好 | `PATCH .../session/my-selection` | 200（推导） | 选人期有效 |
| 修改设置 | `PUT /lol-settings/v*/...` | 未逐条验证 | 影响面大，需谨慎 |

---

## 3. T2 流程控制（有真实影响，FACM 已覆盖大部分）

| 操作 | 端点 | 状态 |
|---|---|---|
| 创建房间 | `POST /lol-lobby/v2/lobby` `{"queueId":430}` | 200（实测） |
| 开始/取消匹配 | `POST`/`DELETE /lol-lobby/v2/lobby/matchmaking/search` | 204 |
| 接受对局 | `POST /lol-matchmaking/v1/ready-check/accept` | 204 |
| 拒绝对局 | `POST .../ready-check/decline` | 记一次 dodge |
| 自动 pick/ban | `PATCH .../session/actions/{id}` + `POST .../complete` | 200 |
| bench 换人 | `POST .../session/bench/swap/{championId}` | 200 |
| 换英雄 | `POST .../session/champion-swaps/{id}/accept|decline` | 200 |
| 邀请 | `POST /lol-lobby/v2/lobby/invitations` | 200 |
| 点赞 | `POST /lol-honor-v2/v1/honor-player` | 200 |

**注意：`PUT /lol-lobby/v2/lobby` 返回 405**，改队列必须用 `POST` 重建。

---

## 4. T3 / T4 破坏性动作与服务端门禁（本项目的核心结论）

退出选人一共 7 条路径，全部实测：

| # | 路径 | 结果 | 档 |
|---|---|---|---|
| 1 | `POST /lol-lobby-team-builder/champ-select/v1/session/quit` | PvP **400** `does not allow quitting`；训练模式 **204 → 1.2 秒回 Lobby** | T3/T4 |
| 2 | `POST /lol-champ-select/v1/session/quit` | **404 Invalid URI format**（该版本无此路由） | 不存在 |
| 3 | `POST /lol-gameflow/v1/session/request-lobby` | PvP **200 `false`**；`phase=None` 时 `200 true` | T4 |
| 4 | `POST /lol-gameflow/v1/session/dodge` | **204**，phase 不动（请求被吞） | T4 |
| 5 | `POST /lol-gameflow/v1/early-exit` | **403** `Not in InProgress or Reconnect phase`（只对局中可用） | T4 |
| 6 | 断网 5/15 秒 | phase 不动；15 秒那次的变化实为选人超时 | 无效 |
| 7 | 结束 `LeagueClient.exe` | 整链退出 → 17–21 秒自动恢复 → `phase=None` | T5 |

**门禁来源已定位到字段**：`session.showQuitButton`，由服务端按队列下发。

- 训练/自定义：`true` → 按钮显示、quit 可用、零惩罚（`leaver=[]` 实测）
- PvP 430：`false` → 按钮不显示、quit 400、给 `request-lobby` 回 `false`

**插件源码级证据**（内存扫描得到，`quit-button-component`）：

```js
actions: {
  quitCustom: function () {
    const e = this.get("isLegacyChampSelect") && this.get("isSpectating")
      ? "/lol-lobby/v1/lobby/custom/cancel-champ-select"
      : "/lol-lobby-team-builder/champ-select/v1/session/quit";
    ajax({ type: "POST", url: e })
  }
}
showQuitButton: computed("session.showQuitButton", "isNotSpectating", "isDropInSpectator", function () {
  return this.get("session.showQuitButton") && (this.get("isNotSpectating") || this.get("isDropInSpectator"))
})
```

即：**客户端自己没有第二条路**，市面上"退出房间"按钮绕过的只是显隐，绕不过服务端。

---

## 5. T5 注入层（实测可行的部分）

| 能力 | 实测 | 备注 |
|---|---|---|
| 注入 `LeagueClientUxRender.exe` | 成功（`CreateRemoteThread` + `LoadLibraryW`） | ACE 未拦截（`ace-tips-render64.dll` 在进程内但不干预） |
| 注入 `LeagueClient.exe` | **失败** `VirtualAllocEx → 0x5` | 受保护 |
| Frida attach Ux / UxRender | 成功（8 个进程） | 原生上下文，无 `window` |
| WAMP CALL | **不支持** | 只能 SUBSCRIBE，四种 procedure 格式全超时 |
| 内存扫 JS 源码 | **成功** | 用特征词扫 `rw-` 段，可挖任意插件实现 |
| `kill-ux` + `launch-ux` | **能重启 Ux** | `kill-and-restart-ux` 是空操作 |
| 给 `LeagueClientUx` 加 `--remote-debugging-port` | 未成功 | 参数能进 `LeagueClient.exe`，但 Ux 用硬编码参数启动，且 Ux 由受保护进程创建，hook 不到 |

**JS 逆向的可行路径（已验证）**：

```
磁盘没有 .js（WAD 是 zstd 资源包、pak 是 CEF service worker）
  → Frida attach Ux，Memory.scanSync 找特征词（如 showQuitButton）
  → dump 命中处上下文 → 得到压缩后的 Ember 源码
```

Riot 构建期源码路径（可直接用于继续挖其它插件）：

```
T:\vfs\mount-Releases\16.23\DevRoot\Client\fe\rcp-fe-lol-champ-select\src\app\quit-button-component\index.js
```

---

## 6. T6 游戏进程

未做。已知约束：`League of Legends.exe` 受 ACE 驱动层保护，overlay 需 DXGI hook + 对抗。
本项目所有结论止于 `LeagueClient` 侧。

---

## 7. 可复用方法论

### 7.1 路由存在性判据（最重要）

LCU 的 404 有两种，含义完全不同：

| 响应 | 含义 |
|---|---|
| `{"errorCode":"RESOURCE_NOT_FOUND","message":"Invalid URI format"}` | **路由没注册** |
| `{"errorCode":"RPC_ERROR","message":"No champ select session in progress"}` 等 | **路由在，只是当前没有会话** |

判断一个端点是否可用，先看是哪种 404，而不是看 200/404。

### 7.2 前缀分裂

国服同时存在两套选人插件：

- `lol-champ-select` → `/lol-champ-select/v1/*`（自定义/训练）
- `lol-lobby-team-builder` → `/lol-lobby-team-builder/champ-select/v1/*`（**PvP**）

`GET /lol-lobby-team-builder/champ-select/v1/implementation-active` 是判据。

**但不是所有路由两边都有**：

| 路由 | 旧前缀 | team-builder |
|---|---|---|
| `session` / `session/timer` / `session/actions` | PvP 下无 delegate | ✅ |
| `toggle-player-muted` / `muted-players` | ✅ | **404** |

所以任何"按前缀重写"的逻辑必须收窄到具体子路径，否则会把静音打成 404。

### 7.3 200 不等于成功

`request-lobby` 与 `dodge` 都返回 2xx，但 body 或 phase 才是真实结果。写操作必须校验 body / 观察 phase。

### 7.5 WAD 解包 —— 这是拿到实现的第一入口（胜过内存扫描）

路由字符串**不在任何二进制里**（扫遍 `LeagueClient` 目录：`LeagueClientBase.dll` 0 条，
`libcef.dll` 0 条，只有两个 exe 各有 4 条边缘路由）。它们是插件层注册的，资源全在
`plugins/<plugin>/assets.wad`。

格式（实测）：

```
偏移 0:  52 57 03 04          "RW" + 版本 0x0304
之后:    若干 zstd 帧（28 B5 2F FD），帧之间用 0x00 对齐
内容:    HTML 模板 / JSON 本地化 / CSS / 二进制资源 / **一个完整 JS bundle**
```

每个插件里最大的那个 JS 帧就是完整实现。解出来之后：

- 路由是客户端自己声明的，比 `/help` 更贴近实现；
- 写调用带上下文，能看出"哪个按钮打哪个端点";
- 不再需要 Frida 扫内存（内存扫描只适合临时取片段）。

本项目解了 10 个关键插件，结果见 [PLUGIN-CAPABILITIES.md](PLUGIN-CAPABILITIES.md)：
**1569 条去重路由、277 处写调用**。

| 插件 | zstd 帧 | JS | 路由 | 写调用 |
|---|---|---|---|---|
| rcp-fe-lol-champ-select | 51 | 1.64 MB | 359 | 24 |
| rcp-fe-lol-navigation | 268 | 1.40 MB | 368 | 54 |
| rcp-fe-lol-parties | 66 | 1.67 MB | 323 | 59 |
| rcp-fe-lol-social | 94 | 1.62 MB | 155 | 80 |
| rcp-fe-lol-postgame | 92 | 0.71 MB | 198 | 27 |
| rcp-fe-lol-collections | 160 | 1.12 MB | 190 | 18 |
| rcp-fe-lol-loot | 77 | 0.83 MB | 90 | 7 |
| rcp-fe-lol-match-history | 39 | 0.32 MB | 21 | 4 |
| rcp-fe-lol-store | 44 | 2.67 MB | 43 | 3 |
| rcp-fe-lol-kiwi-hub | 29 | 0.08 MB | 34 | 1 |

工具：`tools/re_wad.py`（单包）、`tools/re_plugins.py`（批量）、`tools/re_summary.py`（汇总）。

### 7.6 静态交叉引用（用于二进制层）

`tools/re_xref.py` 在 PE 里定位字符串 VA，扫 `48 8D xx disp32` 形式的 RIP-relative `lea`，
命中后用 capstone 反汇编上下文。适用于"字符串确实在二进制里"的场景；
本项目验证过路由字符串不在二进制里，所以主战场是 7.5。

### 7.4 惩罚有三个出口，`notifications` 可能是空的

2026-11-28 实测：同一个账号、同一时刻，三个端点给出三种答案：

```
GET /lol-leaver-buster/v1/notifications        → []                    ← 空，但惩罚真实存在
GET /lol-leaver-buster/v1/ranked-restriction   → 无限制
GET /lol-matchmaking/v1/search                 → errors[].errorType = "QUEUE_DODGER"
                                                  penaltyTimeRemaining = 39233   ← 真正在拦排队
```

| 出口 | 反映什么 | 有惩罚时会不会为空 |
|---|---|---|
| `matchmaking/v1/search` → `errors[]` | **进行中的排队惩罚（QUEUE_DODGER）+ 剩余秒数** | 不会 |
| `leaver-buster/v1/notifications` | 已生成的通知 | **会，实测就是 `[]`** |
| `leaver-buster/v1/ranked-restriction` | 只反映排位禁赛，与 dodge 无关 | 会 |

**判断"现在能不能排队"只认 `matchmaking/v1/search` 的 `errors[]`。**
本项目早期只看 `ranked-restriction` 判"没惩罚"，连续重试后锁从 5 分钟涨到 30 分钟，最终累积到
10.9 小时——这就是把错误端点当判据的代价。

---

## 8. 工具清单

| 工具 | 用途 |
|---|---|
| `tools/lcu_dump.py` | 会话发现 + `/help` 导出 + 插件索引 + 日志端点提取 + WAMP 录制 |
| `tools/lcu_quit_probe.py` | 进选人自动试退出路径并记录 phase 前后 |
| `tools/test_request_lobby.py` | 排队→接受→进选人→只打 request-lobby 的对照实验 |
| `tools/frida_mem_scan.py` | 在 Ux 进程内存里扫特征词，dump 插件 JS |
| `tools/frida_net_hook.py` | 原生层 `ws2_32!send` 抓包（注意：LCU 是 TLS，只能看到密文） |
| `tools/frida_debug_port.py` | hook 启动链 `CreateProcessW` 注入 CEF 调试端口（部分成功） |
| `tools/dodge_cut.py` | 断网实验（已证伪，保留作为反例） |
| `tools/wegame_launch.py` | 通过 WeGame 窗口拉起客户端 |
| `tools/lcu_schema_export.py` | 本目录三个大表的生成脚本 |

---

## 9. 对实现的约束（给后续开发者）

1. **先判档再写代码**：T0/T1 用普通 HTTP 客户端即可；T3 必须确认是否落在 T4；
2. **不要按前缀整体重写**，只重写确认存在的子路径；
3. **写操作必须校验 body 与 phase**，不能只看状态码；
4. **惩罚检测只看 `matchmaking/v1/search` 的 `errors[]`**；`notifications` 有惩罚时也可能为空，`ranked-restriction` 只反映排位；
5. **要 JS 实现就扫内存**，磁盘上没有明文；
6. **WAMP 只能订阅**，需要"调用"就只能走 HTTP。
