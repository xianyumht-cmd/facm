# FACM Pitfalls

## Do not confuse 4.x history with the current product

FACM 4.x is retired from the default working tree. A bug, constraint or build rule that existed only in 4.x must not be described as a 3.5 bug. If old Git history is consulted, first verify the behavior exists in current `src/FACM`.

## Do not reintroduce heavyweight PetHost embedding accidentally

Normal 3.5 builds must not embed `FACM.Resources.PetHost.zip`. A stale `out/PetHostBundle.zip` is not a reason to change output. `FACM.PetHost` is built/self-tested separately.

If FACM.exe suddenly grows dramatically, inspect `IncludePetHostBundle`/`RequirePetHostBundle`, embedded resources and release scripts before changing product architecture.

## Do not revive the old publisher

The current publisher is `publish-3.5-lightweight.yml` and the file request is `release/3.5-request.json`. The retired `publish-release.yml`/`release/request.json` path produced a conflicting heavyweight package.

Never reuse an existing version tag for new release bytes.

## Do not multiply Gameflow polling loops

League automation, UI and presence should consume the shared monitor. Adding a “faster” second timer can create races, duplicate requests and higher LCU load.

Improve perceived latency by reacting immediately when a phase is observed and by using the correct shared cadence.

## Do not turn configurable delay into an unbounded sleep

Akari-style auto-match/auto-accept delays belong inside the existing phase-bounded matchmaking controller. A Lobby start delay must be cancelled as soon as Lobby ownership/settings change; an accept delay must be cancelled as soon as ReadyCheck/settings change. Never implement these options with a detached background timer or a second Gameflow observer.

The default remains zero delay. User values are clamped in shared `AppSettings` (minimum party 1-5, matchmaking wait 0-60 seconds, accept wait 0-15 seconds), and changing them reuses the same episode fences so a settings toggle cannot duplicate an ambiguous write.

## Keep compact numeric-unit copy localized

Small automation controls still participate in the UI text contract. Do not hard-code unit suffixes such as Chinese `人` / `秒` in a Form merely because they are one character. Put the complete localized label in the feature text catalog (for example `自动排队最低人数（人）` or `接受对局延迟（秒）`) so custom language catalogs and the UI copy gate stay authoritative.

## Do not add arbitrary first-action sleeps

Lobby/ReadyCheck previously felt slower because of fixed initial delays. If an endpoint may lag behind Gameflow, use the phase-bounded observer/retry path rather than sleeping before every first attempt. The optional Akari-style delays are explicit user policy; they must not become hidden mandatory latency.

## Do not mark writes successful before they are known to be successful

A stable-Lobby fingerprint or ReadyCheck episode fence must protect against duplicate concurrent writes, but a true failure still needs a recovery path.

For ambiguous matchmaking writes, reconcile authoritative queue state before retrying. For ReadyCheck, reconcile final local response before repeating an accept.

## Do not treat HTTP 2xx as the only postcondition where the write can outlive the response

LCU can apply a request and the client can still see a timeout/reset. Blind retry then duplicates the action. Use postcondition reads only on failure/ambiguity so the normal success path remains fast.

## Do not confuse “close lobby” with “leave current Champion Select”

The legacy `close-lobby` efficiency action terminates LeagueClient/LeagueClientUx processes. It is not a safe implementation of “退出当前选人但保留大厅/队伍”.

Runtime Companion uses a dedicated writer that can issue only `POST /lol-lobby-team-builder/champ-select/v1/session/quit`. The transaction must preflight live ChampSelect and read back both “phase left ChampSelect” and “`/lol-lobby/v2/lobby` still exists” before reporting success. Never fall back to killing the client or `DELETE /lol-lobby/v2/lobby` when this route fails; League/Tencent dodge penalties also remain outside FACM ownership.

## Do not restore UI visibility you did not hide

Gameflow suppression owns only its own restore. If the user had already hidden the desktop entry, leaving the game must not force it visible.

The first InGame transition may still close a transient control center; repeated InGame heartbeats must not repeatedly close a control center the user explicitly reopened.

