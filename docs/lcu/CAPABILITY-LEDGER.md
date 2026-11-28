# HLMD — Capability and operation traceability ledger

Baseline: PR #370, executable candidate 7ff70262. Latest verified full head 76aea19b (Windows 46164390255, Glass 46164390270, UI Text 46164390242, Mayhem 46164390239). Diagnostics body-cancellation candidate passed CI; shared global Mayhem ranking page candidate pending CI. Previous full CI: 9b52b943 (Windows 46148139501, Glass 46148139491, UI Text 46148139489, Mayhem 46148139490); Windows job 140988616748 executed history smoke and host/response/performance/update contracts. This ledger is an initial source mapping, not a completion report. Source paths below are relative to src/FACM/League unless specified. UI placement, every individual request body and real-client behavior are still open unless explicitly evidenced.

## Playbook inventory (29 numbered sections + two unnumbered sections)

| Source | Intended operation / plugin | Current owner or located evidence | State / next verification |
|---|---|---|---|
| 1.1 | Mute all; champ-select | LeagueChampSelectMuteRuntime.cs, LeagueResearchIntegrationSmokeTest.cs | Existing four-field identity, read-before-toggle; training experiment only; real PvP remaining. |
| 1.2 | Pick/ban/lock; champ-select | LeagueAutoPickBanController.cs, LeagueChampSelectActionWriteRuntime.cs, LeagueAutoPickBanSmokeTest.cs | Combined PATCH now uses the documented two fields, scoped transport owner, action-ID/local-cell/context/champion/completed readback and bounded retry. 23fe117f intermediate CI failed an outdated Gameflow read-count assertion; revised fixtures passed all four gates at 3018803f. Staged show/lock strategy retains existing behavior; live confirmation pending. |
| 1.3 | ARAM reroll; champ-select | LeagueBenchQuickPickService.cs, LeagueBenchSwapWriteRuntime.cs, LeagueLiveDataService.cs, native and Glass Runtime Companion | c08c6f7: explicit manual scoped POST; fresh draft/count/eligibility, shared mutation gate, new champion + count decrement readback; UI only when explicit rerollsDisabled=false. Live availability of that UI-derived field is unverified. No claim of actual inline lobby integration. |
| 1.4 | Bench swap; champ-select | LeagueBenchSwapWriteRuntime.cs, LeagueBenchQuickPickService.cs, LeagueAutoPickBanController.cs | Both bench route prefixes present; 4ddb8a77 scopes automatic bench and manual card/bench writes. b16c64f1 adds explicit mastery-priority bench choice using the researched local mastery route, eligible known scores and verified scoped swap in both companion surfaces. 60229386 adds opt-in automatic mastery ranking and minimum score, one mastery read and at most one mastery swap per selection episode; 4c5eef40 corrects its smoke clock. Live mode/cooldown verification and inline lobby placement remain open. |
| 1.5 | Champion, position, pick-order swaps; champ-select | LeagueBenchSwapWriteRuntime.cs, LeagueAutoPickBanController.cs | 4ddb8a77 scopes automatic champion response, adds bounded retry and same-ID terminal readback. e43afb8e adds typed three-family manual incoming accept/decline and outgoing cancel in both companion team surfaces, read on explicit click. Source provides exact route families, but POSITION/PICK_ORDER/SENT payload enums and terminal response persistence remain unverified on live Tencent; explicit request initiation, clear route distinction, shared auto/manual arbitration and true inline lobby placement remain open. |
| 1.6 | Eligible exit; champ-select | LeagueChampSelectQuitRuntime.cs, LeagueChampSelectQuitSmokeTest.cs | showQuitButton and phase/lobby readback implemented; legacy spectator route not located. No restricted-exit bypass. |
| 1.7 | Team boost; champ-select | Not located | Open: catalog/eligibility and explicit purchase, no automatic spending. |
| 2.1 | Lobby creation/change; parties | LeagueLobbyInvitationAutomation.cs reads lobby; no create writer located | Open: 42562757 adds typed POST queueId transaction with fresh owner/phase/lobby and queue readback; 7ff70262 exposes both Efficiency surfaces. Dynamic catalog candidate replaces six fixed IDs with visible Available local choices and rechecks before each queue write. Live Tencent catalog schema, account restrictions, custom-room catalog and actual inline placement remain open. |
| 2.2 | Leave lobby/cancel queue; parties | LeagueMatchmakingWriteRuntime.cs, LeagueMatchmakingStopController.cs | Search DELETE exists; 42562757 adds separately scoped DELETE lobby, with observed None confirmation. 7ff70262 provides explicit leave action, not process termination. |
| 2.3 | Accept invites; parties | LeagueLobbyInvitationAutomation.cs, LeagueMatchmakingWriteRuntime.cs | Accept/decline present; candidate adds scoped transport and exact-ID terminal readback. Missing terminal record remains unverified; live state and party-join confirmation open. |
| 2.4 | Join party; parties | Not located | Open: explicit eligible party join. |
| 2.5 | Play again; navigation/postgame | LeaguePostGameAutomation.cs, LeaguePostGameWriteRuntime.cs | Waits EndOfGame and verifies Lobby; f0f3d3f7 CI passed, field outstanding. |
| 2.6 | Kick/promote/invite privilege; parties | LeagueMatchmakingWriteRuntime.cs | 42562757 adds scoped kick and explicit promotion with leader/member checks and state confirmation; 7ff70262 exposes both Efficiency surfaces. Automatic promotion now uses the same room transaction and requires target-specific leader readback before room notice. Candidate adds explicit grant/revoke invite privilege with local toggle authority, target permission and readback; live verification and inline placement remain open. |
| 2.7 | Invitations; parties | LeagueFriendInvitationController.cs, LeagueMatchmakingWriteRuntime.cs | toSummonerId and decline present; contextual PUUID and recent teammate scope open. |
| 2.8 | Role preference/spectator slot; parties | LeagueLobbyRoomService, LeagueMatchmakingWriteRuntime, native and Glass Efficiency | Candidate implements explicit first/second preference and eligible custom spectator slot entry with fresh capability snapshot and readback. Tencent schema/capacity and inline placement remain open. |
| 2.9 | Custom bots; parties | Not located | Open: supported difficulty/team/position and modify/remove. |
| 3.1 | Friend requests; social | LeagueFriendInvitationController.cs manages invitations, not friend requests | Open: distinguish friend requests from party invitations. |
| 3.2 | Friend groups/notes/delete; social | LeagueFriendActivityService.cs read-side | Open: full group/list management and verification. |
| 3.3 | Block/unblock; social | LeagueSocialService.cs, LeagueResearchIntegrationSmokeTest.cs | Read count and write helper exist; helper checks HTTP only, UI ownership and readback open. |
| 3.4 | Conversations/messages; social | LeagueAutoReplyController.cs, LeagueMatchmakingWriteRuntime.cs | Existing reply/quick-message writes; group creation/active-conversation management and history open. |
| 3.5 | Availability/signature; social | LeaguePresenceService.cs, LeaguePresenceWriteRuntime.cs, LeagueOfflineStatusLockController.cs | Initial/scoped/event-driven correction CI verified at 9b52b943; actual availability-only merge semantics and friend-visible state open. |
| 4.1 | Skip pre-end sequence; postgame | LeaguePostGameAutomation.cs, LeaguePostGameWriteRuntime.cs | Exact researched POST added; phase and sequence rechecked, honor/mission guarded; f0f3d3f7 CI passed, field outstanding. |
| 4.2 | Honor and refresh; postgame | LeaguePostGameAutomation.cs, LeaguePostGameAutomationSmokeTest.cs | CI verified at 9b52b943; actual modern body and target-confirmed live evidence open. |
| 4.3 | Dismiss stats; navigation | No writer located | Open: separately verify effect relative to play-again; do not dismiss before preserving desired party or unresolved honor. |
| 4.4 | Report; social | Playbook claims existing report module; not located in reviewed owner list | Verify actual source before claiming presence; explicit user-controlled report. |
| 5.1 | Bulk purchase; store | Not located | Open: actual catalog/payment eligibility, explicit purchase and token outcome. |
| 5.2 | Craft/redeem/milestones/refresh; loot | LeagueRewardsOverviewService.cs read-only | Open: recipe slots and material counts, confirmation and no duplicate spending. |
| 5.3 | Mission reward/series; objectives | LeagueRewardsOverviewService.cs read-only | Open: claim eligibility, reward group and series selection. |
| 5.4 | Bulk reward selection; objectives | Not located | Open: actual request shape and eligibility. |
| 6 | Spectate | LeagueEncounterReplayService.cs is replay, not proof of spectator launch | Open: actual supported spectate command and eligibility. |
| 7 | Security diagnostic | Source mentions telemetry POST; not a demonstrated read-only API | No new security-system interaction; ordinary diagnostics from available client reports only. |

