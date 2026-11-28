# LCU 端点全表（由 GET /help 推导）

- 函数总数: 1452
- 可推导为 HTTP 路由: 1432
- 无法推导（内部基类方法等）: 20
- 置信度 §: `ByXxx` 段按 `{xxx}` 处理，参数名可能与实际不同

## AntiAddiction（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/anti-addiction/v1/policies/by/policy/type/anti/addiction/state` | `GetAntiAddictionV1PoliciesByPolicyTypeAntiAddictionState` |  |

## ByPluginAssetsByPath（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/by-plugin-assets-by-path` | `GetByPluginAssetsByPath` |  |
| HEAD | `/by-plugin-assets-by-path` | `HeadByPluginAssetsByPath` |  |

## ClientConfig（8）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| PUT | `/client-config/v1/entitlements/token` | `PutClientConfigV1EntitlementsToken` |  |
| PUT | `/client-config/v1/refresh/config/status` | `PutClientConfigV1RefreshConfigStatus` |  |
| GET | `/client-config/v1/status/by/type` | `GetClientConfigV1StatusByType` |  |
| GET | `/client-config/v2/config/by/name` | `GetClientConfigV2ConfigByName` |  |
| GET | `/client-config/v2/namespace/by/namespace` | `GetClientConfigV2NamespaceByNamespace` |  |
| GET | `/client-config/v2/namespace/by/namespace/player` | `GetClientConfigV2NamespaceByNamespacePlayer` |  |
| GET | `/client-config/v2/namespace/by/namespace/public` | `GetClientConfigV2NamespaceByNamespacePublic` |  |
| PUT | `/client-config/v2/namespace/changes` | `PutClientConfigV2NamespaceChanges` |  |

## CookieJar（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/cookie-jar/v1/cookies` | `GetCookieJarV1Cookies` |  |
| POST | `/cookie-jar/v1/cookies` | `PostCookieJarV1Cookies` |  |

## CrashReporting（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/crash-reporting/v1/crash/status` | `GetCrashReportingV1CrashStatus` |  |

## DataStore（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/data-store/v1/install/dir` | `GetDataStoreV1InstallDir` |  |
| GET | `/data-store/v1/install/settings/by/path` | `GetDataStoreV1InstallSettingsByPath` |  |
| POST | `/data-store/v1/install/settings/by/path` | `PostDataStoreV1InstallSettingsByPath` |  |
| GET | `/data-store/v1/system/settings/by/path` | `GetDataStoreV1SystemSettingsByPath` |  |

## DeepLinks（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/deep-links/v1/launch/lor/link` | `PostDeepLinksV1LaunchLorLink` |  |
| GET | `/deep-links/v1/settings` | `GetDeepLinksV1Settings` |  |

## Entitlements（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/entitlements/v1/entitlements` | `GetEntitlementsV1Entitlements` |  |
| GET | `/entitlements/v1/token` | `GetEntitlementsV1Token` |  |

## ErrorMonitor（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/error-monitor/v1/logs/batches` | `PostErrorMonitorV1LogsBatches` |  |
| GET | `/error-monitor/v1/logs/changed` | `GetErrorMonitorV1LogsChanged` |  |
| POST | `/error-monitor/v1/ux/logs` | `PostErrorMonitorV1UxLogs` |  |

## LolAccountVerification（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-account-verification/v1/confirm/activation/pin` | `PostLolAccountVerificationV1ConfirmActivationPin` |  |
| POST | `/lol-account-verification/v1/confirm/deactivation/pin` | `PostLolAccountVerificationV1ConfirmDeactivationPin` |  |
| GET | `/lol-account-verification/v1/is/verified` | `GetLolAccountVerificationV1IsVerified` |  |
| GET | `/lol-account-verification/v1/phone/number` | `GetLolAccountVerificationV1PhoneNumber` |  |
| POST | `/lol-account-verification/v1/send/activation/pin` | `PostLolAccountVerificationV1SendActivationPin` |  |
| POST | `/lol-account-verification/v1/send/deactivation/pin` | `PostLolAccountVerificationV1SendDeactivationPin` |  |

## LolActiveBoosts（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-active-boosts/v1/active/boosts` | `GetLolActiveBoostsV1ActiveBoosts` |  |

## LolActivityCenter（9）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-activity-center/v1/clear/cache` | `PostLolActivityCenterV1ClearCache` |  |
| GET | `/lol-activity-center/v1/config/data` | `GetLolActivityCenterV1ConfigData` |  |
| GET | `/lol-activity-center/v1/content/by/page/name` | `GetLolActivityCenterV1ContentByPageName` |  |
| GET | `/lol-activity-center/v1/content/client/nav` | `GetLolActivityCenterV1ContentClientNav` |  |
| GET | `/lol-activity-center/v1/jade/home/tencent` | `GetLolActivityCenterV1JadeHomeTencent` |  |
| GET | `/lol-activity-center/v1/jade/home/tencent/ready` | `GetLolActivityCenterV1JadeHomeTencentReady` |  |
| GET | `/lol-activity-center/v1/jade/ready` | `GetLolActivityCenterV1JadeReady` |  |
| GET | `/lol-activity-center/v1/overrides` | `GetLolActivityCenterV1Overrides` |  |
| GET | `/lol-activity-center/v1/ready` | `GetLolActivityCenterV1Ready` |  |

## LolAntiAddiction（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-anti-addiction/v1/anti/addiction/token` | `GetLolAntiAddictionV1AntiAddictionToken` |  |

## LolBanners（5）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-banners/v1/current/summoner/flags` | `GetLolBannersV1CurrentSummonerFlags` |  |
| GET | `/lol-banners/v1/current/summoner/flags/equipped` | `GetLolBannersV1CurrentSummonerFlagsEquipped` |  |
| PUT | `/lol-banners/v1/current/summoner/flags/equipped` | `PutLolBannersV1CurrentSummonerFlagsEquipped` |  |
| GET | `/lol-banners/v1/current/summoner/frames/equipped` | `GetLolBannersV1CurrentSummonerFramesEquipped` |  |
| GET | `/lol-banners/v1/players/by/puuid/flags/equipped` | `GetLolBannersV1PlayersByPuuidFlagsEquipped` |  |

## LolCapMissions（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-cap-missions/v1/getmissions` | `GetLolCapMissionsV1Getmissions` |  |
| POST | `/lol-cap-missions/v1/markasviewed` | `PostLolCapMissionsV1Markasviewed` |  |
| GET | `/lol-cap-missions/v1/ready` | `GetLolCapMissionsV1Ready` |  |

## LolCatalog（8）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-catalog/v1/item/details` | `GetLolCatalogV1ItemDetails` |  |
| GET | `/lol-catalog/v1/items` | `GetLolCatalogV1Items` |  |
| GET | `/lol-catalog/v1/items/by/bundle/id/related/items` | `GetLolCatalogV1ItemsByBundleIdRelatedItems` |  |
| GET | `/lol-catalog/v1/items/by/inventory/type` | `GetLolCatalogV1ItemsByInventoryType` |  |
| GET | `/lol-catalog/v1/items/by/skin/id/related/bundles` | `GetLolCatalogV1ItemsBySkinIdRelatedBundles` |  |
| GET | `/lol-catalog/v1/items/list/details` | `GetLolCatalogV1ItemsListDetails` |  |
| GET | `/lol-catalog/v1/items/list/details/skip/cache` | `GetLolCatalogV1ItemsListDetailsSkipCache` |  |
| GET | `/lol-catalog/v1/ready` | `GetLolCatalogV1Ready` |  |

## LolChallenges（17）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-challenges/v1/ack/challenge/update/by/id` | `PostLolChallengesV1AckChallengeUpdateById` |  |
| GET | `/lol-challenges/v1/available/queue/ids` | `GetLolChallengesV1AvailableQueueIds` |  |
| GET | `/lol-challenges/v1/challenges/category/data` | `GetLolChallengesV1ChallengesCategoryData` |  |
| GET | `/lol-challenges/v1/challenges/local/player` | `GetLolChallengesV1ChallengesLocalPlayer` |  |
| GET | `/lol-challenges/v1/client/state` | `GetLolChallengesV1ClientState` |  |
| GET | `/lol-challenges/v1/level/points` | `GetLolChallengesV1LevelPoints` |  |
| GET | `/lol-challenges/v1/my/updated/challenges/by/game/id` | `GetLolChallengesV1MyUpdatedChallengesByGameId` |  |
| GET | `/lol-challenges/v1/penalty` | `GetLolChallengesV1Penalty` |  |
| POST | `/lol-challenges/v1/rsbot/challenges/by/game/id` | `PostLolChallengesV1RsbotChallengesByGameId` |  |
| GET | `/lol-challenges/v1/seasons` | `GetLolChallengesV1Seasons` |  |
| GET | `/lol-challenges/v1/summary/player/data/local/player` | `GetLolChallengesV1SummaryPlayerDataLocalPlayer` |  |
| GET | `/lol-challenges/v1/summary/player/data/player/by/puuid` | `GetLolChallengesV1SummaryPlayerDataPlayerByPuuid` |  |
| GET | `/lol-challenges/v1/summary/players/data/players` | `GetLolChallengesV1SummaryPlayersDataPlayers` |  |
| POST | `/lol-challenges/v1/update/player/preferences` | `PostLolChallengesV1UpdatePlayerPreferences` |  |
| GET | `/lol-challenges/v1/updated/challenges/by/game/id/by/puuid` | `GetLolChallengesV1UpdatedChallengesByGameIdByPuuid` |  |
| GET | `/lol-challenges/v2/titles/all` | `GetLolChallengesV2TitlesAll` |  |
| GET | `/lol-challenges/v2/titles/local/player` | `GetLolChallengesV2TitlesLocalPlayer` |  |

## LolChampSelect（51）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-champ-select/v1/all/grid/champions` | `GetLolChampSelectV1AllGridChampions` |  |
| GET | `/lol-champ-select/v1/aw/set` | `GetLolChampSelectV1AwSet` |  |
| GET | `/lol-champ-select/v1/bannable/champion/ids` | `GetLolChampSelectV1BannableChampionIds` |  |
| GET | `/lol-champ-select/v1/current/champion` | `GetLolChampSelectV1CurrentChampion` |  |
| GET | `/lol-champ-select/v1/disabled/champion/ids` | `GetLolChampSelectV1DisabledChampionIds` |  |
| GET | `/lol-champ-select/v1/grid/champions/by/champion/id` | `GetLolChampSelectV1GridChampionsByChampionId` |  |
| GET | `/lol-champ-select/v1/muted/players` | `GetLolChampSelectV1MutedPlayers` |  |
| GET | `/lol-champ-select/v1/ongoing/champion/swap` | `GetLolChampSelectV1OngoingChampionSwap` |  |
| POST | `/lol-champ-select/v1/ongoing/champion/swap/by/id/clear` | `PostLolChampSelectV1OngoingChampionSwapByIdClear` |  |
| GET | `/lol-champ-select/v1/ongoing/pick/order/swap` | `GetLolChampSelectV1OngoingPickOrderSwap` |  |
| POST | `/lol-champ-select/v1/ongoing/pick/order/swap/by/id/clear` | `PostLolChampSelectV1OngoingPickOrderSwapByIdClear` |  |
| GET | `/lol-champ-select/v1/ongoing/position/swap` | `GetLolChampSelectV1OngoingPositionSwap` |  |
| POST | `/lol-champ-select/v1/ongoing/position/swap/by/id/clear` | `PostLolChampSelectV1OngoingPositionSwapByIdClear` |  |
| GET | `/lol-champ-select/v1/pickable/champion/ids` | `GetLolChampSelectV1PickableChampionIds` |  |
| GET | `/lol-champ-select/v1/pickable/skin/ids` | `GetLolChampSelectV1PickableSkinIds` |  |
| GET | `/lol-champ-select/v1/pin/drop/notification` | `GetLolChampSelectV1PinDropNotification` |  |
| POST | `/lol-champ-select/v1/retrieve/latest/game/dto` | `PostLolChampSelectV1RetrieveLatestGameDto` |  |
| GET | `/lol-champ-select/v1/session` | `GetLolChampSelectV1Session` |  |
| PATCH | `/lol-champ-select/v1/session/actions/by/id` | `PatchLolChampSelectV1SessionActionsById` |  |
| POST | `/lol-champ-select/v1/session/actions/by/id/complete` | `PostLolChampSelectV1SessionActionsByIdComplete` |  |
| POST | `/lol-champ-select/v1/session/bench/swap/by/champion/id` | `PostLolChampSelectV1SessionBenchSwapByChampionId` |  |
| GET | `/lol-champ-select/v1/session/champion/swaps` | `GetLolChampSelectV1SessionChampionSwaps` |  |
| GET | `/lol-champ-select/v1/session/champion/swaps/by/id` | `GetLolChampSelectV1SessionChampionSwapsById` |  |
| POST | `/lol-champ-select/v1/session/champion/swaps/by/id/accept` | `PostLolChampSelectV1SessionChampionSwapsByIdAccept` |  |
| POST | `/lol-champ-select/v1/session/champion/swaps/by/id/cancel` | `PostLolChampSelectV1SessionChampionSwapsByIdCancel` |  |
| POST | `/lol-champ-select/v1/session/champion/swaps/by/id/decline` | `PostLolChampSelectV1SessionChampionSwapsByIdDecline` |  |
| POST | `/lol-champ-select/v1/session/champion/swaps/by/id/request` | `PostLolChampSelectV1SessionChampionSwapsByIdRequest` |  |
| GET | `/lol-champ-select/v1/session/my/selection` | `GetLolChampSelectV1SessionMySelection` |  |
| PATCH | `/lol-champ-select/v1/session/my/selection` | `PatchLolChampSelectV1SessionMySelection` |  |
| POST | `/lol-champ-select/v1/session/my/selection/reroll` | `PostLolChampSelectV1SessionMySelectionReroll` |  |
| GET | `/lol-champ-select/v1/session/pick/order/swaps` | `GetLolChampSelectV1SessionPickOrderSwaps` |  |
| GET | `/lol-champ-select/v1/session/pick/order/swaps/by/id` | `GetLolChampSelectV1SessionPickOrderSwapsById` |  |
| POST | `/lol-champ-select/v1/session/pick/order/swaps/by/id/accept` | `PostLolChampSelectV1SessionPickOrderSwapsByIdAccept` |  |
| POST | `/lol-champ-select/v1/session/pick/order/swaps/by/id/cancel` | `PostLolChampSelectV1SessionPickOrderSwapsByIdCancel` |  |
| POST | `/lol-champ-select/v1/session/pick/order/swaps/by/id/decline` | `PostLolChampSelectV1SessionPickOrderSwapsByIdDecline` |  |
| POST | `/lol-champ-select/v1/session/pick/order/swaps/by/id/request` | `PostLolChampSelectV1SessionPickOrderSwapsByIdRequest` |  |
| GET | `/lol-champ-select/v1/session/position/swaps` | `GetLolChampSelectV1SessionPositionSwaps` |  |
| POST | `/lol-champ-select/v1/session/position/swaps/by/id/accept` | `PostLolChampSelectV1SessionPositionSwapsByIdAccept` |  |
| POST | `/lol-champ-select/v1/session/position/swaps/by/id/cancel` | `PostLolChampSelectV1SessionPositionSwapsByIdCancel` |  |
| POST | `/lol-champ-select/v1/session/position/swaps/by/id/decline` | `PostLolChampSelectV1SessionPositionSwapsByIdDecline` |  |
| POST | `/lol-champ-select/v1/session/position/swaps/by/id/request` | `PostLolChampSelectV1SessionPositionSwapsByIdRequest` |  |
| POST | `/lol-champ-select/v1/session/simple/inventory` | `PostLolChampSelectV1SessionSimpleInventory` |  |
| GET | `/lol-champ-select/v1/session/timer` | `GetLolChampSelectV1SessionTimer` |  |
| GET | `/lol-champ-select/v1/sfx/notifications` | `GetLolChampSelectV1SfxNotifications` |  |
| GET | `/lol-champ-select/v1/skin/carousel/skins` | `GetLolChampSelectV1SkinCarouselSkins` |  |
| GET | `/lol-champ-select/v1/skin/selector/info` | `GetLolChampSelectV1SkinSelectorInfo` |  |
| GET | `/lol-champ-select/v1/summoners/by/slot/id` | `GetLolChampSelectV1SummonersBySlotId` |  |
| GET | `/lol-champ-select/v1/team/boost` | `GetLolChampSelectV1TeamBoost` |  |
| POST | `/lol-champ-select/v1/team/boost/purchase` | `PostLolChampSelectV1TeamBoostPurchase` |  |
| POST | `/lol-champ-select/v1/toggle/favorite/by/champion/id/by/position` | `PostLolChampSelectV1ToggleFavoriteByChampionIdByPosition` |  |
| POST | `/lol-champ-select/v1/toggle/player/muted` | `PostLolChampSelectV1TogglePlayerMuted` |  |

## LolChampionMastery（12）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-champion-mastery/v1/by/puuid/champion/mastery` | `GetLolChampionMasteryV1ByPuuidChampionMastery` |  |
| POST | `/lol-champion-mastery/v1/by/puuid/champion/mastery/top` | `PostLolChampionMasteryV1ByPuuidChampionMasteryTop` |  |
| POST | `/lol-champion-mastery/v1/by/puuid/champion/mastery/view` | `PostLolChampionMasteryV1ByPuuidChampionMasteryView` |  |
| POST | `/lol-champion-mastery/v1/by/puuid/champion/mastery/view/top` | `PostLolChampionMasteryV1ByPuuidChampionMasteryViewTop` |  |
| GET | `/lol-champion-mastery/v1/local/player/champion/mastery` | `GetLolChampionMasteryV1LocalPlayerChampionMastery` |  |
| GET | `/lol-champion-mastery/v1/local/player/champion/mastery/score` | `GetLolChampionMasteryV1LocalPlayerChampionMasteryScore` |  |
| GET | `/lol-champion-mastery/v1/local/player/champion/mastery/sets/and/rewards` | `GetLolChampionMasteryV1LocalPlayerChampionMasterySetsAndRewards` |  |
| GET | `/lol-champion-mastery/v1/notifications` | `GetLolChampionMasteryV1Notifications` |  |
| POST | `/lol-champion-mastery/v1/notifications/ack` | `PostLolChampionMasteryV1NotificationsAck` |  |
| GET | `/lol-champion-mastery/v1/reward/grants` | `GetLolChampionMasteryV1RewardGrants` |  |
| DELETE | `/lol-champion-mastery/v1/reward/grants/by/id` | `DeleteLolChampionMasteryV1RewardGrantsById` |  |
| POST | `/lol-champion-mastery/v1/scouting` | `PostLolChampionMasteryV1Scouting` |  |

## LolChampions（11）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-champions/v1/inventories/by/summoner/id/by/queue/id/champions/minimal/per/queue` | `GetLolChampionsV1InventoriesBySummonerIdByQueueIdChampionsMinimalPerQueue` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/by/queue/id/champions/per/queue` | `GetLolChampionsV1InventoriesBySummonerIdByQueueIdChampionsPerQueue` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/champions` | `GetLolChampionsV1InventoriesBySummonerIdChampions` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/champions/by/champion/id` | `GetLolChampionsV1InventoriesBySummonerIdChampionsByChampionId` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/champions/by/champion/id/skins` | `GetLolChampionsV1InventoriesBySummonerIdChampionsByChampionIdSkins` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/champions/by/champion/id/skins/by/champion/skin/id` | `GetLolChampionsV1InventoriesBySummonerIdChampionsByChampionIdSkinsByChampionSkinId` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/champions/by/champion/id/skins/by/skin/id/chromas` | `GetLolChampionsV1InventoriesBySummonerIdChampionsByChampionIdSkinsBySkinIdChromas` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/champions/minimal` | `GetLolChampionsV1InventoriesBySummonerIdChampionsMinimal` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/champions/playable/count` | `GetLolChampionsV1InventoriesBySummonerIdChampionsPlayableCount` |  |
| GET | `/lol-champions/v1/inventories/by/summoner/id/skins/minimal` | `GetLolChampionsV1InventoriesBySummonerIdSkinsMinimal` |  |
| GET | `/lol-champions/v1/owned/champions/minimal` | `GetLolChampionsV1OwnedChampionsMinimal` |  |

