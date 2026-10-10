# GGman — ESC setting backup and registered-account cloud sync

**Status:** ESC cloud feature initially shipped in 3.5.61 and WeGame folder recognition was corrected in signed official GGman 3.5.63, one-click online update enabled (2026-10-10). The operator installed the ESC table/RPCs and validated catalog RLS and role permissions. A/B mock-role SQL isolation passed; real production HTTP probes with no credentials and with an anonymous bearer both returned 401. Actual *two registered accounts* HTTP isolation, simultaneous version conflicts, and Tencent/Riot game settings restoration remain post-release field checks, not proven by CI.

## Purpose and data boundary

ESC means the League of Legends settings accessed through the in-game Escape menu. This feature handles only allowlisted files from the selected League installation's `Config` directory: `PersistedSettings.json`, `game.cfg`, and `input.ini`. These are configuration files, not GGman preferences. Tencent/Riot may overwrite some settings when the game restarts or the current game account changes; real-client testing is mandatory.

This flow is **opt-in**, separate from the existing device-anonymous `ggman_settings_sync`, user history, usage telemetry and rankings.

**Current 3.5.62 UX:** this feature is now available as an inline subsection of **LOL 工作台 → 我的 GGman** rather than a standalone Settings/tray dialog. The location detector runs when the section opens and has no background scanner. The signed 3.5.62 online update is enabled. Registered-account ownership and CloudBase database schema remain unchanged. Config files generally can be accessed with the game running, but newly restored bytes may not be reflected in memory and may be overwritten by a later client save.

**WeGame folder correction (released in signed GGman 3.5.63):** WeGame's `LeagueClient\\Config` YAML client/account preferences are distinct from in-game ESC files. The auto-locator prioritizes `Game\\Config` containing `game.cfg`, `input.ini`, or `PersistedSettings.json`. The presence of `LeagueClient.exe` alone is no longer sufficient. Both the locator and backup/restore path resolver handle launcher executable hints, installed root and stale `LeagueClient\\Config` hints consistently; supported cloud JSON payload and owner/RLS rules remain unchanged. On the owner's machine 3.5.62 incorrectly chose the client YAML folder and failed cloud upload; do not treat that failed attempt as a saved cloud backup.

## Local operation

- Resolve a League `Config` directory automatically when entering **LOL 工作台 → 我的 GGman**: first a running League process executable (MainModule or WMI fallback), then the saved GGman game path, then League-specific uninstall registry entries. Require valid game/install markers and a real Config folder; do not broadly scan all drives. Users can re-detect or select a directory manually. Reject linked target files or a linked config directory.
- Back up each existing nonempty allowlisted file with base64, SHA-256, timestamp and schema version. Cap each source file at 128 KiB, total raw bytes at 192 KiB and serialized backup JSON at 400,000 characters.
- Local backup is written under `data/esc-backups`; local restore uses an explicitly chosen backup file.
- Restore does **not** require the League game or client to close. Validate exact filenames, duplicates, sizes, hashes, JSON schema. Show filenames in confirmation, save pre-restore local snapshot, then write via temporary files and `File.Replace`/move; attempt rollback if any file fails. Never claim guaranteed preservation if Windows fails partway through an emergency rollback.

## Cloud operation

- Account login is **optional** for GGman but **required** for ESC cloud buttons. `GgmanEscCloudClient` uses `GgmanAccountSession`'s registered bearer and `sub`; it NEVER calls anonymous signin or reuses the anonymous `CloudBaseClient`.
- Upload always captures current config, writes a local backup, reads the current cloud revision, previews the replace operation and requires explicit consent. Versioned optimistic concurrency ensures parallel uploads do not silently overwrite each other. A detected server-side revision mismatch fails, requiring a fresh read and user confirmation.
- Cloud restore reads the current registered account's snapshot and shows remote revision, modification time and filenames. It permits a running game, and still requires explicit confirmation, a complete local backup and guarded writes. Windows file locks or denied permissions may cause a legitimate failure; the game can also overwrite the changed file when it exits.
- Requests are HTTPS only, no automatic redirects, bounded/cancellable, and never log tokens or backup contents. The app does not persist registered credentials. Session expiry requires sign-in again.

