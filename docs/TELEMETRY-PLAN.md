# Anonymous Usage Telemetry Plan

## Scope

Implement a privacy-minimized, opt-in feature-usage counter for GGman 3.5.x.

## Rules

1. Plan before implementation and ship as a normal patch release.
2. Store consent locally under the existing portable data directory.
3. Keep telemetry independent from personal account history and ranking data.
4. Upload only daily aggregate feature counters and app version.
5. Do not upload PUUID, account names, credentials, IP addresses, hardware identifiers, file paths, logs, request URLs, or response bodies.
6. Use the existing anonymous CloudBase identity and RLS/RPC model.
7. Buffer events in memory and send them periodically; CloudBase failures are fail-soft.
8. Retain cloud aggregates for 180 days per owner.
9. Keep the event vocabulary explicit and small so new data cannot be collected accidentally by arbitrary callers.
10. Add deterministic build-time smoke coverage for the event contract.

## Initial event vocabulary

- `app_launch`
- `league_dashboard_open`
- `league_player_open`
- `league_live_open`
- `mayhem_lookup_open`
- `personal_stats_open`
- `opgg_advisor_open`
- `efficiency_open`
- `game_repair_open`
- `presence_open`
- `champ_select_companion_open`

## Explicitly deferred

Error payload telemetry, performance traces, raw game-state snapshots, account-level analytics, IP/device fingerprinting and cross-device identity are outside this stage.
