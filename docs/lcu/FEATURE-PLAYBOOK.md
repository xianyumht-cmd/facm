# 玩家功能开发手册（按动线，精确到请求体）

本文件只收录**玩家实际用得到**的能力，每一条都给出：端点、精确请求体、前置条件、源码出处。
所有请求体都是从客户端自己的 JS bundle 里读出来的（`tools/re_wad.py` 解包 `assets.wad`），
不是猜的。标记「实测」的项在真实客户端上跑通过。

---

## 一、选人阶段

### 1.1 一键静音全场 —— 实测可用

```http
POST /lol-champ-select/v1/toggle-player-muted
Content-Type: application/json

{"summonerId":<int>,"puuid":"<str>","obfuscatedSummonerId":<int>,"obfuscatedPuuid":"<str>"}
```

- 四个字段全部来自 `session.myTeam[]` / `session.theirTeam[]`，**不是 cellId**；
- 路径只在旧前缀存在，team-builder 前缀返回 404；
- 源码：`rcp-fe-lol-champ-select` 的 player 组件，`actions` 里直接 `ajax POST`；
- 官方等价操作：右键头像 → 屏蔽，一次只能一个。

### 1.2 精确选择 / 锁定英雄

```http
PATCH /lol-champ-select/v1/session/actions/{actionId}
Content-Type: application/json

{"completed":true,"championId":123}
```

- **一次请求同时完成"选"和"锁"**，不需要再 POST `.../complete`（我最初的实现多打了一次）；
- `actionId` 来自 `session.actions[]` 里 `isInProgress=true` 且 `actorCellId` 等于自己的那一项；
- 源码：`selectChampion` / 锁定按钮的 handler。

### 1.3 ARAM 重掷

```http
POST /lol-champ-select/v1/session/my-selection/reroll
```

- 无请求体；
- 前置：`session.rerollsRemaining > 0`，且 UI 侧还要满足 `!rerollsDisabled`；
- 源码：`rerollInProgress` 分支。

### 1.4 bench 换英雄（ARAM 换人）

```http
POST /lol-champ-select/v1/session/bench/swap/{championId}
```

- 无请求体，championId 在路径里；
- 路径存在两套前缀，team-builder 版本在 PvP 生效；
- 源码：`championClicked` handler，带 `pendingRequest` 与冷却去重。

### 1.5 换英雄 / 换位 / 交换选人顺序

```js
// 三种 swap 各自有独立路径与服务方法
SWAP_SESSION_SERVICE_CALL_PATHS = {
  CHAMPION:   "/lol-champ-select/v1/session/champion-swaps",
  PICK_ORDER: "/lol-champ-select/v1/session/pick-order-swaps",
  POSITION:   "/lol-champ-select/v1/session/position-swaps"
}
SWAP_SERVICE_CALL_METHODS = { CANCEL:"cancel", ACCEPT:"accept", DECLINE:"decline", CLEAR:"clear" }
```

即 `POST {path}/{swapId}/{method}`，另有 `GET /lol-champ-select/v1/ongoing-*-swap` 查询进行中的交换。

### 1.6 退出选人（重点，已彻底证伪"有其他通道"）

```http
POST /lol-lobby-team-builder/champ-select/v1/session/quit     // 常规
POST /lol-lobby/v1/lobby/custom/cancel-champ-select            // 观战 + isLegacyChampSelect
```

- 客户端源码就是这两条，没有第三条；
- 显示与否只看 `session.showQuitButton`；
- **PvP 下服务端返回 400 `Current champ select does not allow quitting`**；
- 训练/自定义 `showQuitButton=true`，`204` → 1.2 秒回 Lobby，零惩罚（实测）。

### 1.7 购买团队 BUFF

```
GET   /lol-champ-select/v1/team-boost
POST  /lol-champ-select/v1/team-boost/purchase
```

源码里作为 DataBinding 订阅 + 购买动作，需要 RP 余额与 `allowBattleBoost`。

---

## 二、大厅与组队

### 2.1 建房 / 换队列

```http
POST /lol-lobby/v2/lobby
{"queueId":430}
```

- **改队列必须用 POST 重建，`PUT` 返回 405**（实测）；
- 常用队列：420 单双排 / 440 灵活 / 430 匹配盲选 / 400 匹配征召 / 450 ARAM；
- 排位（420/440）在未定级账号上会被 `GATEKEEPER_RESTRICTED` 拒绝。

### 2.2 退出房间

```http
DELETE /lol-lobby/v2/lobby          // 见 rcp-fe-lol-parties
DELETE /lol-lobby/v2/lobby/matchmaking/search   // 只取消匹配
```

### 2.3 接受收到的邀请

```http
POST /lol-lobby/v2/received-invitations/{invitationId}/accept
```

### 2.4 加入他人队伍

```http
POST /lol-lobby/v2/party/{partyId}/join
```

### 2.5 再来一局

```http
POST /lol-lobby/v2/play-again
```

---

## 三、社交

### 3.1 好友请求