## LolChat（58）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-chat/v1/blocked/players` | `GetLolChatV1BlockedPlayers` |  |
| POST | `/lol-chat/v1/blocked/players` | `PostLolChatV1BlockedPlayers` |  |
| DELETE | `/lol-chat/v1/blocked/players/by/id` | `DeleteLolChatV1BlockedPlayersById` |  |
| GET | `/lol-chat/v1/blocked/players/by/id` | `GetLolChatV1BlockedPlayersById` |  |
| GET | `/lol-chat/v1/config` | `GetLolChatV1Config` |  |
| GET | `/lol-chat/v1/conversations` | `GetLolChatV1Conversations` |  |
| POST | `/lol-chat/v1/conversations` | `PostLolChatV1Conversations` |  |
| DELETE | `/lol-chat/v1/conversations/active` | `DeleteLolChatV1ConversationsActive` |  |
| GET | `/lol-chat/v1/conversations/active` | `GetLolChatV1ConversationsActive` |  |
| PUT | `/lol-chat/v1/conversations/active` | `PutLolChatV1ConversationsActive` |  |
| GET | `/lol-chat/v1/conversations/by/cid/participants` | `GetLolChatV1ConversationsByCidParticipants` |  |
| POST | `/lol-chat/v1/conversations/by/cid/participants` | `PostLolChatV1ConversationsByCidParticipants` |  |
| GET | `/lol-chat/v1/conversations/by/cid/participants/by/pid` | `GetLolChatV1ConversationsByCidParticipantsByPid` |  |
| DELETE | `/lol-chat/v1/conversations/by/id` | `DeleteLolChatV1ConversationsById` |  |
| GET | `/lol-chat/v1/conversations/by/id` | `GetLolChatV1ConversationsById` |  |
| PUT | `/lol-chat/v1/conversations/by/id` | `PutLolChatV1ConversationsById` |  |
| DELETE | `/lol-chat/v1/conversations/by/id/messages` | `DeleteLolChatV1ConversationsByIdMessages` |  |
| GET | `/lol-chat/v1/conversations/by/id/messages` | `GetLolChatV1ConversationsByIdMessages` |  |
| POST | `/lol-chat/v1/conversations/by/id/messages` | `PostLolChatV1ConversationsByIdMessages` |  |
| GET | `/lol-chat/v1/conversations/by/id/messages/by/mid` | `GetLolChatV1ConversationsByIdMessagesByMid` |  |
| POST | `/lol-chat/v1/conversations/eog/chat/toggle` | `PostLolChatV1ConversationsEogChatToggle` |  |
| GET | `/lol-chat/v1/conversations/notify` | `GetLolChatV1ConversationsNotify` |  |
| POST | `/lol-chat/v1/discord/link` | `PostLolChatV1DiscordLink` |  |
| GET | `/lol-chat/v1/discord/link/status` | `GetLolChatV1DiscordLinkStatus` |  |
| GET | `/lol-chat/v1/errors` | `GetLolChatV1Errors` |  |
| DELETE | `/lol-chat/v1/errors/by/id` | `DeleteLolChatV1ErrorsById` |  |
| GET | `/lol-chat/v1/friend/counts` | `GetLolChatV1FriendCounts` |  |
| GET | `/lol-chat/v1/friend/exists/by/summoner/id` | `GetLolChatV1FriendExistsBySummonerId` |  |
| GET | `/lol-chat/v1/friend/groups` | `GetLolChatV1FriendGroups` |  |
| POST | `/lol-chat/v1/friend/groups` | `PostLolChatV1FriendGroups` |  |
| DELETE | `/lol-chat/v1/friend/groups/by/id` | `DeleteLolChatV1FriendGroupsById` |  |
| GET | `/lol-chat/v1/friend/groups/by/id` | `GetLolChatV1FriendGroupsById` |  |
| PUT | `/lol-chat/v1/friend/groups/by/id` | `PutLolChatV1FriendGroupsById` |  |
| PUT | `/lol-chat/v1/friend/groups/order` | `PutLolChatV1FriendGroupsOrder` |  |
| GET | `/lol-chat/v1/friends` | `GetLolChatV1Friends` |  |
| DELETE | `/lol-chat/v1/friends/by/id` | `DeleteLolChatV1FriendsById` |  |
| GET | `/lol-chat/v1/friends/by/id` | `GetLolChatV1FriendsById` |  |
| PUT | `/lol-chat/v1/friends/by/id` | `PutLolChatV1FriendsById` |  |
| GET | `/lol-chat/v1/is/discord/integration/enabled` | `GetLolChatV1IsDiscordIntegrationEnabled` |  |
| GET | `/lol-chat/v1/is/discord/link/available` | `GetLolChatV1IsDiscordLinkAvailable` |  |
| GET | `/lol-chat/v1/is/discord/linked` | `GetLolChatV1IsDiscordLinked` |  |
| GET | `/lol-chat/v1/me` | `GetLolChatV1Me` |  |
| PUT | `/lol-chat/v1/me` | `PutLolChatV1Me` |  |
| GET | `/lol-chat/v1/obfuscated/puuids` | `GetLolChatV1ObfuscatedPuuids` |  |
| GET | `/lol-chat/v1/obfuscated/summoner/ids` | `GetLolChatV1ObfuscatedSummonerIds` |  |
| DELETE | `/lol-chat/v1/player/mutes` | `DeleteLolChatV1PlayerMutes` |  |
| GET | `/lol-chat/v1/player/mutes` | `GetLolChatV1PlayerMutes` |  |
| POST | `/lol-chat/v1/player/mutes` | `PostLolChatV1PlayerMutes` |  |
| GET | `/lol-chat/v1/resources` | `GetLolChatV1Resources` |  |
| GET | `/lol-chat/v1/session` | `GetLolChatV1Session` |  |
| GET | `/lol-chat/v1/settings` | `GetLolChatV1Settings` |  |
| PUT | `/lol-chat/v1/settings` | `PutLolChatV1Settings` |  |
| POST | `/lol-chat/v1/system/mutes` | `PostLolChatV1SystemMutes` |  |
| GET | `/lol-chat/v2/friend/exists/by/puuid` | `GetLolChatV2FriendExistsByPuuid` |  |
| GET | `/lol-chat/v2/friend/requests` | `GetLolChatV2FriendRequests` |  |
| POST | `/lol-chat/v2/friend/requests` | `PostLolChatV2FriendRequests` |  |
| DELETE | `/lol-chat/v2/friend/requests/by/puuid` | `DeleteLolChatV2FriendRequestsByPuuid` |  |
| PUT | `/lol-chat/v2/friend/requests/by/puuid` | `PutLolChatV2FriendRequestsByPuuid` |  |

## LolClash（88）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-clash/v1/all/tournaments` | `GetLolClashV1AllTournaments` |  |
| GET | `/lol-clash/v1/awaiting/resent/eog` | `GetLolClashV1AwaitingResentEog` |  |
| GET | `/lol-clash/v1/bracket/by/bracket/id` | `GetLolClashV1BracketByBracketId` |  |
| GET | `/lol-clash/v1/checkin/allowed` | `GetLolClashV1CheckinAllowed` |  |
| GET | `/lol-clash/v1/current/tournament/ids` | `GetLolClashV1CurrentTournamentIds` |  |
| GET | `/lol-clash/v1/disabled/config` | `GetLolClashV1DisabledConfig` |  |
| GET | `/lol-clash/v1/enabled` | `GetLolClashV1Enabled` |  |
| GET | `/lol-clash/v1/eog/player/update` | `GetLolClashV1EogPlayerUpdate` |  |
| POST | `/lol-clash/v1/eog/player/update/acknowledge` | `PostLolClashV1EogPlayerUpdateAcknowledge` |  |
| GET | `/lol-clash/v1/event/by/uuid` | `GetLolClashV1EventByUuid` |  |
| POST | `/lol-clash/v1/events` | `PostLolClashV1Events` |  |
| GET | `/lol-clash/v1/game/end` | `GetLolClashV1GameEnd` |  |
| POST | `/lol-clash/v1/game/end/acknowledge` | `PostLolClashV1GameEndAcknowledge` |  |
| GET | `/lol-clash/v1/historyandwinners` | `GetLolClashV1Historyandwinners` |  |
| GET | `/lol-clash/v1/iconconfig` | `GetLolClashV1Iconconfig` |  |
| GET | `/lol-clash/v1/invited/roster/ids` | `GetLolClashV1InvitedRosterIds` |  |
| POST | `/lol-clash/v1/lft/player` | `PostLolClashV1LftPlayer` |  |
| POST | `/lol-clash/v1/lft/player/find` | `PostLolClashV1LftPlayerFind` |  |
| POST | `/lol-clash/v1/lft/team` | `PostLolClashV1LftTeam` |  |
| POST | `/lol-clash/v1/lft/team/by/roster/id/request` | `PostLolClashV1LftTeamByRosterIdRequest` |  |
| POST | `/lol-clash/v1/lft/team/fetch/requests` | `PostLolClashV1LftTeamFetchRequests` |  |
| POST | `/lol-clash/v1/lft/team/find` | `PostLolClashV1LftTeamFind` |  |
| GET | `/lol-clash/v1/lft/team/requests` | `GetLolClashV1LftTeamRequests` |  |
| GET | `/lol-clash/v1/notifications` | `GetLolClashV1Notifications` |  |
| POST | `/lol-clash/v1/notifications/acknowledge` | `PostLolClashV1NotificationsAcknowledge` |  |
| GET | `/lol-clash/v1/ping` | `GetLolClashV1Ping` |  |
| GET | `/lol-clash/v1/player` | `GetLolClashV1Player` |  |
| GET | `/lol-clash/v1/player/chat/rosters` | `GetLolClashV1PlayerChatRosters` |  |
| GET | `/lol-clash/v1/player/history` | `GetLolClashV1PlayerHistory` |  |
| GET | `/lol-clash/v1/playmode/restricted` | `GetLolClashV1PlaymodeRestricted` |  |
| GET | `/lol-clash/v1/ready` | `GetLolClashV1Ready` |  |
| POST | `/lol-clash/v1/refresh` | `PostLolClashV1Refresh` |  |
| GET | `/lol-clash/v1/rewards` | `GetLolClashV1Rewards` |  |
| GET | `/lol-clash/v1/roster/by/roster/id` | `GetLolClashV1RosterByRosterId` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/accept` | `PostLolClashV1RosterByRosterIdAccept` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/cancel/withdraw` | `PostLolClashV1RosterByRosterIdCancelWithdraw` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/change/all/details` | `PostLolClashV1RosterByRosterIdChangeAllDetails` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/change/icon` | `PostLolClashV1RosterByRosterIdChangeIcon` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/change/name` | `PostLolClashV1RosterByRosterIdChangeName` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/change/short/name` | `PostLolClashV1RosterByRosterIdChangeShortName` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/decline` | `PostLolClashV1RosterByRosterIdDecline` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/disband` | `PostLolClashV1RosterByRosterIdDisband` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/invite` | `PostLolClashV1RosterByRosterIdInvite` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/kick` | `PostLolClashV1RosterByRosterIdKick` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/leave` | `PostLolClashV1RosterByRosterIdLeave` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/lockin` | `PostLolClashV1RosterByRosterIdLockin` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/set/position` | `PostLolClashV1RosterByRosterIdSetPosition` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/set/ticket` | `PostLolClashV1RosterByRosterIdSetTicket` |  |
| GET | `/lol-clash/v1/roster/by/roster/id/stats` | `GetLolClashV1RosterByRosterIdStats` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/suggest` | `PostLolClashV1RosterByRosterIdSuggest` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/suggest/by/summoner/id/accept` | `PostLolClashV1RosterByRosterIdSuggestBySummonerIdAccept` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/suggest/by/summoner/id/decline` | `PostLolClashV1RosterByRosterIdSuggestBySummonerIdDecline` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/suggest/by/summoner/id/revoke` | `PostLolClashV1RosterByRosterIdSuggestBySummonerIdRevoke` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/ticket/offer/by/summoner/id/accept` | `PostLolClashV1RosterByRosterIdTicketOfferBySummonerIdAccept` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/ticket/offer/by/summoner/id/decline` | `PostLolClashV1RosterByRosterIdTicketOfferBySummonerIdDecline` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/ticket/offer/by/summoner/id/offer` | `PostLolClashV1RosterByRosterIdTicketOfferBySummonerIdOffer` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/ticket/offer/by/summoner/id/revoke` | `PostLolClashV1RosterByRosterIdTicketOfferBySummonerIdRevoke` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/transfer/captain` | `PostLolClashV1RosterByRosterIdTransferCaptain` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/unlockin` | `PostLolClashV1RosterByRosterIdUnlockin` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/unwithdraw` | `PostLolClashV1RosterByRosterIdUnwithdraw` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/update/logos` | `PostLolClashV1RosterByRosterIdUpdateLogos` |  |
| POST | `/lol-clash/v1/roster/by/roster/id/withdraw` | `PostLolClashV1RosterByRosterIdWithdraw` |  |
| GET | `/lol-clash/v1/scouting/champions` | `GetLolClashV1ScoutingChampions` |  |
| GET | `/lol-clash/v1/scouting/matchhistory` | `GetLolClashV1ScoutingMatchhistory` |  |
| GET | `/lol-clash/v1/season/rewards/by/season/id` | `GetLolClashV1SeasonRewardsBySeasonId` |  |
| GET | `/lol-clash/v1/simple/state/flags` | `GetLolClashV1SimpleStateFlags` |  |
| POST | `/lol-clash/v1/simple/state/flags/by/id/acknowledge` | `PostLolClashV1SimpleStateFlagsByIdAcknowledge` |  |
| GET | `/lol-clash/v1/thirdparty/team/data` | `GetLolClashV1ThirdpartyTeamData` |  |
| GET | `/lol-clash/v1/time` | `GetLolClashV1Time` |  |
| GET | `/lol-clash/v1/tournament/by/tournament/id` | `GetLolClashV1TournamentByTournamentId` |  |
| POST | `/lol-clash/v1/tournament/by/tournament/id/create/roster` | `PostLolClashV1TournamentByTournamentIdCreateRoster` |  |
| GET | `/lol-clash/v1/tournament/by/tournament/id/get/player/tiers` | `GetLolClashV1TournamentByTournamentIdGetPlayerTiers` |  |
| GET | `/lol-clash/v1/tournament/by/tournament/id/player` | `GetLolClashV1TournamentByTournamentIdPlayer` |  |
| GET | `/lol-clash/v1/tournament/by/tournament/id/player/honor/restricted` | `GetLolClashV1TournamentByTournamentIdPlayerHonorRestricted` |  |
| GET | `/lol-clash/v1/tournament/by/tournament/id/state/info` | `GetLolClashV1TournamentByTournamentIdStateInfo` |  |
| GET | `/lol-clash/v1/tournament/by/tournament/id/winners` | `GetLolClashV1TournamentByTournamentIdWinners` |  |
| GET | `/lol-clash/v1/tournament/cancelled` | `GetLolClashV1TournamentCancelled` |  |
| GET | `/lol-clash/v1/tournament/get/all/player/tiers` | `GetLolClashV1TournamentGetAllPlayerTiers` |  |
| GET | `/lol-clash/v1/tournament/state/info` | `GetLolClashV1TournamentStateInfo` |  |
| GET | `/lol-clash/v1/tournament/summary` | `GetLolClashV1TournamentSummary` |  |
| POST | `/lol-clash/v1/update/logos` | `PostLolClashV1UpdateLogos` |  |
| GET | `/lol-clash/v1/visible` | `GetLolClashV1Visible` |  |
| DELETE | `/lol-clash/v1/voice` | `DeleteLolClashV1Voice` |  |
| POST | `/lol-clash/v1/voice` | `PostLolClashV1Voice` |  |
| DELETE | `/lol-clash/v1/voice/delay/by/delay/seconds` | `DeleteLolClashV1VoiceDelayByDelaySeconds` |  |
| POST | `/lol-clash/v1/voice/delay/by/delay/seconds` | `PostLolClashV1VoiceDelayByDelaySeconds` |  |
| GET | `/lol-clash/v1/voice/enabled` | `GetLolClashV1VoiceEnabled` |  |
| GET | `/lol-clash/v2/playmode/restricted` | `GetLolClashV2PlaymodeRestricted` |  |

## LolClientConfig（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-client-config/v3/client/config/by/name` | `GetLolClientConfigV3ClientConfigByName` |  |

## LolCollections（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-collections/v1/inventories/by/summoner/id/backdrop` | `GetLolCollectionsV1InventoriesBySummonerIdBackdrop` |  |
| GET | `/lol-collections/v1/inventories/by/summoner/id/spells` | `GetLolCollectionsV1InventoriesBySummonerIdSpells` |  |
| GET | `/lol-collections/v1/inventories/by/summoner/id/ward/skins` | `GetLolCollectionsV1InventoriesBySummonerIdWardSkins` |  |
| GET | `/lol-collections/v1/inventories/by/summoner/id/ward/skins/by/ward/skin/id` | `GetLolCollectionsV1InventoriesBySummonerIdWardSkinsByWardSkinId` |  |

## LolCosmetics（26）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| DELETE | `/lol-cosmetics/v1/favorites/tft/by/cosmetic/type/by/content/id` | `DeleteLolCosmeticsV1FavoritesTftByCosmeticTypeByContentId` |  |
| PUT | `/lol-cosmetics/v1/favorites/tft/by/cosmetic/type/by/content/id` | `PutLolCosmeticsV1FavoritesTftByCosmeticTypeByContentId` |  |
| GET | `/lol-cosmetics/v1/favorites/tft/companions` | `GetLolCosmeticsV1FavoritesTftCompanions` |  |
| GET | `/lol-cosmetics/v1/favorites/tft/damage/skins` | `GetLolCosmeticsV1FavoritesTftDamageSkins` |  |
| GET | `/lol-cosmetics/v1/favorites/tft/map/skins` | `GetLolCosmeticsV1FavoritesTftMapSkins` |  |
| PUT | `/lol-cosmetics/v1/favorites/tft/save` | `PutLolCosmeticsV1FavoritesTftSave` |  |
| GET | `/lol-cosmetics/v1/favorites/tft/zoom/skins` | `GetLolCosmeticsV1FavoritesTftZoomSkins` |  |
| GET | `/lol-cosmetics/v1/inventories/by/set/name/augment/pillars` | `GetLolCosmeticsV1InventoriesBySetNameAugmentPillars` |  |
| GET | `/lol-cosmetics/v1/inventories/by/set/name/companions` | `GetLolCosmeticsV1InventoriesBySetNameCompanions` |  |
| GET | `/lol-cosmetics/v1/inventories/by/set/name/damage/skins` | `GetLolCosmeticsV1InventoriesBySetNameDamageSkins` |  |
| GET | `/lol-cosmetics/v1/inventories/by/set/name/map/skins` | `GetLolCosmeticsV1InventoriesBySetNameMapSkins` |  |
| GET | `/lol-cosmetics/v1/inventories/by/set/name/playbooks` | `GetLolCosmeticsV1InventoriesBySetNamePlaybooks` |  |
| GET | `/lol-cosmetics/v1/inventories/by/set/name/zoom/skins` | `GetLolCosmeticsV1InventoriesBySetNameZoomSkins` |  |
| PATCH | `/lol-cosmetics/v1/recent/by/type` | `PatchLolCosmeticsV1RecentByType` |  |
| DELETE | `/lol-cosmetics/v1/selection/companion` | `DeleteLolCosmeticsV1SelectionCompanion` |  |
| PUT | `/lol-cosmetics/v1/selection/companion` | `PutLolCosmeticsV1SelectionCompanion` |  |
| DELETE | `/lol-cosmetics/v1/selection/playbook` | `DeleteLolCosmeticsV1SelectionPlaybook` |  |
| PUT | `/lol-cosmetics/v1/selection/playbook` | `PutLolCosmeticsV1SelectionPlaybook` |  |
| DELETE | `/lol-cosmetics/v1/selection/tft/augment/pillar` | `DeleteLolCosmeticsV1SelectionTftAugmentPillar` |  |
| PUT | `/lol-cosmetics/v1/selection/tft/augment/pillar` | `PutLolCosmeticsV1SelectionTftAugmentPillar` |  |
| DELETE | `/lol-cosmetics/v1/selection/tft/damage/skin` | `DeleteLolCosmeticsV1SelectionTftDamageSkin` |  |
| PUT | `/lol-cosmetics/v1/selection/tft/damage/skin` | `PutLolCosmeticsV1SelectionTftDamageSkin` |  |
| DELETE | `/lol-cosmetics/v1/selection/tft/map/skin` | `DeleteLolCosmeticsV1SelectionTftMapSkin` |  |
| PUT | `/lol-cosmetics/v1/selection/tft/map/skin` | `PutLolCosmeticsV1SelectionTftMapSkin` |  |
| DELETE | `/lol-cosmetics/v1/selection/tft/zoom/skin` | `DeleteLolCosmeticsV1SelectionTftZoomSkin` |  |
| PUT | `/lol-cosmetics/v1/selection/tft/zoom/skin` | `PutLolCosmeticsV1SelectionTftZoomSkin` |  |

## LolDirectxUpgradeNeedsHardwareUpgrade（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-directx-upgrade-needs-hardware-upgrade` | `GetLolDirectxUpgradeNeedsHardwareUpgrade` |  |

## LolDirectxUpgradeNotificationAck（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-directx-upgrade-notification-ack` | `PostLolDirectxUpgradeNotificationAck` |  |

## LolDirectxUpgradeNotificationType（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-directx-upgrade-notification-type` | `GetLolDirectxUpgradeNotificationType` |  |

## LolDrops（8）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-drops/v1/drop/tables` | `GetLolDropsV1DropTables` |  |
| GET | `/lol-drops/v1/drop/tables/by/drop/table/id` | `GetLolDropsV1DropTablesByDropTableId` |  |
| GET | `/lol-drops/v1/drop/tables/by/drop/table/id/odds/list` | `GetLolDropsV1DropTablesByDropTableIdOddsList` |  |
| GET | `/lol-drops/v1/drop/tables/by/drop/table/id/odds/tree` | `GetLolDropsV1DropTablesByDropTableIdOddsTree` |  |
| GET | `/lol-drops/v1/drop/tables/by/drop/table/id/players/by/player/id/pity/count` | `GetLolDropsV1DropTablesByDropTableIdPlayersByPlayerIdPityCount` |  |
| GET | `/lol-drops/v1/players/by/player/id/pity/counts` | `GetLolDropsV1PlayersByPlayerIdPityCounts` |  |
| GET | `/lol-drops/v1/players/by/player/id/total/rolls/counts` | `GetLolDropsV1PlayersByPlayerIdTotalRollsCounts` |  |
| GET | `/lol-drops/v1/ready` | `GetLolDropsV1Ready` |  |

## LolEmailVerification（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-email-verification/v1/confirm/email` | `PostLolEmailVerificationV1ConfirmEmail` |  |
| GET | `/lol-email-verification/v1/email` | `GetLolEmailVerificationV1Email` |  |
| PUT | `/lol-email-verification/v1/email` | `PutLolEmailVerificationV1Email` |  |
| GET | `/lol-email-verification/v1/email/email/verification/status` | `GetLolEmailVerificationV1EmailEmailVerificationStatus` |  |

## LolEndOfGame（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-end-of-game/v1/champion/mastery/updates` | `GetLolEndOfGameV1ChampionMasteryUpdates` |  |
| GET | `/lol-end-of-game/v1/eog/stats/block` | `GetLolEndOfGameV1EogStatsBlock` |  |
| GET | `/lol-end-of-game/v1/gameclient/eog/stats/block` | `GetLolEndOfGameV1GameclientEogStatsBlock` |  |
| POST | `/lol-end-of-game/v1/gameclient/eog/stats/block` | `PostLolEndOfGameV1GameclientEogStatsBlock` |  |
| POST | `/lol-end-of-game/v1/state/dismiss/stats` | `PostLolEndOfGameV1StateDismissStats` |  |
| GET | `/lol-end-of-game/v1/tft/eog/stats` | `GetLolEndOfGameV1TftEogStats` |  |

## LolEsportStreamNotifications（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-esport-stream-notifications/v1/live/streams` | `GetLolEsportStreamNotificationsV1LiveStreams` |  |
| POST | `/lol-esport-stream-notifications/v1/send/stats` | `PostLolEsportStreamNotificationsV1SendStats` |  |
| GET | `/lol-esport-stream-notifications/v1/stream/url` | `GetLolEsportStreamNotificationsV1StreamUrl` |  |

## LolEventHub（28）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-event-hub/v1/events` | `GetLolEventHubV1Events` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/chapters` | `GetLolEventHubV1EventsByEventIdChapters` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/event/details/data` | `GetLolEventHubV1EventsByEventIdEventDetailsData` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/info` | `GetLolEventHubV1EventsByEventIdInfo` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/is/grace/period` | `GetLolEventHubV1EventsByEventIdIsGracePeriod` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/narrative` | `GetLolEventHubV1EventsByEventIdNarrative` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/objectives/banner` | `GetLolEventHubV1EventsByEventIdObjectivesBanner` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/pass/background/data` | `GetLolEventHubV1EventsByEventIdPassBackgroundData` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/pass/bundles` | `GetLolEventHubV1EventsByEventIdPassBundles` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/progress/info/data` | `GetLolEventHubV1EventsByEventIdProgressInfoData` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/progression/purchase/data` | `GetLolEventHubV1EventsByEventIdProgressionPurchaseData` |  |
| POST | `/lol-event-hub/v1/events/by/event/id/purchase/offer` | `PostLolEventHubV1EventsByEventIdPurchaseOffer` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/reward/track/bonus/items` | `GetLolEventHubV1EventsByEventIdRewardTrackBonusItems` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/reward/track/bonus/progress` | `GetLolEventHubV1EventsByEventIdRewardTrackBonusProgress` |  |
| POST | `/lol-event-hub/v1/events/by/event/id/reward/track/claim/all` | `PostLolEventHubV1EventsByEventIdRewardTrackClaimAll` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/reward/track/counter` | `GetLolEventHubV1EventsByEventIdRewardTrackCounter` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/reward/track/failure` | `GetLolEventHubV1EventsByEventIdRewardTrackFailure` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/reward/track/items` | `GetLolEventHubV1EventsByEventIdRewardTrackItems` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/reward/track/progress` | `GetLolEventHubV1EventsByEventIdRewardTrackProgress` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/reward/track/unclaimed/rewards` | `GetLolEventHubV1EventsByEventIdRewardTrackUnclaimedRewards` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/reward/track/xp` | `GetLolEventHubV1EventsByEventIdRewardTrackXp` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/token/shop` | `GetLolEventHubV1EventsByEventIdTokenShop` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/token/shop/categories/offers` | `GetLolEventHubV1EventsByEventIdTokenShopCategoriesOffers` |  |
| GET | `/lol-event-hub/v1/events/by/event/id/token/shop/token/balance` | `GetLolEventHubV1EventsByEventIdTokenShopTokenBalance` |  |
| GET | `/lol-event-hub/v1/navigation/button/data` | `GetLolEventHubV1NavigationButtonData` |  |
| POST | `/lol-event-hub/v1/purchase/item` | `PostLolEventHubV1PurchaseItem` |  |
| GET | `/lol-event-hub/v1/skins` | `GetLolEventHubV1Skins` |  |
| GET | `/lol-event-hub/v1/token/upsell` | `GetLolEventHubV1TokenUpsell` |  |

