# Batch C 交付记录：双排识别 + 踩坑名单

日期：2026-11-25
代码：`dll/ggman_core.c`（C / Zig cc）
验证：mock LCU（`cs_full10.json`，其中 enemy0/enemy1 共享 6 场历史）
持久化：`dll/build/known_players.txt`（TSV）

---

## 1. 双排识别

原理：Batch A 抓情报时已把每人近 20 场的 `gameId` 存在内存里（`IntelEntry.game_ids[20]`）。
情报抓完后 `duo_eval()` 做**同队两两交集**，共享 ≥2 场就标记：

```
敌 双排6 top     Allyemy0#1234   GOLD II 45  30%(20) 2连胜 103/86/64
敌 双排6 jungle  Allyemy1#1234   GOLD II 45  30%(20) 3连胜 86/64/222
```

面板在队伍标记后追加 `双排N`（N = 共享场次）。

### 踩到的关键 bug：64 位 gameId

第一版用 `long` 存 `gameId`，Windows 上 `long` 是 32 位，而 LoL 的 gameId ≈ 7.9e9，
`atol` 截断后不同对局变成同一个数 → **7 个玩家全被判成双排**（日志 `duo-marked=7`）。
改成 `long long` + `strtoll` 后正确（`duo-marked=2`，正好是 fixture 里共享 6 场的那一对）。

## 2. 踩坑名单（本地持久化）

```
dll/build/known_players.txt
# puuid	name	note	count
mock-puuid-enemy0	Allyemy0#1234	手动标记	1
```

- 启动时 `known_load()` 读入（最多 200 条），离开时/变更时 `known_save()` 写回
- 情报抓完自动比对：命中就标 `⚠`，并把 `count` +1（遇到次数）
- 手动标记入口：工具页第 6 行 **「标记对位为踩坑」**——优先标"敌方与我同位置"的那个人，否则标第一个敌人
- 面板效果：

```
敌 ⚠ 双排6 top  Allyemy0#1234  GOLD II 45  30%(20) 2连胜 103/86/64
```

`⚠` = 名单命中，`双排6` = 双排共享 6 场，颜色仍是"对位高亮"的琥珀色。

## 3. 实测证据

```
# 抓完
intel: done (7 players), duo-marked=2, known=0
# 点「标记对位为踩坑」
action: 07:13:31 已标记 Allyemy0#1234 为踩坑
known list saved: 1 entries
# 文件
# puuid                 name            note      count
mock-puuid-enemy0       Allyemy0#1234   手动标记   1
```

面板截图：`shots/intel-duo-known.png`（同时体现 ⚠ + 双排6 + 对位高亮）。

## 4. 限制与后续

| 项 | 说明 |
|---|---|
| 双排判定阈值 | 共享 ≥2 场；匹配池大时可能误判（同一场匹配里偶遇两次以上才算） |
| 名单容量 | 200 条（超出后不再新增，避免文件无限增长） |
| 自动标记 | 目前只有手动；后续可按"战绩异常低 + 队友举报"自动加名单（需要更多数据） |
| 名单同步 | 纯本地文件，不联网；要跨机器可后续接 CloudBase |

## 5. 下一步（同一目标）

- **④ ARAM bench 一键换英雄**：`/lol-champ-select/v1/session/bench/swap/{championId}`
- **⑤ 批量静音 / 邀请最近队友**（端点不通则记录跳过）
- **⑥ Cross 组件瘦身**