```
POST   /lol-chat/v2/friend-requests/           // 发起
PUT    /lol-chat/v2/friend-requests/           // 处理（接受）
DELETE /lol-chat/v2/friend-requests/           // 取消/拒绝
GET    /lol-chat/v2/friend-requests            // 列表（实测 200，含 puuid）
```

### 3.2 好友分组

```
POST   /lol-chat/v1/friend-groups
PUT    /lol-chat/v1/friends/{id}               // 改备注/分组归属
DELETE /lol-chat/v1/friends/{id}
GET    /lol-chat/v1/friend-groups              // 实测 200（含"人机"等默认组）
```

### 3.3 屏蔽

```
POST   /lol-chat/v1/blocked-players            {"id":"<puuid>"}
DELETE /lol-chat/v1/blocked-players/{id}
GET    /lol-chat/v1/blocked-players            // 实测 200
```

### 3.4 会话与消息

```
POST /lol-chat/v1/conversations                          // 建群
POST /lol-chat/v1/conversations/{id}/messages            {"body":"...","type":"chat"}
PUT  /lol-chat/v1/conversations/active
```

`{id}` 对好友私聊就是对方 puuid。

### 3.5 在线状态

```http
PUT /lol-chat/v1/me
{"availability":"chat|dnd|offline|away|mobile","statusMessage":"..."}
```

- **merge 语义**：只传 statusMessage 不会丢 availability（实测 201）；
- FACM 已有 `LeaguePresenceWriteApiClient`，不要重复实现。

---

## 四、对局后

### 4.1 跳过结算动画（官方端点，之前没发现）

```http
POST /lol-pre-end-of-game/v1/skip-pre-end-of-game
```

### 4.2 点赞

```
POST /lol-honor-v2/v1/honor-player
POST /lol-honor-v2/v1/ballot/refresh
GET  /lol-honor-v2/v1/ballot                   // 实测 200，含 eligibleAllies/Opponents
```

### 4.3 关闭结算统计

```http
POST /lol-end-of-game/v1/state/dismiss-stats
```

### 4.4 举报

见 `rcp-fe-lol-social` 的 `/lol-player-report-sender/v1/...`（FACM 已有 player report 模块）。

---

## 五、商店 / 资产

### 5.1 批量购买（含精确请求体）

```http
POST /lol-shoppefront/v1/bulk-purchases
Content-Type: application/json

{
  "purchaseItems": [{
    "catalogEntryId": <itemId>,
    "paymentOptions": ["<paymentKey>"],
    "quantity": 1,
    "storeId": "<storeId>"
  }],
  "purchaseTimeOut": 10000
}
```

下单后轮询 `/lol-shoppefront/v1/purchases/{token}` 看结果。源码里就是这么写的（12 秒超时）。

### 5.2 战利品

```
GET  /lol-loot/v1/player-loot                  // 实测 200
POST /lol-loot/v1/refresh
POST /lol-loot/v1/player-loot-notifications/{id}/acknowledge
DELETE /lol-loot/v1/loot-grants/{id}
```

合成配方端点（`/lol-loot/v1/recipes/{recipe}/craft`）在本轮解包的插件里没出现，
需要再解 `rcp-fe-lol-loot` 的交互组件确认；不要凭猜实现。

### 5.3 奖励批量选择

```http
POST /lol-rewards/v1/select-bulk
```

---

## 六、观战

```http
POST /lol-spectator/v1/spectate/launch
```

---

## 七、反作弊自检（只读，但信息量大）

```http
POST /lol-vanguard/v1/telemetry/system-check
```

客户端自己会打这个端点上报系统检查结果。想了解"它检查了什么"，可以在 Ux 里下断点看请求体。

---

## 八、实现纪律（每条都踩过）

| 教训 | 规则 |
|---|---|
| 我按 `{cellId}` 实现静音，源码是四元组 | **请求体必须从 bundle 里读**，不能按字段名猜 |
| 我 PATCH + POST complete 两步锁定 | 源码一步 `PATCH {completed, championId}` 就够 |
| 全量前缀重写把静音打成 404 | 只重写确认存在于目标前缀的子路径 |
| `request-lobby` 回 `200 false` 被我当成成功 | 写操作要校验 body 与 phase |
| `PUT /lol-lobby/v2/lobby` 405 才发现要 POST | 方法也要从源码确认 |
| 以为 `skip-pre-end-of-game` 需要注入 UI | 官方有端点，先查 bundle |

## 九、还值得解包的插件（按玩家价值排序）

| 插件 | 价值 | 未确认的能力 |
|---|---|---|
| `rcp-fe-lol-loot` | 高 | 战利品合成 / 分解 / 批量开启的精确请求体 |
| `rcp-fe-lol-parties` | 高 | 踢人、转让队长、房间设置的精确路径 |
| `rcp-fe-lol-postgame` | 中 | 举报表单字段、评分数据来源 |
| `rcp-fe-lol-store` | 中 | 皮肤详情、折扣、礼物 |
| `rcp-fe-lol-kiwi-hub` | 中 | 海斗（KIWI）模式专属数据 |
| `rcp-fe-lol-missions` | 中 | 任务领取（当前未解） |
