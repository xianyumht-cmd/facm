# Personal Stats Activity Metrics

## Scope

The local “My GGman” profile derives activity metrics from the existing `personal-stats.json` history. No new database, telemetry stream, polling loop, or cloud table is introduced by this feature.

## Metrics

- **Current streak**: consecutive local calendar days ending today that appear in `ActiveDays`. If today is not recorded, the streak is `0`.
- **Recent 7 days**: distinct recorded local calendar days in the inclusive seven-day window ending today.
- **Recent 30 days**: distinct recorded local calendar days in the inclusive thirty-day window ending today.
- **New accounts this month**: unique local account records whose first-seen timestamp falls in the current local calendar month.

The existing total account count, total active days, first-seen date, recent account history, and anonymous ranking remain unchanged.

## Data boundary

The metrics are derived from data already stored locally. They are not uploaded to CloudBase and are not included in the existing anonymous account-ranking RPC payload.

## UI contract

The activity summary is shown as a single compact line below the existing profile hint. The window remains within the current 720x560 layout and does not introduce a visible scrollbar or a second visual framework.

## Failure behavior

Malformed or unsupported local history is handled by the existing last-known-good recovery path. Activity calculations ignore invalid calendar-day entries and invalid account timestamps rather than inventing values.