## LolEventMission（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-event-mission/v1/event/mission` | `GetLolEventMissionV1EventMission` |  |

## LolGameClientChat（5）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-game-client-chat/v2/buddies` | `GetLolGameClientChatV2Buddies` |  |
| POST | `/lol-game-client-chat/v2/instant/messages` | `PostLolGameClientChatV2InstantMessages` |  |
| GET | `/lol-game-client-chat/v2/muted/players` | `GetLolGameClientChatV2MutedPlayers` |  |
| GET | `/lol-game-client-chat/v2/parental/controls/status` | `GetLolGameClientChatV2ParentalControlsStatus` |  |
| GET | `/lol-game-client-chat/v3/muted/players` | `GetLolGameClientChatV3MutedPlayers` |  |

## LolGameData（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-game-data/inventory/v1/items/content/ids` | `GetLolGameDataInventoryV1ItemsContentIds` |  |
| GET | `/lol-game-data/inventory/v1/items/content/ids/by/content/id` | `GetLolGameDataInventoryV1ItemsContentIdsByContentId` |  |
| GET | `/lol-game-data/inventory/v1/items/content/ids/inventory/types/by/inventory/type` | `GetLolGameDataInventoryV1ItemsContentIdsInventoryTypesByInventoryType` |  |
| GET | `/lol-game-data/inventory/v1/items/item/ids/inventory/types/by/inventory/type` | `GetLolGameDataInventoryV1ItemsItemIdsInventoryTypesByInventoryType` |  |
| GET | `/lol-game-data/inventory/v1/items/item/ids/inventory/types/by/inventory/type/item/ids/by/item/id` | `GetLolGameDataInventoryV1ItemsItemIdsInventoryTypesByInventoryTypeItemIdsByItemId` |  |
| GET | `/lol-game-data/inventory/v1/ready` | `GetLolGameDataInventoryV1Ready` |  |

## LolGameQueues（7）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-game-queues/v1/custom` | `GetLolGameQueuesV1Custom` |  |
| GET | `/lol-game-queues/v1/custom/non/default` | `GetLolGameQueuesV1CustomNonDefault` |  |
| GET | `/lol-game-queues/v1/matchmaking/queues` | `GetLolGameQueuesV1MatchmakingQueues` |  |
| GET | `/lol-game-queues/v1/queues` | `GetLolGameQueuesV1Queues` |  |
| GET | `/lol-game-queues/v1/queues/by/id` | `GetLolGameQueuesV1QueuesById` |  |
| GET | `/lol-game-queues/v1/queues/by/id/is/custom` | `GetLolGameQueuesV1QueuesByIdIsCustom` |  |
| GET | `/lol-game-queues/v1/queues/type/by/queue/type` | `GetLolGameQueuesV1QueuesTypeByQueueType` |  |

## LolGameSettings（10）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-game-settings/v1/didreset` | `GetLolGameSettingsV1Didreset` |  |
| GET | `/lol-game-settings/v1/game/settings` | `GetLolGameSettingsV1GameSettings` |  |
| PATCH | `/lol-game-settings/v1/game/settings` | `PatchLolGameSettingsV1GameSettings` |  |
| GET | `/lol-game-settings/v1/game/settings/schema` | `GetLolGameSettingsV1GameSettingsSchema` |  |
| GET | `/lol-game-settings/v1/input/settings` | `GetLolGameSettingsV1InputSettings` |  |
| PATCH | `/lol-game-settings/v1/input/settings` | `PatchLolGameSettingsV1InputSettings` |  |
| GET | `/lol-game-settings/v1/input/settings/schema` | `GetLolGameSettingsV1InputSettingsSchema` |  |
| GET | `/lol-game-settings/v1/ready` | `GetLolGameSettingsV1Ready` |  |
| POST | `/lol-game-settings/v1/reload/post/game` | `PostLolGameSettingsV1ReloadPostGame` |  |
| POST | `/lol-game-settings/v1/save` | `PostLolGameSettingsV1Save` |  |

## LolGameflow（51）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-gameflow/v1/ack/failed/to/launch` | `PostLolGameflowV1AckFailedToLaunch` |  |
| GET | `/lol-gameflow/v1/active/patcher/lock` | `GetLolGameflowV1ActivePatcherLock` |  |
| GET | `/lol-gameflow/v1/availability` | `GetLolGameflowV1Availability` |  |
| GET | `/lol-gameflow/v1/basic/tutorial` | `GetLolGameflowV1BasicTutorial` |  |
| GET | `/lol-gameflow/v1/battle/training` | `GetLolGameflowV1BattleTraining` |  |
| POST | `/lol-gameflow/v1/early/exit` | `PostLolGameflowV1EarlyExit` |  |
| GET | `/lol-gameflow/v1/early/exit/enabled` | `GetLolGameflowV1EarlyExitEnabled` |  |
| DELETE | `/lol-gameflow/v1/early/exit/notifications/eog` | `DeleteLolGameflowV1EarlyExitNotificationsEog` |  |
| GET | `/lol-gameflow/v1/early/exit/notifications/eog` | `GetLolGameflowV1EarlyExitNotificationsEog` |  |
| DELETE | `/lol-gameflow/v1/early/exit/notifications/eog/by/key` | `DeleteLolGameflowV1EarlyExitNotificationsEogByKey` |  |
| DELETE | `/lol-gameflow/v1/early/exit/notifications/missions` | `DeleteLolGameflowV1EarlyExitNotificationsMissions` |  |
| GET | `/lol-gameflow/v1/early/exit/notifications/missions` | `GetLolGameflowV1EarlyExitNotificationsMissions` |  |
| DELETE | `/lol-gameflow/v1/early/exit/notifications/missions/by/key` | `DeleteLolGameflowV1EarlyExitNotificationsMissionsByKey` |  |
| GET | `/lol-gameflow/v1/early/exit/quit/enabled` | `GetLolGameflowV1EarlyExitQuitEnabled` |  |
| GET | `/lol-gameflow/v1/extra/game/client/args` | `GetLolGameflowV1ExtraGameClientArgs` |  |
| POST | `/lol-gameflow/v1/extra/game/client/args` | `PostLolGameflowV1ExtraGameClientArgs` |  |
| GET | `/lol-gameflow/v1/game/exit/early/vanguard` | `GetLolGameflowV1GameExitEarlyVanguard` |  |
| GET | `/lol-gameflow/v1/gameflow/metadata/player/status` | `GetLolGameflowV1GameflowMetadataPlayerStatus` |  |
| POST | `/lol-gameflow/v1/gameflow/metadata/player/status` | `PostLolGameflowV1GameflowMetadataPlayerStatus` |  |
| GET | `/lol-gameflow/v1/gameflow/metadata/registration/status` | `GetLolGameflowV1GameflowMetadataRegistrationStatus` |  |
| POST | `/lol-gameflow/v1/gameflow/metadata/registration/status` | `PostLolGameflowV1GameflowMetadataRegistrationStatus` |  |
| POST | `/lol-gameflow/v1/gameflow/monitor` | `PostLolGameflowV1GameflowMonitor` |  |
| GET | `/lol-gameflow/v1/gameflow/phase` | `GetLolGameflowV1GameflowPhase` |  |
| POST | `/lol-gameflow/v1/launch/tft` | `PostLolGameflowV1LaunchTft` |  |
| POST | `/lol-gameflow/v1/launch/tft/spectator` | `PostLolGameflowV1LaunchTftSpectator` |  |
| GET | `/lol-gameflow/v1/player/kicked/vanguard` | `GetLolGameflowV1PlayerKickedVanguard` |  |
| POST | `/lol-gameflow/v1/postgame/rms/message` | `PostLolGameflowV1PostgameRmsMessage` |  |
| POST | `/lol-gameflow/v1/pre/end/game/transition` | `PostLolGameflowV1PreEndGameTransition` |  |
| POST | `/lol-gameflow/v1/reconnect` | `PostLolGameflowV1Reconnect` |  |
| GET | `/lol-gameflow/v1/session` | `GetLolGameflowV1Session` |  |
| POST | `/lol-gameflow/v1/session/champ/select/phase/time/remaining` | `PostLolGameflowV1SessionChampSelectPhaseTimeRemaining` |  |
| POST | `/lol-gameflow/v1/session/dodge` | `PostLolGameflowV1SessionDodge` |  |
| POST | `/lol-gameflow/v1/session/event` | `PostLolGameflowV1SessionEvent` |  |
| POST | `/lol-gameflow/v1/session/game/configuration` | `PostLolGameflowV1SessionGameConfiguration` |  |
| GET | `/lol-gameflow/v1/session/per/position/summoner/spells/disallowed` | `GetLolGameflowV1SessionPerPositionSummonerSpellsDisallowed` |  |
| GET | `/lol-gameflow/v1/session/per/position/summoner/spells/disallowed/as/string` | `GetLolGameflowV1SessionPerPositionSummonerSpellsDisallowedAsString` |  |
| GET | `/lol-gameflow/v1/session/per/position/summoner/spells/required` | `GetLolGameflowV1SessionPerPositionSummonerSpellsRequired` |  |
| GET | `/lol-gameflow/v1/session/per/position/summoner/spells/required/as/string` | `GetLolGameflowV1SessionPerPositionSummonerSpellsRequiredAsString` |  |
| POST | `/lol-gameflow/v1/session/request/enter/gameflow` | `PostLolGameflowV1SessionRequestEnterGameflow` |  |
| POST | `/lol-gameflow/v1/session/request/lobby` | `PostLolGameflowV1SessionRequestLobby` |  |
| POST | `/lol-gameflow/v1/session/request/tournament/checkin` | `PostLolGameflowV1SessionRequestTournamentCheckin` |  |
| POST | `/lol-gameflow/v1/session/tournament/ended` | `PostLolGameflowV1SessionTournamentEnded` |  |
| GET | `/lol-gameflow/v1/spectate` | `GetLolGameflowV1Spectate` |  |
| GET | `/lol-gameflow/v1/spectate/delayed/launch` | `GetLolGameflowV1SpectateDelayedLaunch` |  |
| POST | `/lol-gameflow/v1/spectate/launch` | `PostLolGameflowV1SpectateLaunch` |  |
| POST | `/lol-gameflow/v1/spectate/quit` | `PostLolGameflowV1SpectateQuit` |  |
| GET | `/lol-gameflow/v1/tft/client/state` | `GetLolGameflowV1TftClientState` |  |
| POST | `/lol-gameflow/v1/tick` | `PostLolGameflowV1Tick` |  |
| GET | `/lol-gameflow/v1/watch` | `GetLolGameflowV1Watch` |  |
| POST | `/lol-gameflow/v1/watch/launch` | `PostLolGameflowV1WatchLaunch` |  |
| POST | `/lol-gameflow/v2/spectate/launch` | `PostLolGameflowV2SpectateLaunch` |  |

## LolHighlights（9）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-highlights/v1/config` | `GetLolHighlightsV1Config` |  |
| POST | `/lol-highlights/v1/file/browser/by/highlight/id` | `PostLolHighlightsV1FileBrowserByHighlightId` |  |
| GET | `/lol-highlights/v1/highlights` | `GetLolHighlightsV1Highlights` |  |
| POST | `/lol-highlights/v1/highlights` | `PostLolHighlightsV1Highlights` |  |
| DELETE | `/lol-highlights/v1/highlights/by/id` | `DeleteLolHighlightsV1HighlightsById` |  |
| GET | `/lol-highlights/v1/highlights/by/id` | `GetLolHighlightsV1HighlightsById` |  |
| PUT | `/lol-highlights/v1/highlights/by/id` | `PutLolHighlightsV1HighlightsById` |  |
| GET | `/lol-highlights/v1/highlights/folder/path` | `GetLolHighlightsV1HighlightsFolderPath` |  |
| GET | `/lol-highlights/v1/highlights/folder/path/default` | `GetLolHighlightsV1HighlightsFolderPathDefault` |  |

## LolHoneyfruit（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-honeyfruit/v1/vng/publisher/settings` | `GetLolHoneyfruitV1VngPublisherSettings` |  |
| POST | `/lol-honeyfruit/v1/vng/publisher/settings` | `PostLolHoneyfruitV1VngPublisherSettings` |  |

## LolHonor（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-honor/v1/ballot` | `PostLolHonorV1Ballot` |  |
| POST | `/lol-honor/v1/honor` | `PostLolHonorV1Honor` |  |

## LolHonorV2（21）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-honor-v2/v1/ack/honor/notification/by/mail/id` | `PostLolHonorV2V1AckHonorNotificationByMailId` |  |
| DELETE | `/lol-honor-v2/v1/ballot` | `DeleteLolHonorV2V1Ballot` |  |
| GET | `/lol-honor-v2/v1/ballot` | `GetLolHonorV2V1Ballot` |  |
| POST | `/lol-honor-v2/v1/ballot/refresh` | `PostLolHonorV2V1BallotRefresh` |  |
| GET | `/lol-honor-v2/v1/config` | `GetLolHonorV2V1Config` |  |
| POST | `/lol-honor-v2/v1/honor/player` | `PostLolHonorV2V1HonorPlayer` |  |
| GET | `/lol-honor-v2/v1/late/recognition` | `GetLolHonorV2V1LateRecognition` |  |
| POST | `/lol-honor-v2/v1/late/recognition/ack` | `PostLolHonorV2V1LateRecognitionAck` |  |
| GET | `/lol-honor-v2/v1/latest/eligible/game` | `GetLolHonorV2V1LatestEligibleGame` |  |
| GET | `/lol-honor-v2/v1/level/change` | `GetLolHonorV2V1LevelChange` |  |
| POST | `/lol-honor-v2/v1/level/change/ack` | `PostLolHonorV2V1LevelChangeAck` |  |
| GET | `/lol-honor-v2/v1/level/change/notifications` | `GetLolHonorV2V1LevelChangeNotifications` |  |
| GET | `/lol-honor-v2/v1/mutual/honor` | `GetLolHonorV2V1MutualHonor` |  |
| POST | `/lol-honor-v2/v1/mutual/honor/ack` | `PostLolHonorV2V1MutualHonorAck` |  |
| GET | `/lol-honor-v2/v1/profile` | `GetLolHonorV2V1Profile` |  |
| GET | `/lol-honor-v2/v1/recognition` | `GetLolHonorV2V1Recognition` |  |
| GET | `/lol-honor-v2/v1/recognition/history` | `GetLolHonorV2V1RecognitionHistory` |  |
| GET | `/lol-honor-v2/v1/reward/granted` | `GetLolHonorV2V1RewardGranted` |  |
| POST | `/lol-honor-v2/v1/reward/granted/ack` | `PostLolHonorV2V1RewardGrantedAck` |  |
| GET | `/lol-honor-v2/v1/team/choices` | `GetLolHonorV2V1TeamChoices` |  |
| GET | `/lol-honor-v2/v1/vote/completion` | `GetLolHonorV2V1VoteCompletion` |  |

## LolHovercard（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-hovercard/v1/friend/info/by/puuid` | `GetLolHovercardV1FriendInfoByPuuid` |  |

## LolInventory（23）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-inventory/v1/aram/inventory` | `GetLolInventoryV1AramInventory` |  |
| GET | `/lol-inventory/v1/champ/select/inventory` | `GetLolInventoryV1ChampSelectInventory` |  |
| GET | `/lol-inventory/v1/cherry/inventory` | `GetLolInventoryV1CherryInventory` |  |
| GET | `/lol-inventory/v1/initial/configuration/complete` | `GetLolInventoryV1InitialConfigurationComplete` |  |
| GET | `/lol-inventory/v1/inventory` | `GetLolInventoryV1Inventory` |  |
| GET | `/lol-inventory/v1/inventory/emotes` | `GetLolInventoryV1InventoryEmotes` |  |
| GET | `/lol-inventory/v1/inventory/with/f2/p` | `GetLolInventoryV1InventoryWithF2P` |  |
| POST | `/lol-inventory/v1/notification/acknowledge` | `PostLolInventoryV1NotificationAcknowledge` |  |
| GET | `/lol-inventory/v1/notifications/by/inventory/type` | `GetLolInventoryV1NotificationsByInventoryType` |  |
| GET | `/lol-inventory/v1/players/by/puuid/inventory` | `GetLolInventoryV1PlayersByPuuidInventory` |  |
| GET | `/lol-inventory/v1/signed/inventory` | `GetLolInventoryV1SignedInventory` |  |
| GET | `/lol-inventory/v1/signed/inventory/cache` | `GetLolInventoryV1SignedInventoryCache` |  |
| GET | `/lol-inventory/v1/signed/inventory/simple` | `GetLolInventoryV1SignedInventorySimple` |  |
| GET | `/lol-inventory/v1/signed/inventory/tournamentlogos` | `GetLolInventoryV1SignedInventoryTournamentlogos` |  |
| GET | `/lol-inventory/v1/signed/wallet` | `GetLolInventoryV1SignedWallet` |  |
| GET | `/lol-inventory/v1/signed/wallet/by/currency/type` | `GetLolInventoryV1SignedWalletByCurrencyType` |  |
| GET | `/lol-inventory/v1/strawberry/inventory` | `GetLolInventoryV1StrawberryInventory` |  |
| GET | `/lol-inventory/v1/wallet` | `GetLolInventoryV1Wallet` |  |
| GET | `/lol-inventory/v1/wallet/by/currency/type` | `GetLolInventoryV1WalletByCurrencyType` |  |
| GET | `/lol-inventory/v1/wallet/by/currency/type/notifications` | `GetLolInventoryV1WalletByCurrencyTypeNotifications` |  |
| POST | `/lol-inventory/v1/wallet/by/currency/type/notifications` | `PostLolInventoryV1WalletByCurrencyTypeNotifications` |  |
| GET | `/lol-inventory/v1/xbox/subscription/status` | `GetLolInventoryV1XboxSubscriptionStatus` |  |
| GET | `/lol-inventory/v2/inventory/by/inventory/type` | `GetLolInventoryV2InventoryByInventoryType` |  |

## LolItemSets（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-item-sets/v1/item/sets/by/summoner/id/sets` | `GetLolItemSetsV1ItemSetsBySummonerIdSets` |  |
| POST | `/lol-item-sets/v1/item/sets/by/summoner/id/sets` | `PostLolItemSetsV1ItemSetsBySummonerIdSets` |  |
| PUT | `/lol-item-sets/v1/item/sets/by/summoner/id/sets` | `PutLolItemSetsV1ItemSetsBySummonerIdSets` |  |
| POST | `/lol-item-sets/v1/item/sets/validate` | `PostLolItemSetsV1ItemSetsValidate` |  |

## LolKickout（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-kickout/v1/notification` | `GetLolKickoutV1Notification` |  |

## LolKrPlaytimeReminder（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-kr-playtime-reminder/v1/hours/played` | `GetLolKrPlaytimeReminderV1HoursPlayed` |  |

## LolKrShutdownLaw（8）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-kr-shutdown-law/v1/custom/status` | `GetLolKrShutdownLawV1CustomStatus` |  |
| GET | `/lol-kr-shutdown-law/v1/disabled/queues` | `GetLolKrShutdownLawV1DisabledQueues` |  |
| GET | `/lol-kr-shutdown-law/v1/is/enabled` | `GetLolKrShutdownLawV1IsEnabled` |  |
| GET | `/lol-kr-shutdown-law/v1/notification` | `GetLolKrShutdownLawV1Notification` |  |
| GET | `/lol-kr-shutdown-law/v1/queue/status/by/queue/id` | `GetLolKrShutdownLawV1QueueStatusByQueueId` |  |
| GET | `/lol-kr-shutdown-law/v1/rating/screen` | `GetLolKrShutdownLawV1RatingScreen` |  |
| POST | `/lol-kr-shutdown-law/v1/rating/screen/acknowledge` | `PostLolKrShutdownLawV1RatingScreenAcknowledge` |  |
| GET | `/lol-kr-shutdown-law/v1/status` | `GetLolKrShutdownLawV1Status` |  |

## LolLeaderboard（9）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-leaderboard/v1/name/by/name/current/season` | `GetLolLeaderboardV1NameByNameCurrentSeason` |  |
| GET | `/lol-leaderboard/v1/name/by/name/groupings` | `GetLolLeaderboardV1NameByNameGroupings` |  |
| GET | `/lol-leaderboard/v1/name/by/name/page/size` | `GetLolLeaderboardV1NameByNamePageSize` |  |
| GET | `/lol-leaderboard/v1/name/by/name/season/by/season/grouping/by/grouping` | `GetLolLeaderboardV1NameByNameSeasonBySeasonGroupingByGrouping` |  |
| GET | `/lol-leaderboard/v1/name/by/name/season/by/season/grouping/by/grouping/entry/ranking/by/entity/id` | `GetLolLeaderboardV1NameByNameSeasonBySeasonGroupingByGroupingEntryRankingByEntityId` |  |
| GET | `/lol-leaderboard/v1/name/by/name/season/by/season/grouping/by/grouping/jump/to/entry/by/entity/id` | `GetLolLeaderboardV1NameByNameSeasonBySeasonGroupingByGroupingJumpToEntryByEntityId` |  |
| GET | `/lol-leaderboard/v1/name/by/name/season/by/season/grouping/by/grouping/multipage` | `GetLolLeaderboardV1NameByNameSeasonBySeasonGroupingByGroupingMultipage` |  |
| GET | `/lol-leaderboard/v1/name/by/name/timer` | `GetLolLeaderboardV1NameByNameTimer` |  |
| GET | `/lol-leaderboard/v1/ready` | `GetLolLeaderboardV1Ready` |  |

## LolLeagueSession（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-league-session/v1/league/session/token` | `GetLolLeagueSessionV1LeagueSessionToken` |  |

## LolLeaverBuster（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-leaver-buster/v1/notifications` | `GetLolLeaverBusterV1Notifications` |  |
| DELETE | `/lol-leaver-buster/v1/notifications/by/id` | `DeleteLolLeaverBusterV1NotificationsById` |  |
| GET | `/lol-leaver-buster/v1/notifications/by/id` | `GetLolLeaverBusterV1NotificationsById` |  |
| GET | `/lol-leaver-buster/v1/ranked/restriction` | `GetLolLeaverBusterV1RankedRestriction` |  |

