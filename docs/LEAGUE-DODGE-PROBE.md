# League Dodge Probe

## Purpose

This is a temporary live-field validation probe for the Tencent League Client LCU. Its only goal is to determine whether Champion Select dodge information exposed by the current client is sufficient to classify the dodger as our side, the opposing side, or unknown.

The probe is intentionally read-only. `LeagueDodgeProbeService` receives only `ILeagueClientApi`; it has no League write interface and therefore cannot accept/decline a ready check, start matchmaking, pick/ban/swap a champion, or cause a dodge.

## Runtime behavior

The existing `LeagueDashboardModule` remains the single authoritative Gameflow owner. The probe consumes that shared phase snapshot and does not add a second Gameflow polling loop.

When Gameflow enters `ChampSelect`, the probe starts one temporary episode:

- reads `/lol-champ-select/v1/session` to cache visible `myTeam` and `theirTeam` `summonerId` values;
- reads `/lol-matchmaking/v1/search` every 250 ms to observe `dodgeData`;
- refreshes the ChampSelect roster every 500 ms while ChampSelect remains active;
- keeps a short 12-second post-ChampSelect observation window for a normal dodge transition;
- stops when the episode ends, the client disconnects, or FACM exits.

There are no LCU POST, PUT, PATCH, or DELETE operations in the probe.

## Classification rules

A captured `dodgerId` is classified conservatively:

- `ally-party`: `PartyDodged`, or the ID matches `myTeam` and the state is `PartyDodged`;
- `ally`: the ID matches a cached `myTeam[].summonerId`;
- `enemy`: the ID matches cached `theirTeam[].summonerId`;
- `enemy`: the complete local-team roster is known and the dodger ID is excluded from that complete roster;
- `unknown`: the available identity data is incomplete or otherwise insufficient.

`StrangerDodged` alone is never treated as proof that the dodger was an enemy.

## Tester workflow

Use the CI-built FACM candidate normally. Do not intentionally dodge and do not change normal matchmaking behavior just for the probe.

Play several games. When the League client reports that a player left Champion Select and matchmaking resumes, note the approximate local time. After the test session, inspect or send the FACM log for that day:

`<FACM folder>\logs\facm-YYYYMMDD.log`

Useful search markers:

- `League Dodge Probe initialized`
- `League Dodge Probe: episode-start`
- `League Dodge Probe: roster`
- `League Dodge Probe: search-baseline`
- `League Dodge Probe: DODGE`
- `League Dodge Probe: search-unavailable`
- `League Dodge Probe: search-recovered`

A successful capture looks like:

`League Dodge Probe: DODGE; ...; state=...; dodgerId=...; side=...; basis=...`

The temporary diagnostic record includes numeric Summoner IDs so that side matching can be audited. It does not intentionally log player display names. Treat the raw test log as diagnostic data rather than something to publish publicly.

## Acceptance before productizing

Do not turn this probe into a user-facing ally/enemy toast solely from old public LCU schemas. First collect multiple real Tencent-client dodge episodes and confirm:

1. `dodgeData` is still exposed reliably;
2. `dodgerId` corresponds to the same Summoner ID namespace used by ChampSelect;
3. the field survives long enough for a read-only observer to capture it;
4. ally/enemy classification agrees with known test cases;
5. ambiguous sessions fail closed as `unknown`.

Only after live acceptance should the diagnostic logging be reduced and a normal user-facing notification be designed.
