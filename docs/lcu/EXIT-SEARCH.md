# 退出选人 / 回大厅 相关的全插件搜索

扫描 43 个插件的 JS bundle（全库解包 48 个插件 / 24.9 MB，其中 43 个含 JS）。

## 最终结论

**客户端侧退出选人只有一条路径，由 `rcp-fe-lol-champ-select` 的 `quitCustom` 独占：**

```js
const e = this.get("isLegacyChampSelect") && this.get("isSpectating")
  ? "/lol-lobby/v1/lobby/custom/cancel-champ-select"
  : "/lol-lobby-team-builder/champ-select/v1/session/quit";
ajax({ type: "POST", url: e })
```

全库证据：

| 关键词 | 命中 | 性质 |
|---|---|---|
| `session/quit` | **1 处，仅 champ-select** | 唯一的选人退出调用 |
| `cancel-champ-select` | **1 处，同一组件** | 观战 + 旧实现分支 |
| `request-lobby` | **0 处** | 客户端从不使用；FACM 把它当回退是无效的 |
| `session/event` | 1 处（navigation） | 只发 `GAMEFLOW_EVENT.FAILED_LOBBY`（大厅加载失败），与选人无关 |
| `dodge`（常量） | `QUEUE_DODGER`/`PARTY_DODGED`/`STRANGER_DODGED`/`MATCHMAKING_DODGE_NOTIFICATIONS` | 全是通知与遥测，无控制能力 |
| `CHAMP_SELECT_QUIT` | 5 处 | 全部是 Datadog 遥测操作名（`XP_CGL_PREGAME_CHAMP_SELECT_QUIT`），不是 gameflow 事件 |
| `GAMEFLOW_EVENT` 常量 | **仅 1 个：`FAILED_LOBBY`** | 没有 `CHAMP_SELECT_QUIT` 之类的事件 |
| `leaveLobby` | parties 10 / social 1 / tft 1 | 离开**队伍房间**，不是退出选人 |

补充实测：`POST /lol-gameflow/v1/session/event` 接受纯字符串 body（JSON 字符串字面量，参数名 `session`），
`"GAMEFLOW_EVENT.FAILED_LOBBY"` / `"FAILED_LOBBY"` / `"GAMEFLOW_EVENT.CHAMP_SELECT_QUIT"` 都返回 204，
但 phase 不变 —— 说明这个端点只做事件上报，不改变流程。

**所以：市面辅助的"退出房间"按钮绕不过服务端 `showQuitButton` 门禁，
PvP 里能改变 phase 的只有"断连"语义（超时 / 杀进程 / 断网到超时）。**

## 命中概览

| 插件 | 命中 |
|---|---|
| `rcp-fe-common-libs` | `dodge`×2 |
| `rcp-fe-lol-champ-select` | `session/quit`×1, `cancel-champ-select`×1, `dodge`×7, `gameflow/v1/session/`×2, `showHome`×2 |
| `rcp-fe-lol-highlights` | `showHome`×1 |
| `rcp-fe-lol-jade` | `showHome`×2 |
| `rcp-fe-lol-navigation` | `gameflow/v1/session/`×1, `showHome`×4 |
| `rcp-fe-lol-new-player-experience` | `showHome`×1 |
| `rcp-fe-lol-parties` | `dodge`×45, `leaveLobby`×10, `showHome`×2 |
| `rcp-fe-lol-social` | `leave-lobby`×1 |
| `rcp-fe-lol-tft` | `leaveLobby`×1 |

## 命中上下文

### rcp-fe-common-libs（1.27 MB）

### rcp-fe-lol-champ-select（1.64 MB）

**选人退出** `session/quit` ×1

```
assNameBindings:["isSpectating:quit-spectate-button:quit-button"],layout:n(142),actions:{quitCustom:function(){o.startOperation(o.XP_CGL_PREGAME_CHAMP_SELECT_QUIT);const e=this.get("isLegacyChampSelect")&&this.get("isSpectating")?"/lol-lobby/v1/lobby/custom/cancel-champ-select":"/lol-lobby-team-builder/champ-select/v1/session/quit";a.default.ajax({type:"POST",url:e}).then((()=>{this.recordDidRequestSucceed&&this.recordDidRequestSucceed(!0),o.stopOperationWithOk(o.XP_CGL_PREGAME_CHAMP_SELECT_QUIT)})).catch((e=>{this.recordDidRequestSucceed&&this.recordDidRequestSucceed(!1,e),o.stopOperationWithError(o.XP_CGL_PREGAME_CHAMP_SELECT_QUIT
---
```