## LolLicenseAgreement（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-license-agreement/v1/agreement` | `GetLolLicenseAgreementV1Agreement` |  |
| GET | `/lol-license-agreement/v1/privacy/policy` | `GetLolLicenseAgreementV1PrivacyPolicy` |  |

## LolLoadouts（7）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-loadouts/v1/loadouts/ready` | `GetLolLoadoutsV1LoadoutsReady` |  |
| DELETE | `/lol-loadouts/v4/loadouts/by/id` | `DeleteLolLoadoutsV4LoadoutsById` |  |
| PATCH | `/lol-loadouts/v4/loadouts/by/id` | `PatchLolLoadoutsV4LoadoutsById` |  |
| PUT | `/lol-loadouts/v4/loadouts/by/id` | `PutLolLoadoutsV4LoadoutsById` |  |
| GET | `/lol-loadouts/v4/loadouts/by/loadout/id` | `GetLolLoadoutsV4LoadoutsByLoadoutId` |  |
| GET | `/lol-loadouts/v4/loadouts/scope/account` | `GetLolLoadoutsV4LoadoutsScopeAccount` |  |
| GET | `/lol-loadouts/v4/loadouts/scope/by/scope/by/scope/item/id` | `GetLolLoadoutsV4LoadoutsScopeByScopeByScopeItemId` |  |

## LolLobby（84）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-lobby/v1/autofill/displayed` | `GetLolLobbyV1AutofillDisplayed` |  |
| PUT | `/lol-lobby/v1/autofill/displayed` | `PutLolLobbyV1AutofillDisplayed` |  |
| DELETE | `/lol-lobby/v1/clash` | `DeleteLolLobbyV1Clash` |  |
| POST | `/lol-lobby/v1/clash` | `PostLolLobbyV1Clash` |  |
| GET | `/lol-lobby/v1/custom/games` | `GetLolLobbyV1CustomGames` |  |
| GET | `/lol-lobby/v1/custom/games/by/id` | `GetLolLobbyV1CustomGamesById` |  |
| POST | `/lol-lobby/v1/custom/games/refresh` | `PostLolLobbyV1CustomGamesRefresh` |  |
| GET | `/lol-lobby/v1/lobby/availability` | `GetLolLobbyV1LobbyAvailability` |  |
| GET | `/lol-lobby/v1/lobby/countdown` | `GetLolLobbyV1LobbyCountdown` |  |
| POST | `/lol-lobby/v1/lobby/custom/bots` | `PostLolLobbyV1LobbyCustomBots` |  |
| POST | `/lol-lobby/v1/lobby/custom/bots/by/summoner/internal/name/by/bot/uuid/to/delete` | `PostLolLobbyV1LobbyCustomBotsBySummonerInternalNameByBotUuidToDelete` |  |
| DELETE | `/lol-lobby/v1/lobby/custom/bots/by/summoner/internal/name/by/bot/uuid/to/delete/by/team/id` | `DeleteLolLobbyV1LobbyCustomBotsBySummonerInternalNameByBotUuidToDeleteByTeamId` |  |
| POST | `/lol-lobby/v1/lobby/custom/cancel/champ/select` | `PostLolLobbyV1LobbyCustomCancelChampSelect` |  |
| POST | `/lol-lobby/v1/lobby/custom/start/champ/select` | `PostLolLobbyV1LobbyCustomStartChampSelect` |  |
| GET | `/lol-lobby/v1/lobby/invitations` | `GetLolLobbyV1LobbyInvitations` |  |
| POST | `/lol-lobby/v1/lobby/invitations` | `PostLolLobbyV1LobbyInvitations` |  |
| GET | `/lol-lobby/v1/lobby/invitations/by/id` | `GetLolLobbyV1LobbyInvitationsById` |  |
| GET | `/lol-lobby/v1/lobby/members/local/member/player/slots` | `GetLolLobbyV1LobbyMembersLocalMemberPlayerSlots` |  |
| PUT | `/lol-lobby/v1/lobby/members/local/member/player/slots` | `PutLolLobbyV1LobbyMembersLocalMemberPlayerSlots` |  |
| POST | `/lol-lobby/v1/lobby/members/local/member/player/slots/by/slots/index/by/perks/string` | `PostLolLobbyV1LobbyMembersLocalMemberPlayerSlotsBySlotsIndexByPerksString` |  |
| PUT | `/lol-lobby/v1/lobby/members/local/member/position/preferences` | `PutLolLobbyV1LobbyMembersLocalMemberPositionPreferences` |  |
| PUT | `/lol-lobby/v1/parties/active` | `PutLolLobbyV1PartiesActive` |  |
| PUT | `/lol-lobby/v1/parties/by/party/id/members/by/puuid/role` | `PutLolLobbyV1PartiesByPartyIdMembersByPuuidRole` |  |
| GET | `/lol-lobby/v1/parties/gamemode` | `GetLolLobbyV1PartiesGamemode` |  |
| PUT | `/lol-lobby/v1/parties/metadata` | `PutLolLobbyV1PartiesMetadata` |  |
| GET | `/lol-lobby/v1/parties/player` | `GetLolLobbyV1PartiesPlayer` |  |
| PUT | `/lol-lobby/v1/parties/queue` | `PutLolLobbyV1PartiesQueue` |  |
| PUT | `/lol-lobby/v1/parties/ready` | `PutLolLobbyV1PartiesReady` |  |
| GET | `/lol-lobby/v1/party/rewards` | `GetLolLobbyV1PartyRewards` |  |
| POST | `/lol-lobby/v1/tournaments/by/id/join` | `PostLolLobbyV1TournamentsByIdJoin` |  |
| GET | `/lol-lobby/v2/ags/ags/activity/id` | `GetLolLobbyV2AgsAgsActivityId` |  |
| DELETE | `/lol-lobby/v2/ags/by/activity/id/join/code` | `DeleteLolLobbyV2AgsByActivityIdJoinCode` |  |
| GET | `/lol-lobby/v2/ags/by/activity/id/join/code` | `GetLolLobbyV2AgsByActivityIdJoinCode` |  |
| POST | `/lol-lobby/v2/ags/by/activity/id/join/code` | `PostLolLobbyV2AgsByActivityIdJoinCode` |  |
| GET | `/lol-lobby/v2/comms/members` | `GetLolLobbyV2CommsMembers` |  |
| GET | `/lol-lobby/v2/comms/token` | `GetLolLobbyV2CommsToken` |  |
| GET | `/lol-lobby/v2/eligibility/game/select/eligibility/hash` | `GetLolLobbyV2EligibilityGameSelectEligibilityHash` |  |
| GET | `/lol-lobby/v2/eligibility/initial/configuration/complete` | `GetLolLobbyV2EligibilityInitialConfigurationComplete` |  |
| POST | `/lol-lobby/v2/eligibility/party` | `PostLolLobbyV2EligibilityParty` |  |
| POST | `/lol-lobby/v2/eligibility/self` | `PostLolLobbyV2EligibilitySelf` |  |
| POST | `/lol-lobby/v2/eog/invitations` | `PostLolLobbyV2EogInvitations` |  |
| DELETE | `/lol-lobby/v2/lobby` | `DeleteLolLobbyV2Lobby` |  |
| GET | `/lol-lobby/v2/lobby` | `GetLolLobbyV2Lobby` |  |
| POST | `/lol-lobby/v2/lobby` | `PostLolLobbyV2Lobby` |  |
| GET | `/lol-lobby/v2/lobby/custom/available/bots` | `GetLolLobbyV2LobbyCustomAvailableBots` |  |
| GET | `/lol-lobby/v2/lobby/custom/bots/enabled` | `GetLolLobbyV2LobbyCustomBotsEnabled` |  |
| GET | `/lol-lobby/v2/lobby/invitations` | `GetLolLobbyV2LobbyInvitations` |  |
| POST | `/lol-lobby/v2/lobby/invitations` | `PostLolLobbyV2LobbyInvitations` |  |
| POST | `/lol-lobby/v2/lobby/invitations/with/context` | `PostLolLobbyV2LobbyInvitationsWithContext` |  |
| PUT | `/lol-lobby/v2/lobby/lp/opt/out` | `PutLolLobbyV2LobbyLpOptOut` |  |
| DELETE | `/lol-lobby/v2/lobby/matchmaking/search` | `DeleteLolLobbyV2LobbyMatchmakingSearch` |  |
| POST | `/lol-lobby/v2/lobby/matchmaking/search` | `PostLolLobbyV2LobbyMatchmakingSearch` |  |
| GET | `/lol-lobby/v2/lobby/matchmaking/search/state` | `GetLolLobbyV2LobbyMatchmakingSearchState` |  |
| PATCH | `/lol-lobby/v2/lobby/member/data` | `PatchLolLobbyV2LobbyMemberData` |  |
| PUT | `/lol-lobby/v2/lobby/member/data` | `PutLolLobbyV2LobbyMemberData` |  |
| GET | `/lol-lobby/v2/lobby/members` | `GetLolLobbyV2LobbyMembers` |  |
| POST | `/lol-lobby/v2/lobby/members/by/summoner/id/grant/invite` | `PostLolLobbyV2LobbyMembersBySummonerIdGrantInvite` |  |
| POST | `/lol-lobby/v2/lobby/members/by/summoner/id/kick` | `PostLolLobbyV2LobbyMembersBySummonerIdKick` |  |
| POST | `/lol-lobby/v2/lobby/members/by/summoner/id/promote` | `PostLolLobbyV2LobbyMembersBySummonerIdPromote` |  |
| POST | `/lol-lobby/v2/lobby/members/by/summoner/id/revoke/invite` | `PostLolLobbyV2LobbyMembersBySummonerIdRevokeInvite` |  |
| PUT | `/lol-lobby/v2/lobby/members/local/member/position/preferences` | `PutLolLobbyV2LobbyMembersLocalMemberPositionPreferences` |  |
| PUT | `/lol-lobby/v2/lobby/party/type` | `PutLolLobbyV2LobbyPartyType` |  |
| PUT | `/lol-lobby/v2/lobby/quickplay/member/data` | `PutLolLobbyV2LobbyQuickplayMemberData` |  |
| PUT | `/lol-lobby/v2/lobby/strawberry/map/id` | `PutLolLobbyV2LobbyStrawberryMapId` |  |
| PUT | `/lol-lobby/v2/lobby/subteam/data` | `PutLolLobbyV2LobbySubteamData` |  |
| POST | `/lol-lobby/v2/lobby/team/by/team` | `PostLolLobbyV2LobbyTeamByTeam` |  |
| POST | `/lol-lobby/v2/matchmaking/quick/search` | `PostLolLobbyV2MatchmakingQuickSearch` |  |
| GET | `/lol-lobby/v2/notifications` | `GetLolLobbyV2Notifications` |  |
| POST | `/lol-lobby/v2/notifications` | `PostLolLobbyV2Notifications` |  |
| DELETE | `/lol-lobby/v2/notifications/by/notification/id` | `DeleteLolLobbyV2NotificationsByNotificationId` |  |
| POST | `/lol-lobby/v2/parties/overrides/enabled/for/team/builder/queues` | `PostLolLobbyV2PartiesOverridesEnabledForTeamBuilderQueues` |  |
| GET | `/lol-lobby/v2/party/active` | `GetLolLobbyV2PartyActive` |  |
| POST | `/lol-lobby/v2/party/by/party/id/join` | `PostLolLobbyV2PartyByPartyIdJoin` |  |
| GET | `/lol-lobby/v2/party/eog/status` | `GetLolLobbyV2PartyEogStatus` |  |
| POST | `/lol-lobby/v2/play/again` | `PostLolLobbyV2PlayAgain` |  |
| POST | `/lol-lobby/v2/play/again/decline` | `PostLolLobbyV2PlayAgainDecline` |  |
| GET | `/lol-lobby/v2/received/invitations` | `GetLolLobbyV2ReceivedInvitations` |  |
| POST | `/lol-lobby/v2/received/invitations/by/invitation/id/accept` | `PostLolLobbyV2ReceivedInvitationsByInvitationIdAccept` |  |
| POST | `/lol-lobby/v2/received/invitations/by/invitation/id/decline` | `PostLolLobbyV2ReceivedInvitationsByInvitationIdDecline` |  |
| GET | `/lol-lobby/v2/registration/status` | `GetLolLobbyV2RegistrationStatus` |  |
| POST | `/lol-lobby/v3/lobby/members/by/puuid/grant/invite` | `PostLolLobbyV3LobbyMembersByPuuidGrantInvite` |  |
| POST | `/lol-lobby/v3/lobby/members/by/puuid/kick` | `PostLolLobbyV3LobbyMembersByPuuidKick` |  |
| POST | `/lol-lobby/v3/lobby/members/by/puuid/promote` | `PostLolLobbyV3LobbyMembersByPuuidPromote` |  |
| POST | `/lol-lobby/v3/lobby/members/by/puuid/revoke/invite` | `PostLolLobbyV3LobbyMembersByPuuidRevokeInvite` |  |

## LolLobbyTeamBuilder（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-lobby-team-builder/v1/matchmaking` | `GetLolLobbyTeamBuilderV1Matchmaking` |  |
| POST | `/lol-lobby-team-builder/v1/ready/check/accept` | `PostLolLobbyTeamBuilderV1ReadyCheckAccept` |  |
| POST | `/lol-lobby-team-builder/v1/ready/check/decline` | `PostLolLobbyTeamBuilderV1ReadyCheckDecline` |  |
| GET | `/lol-lobby-team-builder/v1/ready/check/is/auto/accept` | `GetLolLobbyTeamBuilderV1ReadyCheckIsAutoAccept` |  |

## LolLobbyTeamBuilderChampSelect（44）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-lobby-team-builder-champ-select/v1/aw/set` | `GetLolLobbyTeamBuilderChampSelectV1AwSet` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/bannable/champion/ids` | `GetLolLobbyTeamBuilderChampSelectV1BannableChampionIds` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/crowd/favorte/champion/list` | `GetLolLobbyTeamBuilderChampSelectV1CrowdFavorteChampionList` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/current/champion` | `GetLolLobbyTeamBuilderChampSelectV1CurrentChampion` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/disabled/champion/ids` | `GetLolLobbyTeamBuilderChampSelectV1DisabledChampionIds` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/has/auto/assigned/smite` | `GetLolLobbyTeamBuilderChampSelectV1HasAutoAssignedSmite` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/implementation/active` | `GetLolLobbyTeamBuilderChampSelectV1ImplementationActive` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/pickable/champion/ids` | `GetLolLobbyTeamBuilderChampSelectV1PickableChampionIds` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/pickable/skin/ids` | `GetLolLobbyTeamBuilderChampSelectV1PickableSkinIds` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/preferences` | `GetLolLobbyTeamBuilderChampSelectV1Preferences` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/retrieve/latest/game/dto` | `PostLolLobbyTeamBuilderChampSelectV1RetrieveLatestGameDto` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session` | `GetLolLobbyTeamBuilderChampSelectV1Session` |  |
| PATCH | `/lol-lobby-team-builder-champ-select/v1/session/actions/by/id` | `PatchLolLobbyTeamBuilderChampSelectV1SessionActionsById` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/actions/by/id/complete` | `PostLolLobbyTeamBuilderChampSelectV1SessionActionsByIdComplete` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/bench/swap/by/champion/id` | `PostLolLobbyTeamBuilderChampSelectV1SessionBenchSwapByChampionId` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/champion/swaps` | `GetLolLobbyTeamBuilderChampSelectV1SessionChampionSwaps` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/champion/swaps/by/id` | `GetLolLobbyTeamBuilderChampSelectV1SessionChampionSwapsById` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/champion/swaps/by/id/accept` | `PostLolLobbyTeamBuilderChampSelectV1SessionChampionSwapsByIdAccept` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/champion/swaps/by/id/cancel` | `PostLolLobbyTeamBuilderChampSelectV1SessionChampionSwapsByIdCancel` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/champion/swaps/by/id/decline` | `PostLolLobbyTeamBuilderChampSelectV1SessionChampionSwapsByIdDecline` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/champion/swaps/by/id/request` | `PostLolLobbyTeamBuilderChampSelectV1SessionChampionSwapsByIdRequest` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/my/selection` | `GetLolLobbyTeamBuilderChampSelectV1SessionMySelection` |  |
| PATCH | `/lol-lobby-team-builder-champ-select/v1/session/my/selection` | `PatchLolLobbyTeamBuilderChampSelectV1SessionMySelection` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/my/selection/reroll` | `PostLolLobbyTeamBuilderChampSelectV1SessionMySelectionReroll` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/obfuscated/puuids` | `GetLolLobbyTeamBuilderChampSelectV1SessionObfuscatedPuuids` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/obfuscated/summoner/ids` | `GetLolLobbyTeamBuilderChampSelectV1SessionObfuscatedSummonerIds` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/pick/order/swaps` | `GetLolLobbyTeamBuilderChampSelectV1SessionPickOrderSwaps` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/pick/order/swaps/by/id` | `GetLolLobbyTeamBuilderChampSelectV1SessionPickOrderSwapsById` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/pick/order/swaps/by/id/accept` | `PostLolLobbyTeamBuilderChampSelectV1SessionPickOrderSwapsByIdAccept` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/pick/order/swaps/by/id/cancel` | `PostLolLobbyTeamBuilderChampSelectV1SessionPickOrderSwapsByIdCancel` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/pick/order/swaps/by/id/decline` | `PostLolLobbyTeamBuilderChampSelectV1SessionPickOrderSwapsByIdDecline` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/pick/order/swaps/by/id/request` | `PostLolLobbyTeamBuilderChampSelectV1SessionPickOrderSwapsByIdRequest` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/position/swaps` | `GetLolLobbyTeamBuilderChampSelectV1SessionPositionSwaps` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/position/swaps/by/id` | `GetLolLobbyTeamBuilderChampSelectV1SessionPositionSwapsById` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/position/swaps/by/id/accept` | `PostLolLobbyTeamBuilderChampSelectV1SessionPositionSwapsByIdAccept` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/position/swaps/by/id/cancel` | `PostLolLobbyTeamBuilderChampSelectV1SessionPositionSwapsByIdCancel` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/position/swaps/by/id/decline` | `PostLolLobbyTeamBuilderChampSelectV1SessionPositionSwapsByIdDecline` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/position/swaps/by/id/request` | `PostLolLobbyTeamBuilderChampSelectV1SessionPositionSwapsByIdRequest` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/session/quit` | `PostLolLobbyTeamBuilderChampSelectV1SessionQuit` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/session/timer` | `GetLolLobbyTeamBuilderChampSelectV1SessionTimer` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/simple/inventory` | `PostLolLobbyTeamBuilderChampSelectV1SimpleInventory` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/subset/champion/list` | `GetLolLobbyTeamBuilderChampSelectV1SubsetChampionList` |  |
| GET | `/lol-lobby-team-builder-champ-select/v1/team/boost` | `GetLolLobbyTeamBuilderChampSelectV1TeamBoost` |  |
| POST | `/lol-lobby-team-builder-champ-select/v1/team/boost/purchase` | `PostLolLobbyTeamBuilderChampSelectV1TeamBoostPurchase` |  |

## LolLockAndLoad（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-lock-and-load/v1/home/hubs/waits` | `GetLolLockAndLoadV1HomeHubsWaits` |  |
| GET | `/lol-lock-and-load/v1/should/show/progress/bar/text` | `GetLolLockAndLoadV1ShouldShowProgressBarText` |  |
| GET | `/lol-lock-and-load/v1/should/wait/for/home/hubs` | `GetLolLockAndLoadV1ShouldWaitForHomeHubs` |  |

## LolLogin（17）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-login/v1/account/state` | `GetLolLoginV1AccountState` |  |
| POST | `/lol-login/v1/account/state` | `PostLolLoginV1AccountState` |  |
| POST | `/lol-login/v1/delete/rso/on/close` | `PostLolLoginV1DeleteRsoOnClose` |  |
| POST | `/lol-login/v1/league/session/status` | `PostLolLoginV1LeagueSessionStatus` |  |
| GET | `/lol-login/v1/login/connection/state` | `GetLolLoginV1LoginConnectionState` |  |
| GET | `/lol-login/v1/login/in/game/creds` | `GetLolLoginV1LoginInGameCreds` |  |
| GET | `/lol-login/v1/login/platform/credentials` | `GetLolLoginV1LoginPlatformCredentials` |  |
| GET | `/lol-login/v1/login/queue/state` | `GetLolLoginV1LoginQueueState` |  |
| DELETE | `/lol-login/v1/service/proxy/async/requests/by/service/name/by/method/name` | `DeleteLolLoginV1ServiceProxyAsyncRequestsByServiceNameByMethodName` |  |
| POST | `/lol-login/v1/service/proxy/async/requests/by/service/name/by/method/name` | `PostLolLoginV1ServiceProxyAsyncRequestsByServiceNameByMethodName` |  |
| DELETE | `/lol-login/v1/session` | `DeleteLolLoginV1Session` |  |
| GET | `/lol-login/v1/session` | `GetLolLoginV1Session` |  |
| DELETE | `/lol-login/v1/shutdown/locks/by/lock/name` | `DeleteLolLoginV1ShutdownLocksByLockName` |  |
| PUT | `/lol-login/v1/shutdown/locks/by/lock/name` | `PutLolLoginV1ShutdownLocksByLockName` |  |
| POST | `/lol-login/v1/summoner/session` | `PostLolLoginV1SummonerSession` |  |
| POST | `/lol-login/v1/summoner/session/failed` | `PostLolLoginV1SummonerSessionFailed` |  |
| GET | `/lol-login/v2/league/session/init/token` | `GetLolLoginV2LeagueSessionInitToken` |  |

