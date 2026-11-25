# Batch B 交付记录：条件秒退 + 换英雄请求自动处理

日期：2026-11-25
代码：`dll/ggman_core.c`（C / Zig cc）
验证：mock LCU（`cs_full10.json` 5v5 fixture），宿主 `notepad.exe`

---

## 1. 条件秒退

| 配置 | 含义 |
|---|---|
| `quit_mode=0` | 关闭（默认） |
| `quit_mode=1` | **只提示**：面板「情报」页顶部红字 `⚠ 建议秒退：原因` |
| `quit_mode=2` | 自动秒退 |
| `quit_enemy_wr=60` | 敌方近 20 场**聚合胜率** ≥ 60% 时触发（0 = 不看） |
| `quit_ally_loss=3` | 我方有人 ≥ 3 连败时触发（0 = 不看） |

判定用的是**已抓取的情报**（10 人段位/胜率/连败），所以只在情报抓完之后触发一次；
`POST /lol-champ-select/v1/session/quit`，失败回退 `/lol-gameflow/v1/session/dodge`。
每个选人会话只触发一次（`g_quit_sent`），离开 ChampSelect 复位。

面板「工具」页还加了一个手动按钮 **「秒退（退出当前选人）」**，不受策略限制。

### 实测

```
# quit_mode=2, quit_enemy_wr=20
POST /lol-champ-select/v1/session/quit  body={}   → HTTP 204
日志：action: 07:10:34 已秒退（敌方近 100 场胜率 34% ≥ 20%）-> HTTP 204

# quit_mode=1（只提示）
日志：action: 07:11:06 建议秒退：敌方近 100 场胜率 34% ≥ 20%
面板：⚠ 建议秒退：敌方近 100 场胜率 34% ≥ 20%      ← shots/intel-dodge-advice.png
```

## 2. 换英雄请求自动处理

| 配置 | 含义 |
|---|---|
| `swap_policy=0` | 关闭（默认） |
| `swap_policy=1` | 自动**接受**所有换英雄请求 |
| `swap_policy=2` | 自动**拒绝**所有换英雄请求 |

轮询 `GET /lol-champ-select/v1/session/champion-swaps`（1 秒一次，仅在 ChampSelect），
对未处理的请求 `POST .../champion-swaps/{id}/accept|decline`。

### 实测

```
GET  /lol-champ-select/v1/session/champion-swaps
POST /lol-champ-select/v1/session/champion-swaps/73/accept body={}  → HTTP 200
（每个 swap id 只发一次；后续轮询只剩 GET）
```

第一次实现时每秒都重复 accept（mock 永远返回同一个未处理请求），加了 8 项 handled-id
环形缓冲后，同一实例只接受一次。

## 3. 面板变化

- 「情报」页顶部：触发条件满足时用琥珀色显示 `⚠ 建议秒退：…`，否则显示自己的段位与抓取进度。
- 「工具」页：从 5 行变 6 行，新增 `秒退（退出当前选人）`。

## 4. 安全默认

```ini
quit_mode=0        ; 不提示也不自动
quit_enemy_wr=0
quit_ally_loss=0
swap_policy=0      ; 不自动处理换英雄
```

要开只提示：`quit_mode=1` + 一个阈值；要自动秒退才设 `quit_mode=2`（有惩罚，自己确认）。

## 5. 下一步（同一目标）

- **③ 踩坑名单 + 双排识别**：用已抓的 20 个 `gameId` 做交集判断双排；本地 JSON 持久化 puuid 标记
- **④ ARAM bench 一键换英雄**
- **⑤ 批量静音 / 邀请最近队友**（端点不通就跳过）
- **⑥ Cross 组件瘦身**
