# League Dodge Probe

## Purpose

FACM 3.5.38 uses this as a **read-only public field-test probe** for Tencent League Champion Select dodges. The first live probe already proved that the Tencent client can reliably expose a dodge event while still withholding the identity needed for direct side classification. The goal of this revision is therefore not to guess from one field, but to collect several short-lived local LCU signals and determine which positive signals are stable enough to distinguish our side from the opposing side.

The probe remains diagnostic. It does not show a final ally/enemy toast yet, and an ambiguous episode stays `unknown`.

## Verified Tencent evidence

Live FACM logs collected on 2026-09-09 captured multiple natural Champion Select dodges with the same shape:

- `/lol-matchmaking/v1/search` changed to `state=StrangerDodged`;
- `dodgerId=0` on the real Tencent client;
- `myTeam` exposed all 5 Summoner IDs;
- `theirTeam` exposed 5 slots but 0 opponent Summoner IDs;
- matchmaking resumed immediately after the detected dodge.

Therefore `StrangerDodged` alone is **not** treated as enemy proof, and the old public-schema assumption that a useful non-zero `dodgerId` will always be present is not valid for this Tencent build.

## Read-only contract

`LeagueDodgeProbeService` and `LeagueDodgeSideEvidenceProbe` receive only `ILeagueClientApi`. They have no League write interface.

The probe does not introduce LCU `POST`, `PUT`, `PATCH`, or `DELETE` requests. It cannot:

- accept or decline ReadyCheck;
- start or cancel matchmaking;
- pick, ban, reroll, swap, or dodge;
- send chat messages;
- change lobby membership.

`LeagueDashboardModule` remains the single authoritative Gameflow owner. The probe consumes its shared phase snapshot and does not create a second Gameflow loop or a second League session.

## Signals sampled

During a Champion Select episode the probe combines the following evidence:

1. **Matchmaking dodgeData**
   - reads `/lol-matchmaking/v1/search`;
   - records the primitive `dodgeData` shape, `state`, and `dodgerId`;
   - keeps direct ID classification when Tencent ever exposes a usable ID.

2. **ChampSelect team snapshots**
   - reads `/lol-champ-select/v1/session`;
   - caches the complete visible `myTeam` identity set;
   - samples the raw visible team at 250 ms so a one-player transient disappearance can be captured before the session closes;
   - also reads `chatDetails.chatRoomName` only to locate the relevant room conversation.

3. **Room/chat system evidence**
   - reads `/lol-chat/v1/conversations`;
   - prefers the conversation matching ChampSelect `chatRoomName`, then ChampSelect/party/lobby-like conversation types;
   - observes `lastMessage` during normal sampling;
   - looks for short-lived system/event departure messages such as the Tencent UI signal equivalent to `某某退出了组队房间`;
   - may correlate a departure name with the in-memory participant-to-Summoner mapping.

4. **Conversation participant changes**
   - reads the selected conversation's `participants` once for baseline;
   - during the post-dodge burst, checks whether exactly one participant disappears and whether that identity belongs to the cached ally team.

5. **Lobby member changes**
   - reads `/lol-lobby/v2/lobby` on a bounded cadence;
   - a positive disappearing member that matches the cached local team is additional ally/party evidence.

## Bounded sampling

Normal Champion Select sampling is intentionally bounded for a public build:

- `dodgeData`: 250 ms diagnostic cadence;
- raw ChampSelect team: 250 ms;
- conversation list / `lastMessage`: normal probe cadence;
- full conversation messages and participants: baseline once, not continuously;
- lobby: about 500 ms;
- after a confirmed dodge: approximately **1 second** of 125 ms evidence-burst sampling for the fleeting room/participant/team transition.

This burst exists specifically because the Tencent room-exit text and room teardown can be visible for only a fraction of a second.

## Classification rules

Positive evidence is preferred; absence is not promoted into certainty.

- `ally-party`: `PartyDodged`, or a positively identified disappearing lobby/party member;
- `ally`: direct `dodgerId` match, transient `myTeam` disappearance, relevant chat participant disappearance, system actor ID match, or a system departure message whose player name maps to the cached ally identity;
- `enemy`: direct opponent ID match, or a non-zero `dodgerId` excluded from a known-complete local roster;
- `unknown`: everything else.

In particular:

- `StrangerDodged` by itself remains `unknown`;
- `theirKnown=0` by itself is never enemy evidence;
- failure to observe an ally departure message is never enemy evidence;
- the probe does not infer enemy merely because a positive ally signal was missed.

## Chat privacy

This public probe does **not** intentionally log ordinary player chat messages.

- raw conversation IDs are represented by local episode labels such as `c1` / `c2` in the log;
- ordinary chat bodies are ignored;
- only system/event-style messages relevant to live dodge diagnosis, including departure-like room messages, may have a sanitized body written to the local FACM log;
- the body is bounded to 160 characters;
- participant display names may be held in memory for correlation but are not dumped as a separate roster.

The log remains local diagnostic data and should not be published publicly without review.

## Useful log markers

Search `<FACM folder>\logs\facm-YYYYMMDD.log` for:

- `League Dodge Probe initialized`
- `League Dodge Probe: episode-start`
- `League Dodge Probe: roster`
- `League Dodge Probe: search-baseline`
- `League Dodge Probe: chat-conversations`
- `League Dodge Probe: chat-candidate`
- `League Dodge Probe: chat-system`
- `League Dodge Probe: chat-participant-drop`
- `League Dodge Probe: my-team-transient-drop`
- `League Dodge Probe: lobby-member-drop`
- `League Dodge Probe: DODGE`
- `League Dodge Probe: evidence-burst-start`
- `League Dodge Probe: DODGE-EVIDENCE`
- `League Dodge Probe: DODGE-PHASE-RETURN`
- `League Dodge Probe: episode-end`

`DODGE-EVIDENCE` is the most useful final line for one captured episode because it contains the base Tencent dodge signal plus the side-evidence summary after the short burst.

## Public-test acceptance

Before adding a normal user-facing `己方玩家秒退` / `对方玩家秒退` notification, collect multiple real Tencent episodes from the public 3.5.38 field test and confirm which signals are repeatable.

The productized classifier should only promote a side when the field data demonstrates a positive, stable mapping. Ambiguous sessions continue to fail closed as `unknown`.
