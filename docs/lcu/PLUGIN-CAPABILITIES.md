# 插件能力面（从 WAD 解包 + JS bundle 提取）

数据来源：`LeagueClient/plugins/<plugin>/assets.wad` → zstd 帧解压 → 最大 JS bundle → 静态提取。
这是**客户端自己声明的路由**，比 `/help` 更贴近实现，也比黑盒探测更完整。

- 覆盖插件: 10
- 去重路由: 1569
- 写调用（去重方法+路径）: 277

## 按前缀分组的路由

### `//lol-champ-select/`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-champ-select/` | rcp-fe-lol-champ-select |

### `//lol-collections/`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/` | rcp-fe-lol-collections |

### `//lol-match-history/`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-match-history/` | rcp-fe-lol-match-history |

### `//lol-navigation/`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/` | rcp-fe-lol-navigation |

### `//lol-parties/`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/` | rcp-fe-lol-parties |

### `//lol-postgame/`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/` | rcp-fe-lol-postgame |

### `//lol-social/`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/` | rcp-fe-lol-social |

### `//lol-static-assets/`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-static-assets/` | rcp-fe-lol-postgame |

### `/data-store/v1`（4）

| 路由 | 声明插件 |
|---|---|
| `/data-store/v1/system-settings/player_bug_report_url` | rcp-fe-lol-social |
| `/data-store/v1/system-settings/player_support_url` | rcp-fe-lol-navigation |
| `/data-store/v1/system-settings/region_data/${o.region}/rso_platform_id` | rcp-fe-lol-postgame |
| `/data-store/v1/system-settings/web_assets_base_path` | rcp-fe-lol-champ-select |

### `/lol-activity-center/v1`（6）

| 路由 | 声明插件 |
|---|---|
| `/lol-activity-center/v1` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation |
| `/lol-activity-center/v1/content/client-nav` | rcp-fe-lol-navigation |
| `/lol-activity-center/v1/debug/force-error` | rcp-fe-lol-navigation |
| `/lol-activity-center/v1/debug/info-hub-error` | rcp-fe-lol-navigation |
| `/lol-activity-center/v1/debug/open-info-hub` | rcp-fe-lol-navigation |
| `/lol-activity-center/v1/ready` | rcp-fe-lol-navigation |

### `/lol-catalog/v1`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-catalog/v1/item-details` | rcp-fe-lol-navigation |
| `/lol-catalog/v1/items/EMOTE` | rcp-fe-lol-collections |
| `/lol-catalog/v1/items/PORTRAIT` | rcp-fe-lol-champ-select |

### `/lol-challenges/v1`（8）

| 路由 | 声明插件 |
|---|---|
| `/lol-challenges/v1/ack-challenge-update/${e}` | rcp-fe-lol-navigation |
| `/lol-challenges/v1/available-queue-ids` | rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-challenges/v1/challenges/local-player` | rcp-fe-lol-postgame |
| `/lol-challenges/v1/my-updated-challenges/${e}` | rcp-fe-lol-postgame |
| `/lol-challenges/v1/notifications` | rcp-fe-lol-navigation |
| `/lol-challenges/v1/penalty` | rcp-fe-lol-postgame |
| `/lol-challenges/v1/summary-player-data/player/${e}` | rcp-fe-lol-social |
| `/lol-challenges/v1/updated-challenges/${a}/${t}` | rcp-fe-lol-postgame |

### `/lol-challenges/v2`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-challenges/v2/titles/all` | rcp-fe-lol-social |

### `/lol-champ-select/images`（20）

| 路由 | 声明插件 |
|---|---|
| `/lol-champ-select/images/background/bravery-splash-background.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/ban-showcase/icon-ban.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/card-select/card-idle-lowspec.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/card-select/card-intro-lowspec.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/card-select/lucky-card-idle-lowspec.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/card-select/lucky-card-intro-lowspec.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/champion-grid/bravery-champion-circle.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/champion-grid/bravery-champion.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/champion-grid/random-champion.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/config/edit-perks-button.png` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-champ-select/images/perks/rune-recommender-icon.png` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-champ-select/images/subteams/Gromp.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/subteams/Krug.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/subteams/Minion.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/subteams/Poro.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/subteams/Raptor.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/subteams/Scuttle.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/subteams/Sentinel.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/subteams/Wolf.png` | rcp-fe-lol-champ-select |
| `/lol-champ-select/images/summoner-object/button-mute.svg` | rcp-fe-lol-champ-select |

### `/lol-champ-select/sounds`（63）

| 路由 | 声明插件 |
|---|---|
| `/lol-champ-select/sounds` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-ban-base-layer-01.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-ban-intensity-layer-01.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-ban-intensity-layer-02.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-ban-to-pick-trans-01.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-finalization-60sec-01.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-pick-base-layer-01.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-pick-intensity-layer-01.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-pick-intensity-layer-02.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-pick-intensity-layer-03.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-pick-intensity-layer-04.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-pickintent-01.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/music-cs-draft-pickintent-to-ban-trans-01.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-champ-select-bench-update.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-champstinger-bravery.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-aram-card-2-appear.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-aram-card-3-appear.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-aram-card-hover.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-aram-card-select.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-button-reroll-click.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-button-reroll-hover.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-button-swap-click.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-10ban-ally-ban.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-10ban-enemy-ban.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-10ban-intro.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-10ban-team-bans-locked.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-10ban-your-ban.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-10ban-your-pick-denied.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-ban-button-click.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-ban-button-hover.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-ban-enemy-team.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-ban-ring-intro.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-ban-your-team.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-intent-champgrid-open.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-left-first-pick.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-left-pick-single.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-notif-yourban.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-notif-yourpick.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-pick-intro.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-posassign-ally-0.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-posassign-ally-1.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-posassign-ally-2.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-posassign-ally-3.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-posassign-ally-4.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-posassign-player.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-right-first-pick.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-draft-right-pick-single.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-lockin-button-click.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-lockin-button-hover.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-notif-roleswaprequest-rcvd.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-notif-swaprequest-accepted.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-notif-swaprequest-declined.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-notif-swaprequest-rcvd.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-timer-tick-small.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-cs-timer-tick.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-spellchoose-${e.id}.ogg` | rcp-fe-lol-parties |
| `/lol-champ-select/sounds/sfx-uikit-button-arrowback-click.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-uikit-button-arrowfwd-click.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-uikit-button-gold-hover.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-uikit-edit-click.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections, rcp-fe-lol-parties |
| `/lol-champ-select/sounds/sfx-uikit-grid-click.ogg` | rcp-fe-lol-champ-select |
| `/lol-champ-select/sounds/sfx-uikit-grid-hover.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections, rcp-fe-lol-parties |

### `/lol-champ-select/svg`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-champ-select/svg/` | rcp-fe-lol-champ-select |
| `/lol-champ-select/svg/position-${t.toLowerCase` | rcp-fe-lol-collections |

### `/lol-champ-select/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-champ-select/trans.json` | rcp-fe-lol-champ-select |

### `/lol-champ-select/v1`（19）

