# GGman Cloud Provider Replacement — Clean-Slate Single Owner (2026-10-22)

## Owner decision

The operator explicitly waived CloudBase backup/import, because current registered use is essentially one person. The new provider starts with an EMPTY Cloudflare D1 database. The earlier backup-first plan is superseded **for this user's fresh-start operation**. Do not delete or modify the old CloudBase environment, but previous CloudBase-only account identities, online settings, ESC/game preset snapshots, UI text and ranking/history will **not** appear in the new service automatically. Existing local desktop settings and local League files are not erased by the candidate.

## What is implemented

- `experiments/cloud-d1-lab/src/index.mjs`: both the original private Access-auth lab and an isolated `ENVIRONMENT=single_owner, AUTH_MODE=single-owner-key` mode. Production-style API paths: `GET /api/v1/whoami`, `GET/PUT /api/v1/documents/{app_settings,ui_text,esc_profile,game_profile}`.
- Authentication for one owner: random **32-byte / 256-bit recovery key**, entered manually in the candidate client. The Worker stores only a SHA-256 digest as a Cloudflare secret; it compares the hash without using an email or CloudBase request. No key is committed or compiled into GGman. HTTPS only for native client.
- The D1 owner is the fixed `single-owner-primary` key scope, not the CloudBase UID. No identity linking or legacy transfer; key loss/compromise requires explicit rotation and safe handling. This is a **single-user convenience solution**, not a public multiuser registration system.
- Native GGman candidate behind `/p:UseD1SingleOwnerCandidate=true` and runtime `GGMAN_D1_ENDPOINT`. Existing account form becomes a secret-key login, and existing application settings, UI text, ESC, and explicit lobby/ESC game-profile cloud clients route to the D1 API. Normal builds remain CloudBase-backed.
- The candidate disables the old CloudBase anonymous device-sync module and legacy usage telemetry while new mode is active. CloudBase registered ranking/history operations fail locally as unsupported (no CloudBase request).
- Cloud-first: existing D1 profile downloads are authoritative; absent D1 profile does NOT auto-upload stale local data. An explicit current-user conflict resolution or manual ESC/game profile upload can create the initial D1 document. D1 conditional version update refuses stale overwrites.
- Signed GGman 3.5.78 production, auto update and original database are unchanged.

## Owner-run provisioning (no installed Cloudflare connector)

Cloudflare account access is not connected in this session. The repository's `experiments/cloud-d1-lab/Setup-Single-Owner.cmd` is an **owner-operated provisioning script** that uses official Wrangler. It needs Node.js/npm and browser approval for `wrangler login`. It creates the D1 database, applies the schema, generates a 43-character random key on the local PC, deploys fail-closed Worker code, and installs the *hash only* as a Worker secret. This is a remote operation **only when the owner runs it on their PC**; no Cloudflare service has been created by commits or CI.

It writes to Documents/GGman-D1-Setup:
- `Personal-Recovery-Key.txt`: SECRET; keep private, do not send or commit, copy/paste into new login form.
- `D1-Endpoint.txt`: nonsecret HTTPS Workers endpoint; copy to candidate folder before launching `Start-D1-Candidate.cmd`.

An auth key is equivalent to full read/write control over the single owner's D1 data. Rotate if leaked; losing it without backup means no simple account recovery. Workers Free-tier quota exhaustion stops requests, not automatically paid. Cloudflare's own D1 recovery features are independent of whether the old CloudBase was backed up.

## Acceptance and rollback

1. Owner creates new D1 under Cloudflare, shares **only sanitized success/error output and the public Worker endpoint**, never key or Cloudflare credentials. Check `whoami` using GUI, no token disclosure.
2. Extract **unsigned** candidate in separate folder; stop signed official GGman first because normal single-instance mutex is retained. Set public endpoint, sign in by entering key. Verify empty first-time state and manual upload/restore of synthetic settings.
3. Check app preferences, customized text, manually uploaded ESC/game config, account switching, concurrent version conflicts, local unsaved edits, cloud-first second Windows user profile, and game behavior. Never run destructive restore purely for testing.
4. Check actual Workers requests, D1 rows read/written, storage footprint and error rates in your Cloudflare dashboard for at least several days. One always-on user at 90-second polling with app/text reads is ~1,920 Worker requests/day, a scenario estimate, versus 100,000 Worker requests/day free limit; scaled growth needs batching/adaptive syncing.
5. Only after real user approval and signed release verification, choose an official backend switch. Original signed executable is always available for rollback. Do not delete CloudBase as part of this task.

## Status (at this source revision)

- Clean-slate replacement code, owner provisioning scripts and CI gates: present on Draft PR #356.
- Offline unit tests, WinForms candidate build, Windows PowerShell syntax: consult GitHub Actions for **final SHA**; do not infer live compatibility from green CI.
- Cloudflare account login, new D1 created, Worker deployed, recovery key provisioned: **NOT DONE** (no account connection).
- Native Windows user sign-in, true D1 read/write, cross-device restore and daily account usage: **NOT TESTED**.
- CloudBase source backup: **INTENTIONALLY SKIPPED AT OWNER REQUEST**; old data unchanged and not migrated.