## LolLoot（36）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-loot/v1/craft/mass` | `PostLolLootV1CraftMass` |  |
| GET | `/lol-loot/v1/currency/configuration` | `GetLolLootV1CurrencyConfiguration` |  |
| GET | `/lol-loot/v1/enabled` | `GetLolLootV1Enabled` |  |
| GET | `/lol-loot/v1/loot/grants` | `GetLolLootV1LootGrants` |  |
| DELETE | `/lol-loot/v1/loot/grants/by/id` | `DeleteLolLootV1LootGrantsById` |  |
| GET | `/lol-loot/v1/loot/items` | `GetLolLootV1LootItems` |  |
| GET | `/lol-loot/v1/loot/items/by/loot/name` | `GetLolLootV1LootItemsByLootName` |  |
| GET | `/lol-loot/v1/loot/odds/by/recipe/name` | `GetLolLootV1LootOddsByRecipeName` |  |
| GET | `/lol-loot/v1/loot/odds/by/recipe/name/visibility` | `GetLolLootV1LootOddsByRecipeNameVisibility` |  |
| PUT | `/lol-loot/v1/loot/odds/evaluate/query` | `PutLolLootV1LootOddsEvaluateQuery` |  |
| GET | `/lol-loot/v1/mass/disenchant/configuration` | `GetLolLootV1MassDisenchantConfiguration` |  |
| GET | `/lol-loot/v1/mass/disenchant/recipes` | `GetLolLootV1MassDisenchantRecipes` |  |
| GET | `/lol-loot/v1/milestones` | `GetLolLootV1Milestones` |  |
| GET | `/lol-loot/v1/milestones/by/loot/milestones/id` | `GetLolLootV1MilestonesByLootMilestonesId` |  |
| POST | `/lol-loot/v1/milestones/by/loot/milestones/id/claim` | `PostLolLootV1MilestonesByLootMilestonesIdClaim` |  |
| GET | `/lol-loot/v1/milestones/by/loot/milestones/id/claim/progress` | `GetLolLootV1MilestonesByLootMilestonesIdClaimProgress` |  |
| GET | `/lol-loot/v1/milestones/by/loot/milestones/id/counter` | `GetLolLootV1MilestonesByLootMilestonesIdCounter` |  |
| GET | `/lol-loot/v1/milestones/counters` | `GetLolLootV1MilestonesCounters` |  |
| GET | `/lol-loot/v1/milestones/items` | `GetLolLootV1MilestonesItems` |  |
| GET | `/lol-loot/v1/player/display/categories` | `GetLolLootV1PlayerDisplayCategories` |  |
| GET | `/lol-loot/v1/player/loot` | `GetLolLootV1PlayerLoot` |  |
| GET | `/lol-loot/v1/player/loot/by/loot/id` | `GetLolLootV1PlayerLootByLootId` |  |
| GET | `/lol-loot/v1/player/loot/by/loot/id/context/menu` | `GetLolLootV1PlayerLootByLootIdContextMenu` |  |
| POST | `/lol-loot/v1/player/loot/by/loot/id/context/menu` | `PostLolLootV1PlayerLootByLootIdContextMenu` |  |
| DELETE | `/lol-loot/v1/player/loot/by/loot/id/new/notification` | `DeleteLolLootV1PlayerLootByLootIdNewNotification` |  |
| POST | `/lol-loot/v1/player/loot/by/loot/name/redeem` | `PostLolLootV1PlayerLootByLootNameRedeem` |  |
| GET | `/lol-loot/v1/player/loot/map` | `GetLolLootV1PlayerLootMap` |  |
| GET | `/lol-loot/v1/player/loot/notifications` | `GetLolLootV1PlayerLootNotifications` |  |
| POST | `/lol-loot/v1/player/loot/notifications/by/id/acknowledge` | `PostLolLootV1PlayerLootNotificationsByIdAcknowledge` |  |
| GET | `/lol-loot/v1/ready` | `GetLolLootV1Ready` |  |
| POST | `/lol-loot/v1/recipes/by/recipe/name/craft` | `PostLolLootV1RecipesByRecipeNameCraft` |  |
| GET | `/lol-loot/v1/recipes/configuration` | `GetLolLootV1RecipesConfiguration` |  |
| GET | `/lol-loot/v1/recipes/initial/item/by/loot/id` | `GetLolLootV1RecipesInitialItemByLootId` |  |
| POST | `/lol-loot/v1/recipes/initial/item/by/loot/id` | `PostLolLootV1RecipesInitialItemByLootId` |  |
| POST | `/lol-loot/v1/refresh` | `PostLolLootV1Refresh` |  |
| GET | `/lol-loot/v2/player/loot/map` | `GetLolLootV2PlayerLootMap` |  |

## LolLoyalty（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-loyalty/v1/status/notification` | `GetLolLoyaltyV1StatusNotification` |  |
| POST | `/lol-loyalty/v1/update/loyalty/inventory` | `PostLolLoyaltyV1UpdateLoyaltyInventory` |  |

## LolMacGraphicsUpgradeNeedsHardwareUpgrade（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-mac-graphics-upgrade-needs-hardware-upgrade` | `GetLolMacGraphicsUpgradeNeedsHardwareUpgrade` |  |

## LolMacGraphicsUpgradeNotificationType（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-mac-graphics-upgrade-notification-type` | `GetLolMacGraphicsUpgradeNotificationType` |  |

## LolMaps（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-maps/v1/map` | `PostLolMapsV1Map` |  |
| GET | `/lol-maps/v1/map/by/id` | `GetLolMapsV1MapById` |  |
| GET | `/lol-maps/v1/maps` | `GetLolMapsV1Maps` |  |
| GET | `/lol-maps/v2/map/by/id/by/game/mode` | `GetLolMapsV2MapByIdByGameMode` |  |
| GET | `/lol-maps/v2/map/by/id/by/game/mode/by/game/mutator` | `GetLolMapsV2MapByIdByGameModeByGameMutator` |  |
| GET | `/lol-maps/v2/maps` | `GetLolMapsV2Maps` |  |

## LolMarketingPreferences（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-marketing-preferences/v1/partition/by/partition/key` | `GetLolMarketingPreferencesV1PartitionByPartitionKey` |  |
| POST | `/lol-marketing-preferences/v1/partition/by/partition/key` | `PostLolMarketingPreferencesV1PartitionByPartitionKey` |  |
| GET | `/lol-marketing-preferences/v1/ready` | `GetLolMarketingPreferencesV1Ready` |  |

## LolMarketplace（8）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-marketplace/v1/products/by/product/purchases` | `GetLolMarketplaceV1ProductsByProductPurchases` |  |
| POST | `/lol-marketplace/v1/products/by/product/purchases` | `PostLolMarketplaceV1ProductsByProductPurchases` |  |
| POST | `/lol-marketplace/v1/products/by/product/refunds` | `PostLolMarketplaceV1ProductsByProductRefunds` |  |
| GET | `/lol-marketplace/v1/products/by/product/stores` | `GetLolMarketplaceV1ProductsByProductStores` |  |
| GET | `/lol-marketplace/v1/products/tft/configs` | `GetLolMarketplaceV1ProductsTftConfigs` |  |
| GET | `/lol-marketplace/v1/products/tft/stores/upgrades` | `GetLolMarketplaceV1ProductsTftStoresUpgrades` |  |
| GET | `/lol-marketplace/v1/purchases/by/purchase/id` | `GetLolMarketplaceV1PurchasesByPurchaseId` |  |
| GET | `/lol-marketplace/v1/ready` | `GetLolMarketplaceV1Ready` |  |

## LolMatchHistory（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-match-history/v1/game/timelines/by/game/id` | `GetLolMatchHistoryV1GameTimelinesByGameId` |  |
| GET | `/lol-match-history/v1/games/by/game/id` | `GetLolMatchHistoryV1GamesByGameId` |  |
| GET | `/lol-match-history/v1/products/lol/by/puuid/matches` | `GetLolMatchHistoryV1ProductsLolByPuuidMatches` |  |
| GET | `/lol-match-history/v1/products/lol/current/summoner/matches` | `GetLolMatchHistoryV1ProductsLolCurrentSummonerMatches` |  |
| GET | `/lol-match-history/v1/products/tft/by/puuid/matches` | `GetLolMatchHistoryV1ProductsTftByPuuidMatches` |  |
| GET | `/lol-match-history/v1/recently/played/summoners` | `GetLolMatchHistoryV1RecentlyPlayedSummoners` |  |

## LolMatchmaking（9）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-matchmaking/v1/ready/check` | `GetLolMatchmakingV1ReadyCheck` |  |
| POST | `/lol-matchmaking/v1/ready/check/accept` | `PostLolMatchmakingV1ReadyCheckAccept` |  |
| POST | `/lol-matchmaking/v1/ready/check/decline` | `PostLolMatchmakingV1ReadyCheckDecline` |  |
| DELETE | `/lol-matchmaking/v1/search` | `DeleteLolMatchmakingV1Search` |  |
| GET | `/lol-matchmaking/v1/search` | `GetLolMatchmakingV1Search` |  |
| POST | `/lol-matchmaking/v1/search` | `PostLolMatchmakingV1Search` |  |
| PUT | `/lol-matchmaking/v1/search` | `PutLolMatchmakingV1Search` |  |
| GET | `/lol-matchmaking/v1/search/errors` | `GetLolMatchmakingV1SearchErrors` |  |
| GET | `/lol-matchmaking/v1/search/errors/by/id` | `GetLolMatchmakingV1SearchErrorsById` |  |

## LolMissions（11）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-missions/v1/data` | `GetLolMissionsV1Data` |  |
| POST | `/lol-missions/v1/force` | `PostLolMissionsV1Force` |  |
| GET | `/lol-missions/v1/missions` | `GetLolMissionsV1Missions` |  |
| GET | `/lol-missions/v1/missions/multiseries` | `GetLolMissionsV1MissionsMultiseries` |  |
| GET | `/lol-missions/v1/missions/series/by/series/name` | `GetLolMissionsV1MissionsSeriesBySeriesName` |  |
| GET | `/lol-missions/v1/missions/series/id` | `GetLolMissionsV1MissionsSeriesId` |  |
| GET | `/lol-missions/v1/missions/series/name` | `GetLolMissionsV1MissionsSeriesName` |  |
| PUT | `/lol-missions/v1/player` | `PutLolMissionsV1Player` |  |
| PUT | `/lol-missions/v1/player/by/mission/id` | `PutLolMissionsV1PlayerByMissionId` |  |
| GET | `/lol-missions/v1/series` | `GetLolMissionsV1Series` |  |
| PUT | `/lol-missions/v2/player/opt` | `PutLolMissionsV2PlayerOpt` |  |

## LolNpeRewards（7）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-npe-rewards/v1/challenges/opt` | `PostLolNpeRewardsV1ChallengesOpt` |  |
| GET | `/lol-npe-rewards/v1/challenges/progress` | `GetLolNpeRewardsV1ChallengesProgress` |  |
| GET | `/lol-npe-rewards/v1/get/poro/experiments` | `GetLolNpeRewardsV1GetPoroExperiments` |  |
| GET | `/lol-npe-rewards/v1/level/rewards` | `GetLolNpeRewardsV1LevelRewards` |  |
| GET | `/lol-npe-rewards/v1/level/rewards/state` | `GetLolNpeRewardsV1LevelRewardsState` |  |
| GET | `/lol-npe-rewards/v1/login/rewards` | `GetLolNpeRewardsV1LoginRewards` |  |
| GET | `/lol-npe-rewards/v1/login/rewards/state` | `GetLolNpeRewardsV1LoginRewardsState` |  |

## LolNpeTutorialPath（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-npe-tutorial-path/v1/rewards/champ` | `GetLolNpeTutorialPathV1RewardsChamp` |  |
| GET | `/lol-npe-tutorial-path/v1/settings` | `GetLolNpeTutorialPathV1Settings` |  |
| PUT | `/lol-npe-tutorial-path/v1/settings` | `PutLolNpeTutorialPathV1Settings` |  |
| GET | `/lol-npe-tutorial-path/v1/tutorials` | `GetLolNpeTutorialPathV1Tutorials` |  |
| PUT | `/lol-npe-tutorial-path/v1/tutorials/by/tutorial/id/view` | `PutLolNpeTutorialPathV1TutorialsByTutorialIdView` |  |
| PATCH | `/lol-npe-tutorial-path/v1/tutorials/init` | `PatchLolNpeTutorialPathV1TutorialsInit` |  |

## LolObjectives（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-objectives/v1/missions/by/tag` | `GetLolObjectivesV1MissionsByTag` |  |
| GET | `/lol-objectives/v1/objectives/by/game/type` | `GetLolObjectivesV1ObjectivesByGameType` |  |
| GET | `/lol-objectives/v1/ready` | `GetLolObjectivesV1Ready` |  |

## LolParentalControls（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-parental-controls/v1/status` | `GetLolParentalControlsV1Status` |  |

## LolPatch（19）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-patch/v1/checking/enabled` | `GetLolPatchV1CheckingEnabled` |  |
| GET | `/lol-patch/v1/environment` | `GetLolPatchV1Environment` |  |
| PUT | `/lol-patch/v1/game/patch/url` | `PutLolPatchV1GamePatchUrl` |  |
| GET | `/lol-patch/v1/game/version` | `GetLolPatchV1GameVersion` |  |
| GET | `/lol-patch/v1/notifications` | `GetLolPatchV1Notifications` |  |
| DELETE | `/lol-patch/v1/notifications/by/id` | `DeleteLolPatchV1NotificationsById` |  |
| GET | `/lol-patch/v1/product/integration/app/update/available` | `GetLolPatchV1ProductIntegrationAppUpdateAvailable` |  |
| POST | `/lol-patch/v1/products/league/of/legends/detect/corruption/request` | `PostLolPatchV1ProductsLeagueOfLegendsDetectCorruptionRequest` |  |
| GET | `/lol-patch/v1/products/league/of/legends/install/location` | `GetLolPatchV1ProductsLeagueOfLegendsInstallLocation` |  |
| POST | `/lol-patch/v1/products/league/of/legends/partial/repair/request` | `PostLolPatchV1ProductsLeagueOfLegendsPartialRepairRequest` |  |
| POST | `/lol-patch/v1/products/league/of/legends/start/checking/request` | `PostLolPatchV1ProductsLeagueOfLegendsStartCheckingRequest` |  |
| POST | `/lol-patch/v1/products/league/of/legends/start/patching/request` | `PostLolPatchV1ProductsLeagueOfLegendsStartPatchingRequest` |  |
| GET | `/lol-patch/v1/products/league/of/legends/state` | `GetLolPatchV1ProductsLeagueOfLegendsState` |  |
| POST | `/lol-patch/v1/products/league/of/legends/stop/checking/request` | `PostLolPatchV1ProductsLeagueOfLegendsStopCheckingRequest` |  |
| POST | `/lol-patch/v1/products/league/of/legends/stop/patching/request` | `PostLolPatchV1ProductsLeagueOfLegendsStopPatchingRequest` |  |
| GET | `/lol-patch/v1/products/league/of/legends/supported/game/releases` | `GetLolPatchV1ProductsLeagueOfLegendsSupportedGameReleases` |  |
| GET | `/lol-patch/v1/status` | `GetLolPatchV1Status` |  |
| GET | `/lol-patch/v1/teamfighttactics/game/version` | `GetLolPatchV1TeamfighttacticsGameVersion` |  |
| PUT | `/lol-patch/v1/ux` | `PutLolPatchV1Ux` |  |

## LolPerks（29）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-perks/v1/currentpage` | `GetLolPerksV1Currentpage` |  |
| PUT | `/lol-perks/v1/currentpage` | `PutLolPerksV1Currentpage` |  |
| GET | `/lol-perks/v1/inventory` | `GetLolPerksV1Inventory` |  |
| DELETE | `/lol-perks/v1/pages` | `DeleteLolPerksV1Pages` |  |
| GET | `/lol-perks/v1/pages` | `GetLolPerksV1Pages` |  |
| POST | `/lol-perks/v1/pages` | `PostLolPerksV1Pages` |  |
| DELETE | `/lol-perks/v1/pages/by/id` | `DeleteLolPerksV1PagesById` |  |
| GET | `/lol-perks/v1/pages/by/id` | `GetLolPerksV1PagesById` |  |
| PUT | `/lol-perks/v1/pages/by/id` | `PutLolPerksV1PagesById` |  |
| DELETE | `/lol-perks/v1/pages/by/id/auto/modified/selections` | `DeleteLolPerksV1PagesByIdAutoModifiedSelections` |  |
| PUT | `/lol-perks/v1/pages/validate` | `PutLolPerksV1PagesValidate` |  |
| GET | `/lol-perks/v1/perks` | `GetLolPerksV1Perks` |  |
| PUT | `/lol-perks/v1/perks/ack/gameplay/updated` | `PutLolPerksV1PerksAckGameplayUpdated` |  |
| GET | `/lol-perks/v1/perks/disabled` | `GetLolPerksV1PerksDisabled` |  |
| GET | `/lol-perks/v1/perks/gameplay/updated` | `GetLolPerksV1PerksGameplayUpdated` |  |
| GET | `/lol-perks/v1/quick/play/selections/champion/by/champion/id/position/by/position` | `GetLolPerksV1QuickPlaySelectionsChampionByChampionIdPositionByPosition` |  |
| GET | `/lol-perks/v1/recommended/champion/positions` | `GetLolPerksV1RecommendedChampionPositions` |  |
| GET | `/lol-perks/v1/recommended/pages/champion/by/champion/id/position/by/position/map/by/map/id` | `GetLolPerksV1RecommendedPagesChampionByChampionIdPositionByPositionMapByMapId` |  |
| GET | `/lol-perks/v1/recommended/pages/position/champion/by/champion/id` | `GetLolPerksV1RecommendedPagesPositionChampionByChampionId` |  |
| POST | `/lol-perks/v1/recommended/pages/position/champion/by/champion/id/position/by/position` | `PostLolPerksV1RecommendedPagesPositionChampionByChampionIdPositionByPosition` |  |
| GET | `/lol-perks/v1/rune/recommender/auto/select` | `GetLolPerksV1RuneRecommenderAutoSelect` |  |
| POST | `/lol-perks/v1/rune/recommender/auto/select` | `PostLolPerksV1RuneRecommenderAutoSelect` |  |
| GET | `/lol-perks/v1/settings` | `GetLolPerksV1Settings` |  |
| PUT | `/lol-perks/v1/settings` | `PutLolPerksV1Settings` |  |
| GET | `/lol-perks/v1/show/auto/modified/pages/notification` | `GetLolPerksV1ShowAutoModifiedPagesNotification` |  |
| POST | `/lol-perks/v1/show/auto/modified/pages/notification` | `PostLolPerksV1ShowAutoModifiedPagesNotification` |  |
| GET | `/lol-perks/v1/styles` | `GetLolPerksV1Styles` |  |
| POST | `/lol-perks/v1/update/page/order` | `PostLolPerksV1UpdatePageOrder` |  |
| PUT | `/lol-perks/v1/validate/page/name/by/page/name/by/context` | `PutLolPerksV1ValidatePageNameByPageNameByContext` |  |

## LolPlatformConfig（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-platform-config/v1/initial/configuration/complete` | `GetLolPlatformConfigV1InitialConfigurationComplete` |  |
| GET | `/lol-platform-config/v1/namespaces` | `GetLolPlatformConfigV1Namespaces` |  |
| GET | `/lol-platform-config/v1/namespaces/by/ns` | `GetLolPlatformConfigV1NamespacesByNs` |  |
| GET | `/lol-platform-config/v1/namespaces/by/ns/by/key` | `GetLolPlatformConfigV1NamespacesByNsByKey` |  |

## LolPlayerBehavior（17）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| PUT | `/lol-player-behavior/v1/ack/credibility/behavior/warning/by/mail/id` | `PutLolPlayerBehaviorV1AckCredibilityBehaviorWarningByMailId` |  |
| GET | `/lol-player-behavior/v1/active/ban` | `GetLolPlayerBehaviorV1ActiveBan` |  |
| GET | `/lol-player-behavior/v1/ban` | `GetLolPlayerBehaviorV1Ban` |  |
| GET | `/lol-player-behavior/v1/chat/restriction` | `GetLolPlayerBehaviorV1ChatRestriction` |  |
| DELETE | `/lol-player-behavior/v1/code/of/conduct/notification` | `DeleteLolPlayerBehaviorV1CodeOfConductNotification` |  |
| GET | `/lol-player-behavior/v1/code/of/conduct/notification` | `GetLolPlayerBehaviorV1CodeOfConductNotification` |  |
| GET | `/lol-player-behavior/v1/config` | `GetLolPlayerBehaviorV1Config` |  |
| GET | `/lol-player-behavior/v1/credibility/behavior/warnings` | `GetLolPlayerBehaviorV1CredibilityBehaviorWarnings` |  |
| GET | `/lol-player-behavior/v1/reform/card` | `GetLolPlayerBehaviorV1ReformCard` |  |
| GET | `/lol-player-behavior/v1/reporter/feedback` | `GetLolPlayerBehaviorV1ReporterFeedback` |  |
| DELETE | `/lol-player-behavior/v1/reporter/feedback/by/id` | `DeleteLolPlayerBehaviorV1ReporterFeedbackById` |  |
| GET | `/lol-player-behavior/v1/reporter/feedback/by/id` | `GetLolPlayerBehaviorV1ReporterFeedbackById` |  |
| GET | `/lol-player-behavior/v2/reform/card` | `GetLolPlayerBehaviorV2ReformCard` |  |
| GET | `/lol-player-behavior/v2/reporter/feedback` | `GetLolPlayerBehaviorV2ReporterFeedback` |  |
| POST | `/lol-player-behavior/v2/reporter/feedback/by/key` | `PostLolPlayerBehaviorV2ReporterFeedbackByKey` |  |
| PUT | `/lol-player-behavior/v3/reform/card/by/id` | `PutLolPlayerBehaviorV3ReformCardById` |  |
| GET | `/lol-player-behavior/v3/reform/cards` | `GetLolPlayerBehaviorV3ReformCards` |  |

## LolPlayerMessaging（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-player-messaging/v1/celebration/notification` | `GetLolPlayerMessagingV1CelebrationNotification` |  |
| DELETE | `/lol-player-messaging/v1/celebration/notification/by/id/acknowledge` | `DeleteLolPlayerMessagingV1CelebrationNotificationByIdAcknowledge` |  |
| GET | `/lol-player-messaging/v1/notification` | `GetLolPlayerMessagingV1Notification` |  |
| DELETE | `/lol-player-messaging/v1/notification/by/id/acknowledge` | `DeleteLolPlayerMessagingV1NotificationByIdAcknowledge` |  |

## LolPlayerPreferences（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-player-preferences/v1/player/preferences/endpoint/override` | `PostLolPlayerPreferencesV1PlayerPreferencesEndpointOverride` |  |
| GET | `/lol-player-preferences/v1/player/preferences/ready` | `GetLolPlayerPreferencesV1PlayerPreferencesReady` |  |
| PUT | `/lol-player-preferences/v1/preference` | `PutLolPlayerPreferencesV1Preference` |  |
| GET | `/lol-player-preferences/v1/preference/by/type` | `GetLolPlayerPreferencesV1PreferenceByType` |  |

## LolPlayerReportSender（10）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-player-report-sender/v1/champ/select/reports` | `PostLolPlayerReportSenderV1ChampSelectReports` |  |
| GET | `/lol-player-report-sender/v1/direct/message/reports` | `GetLolPlayerReportSenderV1DirectMessageReports` |  |
| POST | `/lol-player-report-sender/v1/direct/message/reports` | `PostLolPlayerReportSenderV1DirectMessageReports` |  |
| POST | `/lol-player-report-sender/v1/end/of/game/reports` | `PostLolPlayerReportSenderV1EndOfGameReports` |  |
| GET | `/lol-player-report-sender/v1/game/ids/with/verbal/abuse/report` | `GetLolPlayerReportSenderV1GameIdsWithVerbalAbuseReport` |  |
| GET | `/lol-player-report-sender/v1/in/game/reports` | `GetLolPlayerReportSenderV1InGameReports` |  |
| POST | `/lol-player-report-sender/v1/in/game/reports` | `PostLolPlayerReportSenderV1InGameReports` |  |
| POST | `/lol-player-report-sender/v1/match/history/reports` | `PostLolPlayerReportSenderV1MatchHistoryReports` |  |
| DELETE | `/lol-player-report-sender/v1/reported/players/game/id/by/game/id` | `DeleteLolPlayerReportSenderV1ReportedPlayersGameIdByGameId` |  |
| GET | `/lol-player-report-sender/v1/reported/players/game/id/by/game/id` | `GetLolPlayerReportSenderV1ReportedPlayersGameIdByGameId` |  |

