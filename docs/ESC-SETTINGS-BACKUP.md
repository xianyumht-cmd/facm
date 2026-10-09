# GGman — League ESC settings backup

## Scope and storage

This opt-in first version uses the existing Tencent CloudBase environment and its PostgreSQL RPC gateway. Cloud migration: `cloudbase/sql/005_esc_profiles.sql` (not applied automatically by a GitHub release). It adds `ggman_esc_profiles`, isolated from the existing `ggman_settings_sync` table. Its single cloud snapshot per authenticated anonymous device subject is RLS-protected and size-limited; cloud payloads contain only the three allowlisted League settings files, not local filesystem paths, CloudBase tokens, game account IDs or passwords. The existing CloudBase session obtains a token in process memory on demand.

The current CloudBase anonymous identity is **device-bound**, and this first version does **not** provide cross-device account recovery, portable device-token exporting or shared profile access. GGman must not represent the cloud button as a cross-PC login system. CloudBase free-tier database/storage/traffic use shared finite resource quotas.

## File contract

The user chooses a League of Legends installation directory or its `Config` child folder. The exact allowed files are `PersistedSettings.json`, `game.cfg`, and `input.ini`. A snapshot contains version=1, timestamp, base64 file bytes and a SHA-256 per file; it contains **no absolute file path**. Each file is at most 128 KiB, all files together at most 192 KiB, and any cloud payload is capped at 400,000 JSON characters. Unknown filenames, repetitions, reparse-point links, invalid base64, mismatched hashes and oversized snapshots fail closed. Missing files are not fabricated.

## Manual operations

- **Local backup**: GGman → Settings → ESC settings backup → pick directory → Local backup. The `data/esc-backups` directory under the GGman executable holds named JSON snapshots. The input validation and file boundary apply to both local and cloud reads.
- **Local restore**: select a backup JSON file; after the file passes integrity checks, GGman shows the exact filenames and asks for an explicit confirmation. League game + client processes must be stopped. Before any overwrite, GGman saves a local `before-restore` snapshot; per-file writes use temporary files and Windows replacement. On a write failure, restoration attempts to roll back touched files. The operator should still check the actual in-game result because Riot synchronizes some settings from the account server.
- **Cloud upload**: capture an allowed snapshot, request user confirmation to replace the one device-owned server copy, keep a local `before-upload` file, then call the new CloudBase RPC. Nothing uploads on startup or without pressing the button.
- **Cloud restore**: read and validate the device-owned snapshot, show a file preview and explicit confirmation, and use the exact local restore safety checks.
- **Failure**: network, RPC, RLS or quota failures show an error and do not modify League files. Cloud functionality is unavailable until `005_esc_profiles.sql` has been run successfully against the actual CloudBase PostgreSQL instance. `ggman_settings_sync` and existing anonymous stats are unaffected.

## Deployment & acceptance

1. In the same CloudBase environment as the existing `ggman_settings_sync`, open **SQL 编辑器** and execute `cloudbase/sql/005_esc_profiles.sql` once. Verify `ggman_esc_profiles` exists with RLS enabled, and confirm both RPC functions are accessible only to the authenticated subject through the existing gateway.
2. Check a real League install's Config directory path, snapshot all expected files and verify in-game keybind options were actually persisted after closing League. Confirm no sensitive account/session data were included.
3. Upload once and confirm one row appears, scoped to the current anonymous user. Download without restoring; inspect filenames. Then close game and client, restore from cloud, re-open League, check keybinds and client-side server overwrite behavior.
4. Test malformed/oversized snapshots, offline client, cloud RPC absent, a second anonymous device (must not fetch this device's snapshot), denied row access, and rollback on forced write failure. Test normal Windows UI at 100/125/150/200% DPI.
5. If cross-device retrieval becomes a requirement, use an explicit account binding or recovery-code design with revocation/ownership checks and a separate threat review; **do not export or repurpose CloudBase access/refresh tokens or silently share device IDs**.

Official GGman release and local smoke tests cannot alone prove CloudBase migration or real Tencent League configuration retention.
