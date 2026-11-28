# HLMD — Capability and operation traceability ledger

Baseline: PR #370, executable candidate bc266594 plus scoped postgame transport in this commit. Previous full CI: 9b52b943 (Windows 46148139501, Glass 46148139491, UI Text 46148139489, Mayhem 46148139490); Windows job 140988616748 executed history smoke and host/response/performance/update contracts. This ledger is an initial source mapping, not a completion report. Source paths below are relative to src/FACM/League unless specified. UI placement, every individual request body and real-client behavior are still open unless explicitly evidenced.

## Playbook inventory (29 numbered sections + two unnumbered sections)

| Source | Intended operation / plugin | Current owner or located evidence | State / next verification |
|---|---|---|---|
| 1.1 | Mute all; champ-select | LeagueChampSelectMuteRuntime.cs, LeagueResearchIntegrationSmokeTest.cs | Existing four-field identity, read-before-toggle; training experiment only; real PvP remaining. |
| 1.2 | Pick/ban/lock; champ-select | LeagueAutoPickBanController.cs, LeagueChampSelectActionWriteRuntime.cs, LeagueAutoPickBanSmokeTest.cs | Single combined PATCH exists; teammate-intent protection extended to ban in bc266594. HTTP success is not readback; audit action confirmation, route and write owner. |
| 1.3 | ARAM reroll; champ-select | No dedicated owner located in reviewed files | Open: verify rerollsRemaining/rerollsDisabled and readback, explicit user action. |
| 1.4 | Bench swap; champ-select | LeagueBenchSwapWriteRuntime.cs, LeagueBenchQuickPickService.cs, LeagueAutoPickBanController.cs | Both route prefixes present; verify actual mode, cooldown, mastery choice and UI. |
| 1.5 | Champion, position, pick-order swaps; champ-select | LeagueBenchSwapWriteRuntime.cs, LeagueAutoPickBanController.cs | Champion accept/decline exists; position/order and cancel/clear still open. |
| 1.6 | Eligible exit; champ-select | LeagueChampSelectQuitRuntime.cs, LeagueChampSelectQuitSmokeTest.cs | showQuitButton and phase/lobby readback implemented; legacy spectator route not located. No restricted-exit bypass. |
| 1.7 | Team boost; champ-select | Not located | Open: catalog/eligibility and explicit purchase, no automatic spending. |
| 2.1 | Lobby creation/change; parties | LeagueLobbyInvitationAutomation.cs reads lobby; no create writer located | Open: POST queueId, verify lobby and eligibility. |
| 2.2 | Leave lobby/cancel queue; parties | LeagueMatchmakingWriteRuntime.cs, LeagueMatchmakingStopController.cs | Search DELETE exists; separate leave-lobby write not located. |
| 2.3 | Accept invites; parties | LeagueLobbyInvitationAutomation.cs, LeagueMatchmakingWriteRuntime.cs | Accept/decline present; audit outcome and ownership. |
| 2.4 | Join party; parties | Not located | Open: explicit eligible party join. |
| 2.5 | Play again; navigation/postgame | LeaguePostGameAutomation.cs, LeaguePostGameWriteRuntime.cs | Candidate now waits EndOfGame and verifies Lobby; exact-head CI/field outstanding. |
| 2.6 | Kick/promote/invite privilege; parties | LeagueMatchmakingWriteRuntime.cs | Promote allowlisted; kick and privilege not located. |
| 2.7 | Invitations; parties | LeagueFriendInvitationController.cs, LeagueMatchmakingWriteRuntime.cs | toSummonerId and decline present; contextual PUUID and recent teammate scope open. |
| 2.8 | Role preference/spectator slot; parties | Not located | Open: actual lobby operations and eligibility. |
| 2.9 | Custom bots; parties | Not located | Open: supported difficulty/team/position and modify/remove. |
| 3.1 | Friend requests; social | LeagueFriendInvitationController.cs manages invitations, not friend requests | Open: distinguish friend requests from party invitations. |
| 3.2 | Friend groups/notes/delete; social | LeagueFriendActivityService.cs read-side | Open: full group/list management and verification. |
| 3.3 | Block/unblock; social | LeagueSocialService.cs, LeagueResearchIntegrationSmokeTest.cs | Read count and write helper exist; helper checks HTTP only, UI ownership and readback open. |
| 3.4 | Conversations/messages; social | LeagueAutoReplyController.cs, LeagueMatchmakingWriteRuntime.cs | Existing reply/quick-message writes; group creation/active-conversation management and history open. |
| 3.5 | Availability/signature; social | LeaguePresenceService.cs, LeaguePresenceWriteRuntime.cs, LeagueOfflineStatusLockController.cs | Initial/scoped/event-driven correction CI verified at 9b52b943; actual availability-only merge semantics and friend-visible state open. |
| 4.1 | Skip pre-end sequence; postgame | LeaguePostGameAutomation.cs, LeaguePostGameWriteRuntime.cs | Exact researched POST added; phase and sequence rechecked, honor/mission guarded; candidate. |
| 4.2 | Honor and refresh; postgame | LeaguePostGameAutomation.cs, LeaguePostGameAutomationSmokeTest.cs | CI verified at 9b52b943; actual modern body and target-confirmed live evidence open. |
| 4.3 | Dismiss stats; navigation | No writer located | Open: separately verify effect relative to play-again; do not dismiss before preserving desired party or unresolved honor. |
| 4.4 | Report; social | Playbook claims existing report module; not located in reviewed owner list | Verify actual source before claiming presence; explicit user-controlled report. |
| 5.1 | Bulk purchase; store | Not located | Open: actual catalog/payment eligibility, explicit purchase and token outcome. |
| 5.2 | Craft/redeem/milestones/refresh; loot | LeagueRewardsOverviewService.cs read-only | Open: recipe slots and material counts, confirmation and no duplicate spending. |
| 5.3 | Mission reward/series; objectives | LeagueRewardsOverviewService.cs read-only | Open: claim eligibility, reward group and series selection. |
| 5.4 | Bulk reward selection; objectives | Not located | Open: actual request shape and eligibility. |
| 6 | Spectate | LeagueEncounterReplayService.cs is replay, not proof of spectator launch | Open: actual supported spectate command and eligibility. |
| 7 | Security diagnostic | Source mentions telemetry POST; not a demonstrated read-only API | No new security-system interaction; ordinary diagnostics from available client reports only. |

## Matrix and supplemental tracking

All original 61 IDs and exact seven owner exclusions remain in PRODUCT-INTEGRATION-PLAN.md. Map each retained ID to the actual owner and UI placement during the full source audit; do not count an existing external window as inline parity. Current mapped P0 IDs H3 (presence), G3/G5 (return/honor), D8 (protect teammate intent), E2 (process/phase observation). Other matrix IDs remain unverified in this ledger rather than implicitly complete. All six L1 groups remain required. Supplemental research obligations and remaining gaps are listed in master plan §1.1. Exact-head CI and actual client evidence must be recorded as separately scoped results.