| 路由 | 声明插件 |
|---|---|
| `/lol-champ-select/v1/bannable-champion-ids` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/ongoing-champion-swap` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/ongoing-pick-order-swap` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/ongoing-position-swap` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/pickable-champion-ids` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/retrieve-latest-game-dto` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session` | rcp-fe-lol-champ-select, rcp-fe-lol-social |
| `/lol-champ-select/v1/session/actions/` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session/actions/${e.get` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session/bench/swap/` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session/champion-swaps` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session/my-selection` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session/my-selection/reroll` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session/pick-order-swaps` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session/position-swaps` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/session/simple-inventory` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/team-boost` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/team-boost/purchase` | rcp-fe-lol-champ-select |
| `/lol-champ-select/v1/toggle-player-muted` | rcp-fe-lol-champ-select |

### `/lol-champ-select/video`（34）

| 路由 | 声明插件 |
|---|---|
| `/lol-champ-select/video/card-select/Champ_Select_Card_Idle_Loop.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/card-select/Champ_Select_Default_Card_Intro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/card-select/Champ_Select_Hover_State_Idle_Loop.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/card-select/Champ_Select_Hover_State_Intro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/card-select/Champ_Select_Lucky_Card_Intro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/card-select/Champ_Select_Selected_State.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/champion-ring/ban-circle-slash-blue.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/champion-ring/ban-circle-slash-red.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/cherry/bravery-animated-splash.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment-intro/map-north-intro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment-intro/map-south-intro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Classic_ChampionSelect_MapPositionPin_Ally.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Classic_ChampionSelect_MapPositionPin_Self.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Classic_ChampionSelect_Nexus_Blue.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Classic_ChampionSelect_Nexus_Purple.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Path_North_Bot.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Path_North_Jungle.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Path_North_Mid.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Path_North_Top.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Path_South_Bot.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Path_South_Jungle.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Path_South_Mid.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Path_South_Top.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Pin_Intro` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/position-assignment/Pin_Me_Intro` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-blue-idle.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-blue-intro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-blue-outro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-gold-idle.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-gold-intro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-gold-outro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-red-idle.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-red-intro.webm` | rcp-fe-lol-champ-select |
| `/lol-champ-select/video/summoner-object/summoner-object-magic-action-red-outro.webm` | rcp-fe-lol-champ-select |

### `/lol-champion-details/audio`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-champion-details/audio/sfx-champselect-button-arrowback-click.ogg` | rcp-fe-lol-champ-select |
| `/lol-champion-details/audio/sfx-champselect-button-arrowfwd-click.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |

### `/lol-champion-details/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-champion-details/trans.json` | rcp-fe-lol-champ-select |

### `/lol-champion-mastery/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-champion-mastery/v1/local-player/champion-mastery` | rcp-fe-lol-parties |
| `/lol-champion-mastery/v1/notifications` | rcp-fe-lol-navigation |

### `/lol-champions/v1`（12）

| 路由 | 声明插件 |
|---|---|
| `/lol-champions/v1/inventories/${e.summonerId}/${o.JADE_QUEUE_ID}/champions-minimal-per-queue` | rcp-fe-lol-champ-select |
| `/lol-champions/v1/inventories/${e.summonerId}/4310/champions-per-queue` | rcp-fe-lol-champ-select |
| `/lol-champions/v1/inventories/${e.summonerId}/champions` | rcp-fe-lol-parties |
| `/lol-champions/v1/inventories/${n}/champions/${e.id}` | rcp-fe-lol-champ-select |
| `/lol-champions/v1/inventories/${s}/${n}/champions-minimal-per-queue` | rcp-fe-lol-champ-select |
| `/lol-champions/v1/inventories/${t}/${e}/champions-minimal-per-queue` | rcp-fe-lol-parties |
| `/lol-champions/v1/inventories/${t}/4310/champions-per-queue` | rcp-fe-lol-champ-select |
| `/lol-champions/v1/inventories/${t}/champions/${a}` | rcp-fe-lol-champ-select |
| `/lol-champions/v1/inventories/${t}/champions/${e}` | rcp-fe-lol-champ-select |
| `/lol-champions/v1/inventories/{{loggedInPlayer}}/champions` | rcp-fe-lol-champ-select |
| `/lol-champions/v1/inventories/{{summonerId}}/champions/{{championId}}` | rcp-fe-lol-loot |
| `/lol-champions/v1/inventories/{{summonerId}}/champions/{{championId}}/skins/{{championSkinId}}` | rcp-fe-lol-loot |

### `/lol-chat/v1`（32）

| 路由 | 声明插件 |
|---|---|
| `/lol-chat/v1/blocked-players` | rcp-fe-lol-match-history, rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-chat/v1/blocked-players/${o}` | rcp-fe-lol-match-history |
| `/lol-chat/v1/config` | rcp-fe-lol-social |
| `/lol-chat/v1/conversations` | rcp-fe-lol-champ-select, rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-chat/v1/conversations/` | rcp-fe-lol-social |
| `/lol-chat/v1/conversations/${a}/messages` | rcp-fe-lol-social |
| `/lol-chat/v1/conversations/${encodeURIComponent` | rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-chat/v1/conversations/active` | rcp-fe-lol-social |
| `/lol-chat/v1/conversations/eog-chat-toggle` | rcp-fe-lol-social |
| `/lol-chat/v1/conversations/notify` | rcp-fe-lol-social |
| `/lol-chat/v1/conversations/{{` | rcp-fe-lol-social |
| `/lol-chat/v1/conversations/{{encodeURIComponent` | rcp-fe-lol-social |
| `/lol-chat/v1/conversations/{{encodedConversationId}}/participants` | rcp-fe-lol-postgame |
| `/lol-chat/v1/discord-link-status` | rcp-fe-lol-social |
| `/lol-chat/v1/errors` | rcp-fe-lol-social |
| `/lol-chat/v1/errors/` | rcp-fe-lol-social |
| `/lol-chat/v1/errors/{id}` | rcp-fe-lol-social |
| `/lol-chat/v1/friend-groups` | rcp-fe-lol-social |
| `/lol-chat/v1/friend-groups/${e.id}` | rcp-fe-lol-social |
| `/lol-chat/v1/friend-groups/${e}` | rcp-fe-lol-social |
| `/lol-chat/v1/friend-groups/${t.id}` | rcp-fe-lol-social |
| `/lol-chat/v1/friends` | rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-chat/v1/friends/` | rcp-fe-lol-social |
| `/lol-chat/v1/friends/${e}` | rcp-fe-lol-social |
| `/lol-chat/v1/friends/${o}` | rcp-fe-lol-match-history |
| `/lol-chat/v1/is-discord-integration-enabled` | rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-chat/v1/is-discord-link-available` | rcp-fe-lol-parties |
| `/lol-chat/v1/is-discord-linked` | rcp-fe-lol-parties |
| `/lol-chat/v1/player-mutes` | rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-chat/v1/resources` | rcp-fe-lol-social |
| `/lol-chat/v1/session` | rcp-fe-lol-social |
| `/lol-chat/v1/settings` | rcp-fe-lol-social |

### `/lol-chat/v2`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-chat/v2/friend-requests` | rcp-fe-lol-social |
| `/lol-chat/v2/friend-requests/` | rcp-fe-lol-match-history, rcp-fe-lol-social |

### `/lol-clash/assets`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-clash/assets/images/roster-logos/social-panel-pending-roster.png` | rcp-fe-lol-social |

### `/lol-clash/v1`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-clash/v1/enabled` | rcp-fe-lol-navigation, rcp-fe-lol-social |
| `/lol-clash/v1/eog-player-update` | rcp-fe-lol-navigation |
| `/lol-clash/v1/player/chat-rosters` | rcp-fe-lol-social |
| `/lol-clash/v1/visible` | rcp-fe-lol-social |

### `/lol-clash/v2`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-clash/v2/playmode-restricted` | rcp-fe-lol-social |

### `/lol-client-config/v3`（50）

| 路由 | 声明插件 |
|---|---|
| `/lol-client-config/v3/client-config` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config${r.ACTIVE_BANNERS}` | rcp-fe-lol-loot |
| `/lol-client-config/v3/client-config/` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.bots.eog.queues_to_show_fake_challenges` | rcp-fe-lol-postgame |
| `/lol-client-config/v3/client-config/lol.client_settings.bots.eog.queues_to_show_selected_positions` | rcp-fe-lol-postgame |
| `/lol-client-config/v3/client-config/lol.client_settings.champ_mastery.lcm_celebrations_enabled` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.champ_select.enable_ability_previews` | rcp-fe-lol-champ-select |
| `/lol-client-config/v3/client-config/lol.client_settings.champion_select.disable_ranked_asset_swap` | rcp-fe-lol-champ-select |
| `/lol-client-config/v3/client-config/lol.client_settings.champion_select.enable_asset_swap` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.client_navigability.eog_seasons_enabled` | rcp-fe-lol-postgame |
| `/lol-client-config/v3/client-config/lol.client_settings.client_navigability.info_hub_disabled` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.client_navigability.persistent-header-disabled` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.client_navigability.tencent_experience` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.demacia_champ_select.enabled` | rcp-fe-lol-champ-select |
| `/lol-client-config/v3/client-config/lol.client_settings.discordIntegration.enabled` | rcp-fe-lol-social |
| `/lol-client-config/v3/client-config/lol.client_settings.discordIntegrationBeta.enabled` | rcp-fe-lol-social |
| `/lol-client-config/v3/client-config/lol.client_settings.display_legacy_patch_numbers` | rcp-fe-lol-social |
| `/lol-client-config/v3/client-config/lol.client_settings.hub_redirect_override` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.internal_to_external_patch_mapping` | rcp-fe-lol-social |
| `/lol-client-config/v3/client-config/lol.client_settings.jade.navRoutes` | rcp-fe-lol-champ-select |
| `/lol-client-config/v3/client-config/lol.client_settings.jade.tencentChampionLearnMoreUrl` | rcp-fe-lol-champ-select |
| `/lol-client-config/v3/client-config/lol.client_settings.lobby.customs_position_preferences.enabled` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.lobby.phantom_lobby_recovery_enabled` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.match_history.recently_played_summoners.friend_finder_disabled` | rcp-fe-lol-social |
| `/lol-client-config/v3/client-config/lol.client_settings.match_history.recently_played_summoners.invite_dialog_disabled` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.navigation.enableRewardsProgram` | rcp-fe-lol-collections |
| `/lol-client-config/v3/client-config/lol.client_settings.oogij.enabled` | rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-client-config/v3/client-config/lol.client_settings.paw.enableRPTopUp` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.player_behavior.display_login_text_chat_restrictions` | rcp-fe-lol-social |
| `/lol-client-config/v3/client-config/lol.client_settings.purchase_widget.discount_disabled` | rcp-fe-lol-champ-select |
| `/lol-client-config/v3/client-config/lol.client_settings.queues.game_select_pip_queue_ids` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.show_win7_notification_on_every_startup` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.social_panel_restriction.enabled` | rcp-fe-lol-social |
| `/lol-client-config/v3/client-config/lol.client_settings.store.lcu.enabled` | rcp-fe-lol-loot |
| `/lol-client-config/v3/client-config/lol.client_settings.summoner_id_removal.champ_select_plugin.enabled` | rcp-fe-lol-champ-select |
| `/lol-client-config/v3/client-config/lol.client_settings.team_voice.enabled` | rcp-fe-lol-champ-select |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.always_show_wip_badge` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.bridge_enabled` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.full_launch_enabled` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.show_os_compatibility_notification_on_every_startup` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.tft_content_retiering_loot_deprecation` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.tft_events` | rcp-fe-lol-postgame |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.tft_loadouts_disabled_content_by_queue` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.tft_loadouts_favorites_max` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.tft_loadouts_sortByLastAcquired` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.tft_playbook` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/lol.client_settings.tft.tft_teamPlanner_endOfGameImport` | rcp-fe-lol-postgame |
| `/lol-client-config/v3/client-config/lol.client_settings.tft_skill_tree_enabled` | rcp-fe-lol-parties |
| `/lol-client-config/v3/client-config/operational.awConfig` | rcp-fe-lol-navigation |
| `/lol-client-config/v3/client-config/operational.champions.globallyDisabledChampions` | rcp-fe-lol-parties |

### `/lol-collections/audio`（26）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/audio/sfx-collection-browse-enlarge-click.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-collection-browse-left-click.ogg` | rcp-fe-lol-collections, rcp-fe-lol-parties |
| `/lol-collections/audio/sfx-collection-browse-open-click.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-collection-browse-right-click.ogg` | rcp-fe-lol-collections, rcp-fe-lol-parties |
| `/lol-collections/audio/sfx-collection-notif-set-complete.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-emotes-button-reset-click.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-emotes-full.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-emotes-pickup.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-emotes-place-all.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-emotes-place.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-emotes-remove.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-empty.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-button-circlegold-hover.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-button-circlex-click.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-button-flyout-close-click.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-button-flyout-open-click.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-button-gold-click.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-button-gold-hover.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-button-replay-click.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-click-generic.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-drag.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-drop.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-dropdown-select.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-generic-click-small.ogg` | rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-grid-click.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-collections/audio/sfx-uikit-grid-hover.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |

### `/lol-collections/images`（20）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/images/control-panel/icon-legacy.png` | rcp-fe-lol-collections |
| `/lol-collections/images/finisher/BaseFinisher.png` | rcp-fe-lol-collections |
| `/lol-collections/images/grid/esport-event-name-decorator.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/Free-Rotation.png` | rcp-fe-lol-collections, rcp-fe-lol-parties |
| `/lol-collections/images/item-element/Loyalty.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/Rental.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/crest-and-banner-mastery-${Math.min` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/crest-and-banner-mastery-0.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/mastery-banner-blue-lr-lines.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/mastery-banner-blue-max.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/mastery-banner-blue-solid.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/mastery-banner-gold-solid.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/mastery-banner-red-max.png` | rcp-fe-lol-collections |
| `/lol-collections/images/item-element/rewards-program-icon.svg` | rcp-fe-lol-collections |
| `/lol-collections/images/mastery-header/bxp-flat-icon.png` | rcp-fe-lol-collections |
| `/lol-collections/images/mastery-header/reward-blue-essence.svg` | rcp-fe-lol-collections |
| `/lol-collections/images/mastery-header/reward-masterwork-chest.png` | rcp-fe-lol-collections |
| `/lol-collections/images/mastery-header/reward-pass-xp.png` | rcp-fe-lol-collections |
| `/lol-collections/images/skins-viewer/borders/borders_${e.rarity.borderRarity}` | rcp-fe-lol-collections |
| `/lol-collections/images/skins-viewer/icon-chroma.png` | rcp-fe-lol-collections |

### `/lol-collections/perks`（112）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/perks/audio/sfx-masteries-button-generic-click.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-default-primary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-default-primary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-default-secondary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-default-seondary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-dom-keystone-dh.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-dom-keystone-elec.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-dom-keystone-hob.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-dom-keystone-pred.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-dom-primary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-dom-primary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-dom-secondary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-dom-secondary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-drawer-open.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-edit-click.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-grid-select-default.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-insp-keystone-ga.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-insp-keystone-ss.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-insp-keystone-vp.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-insp-primary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-insp-primary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-insp-secondary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-insp-secondary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lightsaber.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8003.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8008.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8021.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8112.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8124.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8128.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8214.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8229.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8230.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8351.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8360.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8369.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8437.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8439.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8465.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-8992.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-keystone-9923.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-primary-8000.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-primary-8100.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-primary-8200.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-primary-8300.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-primary-8400.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-secondary-8000.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-secondary-8100.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-secondary-8200.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-secondary-8300.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-lockin-secondary-8400.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-prec-keystone-con.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-prec-keystone-ff.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-prec-keystone-lt.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-prec-keystone-pta.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-prec-primary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-prec-primary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-prec-secondary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-prec-secondary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-res-keystone-a.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-res-keystone-g.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-res-keystone-gotu.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-res-primary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-res-primary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-res-secondary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-res-secondary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-Bp.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-No.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-bd.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-cb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-d.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-default.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-gp.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-ma.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-pt.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-sc.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-bd.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-bp.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-cb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-d.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-default.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-gp.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-ma.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-no.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-pt.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-sc.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-secondary-zw.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-runes-zw.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-sorc-keystone-ac.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-sorc-keystone-pr.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-sorc-keystone-sk.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-sorc-primary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-sorc-primary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-sorc-secondary-amb.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-perks-sorc-secondary-intro.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/audio/sfx-uikit-grid-hover.ogg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/Domination/domination_icon.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/Inspiration/inspiration_icon.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/Precision/precision_icon.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/Resolve/resolve_icon.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/Sorcery/sorcery_icon.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/exclamation.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/lock.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/rune-recommender/edit-page-icon.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/rune-recommender/runes-recommender-icon.png` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/rune-recommender/summoner-spell-icon.png` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/rune-recommender/swap-summoner-spells-icon.png` | rcp-fe-lol-collections |
| `/lol-collections/perks/images/runes.svg` | rcp-fe-lol-collections |
| `/lol-collections/perks/video/${t}/background_loop.webm` | rcp-fe-lol-collections |
| `/lol-collections/perks/video/inventory-card/CreateCard.webm` | rcp-fe-lol-collections |
| `/lol-collections/perks/video/style-picker-column/VFX_hover.webm` | rcp-fe-lol-collections |

### `/lol-collections/trans-emote-panel.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/trans-emote-panel.json` | rcp-fe-lol-collections |

### `/lol-collections/trans-item-sets.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/trans-item-sets.json` | rcp-fe-lol-collections |

### `/lol-collections/trans-perks.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/trans-perks.json` | rcp-fe-lol-collections |

### `/lol-collections/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/trans.json` | rcp-fe-lol-collections |

### `/lol-collections/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/v1/inventories/${e}/backdrop` | rcp-fe-lol-social |

### `/lol-collections/video`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-collections/video` | rcp-fe-lol-collections |
| `/lol-collections/video/purchase/linger.webm` | rcp-fe-lol-collections |
| `/lol-collections/video/purchase/skin-linger.webm` | rcp-fe-lol-collections |
| `/lol-collections/video/spells/spells_` | rcp-fe-lol-collections |

### `/lol-cosmetics/v1`（19）

| 路由 | 声明插件 |
|---|---|
| `/lol-cosmetics/v1/favorites/tft/augment-pillars` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/favorites/tft/companions` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/favorites/tft/damage-skins` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/favorites/tft/map-skins` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/favorites/tft/save` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/favorites/tft/zoom-skins` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/inventories/tft/augment-pillars` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/inventories/tft/companions` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/inventories/tft/damage-skins` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/inventories/tft/map-skins` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/inventories/tft/playbooks` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/inventories/tft/zoom-skins` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/recent/` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/selection/companion` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/selection/playbook` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/selection/tft-augment-pillar` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/selection/tft-damage-skin` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/selection/tft-map-skin` | rcp-fe-lol-parties |
| `/lol-cosmetics/v1/selection/tft-zoom-skin` | rcp-fe-lol-parties |

### `/lol-directx-upgrade/needs-hardware-upgrade`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-directx-upgrade/needs-hardware-upgrade` | rcp-fe-lol-social |

### `/lol-directx-upgrade/notification-ack`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-directx-upgrade/notification-ack` | rcp-fe-lol-navigation |

### `/lol-directx-upgrade/notification-type`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-directx-upgrade/notification-type` | rcp-fe-lol-navigation |

### `/lol-email-verification/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-email-verification/v1/email` | rcp-fe-lol-navigation |

### `/lol-end-of-game/v1`（5）

| 路由 | 声明插件 |
|---|---|
| `/lol-end-of-game/v1/champion-mastery-updates` | rcp-fe-lol-postgame |
| `/lol-end-of-game/v1/eog-stats-block` | rcp-fe-lol-match-history, rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-end-of-game/v1/gameclient-eog-stats-block` | rcp-fe-lol-postgame |
| `/lol-end-of-game/v1/state/dismiss-stats` | rcp-fe-lol-navigation |
| `/lol-end-of-game/v1/tft-eog-stats` | rcp-fe-lol-postgame |

### `/lol-event-hub/v1`（9）

| 路由 | 声明插件 |
|---|---|
| `/lol-event-hub/v1` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-event-hub/v1/events` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-event-hub/v1/events/${e}/objectives-banner` | rcp-fe-lol-navigation |
| `/lol-event-hub/v1/events/${o.eventId}/objectives-banner` | rcp-fe-lol-postgame |
| `/lol-event-hub/v1/events/${t}/objectives-banner` | rcp-fe-lol-navigation |
| `/lol-event-hub/v1/events/${t}/reward-track/counter` | rcp-fe-lol-postgame |
| `/lol-event-hub/v1/events/${t}/reward-track/items` | rcp-fe-lol-champ-select |
| `/lol-event-hub/v1/events/${t}/reward-track/progress` | rcp-fe-lol-champ-select |
| `/lol-event-hub/v1/events/0724e93d-6b74-449f-abef-8785262c3890/reward-track/counter` | rcp-fe-lol-postgame |

### `/lol-game-data-inventory/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-game-data-inventory/v1/items/contentIds` | rcp-fe-lol-champ-select, rcp-fe-lol-loot |
| `/lol-game-data-inventory/v1/items/contentIds/${t}` | rcp-fe-lol-champ-select |

### `/lol-game-data/assets`（42）

| 路由 | 声明插件 |
|---|---|
| `/lol-game-data/assets` | rcp-fe-lol-navigation |
| `/lol-game-data/assets/` | rcp-fe-lol-champ-select, rcp-fe-lol-loot |
| `/lol-game-data/assets/ASSETS/UX/TFT/OutOfGame/EventHub/Promethium/Buddies/Prom_Icon_PerfectRun.png` | rcp-fe-lol-postgame |
| `/lol-game-data/assets/ASSETS/UX/TFT/OutOfGame/Missions/SeriesIcons/series_icon.svg` | rcp-fe-lol-parties |
| `/lol-game-data/assets/content/src/LeagueClient/GameModeAssets` | rcp-fe-lol-parties |
| `/lol-game-data/assets/content/src/LeagueClient/GameModeAssets/Cherry/img/icon-v2.png` | rcp-fe-lol-parties |
| `/lol-game-data/assets/content/src/LeagueClient/GameModeAssets/GameModeCommon/img/icon-v2.png` | rcp-fe-lol-parties |
| `/lol-game-data/assets/content/src/LeagueClient/GameModeAssets/Jade/img/game-select-mode-icon-selected.png` | rcp-fe-lol-parties |
| `/lol-game-data/assets/content/src/LeagueClient/GameModeAssets/Strawberry/img/icon-v2.png` | rcp-fe-lol-parties |
| `/lol-game-data/assets/content/src/LeagueClient/WardSkinImages` | rcp-fe-lol-store |
| `/lol-game-data/assets/v1` | rcp-fe-lol-champ-select, rcp-fe-lol-store |
| `/lol-game-data/assets/v1/champion-icons/` | rcp-fe-lol-collections |
| `/lol-game-data/assets/v1/champion-icons/${e}.png` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-game-data/assets/v1/champion-icons/-1.png` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-game-data/assets/v1/champion-summary.json` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation, rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-game-data/assets/v1/champions/${e.id}.json` | rcp-fe-lol-champ-select |
| `/lol-game-data/assets/v1/champions/${e}.json` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation, rcp-fe-lol-social |
| `/lol-game-data/assets/v1/cherry-augments.json` | rcp-fe-lol-postgame |
| `/lol-game-data/assets/v1/game-mode-mutators.json` | rcp-fe-lol-postgame |
| `/lol-game-data/assets/v1/generic-assets.json` | rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-game-data/assets/v1/hextech-images/${e.lootName}.png` | rcp-fe-lol-loot |
| `/lol-game-data/assets/v1/items.json` | rcp-fe-lol-postgame |
| `/lol-game-data/assets/v1/jade-mastery-display.json` | rcp-fe-lol-champ-select |
| `/lol-game-data/assets/v1/jade-perks.json` | rcp-fe-lol-champ-select |
| `/lol-game-data/assets/v1/kiwi-hub.json` | rcp-fe-lol-kiwi-hub |
| `/lol-game-data/assets/v1/lolcurrency.json` | rcp-fe-lol-champ-select |
| `/lol-game-data/assets/v1/perks.json` | rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-game-data/assets/v1/perkstyles.json` | rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-game-data/assets/v1/portraits.json` | rcp-fe-lol-champ-select |
| `/lol-game-data/assets/v1/profile-icons/${e}.jpg` | rcp-fe-lol-navigation, rcp-fe-lol-parties |
| `/lol-game-data/assets/v1/profile-icons/${t}.jpg` | rcp-fe-lol-champ-select |
| `/lol-game-data/assets/v1/profile-icons/1.jpg` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-game-data/assets/v1/rarity-gem-icons/epic.png` | rcp-fe-lol-parties |
| `/lol-game-data/assets/v1/rarity-gem-icons/legendary.png` | rcp-fe-lol-parties |
| `/lol-game-data/assets/v1/rarity-gem-icons/mythic.png` | rcp-fe-lol-parties |
| `/lol-game-data/assets/v1/rarity-gem-icons/ultimate.png` | rcp-fe-lol-parties |
| `/lol-game-data/assets/v1/skins.json` | rcp-fe-lol-loot |
| `/lol-game-data/assets/v1/summoner-icons.json` | rcp-fe-lol-social |
| `/lol-game-data/assets/v1/summoner-spells.json` | rcp-fe-lol-champ-select, rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-game-data/assets/v1/tftsets.json` | rcp-fe-lol-navigation, rcp-fe-lol-postgame |
| `/lol-game-data/assets/v1/tftuxtunables.json` | rcp-fe-lol-postgame |
| `/lol-game-data/assets/v1/ward-skins.json` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |

### `/lol-game-queues/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-game-queues/v1/queues` | rcp-fe-lol-social |
| `/lol-game-queues/v1/queues/4310` | rcp-fe-lol-champ-select |

### `/lol-gameflow/${f}`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-gameflow/${f}` | rcp-fe-lol-champ-select |

### `/lol-gameflow/v1`（11）

| 路由 | 声明插件 |
|---|---|
| `/lol-gameflow/v1/ack-failed-to-launch` | rcp-fe-lol-navigation |
| `/lol-gameflow/v1/availability` | rcp-fe-lol-postgame |
| `/lol-gameflow/v1/game-client` | rcp-fe-lol-navigation |
| `/lol-gameflow/v1/game-exit-early-vanguard` | rcp-fe-lol-navigation |
| `/lol-gameflow/v1/gameflow-metadata/player-status` | rcp-fe-lol-postgame |
| `/lol-gameflow/v1/player-kicked-vanguard` | rcp-fe-lol-navigation |
| `/lol-gameflow/v1/session` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-gameflow/v1/session/event` | rcp-fe-lol-navigation |
| `/lol-gameflow/v1/session/per-position-summoner-spells/disallowed/as-string` | rcp-fe-lol-champ-select |
| `/lol-gameflow/v1/session/per-position-summoner-spells/required/as-string` | rcp-fe-lol-champ-select |
| `/lol-gameflow/v1/tft-client-state` | rcp-fe-lol-navigation |

### `/lol-honor-v2/v1`（15）

| 路由 | 声明插件 |
|---|---|
| `/lol-honor-v2/v1/ack-honor-notification/` | rcp-fe-lol-navigation |
| `/lol-honor-v2/v1/ballot` | rcp-fe-lol-postgame |
| `/lol-honor-v2/v1/ballot/refresh` | rcp-fe-lol-postgame |
| `/lol-honor-v2/v1/config` | rcp-fe-lol-navigation, rcp-fe-lol-postgame |
| `/lol-honor-v2/v1/late-recognition` | rcp-fe-lol-navigation |
| `/lol-honor-v2/v1/latest-eligible-game` | rcp-fe-lol-postgame |
| `/lol-honor-v2/v1/level-change-notifications` | rcp-fe-lol-navigation |
| `/lol-honor-v2/v1/level-change/ack` | rcp-fe-lol-navigation |
| `/lol-honor-v2/v1/profile` | rcp-fe-lol-postgame |
| `/lol-honor-v2/v1/recognition` | rcp-fe-lol-postgame |
| `/lol-honor-v2/v1/recognition-history` | rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-honor-v2/v1/reward-granted` | rcp-fe-lol-navigation |
| `/lol-honor-v2/v1/reward-granted/ack` | rcp-fe-lol-navigation |
| `/lol-honor-v2/v1/team-choices` | rcp-fe-lol-postgame |
| `/lol-honor-v2/v1/vote-completion` | rcp-fe-lol-postgame |

### `/lol-honor/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-honor/v1/ballot` | rcp-fe-lol-postgame |
| `/lol-honor/v1/honor` | rcp-fe-lol-postgame |

### `/lol-hovercard/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-hovercard/v1/friend-info/${e}` | rcp-fe-lol-social |

### `/lol-inventory/v1`（9）

| 路由 | 声明插件 |
|---|---|
| `/lol-inventory/v1/inventory/emotes` | rcp-fe-lol-loot |
| `/lol-inventory/v1/notifications/${e}` | rcp-fe-lol-navigation |
| `/lol-inventory/v1/notifications/${t}` | rcp-fe-lol-navigation |
| `/lol-inventory/v1/wallet/${e}` | rcp-fe-lol-champ-select, rcp-fe-lol-loot |
| `/lol-inventory/v1/wallet/${t}/notifications` | rcp-fe-lol-postgame |
| `/lol-inventory/v1/wallet/RP` | rcp-fe-lol-champ-select |
| `/lol-inventory/v1/wallet/lol_blue_essence` | rcp-fe-lol-loot |
| `/lol-inventory/v1/wallet/mgs_opal_shield` | rcp-fe-lol-navigation |
| `/lol-inventory/v1/wallet/tft_star_fragments` | rcp-fe-lol-navigation |

### `/lol-inventory/v2`（8）

| 路由 | 声明插件 |
|---|---|
| `/lol-inventory/v2/inventory/${e}` | rcp-fe-lol-champ-select, rcp-fe-lol-loot, rcp-fe-lol-navigation |
| `/lol-inventory/v2/inventory/${i.INVENTORY_TYPES.RUNE_SLOT}` | rcp-fe-lol-champ-select |
| `/lol-inventory/v2/inventory/${t}` | rcp-fe-lol-navigation |
| `/lol-inventory/v2/inventory/CHAMPION` | rcp-fe-lol-champ-select |
| `/lol-inventory/v2/inventory/CHAMPION_SKIN` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-inventory/v2/inventory/NEXUS_FINISHER` | rcp-fe-lol-loot |
| `/lol-inventory/v2/inventory/SUMMONER_ICON` | rcp-fe-lol-loot |
| `/lol-inventory/v2/inventory/WARD_SKIN` | rcp-fe-lol-champ-select, rcp-fe-lol-loot, rcp-fe-lol-parties |

### `/lol-jade-dev/v1`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-jade-dev/v1/force-error` | rcp-fe-lol-champ-select |
| `/lol-jade-dev/v1/force-shop-error` | rcp-fe-lol-champ-select |
| `/lol-jade-dev/v1/force-voting-error` | rcp-fe-lol-champ-select |

### `/lol-jade/audio`（16）

| 路由 | 声明插件 |
|---|---|
| `/lol-jade/audio/` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-buttonlocked.ogg` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-buttonpress-1.ogg` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-champselect-banchampion.ogg` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-champselect-browse-skins.ogg` | rcp-fe-lol-parties |
| `/lol-jade/audio/sfx-ui-jade-champselect-yourturn.ogg` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-chat-message-receive.ogg` | rcp-fe-lol-social |
| `/lol-jade/audio/sfx-ui-jade-join-chat.ogg` | rcp-fe-lol-social |
| `/lol-jade/audio/sfx-ui-jade-leave-chat.ogg` | rcp-fe-lol-social |
| `/lol-jade/audio/sfx-ui-jade-masteries-addpoint.ogg` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-masteries-removepoint.ogg` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-match-accept.ogg` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation |
| `/lol-jade/audio/sfx-ui-jade-match-decline.ogg` | rcp-fe-lol-navigation |
| `/lol-jade/audio/sfx-ui-jade-openstore.ogg` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-runes-runedrop-1.ogg` | rcp-fe-lol-champ-select |
| `/lol-jade/audio/sfx-ui-jade-runes-runedrop-2.ogg` | rcp-fe-lol-champ-select |

### `/lol-jade/images`（59）

| 路由 | 声明插件 |
|---|---|
| `/lol-jade/images/battlepass/achievement-title.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/battle-pass-upgraded-crown.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/battlepass-image.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/classic-coin-bonus.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/crown-blue.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/crown-muted.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/crown.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/currency-blue-essence.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/frame-corner-left.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/frame-corner-right.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/lock-closed.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/premium-pass-icon.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/battlepass/voting-power-reward.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/champion-select/champion-banned.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/champion-select/filter-bot.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/champion-select/filter-jungle.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/champion-select/filter-mid.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/champion-select/filter-support.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/champion-select/filter-top.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/champion-select/jade-free-to-play-lock.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/collections/portrait-locked-icon.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/collections/role_${e.toLowerCase` | rcp-fe-lol-champ-select |
| `/lol-jade/images/ftux/ftux-col-equip.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/ftux/ftux-col-progression.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/ftux/ftux-col-unlock.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/ftux/ftux-home-col-champions.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/ftux/ftux-home-col-gameplay.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/ftux/ftux-home-col-loadouts.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/home-nav-dark-default.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/home-nav-dark-disabled.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/home-nav-dark-hover.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/home-nav-dark-selected.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/home-play-btn-default.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/home-play-btn-disabled.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/home-play-btn-hover.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/jade-home-top-nav-bg-dark.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-home/the_council.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/checkmark.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/eat-close-gold.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/sort-arrow-down.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/sort-arrow-up.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/sort-check.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/sort-dropdown-caret.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/tooltip-caret.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/voting-system-external-link.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/voting-system-info.svg` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-uikit/voting_system_overlay_checkmark.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade-warning.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/jade_valor.png` | rcp-fe-lol-parties |
| `/lol-jade/images/mastery_banner_` | rcp-fe-lol-champ-select |
| `/lol-jade/images/postgame/scoreboard-header-separator-bullet.svg` | rcp-fe-lol-postgame |
| `/lol-jade/images/rune_page_arrow.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/store/classic_akali_bundle.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/store/classic_pass_image.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/store/classic_shen_bundle.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/store/classic_welcome_bundle.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/store/kennen_bundle.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/store/legendary_welcome_bundle.png` | rcp-fe-lol-champ-select |
| `/lol-jade/images/store/welcome_bundle_champions.png` | rcp-fe-lol-champ-select |

### `/lol-jade/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-jade/trans.json` | rcp-fe-lol-champ-select, rcp-fe-lol-collections, rcp-fe-lol-parties, rcp-fe-lol-postgame |

### `/lol-jade/video`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-jade/video/spells/spells_` | rcp-fe-lol-champ-select |

### `/lol-jade/videos`（9）

| 路由 | 声明插件 |
|---|---|
| `/lol-jade/videos/battlepass/` | rcp-fe-lol-champ-select |
| `/lol-jade/videos/champselect/CS_ActiveArrow_Flash.webm` | rcp-fe-lol-champ-select |
| `/lol-jade/videos/champselect/CS_ActiveArrow_Sheen.webm` | rcp-fe-lol-champ-select |
| `/lol-jade/videos/champselect/CS_ActivePlayerRectangle_Sheen.webm` | rcp-fe-lol-champ-select |
| `/lol-jade/videos/champselect/CS_SelfOrangeRectangleArrows.webm` | rcp-fe-lol-champ-select |
| `/lol-jade/videos/runes/Classic_BestRuneFX_BLUE.webm` | rcp-fe-lol-champ-select |
| `/lol-jade/videos/runes/Classic_BestRuneFX_PURPLE.webm` | rcp-fe-lol-champ-select |
| `/lol-jade/videos/runes/Classic_BestRuneFX_RED.webm` | rcp-fe-lol-champ-select |
| `/lol-jade/videos/runes/Classic_BestRuneFX_YELLOW.webm` | rcp-fe-lol-champ-select |

### `/lol-kiwi-hub/audio`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-kiwi-hub/audio/sfx-k3-band-unlock-group${this.get` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/audio/sfx-k3-superstar-rank-up.ogg` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/audio/sfx-uikit-button-arrowfwd-click.ogg` | rcp-fe-lol-kiwi-hub |

### `/lol-kiwi-hub/images`（5）

| 路由 | 声明插件 |
|---|---|
| `/lol-kiwi-hub/images/Emote_Type_Icon.png` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/images/Hat_Type_Icon.png` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/images/Unlocked_Icon.png` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/images/temp_Augment_Icon.png` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/images/temp_Exp_Icon.png` | rcp-fe-lol-kiwi-hub |

### `/lol-kiwi-hub/mograph`（6）

| 路由 | 声明插件 |
|---|---|
| `/lol-kiwi-hub/mograph/Group_Objective_Card_Hoverable.webm` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/mograph/Main_Progress_Track_Rank_Up.webm` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/mograph/Summary_Page_Gift_Icon_Glow.webm` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/mograph/Summary_Page_Group_Claimable_Rewards.webm` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/mograph/Summary_Page_Main_Progress_Track_Fill.webm` | rcp-fe-lol-kiwi-hub |
| `/lol-kiwi-hub/mograph/Summary_Page_Unlock_Group` | rcp-fe-lol-kiwi-hub |

### `/lol-kiwi-hub/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-kiwi-hub/trans.json` | rcp-fe-lol-kiwi-hub, rcp-fe-lol-parties |

### `/lol-kr-shutdown-law/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-kr-shutdown-law/v1/is-enabled` | rcp-fe-lol-navigation |
| `/lol-kr-shutdown-law/v1/rating-screen/acknowledge` | rcp-fe-lol-navigation |

### `/lol-l10n/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-l10n/trans.json` | rcp-fe-lol-champ-select, rcp-fe-lol-collections, rcp-fe-lol-kiwi-hub, rcp-fe-lol-loot, rcp-fe-lol-match-history, rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social, rcp-fe-lol-store |

### `/lol-leaver-buster/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-leaver-buster/v1/notifications` | rcp-fe-lol-social |

### `/lol-loadouts/v4`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-loadouts/v4/loadouts/${e}` | rcp-fe-lol-champ-select |
| `/lol-loadouts/v4/loadouts/${t.id}` | rcp-fe-lol-champ-select |
| `/lol-loadouts/v4/loadouts/${t}` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-loadouts/v4/loadouts/scope/account` | rcp-fe-lol-champ-select, rcp-fe-lol-collections, rcp-fe-lol-parties |

### `/lol-lobby-team-builder/champ-select`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-lobby-team-builder/champ-select` | rcp-fe-lol-champ-select |
| `/lol-lobby-team-builder/champ-select/v1/crowd-favorite-champion-list` | rcp-fe-lol-champ-select |
| `/lol-lobby-team-builder/champ-select/v1/session/quit` | rcp-fe-lol-champ-select |
| `/lol-lobby-team-builder/champ-select/v1/subset-champion-list` | rcp-fe-lol-champ-select |

### `/lol-lobby/${s}`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-lobby/${s}` | rcp-fe-lol-parties |

### `/lol-lobby/${t}`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-lobby/${t}` | rcp-fe-lol-parties |

### `/lol-lobby/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-lobby/v1/last-queued-lobby` | rcp-fe-lol-postgame |
| `/lol-lobby/v1/lobby/custom/cancel-champ-select` | rcp-fe-lol-champ-select |

### `/lol-lobby/v2`（11）

| 路由 | 声明插件 |
|---|---|
| `/lol-lobby/v2/ags/${e}/joinCode` | rcp-fe-lol-parties |
| `/lol-lobby/v2/ags/agsActivityId` | rcp-fe-lol-parties |
| `/lol-lobby/v2/comms/members` | rcp-fe-lol-champ-select |
| `/lol-lobby/v2/lobby` | rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-lobby/v2/lobby/matchmaking/search` | rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-lobby/v2/party/${t}/join` | rcp-fe-lol-social |
| `/lol-lobby/v2/party/eog-status` | rcp-fe-lol-postgame |
| `/lol-lobby/v2/play-again` | rcp-fe-lol-navigation |
| `/lol-lobby/v2/received-invitations` | rcp-fe-lol-social |
| `/lol-lobby/v2/received-invitations/` | rcp-fe-lol-social |
| `/lol-lobby/v2/received-invitations/${t}/accept` | rcp-fe-lol-parties |

### `/lol-lock-and-load/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-lock-and-load/v1/should-show-progress-bar-text` | rcp-fe-lol-navigation |

### `/lol-login/v1`（6）

| 路由 | 声明插件 |
|---|---|
| `/lol-login/v1` | rcp-fe-lol-navigation, rcp-fe-lol-social |
| `/lol-login/v1/account-state` | rcp-fe-lol-store |
| `/lol-login/v1/delete-rso-on-close` | rcp-fe-lol-navigation |
| `/lol-login/v1/login-connection-state` | rcp-fe-lol-navigation |
| `/lol-login/v1/login-queue-state` | rcp-fe-lol-navigation |
| `/lol-login/v1/session` | rcp-fe-lol-champ-select, rcp-fe-lol-collections, rcp-fe-lol-loot, rcp-fe-lol-match-history, rcp-fe-lol-navigation, rcp-fe-lol-social |

### `/lol-loot/assets`（8）

| 路由 | 声明插件 |
|---|---|
| `/lol-loot/assets/border_images/frame.svg` | rcp-fe-lol-loot |
| `/lol-loot/assets/loot_item_icons/currency_champion.png` | rcp-fe-lol-loot |
| `/lol-loot/assets/loot_item_icons/material_key.png` | rcp-fe-lol-navigation |
| `/lol-loot/assets/the_sanctum/me_update.png` | rcp-fe-lol-loot |
| `/lol-loot/assets/the_sanctum/sanctum_bg.png` | rcp-fe-lol-loot |
| `/lol-loot/assets/the_sanctum/spark_about.png` | rcp-fe-lol-loot |
| `/lol-loot/assets/tooltips/loot_odds_tooltip_icon.svg` | rcp-fe-lol-loot |
| `/lol-loot/assets/videos/low_spec_images/open_cm_image.png` | rcp-fe-lol-postgame |

### `/lol-loot/lottie`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-loot/lottie/HextechEgg.json` | rcp-fe-lol-loot |
| `/lol-loot/lottie/images/` | rcp-fe-lol-loot |
| `/lol-loot/lottie/videos/HexEgg_VFX_` | rcp-fe-lol-loot |
| `/lol-loot/lottie/videos/HexEgg_VFX_Loop.webm` | rcp-fe-lol-loot |

### `/lol-loot/v1`（12）

| 路由 | 声明插件 |
|---|---|
| `/lol-loot/v1/currency-configuration` | rcp-fe-lol-loot |
| `/lol-loot/v1/loot-grants` | rcp-fe-lol-postgame |
| `/lol-loot/v1/milestones/${e}/claimProgress` | rcp-fe-lol-loot |
| `/lol-loot/v1/milestones/${e}/counter` | rcp-fe-lol-loot |
| `/lol-loot/v1/milestones/items` | rcp-fe-lol-loot |
| `/lol-loot/v1/player-loot-map` | rcp-fe-lol-loot |
| `/lol-loot/v1/player-loot-notifications` | rcp-fe-lol-navigation |
| `/lol-loot/v1/player-loot-notifications/${e}/acknowledge` | rcp-fe-lol-navigation |
| `/lol-loot/v1/player-loot/MATERIAL_key_fragment` | rcp-fe-lol-navigation |
| `/lol-loot/v1/player-loot/{{lootId}}` | rcp-fe-lol-navigation |
| `/lol-loot/v1/recipes/configuration` | rcp-fe-lol-loot |
| `/lol-loot/v1/refresh` | rcp-fe-lol-loot |

### `/lol-loyalty/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-loyalty/v1/status-notification` | rcp-fe-lol-navigation |

### `/lol-mac-graphics-upgrade/needs-hardware-upgrade`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-mac-graphics-upgrade/needs-hardware-upgrade` | rcp-fe-lol-social |

### `/lol-mac-graphics-upgrade/notification-type`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-mac-graphics-upgrade/notification-type` | rcp-fe-lol-navigation |

### `/lol-maps/v2`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-maps/v2/map/22/tft` | rcp-fe-lol-navigation |
| `/lol-maps/v2/maps` | rcp-fe-lol-navigation, rcp-fe-lol-social |

### `/lol-match-history/images`（8）

| 路由 | 声明插件 |
|---|---|
| `/lol-match-history/images/subteams/gromp.svg` | rcp-fe-lol-match-history |
| `/lol-match-history/images/subteams/krug.svg` | rcp-fe-lol-match-history |
| `/lol-match-history/images/subteams/minion.svg` | rcp-fe-lol-match-history |
| `/lol-match-history/images/subteams/poro.svg` | rcp-fe-lol-match-history |
| `/lol-match-history/images/subteams/raptor.svg` | rcp-fe-lol-match-history |
| `/lol-match-history/images/subteams/scuttle.svg` | rcp-fe-lol-match-history |
| `/lol-match-history/images/subteams/sentinel.svg` | rcp-fe-lol-match-history |
| `/lol-match-history/images/subteams/wolf.svg` | rcp-fe-lol-match-history |

### `/lol-match-history/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-match-history/trans.json` | rcp-fe-lol-match-history |

### `/lol-match-history/v1`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-match-history/v1/games/{{baseGameId}}` | rcp-fe-lol-match-history |
| `/lol-match-history/v1/products/lol/current-summoner/matches` | rcp-fe-lol-parties |
| `/lol-match-history/v1/recently-played-summoners` | rcp-fe-lol-parties, rcp-fe-lol-social |

### `/lol-matchmaking/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-matchmaking/v1/ready-check` | rcp-fe-lol-navigation, rcp-fe-lol-parties |
| `/lol-matchmaking/v1/search` | rcp-fe-lol-parties, rcp-fe-lol-social |

### `/lol-missions/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-missions/v1/missions` | rcp-fe-lol-kiwi-hub, rcp-fe-lol-navigation |

### `/lol-navigation/AllIcon.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/AllIcon.svg` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_Default.jpg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_Default.jpg` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_ExplodeLarge_Magic.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_ExplodeLarge_Magic.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_ExplodeMedium_Magic.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_ExplodeMedium_Magic.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_ExplodeSmall_Magic.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_ExplodeSmall_Magic.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_Intro_Magic.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_Intro_Magic.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_Looping_BGMagic.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_Looping_BGMagic.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_BlueEssence.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_BlueEssence.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_Champion.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_Champion.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_Chest.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_Chest.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_IP.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_IP.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_IPBoost.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_IPBoost.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_Key.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_Key.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_Material.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_Material.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_Mission.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_Mission.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_OrangeEssence.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_OrangeEssence.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_Portal.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_Portal.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_PortalKey.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_PortalKey.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_Skin.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_Skin.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_SummonerIcon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_SummonerIcon.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_MissionReward_Ward.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_MissionReward_Ward.png` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_1280x330.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_1280x330.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_1280x445.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_1280x445.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_515x330.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_515x330.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_515x445.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_515x445.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_900x330.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_900x330.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_900x445.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_900x445.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_Large.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_Large.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_Medium.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_Medium.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/EoG_RewardOutro_Magic_Small.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/EoG_RewardOutro_Magic_Small.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/LevelUpExplosion_Large.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/LevelUpExplosion_Large.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/LevelUpExplosion_Medium.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/LevelUpExplosion_Medium.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/LevelUpExplosion_Small.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/LevelUpExplosion_Small.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/LevelUpExplosion_XLarge.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/LevelUpExplosion_XLarge.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/MissionIcon.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/MissionIcon.svg` | rcp-fe-lol-navigation |

### `/lol-navigation/Progression_Placeholder.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Progression_Placeholder.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_BlueEssence.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_BlueEssence.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Bundle.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Bundle.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Champ.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Champ.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Chest.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Chest.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Chest_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Chest_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_IP.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_IP.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_IPBoost.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_IPBoost.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Key.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Key.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Material.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Material.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Mission.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Mission.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_OrangeEssence.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_OrangeEssence.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Portal.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Portal.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_PortalKey.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_PortalKey.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Skin.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Skin.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_SummonerIcon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_SummonerIcon.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Tickets.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Tickets.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_Ward.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_Ward.png` | rcp-fe-lol-navigation |

### `/lol-navigation/RewardIcons_XP.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/RewardIcons_XP.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_BlueEssence.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_BlueEssence.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_BlueEssence_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_BlueEssence_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Champion.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Champion.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Champion_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Champion_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Chest.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Chest.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_IP.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_IP.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_IPBoost.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_IPBoost.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_IPBoost_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_IPBoost_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_IP_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_IP_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Key.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Key.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Key_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Key_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Material.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Material.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Material_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Material_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Mission.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Mission.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Mission_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Mission_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_OrangeEssence.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_OrangeEssence.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_OrangeEssence_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_OrangeEssence_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Portal.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Portal.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_PortalKey.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_PortalKey.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_PortalKey_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_PortalKey_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Portal_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Portal_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Skin.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Skin.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Skin_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Skin_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_SummonerIcon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_SummonerIcon.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_SummonerIcon_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_SummonerIcon_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Ward.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Ward.png` | rcp-fe-lol-navigation |

### `/lol-navigation/Reward_Ward_double.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/Reward_Ward_double.png` | rcp-fe-lol-navigation |

### `/lol-navigation/activity-center`（36）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/activity-center/ac-navigation-tab/static_navigation_magic.png` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/ac-sfx-mgx-activity-center-chapter-notification.ogg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/ac-sfx-mgx-gold-flair.ogg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/ac-sfx-mgx-logo.ogg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/ac-skin-template/skin-rarity-plate-bg.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/external-link-hover.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/external-link-rest.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/game-toggle-glow.png` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/header-logo-partical-magic.webm` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/header-title-partical-magic.webm` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/league-logo-active.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/league-logo-hover.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/league-logo-rest.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/mel_masked_v2.png` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/navigation_magic_hover.webm` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/navigation_magic_idle.webm` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/npe-beginners-journey.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/npe-daily-play-rewards.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/npe-v2` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/npe-v2/npe-v2-beginners-journey-background.png` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/npe-v2/npe-v2-beginners-journey-tab-thumbnail.png` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/npe-v2/npe_missions_background.png` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/npe-v2/npe_starterbundle2.png` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/owned_checkmark.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/season-pass/pass-banner-shroud.png` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/test_icon_1.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/tft-logo-active.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/tft-logo-hover.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/tft-logo-rest.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/thematic-timeline-node-magic.webm` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/thematic-timeline/future-hover.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/thematic-timeline/future-rest.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/thematic-timeline/present-past-hover.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/thematic-timeline/present-past-pressed.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/thematic-timeline/present-past-rest.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/activity-center/thematic-timeline/thematic-timeline-node-magic-ls.png` | rcp-fe-lol-navigation |

### `/lol-navigation/add-blue-essence.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/add-blue-essence.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/celebration-chest.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/celebration-chest.png` | rcp-fe-lol-navigation |

### `/lol-navigation/event-hub`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/event-hub/passes.svg` | rcp-fe-lol-navigation |

### `/lol-navigation/images`（7）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/images/braum-header.png` | rcp-fe-lol-navigation |
| `/lol-navigation/images/checkmark.png` | rcp-fe-lol-navigation |
| `/lol-navigation/images/full-launch-announcement-separator.png` | rcp-fe-lol-navigation |
| `/lol-navigation/images/hashes.png` | rcp-fe-lol-navigation |
| `/lol-navigation/images/tft-tftu-modal-1.png` | rcp-fe-lol-navigation |
| `/lol-navigation/images/tft-tftu-modal-2.png` | rcp-fe-lol-navigation |
| `/lol-navigation/images/tft-tftu-modal-3.png` | rcp-fe-lol-navigation |

### `/lol-navigation/metagames`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/metagames/o-crest-divider.png` | rcp-fe-lol-navigation |
| `/lol-navigation/metagames/o-minimized-icon.svg` | rcp-fe-lol-navigation |
| `/lol-navigation/metagames/o-ssc.svg` | rcp-fe-lol-navigation |

### `/lol-navigation/new`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/new/new-mission-bottom.webm` | rcp-fe-lol-navigation |
| `/lol-navigation/new/new-mission-top.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/remove-blue-essence.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/remove-blue-essence.webm` | rcp-fe-lol-navigation |

### `/lol-navigation/sfx-receive-blue-essence.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/sfx-receive-blue-essence.ogg` | rcp-fe-lol-navigation |

### `/lol-navigation/sfx-servicestatus-button-click-off.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/sfx-servicestatus-button-click-off.ogg` | rcp-fe-lol-navigation |

### `/lol-navigation/sfx-servicestatus-button-click-on.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/sfx-servicestatus-button-click-on.ogg` | rcp-fe-lol-navigation |

### `/lol-navigation/sfx-soc-ui-click-generic.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/sfx-soc-ui-click-generic.ogg` | rcp-fe-lol-navigation |

### `/lol-navigation/trans-activity-center.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/trans-activity-center.json` | rcp-fe-lol-navigation, rcp-fe-lol-postgame |

### `/lol-navigation/trans-app-controls.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/trans-app-controls.json` | rcp-fe-lol-navigation |

### `/lol-navigation/trans-champion-mastery-notifications.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/trans-champion-mastery-notifications.json` | rcp-fe-lol-navigation |

### `/lol-navigation/trans-email-verification.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/trans-email-verification.json` | rcp-fe-lol-navigation |

### `/lol-navigation/trans-game-in-progress.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/trans-game-in-progress.json` | rcp-fe-lol-navigation |

### `/lol-navigation/trans-loading-screen.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/trans-loading-screen.json` | rcp-fe-lol-navigation |

### `/lol-navigation/trans-npe-first-touch.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/trans-npe-first-touch.json` | rcp-fe-lol-navigation |

### `/lol-navigation/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-navigation/trans.json` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation, rcp-fe-lol-parties |

### `/lol-npe-rewards/v1`（5）

| 路由 | 声明插件 |
|---|---|
| `/lol-npe-rewards/v1/challenges/opt` | rcp-fe-lol-navigation |
| `/lol-npe-rewards/v1/challenges/progress` | rcp-fe-lol-navigation |
| `/lol-npe-rewards/v1/get_poro_experiments` | rcp-fe-lol-parties |
| `/lol-npe-rewards/v1/level-rewards` | rcp-fe-lol-navigation |
| `/lol-npe-rewards/v1/login-rewards` | rcp-fe-lol-navigation |

### `/lol-npe-tutorial-path/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-npe-tutorial-path/v1/settings` | rcp-fe-lol-navigation |

### `/lol-objectives/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-objectives/trans.json` | rcp-fe-lol-navigation |

### `/lol-objectives/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-objectives/v1` | rcp-fe-lol-navigation |
| `/lol-objectives/v1/missions-by-tag` | rcp-fe-lol-navigation, rcp-fe-lol-postgame |

### `/lol-parental-controls/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parental-controls/v1/status` | rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |

### `/lol-parties-cosmetics-panel/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-cosmetics-panel/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-cosmetics-picker/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-cosmetics-picker/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-cosmetics-renderer/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-cosmetics-renderer/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-cosmetics-upgrade-celebration/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-cosmetics-upgrade-celebration/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-game-info-panel/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-game-info-panel/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-game-invite/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-game-invite/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-game-invites/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-game-invites/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-game-search/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-game-search/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-loadouts-purchase-dialog-content/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-loadouts-purchase-dialog-content/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-series-button/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-series-button/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties-status-card/component-style.styl`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties-status-card/component-style.styl` | rcp-fe-lol-parties |

### `/lol-parties/Compensation.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/Compensation.png` | rcp-fe-lol-parties |

### `/lol-parties/Loyalty.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/Loyalty.png` | rcp-fe-lol-parties |

### `/lol-parties/WrenchFist_Disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/WrenchFist_Disabled.png` | rcp-fe-lol-parties |

### `/lol-parties/banner-invite-click.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/banner-invite-click.png` | rcp-fe-lol-parties |

### `/lol-parties/banner-invite-hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/banner-invite-hover.png` | rcp-fe-lol-parties |

### `/lol-parties/banner-invite.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/banner-invite.png` | rcp-fe-lol-parties |

### `/lol-parties/battletraining-active-video.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/battletraining-active-video.webm` | rcp-fe-lol-parties |

### `/lol-parties/battletraining-active.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/battletraining-active.png` | rcp-fe-lol-parties |

### `/lol-parties/battletraining-default.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/battletraining-default.png` | rcp-fe-lol-parties |

### `/lol-parties/battletraining-disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/battletraining-disabled.png` | rcp-fe-lol-parties |

### `/lol-parties/battletraining-hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/battletraining-hover.png` | rcp-fe-lol-parties |

### `/lol-parties/battletraining-icon-victory.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/battletraining-icon-victory.png` | rcp-fe-lol-parties |

### `/lol-parties/battletraining-intro-video.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/battletraining-intro-video.webm` | rcp-fe-lol-parties |

### `/lol-parties/bluebutton_checkmark.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/bluebutton_checkmark.png` | rcp-fe-lol-parties |

### `/lol-parties/button-back-arrow-disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-back-arrow-disabled.png` | rcp-fe-lol-parties |

### `/lol-parties/button-back-arrow-down.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-back-arrow-down.png` | rcp-fe-lol-parties |

### `/lol-parties/button-back-arrow-over.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-back-arrow-over.png` | rcp-fe-lol-parties |

### `/lol-parties/button-back-arrow.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-back-arrow.png` | rcp-fe-lol-parties |

### `/lol-parties/button-bg-disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-bg-disabled.png` | rcp-fe-lol-parties, rcp-fe-lol-postgame |

### `/lol-parties/button-bg-down.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-bg-down.png` | rcp-fe-lol-parties, rcp-fe-lol-postgame |

### `/lol-parties/button-bg-over.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-bg-over.png` | rcp-fe-lol-parties, rcp-fe-lol-postgame |

### `/lol-parties/button-bg.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-bg.png` | rcp-fe-lol-parties, rcp-fe-lol-postgame |

### `/lol-parties/button-find-match-disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-find-match-disabled.png` | rcp-fe-lol-parties |

### `/lol-parties/button-find-match-down.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-find-match-down.png` | rcp-fe-lol-parties |

### `/lol-parties/button-find-match-over.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-find-match-over.png` | rcp-fe-lol-parties |

### `/lol-parties/button-find-match.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-find-match.png` | rcp-fe-lol-parties |

### `/lol-parties/button-x-down.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-x-down.png` | rcp-fe-lol-parties |

### `/lol-parties/button-x-over.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-x-over.png` | rcp-fe-lol-parties |

### `/lol-parties/button-x.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/button-x.png` | rcp-fe-lol-parties |

### `/lol-parties/clear_mask.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/clear_mask.png` | rcp-fe-lol-parties |

### `/lol-parties/common_plating.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/common_plating.png` | rcp-fe-lol-parties |

### `/lol-parties/common_plating_hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/common_plating_hover.png` | rcp-fe-lol-parties |

### `/lol-parties/custom-team-position-warning.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/custom-team-position-warning.png` | rcp-fe-lol-parties |

### `/lol-parties/custom-team-warning.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/custom-team-warning.png` | rcp-fe-lol-parties |

### `/lol-parties/demacia_exclamation.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/demacia_exclamation.svg` | rcp-fe-lol-parties |

### `/lol-parties/epic_plating.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/epic_plating.png` | rcp-fe-lol-parties |

### `/lol-parties/epic_plating_hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/epic_plating_hover.png` | rcp-fe-lol-parties |

### `/lol-parties/favorites-heart.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/favorites-heart.png` | rcp-fe-lol-parties |

### `/lol-parties/friend-default-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/friend-default-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/friend-hover-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/friend-hover-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/friend-tooltip-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/friend-tooltip-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/icon-crown.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-crown.png` | rcp-fe-lol-parties |

### `/lol-parties/icon-position-`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-position-` | rcp-fe-lol-postgame |

### `/lol-parties/icon-position-${n.toLowerCase`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-position-${n.toLowerCase` | rcp-fe-lol-parties |

### `/lol-parties/icon-position-banner-${this.get`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-position-banner-${this.get` | rcp-fe-lol-parties |

### `/lol-parties/icon-position-button-`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-position-button-` | rcp-fe-lol-parties |

### `/lol-parties/icon-position-button-${`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-position-button-${` | rcp-fe-lol-parties |

### `/lol-parties/icon-position-button-${e.toLowerCase`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-position-button-${e.toLowerCase` | rcp-fe-lol-parties |

### `/lol-parties/icon-position-button-${e}.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-position-button-${e}.png` | rcp-fe-lol-parties |

### `/lol-parties/icon-warning-yellow.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon-warning-yellow.svg` | rcp-fe-lol-parties |

### `/lol-parties/icon_warning_mask.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/icon_warning_mask.png` | rcp-fe-lol-parties |

### `/lol-parties/legendary_plating.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/legendary_plating.png` | rcp-fe-lol-parties |

### `/lol-parties/legendary_plating_hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/legendary_plating_hover.png` | rcp-fe-lol-parties |

### `/lol-parties/lock.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/lock.png` | rcp-fe-lol-parties |

### `/lol-parties/lock_hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/lock_hover.png` | rcp-fe-lol-parties |

### `/lol-parties/lottie`（15）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/lottie/LLUpgradeCeremony_01Scene/LLUpgradeCeremony_Scene_IN.json` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeCeremony_01Scene/LLUpgradeCeremony_Scene_LOOP.json` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeCeremony_01Scene/images/` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeCeremony_02BGParticles/videos/LLUpgradeCeremony_ParticlesBG_IN.webm` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeCeremony_02BGParticles/videos/LLUpgradeCeremony_ParticlesBG_LOOP.webm` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeCeremony_04FGLightning/videos/LLUpgradeCeremony_BoltsFG.webm` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeCeremony_05RarityBolts/videos/LLUpgradeCeremony_RarityBolts.webm` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeCeremony_06LevelStarIcons/LLUpgradeCeremony_StarsLv${t.level}.json` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeCeremony_06LevelStarIcons/images/` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeConfirm_StarIcons/LLUpgradeConfirm_Lv${this._data.level}.json` | rcp-fe-lol-parties |
| `/lol-parties/lottie/LLUpgradeConfirm_StarIcons/images/` | rcp-fe-lol-parties |
| `/lol-parties/lottie/parties-v2/intro-banner.json` | rcp-fe-lol-parties |
| `/lol-parties/lottie/parties-v2/intro-party.json` | rcp-fe-lol-parties |
| `/lol-parties/lottie/parties-v2/intro-role-selector-button.json` | rcp-fe-lol-parties |
| `/lol-parties/lottie/parties-v2/intro-role-selector.json` | rcp-fe-lol-parties |

### `/lol-parties/map_21.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/map_21.png` | rcp-fe-lol-parties |

### `/lol-parties/map_ha.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/map_ha.png` | rcp-fe-lol-parties, rcp-fe-lol-social |

### `/lol-parties/map_rgm.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/map_rgm.png` | rcp-fe-lol-parties, rcp-fe-lol-social |

### `/lol-parties/map_sr.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/map_sr.png` | rcp-fe-lol-parties, rcp-fe-lol-social |

### `/lol-parties/map_tft.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/map_tft.png` | rcp-fe-lol-parties |

### `/lol-parties/map_tt.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/map_tt.png` | rcp-fe-lol-parties, rcp-fe-lol-social |

### `/lol-parties/mythic_plating.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/mythic_plating.png` | rcp-fe-lol-parties |

### `/lol-parties/mythic_plating_hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/mythic_plating_hover.png` | rcp-fe-lol-parties |

### `/lol-parties/parties-v2`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/parties-v2/invited-banner.webm` | rcp-fe-lol-parties |
| `/lol-parties/parties-v2/outro-role-selector-magic.webm` | rcp-fe-lol-parties |
| `/lol-parties/parties-v2/outro-role-selector.webm` | rcp-fe-lol-parties |

### `/lol-parties/party_panel_header_icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/party_panel_header_icon.png` | rcp-fe-lol-parties |

### `/lol-parties/player_empty.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/player_empty.png` | rcp-fe-lol-parties |

### `/lol-parties/player_joined.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/player_joined.png` | rcp-fe-lol-parties |

### `/lol-parties/practicetool-active-video.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/practicetool-active-video.webm` | rcp-fe-lol-parties |

### `/lol-parties/practicetool-active.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/practicetool-active.png` | rcp-fe-lol-parties |

### `/lol-parties/practicetool-default.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/practicetool-default.png` | rcp-fe-lol-parties |

### `/lol-parties/practicetool-disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/practicetool-disabled.png` | rcp-fe-lol-parties |

### `/lol-parties/practicetool-hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/practicetool-hover.png` | rcp-fe-lol-parties |

### `/lol-parties/practicetool-icon-victory.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/practicetool-icon-victory.png` | rcp-fe-lol-parties |

### `/lol-parties/practicetool-intro-video.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/practicetool-intro-video.webm` | rcp-fe-lol-parties |

### `/lol-parties/recently-honored-default-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/recently-honored-default-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/recently-honored-hover-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/recently-honored-hover-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/recently-honored-tooltip-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/recently-honored-tooltip-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/recently-played-default-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/recently-played-default-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/recently-played-hover-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/recently-played-hover-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/recently-played-tooltip-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/recently-played-tooltip-icon.png` | rcp-fe-lol-parties |

### `/lol-parties/sfx-celebrate-upgrade-littlelegend.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-celebrate-upgrade-littlelegend.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-gameselect-button-confirm-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-gameselect-button-confirm-click.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-gameselect-button-confirm-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-gameselect-button-confirm-hover.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-gameselect-button-map-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-gameselect-button-map-click.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-gameselect-trans-to-lobby.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-gameselect-trans-to-lobby.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-banner-intro-flare.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-banner-intro-flare.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-button-banner-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-button-banner-click.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-button-find-match-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-button-find-match-click.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-button-find-match-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-button-find-match-hover.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-button-quit-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-button-quit-click.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-button-quit-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-button-quit-hover.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-notif-bad-invite.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-notif-bad-invite.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-notif-captain-granted.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-notif-captain-granted.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-notif-invite-granted.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-notif-invite-granted.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-notif-revoke.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-notif-revoke.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-lobby-trans-to-gameselect.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-lobby-trans-to-gameselect.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-login-notif-login-fail.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-login-notif-login-fail.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-parties-button-toggle.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-parties-button-toggle.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-parties-trans-change-game-type.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-parties-trans-change-game-type.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-radialmenu-open.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-radialmenu-open.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-radialmenu-role-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-radialmenu-role-hover.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-radialmenu-role-primary.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-radialmenu-role-primary.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-radialmenu-role-secondary.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-radialmenu-role-secondary.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-radialmenu-role-select.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-radialmenu-role-select.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-soc-ui-click-generic.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-soc-ui-click-generic.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-soc-ui-drag.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-soc-ui-drag.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-soc-ui-drop.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-soc-ui-drop.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-soc-ui-gameinvite-accept-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-soc-ui-gameinvite-accept-click.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-starshards-button-release.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-starshards-button-release.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-starshards-success.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-starshards-success.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-uikit-button-circlegold-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-uikit-button-circlegold-hover.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-uikit-button-locked-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-uikit-button-locked-click.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-uikit-grid-big-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-uikit-grid-big-hover.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-uikit-grid-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-uikit-grid-click.ogg` | rcp-fe-lol-parties |

### `/lol-parties/sfx-uikit-grid-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/sfx-uikit-grid-hover.ogg` | rcp-fe-lol-parties |

### `/lol-parties/spine`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/spine/randomizer_dice.atlas` | rcp-fe-lol-parties |
| `/lol-parties/spine/randomizer_dice.skel` | rcp-fe-lol-parties |

### `/lol-parties/tft-${e}.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/tft-${e}.svg` | rcp-fe-lol-parties |

### `/lol-parties/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/trans.json` | rcp-fe-lol-parties |

### `/lol-parties/tutorial-active-video.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/tutorial-active-video.webm` | rcp-fe-lol-parties |

### `/lol-parties/tutorial-active.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/tutorial-active.png` | rcp-fe-lol-parties |

### `/lol-parties/tutorial-default.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/tutorial-default.png` | rcp-fe-lol-parties |

### `/lol-parties/tutorial-disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/tutorial-disabled.png` | rcp-fe-lol-parties |

### `/lol-parties/tutorial-hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/tutorial-hover.png` | rcp-fe-lol-parties |

### `/lol-parties/tutorial-icon-victory.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/tutorial-icon-victory.png` | rcp-fe-lol-parties |

### `/lol-parties/tutorial-intro-video.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/tutorial-intro-video.webm` | rcp-fe-lol-parties |

### `/lol-parties/x_mask.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-parties/x_mask.png` | rcp-fe-lol-parties |

### `/lol-patch/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-patch/v1/game-version` | rcp-fe-lol-social |
| `/lol-patch/v1/product-integration/app-update/available` | rcp-fe-lol-navigation |

### `/lol-paw/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-paw/trans.json` | rcp-fe-lol-loot |

### `/lol-paw/videos`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-paw/videos/UnlockCeremony.webm` | rcp-fe-lol-champ-select |

### `/lol-perks/v1`（11）

| 路由 | 声明插件 |
|---|---|
| `/lol-perks/v1` | rcp-fe-lol-collections |
| `/lol-perks/v1/currentpage` | rcp-fe-lol-champ-select, rcp-fe-lol-collections |
| `/lol-perks/v1/pages` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-perks/v1/pages/` | rcp-fe-lol-collections |
| `/lol-perks/v1/perks` | rcp-fe-lol-champ-select |
| `/lol-perks/v1/perks/ack-gameplay-updated` | rcp-fe-lol-collections |
| `/lol-perks/v1/rune-recommender-auto-select` | rcp-fe-lol-champ-select |
| `/lol-perks/v1/settings` | rcp-fe-lol-champ-select |
| `/lol-perks/v1/styles` | rcp-fe-lol-champ-select |
| `/lol-perks/v1/update-page-order` | rcp-fe-lol-collections |
| `/lol-perks/v1/validate-page-name/${e}/${t}` | rcp-fe-lol-champ-select |

### `/lol-platform-config/v1`（28）

| 路由 | 声明插件 |
|---|---|
| `/lol-platform-config/v1/initial-configuration-complete` | rcp-fe-lol-navigation, rcp-fe-lol-postgame |
| `/lol-platform-config/v1/namespaces` | rcp-fe-lol-champ-select |
| `/lol-platform-config/v1/namespaces/Challenges` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/Challenges/ClientState` | rcp-fe-lol-parties |
| `/lol-platform-config/v1/namespaces/Challenges/DisableEogAnimations` | rcp-fe-lol-postgame |
| `/lol-platform-config/v1/namespaces/Challenges/LevelUpCelebrationEnabled` | rcp-fe-lol-navigation |
| `/lol-platform-config/v1/namespaces/Challenges/PostgameChallengesEnabled` | rcp-fe-lol-postgame |
| `/lol-platform-config/v1/namespaces/ChampionMasteryConfig` | rcp-fe-lol-postgame |
| `/lol-platform-config/v1/namespaces/ChampionMasteryConfig/PostgameChampionMasteryEnabled` | rcp-fe-lol-postgame |
| `/lol-platform-config/v1/namespaces/ChampionMasteryConfig/RequirementPerLevel` | rcp-fe-lol-navigation |
| `/lol-platform-config/v1/namespaces/Chat` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/ClashConfig` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/ClientSystemStates` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/Eternals/Enabled` | rcp-fe-lol-postgame |
| `/lol-platform-config/v1/namespaces/FriendRecommendations` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/ItemSets` | rcp-fe-lol-postgame |
| `/lol-platform-config/v1/namespaces/LcuBuddySpectate` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/LcuHovercard` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/LcuNpe` | rcp-fe-lol-navigation |
| `/lol-platform-config/v1/namespaces/LcuSocial` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/LcuTutorial` | rcp-fe-lol-navigation |
| `/lol-platform-config/v1/namespaces/LcuTutorial/CarouselChampIds` | rcp-fe-lol-navigation |
| `/lol-platform-config/v1/namespaces/LootConfig` | rcp-fe-lol-loot |
| `/lol-platform-config/v1/namespaces/Perks` | rcp-fe-lol-collections |
| `/lol-platform-config/v1/namespaces/Perks/RestrictedPageNamesEnabled` | rcp-fe-lol-champ-select |
| `/lol-platform-config/v1/namespaces/PlayerNotification` | rcp-fe-lol-social |
| `/lol-platform-config/v1/namespaces/Postgame/ShowPositionDetectionEnabled` | rcp-fe-lol-postgame |
| `/lol-platform-config/v1/namespaces/SkinsViewer` | rcp-fe-lol-parties |

### `/lol-player-behavior/v1`（6）

| 路由 | 声明插件 |
|---|---|
| `/lol-player-behavior/v1/ack-credibility-behavior-warning/` | rcp-fe-lol-social |
| `/lol-player-behavior/v1/chat-restriction` | rcp-fe-lol-social |
| `/lol-player-behavior/v1/config` | rcp-fe-lol-social |
| `/lol-player-behavior/v1/credibility-behavior-warnings` | rcp-fe-lol-social |
| `/lol-player-behavior/v1/reform-card` | rcp-fe-lol-social |
| `/lol-player-behavior/v1/reporter-feedback` | rcp-fe-lol-social |

### `/lol-player-behavior/v2`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-player-behavior/v2/reform-card` | rcp-fe-lol-social |

### `/lol-player-behavior/v3`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-player-behavior/v3/reform-cards` | rcp-fe-lol-social |

### `/lol-player-report-sender/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-player-report-sender/v1/direct-message-reports` | rcp-fe-lol-social |
| `/lol-player-report-sender/v1/game-ids-with-verbal-abuse-report` | rcp-fe-lol-postgame |

### `/lol-postgame/EOG_Scoreboard_Team_Color.webm`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/EOG_Scoreboard_Team_Color.webm` | rcp-fe-lol-postgame |

### `/lol-postgame/Honor-Emblem.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/Honor-Emblem.png` | rcp-fe-lol-postgame |

### `/lol-postgame/Honor-Highlight.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/Honor-Highlight.png` | rcp-fe-lol-postgame |

### `/lol-postgame/TFT_Teamplanner_Import_Clicked.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/TFT_Teamplanner_Import_Clicked.png` | rcp-fe-lol-postgame |

### `/lol-postgame/TFT_Teamplanner_Import_Default.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/TFT_Teamplanner_Import_Default.png` | rcp-fe-lol-postgame |

### `/lol-postgame/TFT_Teamplanner_Import_Disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/TFT_Teamplanner_Import_Disabled.png` | rcp-fe-lol-postgame |

### `/lol-postgame/TFT_Teamplanner_Import_Hover.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/TFT_Teamplanner_Import_Hover.png` | rcp-fe-lol-postgame |

### `/lol-postgame/TFT_Teamplanner_Import_Saved.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/TFT_Teamplanner_Import_Saved.png` | rcp-fe-lol-postgame |

### `/lol-postgame/add-rune-page-disabled.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/add-rune-page-disabled.png` | rcp-fe-lol-postgame |

### `/lol-postgame/add-rune-page.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/add-rune-page.png` | rcp-fe-lol-postgame |

### `/lol-postgame/button-find-match-down.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/button-find-match-down.png` | rcp-fe-lol-postgame |

### `/lol-postgame/button-find-match-over.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/button-find-match-over.png` | rcp-fe-lol-postgame |

### `/lol-postgame/button-find-match.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/button-find-match.png` | rcp-fe-lol-postgame |

### `/lol-postgame/countdown_meter.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/countdown_meter.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/countdown_meter_bg.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/countdown_meter_bg.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/eternals-icon.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/eternals-icon.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/icon-cancel.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/icon-cancel.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/icon-crystal.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/icon-crystal.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/icon-milestone.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/icon-milestone.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/icon-progressed.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/icon-progressed.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/icon-target.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/icon-target.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/images`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/images/toast-blue-essence.png` | rcp-fe-lol-postgame |

### `/lol-postgame/miniseries-progress-`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/miniseries-progress-` | rcp-fe-lol-postgame |

### `/lol-postgame/mograph`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/mograph/EOG_Honor_Received.webm` | rcp-fe-lol-postgame |
| `/lol-postgame/mograph/EOG_Honor_Trail.webm` | rcp-fe-lol-postgame |

### `/lol-postgame/scoreboard-challenge-crystal-icon.svg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/scoreboard-challenge-crystal-icon.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/sfx-nav-button-play-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/sfx-nav-button-play-click.ogg` | rcp-fe-lol-postgame |

### `/lol-postgame/sfx-nav-button-play-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/sfx-nav-button-play-hover.ogg` | rcp-fe-lol-postgame |

### `/lol-postgame/sfx-uikit-button-gold-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/sfx-uikit-button-gold-click.ogg` | rcp-fe-lol-postgame |

### `/lol-postgame/sfx-uikit-button-gold-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/sfx-uikit-button-gold-hover.ogg` | rcp-fe-lol-postgame |

### `/lol-postgame/subteams`（8）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/subteams/gromp.svg` | rcp-fe-lol-postgame |
| `/lol-postgame/subteams/krug.svg` | rcp-fe-lol-postgame |
| `/lol-postgame/subteams/minion.svg` | rcp-fe-lol-postgame |
| `/lol-postgame/subteams/poro.svg` | rcp-fe-lol-postgame |
| `/lol-postgame/subteams/raptor.svg` | rcp-fe-lol-postgame |
| `/lol-postgame/subteams/scuttle.svg` | rcp-fe-lol-postgame |
| `/lol-postgame/subteams/sentinel.svg` | rcp-fe-lol-postgame |
| `/lol-postgame/subteams/wolf.svg` | rcp-fe-lol-postgame |

### `/lol-postgame/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-postgame/trans.json` | rcp-fe-lol-navigation, rcp-fe-lol-postgame |

### `/lol-pre-end-of-game/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-pre-end-of-game/v1/currentSequenceEvent` | rcp-fe-lol-postgame |
| `/lol-pre-end-of-game/v1/skip-pre-end-of-game` | rcp-fe-lol-postgame |

### `/lol-progression/v1`（6）

| 路由 | 声明插件 |
|---|---|
| `/lol-progression/v1/groups/${e}/instanceData` | rcp-fe-lol-kiwi-hub |
| `/lol-progression/v1/groups/${i}/configuration` | rcp-fe-lol-champ-select, rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-progression/v1/groups/${i}/instanceData` | rcp-fe-lol-champ-select, rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-progression/v1/groups/${o}/configuration` | rcp-fe-lol-postgame |
| `/lol-progression/v1/groups/${o}/instanceData` | rcp-fe-lol-postgame |
| `/lol-progression/v1/ready` | rcp-fe-lol-champ-select, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |

### `/lol-ranked/v1`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-ranked/v1/cached-ranked-stats/${this.get` | rcp-fe-lol-postgame |
| `/lol-ranked/v1/current-lp-change-notification` | rcp-fe-lol-postgame |
| `/lol-ranked/v1/current-ranked-stats` | rcp-fe-lol-postgame |
| `/lol-ranked/v1/ranked-stats/${e}` | rcp-fe-lol-social |

### `/lol-regalia/v2`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-regalia/v2/config` | rcp-fe-lol-social |

### `/lol-regalia/v3`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-regalia/v3/inventory/REGALIA_BANNER` | rcp-fe-lol-loot |

### `/lol-remedy/v1`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-remedy/v1/ack-remedy-notification/` | rcp-fe-lol-social |
| `/lol-remedy/v1/config/is-verbal-abuse-remedy-modal-enabled` | rcp-fe-lol-postgame |
| `/lol-remedy/v1/hostage-detected` | rcp-fe-lol-social |
| `/lol-remedy/v1/remedy-notifications` | rcp-fe-lol-social |

### `/lol-reward-track/${e}`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-reward-track/${e}/reward-track/items` | rcp-fe-lol-kiwi-hub |

### `/lol-reward-track/register`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-reward-track/register/${e}` | rcp-fe-lol-kiwi-hub |

### `/lol-rewards/v1`（5）

| 路由 | 声明插件 |
|---|---|
| `/lol-rewards/v1` | rcp-fe-lol-champ-select |
| `/lol-rewards/v1/grants` | rcp-fe-lol-kiwi-hub |
| `/lol-rewards/v1/groups` | rcp-fe-lol-champ-select, rcp-fe-lol-kiwi-hub, rcp-fe-lol-parties |
| `/lol-rewards/v1/groups:` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-rewards/v1/select-bulk` | rcp-fe-lol-kiwi-hub |

### `/lol-sanctum/v1`（10）

| 路由 | 声明插件 |
|---|---|
| `/lol-sanctum/v1/banners` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/banners/${e}/odds` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/banners/${e}/token-catalog-entry` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/banners/selected/id` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/counters/${e}` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/purchase/roll` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/purchase/token` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/purchases/${e}` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/ready` | rcp-fe-lol-loot |
| `/lol-sanctum/v1/velocity-limits` | rcp-fe-lol-loot |

### `/lol-seasons/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-seasons/v1/season/product/${e}` | rcp-fe-lol-navigation |
| `/lol-seasons/v1/season/should-ac-refresh` | rcp-fe-lol-navigation |

### `/lol-service-status/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-service-status/v1/lcu-status` | rcp-fe-lol-navigation |
| `/lol-service-status/v1/ticker-messages` | rcp-fe-lol-navigation |

### `/lol-settings/v1`（7）

| 路由 | 声明插件 |
|---|---|
| `/lol-settings/v1/account/champ-select` | rcp-fe-lol-parties |
| `/lol-settings/v1/account/lol-tutorial` | rcp-fe-lol-champ-select, rcp-fe-lol-collections, rcp-fe-lol-parties |
| `/lol-settings/v1/account/save` | rcp-fe-lol-social |
| `/lol-settings/v1/local/lol-audio` | rcp-fe-lol-champ-select, rcp-fe-lol-loot |
| `/lol-settings/v1/local/lol-skins-viewer` | rcp-fe-lol-collections |
| `/lol-settings/v1/local/lol-vanguard` | rcp-fe-lol-navigation |
| `/lol-settings/v1/local/video` | rcp-fe-lol-social |

### `/lol-settings/v2`（27）

| 路由 | 声明插件 |
|---|---|
| `/lol-settings/v2` | rcp-fe-lol-navigation |
| `/lol-settings/v2/account/LCUPreferences` | rcp-fe-lol-collections, rcp-fe-lol-social |
| `/lol-settings/v2/account/LCUPreferences/jade-ftux` | rcp-fe-lol-champ-select |
| `/lol-settings/v2/account/LCUPreferences/jade-store` | rcp-fe-lol-champ-select |
| `/lol-settings/v2/account/LCUPreferences/lol-champ-select` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-settings/v2/account/LCUPreferences/lol-chat` | rcp-fe-lol-social |
| `/lol-settings/v2/account/LCUPreferences/lol-general` | rcp-fe-lol-social |
| `/lol-settings/v2/account/LCUPreferences/lol-home` | rcp-fe-lol-navigation |
| `/lol-settings/v2/account/LCUPreferences/lol-info-hub` | rcp-fe-lol-navigation |
| `/lol-settings/v2/account/LCUPreferences/lol-kiwi-hub` | rcp-fe-lol-kiwi-hub |
| `/lol-settings/v2/account/LCUPreferences/lol-loot` | rcp-fe-lol-loot |
| `/lol-settings/v2/account/LCUPreferences/lol-navigation` | rcp-fe-lol-postgame |
| `/lol-settings/v2/account/LCUPreferences/lol-npe-first-touch` | rcp-fe-lol-navigation, rcp-fe-lol-parties |
| `/lol-settings/v2/account/LCUPreferences/lol-npe-tutorial-path` | rcp-fe-lol-navigation |
| `/lol-settings/v2/account/LCUPreferences/lol-parties-game-select` | rcp-fe-lol-parties |
| `/lol-settings/v2/account/LCUPreferences/lol-parties-jade-position` | rcp-fe-lol-parties |
| `/lol-settings/v2/account/LCUPreferences/lol-perks` | rcp-fe-lol-champ-select |
| `/lol-settings/v2/account/LCUPreferences/lol-quick-play` | rcp-fe-lol-parties |
| `/lol-settings/v2/account/LCUPreferences/lol-skins-viewer` | rcp-fe-lol-collections |
| `/lol-settings/v2/account/LCUPreferences/lol-tft` | rcp-fe-lol-navigation, rcp-fe-lol-parties |
| `/lol-settings/v2/account/LCUPreferences/lol-tutorial` | rcp-fe-lol-navigation |
| `/lol-settings/v2/account/VotingPreferences/jade-voting-system` | rcp-fe-lol-champ-select |
| `/lol-settings/v2/local` | rcp-fe-lol-collections |
| `/lol-settings/v2/local/lol-audio` | rcp-fe-lol-champ-select |
| `/lol-settings/v2/local/lol-user-experience` | rcp-fe-lol-champ-select, rcp-fe-lol-loot, rcp-fe-lol-match-history, rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-settings/v2/local/lol-win7-upgrade` | rcp-fe-lol-navigation |
| `/lol-settings/v2/ready` | rcp-fe-lol-champ-select, rcp-fe-lol-kiwi-hub, rcp-fe-lol-navigation, rcp-fe-lol-parties |

### `/lol-shared-components/badge-honor-${this.get`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/badge-honor-${this.get` | rcp-fe-lol-social |

### `/lol-shared-components/mastery-`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/mastery-` | rcp-fe-lol-champ-select |

### `/lol-shared-components/trans-challenges.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/trans-challenges.json` | rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |

### `/lol-shared-components/trans-champion-mastery.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/trans-champion-mastery.json` | rcp-fe-lol-collections, rcp-fe-lol-navigation, rcp-fe-lol-postgame |

### `/lol-shared-components/trans-currency-container.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/trans-currency-container.json` | rcp-fe-lol-loot, rcp-fe-lol-store |

### `/lol-shared-components/trans-digital-goods-disclaimer.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/trans-digital-goods-disclaimer.json` | rcp-fe-lol-champ-select, rcp-fe-lol-loot, rcp-fe-lol-store |

### `/lol-shared-components/trans-player-behavior.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/trans-player-behavior.json` | rcp-fe-lol-social |

### `/lol-shared-components/trans-shoppefront.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/trans-shoppefront.json` | rcp-fe-lol-champ-select, rcp-fe-lol-loot, rcp-fe-lol-store |

### `/lol-shared-components/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shared-components/trans.json` | rcp-fe-lol-match-history, rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |

### `/lol-shoppefront/v1`（6）

| 路由 | 声明插件 |
|---|---|
| `/lol-shoppefront/v1/bulk-purchases` | rcp-fe-lol-champ-select |
| `/lol-shoppefront/v1/drop-tables/${e}/odds` | rcp-fe-lol-champ-select |
| `/lol-shoppefront/v1/purchases/` | rcp-fe-lol-champ-select |
| `/lol-shoppefront/v1/purchases/${d}` | rcp-fe-lol-champ-select |
| `/lol-shoppefront/v1/stores/JADE_SHOP` | rcp-fe-lol-champ-select |
| `/lol-shoppefront/v1/velocity` | rcp-fe-lol-champ-select, rcp-fe-lol-loot |

### `/lol-shutdown/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-shutdown/v1/notification` | rcp-fe-lol-navigation |

### `/lol-social/code-of-conduct`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/code-of-conduct/game-logo-divider.png` | rcp-fe-lol-social |
| `/lol-social/code-of-conduct/game-logo-lol.png` | rcp-fe-lol-social |
| `/lol-social/code-of-conduct/game-logo-tft.png` | rcp-fe-lol-social |
| `/lol-social/code-of-conduct/logo-riot.png` | rcp-fe-lol-social |

### `/lol-social/discord-friend.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/discord-friend.png` | rcp-fe-lol-social |

### `/lol-social/discord-hovercard-splash.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/discord-hovercard-splash.png` | rcp-fe-lol-social |

### `/lol-social/player-notifications`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/player-notifications/${t}` | rcp-fe-lol-social |

### `/lol-social/recently-honored-default-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/recently-honored-default-icon.png` | rcp-fe-lol-social |

### `/lol-social/recently-honored-gold-3-icon.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/recently-honored-gold-3-icon.png` | rcp-fe-lol-social |

### `/lol-social/sounds`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/sounds` | rcp-fe-lol-social |
| `/lol-social/sounds/sfx-soc-notif-gameinvite-rcvd.ogg` | rcp-fe-lol-parties |
| `/lol-social/sounds/sfx-soc-ui-click-generic.ogg` | rcp-fe-lol-social |

### `/lol-social/trans-player-behavior.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/trans-player-behavior.json` | rcp-fe-lol-social |

### `/lol-social/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/trans.json` | rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |

### `/lol-social/videos`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-social/videos/SocialPanel_BGLoop.webm` | rcp-fe-lol-social |

### `/lol-spectator/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-spectator/v1/spectate/config` | rcp-fe-lol-social |
| `/lol-spectator/v1/spectate/launch` | rcp-fe-lol-social |

### `/lol-spectator/v3`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-spectator/v3/buddy/can-spectate/${t}/${encodeURIComponent` | rcp-fe-lol-social |

### `/lol-static-assets/allowedCharacters.txt`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-static-assets/allowedCharacters.txt` | rcp-fe-lol-navigation |

### `/lol-static-assets/images`（98）

| 路由 | 声明插件 |
|---|---|
| `/lol-static-assets/images` | rcp-fe-lol-navigation, rcp-fe-lol-store |
| `/lol-static-assets/images/` | rcp-fe-lol-social |
| `/lol-static-assets/images/aram-wardrobe/icon-large.png` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-static-assets/images/aram-wardrobe/icon-small-circle.png` | rcp-fe-lol-parties |
| `/lol-static-assets/images/aram-wardrobe/icon-small.png` | rcp-fe-lol-champ-select |
| `/lol-static-assets/images/buttons/chat-disabled-hover.png` | rcp-fe-lol-social |
| `/lol-static-assets/images/buttons/chat-disabled.png` | rcp-fe-lol-social |
| `/lol-static-assets/images/buttons/chat-enabled-hover.png` | rcp-fe-lol-social |
| `/lol-static-assets/images/buttons/chat-enabled.png` | rcp-fe-lol-social |
| `/lol-static-assets/images/buttons/find_match_active.png` | rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-static-assets/images/buttons/find_match_default.png` | rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-static-assets/images/buttons/find_match_hover.png` | rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-static-assets/images/challenges-shared/VGN_CRS_BG.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/challenges-shared/VGN_CRS_BG_SquareBG.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/challenges-shared/VGN_CRS_ICN_Square.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/challenges-shared/VGN_CRS_Tick_INTRO.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/challenges-shared/crystal_${e.toLowerCase` | rcp-fe-lol-parties |
| `/lol-static-assets/images/challenges-shared/crystal_${this.get` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/challenges-shared/icon_background.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/champ-select-shared/champion-grid/pg-square-portrait.png` | rcp-fe-lol-champ-select, rcp-fe-lol-parties |
| `/lol-static-assets/images/champion-mastery/level-up-celebration-bg.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/champions_rewards.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/checkmark.svg` | rcp-fe-lol-loot |
| `/lol-static-assets/images/clock-icon-grey1_5.svg` | rcp-fe-lol-parties |
| `/lol-static-assets/images/currency-ip-mini.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/currency/icons/ancient_spark.svg` | rcp-fe-lol-loot |
| `/lol-static-assets/images/currency/tokens/mythic_essence.png` | rcp-fe-lol-loot |
| `/lol-static-assets/images/currency/tokens/mythic_essence_plethora.png` | rcp-fe-lol-loot |
| `/lol-static-assets/images/default-background.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/discord-logo-blurple.svg` | rcp-fe-lol-parties |
| `/lol-static-assets/images/discord_blue.svg` | rcp-fe-lol-social |
| `/lol-static-assets/images/eat-icon-loot.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/empty_states/sleeping-poro.svg` | rcp-fe-lol-loot |
| `/lol-static-assets/images/event-shop/icon-shocked-poro-clear.png` | rcp-fe-lol-champ-select |
| `/lol-static-assets/images/event-shop/red-warning.png` | rcp-fe-lol-champ-select |
| `/lol-static-assets/images/exalted/card-frame-${e}.svg` | rcp-fe-lol-loot |
| `/lol-static-assets/images/flat-button-pause.svg` | rcp-fe-lol-loot |
| `/lol-static-assets/images/flat-button-play.svg` | rcp-fe-lol-loot |
| `/lol-static-assets/images/game-id-clipboard-copy.svg` | rcp-fe-lol-social |
| `/lol-static-assets/images/hextech-ui-icons/lock-closed.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/honor` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/honor-leaf.svg` | rcp-fe-lol-parties |
| `/lol-static-assets/images/honor/Heart_MiniIcon.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/RoleIcon_BOTTOM.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/RoleIcon_JUNGLE.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/RoleIcon_MIDDLE.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/RoleIcon_TOP.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/RoleIcon_UTILITY.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/Tooltip.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/Voting_BG_static.jpeg` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/Voting_Diamond.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/honor/Voting_Heart.png` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/icon-be-150.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/icon-rp-gradient-32.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/icon-star-shards-36.png` | rcp-fe-lol-parties |
| `/lol-static-assets/images/icons/chroma-icon.png` | rcp-fe-lol-loot |
| `/lol-static-assets/images/icons/half-tier-icon.svg` | rcp-fe-lol-loot |
| `/lol-static-assets/images/kr-rating-screen.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/lcm-eat-mastery-icon.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/left-right-arrows.svg` | rcp-fe-lol-parties |
| `/lol-static-assets/images/lol_icon.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/loyalty-nav-bar.svg` | rcp-fe-lol-parties |
| `/lol-static-assets/images/loyaltyMayhemBPAugmentsCount_rewards.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/loyaltyTFTCompanionCount_rewards.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/loyaltyTFTDamageSkinCount_rewards.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/loyaltyTFTMapSkinCount_rewards.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/loyaltyTFTZoomSkinCount_rewards.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/mobile-online.svg` | rcp-fe-lol-social |
| `/lol-static-assets/images/mobile.svg` | rcp-fe-lol-social |
| `/lol-static-assets/images/nav-icon-collections.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/nav-icon-loot.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/nav-icon-store.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-ft-hover-arrow.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-ft-radial-ticks.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-circle-` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-level-10-sr-draft-runepage.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-level-2-first-champion.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-level-4-spells.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-level-6-spells.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-level-7-spells.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-level-9-spells.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-poro-pal-emote.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-spells-ghost-heal.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-sr-blind-aram.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/npe-rewards-reward-major-ward-skin.png` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/ranked-mini-crests` | rcp-fe-lol-champ-select |
| `/lol-static-assets/images/red-info-icon.svg` | rcp-fe-lol-postgame |
| `/lol-static-assets/images/red-warning.png` | rcp-fe-lol-champ-select |
| `/lol-static-assets/images/rewards_programs_${a}.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/sanctum/card-frame-tier${e}.svg` | rcp-fe-lol-loot |
| `/lol-static-assets/images/skins_rewards.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/summoner-icon/summoner-icon-rare.png` | rcp-fe-lol-parties |
| `/lol-static-assets/images/tomacco/Asset_UpgradeableTile_Lowspec.png` | rcp-fe-lol-loot |
| `/lol-static-assets/images/warning_icon.svg` | rcp-fe-lol-parties |
| `/lol-static-assets/images/warning_icon_red.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/xpBoost_rewards.svg` | rcp-fe-lol-navigation |
| `/lol-static-assets/images/yellow-info-icon.svg` | rcp-fe-lol-navigation, rcp-fe-lol-postgame |
| `/lol-static-assets/images/yourshop.svg` | rcp-fe-lol-navigation |

### `/lol-static-assets/lottie`（20）

| 路由 | 声明插件 |
|---|---|
| `/lol-static-assets/lottie/challenges/VGN_CRS_BG_SquareBG.json` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/challenges/VGN_CRS_ICN_Square.json` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/challenges/VGN_CRS_Tick_INTRO.json` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/challenges/challenges-progress-bar-levelup.json` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/challenges/challenges-progress-bar.json` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/challenges/icons/collection.json` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/challenges/icons/expertise.json` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/challenges/icons/images/` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/challenges/icons/innovation.json` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/challenges/icons/teamwork.json` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/challenges/icons/veterancy.json` | rcp-fe-lol-navigation |
| `/lol-static-assets/lottie/postgame/` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/postgame/images/` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/postgame/postgame-challenges-card-intro.json` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/postgame/postgame-challenges-card-sheen.json` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/postgame/postgame-eternals-card-intro.json` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/postgame/postgame-screen-border.json` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/postgame/tft_EOG_WinStreak_LabelVFX.json` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/tft-rated/Badge_Highlight_EOG.json` | rcp-fe-lol-postgame |
| `/lol-static-assets/lottie/tft-rated/images/` | rcp-fe-lol-postgame |

### `/lol-static-assets/sounds`（49）

| 路由 | 声明插件 |
|---|---|
| `/lol-static-assets/sounds` | rcp-fe-lol-navigation |
| `/lol-static-assets/sounds/challenges/${t}` | rcp-fe-lol-navigation |
| `/lol-static-assets/sounds/honor` | rcp-fe-lol-navigation |
| `/lol-static-assets/sounds/honor/sfx-honor-vote-cast-low-spec.ogg` | rcp-fe-lol-postgame |
| `/lol-static-assets/sounds/honor/sfx-honor-vote-cast.ogg` | rcp-fe-lol-postgame |
| `/lol-static-assets/sounds/honor/sfx-honor-voting-intro.ogg` | rcp-fe-lol-postgame |
| `/lol-static-assets/sounds/npe-ft-sfx-pip-backward-click.ogg` | rcp-fe-lol-navigation, rcp-fe-lol-parties |
| `/lol-static-assets/sounds/npe-ft-sfx-pip-forward-click.ogg` | rcp-fe-lol-navigation, rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-eog-lobby-player-returned.ogg` | rcp-fe-lol-postgame |
| `/lol-static-assets/sounds/sfx-exalted-chase-10x-roll-rewards-appear-anim.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-chase-1x-roll-reward-appear-anim.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-chase-reward-celebration-anim.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-chase-s-tier-card-vfx.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-chase-sweet-button-click.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-hub-banner-button-click.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-hub-banner-button-hover.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-hub-button-currency-click.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-hub-summon-x1-click.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-hub-summon-x1-hover.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-hub-summon-x10-click.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-hub-summon-x10-hover.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-rolling-ceremony-multi-anim.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-rolling-ceremony-multi-gold-anim.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-rolling-ceremony-single-anim.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-exalted-rolling-ceremony-single-gold-anim.ogg` | rcp-fe-lol-loot |
| `/lol-static-assets/sounds/sfx-gameselect-button-confirm-click.ogg` | rcp-fe-lol-navigation |
| `/lol-static-assets/sounds/sfx-gameselect-button-confirm-hover.ogg` | rcp-fe-lol-navigation |
| `/lol-static-assets/sounds/sfx-loadout-rarity-button-click.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-lobby-arena-player-swap.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-parties-member-banner-invited.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-readycheck-decline-button-click.ogg` | rcp-fe-lol-navigation |
| `/lol-static-assets/sounds/sfx-uikit-arena-modal-click.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-arena-modal-hover.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-button-circlegold-click.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-button-circlegold-hover.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-button-generic-click.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-button-generic-hover.ogg` | rcp-fe-lol-kiwi-hub, rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-button-gold-click.ogg` | rcp-fe-lol-kiwi-hub, rcp-fe-lol-loot, rcp-fe-lol-navigation, rcp-fe-lol-postgame |
| `/lol-static-assets/sounds/sfx-uikit-button-gold-hover.ogg` | rcp-fe-lol-navigation, rcp-fe-lol-postgame |
| `/lol-static-assets/sounds/sfx-uikit-button-pageup-click.ogg` | rcp-fe-lol-kiwi-hub |
| `/lol-static-assets/sounds/sfx-uikit-button-replay-click.ogg` | rcp-fe-lol-kiwi-hub |
| `/lol-static-assets/sounds/sfx-uikit-button-text-click.ogg` | rcp-fe-lol-kiwi-hub |
| `/lol-static-assets/sounds/sfx-uikit-grid-big-click.ogg` | rcp-fe-lol-navigation |
| `/lol-static-assets/sounds/sfx-uikit-grid-big-hover.ogg` | rcp-fe-lol-navigation |
| `/lol-static-assets/sounds/sfx-uikit-grid-click.ogg` | rcp-fe-lol-navigation, rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-grid-drag.ogg` | rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-grid-hover.ogg` | rcp-fe-lol-kiwi-hub, rcp-fe-lol-navigation, rcp-fe-lol-parties |
| `/lol-static-assets/sounds/sfx-uikit-magic-button-hover.ogg` | rcp-fe-lol-postgame |
| `/lol-static-assets/sounds/sfx-uikit-text-click-small.ogg` | rcp-fe-lol-loot, rcp-fe-lol-navigation, rcp-fe-lol-parties |

### `/lol-static-assets/videos`（26）

| 路由 | 声明插件 |
|---|---|
| `/lol-static-assets/videos` | rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-static-assets/videos/LootCrafting_UpgradeableTile.webm` | rcp-fe-lol-loot |
| `/lol-static-assets/videos/challenges/VGN_CRS_Flare.webm` | rcp-fe-lol-navigation |
| `/lol-static-assets/videos/challenges/crystal-levels/${this.get` | rcp-fe-lol-navigation |
| `/lol-static-assets/videos/challenges/postgame/postgame-challenges-card-intro.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/challenges/postgame/postgame-eternals-card-intro.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/challenges/postgame/postgame-spark-large.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/challenges/postgame/postgame-spark-medium.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/challenges/postgame/postgame-spark-small.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/champion-mastery/cm-celebration-background.webm` | rcp-fe-lol-navigation |
| `/lol-static-assets/videos/exalted/card-frame-${e}-${t}.webm` | rcp-fe-lol-loot |
| `/lol-static-assets/videos/honor` | rcp-fe-lol-navigation |
| `/lol-static-assets/videos/honor/` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/honor/Voting_BG.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/honor/Voting_VoteCast_Celebration_V2.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/lobby-crystal-intro-player.webm` | rcp-fe-lol-parties |
| `/lol-static-assets/videos/lp_penalty_effect.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/lp_returned_effect.webm` | rcp-fe-lol-postgame |
| `/lol-static-assets/videos/npe-ft-intro-video.webm` | rcp-fe-lol-navigation |
| `/lol-static-assets/videos/reward-item-claimable-glow.webm` | rcp-fe-lol-kiwi-hub |
| `/lol-static-assets/videos/reward-track-progress-bar.webm` | rcp-fe-lol-kiwi-hub |
| `/lol-static-assets/videos/sanctum/card-frame-tier${e}-${t}.webm` | rcp-fe-lol-loot |
| `/lol-static-assets/videos/timer-accepted-idle.webm` | rcp-fe-lol-navigation |
| `/lol-static-assets/videos/timer-accepted-intro.webm` | rcp-fe-lol-navigation |
| `/lol-static-assets/videos/timer-countdown.webm` | rcp-fe-lol-navigation |
| `/lol-static-assets/videos/timer-declined.webm` | rcp-fe-lol-navigation |

### `/lol-statstones/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-statstones/v1/eog-notifications` | rcp-fe-lol-postgame |
| `/lol-statstones/v1/statstones-enabled-queue-ids` | rcp-fe-lol-postgame |

### `/lol-store/background.png`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-store/background.png` | rcp-fe-lol-store |

### `/lol-store/storefront`（31）

| 路由 | 声明插件 |
|---|---|
| `/lol-store/storefront/addon/public/img` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/back-disabled.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/back-hover.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/back.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/caret.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/checkbox-spritesheet.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/close.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/dropdown-check.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/dropdown-select-dot.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/frame-button-close-top-down.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/hextechMagicBg.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/magic-bg.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/purchase-loading.gif` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/reset-timer-clock.svg` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/ric-riot-logo-red.svg` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/ric-rp.svg` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/search-box-clear.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/search-icon.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/sheen.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/silvershields.svg` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/skin-variant-mask.svg` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/spinner.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/sprite-c98e2cc7a7c85bd2b11e41da606a66a9.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/sprite-source/lcu-be-svg.svg` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/sub-border-primary-horizontal.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/sub-border-primary-vertical.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/sub-border-secondary-horizontal.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/sub-border-secondary-vertical.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/up-down-arrow-locked.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/up-down-arrow.png` | rcp-fe-lol-store |
| `/lol-store/storefront/addon/public/img/x-icon.png` | rcp-fe-lol-store |

### `/lol-store/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-store/trans.json` | rcp-fe-lol-store |

### `/lol-store/v1`（12）

| 路由 | 声明插件 |
|---|---|
| `/lol-store/v1` | rcp-fe-lol-champ-select |
| `/lol-store/v1/catalog` | rcp-fe-lol-champ-select, rcp-fe-lol-navigation |
| `/lol-store/v1/catalog/${a}/${i}/tags` | rcp-fe-lol-champ-select |
| `/lol-store/v1/catalog/CHAMPION_SKIN` | rcp-fe-lol-parties |
| `/lol-store/v1/catalog/items/skip-cache` | rcp-fe-lol-champ-select |
| `/lol-store/v1/catalogByInstanceIds` | rcp-fe-lol-champ-select |
| `/lol-store/v1/giftablefriends` | rcp-fe-lol-social |
| `/lol-store/v1/skins/` | rcp-fe-lol-champ-select |
| `/lol-store/v1/skins/${t}` | rcp-fe-lol-champ-select |
| `/lol-store/v1/skins/{{skin.id}}` | rcp-fe-lol-champ-select |
| `/lol-store/v1/skins/{{viewSkin.id}}` | rcp-fe-lol-champ-select |
| `/lol-store/v1/store-ready` | rcp-fe-lol-champ-select |

### `/lol-strawberry-hub/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-strawberry-hub/trans.json` | rcp-fe-lol-parties, rcp-fe-lol-postgame |

### `/lol-summoner-profiles/v1`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-summoner-profiles/v1/get-honor-view` | rcp-fe-lol-social |
| `/lol-summoner-profiles/v1/get-jade-voting-view` | rcp-fe-lol-champ-select |
| `/lol-summoner-profiles/v1/pco/${I}` | rcp-fe-lol-champ-select |

### `/lol-summoner/v1`（8）

| 路由 | 声明插件 |
|---|---|
| `/lol-summoner/v1` | rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-summoner/v1/current-summoner` | rcp-fe-lol-champ-select, rcp-fe-lol-collections, rcp-fe-lol-loot, rcp-fe-lol-match-history, rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |
| `/lol-summoner/v1/current-summoner/autofill` | rcp-fe-lol-parties |
| `/lol-summoner/v1/player-alias-state` | rcp-fe-lol-navigation |
| `/lol-summoner/v1/save-alias` | rcp-fe-lol-navigation |
| `/lol-summoner/v1/summoner-requests-ready` | rcp-fe-lol-navigation |
| `/lol-summoner/v1/summoners` | rcp-fe-lol-parties, rcp-fe-lol-social |
| `/lol-summoner/v1/validate-alias` | rcp-fe-lol-navigation |

### `/lol-summoner/v2`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-summoner/v2/summoner-${t}s` | rcp-fe-lol-social |
| `/lol-summoner/v2/summoner-icons` | rcp-fe-lol-social |
| `/lol-summoner/v2/summoners` | rcp-fe-lol-social |

### `/lol-tencent-diagnostic-assistant/v1`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-tencent-diagnostic-assistant/v1/enabled` | rcp-fe-lol-postgame |
| `/lol-tencent-diagnostic-assistant/v1/invoke` | rcp-fe-lol-postgame |
| `/lol-tencent-diagnostic-assistant/v1/needNotify` | rcp-fe-lol-social |

### `/lol-tft-event-pve/v1`（3）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft-event-pve/v1/eogmissionrewards` | rcp-fe-lol-postgame |
| `/lol-tft-event-pve/v1/eventpvehub` | rcp-fe-lol-postgame |
| `/lol-tft-event-pve/v1/journeytrack/bonuses` | rcp-fe-lol-postgame |

### `/lol-tft-pass/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft-pass/v1/battle-pass` | rcp-fe-lol-navigation |
| `/lol-tft-pass/v1/event-pass` | rcp-fe-lol-postgame |

### `/lol-tft-promethium/audio`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft-promethium/audio/mus-pm-hub-base.ogg` | rcp-fe-lol-postgame |
| `/lol-tft-promethium/audio/sfx-pm-hub-amb-01.ogg` | rcp-fe-lol-postgame |
| `/lol-tft-promethium/audio/sfx-pm-hub-amb-02.ogg` | rcp-fe-lol-postgame |
| `/lol-tft-promethium/audio/sfx-pm-hub-amb-03.ogg` | rcp-fe-lol-postgame |

### `/lol-tft-promethium/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft-promethium/trans.json` | rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |

### `/lol-tft-skill-tree/v1`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft-skill-tree/v1/skill-tree` | rcp-fe-lol-parties, rcp-fe-lol-postgame |
| `/lol-tft-skill-tree/v1/skill-tree-rank/` | rcp-fe-lol-parties |

### `/lol-tft/images`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft/images/tft-icon.svg` | rcp-fe-lol-navigation |
| `/lol-tft/images/vector-line.svg` | rcp-fe-lol-navigation |

### `/lol-tft/spine`（4）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft/spine/skill-tree/Fire_Ceremony.skel` | rcp-fe-lol-postgame |
| `/lol-tft/spine/skill-tree/Lightning_Ceremony.skel` | rcp-fe-lol-postgame |
| `/lol-tft/spine/skill-tree/SkillTree_Ceremonies_All_Reduced.atlas` | rcp-fe-lol-postgame |
| `/lol-tft/spine/skill-tree/Water_Ceremony.skel` | rcp-fe-lol-postgame |

### `/lol-tft/trans.json`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft/trans.json` | rcp-fe-lol-parties |

### `/lol-tft/v1`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-tft/v1/tft/events` | rcp-fe-lol-parties |

### `/lol-typekit/fonts`（2）

| 路由 | 声明插件 |
|---|---|
| `/lol-typekit/fonts/fonts.css` | rcp-fe-lol-store |
| `/lol-typekit/fonts/styles.css` | rcp-fe-lol-store |

### `/lol-uikit/images`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/images/close.png` | rcp-fe-lol-postgame |

### `/lol-uikit/main.css`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/main.css` | rcp-fe-lol-social |

### `/lol-uikit/sfx-uikit-button-circlegold-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-button-circlegold-hover.ogg` | rcp-fe-lol-parties |

### `/lol-uikit/sfx-uikit-button-circlex-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-button-circlex-click.ogg` | rcp-fe-lol-navigation |

### `/lol-uikit/sfx-uikit-button-gold-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-button-gold-click.ogg` | rcp-fe-lol-loot, rcp-fe-lol-parties |

### `/lol-uikit/sfx-uikit-button-gold-hover.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-button-gold-hover.ogg` | rcp-fe-lol-parties |

### `/lol-uikit/sfx-uikit-button-locked-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-button-locked-click.ogg` | rcp-fe-lol-parties |

### `/lol-uikit/sfx-uikit-checkbox-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-checkbox-click.ogg` | rcp-fe-lol-social |

### `/lol-uikit/sfx-uikit-click-and-slide.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-click-and-slide.ogg` | rcp-fe-lol-parties |

### `/lol-uikit/sfx-uikit-generic-click-small.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-generic-click-small.ogg` | rcp-fe-lol-parties |

### `/lol-uikit/sfx-uikit-radio-click.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-radio-click.ogg` | rcp-fe-lol-parties |

### `/lol-uikit/sfx-uikit-text-click-small.ogg`（1）

| 路由 | 声明插件 |
|---|---|
| `/lol-uikit/sfx-uikit-text-click-small.ogg` | rcp-fe-lol-parties |

### `/lol-vanguard/v1`（7）

| 路由 | 声明插件 |
|---|---|
| `/lol-vanguard/v1/ack-notification/${e.mailId}` | rcp-fe-lol-navigation |
| `/lol-vanguard/v1/config/enabled` | rcp-fe-lol-navigation |
| `/lol-vanguard/v1/is-playing-in-pcb` | rcp-fe-lol-navigation |
| `/lol-vanguard/v1/machine-specs` | rcp-fe-lol-navigation |
| `/lol-vanguard/v1/notification` | rcp-fe-lol-navigation |
| `/lol-vanguard/v1/session` | rcp-fe-lol-navigation |
| `/lol-vanguard/v1/telemetry/system-check` | rcp-fe-lol-navigation |

### `/riotclient/command-line-args`（1）

| 路由 | 声明插件 |
|---|---|
| `/riotclient/command-line-args` | rcp-fe-lol-navigation |

### `/riotclient/pre-shutdown`（1）

| 路由 | 声明插件 |
|---|---|
| `/riotclient/pre-shutdown/begin` | rcp-fe-lol-social |

### `/riotclient/region-locale`（1）

| 路由 | 声明插件 |
|---|---|
| `/riotclient/region-locale` | rcp-fe-lol-champ-select, rcp-fe-lol-loot, rcp-fe-lol-navigation, rcp-fe-lol-parties, rcp-fe-lol-postgame, rcp-fe-lol-social |

### `/riotclient/system-info`（1）

| 路由 | 声明插件 |
|---|---|
| `/riotclient/system-info/v1/basic-info` | rcp-fe-lol-navigation |

## 写调用（POST / PUT / PATCH / DELETE）

| 方法 | 路径 | 插件 |
|---|---|---|
| DELETE | `${a}/${e}` | rcp-fe-lol-social |
| DELETE | `${s}/${e}` | rcp-fe-lol-social |
| DELETE | `/lol-chat/v1/blocked-players/${o}` | rcp-fe-lol-match-history |
| DELETE | `/lol-chat/v1/conversations/` | rcp-fe-lol-social |
| DELETE | `/lol-chat/v1/conversations/active` | rcp-fe-lol-social |
| DELETE | `/lol-chat/v1/errors/` | rcp-fe-lol-social |
| DELETE | `/lol-chat/v1/errors/{id}` | rcp-fe-lol-social |
| DELETE | `/lol-chat/v1/friend-groups/${e}` | rcp-fe-lol-social |
| DELETE | `/lol-chat/v1/friends/` | rcp-fe-lol-social |
| DELETE | `/lol-chat/v1/friends/${o}` | rcp-fe-lol-match-history |
| DELETE | `/lol-chat/v2/friend-requests/` | rcp-fe-lol-social |
| DELETE | `/lol-lobby/v2/lobby/matchmaking/search` | rcp-fe-lol-social |
| DELETE | `/lol-login/v1/session` | rcp-fe-lol-navigation |
| DELETE | `/player-notifications/v1/notifications/` | rcp-fe-lol-social |
| DELETE | `/v1/blocked-players/` | rcp-fe-lol-social |
| DELETE | `/v1/blocked-players/${encodeURIComponent(o.id)}` | rcp-fe-lol-postgame |
| DELETE | `/v1/code-of-conduct-notification` | rcp-fe-lol-social |
| DELETE | `/v1/conversations/` | rcp-fe-lol-social |
| DELETE | `/v1/friend-groups/` | rcp-fe-lol-social |
| DELETE | `/v1/loot-grants/${t.id}` | rcp-fe-lol-postgame |
| DELETE | `/v1/notifications/` | rcp-fe-lol-social |
| DELETE | `/v1/notifications/${e}` | rcp-fe-lol-navigation |
| DELETE | `/v1/notifications/${t}` | rcp-fe-lol-social |
| DELETE | `/v1/player-loot/` | rcp-fe-lol-loot |
| DELETE | `/v1/reporter-feedback/` | rcp-fe-lol-social |
| DELETE | `/v1/suggested-players/${e}` | rcp-fe-lol-parties |
| DELETE | `/v2/lobby` | rcp-fe-lol-parties |
| DELETE | `/v2/lobby/matchmaking/search` | rcp-fe-lol-parties |
| DELETE | `/v2/notifications/${e}` | rcp-fe-lol-parties |
| DELETE | `v2/lobby` | rcp-fe-lol-parties |
| PATCH | `${s.REWARDS_API.GRANTS_V1}/view` | rcp-fe-lol-navigation |
| PATCH | `/lol-champ-select/v1/session/actions/${e.get(` | rcp-fe-lol-champ-select |
| PATCH | `/lol-champ-select/v1/session/my-selection` | rcp-fe-lol-champ-select |
| PATCH | `/lol-cosmetics/v1/recent/` | rcp-fe-lol-parties |
| PATCH | `/lol-loadouts/v4/loadouts/${e}` | rcp-fe-lol-champ-select |
| PATCH | `/lol-loadouts/v4/loadouts/${t.id}` | rcp-fe-lol-champ-select |
| PATCH | `/lol-loadouts/v4/loadouts/${t}` | rcp-fe-lol-champ-select |
| PATCH | `/lol-settings/v2/account/LCUPreferences/lol-chat` | rcp-fe-lol-social |
| PATCH | `/lol-settings/v2/account/LCUPreferences/lol-info-hub` | rcp-fe-lol-navigation |
| PATCH | `/lol-settings/v2/account/LCUPreferences/lol-tft` | rcp-fe-lol-navigation |
| PATCH | `/v1/account/lol-parties` | rcp-fe-lol-parties |
| PATCH | `/v1/account/lol-tutorial` | rcp-fe-lol-collections |
| PATCH | `/v2/account/LCUPreferences/activity-center` | rcp-fe-lol-navigation |
| PATCH | `/v2/account/LCUPreferences/emotes` | rcp-fe-lol-collections |
| PATCH | `/v2/account/LCUPreferences/lol-email-verification` | rcp-fe-lol-navigation |
| PATCH | `/v2/account/LCUPreferences/lol-general` | rcp-fe-lol-navigation |
| PATCH | `/v2/account/LCUPreferences/lol-perks` | rcp-fe-lol-collections |
| PATCH | `/v2/account/LCUPreferences/lol-tft` | rcp-fe-lol-parties |
| PATCH | `/v4/loadouts/` | rcp-fe-lol-champ-select |
| PATCH | `/v4/loadouts/${e}` | rcp-fe-lol-collections |
| PATCH | `v1/account/lol-parties` | rcp-fe-lol-parties |
| PATCH | `v1/session/my-selection` | rcp-fe-lol-champ-select |
| POST | `${i}/request` | rcp-fe-lol-champ-select |
| POST | `/clear-cache` | rcp-fe-lol-navigation |
| POST | `/lol-challenges/v1/ack-challenge-update/${e}` | rcp-fe-lol-navigation |
| POST | `/lol-champ-select/v1/retrieve-latest-game-dto` | rcp-fe-lol-champ-select |
| POST | `/lol-champ-select/v1/session/simple-inventory` | rcp-fe-lol-champ-select |
| POST | `/lol-chat/v1/blocked-players` | rcp-fe-lol-match-history |
| POST | `/lol-chat/v1/conversations` | rcp-fe-lol-social |
| POST | `/lol-chat/v1/conversations/` | rcp-fe-lol-social |
| POST | `/lol-chat/v1/conversations/${encodeURIComponent(e)}/messages` | rcp-fe-lol-postgame |
| POST | `/lol-chat/v1/conversations/${encodeURIComponent(t)}/messages` | rcp-fe-lol-social |
| POST | `/lol-chat/v1/conversations/eog-chat-toggle` | rcp-fe-lol-social |
| POST | `/lol-chat/v1/friend-groups` | rcp-fe-lol-social |
| POST | `/lol-chat/v2/friend-requests/` | rcp-fe-lol-match-history |
| POST | `/lol-directx-upgrade/notification-ack` | rcp-fe-lol-navigation |
| POST | `/lol-end-of-game/v1/state/dismiss-stats` | rcp-fe-lol-navigation |
| POST | `/lol-gameflow/v1/session/event` | rcp-fe-lol-navigation |
| POST | `/lol-honor-v2/v1/ack-honor-notification/` | rcp-fe-lol-navigation |
| POST | `/lol-honor-v2/v1/ballot/refresh` | rcp-fe-lol-postgame |
| POST | `/lol-honor-v2/v1/level-change/ack` | rcp-fe-lol-navigation |
| POST | `/lol-honor-v2/v1/reward-granted/ack` | rcp-fe-lol-navigation |
| POST | `/lol-honor/v1/ballot` | rcp-fe-lol-postgame |
| POST | `/lol-honor/v1/honor` | rcp-fe-lol-postgame |
| POST | `/lol-kr-shutdown-law/v1/rating-screen/acknowledge` | rcp-fe-lol-navigation |
| POST | `/lol-lobby/v2/party/${t}/join` | rcp-fe-lol-social |
| POST | `/lol-lobby/v2/play-again` | rcp-fe-lol-navigation |
| POST | `/lol-lobby/v2/received-invitations/` | rcp-fe-lol-social |
| POST | `/lol-lobby/v2/received-invitations/${t}/accept` | rcp-fe-lol-parties |
| POST | `/lol-login/v1/delete-rso-on-close` | rcp-fe-lol-navigation |
| POST | `/lol-loot/v1/player-loot-notifications/${e}/acknowledge` | rcp-fe-lol-navigation |
| POST | `/lol-loot/v1/refresh` | rcp-fe-lol-loot |
| POST | `/lol-pre-end-of-game/v1/skip-pre-end-of-game` | rcp-fe-lol-postgame |
| POST | `/lol-rewards/v1/select-bulk` | rcp-fe-lol-kiwi-hub |
| POST | `/lol-settings/v1/account/save` | rcp-fe-lol-social |
| POST | `/lol-shoppefront/v1/bulk-purchases` | rcp-fe-lol-champ-select |
| POST | `/lol-spectator/v1/spectate/launch` | rcp-fe-lol-social |
| POST | `/lol-summoner/v1/save-alias` | rcp-fe-lol-navigation |
| POST | `/lol-summoner/v1/validate-alias` | rcp-fe-lol-navigation |
| POST | `/lol-tencent-diagnostic-assistant/v1/invoke` | rcp-fe-lol-postgame |
| POST | `/lol-vanguard/v1/telemetry/system-check` | rcp-fe-lol-navigation |
| POST | `/player-notifications/v1/notifications` | rcp-fe-lol-navigation |
| POST | `/telemetry/v1/events/${e}` | rcp-fe-lol-navigation |
| POST | `/v1/${t.puuid}/champion-mastery/top?count=1` | rcp-fe-lol-collections |
| POST | `/v1/account-state` | rcp-fe-lol-store |
| POST | `/v1/blocked-players` | rcp-fe-lol-parties |
| POST | `/v1/complete/missions-celebration` | rcp-fe-lol-navigation |
| POST | `/v1/confirm-email` | rcp-fe-lol-navigation |
| POST | `/v1/conversations` | rcp-fe-lol-social |
| POST | `/v1/conversations/` | rcp-fe-lol-social |
| POST | `/v1/conversations/${encodeURIComponent(n)}/messages` | rcp-fe-lol-social |
| POST | `/v1/conversations/${encodeURIComponent(t)}/messages` | rcp-fe-lol-postgame |
| POST | `/v1/conversations/${e}/messages` | rcp-fe-lol-champ-select |
| POST | `/v1/conversations/${n}/messages` | rcp-fe-lol-champ-select |
| POST | `/v1/conversations/eog-chat-toggle` | rcp-fe-lol-social |
| POST | `/v1/craft/mass` | rcp-fe-lol-loot |
| POST | `/v1/custom-games/refresh` | rcp-fe-lol-parties |
| POST | `/v1/delete-rso-on-close` | rcp-fe-lol-social |
| POST | `/v1/direct-message-reports` | rcp-fe-lol-social |
| POST | `/v1/early-exit` | rcp-fe-lol-navigation |
| POST | `/v1/events/` | rcp-fe-lol-social |
| POST | `/v1/events/${e}` | rcp-fe-lol-parties |
| POST | `/v1/events/item_sets__item_sets_event` | rcp-fe-lol-collections |
| POST | `/v1/events/ready_check_event` | rcp-fe-lol-navigation |
| POST | `/v1/friend-groups` | rcp-fe-lol-social |
| POST | `/v1/invoke` | rcp-fe-lol-champ-select |
| POST | `/v1/item-sets/validate` | rcp-fe-lol-collections |
| POST | `/v1/late-recognition/ack` | rcp-fe-lol-navigation |
| POST | `/v1/lobby/custom/bots` | rcp-fe-lol-parties |
| POST | `/v1/lobby/custom/start-champ-select` | rcp-fe-lol-parties |
| POST | `/v1/milestones/${e}/claim` | rcp-fe-lol-loot |
| POST | `/v1/notifications/acknowledge` | rcp-fe-lol-navigation |
| POST | `/v1/pages` | rcp-fe-lol-collections |
| POST | `/v1/player-loot/${e}/redeem` | rcp-fe-lol-loot |
| POST | `/v1/player-mutes` | rcp-fe-lol-postgame |
| POST | `/v1/process/quit` | rcp-fe-lol-store |
| POST | `/v1/ready-check/accept` | rcp-fe-lol-navigation |
| POST | `/v1/ready-check/decline` | rcp-fe-lol-navigation |
| POST | `/v1/recipes/` | rcp-fe-lol-loot |
| POST | `/v1/reconnect` | rcp-fe-lol-navigation |
| POST | `/v1/refresh` | rcp-fe-lol-loot |
| POST | `/v1/rsbot-challenges/${e}` | rcp-fe-lol-postgame |
| POST | `/v1/rune-recommender-auto-select` | rcp-fe-lol-champ-select |
| POST | `/v1/show-auto-modified-pages-notification` | rcp-fe-lol-champ-select |
| POST | `/v1/spectate/quit` | rcp-fe-lol-navigation |
| POST | `/v1/state/dismiss-stats` | rcp-fe-lol-postgame |
| POST | `/v1/summoners/aliases` | rcp-fe-lol-parties |
| POST | `/v1/tft/newPlayer` | rcp-fe-lol-parties |
| POST | `/v1/toggle-favorite/${n}/${t}` | rcp-fe-lol-champ-select |
| POST | `/v1/tournaments/${e}/join` | rcp-fe-lol-parties |
| POST | `/v2/eligibility/party` | rcp-fe-lol-parties |
| POST | `/v2/eog-invitations` | rcp-fe-lol-postgame |
| POST | `/v2/friend-requests` | rcp-fe-lol-parties |
| POST | `/v2/lobby/invitations` | rcp-fe-lol-parties |
| POST | `/v2/lobby/invitationsWithContext` | rcp-fe-lol-parties |
| POST | `/v2/lobby/matchmaking/search` | rcp-fe-lol-parties |
| POST | `/v2/lobby/members/${e}/${t}` | rcp-fe-lol-parties |
| POST | `/v2/lobby/team/SPECTATOR` | rcp-fe-lol-parties |
| POST | `/v2/party/${n}/join` | rcp-fe-lol-social |
| POST | `/v2/play-again` | rcp-fe-lol-postgame |
| POST | `/v2/play-again-decline` | rcp-fe-lol-postgame |
| POST | `/v2/received-invitations/` | rcp-fe-lol-social |
| POST | `/v2/received-invitations/${e}/decline` | rcp-fe-lol-parties |
| POST | `/v2/reporter-feedback/` | rcp-fe-lol-social |
| POST | `/v2/summoners/names` | rcp-fe-lol-parties |
| POST | `/v2/summoners/puuid` | rcp-fe-lol-parties |
| POST | `v1/basic-tutorial/start` | rcp-fe-lol-parties |
| POST | `v1/battle-training/start` | rcp-fe-lol-parties |
| POST | `v1/notification/acknowledge` | rcp-fe-lol-collections |
| POST | `v1/recommended-pages-position/champion/${t}/position/${e}` | rcp-fe-lol-collections |
| POST | `v1/spectate/launch` | rcp-fe-lol-social |
| POST | `v2/lobby` | rcp-fe-lol-parties |
| PUT | `${s.MISSIONS_API.PLAYER_V1}/${e}` | rcp-fe-lol-navigation |
| PUT | `/lol-chat/v1/conversations/` | rcp-fe-lol-social |
| PUT | `/lol-chat/v1/conversations/active` | rcp-fe-lol-social |
| PUT | `/lol-chat/v1/friend-groups/${e.id}` | rcp-fe-lol-social |
| PUT | `/lol-chat/v1/friend-groups/${t.id}` | rcp-fe-lol-social |
| PUT | `/lol-chat/v1/friends/` | rcp-fe-lol-social |
| PUT | `/lol-chat/v1/friends/${e}` | rcp-fe-lol-social |
| PUT | `/lol-chat/v1/settings?doAsync=true` | rcp-fe-lol-social |
| PUT | `/lol-chat/v2/friend-requests/` | rcp-fe-lol-social |
| PUT | `/lol-cosmetics/v1/favorites/tft/save` | rcp-fe-lol-parties |
| PUT | `/lol-cosmetics/v1/selection/companion` | rcp-fe-lol-parties |
| PUT | `/lol-cosmetics/v1/selection/tft-augment-pillar` | rcp-fe-lol-parties |
| PUT | `/lol-cosmetics/v1/selection/tft-damage-skin` | rcp-fe-lol-parties |
| PUT | `/lol-cosmetics/v1/selection/tft-map-skin` | rcp-fe-lol-parties |
| PUT | `/lol-cosmetics/v1/selection/tft-zoom-skin` | rcp-fe-lol-parties |
| PUT | `/lol-npe-tutorial-path/v1/settings` | rcp-fe-lol-navigation |
| PUT | `/lol-perks/v1/validate-page-name/${e}/${t}` | rcp-fe-lol-champ-select |
| PUT | `/lol-player-behavior/v1/ack-credibility-behavior-warning/` | rcp-fe-lol-social |
| PUT | `/lol-remedy/v1/ack-remedy-notification/` | rcp-fe-lol-social |
| PUT | `/lol-vanguard/v1/ack-notification/${e.mailId}` | rcp-fe-lol-navigation |
| PUT | `/player-notifications/v1/notifications/` | rcp-fe-lol-social |
| PUT | `/v1/conversations/` | rcp-fe-lol-social |
| PUT | `/v1/current-summoner/icon` | rcp-fe-lol-collections |
| PUT | `/v1/email` | rcp-fe-lol-navigation |
| PUT | `/v1/friend-groups/` | rcp-fe-lol-social |
| PUT | `/v1/friends/` | rcp-fe-lol-social |
| PUT | `/v1/item-sets/${c}/sets` | rcp-fe-lol-collections |
| PUT | `/v1/item-sets/${this.get(` | rcp-fe-lol-collections |
| PUT | `/v1/lobby/members/localMember/position-preferences` | rcp-fe-lol-parties |
| PUT | `/v1/me` | rcp-fe-lol-social |
| PUT | `/v1/pages/validate/` | rcp-fe-lol-collections |
| PUT | `/v1/settings` | rcp-fe-lol-navigation |
| PUT | `/v2/account/LCUPreferences/lol-npe-first-touch` | rcp-fe-lol-navigation |
| PUT | `/v2/lobby/memberData` | rcp-fe-lol-parties |
| PUT | `/v2/lobby/partyType` | rcp-fe-lol-parties |
| PUT | `/v2/lobby/quickplayMemberData` | rcp-fe-lol-parties |
| PUT | `/v2/lobby/subteamData` | rcp-fe-lol-parties |
| PUT | `/v3/reform-card/` | rcp-fe-lol-social |