## Matrix traceability (all 61 IDs)

This is a complete ID-level source-location inventory, not an exhaustive owner/body/UI verification or completion claim. "Partial" means a relevant existing owner or candidate exists, not the original feature fully implemented; "Open" means the original target is not established. Source file names and Shard registrations identify the first audit target. All actual League Client inline targets remain distinct from the external companion.

| ID | Original capability | Located owner / evidence | Current disposition and remaining work |
|---|---|---|---|
| A1 | One-click account switching / stored login sessions | Owner excluded | No implementation or substitute project. |
| A2 | Login failure diagnosis | LeagueClientModule; ResilientLeagueClientSessionDiscovery; existing diagnostics | Partial — diagnose actual login/launcher failure classes, not just LCU discovery. |
| A3 | Suppress unwanted lobby startup popups | No confirmed lobby owner | Open — inventory ordinary popup preferences/dismissal and actual League Client effect. |
| A4 | Custom lobby startup page, background and music | HLMD landing-page proposal; existing AppearanceShard is GGman-only | Open — actual supported lobby startup customization, reversible configuration. |
| A5 | Multiple League Client instances | Owner excluded | No implementation or substitute project. |
| A6 | Reduce optional client loading animations | LeaguePostGameAutomation (postgame only) | Open — startup animation target not covered by postgame. |
| B1 | Suppress all nonessential lobby advertising categories | HLMD lobby slimming reports | Open — actual lobby promotion categories and resource metrics; GGman-only no parity. |
| B2 | Integrated custom lobby workbench | 23 registered Shards; Runtime Companion | Partial — external owners exist; genuine lobby entry/page integration unverified. |
| B3 | Lobby shortcuts and command palette | QuickMessagesShard; LeagueEfficiencyHotkeys | Partial — current shortcuts; actual lobby command palette outstanding. |
| B4 | Hide/rearrange lobby modules | No confirmed actual lobby layout owner | Open — supported lobby navigation/layout customization. |
| B5 | Enhanced lobby friend list | LeagueFriendActivityService; LeagueFriendInvitationController | Partial — friend reads/invitations; actual lobby groups, sorting and editing outstanding. |
| B6 | Detailed friend activity | LeagueFriendActivityService | Partial — available presence data; actual friend-row display and privacy limits. |
| B7 | Lobby server and queue status | LeagueQueueStatusService; LeagueLeaverBusterService; LeagueInsightService | Partial — existing estimates/restrictions; regional health and actual lobby placement open. |
| B8 | Lobby asset overview | LeagueRewardsOverviewService; LeagueInsightService | Partial — read-side rewards; inventory/currency/claims and lobby placement open. |
| B9 | League Client performance controls | HLMD performance measurements; existing PerformanceModule | Open — measure actual LeagueClient/Ux/renderer and supported performance settings. |
| B10 | Client crash recovery | LeagueGameflowReconnectController; LeagueClientUxRepairWriteRuntime | Partial — audit normal relaunch, crash versus exit and bounded recovery. |
| B11 | One-click complete client exit | LeagueEfficiencyActions; LeagueEfficiencyHotkeys | Partial — verify actual close lifecycle and existing UX. |
| B12 | League Client window enhancement | Runtime Companion window state | Open — actual League Client window controls not established. |
| C1 | Artificial matchmaking activity / AFK avoidance | Owner excluded | No implementation or substitute project. |
| C2 | Precise queue information | LeagueQueueStatusService; LeagueLeaverBusterService | Partial — queue estimates and restrictions; full UI and data provenance. |
| C3 | Multi-channel ready-check reminders | LeagueMatchmakingAutomation | Partial — ready check owner; multi-channel notification coverage open. |
| C4 | Matchmaking minimize and restore | DesktopEntryGameflowPolicy | Open — existing GGman ball policy is not actual lobby minimize/restore. |
| C5 | Short/long queue prediction | LeagueQueueStatusService | Open — historical predictive model and uncertainty. |
| D1 | Return-to-lobby button in champion selection | LeagueChampSelectQuitRuntime; Runtime Companion | Partial — eligible quit verified in historical training; actual inline placement and legacy spectator path open. |
| D2 | Inline player intelligence | LeaguePlayerDataService; LeagueRuntimeCompanionController | Partial — available player history; actual card placement and roster limits. |
| D3 | Inline matchup/counter information | LeagueBuildAdvisorDataService; MayhemGuideShard | Partial — verify public matchup/counter datasets and inline placement. |
| D4 | Inline match win probability | LeaguePremadeInference; existing history data | Open — calibrated win probability model not established. |
| D5 | Precise timer and voice reminder | LeagueRuntimeCompanionController; timer snapshot | Partial — display existing timer; drift/voice and actual inline placement open. |
| D6 | Mute all and emote controls | LeagueChampSelectMuteRuntime; Runtime Companion | Partial — four-field verified-state mute; emote semantics and actual inline placement open. |
| D7 | Timed champion/loadout adjustments | LeagueAutoPickBanController; LeagueAutoApplyController; LeagueBenchQuickPickService | Partial — scoped action/reroll CI verified at 3018803f; new three-family swap responses and manual mastery bench candidate. Automatic mastery threshold now implemented in existing profile editor; initiating swaps, clear route and live evidence remain. |
| D8 | Avoid banning teammate intent | LeagueAutoPickBanController | CI verified at f0f3d3f7 for teammate-intent ban protection; live outstanding. |
| D9 | Inline build/rune panel | LeagueBuildApplyService; LeagueRuntimeCompanionController | Partial — existing recommendation/apply; true inline placement outstanding. |
| D10 | Chat and emote suppression | LeagueChampSelectMuteRuntime; LeagueAutoReplyController | Partial — chat mute; actual emote/channel filtering unresolved. |
| D11 | Disconnect/AFK warning | LeagueLiveDataService; Gameflow monitor | Open — teammate-specific disconnect evidence and warning UI. |
| D12 | Composition gap analysis | LeagueLiveDataService; Runtime Companion team projection | Open — composition scoring, available data and inline surface. |
| E1 | Game-renderer loading-screen overlay | Owner excluded | No implementation or substitute project. |
| E2 | Loading stall diagnosis and one-click reconnect | LeagueGameflowReconnectController; LeagueDashboardPhaseService | Partial — independent executable observation CI verified; loading diagnosis and actual reconnect audit. |
| E3 | Preload matchup guides | LeagueRuntimeCompanionController; existing guide request lifecycle | Partial — prefetch by selected matchup and cancellation coverage. |
| F1 | Live data in League Client lobby | LeagueLiveDataService; LiveMatchShard | Partial — existing authorized live data; actual lobby panel outstanding. |
| F2 | Key-event screenshots and recordings | No confirmed capture owner | Open — supported OS capture, event evidence, retention and resource budget. |
| F3 | In-game voice/text operation in game process | Owner excluded | No implementation or substitute project. |
| G1 | Enhanced actual postgame page | LeagueEndOfGameStatsService; LeagueInsightService; history services | Partial — existing stats; actual postgame placement and analysis. |
| G2 | MMR change estimate | LeagueRankedStatusService; LeaguePlayerDataService | Open — observable-data MMR estimate and uncertainty. |
| G3 | Automatic return to lobby | LeaguePostGameAutomation; LeaguePostGameWriteRuntime | CI verified at f0f3d3f7 for phase-aware return; dismiss-stats and field outstanding. |
| G4 | Inline postgame social actions | LeagueSocialService; LeagueFriendInvitationController | Partial — block helper; report/add-friend/postgame row ownership and readback. |
| G5 | Custom honor recipient and vote count | LeaguePostGameAutomation; PostgameSettingsShard | Partial — prior ballot/confirmation CI verified; configurable votes and live confirmed payload. |
| H1 | Local chat history/search/export | LeagueChatEventStream; LeagueAutoReplyController | Open — account-local history, retention, export and search. |
| H2 | Message history and recall behavior | LeagueChatEventStream | Open — permitted update/deletion semantics; do not claim recall bypass. |
| H3 | Custom presence/status presentation | LeaguePresenceService; LeagueOfflineStatusLockController; PresenceShard | Partial — initial/scoped/event correction CI verified; live visibility and merge semantics open. |
| H4 | Friend presence notifications | LeagueFriendActivityService; LeagueChatEventStream | Open — permitted friend-specific transitions and notifications. |
| H5 | Friend group communication | LeagueMatchmakingWriteRuntime; QuickMessagesShard | Partial — existing explicit communication; group selection and rate limits open. |
| H6 | Keyword filters/auto reply | LeagueAutoReplyController; LeagueChatEventStream | Partial — existing auto reply; actual supported keyword filtering and loop audit. |
| I1 | Remove single-instance restriction | Owner excluded | No implementation or substitute project. |
| I2 | League Client developer diagnostics | HLMD research reports; GGman WebView2 diagnostics | Open — authorized League Client diagnostics availability; no bypass. |
| I3 | Lobby UI extension framework | Existing Shards and external Runtime Companion | Open — genuine supported page integration; separate fallback. |
| I4 | Local response/presentation transformation | LeagueLiveDataService and existing typed projections | Partial — presentation projections; exact lobby preview objective and authoritative distinction. |
| I5 | LCU request diagnostics and replay | Existing scoped LCU transports; HLMD mock fixtures | Partial — sanitized request diagnostics and replay contract; no interception of unrelated processes. |
| I6 | Prevent mandatory League Client updates | Owner excluded | No implementation or substitute project. |
| I7 | League Client resource customization | AppearanceShard; HLMD resource research | Open — actual supported League Client resource customization; GGman skins not parity. |
| I8 | Unowned cosmetic client-side preview | ProfileCustomizationShard; existing cosmetic catalog services | Open — actual visual-only lobby preview and entitlement distinction. |
| I9 | League Client crash dump analysis | DiagnosticsExportService; HLMD crash incident reports | Partial — existing GGman diagnostics; actual client-generated crash evidence and redaction. |

## Additional L1 groups

| Group | Located evidence | Remaining |
|---|---|---|
| Asset overview and reward collection | LeagueRewardsOverviewService | Inventory, currencies, eligible claims and actual lobby entry. |
| Full friend management | LeagueFriendActivityService, LeagueSocialService | Groups, requests, notes, explicit actions and lobby placement. |
| Match-data export | Existing history services | Audit actual export, schema, redaction and selection. |
| Server and queue information | LeagueQueueStatusService, LeagueLeaverBusterService | Health provenance, available fields and actual lobby placement. |
| Match-history sharing image | Existing match-history UI | Local rendering, user-selected content, privacy and export. |
| Account/login diagnostics | Existing discovery and diagnostics | Actual login failure evidence and user-accessible reports. |

J limitations remain in the master plan. Supplemental research obligations remain in §1.1. Exact-head CI and actual client evidence are separate. Continue operation-level audit and add exact route/method/body/event/tests and UI placement evidence; no arbitrary closure.
