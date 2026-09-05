# FACM Operations

## Normal development flow

1. Work from current `main` or a focused branch.
2. Keep changes inside the 3.5.x lightweight architecture unless a concrete requirement proves otherwise.
3. Run/observe **FACM Windows Build** and **FACM UI Text Contract**.
4. For League/Mayhem/UI changes, use the relevant smoke tests and a real Windows/League check when behavior cannot be proven in CI.
5. Merge only after the branch is green.

## Local release build

Windows requirements: Visual Studio 2022 Build Tools or Visual Studio 2022, .NET Framework 4.8 targeting pack and .NET 8 SDK.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

The script must keep the lightweight contract:

- validate tool inputs;
- build/self-test `FACM.PetHost` separately;
- remove/ignore stale `out/PetHostBundle.zip`;
- build `FACM.sln` with CI smoke tests;
- verify ToolBundle is embedded;
- verify `FACM.Resources.PetHost.zip` is absent;
- verify FACM.exe <10 MiB.

## GitHub build artifact

Use **Actions → FACM Windows Build → Run workflow** when a fresh candidate is needed. A successful run uploads `FACM-Windows-x64-<run-number>` containing the lightweight executable/package metadata.

Do not treat an artifact as a public release until the release workflow updates the GitHub Release and `online/version.json`.

## Publish a new 3.5.x version

Canonical workflow: **FACM 3.5 Lightweight Release** (`.github/workflows/publish-3.5-lightweight.yml`).

Two supported entry points:

### Manual

Actions → FACM 3.5 Lightweight Release → Run workflow, then supply:

- new 3.5.x `version`;
- `minimum_version`;
- `force_update`;
- `prerelease`;
- `release_notes`.

### Audited request file

Edit `release/3.5-request.json` on `main` using the exact current schema:

```json
{
  "version": "3.5.21",
  "minimum_version": "3.0.0",
  "force_update": false,
  "prerelease": false,
  "release_notes": "FACM 3.5.21 lightweight update."
}
```

A push touching that file triggers the same publisher. The workflow rejects an already-existing release tag; never reuse an old version number to publish new bytes.

The publisher freezes `main`, builds and signs the candidate, first writes an `enabled=false` manifest, publishes the GitHub Release, downloads the public asset again to verify size/SHA-256/signer, then enables the online manifest. The current manifest schema is migration-free.

After publishing, verify:

1. GitHub Release `vX.Y.Z` exists and contains `FACM.exe`.
2. Release asset SHA-256 matches `online/version.json`.
3. `online/version.json.enabled=true` and points to the exact Release asset.
4. `minimum_version` and `force_update` are intentional.
5. `online/version.json` still contains no 4.x migration object.
6. A currently supported client can check/download the update.

## Online manifest safety

`online/version.json` is the client-facing release pointer. Do not point it at CI artifacts, branch files or an old tag with different bytes.

The current updater accepts approved HTTPS release URLs, validates SHA-256 and package identity, then performs atomic replacement using `FACM.Updater.exe`. 4.x migration/bootstrapper fields are no longer part of the runtime contract or publisher output.

## Announcements and mirrors

- Announcements: `online/announcement.json` and **FACM Online Management**.
- Mirror pool: `online/mirrors.json`.
- GitHub Release remains the canonical artifact source; mirrors are transport accelerators, not a release trust source.

## League regression checklist

For automation changes verify at least:

- Lobby auto-search reacts without an artificial first delay.
- one stable Lobby does not generate duplicate search POSTs.
- a true failed search can retry; an already-applied ambiguous write does not duplicate after queue-state reconciliation.
- ReadyCheck reacts immediately; a true failure can retry; final Accepted/Declined state stops writes.
- no secondary Gameflow poll loop was introduced.

For desktop visibility changes verify InGame hide and post-game ownership restore for default ball, sprite/VPet and user-manually-hidden states.

For Mayhem changes verify percentage units, full content and load speed; do not casually change the service/cache/network path.

## Rollback / incident response

If a new release has a blocking defect:

1. stop further publishing;
2. do not mutate an existing Release asset in place;
3. prepare a new patch version from the last known-good source plus the fix;
4. use `force_update` only when the impact justifies it;
5. keep evidence/logs needed to reproduce the issue.

If CI breaks after cleanup, first check for references to removed 4.x projects/scripts/workflows rather than restoring the entire 4.x tree.

## Repository hygiene

Current worktree should not contain FACM4 solution/projects, 4.x migration/bootstrapper code, 4.x-only workflows, CAB/BOOT release tooling or the old heavyweight publisher. Git history remains intact.