**自定义取消选人** `cancel-champ-select` ×1

```
atadogRum:o,Ember:l}=i;n(141),e.exports=l.Component.extend({classNameBindings:["isSpectating:quit-spectate-button:quit-button"],layout:n(142),actions:{quitCustom:function(){o.startOperation(o.XP_CGL_PREGAME_CHAMP_SELECT_QUIT);const e=this.get("isLegacyChampSelect")&&this.get("isSpectating")?"/lol-lobby/v1/lobby/custom/cancel-champ-select":"/lol-lobby-team-builder/champ-select/v1/session/quit";a.default.ajax({type:"POST",url:e}).then((()=>{this.recordDidRequestSucceed&&this.recordDidRequestSucceed(!0),o.stopOperationWithOk(o.XP_CGL_PREGAME_CHAMP_SELECT_QUIT)})).catch((e=>{this.recordDidRequestSucceed&&this.recordDidRequestSucceed(!1,
---
```

**流程会话写** `gameflow/v1/session/` ×2

```
eakMap,n=new WeakMap;return(a=function(e){return e?n:t})(e)}var i=s.default.EmberDataBinding({Ember:s.Ember,websocket:s.default.getProvider().getSocket(),logPrefix:"rcp-fe-lol-champ-select",defaultPropertyValue:{},boundProperties:{gameflow:"/lol-gameflow/v1/session",sessionPerPositionRequiredSummonerSpells:{path:"/lol-gameflow/v1/session/per-position-summoner-spells/required/as-string",default:"{}"},sessionPerPositionDisallowedSummonerSpells:{path:"/lol-gameflow/v1/session/per-position-summoner-spells/disallowed/as-string",default:"{}"},localSummoner:"/lol-summoner/v1/current-summoner",login:"/lol-login/v1/session",walletRP:"/lol-in
---
r().getSocket(),logPrefix:"rcp-fe-lol-champ-select",defaultPropertyValue:{},boundProperties:{gameflow:"/lol-gameflow/v1/session",sessionPerPositionRequiredSummonerSpells:{path:"/lol-gameflow/v1/session/per-position-summoner-spells/required/as-string",default:"{}"},sessionPerPositionDisallowedSummonerSpells:{path:"/lol-gameflow/v1/session/per-position-summoner-spells/disallowed/as-string",default:"{}"},localSummoner:"/lol-summoner/v1/current-summoner",login:"/lol-login/v1/session",walletRP:"/lol-inventory/v1/wallet/RP",inventory:{path:"/lol-champions/v1/inventories/{{loggedInPlayer}}/champions",objectTypes:{"[]":n(18)},default:[]},pi
---
```

### rcp-fe-lol-highlights（0.04 MB）

### rcp-fe-lol-jade（0.99 MB）

### rcp-fe-lol-navigation（1.40 MB）

**流程会话写** `gameflow/v1/session/` ×1

```
e(e)})),i.db.observe(o,this,(e=>{this._notify(e)})),i.db.observe("/lol-gameflow/v1/game-exit-early-vanguard",this,(e=>{this.get("lastCheaterDetectedGameId")!==e&&(i.logger.info(`Last game was ended by Vanguard. gameId: ${e}. Sending user back to lobby`),i.db.post("/lol-lobby/v2/play-again").catch((()=>{i.db.post("/lol-gameflow/v1/session/event","GAMEFLOW_EVENT.FAILED_LOBBY")})),this.set("lastCheaterDetectedGameId",e),this._showGameEndedEarlyVanguardNotification())})),i.db.observe("/lol-gameflow/v1/player-kicked-vanguard",this,(e=>{e&&(i.logger.info("Player kicked by Vanguard. Shutting client down."),this.set("playerKickedVanguard",!
---
```

### rcp-fe-lol-new-player-experience（0.08 MB）

### rcp-fe-lol-parties（1.67 MB）

**离房(方法)** `leaveLobby` ×10