## LolPreEndOfGame（5）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-pre-end-of-game/v1/complete/by/sequence/event/name` | `PostLolPreEndOfGameV1CompleteBySequenceEventName` |  |
| GET | `/lol-pre-end-of-game/v1/current/sequence/event` | `GetLolPreEndOfGameV1CurrentSequenceEvent` |  |
| DELETE | `/lol-pre-end-of-game/v1/registration/by/sequence/event/name` | `DeleteLolPreEndOfGameV1RegistrationBySequenceEventName` |  |
| POST | `/lol-pre-end-of-game/v1/registration/by/sequence/event/name/by/priority` | `PostLolPreEndOfGameV1RegistrationBySequenceEventNameByPriority` |  |
| POST | `/lol-pre-end-of-game/v1/skip/pre/end/of/game` | `PostLolPreEndOfGameV1SkipPreEndOfGame` |  |

## LolPremadeVoice（35）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-premade-voice/v1/availability` | `GetLolPremadeVoiceV1Availability` |  |
| GET | `/lol-premade-voice/v1/capturedevices` | `GetLolPremadeVoiceV1Capturedevices` |  |
| PUT | `/lol-premade-voice/v1/capturedevices` | `PutLolPremadeVoiceV1Capturedevices` |  |
| GET | `/lol-premade-voice/v1/devices/capture/permission` | `GetLolPremadeVoiceV1DevicesCapturePermission` |  |
| GET | `/lol-premade-voice/v1/first/experience` | `GetLolPremadeVoiceV1FirstExperience` |  |
| POST | `/lol-premade-voice/v1/first/experience/game` | `PostLolPremadeVoiceV1FirstExperienceGame` |  |
| POST | `/lol-premade-voice/v1/first/experience/lcu` | `PostLolPremadeVoiceV1FirstExperienceLcu` |  |
| POST | `/lol-premade-voice/v1/first/experience/reset` | `PostLolPremadeVoiceV1FirstExperienceReset` |  |
| POST | `/lol-premade-voice/v1/game/client/updated/p/t/t/key` | `PostLolPremadeVoiceV1GameClientUpdatedPTTKey` |  |
| POST | `/lol-premade-voice/v1/game/client/updated/team/p/t/t/key` | `PostLolPremadeVoiceV1GameClientUpdatedTeamPTTKey` |  |
| DELETE | `/lol-premade-voice/v1/mic/test` | `DeleteLolPremadeVoiceV1MicTest` |  |
| GET | `/lol-premade-voice/v1/mic/test` | `GetLolPremadeVoiceV1MicTest` |  |
| POST | `/lol-premade-voice/v1/mic/test` | `PostLolPremadeVoiceV1MicTest` |  |
| GET | `/lol-premade-voice/v1/parental/controls/status` | `GetLolPremadeVoiceV1ParentalControlsStatus` |  |
| GET | `/lol-premade-voice/v1/participant/records` | `GetLolPremadeVoiceV1ParticipantRecords` |  |
| GET | `/lol-premade-voice/v1/participants` | `GetLolPremadeVoiceV1Participants` |  |
| PUT | `/lol-premade-voice/v1/participants/by/puuid/mute` | `PutLolPremadeVoiceV1ParticipantsByPuuidMute` |  |
| PUT | `/lol-premade-voice/v1/participants/by/puuid/volume` | `PutLolPremadeVoiceV1ParticipantsByPuuidVolume` |  |
| POST | `/lol-premade-voice/v1/push/to/talk/check/available` | `PostLolPremadeVoiceV1PushToTalkCheckAvailable` |  |
| PUT | `/lol-premade-voice/v1/self/activation/sensitivity` | `PutLolPremadeVoiceV1SelfActivationSensitivity` |  |
| PUT | `/lol-premade-voice/v1/self/auto/join` | `PutLolPremadeVoiceV1SelfAutoJoin` |  |
| PUT | `/lol-premade-voice/v1/self/auto/join/team/voice` | `PutLolPremadeVoiceV1SelfAutoJoinTeamVoice` |  |
| PUT | `/lol-premade-voice/v1/self/input/mode` | `PutLolPremadeVoiceV1SelfInputMode` |  |
| PUT | `/lol-premade-voice/v1/self/mic/level` | `PutLolPremadeVoiceV1SelfMicLevel` |  |
| PUT | `/lol-premade-voice/v1/self/mute` | `PutLolPremadeVoiceV1SelfMute` |  |
| PUT | `/lol-premade-voice/v1/self/mute/on/connect` | `PutLolPremadeVoiceV1SelfMuteOnConnect` |  |
| PUT | `/lol-premade-voice/v1/self/noise/suppression` | `PutLolPremadeVoiceV1SelfNoiseSuppression` |  |
| PUT | `/lol-premade-voice/v1/self/player/volume` | `PutLolPremadeVoiceV1SelfPlayerVolume` |  |
| PUT | `/lol-premade-voice/v1/self/voice/font` | `PutLolPremadeVoiceV1SelfVoiceFont` |  |
| DELETE | `/lol-premade-voice/v1/session` | `DeleteLolPremadeVoiceV1Session` |  |
| GET | `/lol-premade-voice/v1/session` | `GetLolPremadeVoiceV1Session` |  |
| POST | `/lol-premade-voice/v1/session` | `PostLolPremadeVoiceV1Session` |  |
| PUT | `/lol-premade-voice/v1/session` | `PutLolPremadeVoiceV1Session` |  |
| GET | `/lol-premade-voice/v1/settings` | `GetLolPremadeVoiceV1Settings` |  |
| POST | `/lol-premade-voice/v1/settings/reset` | `PostLolPremadeVoiceV1SettingsReset` |  |

## LolProgression（5）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-progression/v1/groups/by/group/id/configuration` | `GetLolProgressionV1GroupsByGroupIdConfiguration` |  |
| GET | `/lol-progression/v1/groups/by/group/id/configuration/v1/schema` | `GetLolProgressionV1GroupsByGroupIdConfigurationV1Schema` |  |
| GET | `/lol-progression/v1/groups/by/group/id/instance/data` | `GetLolProgressionV1GroupsByGroupIdInstanceData` |  |
| GET | `/lol-progression/v1/groups/configuration` | `GetLolProgressionV1GroupsConfiguration` |  |
| GET | `/lol-progression/v1/ready` | `GetLolProgressionV1Ready` |  |

## LolPublishingContent（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-publishing-content/v1/listeners/allow/list/by/region` | `GetLolPublishingContentV1ListenersAllowListByRegion` |  |
| GET | `/lol-publishing-content/v1/listeners/client/data` | `GetLolPublishingContentV1ListenersClientData` |  |
| GET | `/lol-publishing-content/v1/listeners/pubhub/config` | `GetLolPublishingContentV1ListenersPubhubConfig` |  |
| GET | `/lol-publishing-content/v1/ready` | `GetLolPublishingContentV1Ready` |  |
| GET | `/lol-publishing-content/v1/settings` | `GetLolPublishingContentV1Settings` |  |
| GET | `/lol-publishing-content/v1/tft/hub/cards` | `GetLolPublishingContentV1TftHubCards` |  |

## LolPurchaseWidget（11）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-purchase-widget/v1/configuration` | `GetLolPurchaseWidgetV1Configuration` |  |
| GET | `/lol-purchase-widget/v1/items/by/item/id/related/bundles` | `GetLolPurchaseWidgetV1ItemsByItemIdRelatedBundles` |  |
| GET | `/lol-purchase-widget/v1/order/notifications` | `GetLolPurchaseWidgetV1OrderNotifications` |  |
| GET | `/lol-purchase-widget/v1/purchasable/item` | `GetLolPurchaseWidgetV1PurchasableItem` |  |
| POST | `/lol-purchase-widget/v1/purchasable/items/by/inventory/type` | `PostLolPurchaseWidgetV1PurchasableItemsByInventoryType` |  |
| POST | `/lol-purchase-widget/v2/purchase/items` | `PostLolPurchaseWidgetV2PurchaseItems` |  |
| GET | `/lol-purchase-widget/v3/base/skin/line/data/by/offer/id` | `GetLolPurchaseWidgetV3BaseSkinLineDataByOfferId` |  |
| POST | `/lol-purchase-widget/v3/purchase/offer` | `PostLolPurchaseWidgetV3PurchaseOffer` |  |
| GET | `/lol-purchase-widget/v3/purchase/offer/order/statuses` | `GetLolPurchaseWidgetV3PurchaseOfferOrderStatuses` |  |
| POST | `/lol-purchase-widget/v3/purchase/offer/via/cap` | `PostLolPurchaseWidgetV3PurchaseOfferViaCap` |  |
| POST | `/lol-purchase-widget/v3/validate/offer` | `PostLolPurchaseWidgetV3ValidateOffer` |  |

## LolRanked（19）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-ranked/v1/apex/leagues/by/queue/type/by/tier` | `GetLolRankedV1ApexLeaguesByQueueTypeByTier` |  |
| GET | `/lol-ranked/v1/cached/ranked/stats/by/puuid` | `GetLolRankedV1CachedRankedStatsByPuuid` |  |
| GET | `/lol-ranked/v1/challenger/ladders/enabled` | `GetLolRankedV1ChallengerLaddersEnabled` |  |
| GET | `/lol-ranked/v1/current/lp/change/notification` | `GetLolRankedV1CurrentLpChangeNotification` |  |
| GET | `/lol-ranked/v1/current/ranked/stats` | `GetLolRankedV1CurrentRankedStats` |  |
| GET | `/lol-ranked/v1/eligible/tiers/queue/type/by/queue/type` | `GetLolRankedV1EligibleTiersQueueTypeByQueueType` |  |
| GET | `/lol-ranked/v1/eos/notifications` | `GetLolRankedV1EosNotifications` |  |
| POST | `/lol-ranked/v1/eos/notifications/by/id/acknowledge` | `PostLolRankedV1EosNotificationsByIdAcknowledge` |  |
| GET | `/lol-ranked/v1/eos/rewards/by/season/id` | `GetLolRankedV1EosRewardsBySeasonId` |  |
| GET | `/lol-ranked/v1/global/notifications` | `GetLolRankedV1GlobalNotifications` |  |
| GET | `/lol-ranked/v1/league/ladders/by/puuid` | `GetLolRankedV1LeagueLaddersByPuuid` |  |
| GET | `/lol-ranked/v1/notifications` | `GetLolRankedV1Notifications` |  |
| POST | `/lol-ranked/v1/notifications/by/id/acknowledge` | `PostLolRankedV1NotificationsByIdAcknowledge` |  |
| GET | `/lol-ranked/v1/ranked/stats/by/puuid` | `GetLolRankedV1RankedStatsByPuuid` |  |
| GET | `/lol-ranked/v1/rated/ladder/by/queue/type` | `GetLolRankedV1RatedLadderByQueueType` |  |
| GET | `/lol-ranked/v1/signed/ranked/stats` | `GetLolRankedV1SignedRankedStats` |  |
| GET | `/lol-ranked/v1/social/leaderboard/ranked/queue/stats/for/puuids` | `GetLolRankedV1SocialLeaderboardRankedQueueStatsForPuuids` |  |
| GET | `/lol-ranked/v1/top/rated/ladders/enabled` | `GetLolRankedV1TopRatedLaddersEnabled` |  |
| GET | `/lol-ranked/v2/tiers` | `GetLolRankedV2Tiers` |  |

## LolRegalia（9）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-regalia/v2/config` | `GetLolRegaliaV2Config` |  |
| GET | `/lol-regalia/v2/current/summoner/regalia` | `GetLolRegaliaV2CurrentSummonerRegalia` |  |
| PUT | `/lol-regalia/v2/current/summoner/regalia` | `PutLolRegaliaV2CurrentSummonerRegalia` |  |
| GET | `/lol-regalia/v2/summoners/by/summoner/id/queues/by/queue/positions/by/position/regalia` | `GetLolRegaliaV2SummonersBySummonerIdQueuesByQueuePositionsByPositionRegalia` |  |
| GET | `/lol-regalia/v2/summoners/by/summoner/id/queues/by/queue/regalia` | `GetLolRegaliaV2SummonersBySummonerIdQueuesByQueueRegalia` |  |
| GET | `/lol-regalia/v2/summoners/by/summoner/id/regalia` | `GetLolRegaliaV2SummonersBySummonerIdRegalia` |  |
| GET | `/lol-regalia/v2/summoners/by/summoner/id/regalia/async` | `GetLolRegaliaV2SummonersBySummonerIdRegaliaAsync` |  |
| GET | `/lol-regalia/v3/inventory/by/inventory/type` | `GetLolRegaliaV3InventoryByInventoryType` |  |
| GET | `/lol-regalia/v3/summoners/by/summoner/id/regalia` | `GetLolRegaliaV3SummonersBySummonerIdRegalia` |  |

## LolRemedy（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| PUT | `/lol-remedy/v1/ack/remedy/notification/by/mail/id` | `PutLolRemedyV1AckRemedyNotificationByMailId` |  |
| GET | `/lol-remedy/v1/config/is/verbal/abuse/remedy/modal/enabled` | `GetLolRemedyV1ConfigIsVerbalAbuseRemedyModalEnabled` |  |
| GET | `/lol-remedy/v1/remedy/notifications` | `GetLolRemedyV1RemedyNotifications` |  |

## LolReplays（10）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-replays/v1/configuration` | `GetLolReplaysV1Configuration` |  |
| GET | `/lol-replays/v1/metadata/by/game/id` | `GetLolReplaysV1MetadataByGameId` |  |
| POST | `/lol-replays/v1/metadata/by/game/id/create/game/version/by/game/version/game/type/by/game/type/queue/id/by/queue/id` | `PostLolReplaysV1MetadataByGameIdCreateGameVersionByGameVersionGameTypeByGameTypeQueueIdByQueueId` |  |
| POST | `/lol-replays/v1/rofls/by/game/id/download` | `PostLolReplaysV1RoflsByGameIdDownload` |  |
| POST | `/lol-replays/v1/rofls/by/game/id/download/graceful` | `PostLolReplaysV1RoflsByGameIdDownloadGraceful` |  |
| POST | `/lol-replays/v1/rofls/by/game/id/watch` | `PostLolReplaysV1RoflsByGameIdWatch` |  |
| GET | `/lol-replays/v1/rofls/path` | `GetLolReplaysV1RoflsPath` |  |
| GET | `/lol-replays/v1/rofls/path/default` | `GetLolReplaysV1RoflsPathDefault` |  |
| POST | `/lol-replays/v1/rofls/scan` | `PostLolReplaysV1RoflsScan` |  |
| POST | `/lol-replays/v2/metadata/by/game/id/create` | `PostLolReplaysV2MetadataByGameIdCreate` |  |

## LolRewardTrack（8）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-reward-track/by/progression/group/id/reward/track/bonus/items` | `GetLolRewardTrackByProgressionGroupIdRewardTrackBonusItems` |  |
| GET | `/lol-reward-track/by/progression/group/id/reward/track/bonus/progress` | `GetLolRewardTrackByProgressionGroupIdRewardTrackBonusProgress` |  |
| GET | `/lol-reward-track/by/progression/group/id/reward/track/failure` | `GetLolRewardTrackByProgressionGroupIdRewardTrackFailure` |  |
| GET | `/lol-reward-track/by/progression/group/id/reward/track/items` | `GetLolRewardTrackByProgressionGroupIdRewardTrackItems` |  |
| GET | `/lol-reward-track/by/progression/group/id/reward/track/progress` | `GetLolRewardTrackByProgressionGroupIdRewardTrackProgress` |  |
| GET | `/lol-reward-track/by/progression/group/id/reward/track/unclaimed/rewards` | `GetLolRewardTrackByProgressionGroupIdRewardTrackUnclaimedRewards` |  |
| GET | `/lol-reward-track/by/progression/group/id/reward/track/xp` | `GetLolRewardTrackByProgressionGroupIdRewardTrackXp` |  |
| GET | `/lol-reward-track/register/by/progression/group/id` | `GetLolRewardTrackRegisterByProgressionGroupId` |  |

## LolRewards（7）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-rewards/v1/celebrations/fsc` | `PostLolRewardsV1CelebrationsFsc` |  |
| GET | `/lol-rewards/v1/grants` | `GetLolRewardsV1Grants` |  |
| POST | `/lol-rewards/v1/grants/by/grant/id/select` | `PostLolRewardsV1GrantsByGrantIdSelect` |  |
| PATCH | `/lol-rewards/v1/grants/view` | `PatchLolRewardsV1GrantsView` |  |
| GET | `/lol-rewards/v1/groups` | `GetLolRewardsV1Groups` |  |
| POST | `/lol-rewards/v1/reward/replay` | `PostLolRewardsV1RewardReplay` |  |
| POST | `/lol-rewards/v1/select/bulk` | `PostLolRewardsV1SelectBulk` |  |

## LolRms（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-rms/v1/champion/mastery/leaveup/update` | `GetLolRmsV1ChampionMasteryLeaveupUpdate` |  |
| DELETE | `/lol-rms/v1/champion/mastery/leaveup/update/by/id` | `DeleteLolRmsV1ChampionMasteryLeaveupUpdateById` |  |

## LolRsoAuth（17）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-rso-auth/configuration/v3/ready/state` | `GetLolRsoAuthConfigurationV3ReadyState` |  |
| DELETE | `/lol-rso-auth/v1/authorization` | `DeleteLolRsoAuthV1Authorization` |  |
| GET | `/lol-rso-auth/v1/authorization` | `GetLolRsoAuthV1Authorization` |  |
| GET | `/lol-rso-auth/v1/authorization/access/token` | `GetLolRsoAuthV1AuthorizationAccessToken` |  |
| GET | `/lol-rso-auth/v1/authorization/country` | `GetLolRsoAuthV1AuthorizationCountry` |  |
| GET | `/lol-rso-auth/v1/authorization/error` | `GetLolRsoAuthV1AuthorizationError` |  |
| POST | `/lol-rso-auth/v1/authorization/gas` | `PostLolRsoAuthV1AuthorizationGas` |  |
| GET | `/lol-rso-auth/v1/authorization/id/token` | `GetLolRsoAuthV1AuthorizationIdToken` |  |
| POST | `/lol-rso-auth/v1/authorization/refresh` | `PostLolRsoAuthV1AuthorizationRefresh` |  |
| GET | `/lol-rso-auth/v1/authorization/userinfo` | `GetLolRsoAuthV1AuthorizationUserinfo` |  |
| POST | `/lol-rso-auth/v1/authorization/userinfo` | `PostLolRsoAuthV1AuthorizationUserinfo` |  |
| POST | `/lol-rso-auth/v1/device/id` | `PostLolRsoAuthV1DeviceId` |  |
| POST | `/lol-rso-auth/v1/external/session/config` | `PostLolRsoAuthV1ExternalSessionConfig` |  |
| DELETE | `/lol-rso-auth/v1/session` | `DeleteLolRsoAuthV1Session` |  |
| GET | `/lol-rso-auth/v1/status/by/platform/id` | `GetLolRsoAuthV1StatusByPlatformId` |  |
| DELETE | `/lol-rso-auth/v2/config` | `DeleteLolRsoAuthV2Config` |  |
| POST | `/lol-rso-auth/v2/config` | `PostLolRsoAuthV2Config` |  |

## LolSanctum（11）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-sanctum/v1/banners` | `GetLolSanctumV1Banners` |  |
| GET | `/lol-sanctum/v1/banners/by/banner/id/odds` | `GetLolSanctumV1BannersByBannerIdOdds` |  |
| GET | `/lol-sanctum/v1/banners/by/banner/id/token/catalog/entry` | `GetLolSanctumV1BannersByBannerIdTokenCatalogEntry` |  |
| GET | `/lol-sanctum/v1/banners/selected/id` | `GetLolSanctumV1BannersSelectedId` |  |
| POST | `/lol-sanctum/v1/banners/selected/id` | `PostLolSanctumV1BannersSelectedId` |  |
| GET | `/lol-sanctum/v1/counters/by/counter/id` | `GetLolSanctumV1CountersByCounterId` |  |
| POST | `/lol-sanctum/v1/purchase/roll` | `PostLolSanctumV1PurchaseRoll` |  |
| POST | `/lol-sanctum/v1/purchase/token` | `PostLolSanctumV1PurchaseToken` |  |
| GET | `/lol-sanctum/v1/purchases/by/purchase/id` | `GetLolSanctumV1PurchasesByPurchaseId` |  |
| GET | `/lol-sanctum/v1/ready` | `GetLolSanctumV1Ready` |  |
| GET | `/lol-sanctum/v1/velocity/limits` | `GetLolSanctumV1VelocityLimits` |  |

## LolSeasons（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-seasons/v1/all/seasons/product/by/product` | `PostLolSeasonsV1AllSeasonsProductByProduct` |  |
| GET | `/lol-seasons/v1/season/l/o/l/current/split/seasons` | `GetLolSeasonsV1SeasonLOLCurrentSplitSeasons` |  |
| GET | `/lol-seasons/v1/season/name/by/name` | `GetLolSeasonsV1SeasonNameByName` |  |
| GET | `/lol-seasons/v1/season/product/by/product` | `GetLolSeasonsV1SeasonProductByProduct` |  |
| GET | `/lol-seasons/v1/season/recent/final/split` | `GetLolSeasonsV1SeasonRecentFinalSplit` |  |
| GET | `/lol-seasons/v1/season/should/ac/refresh` | `GetLolSeasonsV1SeasonShouldAcRefresh` |  |

## LolServiceStatus（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-service-status/v1/lcu/status` | `GetLolServiceStatusV1LcuStatus` |  |
| GET | `/lol-service-status/v1/ticker/messages` | `GetLolServiceStatusV1TickerMessages` |  |

## LolSettings（18）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-settings/v1/account/by/category` | `GetLolSettingsV1AccountByCategory` |  |
| PATCH | `/lol-settings/v1/account/by/category` | `PatchLolSettingsV1AccountByCategory` |  |
| PUT | `/lol-settings/v1/account/by/category` | `PutLolSettingsV1AccountByCategory` |  |
| GET | `/lol-settings/v1/account/didreset` | `GetLolSettingsV1AccountDidreset` |  |
| POST | `/lol-settings/v1/account/save` | `PostLolSettingsV1AccountSave` |  |
| GET | `/lol-settings/v1/local/by/category` | `GetLolSettingsV1LocalByCategory` |  |
| PATCH | `/lol-settings/v1/local/by/category` | `PatchLolSettingsV1LocalByCategory` |  |
| GET | `/lol-settings/v2/account/by/pp/type/by/category` | `GetLolSettingsV2AccountByPpTypeByCategory` |  |
| PATCH | `/lol-settings/v2/account/by/pp/type/by/category` | `PatchLolSettingsV2AccountByPpTypeByCategory` |  |
| PUT | `/lol-settings/v2/account/by/pp/type/by/category` | `PutLolSettingsV2AccountByPpTypeByCategory` |  |
| GET | `/lol-settings/v2/config` | `GetLolSettingsV2Config` |  |
| GET | `/lol-settings/v2/didreset/by/pp/type` | `GetLolSettingsV2DidresetByPpType` |  |
| GET | `/lol-settings/v2/local/by/category` | `GetLolSettingsV2LocalByCategory` |  |
| PATCH | `/lol-settings/v2/local/by/category` | `PatchLolSettingsV2LocalByCategory` |  |
| GET | `/lol-settings/v2/ready` | `GetLolSettingsV2Ready` |  |
| POST | `/lol-settings/v2/reload/by/pp/type` | `PostLolSettingsV2ReloadByPpType` |  |
| POST | `/lol-settings/v2/reload/server/by/pp/type` | `PostLolSettingsV2ReloadServerByPpType` |  |
| POST | `/lol-settings/v2/upload/to/server/by/pp/type` | `PostLolSettingsV2UploadToServerByPpType` |  |