## PetHost active is not the same as visible

PetHost may be alive while hidden. Desired visibility must survive startup races. Do not stop/restart the process merely to hide it during a match.

## Mayhem percentage values are percentage points

A value such as `53.5` means `53.5%`, not `5350%`. Do not multiply by 100 in the display layer.

## Preserve the fast Mayhem path

The current cache/network/service path is intentionally retained. UI fixes should not trigger a rewrite or extra normal-path network requests unless a concrete functional defect requires it.

## Do not enrich the same ARAM balance twice

`RiotGameDataService.EnrichAsync` already starts the bounded `OpggAramBaseBalanceService` task in parallel with visual metadata. Calling the same balance service in `MayhemAutomaticGuideService` immediately before Riot enrichment looks harmless when a complete result hits the ten-minute cache, but an unavailable result can cause a second external attempt and lengthen the visible failure path.

Keep one automatic-guide enrichment owner. The Runtime Companion should render the resulting `BaseBalanceSummary`; it must not create another balance fetch loop merely because the data has a new visible section.

## Bench availability is not proof of ARAM Mayhem

Ordinary ARAM and ARAM Mayhem can both expose Bench. Starting the full Mayhem guide whenever `benchEnabled=true` leaks Mayhem-only augments/data into normal ARAM and wastes external requests. Classify the queue first: current Runtime Companion policy recognizes base ARAM separately from global/CN Mayhem queue identifiers and mode tokens, and unknown Bench queues fail closed.

Base ARAM should wait for the matching Build Advisor version before requesting its balance-only supplement so patch mismatch remains visible instead of silently accepting stale values.

## Owner-drawn UI must repaint deterministically

Transparent/low-alpha idle backgrounds can leave stale text pixels after state changes. Idle owner-draw backgrounds should cover prior content deterministically.

For compact windows, apply final geometry before the first `Show()`; showing a full historical size and cropping afterward can leave desktop compositor artifacts.

## Per-monitor transient windows must not trust pre-Show 96-DPI geometry

FACM is PerMonitorV2-aware. A transient borderless window can have the correct logical WinForms size but a different physical size after its handle is created on a 125/150/200% display. Computing upper-right placement from the logical width before `Show()` can therefore leave a Runtime Companion clipped or restore it to the wrong place.

For the Runtime Companion, let its normal `Shown` path establish DPI-scaled geometry, then fit/clamp the physical bounds before the surface becomes visible to the user. Saved monitor coordinates may legitimately be negative when a display is left of the primary monitor; negative values are not corruption. Clamp to a current `Screen.WorkingArea` when topology changes instead of resetting to `(0,0)`.

## Runtime Companion preferences must use the shared settings owner

Do not call `AppSettings.Load()` from the transient Form and do not create a companion-specific INI/JSON file. That creates two mutable settings objects and can overwrite unrelated changes when either copy saves.

Reuse the already-initialized `SettingsModule.Settings` object and its normal atomic/LKG save path. Restore pin/collapse through the Form's existing behavior owner so `TopMost`, internal state, glyphs and tooltips cannot drift apart.

## Updater migration residue is not needed

Current 3.5 updates use the embedded small updater for one-EXE replacement/rollback. Do not add back FACM 4 bootstrapper/manifest/migration arguments to solve ordinary 3.5 update issues.

## Online JSON is a compatibility surface

Clients may encounter older JSON with unknown properties. Removing a retired model property is safe when the serializer ignores unknown properties, but do not silently rename/remove fields still consumed by supported clients.

## Clean Git history separately

Removing files from `main` does not remove them from old commits, releases, tags or remote branches. History rewriting is a separate, higher-risk operation. Do not mix it into ordinary cleanup.

## Create the task branch before the first write

Do not use a placeholder file on `main` as a way to obtain a commit for a new task branch. Resolve the current `main` SHA first, create the feature branch from that SHA/ref, and only then write task files.

If an accidental no-op content change does land on `main`, reverse it with an ordinary commit and verify the resulting tree matches the intended prior tree. Do not hide the mistake with reset, force-push or history rewriting.