```
r_code:i.error_code||void 0,implementation_details:Object.keys(o??{}).length?o:void 0}),s.datadogRum.hasOngoingOperation(s.datadogRum.XP_CGL_PREGAME_LOBBY_CREATE)&&s.datadogRum.stopOperationWithError(s.datadogRum.XP_CGL_PREGAME_LOBBY_CREATE,t,{lobby_create:{queue_id:e,source:"party_api_create_lobby"},error_details:i})}leaveLobby(){return new Promise(((e,t)=>{this._currentPlayer?this._currentParty?(this._partyDestroyedCallback=()=>{e()},this._lobbiesObserver.leaveLobby(this._phantomLobbyRecoveryEnabled).then((t=>{t&&(this._partyDestroyedCallback=null,e())})).catch((e=>{this._partyDestroyedCallback=null,t(e)}))):t(new Error("NOT_IN_LO
---
REGAME_LOBBY_CREATE)&&s.datadogRum.stopOperationWithError(s.datadogRum.XP_CGL_PREGAME_LOBBY_CREATE,t,{lobby_create:{queue_id:e,source:"party_api_create_lobby"},error_details:i})}leaveLobby(){return new Promise(((e,t)=>{this._currentPlayer?this._currentParty?(this._partyDestroyedCallback=()=>{e()},this._lobbiesObserver.leaveLobby(this._phantomLobbyRecoveryEnabled).then((t=>{t&&(this._partyDestroyedCallback=null,e())})).catch((e=>{this._partyDestroyedCallback=null,t(e)}))):t(new Error("NOT_IN_LOBBY")):t("NOT_SIGNED_IN")}))}_connectViewportIsShowing(){this._screenRoot.on("show",(()=>{this._showingState.set("isInViewport",!0),y.default.
---
viceShutdown=r,this._partyServiceUnavailable=c,this._lobbyBinding=(0,s.dataBinding)("/lol-lobby",l),this._gameflowBinding=(0,s.dataBinding)("/lol-gameflow",l),this._observeLobbies(),this._observeEligibilitiesReady(),this._observeLobbyNotifications()}createLobby(e){return this._lobbyBinding.post("v2/lobby",{queueId:e})}leaveLobby(e=!1){return this._lobbyBinding.delete("v2/lobby").then((()=>!1)).catch((t=>{if(!e||"LOBBY_NOT_FOUND"!==t?.data?.errorCode)throw t;return s.logger.error("FAILED TO LEAVE LOBBY, FOUNDATION HAS NO LOBBY BUT UI STILL HAS ONE, CLEARING UI"),this._lobbyData&&this._deletedLobby(),!0}))}_observeLobbies(){this._lobb
---
```

### rcp-fe-lol-social（1.62 MB）

**离房** `leave-lobby` ×1

```
-open.ogg",chatWindowClose:"sfx-soc-ui-chatwindow-close.ogg",friendRequest:"sfx-soc-notif-friendreq-rcvd.ogg",gameInvite:"sfx-soc-notif-gameinvite-rcvd.ogg",gameInviteAccept:"sfx-soc-ui-gameinvite-accept-click.ogg",gameInviteFailed:"sfx-lobby-notif-bad-invite.ogg",joinedChatRoom:null,leftChatRoom:"sfx-soc-notif-player-leave-lobby.ogg",menuOpen:"sfx-soc-ui-statuswindow-open.ogg",menuClose:"sfx-soc-ui-statuswindow-close.ogg",success:"sfx-soc-notif-success.ogg",drag:"sfx-soc-ui-drag.ogg",drop:"sfx-soc-ui-drop.ogg",error:"sfx-login-notif-login-fail.ogg",gridHover:"sfx-uikit-grid-big-hover.ogg"},a={JADE_chatMessage:"/fe/lol-jade/audio/sf
---
```

### rcp-fe-lol-tft（0.88 MB）

**离房(方法)** `leaveLobby` ×1

```
:"promethium_hub_redirect"});s.datadogRum.stopOperationWithAbort(s.datadogRum.XP_CGL_PREGAME_LOBBY_CREATE,e)}return}let a;return s.Telemetry.sendCustomData(l.DEFAULT_TELEMETRY_TABLE,{eventName:"click-event-hub-play-button"}),this.get("shouldRemakeParty")?(s.Parties.suppressNavigateHomeForNextPartyDestroy(),a=s.Parties.leaveLobby().catch((e=>{throw s.Parties.clearSuppressNavigateHome(),e})).then((()=>s.Parties.createLobby(t)))):a=s.Parties.createLobby(t),a.then((()=>{const e=this.get("promethiumManagerService");e.validatePromethiumQueue(this.get("quickPlayGameModeQueueId"))&&e.clearMissions(),s.datadogRum.stopOperationWithOk(s.datado
---
```

