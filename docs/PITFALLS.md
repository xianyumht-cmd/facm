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

## Do not add arbitrary first-action sleeps

Lobby/ReadyCheck previously felt slower because of fixed initial delays. If an endpoint may lag behind Gameflow, use the phase-bounded observer/retry path rather than sleeping before every first attempt.

## Do not mark writes successful before they are known to be successful

A stable-Lobby fingerprint or ReadyCheck episode fence must protect against duplicate concurrent writes, but a true failure still needs a recovery path.

For ambiguous matchmaking writes, reconcile authoritative queue state before retrying. For ReadyCheck, reconcile final local response before repeating an accept.

## Do not treat HTTP 2xx as the only postcondition where the write can outlive the response

LCU can apply a request and the client can still see a timeout/reset. Blind retry then duplicates the action. Use postcondition reads only on failure/ambiguity so the normal success path remains fast.

## Do not restore UI visibility you did not hide

Gameflow suppression owns only its own restore. If the user had already hidden the desktop entry, leaving the game must not force it visible.

The first InGame transition may still close a transient control center; repeated InGame heartbeats must not repeatedly close a control center the user explicitly reopened.

## PetHost active is not the same as visible

PetHost may be alive while hidden. Desired visibility must survive startup races. Do not stop/restart the process merely to hide it during a match.

## Mayhem percentage values are percentage points

A value such as `53.5` means `53.5%`, not `5350%`. Do not multiply by 100 in the display layer.

## Preserve the fast Mayhem path

The current cache/network/service path is intentionally retained. UI fixes should not trigger a rewrite or extra normal-path network requests unless a concrete functional defect requires it.

## Owner-drawn UI must repaint deterministically

Transparent/low-alpha idle backgrounds can leave stale text pixels after state changes. Idle owner-draw backgrounds should cover prior content deterministically.

For compact windows, apply final geometry before the first `Show()`; showing a full historical size and cropping afterward can leave desktop compositor artifacts.

## Updater migration residue is not needed

Current 3.5 updates use the embedded small updater for one-EXE replacement/rollback. Do not add back FACM 4 bootstrapper/manifest/migration arguments to solve ordinary 3.5 update issues.

## Online JSON is a compatibility surface

Clients may encounter older JSON with unknown properties. Removing a retired model property is safe when the serializer ignores unknown properties, but do not silently rename/remove fields still consumed by supported clients.

## Clean Git history separately

Removing files from `main` does not remove them from old commits, releases, tags or remote branches. History rewriting is a separate, higher-risk operation. Do not mix it into ordinary cleanup.
