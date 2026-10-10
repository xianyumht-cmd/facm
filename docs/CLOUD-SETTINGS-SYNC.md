# GGman Configuration Synchronization

## Current behavior (3.5.67)

**Registered GGman email accounts**, not the anonymous device identity, own current cloud configuration synchronization. The UI in **LOL 工作台 → 我的 GGman** shows one device-local **自动同步配置** checkbox, enabled by default. Once the email session is verified, `GgmanAutoSyncModule` runs `GgmanAutoSyncService` in the background independently of that page. Ordinary manual cloud upload/restore buttons were removed in 3.5.67.

The three independent, versioned CloudBase categories are:

- Portable GGman preferences: `GgmanPortableSettingsStore` and `GgmanAppSettingsCloudClient`, backed by deployed `cloudbase/sql/008_registered_app_settings.sql`.
- Custom `ui-text.ini` text/replacement overrides: `UiTextCustomizationStore` and `GgmanUiTextCloudClient`, backed by deployed SQL `007_account_ui_text.sql`.
- Allowlisted in-game LOL ESC files under `Game/Config`: `EscSettingsBackup` and `GgmanEscCloudClient`, backed by deployed SQL `005_esc_profiles.sql`.

All categories use the verified registered UID, their own cloud revision and compare-and-swap updates. `data/account-auto-sync-state.json` stores per-category revision/content hashes and hashed owner identity; it does not hold raw configuration, passwords or account tokens. No new SQL migration was required for 3.5.67. **Do not rerun create-once SQL 005/007/008** against an already-deployed database.

## Synchronization and safety

- Content unchanged: no upload/restore. Only local changed: CAS upload. Only remote changed: restore, with the existing local backup safeguards.
- First link over known default portable preferences or empty UI-text overrides may accept a remote profile. Existing local customizations, concurrent edits and cross-account ownership changes must not be silently overwritten.
- An exceptional conflict pauses only its category and exposes **保留本机配置 / 采用云端配置**. User choices are not ordinary manual sync actions.
- Missing game directories skip ESC; unsaved visible text edits are guarded. The Game/Config fingerprint omits snapshot creation timestamps.
- Network failures back off without blocking local League or desktop features. Disabling the checkbox or logging out cancels future activity best-effort, but already-sent HTTP cannot be unsent.
- Device-specific paths, window/desktop positions, identities, raw PUUIDs, authentication credentials, ranking preferences and telemetry consent are not part of portable settings. `LeagueClient/Config` lobby YAML is excluded pending a safe allowlist and write-back validation.
- Registered authentication is currently process-memory-only: after restarting GGman, users must verify their email again. The automatic-sync checkbox preference is stored locally and is never synced between devices.

## Historical anonymous settings synchronization (superseded)

Versions before the registered-account consolidation included an anonymous-device settings-sync contract implemented with `cloudbase/sql/003_settings_sync.sql`, `ggman_settings_sync` and `ggman_get_settings_sync` / `ggman_set_settings_sync`. That older flow compared local file modification time with server `updated_at`. **It is not the current portable-preferences restore/upload path**; anonymous software-settings auto-restore was disabled in 3.5.66 to prevent it from overriding registered-account data. Existing anonymous device records and separately consented telemetry are not deleted or re-owned.

## Release evidence and pending field acceptance

- Signed official release: [`v3.5.67`](https://github.com/xianyumht-cmd/facm/releases/tag/v3.5.67), enabled through `online/version.json`; GGman and compatibility FACM assets have the same SHA-256.
- Source and CI decision/smoke tests cover the default-on worker, registered category boundary, content fingerprint/checkpoint logic and conflict policy.
- **Still unverified on real user devices:** normal first email login, A/B account isolation on one PC, same UID across two PCs, cross-device conflict resolution, offline/retry and actual LOL ESC restore while the game may write its own settings. Do not claim those as accepted based on CI alone.

See `docs/PROJECT_STATE.md` for immutable release evidence and `docs/OPERATIONS.md` for the field acceptance checklist.
