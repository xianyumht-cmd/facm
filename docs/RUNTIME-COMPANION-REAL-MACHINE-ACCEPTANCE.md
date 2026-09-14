# Runtime Companion Real-Machine Readability Acceptance

This note records the live Tencent-client review criteria that close the gap between merely fitting a 320 px window and matching the intended Akari-style runtime interaction density.

## Observed rejection cases

The reviewed compact candidate was rejected when primary recommendation text was clipped by adjacent actions, the Mayhem surface used a native WinForms Details table with light scrollbars/header chrome, Bench overflow exposed a native scrollbar, and unresolved champion identity leaked a raw numeric id such as `#904`.

## Current acceptance target

- Keep the transient client width at the 320 px baseline; do not solve clipping by growing back into a large sidebar.
- Primary recommendation text owns the first reading line(s); evidence/metrics are secondary.
- Compact actions use short labels and must not consume the recommendation lane.
- Mayhem augments use FACM-native icon rows with local paging rather than a Details table.
- Bench overflow uses local paging rather than a native scrollbar.
- The scrollable detail body may respond to wheel/input, but native light-themed WinForms scrollbars must not be visible on the dark companion surface.
- Unresolved hero identity uses a neutral resolving state and then the localized Riot/LCU name; raw internal champion ids are not user-facing names.
- The header status indicator is a semantic non-prose glyph with detailed state in tooltip/context; it is generated as a code point rather than introduced as new UI copy.

Real-machine acceptance still requires screenshots on the Tencent client after a CI-green review build. This file does not authorize merge or production release.
