# FACM Decisions

This file records current product decisions. Historical implementation detail belongs in Git history and the 3.5.19 backport audit.

## D-001 — 3.5.x is the only maintained product line

**Decision:** keep WinForms/.NET Framework 4.8/single-EXE as the canonical FACM product.

4.x is retired from the default working tree. Do not reintroduce WinUI/Morphing Surface, FACM.App/Core/Infrastructure/Platform.Windows, native bootstrapper, CAB or multi-version runtime unless a future requirement is independently justified.

## D-002 — Backport behavior, not architecture

Useful 4.x lessons are allowed when they solve a concrete 3.5 problem with a small implementation: immediate automation evaluation, one state owner, cancellation, generation/fingerprint fences, postcondition reconciliation and reason-owned visibility.

Do not transplant architecture solely because it is newer.

## D-003 — Preserve the 3.5 Mayhem data path

The 3.5 Mayhem/海符 path is fast and remains canonical. Fix display units, UI completeness, cancellation or concrete defects without replacing its service/cache/network stack.

## D-004 — One Gameflow owner

League phase/activity observation has one process-wide owner. Automation and UI subscribe to that state instead of creating parallel phase loops.

Human-visible response speed comes from reacting immediately to observed state and removing unnecessary sleeps, not from multiplying pollers.

## D-005 — Writes must be deduplicated and reconciled

Matchmaking and ReadyCheck automation are best-effort writes with explicit ownership:

- successful writes commit their fence;
- true failures can retry within the existing phase/episode;
- ambiguous writes read authoritative local state before retrying;
- normal success paths do not pay extra reconciliation network cost.

## D-006 — In-game hiding is non-destructive

Entering InGame hides the shell/pet but does not stop the pet runtime. Gameflow restores only visibility it owns. User-explicit actions remain allowed.

## D-007 — Lightweight PetHost contract

`FACM.PetHost` source stays because VPet compatibility is useful, but normal 3.5 publishing builds/self-tests it separately and does not embed the self-contained bundle into FACM.exe.

A stale local `out/PetHostBundle.zip` must never change the ordinary build output implicitly.

## D-008 — One canonical publisher

The only current release workflow is `.github/workflows/publish-3.5-lightweight.yml` (**FACM 3.5 Lightweight Release**). Its file-driven request is `release/3.5-request.json`.

The old heavyweight publisher and `release/request.json` are retired.

## D-009 — Normal 3.5 updater only

FACM downloads a trusted 3.5 Release EXE, validates it, then uses the embedded small updater for atomic replacement/rollback/restart. 4.x bootstrapper/migration mode is retired.

## D-010 — Git history is not the working tree

Removing 4.x means removing it from the current working tree and current CI/release surface. Do not rewrite Git history. Old releases/tags/remote branches are separate destructive-history cleanup and require a separate explicit decision.

## D-011 — Rebrand later and narrowly

Future public brand target may be GGman（鸡鸡侠）, but do not globally replace `FACM` identifiers. First inventory update URLs, namespaces, assembly/resource names, mutex/config paths and compatibility contracts. User-facing naming can change before internals.

## D-012 — 3.5.20 is the first post-cleanup release

3.5.20 was published after P1 and the 4.x working-tree cleanup reached `main`. It is a new lightweight Release rather than mutated/reused 3.5.19 bytes. Future releases must use a new 3.5.x patch version and keep the online manifest migration-free.
