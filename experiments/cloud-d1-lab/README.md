# GGman Cloud D1 Lab — isolated experiment

**Not deployed. CloudBase production backup not performed. No production authentication, endpoint or config modified.**

## Mandatory backup-first gate

The current GGman CloudBase database contains registered preference, UI text, ESC/game-profile, personal-stats, account-history and legacy records. CloudBase email identities are provider-managed. The repository's scripts/cloudbase-backup/Backup-CloudBase.cmd offers an operator-run pg_dump and pg_restore inventory/hash check for a read-authorized PostgreSQL connection. No credentials are embedded. Backups may contain sensitive records: keep encrypted/offline, never commit or share them publicly.

Required proof before any live data import: backup command success, backup SHA-256 and object inventory, independently restored isolated PostgreSQL, required GGman tables with counts/owners, and separate evidence of CloudBase identity-provider user export and recovery. If the exhausted CloudBase plan blocks DB connections, use the CloudBase authenticated console/manual backup options instead. The backup script itself does not export provider-managed auth identities. Do not assert that source data are safe merely because a dump archive exists.

## Lab authorization

This Worker is intended only for a small private testing group. It verifies signed Cloudflare Access RS256 JWTs against the configured issuer and application audience. Verified JWT sub is the *only* database ownership key; caller-provided user/account IDs are ignored. Authentication failure -> 401. Issuer/JWKS unavailable -> 503. No broad CORS, host objects, session cookies, CloudBase admin tokens, or SQL proxy. A protected Access policy on the custom test hostname is still required.

**Cloudflare Access Free has a 50-user limit**, so this is NOT the final GGman player login system. A later identity provider, old UID linking, account recovery and user consent are mandatory before a user-facing migration. Do not use Access subject as if it were the CloudBase UID.

## D1 schema and sync guarantees

D1 migration migrations/0001_init.sql defines per-owner per-category documents. Supported categories: app_settings (32 KiB), ui_text (180 KiB), esc_profile (400000 bytes), game_profile (510000 bytes). This does not cover historic anonymous settings, ranking or usage metrics.

GET returns existing cloud snapshot plus revision; absent server row explicitly means no cloud backup. Client must NOT automatically upload old local settings after account switch. PUT requires expectedVersion, and first revision requires explicitBootstrap=true. Conditional owner-key INSERT/UPDATE prevents overwriting newer cloud revisions. Shape/byte checks and credential-shaped key rejection prevent accidental ingestion of sensitive identity fields. ESC/YAML must NEVER be written back to live local game files by this lab.

Run offline tests using Node 22 or later:
- npm test
- npm run check

Node tests use a fake D1 database and injection of a test identity verifier; production request handling contains no HTTP-header test bypass. GitHub Actions tests SQLite schema separately. CI does not prove live Cloudflare authentication, actual D1 execution, or real client behavior.

## Deliberate deployment blockade

wrangler.toml keeps workers_dev=false and a placeholder database UUID. No route or production secrets are configured. No automatic deployment task exists. To stage under an authorized Cloudflare account, an operator must create a *separate* D1 named ggman-cloud-lab, replace the test UUID, set ACCESS_ISSUER/ACCESS_AUD privately, add a restricted Access application on a test HTTPS hostname, apply schema migration with wrangler d1 migrations apply ggman-cloud-lab --remote, and only then consider a private deployment. Never apply migrations or import data against live GGman.

Cloudflare documentation: https://developers.cloudflare.com/d1/wrangler-commands/ and https://developers.cloudflare.com/d1/best-practices/local-development/.

## Real usage and acceptance

Design estimates: one indexed D1 read for each GET, one conditional write for each PUT (plus platform overhead). These are NOT actual usage measurements. Once the owner operates a private test environment, inspect Cloudflare Worker requests, D1 rows read/written, stored bytes and runtime errors daily in the account dashboard, and allow at least 20% headroom against current Free-tier limits. A daily D1 quota exhausts into query errors, it does not silently upgrade to paid.

Go/no-go requires: verified source backup + isolated restore; A/B real identity isolation; old account → new mapping and recovery; cross-machine cloud-first restore and unsaved-editor protection; conflict and explicit bootstrap; live quota measurement over multiple days; safe rollback, native tests and explicit approval. None of these live gates have been completed.
