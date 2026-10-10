# GGman — ESC setting backup and registered-account cloud sync

**Stage:** implementation branch only; not deployed or released. Depends on AUTH-1 PR #314 and CloudBase production migration acceptance (Issue #313).

## Purpose and data boundary

ESC means the League of Legends settings accessed through the in-game Escape menu. This feature handles only allowlisted files from the selected League installation's `Config` directory: `PersistedSettings.json`, `game.cfg`, and `input.ini`. These are configuration files, not GGman preferences. Tencent/Riot may overwrite some settings when the game restarts or the current game account changes; real-client testing is mandatory.

This flow is **opt-in**, separate from the existing device-anonymous `ggman_settings_sync`, user history, usage telemetry and rankings.

## Local operation

- Locate a League `Config` directory. Reject linked target files or a linked config directory.
- Back up each existing nonempty allowlisted file with base64, SHA-256, timestamp and schema version. Cap each source file at 128 KiB, total raw bytes at 192 KiB and serialized backup JSON at 400,000 characters.
- Local backup is written under `data/esc-backups`; local restore uses an explicitly chosen backup file.
- Restore requires both League game and client processes to be closed. Validate exact filenames, duplicates, sizes, hashes, JSON schema. Show filenames in confirmation, save pre-restore local snapshot, then write via temporary files and `File.Replace`/move; attempt rollback if any file fails. Never claim guaranteed preservation if Windows fails partway through an emergency rollback.

## Cloud operation

- Account login is **optional** for GGman but **required** for ESC cloud buttons. `GgmanEscCloudClient` uses `GgmanAccountSession`'s registered bearer and `sub`; it NEVER calls anonymous signin or reuses the anonymous `CloudBaseClient`.
- Upload always captures current config, writes a local backup, reads the current cloud revision, previews the replace operation and requires explicit consent. Versioned optimistic concurrency ensures parallel uploads do not silently overwrite each other. A detected server-side revision mismatch fails, requiring a fresh read and user confirmation.
- Cloud restore reads the current registered account's snapshot and shows remote revision, modification time and filenames. It requires game/client closed, explicit confirmation, a complete local backup and guarded writes.
- Requests are HTTPS only, no automatic redirects, bounded/cancellable, and never log tokens or backup contents. The app does not persist registered credentials. Session expiry requires sign-in again.

## CloudBase database contract (external action)

`cloudbase/sql/005_esc_profiles.sql` is the revised **registered-only** migration for existing environment `ggman-d4gioqqcz434d9e4d`. The old experimental migration (anonymous device owner, `GRANT ... TO anon`) must NOT be run. Migration implements:

- `ggman_esc_profiles`: one current snapshot per registered owner with an incrementing `version`, `updated_at`, and a 400 KB database-side payload limit.
- All table policies use `TO authenticated` and `owner_id = auth.uid()`; no `anon` table permissions.
- Two `SECURITY INVOKER` RPCs, `ggman_get_esc_profile()` and `ggman_set_esc_profile(p_payload jsonb, p_expected_version bigint)`. Both additionally reject nonregistered roles. The write function accepts only expected current version; conflict raises `ESC_VERSION_CONFLICT`.
- Despite SQL GRANT EXECUTE, CloudBase's PostgREST gateway may expose RPC route calls to anon; real data isolation relies on invoker/RLS and explicit role checks inside both functions.
- Database migration is NOT run or verified by GitHub CI. It needs an authorized CloudBase console operator; verify exact environment and inspect existing object schema/policies before executing. No server secrets or admin credential should be shipped to clients.

## External release gates

1. AUTH-1 registered email login and sign-out accepted, same email on two Windows users/PCs yields the same UID; another account gets a distinct UID.
2. Inspect the real PG schema. Apply revised 005 script only after confirming no conflicting pre-existing ESC table/function, or prepare an explicit migration if the old script was mistakenly applied.
3. Validate with actual bearer tokens via official CloudBase RPC: authenticated A can upload/read A; B cannot read A; anonymous JWT and unauthenticated requests are denied, including direct RPC attempts; wrong revision yields conflict; excessive payload denied.
4. Validate full closed-game backup/restore on Tencent/Riot League with different accounts as appropriate and real in-game ESC persistence. Check failure recovery, local backup integrity, no surprise login on anonymous startup, UI at Win10 100–200% display scale.
5. Final-HEAD GGman Windows Build, performance smoke and FACM UI Text Contract must pass. Only then merge reviewed branches, create a new signed 3.5.x Release, verify public GGman.exe/FACM.exe signature and checksum, and finally enable `online/version.json`.

No detached test artifact is a deliverable or substitute for the official update. If external gates fail, stop at an unmerged PR rather than publishing an unsafe production build.

References:
- https://docs.cloudbase.net/database/postgresql/data-permission
- https://docs.cloudbase.net/database/postgresql/rpc
- https://docs.cloudbase.net/authentication-v2/auth/auth-pg
