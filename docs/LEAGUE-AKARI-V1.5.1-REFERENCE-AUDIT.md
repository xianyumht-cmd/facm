# League Akari v1.5.1 — Version-locked implementation research

**Status:** source-backed functional inventory in progress; **not** a claim that all features are implemented in GGman. This document is research input for Draft PR #355. It does not authorize release, source copying, binary execution or changes to the owner-approved compact UI.

## Authoritative artifact and source identity

The user supplied this **specific** Windows x64 distribution:
`https://akari-static.yuru-yuri.com/league-akari/releases/1.5.1/windows-x64/9b7819d22a50afe13f6a0b382d45ec1da60d3979d25fa7e36f99b8e93221224a/e5b2f673-63e7-4576-bd8e-8aeed03444d8/league-akari-win-x64.7z`

- Official release: https://github.com/LeagueAkari/LeagueAkari/releases/tag/v1.5.1
- Published: **2026-07-25T19:17:42Z**; Windows artifact `league-akari-win-x64.7z`, **88,579,402 bytes**.
- Official release asset `digest`: **SHA-256 `9b7819d22a50afe13f6a0b382d45ec1da60d3979d25fa7e36f99b8e93221224a`**, which exactly matches the 64-character digest segment in the user-provided URL.
- Official tag `refs/tags/v1.5.1` directly resolves to **`14557723706ccc0e0a9d62c470141d4cb7190fcd`**, also the release `target_commitish`. Its `package.json` reports `version: 1.5.1`.
- Source baseline: https://github.com/LeagueAkari/LeagueAkari/tree/14557723706ccc0e0a9d62c470141d4cb7190fcd
- Archive retrieval limitation: the analysis environment could not resolve the user's CDN host. **No archive was downloaded, extracted or executed; its internal files and actual runtime behavior have not been validated.** The release-asset digest comparison validates metadata correspondence, not a freshly computed local archive hash.
- Do **not** silently substitute the current default branch, a later pre-release, any 1.4.x or older source. Any future archive inspection must compute SHA-256 locally and compare to the digest above before acceptance.

## v1.5.1 source-backed functional inventory — initial pass

These are implementations and settings **present in the pinned source**, not a verified Windows/Tencent live-operation checklist.

| Area | Source implementation | Evidence from pinned source | GGman adaptation direction |
|---|---|---|---|
| Automated lobby/gameflow | `src/main/shards/auto-gameflow/{action-controller,lobby-flow-controller,matchmaking-controller,honor-controller,invitation-controller,aram-team-side-controller}.ts` | Auto-accept and delay, matchmaking/rematch, post-game actions/honor, reconnection, invitation handling, ARAM team side are configured in `state.ts` and wired in `index.ts` | Compare with existing `LeagueEfficiencyModule` and its writer interfaces; add only missing capabilities with cancellation and verifiable LCU readback |
| Ban/pick & bench/trade | `src/main/shards/auto-select/{action-executor,ban-pick-controller,bench-controller,trade-controller,config-manager}.ts` | Controllers wired and observed by `auto-select/index.ts`; settings support configurations and temporary disable | Reuse GGman's `LeagueBenchQuickPickService`, current ChampSelect owner and League phase events; **do not** create a second continuous poller |
| Champion presets | `src/main/shards/auto-champ-config/{auto-config-controller,state,index}.ts` | Per-champion rune and summoner-spell profiles; feature enable and settings persistence | Compare to existing GGman `LeagueBuildAdvisorModule` and automatic-apply coordinator; preserve original write permission checks |
| Live-match analysis | `src/main/shards/ongoing-game/{match-history-loader,player-data-loader,analysis-controller,additional-info-controller,champ-select-handoff-controller}.ts` | Match history, ranked stats, mastery, tracked teammate details and queue-sensitive phase handoff; bounded queue via `QueueKeeper` | Compare to `LeaguePlayerDataService`, `LeagueLiveModule` and `LeagueRuntimeCompanionController`; prefer lifecycle-scoped, bounded data acquisition |
| Player tags & encountered games | `src/main/shards/saved-player/index.ts` and `src/main/shards/storage/entities/*` | Per-user tagged players and prior encountered matches in local persistence | Potential optional **local-only** GGman feature with explicit privacy/retention design; never send player data to new cloud tables by default |
| Respawn timer | `src/main/shards/respawn-timer/{respawn-timer-controller,index}.ts` | Opt-in feature, active only during `InProgress`, reads client-provided live-player timer | Optional later feature after verifying client support, gameplay policy and very low poll/CPU cost; no invasive game memory reading |
| Accounts / server data gateway | `src/main/shards/sgp/{http-client-controller,protocol-controller,token-state-controller}.ts` | Gateway authorization/server mapping and SGP requests | Study for concepts; reuse GGman's existing authenticated client and server mapping, **never** ship Akari/third-party secrets or impersonate clients |
| Diagnostics & updates | `src/main/shards/app-common/diagnostics-controller.ts`, `src/main/shards/self-update/update-controller.ts` | Diagnostics, update lifecycle and platform checks present | Preserve GGman managed logs, coordinated correlation IDs, signed update/trust chain and small binaries; no Electron services |

## Compatibility, quality and acceptance rules

1. User reports hands-on successful Tencent-server usage of Akari. The upstream README support disclaimer is **not proof of technical non-functionality**. Still validate each GGman endpoint and behavior with authenticated, human-operated Chinese LOL test sessions before shipping.
2. GGman remains native WinForms, lightweight, and based on its existing `LayeredFloatingBall` and approved compact panel. **Never change visual geometry or orb behavior** under the pretext of porting an Akari feature. Do not introduce Electron, embedded Chromium, a second LCU monitor or always-on multiple network pollers.
3. Research process per feature: read pinned v1.5.1 **settings -> controller -> API client/writer -> error/fallback -> lifecycle -> tests**, then compare existing GGman equivalents. Classify **already implemented**, **partial**, **missing**, **not practical**. Source presence alone must not become a "GGman complete" checkbox.
4. New features must be opt-in when they mutate LCU state. Preserve writer authorization, queue restrictions, delayed task cancellation, read-after-write confirmation, phase cancellation and process cleanup.
5. Source-aware network issues: especially Tencent LCU region, external ranking patches and SGP authentication. Never fabricate a tier/ranking because an upstream endpoint is blocked.
6. Diagnostics are part of each feature: one correlation ID across interaction, LCU read/write, remote provider, cache and render; export sanitised, bounded component logs alongside `facm-YYYYMMDD.log`. Avoid account IDs, tokens, private chats, raw LCU payloads and any personal identifiers.
7. Prove the runtime/CPU benefit on user-class Windows 10 hardware: cold start, private working set, idle CPU, active ChampSelect CPU, allocations and resource cleanup. Binary size alone is not a performance metric.
8. Keep PR #355 Draft, no production release/merge, and no claim of archival binary reverse engineering. Maintain MIT attribution if reusing actual copyrighted source, not just functional concepts.

## Next research and implementation gate

Continue reviewing the **pinned 1.5.1** controllers method-by-method and write a feature-by-feature compatibility matrix. Prioritize upgrading GGman's existing flows over opening new subsystems. Current PR #355's role-qualified public rank source work and compact mode integration must finish CI/live-source validation independently; do not represent them as Akari-derived functionality or as already released.
