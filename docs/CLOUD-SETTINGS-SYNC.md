# GGman Cloud Settings Sync

## Scope

GGman can keep a portable subset of user preferences in CloudBase, keyed by the existing anonymous device identity.

The sync payload excludes machine-local state:

- game installation paths
- floating-ball coordinates
- League Companion coordinates
- announcement bookkeeping
- account identifiers, PUUIDs, passwords and LCU credentials

It includes user preferences such as theme, desktop-pet choice, automation toggles, hotkeys, matchmaking delays and personal-stats preferences.

## Sync policy

- The existing CloudBase anonymous session is reused. No second authentication path is introduced.
- Sync starts after the shell is visible, so CloudBase failure cannot block application startup.
- If no cloud settings exist, the current local settings are uploaded.
- If the local settings file was created during the current run, existing cloud settings are restored locally.
- Otherwise the newer side wins using the local settings file write time and the server `updated_at` timestamp.
- A mismatch where local settings are newer uploads the local snapshot.
- A cloud restore writes the snapshot through the existing atomic settings path.
- Network and CloudBase failures remain fail-soft and are logged without surfacing a blocking dialog.

## Database contract

Migration: `cloudbase/sql/003_settings_sync.sql`

Table: `public.ggman_settings_sync`

- `owner_id`: anonymous CloudBase subject, stored as `TEXT` to match the existing GGman ownership contract; it is intentionally not a foreign key to `auth.users(id)`
- `settings_json`: portable settings snapshot
- `updated_at`: server write timestamp

RLS policies compare `owner_id` with the authenticated subject as text. This keeps the settings table compatible with the existing `ggman_devices` / personal-stats schema instead of coupling it to the internal type of `auth.users.id`.

RPCs:

- `ggman_get_settings_sync()` returns the current caller's snapshot and server timestamp.
- `ggman_set_settings_sync(p_settings jsonb)` replaces the caller's snapshot and returns the new timestamp.

Both functions use `SECURITY INVOKER`; row-level security remains the data boundary.

## Compatibility

The sync payload is an explicit allowlist rather than a serialization of the entire local settings file. New settings must be deliberately classified as portable before they are added to the cloud contract.

The feature does not require a second database table for local settings. Existing local settings and recovery files remain authoritative for the current machine.
