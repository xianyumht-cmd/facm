# GGman — League ESC settings backup and registered cloud synchronization

**Current signed release: GGman 3.5.67.** The in-game ESC configuration category participates in default-on background synchronization after a registered GGman email login; ordinary manual **cloud** upload/restore buttons were removed. Manual **local** backup/restore, re-detect and path selection remain available from **LOL 工作台 → 我的 GGman**. See `docs/CLOUD-SETTINGS-SYNC.md` for shared synchronization rules.

## Source and safety boundary

Only these game configuration files under the selected League installation's `Game/Config` are allowlisted: `PersistedSettings.json`, `game.cfg`, and `input.ini`. Launcher-side `LeagueClient/Config` YAML is not an ESC source and is **not** synchronized. WeGame path discovery prefers `Game/Config` containing at least one real ESC file; `LeagueClient.exe` alone is insufficient. Discovery can use a running game process (including WMI fallback), configured install path and uninstall-registry hints. No unrestricted drive scan is performed.

Local backups are saved under `data/esc-backups`. Capture validates actual files and records base64 contents, SHA-256, timestamps and schema version. The implementation bounds each raw file at 128 KiB, aggregate raw data at 192 KiB, and serialized backup JSON at 400,000 characters. Restore validates filename, duplication, size, digest and schema before writing, and creates a pre-restore local backup; writes use temporary file replacement/moves and best-effort rollback. Windows file locks or denied permissions can still fail. A running League client may keep in-memory preferences or overwrite disk bytes on exit, so in-game persistence is not guaranteed by automated tests.

## Registered cloud behavior in 3.5.67

- `GgmanAutoSyncService` runs independent of the page. The local **自动同步配置** checkbox defaults on; cloud operations wait for an authenticated email session held in process memory. Restart requires signing in again.
- `GgmanEscCloudClient` uses the registered bearer from `GgmanAccountSession`, never anonymous CloudBase identity. The per-UID cloud row is versioned with compare-and-swap; unchanged content is a no-op.
- A local-only edit may upload, and a remote-only revision may restore with local safeguards. First association to incompatible customization, simultaneous edits or same-device A/B account switching triggers a pause and exceptional **保留本机配置 / 采用云端配置** choice. Never overwrite silently.
- ESC synchronization skips an unresolved game directory rather than deleting an existing cloud backup. Snapshot creation timestamps do not affect content fingerprints. Local backup/restore does not require an email login.
- Turning off auto-sync or logging out stops new work best-effort; an already-issued HTTPS request may still finish. ESC remains independent of software preference and UI-text categories.

## CloudBase contract and security

`cloudbase/sql/005_esc_profiles.sql` is the already deployed registered-only schema; **do not rerun** its create-once migration. `ggman_esc_profiles` has one current snapshot and version per registered owner. The `SECURITY INVOKER` RPCs `ggman_get_esc_profile()` and `ggman_set_esc_profile(p_payload jsonb, p_expected_version bigint)` enforce authenticated ownership with `auth.uid()` and RLS. Stale writes must fail with `ESC_VERSION_CONFLICT`; oversized backups are rejected. Clients must not supply or override the database owner ID. The operator already checked role grants, RLS, simulated A/B isolation, and unauthenticated/anonymous gateway rejections; these are not proof of real two-registered-account isolation.

The existing **检查云端权限** action is a read-only diagnostic: after email sign-in it fetches this user's remote revision and checks rejection of a second request without an Authorization header. It cannot certify the full real A/B isolation matrix or file restoration.

## History and field acceptance

- **3.5.61:** registered ESC cloud storage and local backup/restore first shipped, originally with manual cloud controls.
- **3.5.62:** the feature moved into My GGman, retaining local operations.
- **3.5.63:** WeGame `LeagueClient/Config` versus `Game/Config` source-path correction shipped.
- **3.5.66:** three registered cloud categories were consolidated behind one manual upload/restore pair.
- **3.5.67:** the manual cloud pair was replaced by the default-checked background service; no new SQL migration.

Still requires real Windows/League acceptance: two different registered users and same UID on two devices; concurrent CAS conflict; game-running backup/restore, client writes on exit, and in-game ESC persistence; invalid paths, file-lock failure/rollback, network backoff, and disabling sync mid-request. Do not mark these as verified solely because the Windows CI, role-simulated SQL checks and signed publication succeeded. If a defect is reproduced, ship a separately signed new version; never replace the bytes of an existing Release tag.

References: [`docs/OPERATIONS.md`](OPERATIONS.md), [`docs/PROJECT_STATE.md`](PROJECT_STATE.md), [CloudBase PostgreSQL permissions](https://docs.cloudbase.net/database/postgresql/data-permission).
