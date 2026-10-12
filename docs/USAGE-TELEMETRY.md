# GGman Anonymous Usage Telemetry

## Purpose

Collect only aggregate feature-usage counts needed to decide which GGman areas are used and maintained. The feature is fail-soft and must never block startup or League functionality.

## Consent

Telemetry is disabled by default. In the upcoming registered-stats version, the user can enable or disable it via **我的 GGman → 数据与隐私** (instead of an always-visible switch). Consent is stored locally in `data/telemetry-consent.json` and is intentionally not part of CloudBase settings sync.

Disabling telemetry immediately clears the in-memory upload queue. No new events are queued while disabled.

## Uploaded data

Each upload contains only:

- existing anonymous CloudBase owner identity
- UTC event day
- allowlisted-style event name (`a-z`, `0-9`, `_`, max 64 characters)
- aggregated event count
- GGman app version

The database stores one aggregate row per owner, day and event name. Rows older than 180 days are removed when that owner submits new telemetry.

## Explicitly excluded

Telemetry does not upload or derive:

- PUUID or account names
- account history hashes
- passwords or LCU credentials
- IP addresses
- hardware serials or hardware fingerprints
- game installation paths
- window coordinates
- arbitrary log contents
- request URLs or response bodies

The existing `ggman_devices` record may contain the normal app/OS metadata already required by the device-identity contract; telemetry does not add a second copy of that data.

## Current events

The first telemetry release records high-level feature surface usage only, such as opening the League Dashboard, Player page, Live page, Mayhem page, My GGman page, OP.GG advisor, Efficiency, Repair and Presence surfaces, plus the automatic Champion Select Companion surface.

No account content or game-state payload is attached to these events.

## Database

Migration: `cloudbase/sql/004_usage_telemetry.sql`

Table: `public.ggman_telemetry_events`

RPC: `public.ggman_record_usage(p_events jsonb, p_app_version text)`

RLS uses the existing anonymous CloudBase subject as `owner_id`. The RPC is `SECURITY INVOKER`; it writes only through the caller's RLS scope.

## Failure behavior

CloudBase failures are logged as informational and the local application continues normally. Pending counters are retried in memory during the current run and are discarded on shutdown if they cannot be uploaded.
