# Batch 1 交付记录（选人阶段做深）

日期：2026-11-25
代码：`dll/ggman_core.c`（C，Zig cc 编译）
验证方式：**离线 mock LCU**（把 DLL 注入 `notepad.exe` 当宿主，假 LCU 在 127.0.0.1:18081），不消耗真实账号

---

## 1. 新增能力

| 能力 | 配置键 | 行为 |
|---|---|---|
| 按位置选人 | `pick_by_pos=1` + `pos_top/jungle/middle/bottom/utility` | 读 `myTeam[me].assignedPosition`，取对应英雄；该路为 0 时退回 `pick_champ` |
| ban 优先列表 | `ban_list=157,99,238` | 每次 ban 轮换取下一个，跨会话归零；列表为空时退回 `ban_champ` |
| 延迟锁定 | `lockin_delay_s=3` | PATCH 英雄后等 N 秒再 `POST .../complete`；0 = 立即锁 |
| 按英雄符文页 | `rune_map=103:页面名,22:页面名` | 我的英雄命中映射就用对应页，否则用 `rune_page` |
| 按英雄召唤师技能 | `spell_map=103:4/14,22:3/4` | 同理，覆盖全局 `spell1/spell2` |
| 动作去重 | 内部 ring buffer（8 个 action id） | 同一 action 只处理一次，静态 mock 也不会重复触发 |

另外补了**离线开发模式**：`agent.ini` 里 `mock_http/mock_port/mock_token` 一旦设置，`lcu_discover()` 直接采用它们，
于是 DLL 可以注入到任意 x64 进程（例如 notepad）对着假 LCU 跑完整状态机 —— 客户端没装也能开发。

## 2. 实测证据（mock 请求序列）

### 2.1 按位置选人（我 assignedPosition=middle，配置 pos_middle=103）

```
GET   /lol-champ-select/v1/session
PATCH /lol-champ-select/v1/session/actions/12   body={"championId":103}
... 3 秒后（lockin_delay_s=3）
POST  /lol-champ-select/v1/session/actions/12/complete
日志：action: 06:35:05 延迟锁定 #12 -> HTTP 200
```

### 2.2 按英雄符文/技能（我的 championId=103，rune_map=103:Conqueror ADC，spell_map=103:11/12）

```
GET   /lol-perks/v1/pages
PUT   /lol-perks/v1/currentpage                    body=1102        ← Conqueror ADC
PATCH /lol-champ-select/v1/session/my-selection     body={"spell1Id":11,"spell2Id":12}
日志：action: 06:35:34 符文页 1102(103) 已应用 (HTTP 200)
      action: 06:35:34 召唤师技能 11/12 已设置 (HTTP 200)
```

### 2.3 ban 列表轮换（同一会话两个 ban 回合，ban_list=157,99,238）

```
PATCH /lol-champ-select/v1/session/actions/21   body={"championId":157}
POST  /lol-champ-select/v1/session/actions/21/complete
PATCH /lol-champ-select/v1/session/actions/31   body={"championId":99}
POST  /lol-champ-select/v1/session/actions/31/complete
```

只发了两次 PATCH、两次 complete —— 去重生效，轮换正确。

### 2.4 回归

```
test_cs.exe cs_my_pick.json 3 22 99   → action_id=12 type=pick champion=22
test_cs.exe cs_other_turn.json 3 22 99 → decisions=0
```

## 3. 踩掉的坑

| 问题 | 原因 | 修复 |
|---|---|---|
| 两个 ban 回合都 ban 了同一个英雄 | `ban_champ` 在调用 `cs_decide` 前算好，返回的多个 decision 共用一个值 | `cs_decide` 只返回"哪个 action、是不是 ban"，英雄由调用方逐条分配并递增 `ban_next` |
| 静态 mock 下同一 action 被反复处理 | 用单个 `g_cs_last_action` 判重 | 改成 8 项 ring buffer（`was_handled`/`mark_handled`），离开选人归零 |
| mock 模式下 `lcu_http` 直接返回 -1 | `lcu_discover()` 需要真实端口 | `mock_port>0` 时直接采用 mock 会话 |

## 4. 当前状态

- `agent.ini`：配置齐了但 **auto_accept / auto_pick / auto_ban / auto_runes / auto_spells 全部为 0**，等你确认后再开
- 客户端已装但未启动；启动后 `python tools\inject_watch.py --once` 即可
- 面板"自动化"页的 7 个开关仍然有效，会写回同一份 ini

## 5. 下一步（batch 1 剩余 + batch 2）

1. **选人期战绩面板**：队友/对手近 20 场、胜率、常玩英雄、双排识别（`/lol-match-history/v3/matchlist/account/{puuid}`，可滚动列表）
2. **工具页一键操作**：建 ARAM/自定义房、邀请最近队友、一键重连、跳过结算
3. **面板设置页**：英雄 ID 步进、符文页/技能选择、延迟滑杆
4. 性能：300 ms 轮询 → WAMP `OnJsonApiEvent` 订阅
