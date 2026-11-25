# GGman UI 改造：分区穿透 + 分阶段界面 + 本地控制台

日期：2026-11-25
代码：`dll/ggman_core.c`（≈275 KB DLL）、`assets/console.html`、`assets/console.css`、`tools/set_landing.py`
现场：注入 renderer pid 14284，watcher 常驻，控制台 `http://127.0.0.1:27888/`

---

## A. 面板不再挡大厅操作（分区穿透）

### 问题
之前为了修"按钮点不动"，把整个面板窗口的 `WM_NCHITTEST` 改成了恒返回 `HTCLIENT` → 面板覆盖区域内的**客户端点击全被吃掉**。

### 做法
1. 抽出 `compute_layout(w,h)`，把**关闭按钮 / 4 个标签 / 每个可点行**的矩形一次性算定 —— 绘制和命中测试共用同一份几何，不再依赖"上一次绘制"的时序（那正是最初按钮失灵的原因）。
2. `WM_NCHITTEST` 只在上述矩形内返回 `HTCLIENT`，其余返回 `HTTRANSPARENT` → 点击直接落到下面的客户端。

### 实测（向窗口发送 WM_NCHITTEST）

| 位置（面板内坐标） | 返回 | 含义 |
|---|---|---|
| (300, 300) 空白区 | `-1` HTTRANSPARENT | 点击穿透到客户端 |
| (300, 32) 标题空档 | `-1` | 穿透 |
| (300, 79) 第一行 | `1` HTCLIENT | 面板接 |
| (371, 48) 标签 | `1` | 面板接 |
| (580, 17) 关闭按钮 | `1` | 面板接 |

按钮回归：点「立即接受对局」→ `点击接受 -> HTTP 404`；点「结算再来一局」→ `HTTP 204`（真实 LCU）。

---

## B. 分阶段界面

配置：`auto_ui_by_phase=1`

| phase | 行为 |
|---|---|
| `ChampSelect` | 自动切到「情报」页（并在标题栏显示选人剩余秒数） |
| `EndOfGame` / `PreEndOfGame` | 自动切到「工具」页（点赞/再来一局就在眼前） |
| `Lobby` / `Matchmaking` / `ReadyCheck` | 回到「状态」页（显示排队时长） |
| `InProgress` / `GameStart` / `Reconnect` | 面板自动隐藏（对局中客户端本来就最小化） |
| `None` | 「状态」页 |

数据源：
- 排队时长 `/lol-matchmaking/v1/search` → `timeInQueue`
- 选人剩余 `/lol-champ-select/v1/session` → `timer.adjustedTimeLeftInPhase`
- 每 500 ms 刷新一次，只在变化时重绘

---

## C3. 本地控制台（真网页 UI）

agent 在 `127.0.0.1:27888` 起一个极简 HTTP（只监听回环，不联网）：

| 端点 | 说明 |
|---|---|
| `GET /` | 控制台页面（`assets/console.html`） |
| `GET /console.css` | 样式 |
| `GET /api/state` | 完整状态 JSON：阶段、召唤师、段位、排队/选人计时、10 人情报、踩坑数、上次动作、pid/port |
| `POST /api/action?id=N` | 直接触发工具页动作（0 接受 … 9 静音） |

实测：

```
GET  /api/state → 200
{"phase":"None","summoner":"360安全卫士#43899","level":11,"rank":"未定级",
 "queue_secs":-1,"cs_left":-1,"intel_total":0,"known_count":0,"pid":14284,"port":53465,"intel":[]}

POST /api/action?id=3 → {"ok":true}
日志：action: 08:42:31 配置已重载
```

页面（截图 `shots/console-screen.png` / 浏览器内实测）：深色卡片风，三块——
「本局情报」（10 人表格，敌方紫、对位琥珀、名单命中带 ⚠、双排带 tag）、
「快捷操作」（10 个按钮，点击后回显 ✓）、「状态」（上次动作/情报进度/名单条数/agent 信息）。
每 1.5 秒自动刷新。

### 把客户端落地页换成控制台（可选，可逆）

```powershell
python tools\set_landing.py            # 只读：显示当前 custom_landing_url
python tools\set_landing.py --apply    # 备份 system.yaml 后写入 http://127.0.0.1:27888/
python tools\set_landing.py --restore  # 还原
```

当前客户端值：`custom_landing_url: https://lol.qq.com/client/client_lcu.shtml`（未改动，**没有替你写**）。
改完需要重启客户端才生效。

---

## 已知事项

| 项 | 说明 |
|---|---|
| 面板尺寸 | 仍是固定 600×372，分阶段只切换内容/显隐；真正的紧凑卡片（320×120）留待下一步 |
| 落地页风险 | 换成 `127.0.0.1` 页面后，若 agent 没起来，主页会白屏 → 用 `--restore` 一条命令还原 |
| CEF 内嵌（C1） | 未做：需要 CDP 端口或 hook `libcef`，成本 1–2 天，按需再上 |
| 静音端点 | 仍为 best-effort（`/lol-chat/v1/conversations/{id}/mute`），真机若不支持会显示"端点不可用" |

## 验证清单

- [x] 面板空白区点击穿透（WM_NCHITTEST = -1）
- [x] 面板按钮/标签/关闭仍可点（= 1），动作真实触发（404/204 都是真实 LCU 返回）
- [x] 面板在切页后不再崩溃（早前 `labels[7]` 越界已修）
- [x] 控制台 `GET /`、`/api/state`、`POST /api/action` 全部 200
- [x] agent + watcher 常驻，renderer 重启自动补注入
