# Mute State Reliability Experiment

**Date:** 2026-11-28
**Client:** LeagueClient 16.23 Tencent, `queueId=3140` (Practice Tool), `phase=ChampSelect`
**Script:** `tools/mute_state_experiment.py` + inline probe
**Related:** `FEATURE-PLAYBOOK.md` §1.1, `PRODUCT-INTEGRATION-PLAN.md` WP1

## Question

`GET /lol-champ-select/v1/muted-players` returns `[]` outside a session and `[]` at session start.
Is that an *empty list* (nobody muted) or an *unavailable sentinel*? The answer decides whether an
idempotent "mute all" is implementable, or only a blind toggle.

## Setup and limitation

Practice Tool was used because the account is under an active matchmaking lock
(`QUEUE_DODGER`, 39,233 s), so matchmade champion select is unreachable.

Practice Tool has `myTeam=1, theirTeam=0` — there is **no second participant**, so no real player
could be muted. The experiment therefore used a **synthetic identity** (summonerId `999999999`,
all-zero puuid). This is sufficient to answer the state-machine question — does the endpoint record
and forget a toggle target — and insufficient to prove behaviour with real teammates in a full
5v5 session.

Identifiers are synthetic or redacted; no personal data is recorded here.

## Observations

```
phase: ChampSelect   queueId: 3140   showQuitButton: true
session: myTeam=1 theirTeam=0 localPlayerCellId=0 timer.phase=BAN_PICK

GET  /lol-champ-select/v1/muted-players                      200  []
POST /lol-champ-select/v1/toggle-player-muted  (identity)    204
GET  /lol-champ-select/v1/muted-players                      200  [ {obfuscatedPuuid, obfuscatedSummonerId,
                                                                     puuid, summonerId} ]
POST /lol-champ-select/v1/toggle-player-muted  (same)        204
GET  /lol-champ-select/v1/muted-players                      200  []
```

The list entry returned the **same four fields** the toggle body carries:

```json
{"obfuscatedPuuid":"00000000-0000-0000-0000-000000000000",
 "obfuscatedSummonerId":999999999,
 "puuid":"00000000-0000-0000-0000-000000000000",
 "summonerId":999999999}
```

## Verified facts

1. **`muted-players` tracks state faithfully.** The identity appeared immediately after one toggle
   and disappeared immediately after the reverse toggle, within the same session.
2. **`[]` means "nothing is muted", not "state unavailable"** — at least in this client version and
   queue. The earlier "empty response is ambiguous" concern is resolved for this build.
3. **The entry schema is the toggle body schema** (four identity fields). Read and write are
   symmetric, so the read value can be compared directly against a candidate target.
4. **The endpoint does not validate that the identity exists.** A synthetic id was accepted
   (`204`) and recorded. It is a plain state machine keyed on identity, not a roster check.
5. **Toggle is the only write verb.** There is no `set-muted`; idempotence must be built by reading
   first and skipping targets already present in the list.

## Implementation consequence

An idempotent **mute-all is implementable**:

```
read muted-players  -> set M of already-muted identities
for each target T in (myTeam + theirTeam, excluding local player):
    if T not in M: POST toggle-player-muted (T)
```

This cannot unmute anyone, because already-muted targets are skipped. The reverse operation
(unmute-all) is symmetric: toggle only the identities currently in `M`.

The "blind toggle" fallback described in the plan is therefore not required for this client version;
it remains the correct behaviour only if a future build returns an error or an unparseable body from
`muted-players`.

## Residual unknowns

- Not reproduced with **real teammates in a 5v5 matchmade session** (account is locked).
- Not reproduced on **other queues** (only 3140).
- Behaviour when a target **leaves the session** between the read and the write is untested.
- Whether the list is **session-scoped or persists across sessions** was not measured; the baseline
  was empty both before and after, but that only shows this session ended clean.

These should be re-checked on the next session that has real participants; none of them blocks
implementing the read-then-skip design, because it degrades safely (worst case: a redundant toggle on
a target whose entry had already been removed).