## LolShoppefront（10）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-shoppefront/v1/bulk/purchases` | `PostLolShoppefrontV1BulkPurchases` |  |
| GET | `/lol-shoppefront/v1/drop/tables/by/drop/table/id/odds` | `GetLolShoppefrontV1DropTablesByDropTableIdOdds` |  |
| POST | `/lol-shoppefront/v1/purchases` | `PostLolShoppefrontV1Purchases` |  |
| GET | `/lol-shoppefront/v1/purchases/by/purchase/id` | `GetLolShoppefrontV1PurchasesByPurchaseId` |  |
| GET | `/lol-shoppefront/v1/ready` | `GetLolShoppefrontV1Ready` |  |
| GET | `/lol-shoppefront/v1/store/digests` | `GetLolShoppefrontV1StoreDigests` |  |
| POST | `/lol-shoppefront/v1/store/digests` | `PostLolShoppefrontV1StoreDigests` |  |
| GET | `/lol-shoppefront/v1/stores` | `GetLolShoppefrontV1Stores` |  |
| GET | `/lol-shoppefront/v1/stores/by/shoppefront/id` | `GetLolShoppefrontV1StoresByShoppefrontId` |  |
| GET | `/lol-shoppefront/v1/velocity` | `GetLolShoppefrontV1Velocity` |  |

## LolShutdown（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-shutdown/v1/notification` | `GetLolShutdownV1Notification` |  |

## LolSimpleDialogMessages（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-simple-dialog-messages/v1/messages` | `GetLolSimpleDialogMessagesV1Messages` |  |
| POST | `/lol-simple-dialog-messages/v1/messages` | `PostLolSimpleDialogMessagesV1Messages` |  |
| DELETE | `/lol-simple-dialog-messages/v1/messages/by/message/id` | `DeleteLolSimpleDialogMessagesV1MessagesByMessageId` |  |

## LolSocialLeaderboard（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-social-leaderboard/v1/leaderboard/next/update/time` | `GetLolSocialLeaderboardV1LeaderboardNextUpdateTime` |  |
| GET | `/lol-social-leaderboard/v1/social/leaderboard/data` | `GetLolSocialLeaderboardV1SocialLeaderboardData` |  |

## LolSpectator（7）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-spectator/v1/buddy/spectate` | `PostLolSpectatorV1BuddySpectate` |  |
| GET | `/lol-spectator/v1/spectate` | `GetLolSpectatorV1Spectate` |  |
| GET | `/lol-spectator/v1/spectate/config` | `GetLolSpectatorV1SpectateConfig` |  |
| POST | `/lol-spectator/v1/spectate/launch` | `PostLolSpectatorV1SpectateLaunch` |  |
| POST | `/lol-spectator/v2/buddy/spectate` | `PostLolSpectatorV2BuddySpectate` |  |
| GET | `/lol-spectator/v3/buddy/can/spectate/by/puuid/by/spectator/key` | `GetLolSpectatorV3BuddyCanSpectateByPuuidBySpectatorKey` |  |
| POST | `/lol-spectator/v3/buddy/spectate` | `PostLolSpectatorV3BuddySpectate` |  |

## LolStatstones（11）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-statstones/v1/eog/notifications/by/game/id` | `GetLolStatstonesV1EogNotificationsByGameId` |  |
| GET | `/lol-statstones/v1/featured/champion/statstones/by/champion/item/id` | `GetLolStatstonesV1FeaturedChampionStatstonesByChampionItemId` |  |
| POST | `/lol-statstones/v1/featured/champion/statstones/by/champion/item/id/by/statstone/id` | `PostLolStatstonesV1FeaturedChampionStatstonesByChampionItemIdByStatstoneId` |  |
| GET | `/lol-statstones/v1/profile/summary/by/puuid` | `GetLolStatstonesV1ProfileSummaryByPuuid` |  |
| GET | `/lol-statstones/v1/statstone/by/content/id/owned` | `GetLolStatstonesV1StatstoneByContentIdOwned` |  |
| GET | `/lol-statstones/v1/statstones/enabled/queue/ids` | `GetLolStatstonesV1StatstonesEnabledQueueIds` |  |
| DELETE | `/lol-statstones/v1/vignette/notifications` | `DeleteLolStatstonesV1VignetteNotifications` |  |
| GET | `/lol-statstones/v1/vignette/notifications` | `GetLolStatstonesV1VignetteNotifications` |  |
| DELETE | `/lol-statstones/v1/vignette/notifications/by/key` | `DeleteLolStatstonesV1VignetteNotificationsByKey` |  |
| GET | `/lol-statstones/v2/player/statstones/self/by/champion/item/id` | `GetLolStatstonesV2PlayerStatstonesSelfByChampionItemId` |  |
| GET | `/lol-statstones/v2/player/summary/self` | `GetLolStatstonesV2PlayerSummarySelf` |  |

## LolStore（25）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-store/v1/alias/change/notifications` | `GetLolStoreV1AliasChangeNotifications` |  |
| GET | `/lol-store/v1/by/page/type` | `GetLolStoreV1ByPageType` |  |
| GET | `/lol-store/v1/catalog` | `GetLolStoreV1Catalog` |  |
| GET | `/lol-store/v1/catalog/by/instance/ids` | `GetLolStoreV1CatalogByInstanceIds` |  |
| GET | `/lol-store/v1/catalog/by/inventory/type` | `GetLolStoreV1CatalogByInventoryType` |  |
| GET | `/lol-store/v1/catalog/by/inventory/type/by/item/id/tags` | `GetLolStoreV1CatalogByInventoryTypeByItemIdTags` |  |
| GET | `/lol-store/v1/catalog/items/skip/cache` | `GetLolStoreV1CatalogItemsSkipCache` |  |
| GET | `/lol-store/v1/catalog/sales` | `GetLolStoreV1CatalogSales` |  |
| GET | `/lol-store/v1/get/store/url` | `GetLolStoreV1GetStoreUrl` |  |
| GET | `/lol-store/v1/giftablefriends` | `GetLolStoreV1Giftablefriends` |  |
| GET | `/lol-store/v1/item/keys/from/instance/ids` | `GetLolStoreV1ItemKeysFromInstanceIds` |  |
| GET | `/lol-store/v1/item/keys/from/offer/ids` | `GetLolStoreV1ItemKeysFromOfferIds` |  |
| GET | `/lol-store/v1/last/page` | `GetLolStoreV1LastPage` |  |
| POST | `/lol-store/v1/last/page` | `PostLolStoreV1LastPage` |  |
| POST | `/lol-store/v1/notifications/acknowledge` | `PostLolStoreV1NotificationsAcknowledge` |  |
| GET | `/lol-store/v1/offers` | `GetLolStoreV1Offers` |  |
| GET | `/lol-store/v1/offers/by/offer/id` | `GetLolStoreV1OffersByOfferId` |  |
| GET | `/lol-store/v1/order/notifications` | `GetLolStoreV1OrderNotifications` |  |
| GET | `/lol-store/v1/order/notifications/by/id` | `GetLolStoreV1OrderNotificationsById` |  |
| GET | `/lol-store/v1/payment/details` | `GetLolStoreV1PaymentDetails` |  |
| GET | `/lol-store/v1/skins/by/skin/id` | `GetLolStoreV1SkinsBySkinId` |  |
| GET | `/lol-store/v1/status` | `GetLolStoreV1Status` |  |
| GET | `/lol-store/v1/store/ready` | `GetLolStoreV1StoreReady` |  |
| GET | `/lol-store/v2/offers` | `GetLolStoreV2Offers` |  |
| POST | `/lol-store/v3/purchase` | `PostLolStoreV3Purchase` |  |

## LolSuggestedPlayers（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-suggested-players/v1/reported/player` | `PostLolSuggestedPlayersV1ReportedPlayer` |  |
| GET | `/lol-suggested-players/v1/suggested/players` | `GetLolSuggestedPlayersV1SuggestedPlayers` |  |
| DELETE | `/lol-suggested-players/v1/suggested/players/by/summoner/id` | `DeleteLolSuggestedPlayersV1SuggestedPlayersBySummonerId` |  |
| POST | `/lol-suggested-players/v1/victorious/comrade` | `PostLolSuggestedPlayersV1VictoriousComrade` |  |

## LolSummoner（48）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-summoner/profiles/v1/get/champion/mastery/view` | `GetLolSummonerProfilesV1GetChampionMasteryView` |  |
| GET | `/lol-summoner/profiles/v1/get/honor/view` | `GetLolSummonerProfilesV1GetHonorView` |  |
| GET | `/lol-summoner/profiles/v1/get/jade/voting/view` | `GetLolSummonerProfilesV1GetJadeVotingView` |  |
| GET | `/lol-summoner/profiles/v1/get/lol/eos/rewards/view` | `GetLolSummonerProfilesV1GetLolEosRewardsView` |  |
| GET | `/lol-summoner/profiles/v1/get/privacy/view` | `GetLolSummonerProfilesV1GetPrivacyView` |  |
| GET | `/lol-summoner/profiles/v1/get/restriction/view` | `GetLolSummonerProfilesV1GetRestrictionView` |  |
| GET | `/lol-summoner/profiles/v1/get/summoner/level/view` | `GetLolSummonerProfilesV1GetSummonerLevelView` |  |
| POST | `/lol-summoner/profiles/v1/pco/by/category` | `PostLolSummonerProfilesV1PcoByCategory` |  |
| POST | `/lol-summoner/profiles/v1/ranked/scouting/view/request` | `PostLolSummonerProfilesV1RankedScoutingViewRequest` |  |
| GET | `/lol-summoner/v1/alias/lookup` | `GetLolSummonerV1AliasLookup` |  |
| GET | `/lol-summoner/v1/check/name/availability/by/name` | `GetLolSummonerV1CheckNameAvailabilityByName` |  |
| GET | `/lol-summoner/v1/check/name/availability/new/summoners/by/name` | `GetLolSummonerV1CheckNameAvailabilityNewSummonersByName` |  |
| GET | `/lol-summoner/v1/current/summoner` | `GetLolSummonerV1CurrentSummoner` |  |
| GET | `/lol-summoner/v1/current/summoner/account/and/summoner/ids` | `GetLolSummonerV1CurrentSummonerAccountAndSummonerIds` |  |
| GET | `/lol-summoner/v1/current/summoner/autofill` | `GetLolSummonerV1CurrentSummonerAutofill` |  |
| PUT | `/lol-summoner/v1/current/summoner/icon` | `PutLolSummonerV1CurrentSummonerIcon` |  |
| GET | `/lol-summoner/v1/current/summoner/jwt` | `GetLolSummonerV1CurrentSummonerJwt` |  |
| POST | `/lol-summoner/v1/current/summoner/name` | `PostLolSummonerV1CurrentSummonerName` |  |
| GET | `/lol-summoner/v1/current/summoner/profile/privacy` | `GetLolSummonerV1CurrentSummonerProfilePrivacy` |  |
| PUT | `/lol-summoner/v1/current/summoner/profile/privacy` | `PutLolSummonerV1CurrentSummonerProfilePrivacy` |  |
| GET | `/lol-summoner/v1/current/summoner/reroll/points` | `GetLolSummonerV1CurrentSummonerRerollPoints` |  |
| GET | `/lol-summoner/v1/current/summoner/summoner/profile` | `GetLolSummonerV1CurrentSummonerSummonerProfile` |  |
| POST | `/lol-summoner/v1/current/summoner/summoner/profile` | `PostLolSummonerV1CurrentSummonerSummonerProfile` |  |
| GET | `/lol-summoner/v1/player/alias/state` | `GetLolSummonerV1PlayerAliasState` |  |
| GET | `/lol-summoner/v1/player/name/mode` | `GetLolSummonerV1PlayerNameMode` |  |
| GET | `/lol-summoner/v1/profile/privacy/enabled` | `GetLolSummonerV1ProfilePrivacyEnabled` |  |
| GET | `/lol-summoner/v1/riot/alias/free/eligibility` | `GetLolSummonerV1RiotAliasFreeEligibility` |  |
| GET | `/lol-summoner/v1/riot/alias/purchase/eligibility` | `GetLolSummonerV1RiotAliasPurchaseEligibility` |  |
| POST | `/lol-summoner/v1/save/alias` | `PostLolSummonerV1SaveAlias` |  |
| GET | `/lol-summoner/v1/status` | `GetLolSummonerV1Status` |  |
| POST | `/lol-summoner/v1/summoner/aliases/by/ids` | `PostLolSummonerV1SummonerAliasesByIds` |  |
| POST | `/lol-summoner/v1/summoner/aliases/by/puuids` | `PostLolSummonerV1SummonerAliasesByPuuids` |  |
| GET | `/lol-summoner/v1/summoner/profile` | `GetLolSummonerV1SummonerProfile` |  |
| GET | `/lol-summoner/v1/summoner/requests/ready` | `GetLolSummonerV1SummonerRequestsReady` |  |
| GET | `/lol-summoner/v1/summoners` | `GetLolSummonerV1Summoners` |  |
| POST | `/lol-summoner/v1/summoners` | `PostLolSummonerV1Summoners` |  |
| POST | `/lol-summoner/v1/summoners/aliases` | `PostLolSummonerV1SummonersAliases` |  |
| GET | `/lol-summoner/v1/summoners/by/id` | `GetLolSummonerV1SummonersById` |  |
| GET | `/lol-summoner/v1/summoners/by/puuid/cached/by/puuid` | `GetLolSummonerV1SummonersByPuuidCachedByPuuid` |  |
| POST | `/lol-summoner/v1/validate/alias` | `PostLolSummonerV1ValidateAlias` |  |
| GET | `/lol-summoner/v2/summoner/icons` | `GetLolSummonerV2SummonerIcons` |  |
| GET | `/lol-summoner/v2/summoner/icons/by/puuids` | `GetLolSummonerV2SummonerIconsByPuuids` |  |
| GET | `/lol-summoner/v2/summoner/names` | `GetLolSummonerV2SummonerNames` |  |
| GET | `/lol-summoner/v2/summoner/names/by/puuids` | `GetLolSummonerV2SummonerNamesByPuuids` |  |
| GET | `/lol-summoner/v2/summoners` | `GetLolSummonerV2Summoners` |  |
| POST | `/lol-summoner/v2/summoners/names` | `PostLolSummonerV2SummonersNames` |  |
| POST | `/lol-summoner/v2/summoners/puuid` | `PostLolSummonerV2SummonersPuuid` |  |
| GET | `/lol-summoner/v2/summoners/puuid/by/puuid` | `GetLolSummonerV2SummonersPuuidByPuuid` |  |

## LolTastes（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-tastes/v1/ready` | `GetLolTastesV1Ready` |  |
| GET | `/lol-tastes/v1/skins/model` | `GetLolTastesV1SkinsModel` |  |
| GET | `/lol-tastes/v1/tft/overview/model` | `GetLolTastesV1TftOverviewModel` |  |

## LolTeamVoice（12）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| DELETE | `/lol-team-voice/v1/internal/prepare/party/voice/session` | `DeleteLolTeamVoiceV1InternalPreparePartyVoiceSession` |  |
| POST | `/lol-team-voice/v1/internal/prepare/party/voice/session` | `PostLolTeamVoiceV1InternalPreparePartyVoiceSession` |  |
| PUT | `/lol-team-voice/v1/participants/by/participant/id/mute/by/is/muted` | `PutLolTeamVoiceV1ParticipantsByParticipantIdMuteByIsMuted` |  |
| PUT | `/lol-team-voice/v1/participants/by/participant/id/volume/by/volume` | `PutLolTeamVoiceV1ParticipantsByParticipantIdVolumeByVolume` |  |
| DELETE | `/lol-team-voice/v1/session` | `DeleteLolTeamVoiceV1Session` |  |
| GET | `/lol-team-voice/v1/session` | `GetLolTeamVoiceV1Session` |  |
| POST | `/lol-team-voice/v1/session` | `PostLolTeamVoiceV1Session` |  |
| PUT | `/lol-team-voice/v1/session` | `PutLolTeamVoiceV1Session` |  |
| GET | `/lol-team-voice/v1/voice/fonts` | `GetLolTeamVoiceV1VoiceFonts` |  |
| GET | `/lol-team-voice/v1/voice/fonts/hardware/supported` | `GetLolTeamVoiceV1VoiceFontsHardwareSupported` |  |
| GET | `/lol-team-voice/v1/voice/fonts/status` | `GetLolTeamVoiceV1VoiceFontsStatus` |  |
| GET | `/lol-team-voice/v2/availability` | `GetLolTeamVoiceV2Availability` |  |

## LolTencentCookieRelay（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/lol-tencent-cookie-relay/v1/browser/fetch/result` | `PostLolTencentCookieRelayV1BrowserFetchResult` |  |
| GET | `/lol-tencent-cookie-relay/v1/tft/cookies` | `GetLolTencentCookieRelayV1TftCookies` |  |

## LolTencentDiagnosticAssistant（4）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-tencent-diagnostic-assistant/v1/enabled` | `GetLolTencentDiagnosticAssistantV1Enabled` |  |
| POST | `/lol-tencent-diagnostic-assistant/v1/invoke` | `PostLolTencentDiagnosticAssistantV1Invoke` |  |
| POST | `/lol-tencent-diagnostic-assistant/v1/invoke/with/cookie` | `PostLolTencentDiagnosticAssistantV1InvokeWithCookie` |  |
| GET | `/lol-tencent-diagnostic-assistant/v1/need/notify` | `GetLolTencentDiagnosticAssistantV1NeedNotify` |  |

