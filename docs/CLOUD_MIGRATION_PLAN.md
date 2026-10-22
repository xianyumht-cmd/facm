# GGman CloudBase → Workers + D1 migration plan (2026-10-22)

## CloudBase source inventory from repository SQL

- 003_settings_sync: anonymous device-owned settings. Never silently transfer between users.
- 005_esc_profiles: registered ESC snapshot, revision/CAS.
- 006_registered_personal_stats: registered preferences, account history, active days, legacy import markers.
- 007_account_ui_text: registered text profile, revision/CAS, maximum 184320 bytes.
- 008_registered_app_settings: registered software preferences, revision/CAS, maximum 32768 bytes.
- 009_manual_game_profiles: registered intentional lobby + ESC publication, revision/CAS, maximum 510000 bytes.
- CloudBase email identity is provider-managed; pg_dump alone does NOT preserve login identity or account-recovery mechanisms.

## Ordered work

1. Export CloudBase production through authorized console/pg_dump. Capture SHA256, table inventory, identity-provider inventory, and verified isolated restore. Do not import raw sensitive data into git or chat.
2. Build a separate Workers+D1 lab with synthetic identities/data, explicit bootstrap, server-verified ownership and CAS. Source: experiments/cloud-d1-lab. Cloudflare Access is restricted laboratory gate only, NOT general user auth.
3. With the owner-provided account connection and explicit deployment approval, create a private test D1 and observe actual requests, D1 rows read/written, errors and size over multiple days.
4. Confirm current user mapping, recovery, read/write A/B separation, two-device cloud-first download, safe unsaved edits, manual ESC/YAML upload, backup and rollback.
5. Only after all checks, decide if moving the GGman official backend away from CloudBase is justified. No merge/deploy/release on source test alone.

## Current verification status

- CloudBase table/RPC structure: source-verified.
- Lab API and backup utility: drafted on dedicated task branch, awaiting CI.
- Production backup completed: NO. No CloudBase admin connection is available in this session.
- D1 created, Access configured, Worker deployed: NO. No Cloudflare account connection is available.
- Real A/B user identity, production metrics, restore or update switch: NOT RUN.
- Official GGman still runs CloudBase unchanged.

Sources:
- https://developers.cloudflare.com/workers/platform/limits/
- https://developers.cloudflare.com/workers/platform/pricing/
- https://developers.cloudflare.com/changelog/product/d1/
- https://docs.cloudbase.net/en/database/postgresql/faq
