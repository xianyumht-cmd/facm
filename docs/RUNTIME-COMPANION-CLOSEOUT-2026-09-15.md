# Runtime Companion closeout — 2026-09-15

PR #283 (`feat/runtime-companion-20260910`) completed consolidated Tencent-client live acceptance and was explicitly approved for production closeout.

## Accepted candidate

- Accepted PR head: `cf6a69f136e36c79abfc4279178f7e7fe7c2aebb`
- FACM UI Text Contract #1049: PASS
- FACM Mayhem Source Probe #723: PASS
- FACM Windows Build #1941: PASS
- Tencent-client consolidated live acceptance: PASS
- Final real-machine follow-ups included the embedded Presence and Efficiency vertical-scroll fixes.

## Merge

- PR #283 was marked ready after live acceptance.
- Merge commit on `main`: `78ab756abcfdc2c5ce6fae661dc15c03085b2195`

## Accepted scope

The accepted release includes the FACM LOL workspace modernization, transient Champion Select Runtime Companion, grouped rune/spell/item/build presentation, draft-aware counters and team context, ARAM/Mayhem/Bench support, guarded apply/import/leave actions, matchmaking/post-game automation, global hotkeys, Presence/signature/display-rank controls, profile/background/regalia/banner/token/emote operations, multi-monitor/DPI handling, and the Hub scrolling regressions fixed during live acceptance.

## Deliberate future scope

The following audited items are not release blockers and remain separate future work:

1. login-time automatic signature/display-rank reapply, pending a shared chat-ready lifecycle;
2. queue-ID lobby creation, requiring its own mutation fence and reconciliation;
3. arbitrary cross-source game-ID preview, requiring an SGP/LCU ownership design rather than a reduced LCU-only clone.

## Production release

Production publication is authorized through the repository's audited `FACM 3.5 Lightweight Release` workflow. The workflow is responsible for version stamping, release build/smokes, mandatory signing, GitHub Release publication, public-byte/signature verification, and only then enabling `online/version.json`.