## LolTft（90）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-tft/event/pve/v1/buddies` | `GetLolTftEventPveV1Buddies` |  |
| GET | `/lol-tft/event/pve/v1/buddy` | `GetLolTftEventPveV1Buddy` |  |
| PUT | `/lol-tft/event/pve/v1/buddy/by/id/equip` | `PutLolTftEventPveV1BuddyByIdEquip` |  |
| GET | `/lol-tft/event/pve/v1/difficulty` | `GetLolTftEventPveV1Difficulty` |  |
| PUT | `/lol-tft/event/pve/v1/difficulty/by/id/equip` | `PutLolTftEventPveV1DifficultyByIdEquip` |  |
| GET | `/lol-tft/event/pve/v1/enabled` | `GetLolTftEventPveV1Enabled` |  |
| GET | `/lol-tft/event/pve/v1/eogmissionrewards` | `GetLolTftEventPveV1Eogmissionrewards` |  |
| POST | `/lol-tft/event/pve/v1/eogmissionrewards/clear` | `PostLolTftEventPveV1EogmissionrewardsClear` |  |
| GET | `/lol-tft/event/pve/v1/eventpvehub` | `GetLolTftEventPveV1Eventpvehub` |  |
| GET | `/lol-tft/event/pve/v1/journeytrack/bonuses` | `GetLolTftEventPveV1JourneytrackBonuses` |  |
| GET | `/lol-tft/event/pve/v1/ready` | `GetLolTftEventPveV1Ready` |  |
| PUT | `/lol-tft/event/pve/v1/seen/buddies` | `PutLolTftEventPveV1SeenBuddies` |  |
| PUT | `/lol-tft/event/pve/v1/seen/buddies/by/id` | `PutLolTftEventPveV1SeenBuddiesById` |  |
| PUT | `/lol-tft/event/pve/v1/seen/levels` | `PutLolTftEventPveV1SeenLevels` |  |
| PUT | `/lol-tft/event/pve/v1/seen/ultimate/victory` | `PutLolTftEventPveV1SeenUltimateVictory` |  |
| GET | `/lol-tft/pass/v1/active/passes` | `GetLolTftPassV1ActivePasses` |  |
| GET | `/lol-tft/pass/v1/battle/pass` | `GetLolTftPassV1BattlePass` |  |
| GET | `/lol-tft/pass/v1/config/fetched` | `GetLolTftPassV1ConfigFetched` |  |
| GET | `/lol-tft/pass/v1/daily/login/pass` | `GetLolTftPassV1DailyLoginPass` |  |
| GET | `/lol-tft/pass/v1/enabled` | `GetLolTftPassV1Enabled` |  |
| GET | `/lol-tft/pass/v1/event/pass` | `GetLolTftPassV1EventPass` |  |
| GET | `/lol-tft/pass/v1/objectives/banner/by/id` | `GetLolTftPassV1ObjectivesBannerById` |  |
| POST | `/lol-tft/pass/v1/pass/by/id` | `PostLolTftPassV1PassById` |  |
| PUT | `/lol-tft/pass/v1/pass/by/id/milestone/by/milestone/id/reward` | `PutLolTftPassV1PassByIdMilestoneByMilestoneIdReward` |  |
| PUT | `/lol-tft/pass/v1/pass/by/id/milestone/claim/all/rewards` | `PutLolTftPassV1PassByIdMilestoneClaimAllRewards` |  |
| POST | `/lol-tft/pass/v1/passes` | `PostLolTftPassV1Passes` |  |
| GET | `/lol-tft/pass/v1/pm/ultimate/victory/pass` | `GetLolTftPassV1PmUltimateVictoryPass` |  |
| GET | `/lol-tft/pass/v1/ready` | `GetLolTftPassV1Ready` |  |
| GET | `/lol-tft/pass/v1/reward/notification` | `GetLolTftPassV1RewardNotification` |  |
| GET | `/lol-tft/pass/v1/skill/tree/pass` | `GetLolTftPassV1SkillTreePass` |  |
| GET | `/lol-tft/skill/tree/v1/enabled` | `GetLolTftSkillTreeV1Enabled` |  |
| GET | `/lol-tft/skill/tree/v1/player/progression` | `GetLolTftSkillTreeV1PlayerProgression` |  |
| GET | `/lol-tft/skill/tree/v1/ready` | `GetLolTftSkillTreeV1Ready` |  |
| GET | `/lol-tft/skill/tree/v1/skill/by/skill/id` | `GetLolTftSkillTreeV1SkillBySkillId` |  |
| PUT | `/lol-tft/skill/tree/v1/skill/by/skill/id/equip` | `PutLolTftSkillTreeV1SkillBySkillIdEquip` |  |
| GET | `/lol-tft/skill/tree/v1/skill/tree` | `GetLolTftSkillTreeV1SkillTree` |  |
| GET | `/lol-tft/skill/tree/v1/skill/tree/rank/by/rank` | `GetLolTftSkillTreeV1SkillTreeRankByRank` |  |
| PUT | `/lol-tft/skill/tree/v1/skill/tree/rank/by/rank/claim/rewards` | `PutLolTftSkillTreeV1SkillTreeRankByRankClaimRewards` |  |
| GET | `/lol-tft/team/planner/v1/config` | `GetLolTftTeamPlannerV1Config` |  |
| GET | `/lol-tft/team/planner/v1/ftue/has/viewed` | `GetLolTftTeamPlannerV1FtueHasViewed` |  |
| PATCH | `/lol-tft/team/planner/v1/ftue/has/viewed` | `PatchLolTftTeamPlannerV1FtueHasViewed` |  |
| POST | `/lol-tft/team/planner/v1/is/name/valid/by/name` | `PostLolTftTeamPlannerV1IsNameValidByName` |  |
| GET | `/lol-tft/team/planner/v1/previous/context` | `GetLolTftTeamPlannerV1PreviousContext` |  |
| PUT | `/lol-tft/team/planner/v1/previous/context` | `PutLolTftTeamPlannerV1PreviousContext` |  |
| PATCH | `/lol-tft/team/planner/v1/set` | `PatchLolTftTeamPlannerV1Set` |  |
| PATCH | `/lol-tft/team/planner/v1/set/last/viewed` | `PatchLolTftTeamPlannerV1SetLastViewed` |  |
| DELETE | `/lol-tft/team/planner/v1/sets/by/set/reminders/by/team` | `DeleteLolTftTeamPlannerV1SetsBySetRemindersByTeam` |  |
| PATCH | `/lol-tft/team/planner/v1/sets/by/set/reminders/by/team` | `PatchLolTftTeamPlannerV1SetsBySetRemindersByTeam` |  |
| POST | `/lol-tft/team/planner/v1/sets/by/set/team/code/by/team` | `PostLolTftTeamPlannerV1SetsBySetTeamCodeByTeam` |  |
| DELETE | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team` | `DeleteLolTftTeamPlannerV1SetsBySetTeamsByTeam` |  |
| POST | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team` | `PostLolTftTeamPlannerV1SetsBySetTeamsByTeam` |  |
| PATCH | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/by/name` | `PatchLolTftTeamPlannerV1SetsBySetTeamsByTeamByName` |  |
| DELETE | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/champions` | `DeleteLolTftTeamPlannerV1SetsBySetTeamsByTeamChampions` |  |
| PATCH | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/champions` | `PatchLolTftTeamPlannerV1SetsBySetTeamsByTeamChampions` |  |
| POST | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/champions` | `PostLolTftTeamPlannerV1SetsBySetTeamsByTeamChampions` |  |
| DELETE | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/champions/by/champion/id` | `DeleteLolTftTeamPlannerV1SetsBySetTeamsByTeamChampionsByChampionId` |  |
| POST | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/champions/by/champion/id` | `PostLolTftTeamPlannerV1SetsBySetTeamsByTeamChampionsByChampionId` |  |
| DELETE | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/champions/by/champion/id/by/index` | `DeleteLolTftTeamPlannerV1SetsBySetTeamsByTeamChampionsByChampionIdByIndex` |  |
| POST | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/champions/by/champion/id/by/index` | `PostLolTftTeamPlannerV1SetsBySetTeamsByTeamChampionsByChampionIdByIndex` |  |
| POST | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/import` | `PostLolTftTeamPlannerV1SetsBySetTeamsByTeamImport` |  |
| POST | `/lol-tft/team/planner/v1/sets/by/set/teams/by/team/last/view` | `PostLolTftTeamPlannerV1SetsBySetTeamsByTeamLastView` |  |
| GET | `/lol-tft/team/planner/v1/sets/dirty` | `GetLolTftTeamPlannerV1SetsDirty` |  |
| POST | `/lol-tft/team/planner/v1/sets/dirty` | `PostLolTftTeamPlannerV1SetsDirty` |  |
| POST | `/lol-tft/team/planner/v1/sets/push/to/server` | `PostLolTftTeamPlannerV1SetsPushToServer` |  |
| PUT | `/lol-tft/team/planner/v1/sets/save/all` | `PutLolTftTeamPlannerV1SetsSaveAll` |  |
| GET | `/lol-tft/team/planner/v1/sort/option` | `GetLolTftTeamPlannerV1SortOption` |  |
| PATCH | `/lol-tft/team/planner/v1/sort/option` | `PatchLolTftTeamPlannerV1SortOption` |  |
| POST | `/lol-tft/team/planner/v1/team/code/clipboard/by/set` | `PostLolTftTeamPlannerV1TeamCodeClipboardBySet` |  |
| GET | `/lol-tft/team/planner/v2/reminders` | `GetLolTftTeamPlannerV2Reminders` |  |
| GET | `/lol-tft/troves/v1/banners` | `GetLolTftTrovesV1Banners` |  |
| GET | `/lol-tft/troves/v1/config` | `GetLolTftTrovesV1Config` |  |
| GET | `/lol-tft/troves/v1/loot/odds/by/drop/table/id` | `GetLolTftTrovesV1LootOddsByDropTableId` |  |
| GET | `/lol-tft/troves/v1/milestone/notifications` | `GetLolTftTrovesV1MilestoneNotifications` |  |
| GET | `/lol-tft/troves/v1/milestones` | `GetLolTftTrovesV1Milestones` |  |
| POST | `/lol-tft/troves/v1/purchase` | `PostLolTftTrovesV1Purchase` |  |
| POST | `/lol-tft/troves/v1/roll` | `PostLolTftTrovesV1Roll` |  |
| DELETE | `/lol-tft/troves/v1/roll/rewards` | `DeleteLolTftTrovesV1RollRewards` |  |
| GET | `/lol-tft/troves/v1/roll/rewards` | `GetLolTftTrovesV1RollRewards` |  |
| GET | `/lol-tft/troves/v1/status/notifications` | `GetLolTftTrovesV1StatusNotifications` |  |
| POST | `/lol-tft/troves/v2/roll` | `PostLolTftTrovesV2Roll` |  |
| GET | `/lol-tft/v1/tft/backgrounds` | `GetLolTftV1TftBackgrounds` |  |
| GET | `/lol-tft/v1/tft/battle/pass/hub` | `GetLolTftV1TftBattlePassHub` |  |
| GET | `/lol-tft/v1/tft/events` | `GetLolTftV1TftEvents` |  |
| PUT | `/lol-tft/v1/tft/experiment/bucket` | `PutLolTftV1TftExperimentBucket` |  |
| GET | `/lol-tft/v1/tft/home/content` | `GetLolTftV1TftHomeContent` |  |
| GET | `/lol-tft/v1/tft/home/hub` | `GetLolTftV1TftHomeHub` |  |
| POST | `/lol-tft/v1/tft/home/hub/redirect` | `PostLolTftV1TftHomeHubRedirect` |  |
| GET | `/lol-tft/v1/tft/news/hub` | `GetLolTftV1TftNewsHub` |  |
| GET | `/lol-tft/v1/tft/promo/buttons` | `GetLolTftV1TftPromoButtons` |  |
| GET | `/lol-tft/v1/tft/tencent/eventhub/configs` | `GetLolTftV1TftTencentEventhubConfigs` |  |

## LolTrophies（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-trophies/v1/current/summoner/trophies/profile` | `GetLolTrophiesV1CurrentSummonerTrophiesProfile` |  |
| GET | `/lol-trophies/v1/players/by/puuid/trophies/profile` | `GetLolTrophiesV1PlayersByPuuidTrophiesProfile` |  |

## LolYourshop（9）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/lol-yourshop/v1/has/permissions` | `GetLolYourshopV1HasPermissions` |  |
| GET | `/lol-yourshop/v1/modal` | `GetLolYourshopV1Modal` |  |
| GET | `/lol-yourshop/v1/offers` | `GetLolYourshopV1Offers` |  |
| POST | `/lol-yourshop/v1/offers/by/id/purchase` | `PostLolYourshopV1OffersByIdPurchase` |  |
| POST | `/lol-yourshop/v1/offers/by/id/reveal` | `PostLolYourshopV1OffersByIdReveal` |  |
| POST | `/lol-yourshop/v1/permissions` | `PostLolYourshopV1Permissions` |  |
| GET | `/lol-yourshop/v1/ready` | `GetLolYourshopV1Ready` |  |
| GET | `/lol-yourshop/v1/status` | `GetLolYourshopV1Status` |  |
| GET | `/lol-yourshop/v1/themed` | `GetLolYourshopV1Themed` |  |

## Memory（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/memory/v1/fe/processes/ready` | `GetMemoryV1FeProcessesReady` |  |
| POST | `/memory/v1/notify/fe/processes/ready` | `PostMemoryV1NotifyFeProcessesReady` |  |
| POST | `/memory/v1/snapshot` | `PostMemoryV1Snapshot` |  |

## Patch（19）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/patch/er/v1/notifications` | `GetPatcherV1Notifications` |  |
| POST | `/patch/er/v1/notifications` | `PostPatcherV1Notifications` |  |
| DELETE | `/patch/er/v1/notifications/by/id` | `DeletePatcherV1NotificationsById` |  |
| GET | `/patch/er/v1/p2p/status` | `GetPatcherV1P2pStatus` |  |
| PATCH | `/patch/er/v1/p2p/status` | `PatchPatcherV1P2pStatus` |  |
| GET | `/patch/er/v1/products` | `GetPatcherV1Products` |  |
| DELETE | `/patch/er/v1/products/by/product/id` | `DeletePatcherV1ProductsByProductId` |  |
| POST | `/patch/er/v1/products/by/product/id/detect/corruption/request` | `PostPatcherV1ProductsByProductIdDetectCorruptionRequest` |  |
| POST | `/patch/er/v1/products/by/product/id/partial/repair/request` | `PostPatcherV1ProductsByProductIdPartialRepairRequest` |  |
| GET | `/patch/er/v1/products/by/product/id/paths` | `GetPatcherV1ProductsByProductIdPaths` |  |
| POST | `/patch/er/v1/products/by/product/id/signal/start/patching/delayed` | `PostPatcherV1ProductsByProductIdSignalStartPatchingDelayed` |  |
| POST | `/patch/er/v1/products/by/product/id/start/checking/request` | `PostPatcherV1ProductsByProductIdStartCheckingRequest` |  |
| POST | `/patch/er/v1/products/by/product/id/start/patching/request` | `PostPatcherV1ProductsByProductIdStartPatchingRequest` |  |
| GET | `/patch/er/v1/products/by/product/id/state` | `GetPatcherV1ProductsByProductIdState` |  |
| POST | `/patch/er/v1/products/by/product/id/stop/checking/request` | `PostPatcherV1ProductsByProductIdStopCheckingRequest` |  |
| POST | `/patch/er/v1/products/by/product/id/stop/patching/request` | `PostPatcherV1ProductsByProductIdStopPatchingRequest` |  |
| GET | `/patch/er/v1/products/by/product/id/tags` | `GetPatcherV1ProductsByProductIdTags` |  |
| GET | `/patch/er/v1/status` | `GetPatcherV1Status` |  |
| PUT | `/patch/er/v1/ux` | `PutPatcherV1Ux` |  |

## Payments（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/payments/v1/pmc/start/url` | `PostPaymentsV1PmcStartUrl` |  |
| GET | `/payments/v1/sku/price/points` | `GetPaymentsV1SkuPricePoints` |  |
| POST | `/payments/v1/update/payment/telemetry/state` | `PostPaymentsV1UpdatePaymentTelemetryState` |  |

## Performance（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/performance/v1/memory` | `GetPerformanceV1Memory` |  |
| POST | `/performance/v1/process/by/process/id` | `PostPerformanceV1ProcessByProcessId` |  |
| GET | `/performance/v1/quickstats` | `GetPerformanceV1Quickstats` |  |
| GET | `/performance/v1/report` | `GetPerformanceV1Report` |  |
| POST | `/performance/v1/report/restart` | `PostPerformanceV1ReportRestart` |  |
| GET | `/performance/v1/system/info` | `GetPerformanceV1SystemInfo` |  |

## PlayerNotifications（7）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/player-notifications/v1/config` | `GetPlayerNotificationsV1Config` |  |
| PUT | `/player-notifications/v1/config` | `PutPlayerNotificationsV1Config` |  |
| GET | `/player-notifications/v1/notifications` | `GetPlayerNotificationsV1Notifications` |  |
| POST | `/player-notifications/v1/notifications` | `PostPlayerNotificationsV1Notifications` |  |
| DELETE | `/player-notifications/v1/notifications/by/id` | `DeletePlayerNotificationsV1NotificationsById` |  |
| GET | `/player-notifications/v1/notifications/by/id` | `GetPlayerNotificationsV1NotificationsById` |  |
| PUT | `/player-notifications/v1/notifications/by/id` | `PutPlayerNotificationsV1NotificationsById` |  |

## PluginManager（7）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/plugin-manager/v1/external/plugins/availability` | `GetPluginManagerV1ExternalPluginsAvailability` |  |
| GET | `/plugin-manager/v1/status` | `GetPluginManagerV1Status` |  |
| GET | `/plugin-manager/v2/descriptions` | `GetPluginManagerV2Descriptions` |  |
| GET | `/plugin-manager/v2/descriptions/by/plugin` | `GetPluginManagerV2DescriptionsByPlugin` |  |
| GET | `/plugin-manager/v2/plugins` | `GetPluginManagerV2Plugins` |  |
| GET | `/plugin-manager/v2/plugins/by/plugin` | `GetPluginManagerV2PluginsByPlugin` |  |
| GET | `/plugin-manager/v3/plugins/manifest` | `GetPluginManagerV3PluginsManifest` |  |

## ProcessControl（2）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/process-control/v1/process` | `GetProcessControlV1Process` |  |
| POST | `/process-control/v1/process/quit` | `PostProcessControlV1ProcessQuit` |  |

## RiotMessagingService（14）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| DELETE | `/riot-messaging-service/v1/connect` | `DeleteRiotMessagingServiceV1Connect` |  |
| POST | `/riot-messaging-service/v1/connect` | `PostRiotMessagingServiceV1Connect` |  |
| DELETE | `/riot-messaging-service/v1/entitlements` | `DeleteRiotMessagingServiceV1Entitlements` |  |
| POST | `/riot-messaging-service/v1/entitlements` | `PostRiotMessagingServiceV1Entitlements` |  |
| GET | `/riot-messaging-service/v1/message/by/a` | `GetRiotMessagingServiceV1MessageByA` |  |
| GET | `/riot-messaging-service/v1/message/by/a/by/b` | `GetRiotMessagingServiceV1MessageByAByB` |  |
| GET | `/riot-messaging-service/v1/message/by/a/by/b/by/c` | `GetRiotMessagingServiceV1MessageByAByBByC` |  |
| GET | `/riot-messaging-service/v1/message/by/a/by/b/by/c/by/d` | `GetRiotMessagingServiceV1MessageByAByBByCByD` |  |
| GET | `/riot-messaging-service/v1/message/by/a/by/b/by/c/by/d/by/e` | `GetRiotMessagingServiceV1MessageByAByBByCByDByE` |  |
| GET | `/riot-messaging-service/v1/message/by/a/by/b/by/c/by/d/by/e/by/f` | `GetRiotMessagingServiceV1MessageByAByBByCByDByEByF` |  |
| GET | `/riot-messaging-service/v1/message/by/a/by/b/by/c/by/d/by/e/by/f/by/g` | `GetRiotMessagingServiceV1MessageByAByBByCByDByEByFByG` |  |
| DELETE | `/riot-messaging-service/v1/session` | `DeleteRiotMessagingServiceV1Session` |  |
| GET | `/riot-messaging-service/v1/session` | `GetRiotMessagingServiceV1Session` |  |
| GET | `/riot-messaging-service/v1/state` | `GetRiotMessagingServiceV1State` |  |

## Riotclient（41）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/riotclient/addorupdatemetric` | `PostRiotclientAddorupdatemetric` |  |
| DELETE | `/riotclient/affinity` | `DeleteRiotclientAffinity` |  |
| GET | `/riotclient/affinity` | `GetRiotclientAffinity` |  |
| POST | `/riotclient/affinity` | `PostRiotclientAffinity` |  |
| GET | `/riotclient/app/name` | `GetRiotclientAppName` |  |
| GET | `/riotclient/app/port` | `GetRiotclientAppPort` |  |
| GET | `/riotclient/auth/token` | `GetRiotclientAuthToken` |  |
| GET | `/riotclient/clipboard` | `GetRiotclientClipboard` |  |
| POST | `/riotclient/clipboard` | `PostRiotclientClipboard` |  |
| GET | `/riotclient/command/line/args` | `GetRiotclientCommandLineArgs` |  |
| POST | `/riotclient/kill/and/restart/ux` | `PostRiotclientKillAndRestartUx` |  |
| POST | `/riotclient/kill/ux` | `PostRiotclientKillUx` |  |
| POST | `/riotclient/launch/ux` | `PostRiotclientLaunchUx` |  |
| GET | `/riotclient/machine/id` | `GetRiotclientMachineId` |  |
| POST | `/riotclient/new/args` | `PostRiotclientNewArgs` |  |
| POST | `/riotclient/open/url/in/browser` | `PostRiotclientOpenUrlInBrowser` |  |
| GET | `/riotclient/region/locale` | `GetRiotclientRegionLocale` |  |
| POST | `/riotclient/show/swagger` | `PostRiotclientShowSwagger` |  |
| DELETE | `/riotclient/splash` | `DeleteRiotclientSplash` |  |
| PUT | `/riotclient/splash` | `PutRiotclientSplash` |  |
| GET | `/riotclient/system/info/v1/basic/info` | `GetRiotclientSystemInfoV1BasicInfo` |  |
| POST | `/riotclient/system/resume` | `PostRiotclientSystemResume` |  |
| POST | `/riotclient/system/suspend` | `PostRiotclientSystemSuspend` |  |
| GET | `/riotclient/trace` | `GetRiotclientTrace` |  |
| POST | `/riotclient/unload` | `PostRiotclientUnload` |  |
| POST | `/riotclient/ux/allow/foreground` | `PostRiotclientUxAllowForeground` |  |
| GET | `/riotclient/ux/crash/count` | `GetRiotclientUxCrashCount` |  |
| POST | `/riotclient/ux/flash` | `PostRiotclientUxFlash` |  |
| PUT | `/riotclient/ux/load/complete` | `PutRiotclientUxLoadComplete` |  |
| POST | `/riotclient/ux/minimize` | `PostRiotclientUxMinimize` |  |
| POST | `/riotclient/ux/show` | `PostRiotclientUxShow` |  |
| GET | `/riotclient/ux/state` | `GetRiotclientUxState` |  |
| PUT | `/riotclient/ux/state/ack` | `PutRiotclientUxStateAck` |  |
| DELETE | `/riotclient/v1/auth/tokens/by/auth/token` | `DeleteRiotclientV1AuthTokensByAuthToken` |  |
| PUT | `/riotclient/v1/auth/tokens/by/auth/token` | `PutRiotclientV1AuthTokensByAuthToken` |  |
| GET | `/riotclient/v1/crash/reporting/environment` | `GetRiotclientV1CrashReportingEnvironment` |  |
| PUT | `/riotclient/v1/crash/reporting/environment` | `PutRiotclientV1CrashReportingEnvironment` |  |
| POST | `/riotclient/v1/crash/reporting/logs` | `PostRiotclientV1CrashReportingLogs` |  |
| POST | `/riotclient/v1/elevation/requests` | `PostRiotclientV1ElevationRequests` |  |
| GET | `/riotclient/zoom/scale` | `GetRiotclientZoomScale` |  |
| POST | `/riotclient/zoom/scale` | `PostRiotclientZoomScale` |  |

## Sanitizer（3）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/sanitizer/v1/contains/sanitized` | `PostSanitizerV1ContainsSanitized` |  |
| POST | `/sanitizer/v1/sanitize` | `PostSanitizerV1Sanitize` |  |
| GET | `/sanitizer/v1/status` | `GetSanitizerV1Status` |  |

## ServicesApiConfig（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/services-api-config/v2/client/config/by/prefix/by/scope` | `PostServicesApiConfigV2ClientConfigByPrefixByScope` |  |

## ServicesApiGameSession（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| PUT | `/services-api-game-session/v1/game/session/token` | `PutServicesApiGameSessionV1GameSessionToken` |  |

## ServicesApiIdentity（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| PUT | `/services-api-identity/v1/player/alias` | `PutServicesApiIdentityV1PlayerAlias` |  |

## ServicesApiPlayerSession（6）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| DELETE | `/services-api-player-session/v1/access/token` | `DeleteServicesApiPlayerSessionV1AccessToken` |  |
| PUT | `/services-api-player-session/v1/access/token` | `PutServicesApiPlayerSessionV1AccessToken` |  |
| DELETE | `/services-api-player-session/v1/entitlements/token` | `DeleteServicesApiPlayerSessionV1EntitlementsToken` |  |
| PUT | `/services-api-player-session/v1/entitlements/token` | `PutServicesApiPlayerSessionV1EntitlementsToken` |  |
| DELETE | `/services-api-player-session/v1/user/info/token` | `DeleteServicesApiPlayerSessionV1UserInfoToken` |  |
| PUT | `/services-api-player-session/v1/user/info/token` | `PutServicesApiPlayerSessionV1UserInfoToken` |  |

## ServicesApiRiotMessageService（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| POST | `/services-api-riot-message-service/v1/data` | `PostServicesApiRiotMessageServiceV1Data` |  |

## System（1）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/system/v1/builds` | `GetSystemV1Builds` |  |

## Telemetry（5）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| GET | `/telemetry/v1/application/start/time` | `GetTelemetryV1ApplicationStartTime` |  |
| GET | `/telemetry/v1/common/data` | `GetTelemetryV1CommonData` |  |
| POST | `/telemetry/v1/common/data/by/key` | `PostTelemetryV1CommonDataByKey` |  |
| POST | `/telemetry/v1/events/by/event/type` | `PostTelemetryV1EventsByEventType` |  |
| POST | `/telemetry/v1/events/with/perf/info/by/event/type` | `PostTelemetryV1EventsWithPerfInfoByEventType` |  |

## Tracing（12）

| Method | Path | 函数名 | 置信 |
|---|---|---|---|
| DELETE | `/tracing/v1/performance/by/name` | `DeleteTracingV1PerformanceByName` |  |
| POST | `/tracing/v1/performance/by/name` | `PostTracingV1PerformanceByName` |  |
| POST | `/tracing/v1/trace/critical/flow` | `PostTracingV1TraceCriticalFlow` |  |
| POST | `/tracing/v1/trace/event` | `PostTracingV1TraceEvent` |  |
| POST | `/tracing/v1/trace/module` | `PostTracingV1TraceModule` |  |
| POST | `/tracing/v1/trace/non/timing/event/by/event/name` | `PostTracingV1TraceNonTimingEventByEventName` |  |
| POST | `/tracing/v1/trace/phase/begin` | `PostTracingV1TracePhaseBegin` |  |
| POST | `/tracing/v1/trace/phase/end` | `PostTracingV1TracePhaseEnd` |  |
| POST | `/tracing/v1/trace/step/event` | `PostTracingV1TraceStepEvent` |  |
| DELETE | `/tracing/v1/trace/time/series/event/by/event/name` | `DeleteTracingV1TraceTimeSeriesEventByEventName` |  |
| POST | `/tracing/v1/trace/time/series/event/by/event/name` | `PostTracingV1TraceTimeSeriesEventByEventName` |  |
| POST | `/tracing/v1/trace/time/series/event/by/event/name/marker/by/marker/name` | `PostTracingV1TraceTimeSeriesEventByEventNameMarkerByMarkerName` |  |

## 未推导的函数名

- `AsyncDelete`
- `AsyncResult`
- `AsyncStatus`
- `Cancel`
- `Exit`
- `Help`
- `Http2AsyncDelete`
- `Http2AsyncResult`
- `Http2AsyncStatus`
- `HttpAsyncDelete`
- `HttpAsyncResult`
- `HttpAsyncStatus`
- `LoggingGetEntries`
- `LoggingMetrics`
- `LoggingMetricsMetadata`
- `LoggingStart`
- `LoggingStop`
- `Subscribe`
- `Unsubscribe`
- `WebSocketFormat`