## CloudBase database contract (external action)

`cloudbase/sql/005_esc_profiles.sql` is the revised **registered-only** migration for existing environment `ggman-d4gioqqcz434d9e4d`. The old experimental migration (anonymous device owner, `GRANT ... TO anon`) must NOT be run. Migration implements:

- `ggman_esc_profiles`: one current snapshot per registered owner with an incrementing `version`, `updated_at`, and a 400 KB database-side payload limit.
- All table policies use `TO authenticated` and `owner_id = auth.uid()`; no `anon` table permissions.
- Two `SECURITY INVOKER` RPCs, `ggman_get_esc_profile()` and `ggman_set_esc_profile(p_payload jsonb, p_expected_version bigint)`. Both additionally reject nonregistered roles. The write function accepts only expected current version; conflict raises `ESC_VERSION_CONFLICT`.
- Despite SQL GRANT EXECUTE, CloudBase's PostgREST gateway may expose RPC route calls to anon; real data isolation relies on invoker/RLS and explicit role checks inside both functions.
- Database migration cannot be run or verified by GitHub CI. The operator's screenshots indicate the objects are now installed in the selected CloudBase database, but **do not prove** direct RPC authorization, data ownership under multiple real tokens or correct Riot recovery. Audit execute grants and policies from the CloudBase SQL Editor, then test actual A/B/anon gateway requests without exposing credentials in chats/logs. No server secrets or admin credential may be shipped to clients.

## Read-only real-gateway diagnostic (shipped in 3.5.61)

The embedded ESC area under **LOL 工作台 → 我的 GGman** includes **检查云端权限**. After normal registered email sign-in, this action sends one authenticated `ggman_get_esc_profile()` request and displays only the remote revision (or absence); then sends a distinct read request without an Authorization header and requires HTTP 401/403. No token, UID, email or raw payload is displayed or logged, and this check never performs SQL writes or modifies League files. This checks authenticated read + **no-credential** denial; it is **not** a replacement for a real anonymous bearer-token check, a second-account A/B ownership check, upload revision conflict or in-game restore acceptance. A 400/404/5xx or a surprising 200 on the unauthenticated leg fails closed for diagnosis.

## Outstanding post-release field acceptance

1. Owner verified real email OTP sign-in on Windows; still verify real sign-out, same registered UID across two devices, and separate accounts getting different UIDs.
2. Operator installed the revised registered-only 005 schema and verified table/RPC grants plus RLS. Preserve these settings; do not re-run the strict new-table migration on an existing database.
3. Validate with actual bearer tokens via official CloudBase RPC: authenticated A can upload/read A; B cannot read A; anonymous JWT and unauthenticated requests are denied, including direct RPC attempts; wrong revision yields conflict; excessive payload denied.
4. Validate actual running-game file access and open-game backup/restore on Tencent/Riot League with different accounts as appropriate, plus whether the client overwrites disk changes on exit and real in-game ESC persistence. Check failure recovery, local backup integrity, no surprise login on anonymous startup, UI at Win10 100–200% display scale.
5. Final-HEAD GGman Windows Build, performance smoke and FACM UI Text Contract must pass. Only then merge reviewed branches, create a new signed 3.5.x Release, verify public GGman.exe/FACM.exe signature and checksum, and finally enable `online/version.json`.

The official signed 3.5.61 release is live. If later acceptance detects a serious ownership/restore defect, disable affected operations through a reviewed fix and issue a new signed patch; never replace an existing Release asset in place. No detached test artifact is a substitute for the official update.

References:
- https://docs.cloudbase.net/database/postgresql/data-permission
- https://docs.cloudbase.net/database/postgresql/rpc
- https://docs.cloudbase.net/authentication-v2/auth/auth-pg
