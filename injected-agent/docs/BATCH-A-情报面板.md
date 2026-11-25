# Batch A 交付记录：选人期全场 10 人情报

日期：2026-11-25
代码：`dll/ggman_core.c`（C / Zig cc）
验证：mock LCU（7 人 fixture，真实客户端不参与），注入 `notepad.exe` 当宿主

---

## 1. 功能说明

面板第 3 个标签 **「情报」**，选人一开始自动抓取全场玩家数据：

| 列 | 内容 | 数据来源 |
|---|---|---|
| 我/敌 | 队伍（我方/敌方） | `session.myTeam` / `theirTeam` |
| 位置 | assignedPosition（top/jungle/middle/bottom/utility） | 同上 |
| 名字 | `gameName#gameTag` | `/lol-summoner/v2/summoners/puuid/{puuid}` |
| 段位 | `GOLD II 45` | `/lol-ranked/v1/ranked-stats/{puuid}` |
| 胜率 | 近 20 场 `40%(20)` | `/lol-match-history/v3/matchlist/account/{puuid}?begIndex=0&endIndex=19` |
| 连胜/连败 | `2连胜` / `3连败` | 同上（按时间倒序的首段连续结果） |
| 常用英雄 | `157/16/99`（出现最多的三个 championId） | 同上 |

**对位高亮**：敌方与我方同位置的玩家用黄色显示（我 top → 敌 top 变黄）。

## 2. 实现要点

- 抓取节奏：每个 300 ms tick 只处理**一名玩家**（3 个请求），10 人约 3 秒内完成，避免瞬时并发打满 LCU。
- 解析：不引 JSON 库，用 `count_kv_true`（数 `"win":true/false`）、`tally_champion`（英雄计数取前三）、
  顺序扫描算连胜/连败；`gameId` 顺手存下来，后面做双排识别要用。
- `json_str` 改成**容忍冒号后空格**（`"name": "x"` 与 `"name":"x"` 都能解析），顺带修好了召唤师名/段位一直为空的问题。
- 会话结束（离开 ChampSelect）自动 `intel_reset()`，下一局重新抓。
- **离线锚点**：没有客户端窗口时，面板/徽标改用桌面工作区定位（`anchor_rect`），
  这样在没有客户端的环境里也能开发/截图（`shots/intel-final.png` 就是这样拍的）。

## 3. 实测证据

mock 请求统计（7 人 fixture，一轮完整抓取）：

```
summoners/puuid : 13
ranked-stats    : 17
matchlist/account : 13
agent log: intel: 7 players in session / intel: done (7 players)
```

面板渲染（`shots/intel-final.png`）：

```
我的段位 未获取    抓取 7/10
我 top     Allyself#1234   GOLD II 45   40%(20) 2连胜 157/16/99
我 utility Allyally#1234   GOLD II 45   30%(20) 2连胜 157/16/99
敌 top     Allyemy0#1234   GOLD II 45   30%(20) 2连胜 103/86/64   ← 对位高亮
敌 jungle  Allyemy1#1234   GOLD II 45   30%(20) 3连胜 86/64/222
敌 middle  Allyemy2#1234   GOLD II 45   35%(20) 4连胜 64/222/157
敌 bottom  Allyemy3#1234   GOLD II 45   35%(20) 1连胜 222/157/16
敌 utility Allyemy4#1234   GOLD II 45   40%(20) 2连胜 157/16/99
```

## 4. 已知限制 / 待真机确认

| 项 | 说明 | 处理 |
|---|---|---|
| 陌生玩家战绩权限 | 国服可能对非好友限制 `matchlist/account/{puuid}`（403/空） | 已在代码里容错：失败只记日志、该行显示 `-`；真机验证后再决定降级策略 |
| `theirTeam` 是否带 puuid | 部分版本只给 summonerId | `intel_collect` 已兼容（无 puuid 时跳过，后续可用 summonerId 换 puuid） |
| 我的段位在 mock 里为空 | mock 没有 `/lol-ranked/v1/current-ranked-stats` | 真机走真实端点 |
| 10 人满编时请求量 | 10×3 = 30 个请求，分散在 ~3 秒 | 可改为事件驱动（WAMP）进一步降 |

## 5. 下一步（同一目标内）

- **② 条件秒退 + 换英雄请求自动处理**：`/lol-champ-select/v1/session/quit`、`/lol-gameflow/v1/session/dodge`、`champion-swaps/{id}/accept|decline`
- **③ 踩坑名单 + 双排识别**：用已经抓到的 `game_ids[20]` 做交集判断双排；本地持久化 puuid 标记
- **④ ARAM bench 一键换英雄**：`/lol-champ-select/v1/session/bench/swap/{championId}`
- **⑤ 批量静音 / 邀请**（端点不通就跳过）
- **⑥ Cross 瘦身**：检测/结束录制直播组件
