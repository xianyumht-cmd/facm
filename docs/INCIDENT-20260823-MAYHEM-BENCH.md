# 2026-08-23 ARAM Mayhem Bench quick-pick regression

Observed on Tencent/China queue 2400 in ChampSelect/FINALIZATION after FACM 3.4.3 release: Live page connects and resolves all five allied champion IDs, but the quick-pick strip stays empty.

Root-cause direction:

- Current FACM only parses `benchChampionIds` from `/lol-champ-select/v1/session`.
- Current League champ-select payloads expose the actual bench as `benchChampions: [{ championId, ... }]`; `benchChampionIds` is not the reliable live field for this mode.
- Modern Team Builder champ select also exposes `isLegacyChampSelect=false`; its bench swap write target is `/lol-lobby-team-builder/champ-select/v1/session/bench/swap/{championId}`, while legacy champ select uses `/lol-champ-select/v1/session/bench/swap/{championId}`.
- The 3.4.3 implementation always used the legacy write endpoint and also added a pre-read before every click, adding avoidable latency to a race-sensitive manual action.

Fix requirements for 3.4.4:

1. Parse `benchChampions[].championId` first, with `benchChampionIds` only as backwards-compatible fallback.
2. Preserve client order and deduplicate IDs.
3. Resolve swap route from `isLegacyChampSelect`; default to legacy only when the flag is absent.
4. Use exactly one user-triggered POST to the resolved route. Do not pre-read before the POST; stale targets are handled by LCU response and read-back verification.
5. Verify success by re-reading the local champion after the POST. Never report success from HTTP 2xx alone.
6. Reduce visible active-bench polling to 100 ms only during ChampSelect; keep inactive/minimized/InGame throttles.
7. Keep Gate2 and all other writers isolated. Do not add auto-pick, auto-swap, reroll, dodge or skin writes.
8. Add deterministic fixtures for current queue-2400 shape (`benchChampions`, `isLegacyChampSelect=false`) and legacy fallback shape.
