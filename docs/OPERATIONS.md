# GGman Operations (3.5.x)

## Candidate-only iOS Glass UI build (2026-10-20)

The opt-in WPF prototype is isolated from the normal 3.5.78 WinForms distribution. Candidate MSBuild command on Windows with .NET Framework 4.8 targeting pack:

```powershell
msbuild FACM.sln /restore /m /p:Configuration=Release /p:Platform="Any CPU" /p:UseIosGlassPreview=true /p:ContinuousIntegrationBuild=true /v:minimal
```

It uses the existing host/League services via `MainForm`, and is activated only when the process environment variable `GGMAN_IOS_GLASS_PREVIEW=1` is set. Without that runtime switch even a preview-enabled build displays the original CompactMenuForm. Default production builds do not compile the WPF preview source and have no WPF opt-in. The `GGman iOS Glass Candidate` Windows workflow builds and uploads an **unsigned**, isolated candidate artifact; do not overwrite the installed official executable, publish it through the normal updater or use it with destructive real game/cloud actions simply for visual QA.

Acceptance is separate from compilation: on an isolated Win10 19045 machine, verify outside click/Escape, tray/external activation, new control-center navigation, fallback when disabled, clean shutdown, account safety, all themes, Chinese text, Tab/focus, per-monitor scaling at 100/125/150/200%, and stable FPS/CPU/GPU/memory in League activity phases. Visual snapshots and hash/size evidence must be pinned to the reviewed commit. If UI preview fails, disable the environment switch or use official v3.5.78; do not change production updater manifests.


## 3.5.78 published release follow-up (2026-10-17)

PR #351 was explicitly authorized for production publication before native visual evidence. The signed GitHub Release v3.5.78 and updater were published by [workflow #39019998489](https://github.com/xianyumht-cmd/facm/actions/runs/39019998489) with the `FACM.exe` compatibility asset, `GGman.exe`, public byte/hash/signature verification and `force_update=false`. Owner acceptance remains pending. For post-release follow-up ask the owner for actual screenshots after updating: Dashboard and navigation; Repair, Presence and Quick Tools; Mayhem and recommendation; account/ESC/cloud; updater/theme; cleanup/profile/pet interfaces; topmost companion/ChampSelect if safely obtainable during normal play. Record theme, DPI scaling, client size, exact version, cropping, truncation, focus and interaction symptoms. Compare against prior baseline where available.

Request an independent Astra design critique only once representative native evidence exists, with professional English handoff, and report source-specific, screenshot-grounded defects. A published build does not certify visual quality. For regressions use a reviewed patch and new official signed release; if a critical defect is confirmed, follow the existing audited rollback procedure and inform affected users. Do not modify the v3.5.78 release bytes.


## B5 continuation / B6 safety and visual QA handoff (2026-10-17)

The current candidate adds CleanupRepair, Captcha, Registered Stats Privacy, Profile Customization, PetPicker, AnimalPetPicker, LeagueItemSet, LeagueBuildApply and Personal Stats. Final B6 native acceptance must capture the same-screen previous/current candidate pairs at a pinned SHA for all, plus the actual topmost companion/ChampSelect assistant and any remaining reachable dialogs. All ten ThemeCatalog IDs, 100/125/150/200% DPI as feasible, focus/Tab/Escape, selection/disabled/hover, narrow windows, long Chinese descriptions, any credential-safe image states, and visual independence of dangerous actions are required. Do not perform destructive deletion, real game write, new-login/password challenge or pet resource download only to obtain a screenshot. Use authorization-safe fixtures or genuine operator observation.

Build issue #39018862920 on `LeagueBuildApplyForm` CS0103 was caused by removing three auto-apply callbacks during presentation refactoring; all were restored in `a3ae722de1226e7f67fdcf672a45cdb87e9f492d`, which passed Windows Build #39019063500. Explicitly exercise enabling/disabling auto-apply, UI state updates, disposal unsubscribe and safe confirmation in the later native regression. A passing build does **not** prove live League service behavior.

**Do not publish from source CI alone.** The owner's accepted sequence remains one consolidated release, then a single independent Astra professional visual review. Pre-release functional/desktop safety, installer/rollback readiness and actual screenshot QA remain prerequisites; record missing cases as INCONCLUSIVE, not green.

## B5 native cleanup and operation dialog visual/safety matrix (pending)

At the consolidated final SHA, capture CleanupReviewForm with blocked and permitted test entries, varied Chinese paths/bytes, footer cancellation focus, and all relevant theme/light/high-contrast states; do not execute destructive cleanup to collect screenshots. Verify success/warning/error status and readable grid text at 100/125/150/200% DPI. Exercise Escape/Tab/Shift+Tab/Enter, explicit Cancel and non-accidental Execute selection. For BackgroundOperationDialog and GameLocatorSearchDialog verify window ownership, no premature close during work, keyboard cancellation for the cancellable task and progress copy visibility. Unit and CI checks cannot establish actual WinForms text clipping or delete safety; all native conditions remain INCONCLUSIVE until tested by an authorized operator.

## B4 source-only verification and eventual consolidated native acceptance (2026-10-17)

At final candidate SHA, capture floating launcher (generic/context/onboarding), theme picker with selected/swatches in dark/light, updater idle/available/required/announcement/progress states, registered account login/session, and ESC directory plus local Save/Restore. Verify Tab/Shift+Tab/Enter/Escape behavior, narration/accessibility, all theme IDs, 100/125/150/200% display scaling, long Chinese text and meaningful contrast. Confirm no change to updater authorization, user credentials, provider session, cloud guards or game local config handlers. Do not perform account login, release installation, destructive restoration or forced game exit solely to obtain screenshots. Update actions must use authorized test data and a rollback candidate. Review launcher compact height and theme-pick button focus on real Win10; CI and source tests do not prove visual success.

If automated UI Text Contract reports new user-visible or a11y literals, use existing UiTextRuntime/UiTextKeys, native button Text or register genuine copy. Do not suppress by weakening the guard. ThemePicker redundant a11y label violations in intermediate commits were resolved this way. Final-head Windows Build, UI Text Contract and Mayhem probe must all be green before claiming the source batch build-accepted.

## B3 Mayhem, League data and recommendation visual acceptance (pending, 2026-10-17)

The owner requested one consolidated operator acceptance after whole-product modernization, not per-page interruptions. At final candidate SHA capture Mayhem (initial/pending/completed/error), Live game and bench, My Matches (loading, player, list), Advisor (waiting/data/error), Recommendation (choices selected/unselected, action-enabled/disabled). Compare exact same theme/client/DPI and content against baseline. Verify image preview shows no unintended **horizontal** scrollbar, vertical scrolling preserves intended position, save/copy output is unchanged, and full 100/125/150/200% scaled WinForms workspace remains keyboard-navigable. Validate light palettes and selected choice contrast; no genuine game destruction or production cloud changes for visual evidence. The latest CI green result is necessary but cannot prove actual WinForms layout; all native outcomes are INCONCLUSIVE until operator evidence exists.


## B2 source regression and pending native checks (2026-10-17)

Windows Build #39007644220 and UI Text Contract #39007644251 passed on code SHA `916d007f82030dc561d4e843a8760c41cbcfb5f4`. In the eventual **consolidated** Windows acceptance (no per-page operator interruptions), capture Quick Tools and Presence under the same theme/scale; verify that all existing controls are reachable by Tab/Shift+Tab, Chinese hints are legible, scroll does not jump unexpectedly, 3/2-column Presence choices remain visually separated, and save/toggle/update handlers remain unchanged. Compare 100/125/150/200% and ten themes as practical. Functional authentication remains separately tested with authorized accounts only; deterministic UI smoke does not prove live status writes.

## Consolidated cross-product visual upgrade verification (2026-10-17)

Under PR #351, internally implement B1–B5 without requiring the operator to review every incremental form. Use DESIGN.md's active-surface ledger. Once B6 is ready, collect one pinned final candidate and screen capture set for Hub, automation, Presence, Mayhem, companion, launcher, updater, account, themes, cleanup and auxiliary dialogs at controlled themes, states, 100/125/150/200% scale where feasible. Preserve native system menus and dialogs.

Pre-release checks: all required final-head CI green, no new P0/P1 regression, native Win10 function/IME/Tab/focus/scroll/DPI/screenshot and measured EXE/startup/memory/latency comparison, package verification and tested rollback path. Do not use production credentials or deliberately execute destructive actions for screenshots.

Owner-selected order puts independent professional Astra screenshot-based visual audit after the one consolidated release. This is a **post-release** audit, not a pre-release guarantee; disclose risk and retain hotfix/revert readiness. Do not publish while any required pre-release engineering or operator safety gate is red or missing.

## B1 game-repair before/after native acceptance (2026-10-17)

For PR #351's next candidate, prioritize **Game Repair** as the operator-visible differentiated sample. From a Windows 10 19045 system, capture the selected repair route from both the unchanged main baseline `1194a4cf8451b1886c03cdf733e067fd38037f66` and the final candidate in the same theme, client dimensions, resolution and display scaling. Also capture a deliberately narrow/scrolling case, focus indication and a harmless result status; do not trigger destructive `一键结束游戏` or client restart merely to obtain imagery.

A separate independent reviewer should verify that all five original operations remain accessible by keyboard, hint copy is fully visible with no microtext, actions form a predictable right-aligned column when wide and stack when narrow, result text remains truthfully classified, and a theme switch does not erase state or alter service behavior. Include selected/current route, 100/125/150/200% when practical, light theme and all ten preset palettes. Compare captured pixel dimensions rather than scaling pictures to match. User task feedback is welcome but not a professional visual signoff.

Use the existing Windows build and UI Text Contract gates and report exact HEAD and skipped online checks. Before claiming B1 COMPLETE, require an independent critique of actual rendered screenshots and a native keyboard/DPI and performance comparison. Treat missing cases as INCONCLUSIVE, not PASS.

## Precision Workbench B1 native design acceptance (2026-10-17)

This is a **Draft-only pilot**; source compilation does not establish screenshot quality. Collect B0 images from the main baseline `1194a4cf8451b1886c03cdf733e067fd38037f66` and candidate images from the final B1 commit. Use the same Windows 10 machine, fixture data, window client dimensions, DPI, color theme and account-safe content. Never publish login credentials, token-bearing UI, QQ/LOL identifiers or production configuration in screenshots.

**Required matrix:** Hub and default Dashboard, 1120×640 and 900×580 logical client size where work area permits, 100/125/150/200% DPI, default dark and cloud-light at each scale, all ten themes at 100% and their selected/focused states at 150%, and one restrained vivid theme at full depth. At 200%, explicitly test 1920×1080 constrained desktop fit; don't pretend a 900 logical-unit width physically fits into ~683 units. Record disconnected, populated, pending, empty/error/stale, long Chinese labels, hovered/pressed/disabled and owned dialog states. Repeat Alt+Tab, close/Escape, Tab/Shift+Tab, touch-free navigation and all nine routes. Profile editor dirty Save/Discard/Cancel and Foo/foo isolation remain regression gates.

**Artifacts:** Name each exact captured PNG `SHA_surface_theme_dpi_clientW-clientH_scenario_state_before-or-after.png`; write a matching scenario table with OS/.NET, resolution, effective scaling, fixture ID, timestamp, observable result and screenshot references. Never resize screenshots to fabricate equal comparison. Independent reviewer should annotate actual failures and score all observed professional design dimensions 0–4; every dimension must reach at least 3 before a visual PASS.

**Performance:** Compare same-builder base and B1 binaries. Target <=2% EXE growth, <=max(10%,50ms) startup median/p95 regression, <=max(10%,5MiB) idle memory regression, <=max(10%,30ms) navigation/theme p95 regression, no new >100ms UI stall, and no sustained idle CPU growth >0.5pp. Collect raw samples, state when cold-cache procedure is unavailable, and classify noisy/incomplete evidence as INCONCLUSIVE. Retain existing strict <10MiB hard contract and League-specific budget limits.

**Machine availability:** No native Windows desktop or screenshot inspection is available to the GitHub connector. CI Windows runners are build runners, not interactive native design acceptance. The product owner/authorized operator only captures and supplies observed evidence; they are not responsible for a professional aesthetic verdict. Don't claim PASS, merge, release or updater activation until native visual and keyboard/DPI evidence and an independent review are complete.

## Third Astra review — replacement-rule selection identity (2026-10-17)

Run the `shell-ux` performance smoke containing `ValidateCaseSensitiveRuleSelectionForSmokeTest` for Yes, No and Cancel. This creates two valid case-distinct rules (`Foo` and `foo`) with in-memory persistence, resolves an unsaved selection change, checks the exact destination key and visible value, then verifies Save and Reset never modify the sibling key. Replacement selection uses ordinal comparison; text entry key selection still ignores case, and search filtering remains case-insensitive. Both final-head Windows Build and UI Text Contract must pass; actual WinForms/DPI and authenticated CloudBase restore checks remain manual premerge gates.

## Second Astra source review — candidate validation (2026-10-17)

For PR #350, repeat `shell-ux` and `ui-text-customization` smoke after correcting stale list entries when Yes resolves Save/Discard/Cancel navigation. The `ValidateNavigationRebaseForSmokeTest` fixtures cover ordinary text and replacement rules with a newer target value, missed restore notification, Yes -> `SaveLocal(false)`, destination selection and a subsequent Save without remote rollback. The `ValidateConflictDiscardRecoveryForSmokeTest` fixtures verify same-key conflict -> Discard -> immediate successful re-edit in both modes. A reset-write failure fixture verifies no baseline advance or sync notification. Always require both required CI workflows on the final PR head and another independent review; do not infer native/registered CloudBase behavior from in-memory smoke.

## Astra data consistency blockers — candidate validation (2026-10-17)

For PR #350 review corrections, run `UiTextEditorPanel.ValidateDraftNavigationForSmokeTest` through the `shell-ux` performance smoke and `UiTextCustomizationStore.ValidateForSmokeTest` through the UI-text-customization smoke. Assertions use injectable **in-memory** persistence and save notification callbacks: simulated disk failure -> discard -> save another entry does not publish rejected text; invalid 201-character replacement source cannot poison later saves; cloud profile B restored over baseline A is retained when another key is saved; an unannounced unrelated external change is rebased; same-key external changes reject save/reset without notification; after refresh, reset preserves unrelated cloud values.

CI recovery on 2026-10-17: first runs for these corrections failed before runner allocation (not compiler failures). Retry attempt 2 on code head `9321b2fd7580d2a93c2a6919c8f7097b752ff532` passed [GGman Windows Build #38989938130](https://github.com/xianyumht-cmd/facm/actions/runs/38989938130) and [FACM UI Text Contract #38989938158](https://github.com/xianyumht-cmd/facm/actions/runs/38989938158). Windows job logs confirm `shell-ux`, `ui-text-customization`, `ui-text-cloud`, and automatic-sync smoke checks passed. These are deterministic/in-memory checks, not an actual authenticated CloudBase account-switch test; five live gateway checks were skipped. Before merging require successful checks for the final documentation-inclusive PR head, a fresh independent code review, WinForms DPI, close/navigation, keyboard and real authenticated cloud-restore manual acceptance with sanitized logs. Do not use this CI package as a released update.


## UI P1 candidate acceptance (PR #350, 2026-10-16)

Before merge or release, require successful **GGman Windows Build** and **FACM UI Text Contract** on the final branch head. Native Windows review must cover: (1) Hub survives Alt+Tab/external click, still closes by deliberate close/Escape, and temporary launcher still dismisses normally; (2) an unsaved text edit prompts Save / Discard / Cancel on item selection, search/filter, mode change, new rule, collapse, workbench navigation and close, with Cancel preserving the visible draft and current route, Save persisting locally and Discard not issuing cloud publication; (3) a failed local Save does not permit destructive navigation, and cloud restore waits for genuinely pending visible drafts; (4) readable enabled/hover/pressed text for all ten themes, especially cloud-light recommendation title and bright accent buttons; (5) keyboard-only cleanup preview Tab/Shift+Tab, Enter/Space, Escape and cancellation, without accidental execution.

Check 100/125/150/200% DPI for clipping and focus order, and verify no changes to League/CloudBase/updater behavior or cold-start overhead. A green compiler/smoke run alone does not prove native focus and draft interaction behavior. Preserve official v3.5.77 in `online/version.json` until a separately verified signed release exists.


## Normal development flow

1. Work from current `main` or a focused branch.
2. Keep changes inside the 3.5.x lightweight architecture unless a concrete requirement proves otherwise.
3. Run/observe **GGman Windows Build** and **FACM UI Text Contract**.
4. For League/Mayhem/UI changes, use the relevant smoke tests and a real Windows/League check when behavior cannot be proven in CI.
5. Merge only after the branch is green.

## Local release build

Windows requirements: Visual Studio 2022 Build Tools or Visual Studio 2022, .NET Framework 4.8 targeting pack and .NET 8 SDK.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

The script must keep the lightweight contract:

- validate tool inputs;
- build/self-test `FACM.PetHost` separately;
- remove/ignore stale `out/PetHostBundle.zip`;
- build `FACM.sln` with CI smoke tests;
- verify ToolBundle is embedded;
- verify `FACM.Resources.PetHost.zip` is absent;
- verify lightweight `GGman.exe` <10 MiB; the published `FACM.exe` is a byte-identical legacy-update compatibility asset.

## GitHub build artifact

Use **Actions → GGman Windows Build → Run workflow** when a fresh candidate is needed. A successful run uploads the build artifact from that run (including the lightweight executable/package metadata); the artifact is not a signed public release.

Do not treat an artifact as a public release until the release workflow updates the GitHub Release and `online/version.json`.

## Publish a new 3.5.x version

Canonical workflow: **GGman 3.5 Lightweight Release** (`.github/workflows/publish-3.5-lightweight.yml`).

Two supported entry points:

### Manual

Actions → GGman 3.5 Lightweight Release → Run workflow, then supply:

- new 3.5.x `version`;
- `minimum_version`;
- `force_update`;
- `prerelease`;
- `release_notes`.

### Audited request file

Edit `release/3.5-request.json` on `main` using the exact current schema:

```json
{
  "version": "3.5.73",
  "minimum_version": "3.0.0",
  "force_update": false,
  "prerelease": false,
  "release_notes": "Describe the verified GGman 3.5.x changes here."
}
```

This JSON is a **format example, not an authorized release request**. Choose the next unused 3.5.x version when actually publishing. A push touching that file triggers the same publisher. The workflow rejects an already-existing release tag; never reuse an old version number to publish new bytes.

The publisher freezes `main`, builds and signs the candidate, first writes an `enabled=false` manifest, publishes the GitHub Release, downloads the public asset again to verify size/SHA-256/signer, then enables the online manifest. The current manifest schema is migration-free.

### 3.5.40 update-asset compatibility

`v3.5.40` validates update manifests against a `FACM.exe` GitHub Release asset name. Current source accepts both `FACM.exe` and `GGman.exe`, but the online manifest must remain consumable by 3.5.40 while that version is still supported.

- Every 3.5.x release publishes byte-identical `GGman.exe` and compatibility `FACM.exe` assets.
- `online/version.json.download_url` points to the compatibility `FACM.exe` asset.
- The publisher verifies that both public assets have the exact same size, SHA-256 and Authenticode signer before enabling online update.
- Do not remove the `FACM.exe` compatibility asset or switch the manifest back to `GGman.exe` until the minimum supported version is newer than 3.5.40 and old-client compatibility has been intentionally retired.

After publishing, verify:

1. GitHub Release `vX.Y.Z` exists and contains `FACM.exe`.
2. Release asset SHA-256 matches `online/version.json`.
3. `online/version.json.enabled=true` and points to the exact Release asset.
4. `minimum_version` and `force_update` are intentional.
5. `online/version.json` still contains no 4.x migration object.
6. A currently supported client can check/download the update.

## Online manifest safety

`online/version.json` is the client-facing release pointer. Do not point it at CI artifacts, branch files or an old tag with different bytes.

The current updater accepts approved HTTPS release URLs, validates SHA-256 and package identity, then performs atomic replacement using `FACM.Updater.exe`. 4.x migration/bootstrapper fields are no longer part of the runtime contract or publisher output.

## Announcements and mirrors

- Announcements: `online/announcement.json` and **FACM Online Management**.
- Mirror pool: `online/mirrors.json`.
- GitHub Release remains the canonical artifact source; mirrors are transport accelerators, not a release trust source.

## 3.5.77 cloud-first account and multi-machine acceptance

The registered server must have a non-empty software-preferences and UI-text record. On machine A, intentionally save portable software preferences and visible UI text, wait for automatic upload, then log in with the same GGman email on machine B (or switch GGman accounts on the same machine). Machine B must adopt remote values **without** `保留本机配置 / 采用云端配置` conflict prompts. An unrelated local edit imported before login must not silently overwrite the server. When a user deliberately Saves a portable setting after the remote has been checkpointed, verify its CAS upload (or that a newer server revision prevails when concurrently updated). Repeat with an active unsaved UI-text editor draft: the local draft should not be silently overwritten; save or discard it and let the cloud restore continue.

For a new registered account with **no** server app/text profile, verify the UI reports `云端尚未创建备份`, offers `创建云端备份` once per category and does **not** silently publish locally inherited cafe settings. Click to create the initial remote revision, restart GGman and verify remote-first behavior. For the LOL game profile, verify only the dedicated `上传大厅与 ESC 设置` button updates its cloud payload, while changing Riot account restores the saved profile safely. No SQL migration is required; do not rerun SQL 009.

## 3.5.76 open-lobby manual-upload acceptance

In the signed client, keep the League launcher open, finish a training match, and wait for `League of Legends.exe` (the game process) to stop. While `LeagueClient` / `LeagueClientUx` remains running, click `上传大厅与 ESC 设置`. The confirmation must describe finishing a training/match and returning to lobby, not ask to close the launcher; capture and upload should succeed if both allowed YAML files are present and stable. Reopen the dialog if needed and verify returned remote version. Launch a training game again and verify an attempted manual upload is blocked while the real game process is running. If live Riot locks YAML or has not flushed preferences to disk, report that explicit file error and retry from the open lobby after saving; do not silently upload half a profile or ask users to close the launcher by default. Existing automatic restore after switching LOL accounts remains unchanged.

## 3.5.75 live-lobby configuration restore acceptance

Home and shared internet-cafe users must **not** close the League launcher after switching LOL accounts. Once the signed-in GGman account has deliberately published one cloud game profile, keep GGman running, open LeagueClient, sign into LOL account A and inspect the in-game ESC settings (e.g. borderless/window mode and keybindings) when a match starts. Switch to account B without closing the launcher and confirm the same cloud ESC settings were applied after the new LCU summoner identity became available. Check that a temporary local keybind modification is not continuously overwritten in the same session; only a true account transition/cloud revision change should trigger reapplication. When `League of Legends.exe` is running, no ESC file mutation is permitted.

Launcher's allowlisted YAML is attempted independently in an open lobby. If a file is locked or Riot rewrites settings from memory/server, the outcome is logged and retried as a separate pending operation; it does not invalidate successfully restored ESC. Check `logs/` for `Registered game ESC restored`, `Registered lobby preference file restored`, or `Lobby YAML restore pending` without exposing Riot account IDs. Existing local backups remain in `data/esc-backups` and `data/lobby-backups`. No additional CloudBase migration; **never rerun SQL 009**. Real native writeback and effective UI behavior are a post-publish field acceptance requirement, not something Windows CI alone proves.

- Verify the Windows fixture also preserves indentation when rewriting `        blockNonFriendGameInvites: false` to `        blockNonFriendGameInvites: true`, then rereads that exact key and checks the unrelated `keepThis: 42` field is untouched. The 3.5.74 bug subtracted leading indentation from the colon offset and produced a truncated YAML key; a successful file write without a successful parse/readback is not acceptance.

## 3.5.74 registered game-profile rollout and verification

The user deployed `cloudbase/sql/009_manual_game_profiles.sql` on 2026-10-12. **Never rerun this create-once migration.** CloudBase schema checks 11/11 passed; the simulated role-switched A/B CAS transaction completed and was rolled back; live public gateway anonymous/no-credential read and write probes returned four HTTP 401 denials (Windows CI #38211027414).

3.5.74 separates portable GGman software preference auto-sync from **manual publication** of LOL in-game ESC plus nine allowlisted Boolean launcher preferences (including `lol-notifications.data.blockNonFriendGameInvites`). Source YAML, full rune pages, `LeagueClientSettings.yaml` account identifiers and local installation IDs are excluded from cloud payloads. Auto-restore takes local backups, refuses to write while League/game processes are present, and applies only once per GGman login and observed LOL account / remote revision. Subsequent local changes are never uploaded without the publish button.

The real two-PC GGman email-login publish/readback, Riot account-switch behavior while League is active, network disruption and end-to-end native persistence **were not confirmed by CI**. Obtain user acceptance after publishing, and fix through a new patch release if required. The current app does not hot-rewrite settings while the client is running. Release workflow must publish a signed official 3.5.x manifest, not a CI build artifact.

## Default-on registered background configuration sync acceptance

Default-on auto-sync originally shipped signed in `v3.5.67`; follow-up fixes to registered token renewal and local sync-state recovery shipped in `v3.5.69` and `v3.5.70`. The current signed online release before the 3.5.74 rollout is `v3.5.73` as of 2026-10-12. These checks are **post-release native field acceptance**, not prerequisites that should be marked complete without real devices. A docs-only closeout does not trigger a new release.

This feature **reuses already deployed SQL 005, 007 and 008**: no migration, table creation or operator SQL is required. Verify a fresh Windows install has the auto-sync checkbox checked, without duplicate manual upload/restore buttons. Sign in and wait for the background status to move from waiting to ready; do not need to open My GGman for changes to transfer. On local-only change, confirm one new matching account-owned cloud revision, then no further revisions while unchanged. On remote-only change, confirm local backup-before-restore and no overwrite of local path/device geometry or privacy consent. Test same account across two Win10 systems, then A->B->A on one PC: never transfer the previous account's file content without the rare conflict confirmation. Test both-sides edits, one-time keep-local/use-cloud actions, cloud-missing fallback, invalid-game-path skip, unsaved text drafts, offline backoff, ESC changes while LOL is running and turning off the checkbox during an ongoing request. Logout cancels pending operations; reopen GGman and log in anew since account credentials are intentionally memory-only in this release. These build/signing gates were completed for 3.5.67; any later client fix requires its own reviewed CI and separately signed patch. Do not substitute detached test EXEs.

## Registered app settings rollout (historical 3.5.66; complete)

SQL `008_registered_app_settings.sql` was deployed before the signed 3.5.66 release. The operator already verified its table/RLS/grants, versioned compare-and-swap RPC behavior, simulated A/B isolation and anonymous gateway rejections; the signed 3.5.67 background-sync feature reuses the same deployed schema. **Do not rerun create-once SQL 008** or request a new migration for 3.5.67. Refer to `docs/PROJECT_STATE.md` for the release and acceptance evidence, and to the default-on checklist above for still-open native A/B and cross-device validation.

## Registered stats rollout (006 migration)

`cloudbase/sql/006_registered_personal_stats.sql` is a **create-once production DDL**, not an idempotent rerun. Before execution check the CloudBase environment ID `ggman-d4gioqqcz434d9e4d` and inspect the proposed four tables and four RPC signatures with `to_regclass`/`to_regprocedure`. Do not execute if a conflicting partial schema already exists; review and reconcile first. Operator execution must be followed by read-only `pg_class.relrowsecurity`, `pg_policies` and `has_table_privilege/has_function_privilege` checks, then real authenticated-A/B/no-header/anonymous bearer gateway calls including a legacy import replay and optional ranking opt-out. Verify that repeated login doesn't reset opting out; duplicate import doesn't add counts/days; two devices sharing one registered identity don't double-count the same newly observed PUUID; original local `personal-stats.json` and recovery remain intact. Official 3.5.64 was signed and enabled on 2026-10-10 after schema/catal​​og, mock-role A/B, eight live anonymous/no-auth RPC denials and final CI passed; native user A/B/cross-device migration remain operational follow-up checks. Do not re-run create-once SQL 006. Telemetry must stay consent-bound, not silently become a registered-account tracker.

## ESC in-page relocation and live-game acceptance

For patches to the ESC workflow, verify the sole user entry is LOL 工作台 → 我的 GGman, and that standalone Settings/tray ESC items are gone. Verify detection by an open League process (both MainModule and WMI fallback when needed), persisted GGman game directory and Windows uninstall registry; an unrelated `Config` must not be accepted. Never require a game/process shutdown solely for backup/restore. Check actual read permissions and file lock responses, local pre-restore backups, successful in-use unlocked file replacement, rollback on failure, and whether an active Riot/Tencent League client rewrites settings on exit. Runtime and in-game persistence cannot be certified by Windows CI alone. Publish only a signed, publicly verified 3.5.x Release through the normal one-click update channel.

## League regression checklist

For automation changes verify at least:

- Lobby auto-search reacts without an artificial first delay.
- one stable Lobby does not generate duplicate search POSTs.
- a true failed search can retry; an already-applied ambiguous write does not duplicate after queue-state reconciliation.
- ReadyCheck reacts immediately; a true failure can retry; final Accepted/Declined state stops writes.
- no secondary Gameflow poll loop was introduced.

For desktop visibility changes verify InGame hide and post-game ownership restore for default ball, sprite/VPet and user-manually-hidden states.

For Mayhem changes verify percentage units, full content and load speed; do not casually change the service/cache/network path.

## 海斗榜来源健康监测

正式工作流：**FACM Mayhem Source Probe**（[mayhem-source-probe.yml](../.github/workflows/mayhem-source-probe.yml)），按 UTC cron `17 */6 * * *` 运行；涉及 Mayhem 源码的 push/PR 也会触发。GitHub Actions 的定时运行存在平台排队偏差，不应据此假设准点执行。

1. 在仓库 Actions → **FACM Mayhem Source Probe** 打开最新的 `schedule` 运行，查看 **Run live Mayhem source probe** 步骤，而非只看工作流总状态。PR 上探针 job 有 `continue-on-error`，步骤失败不等于工作流总状态一定失败。
2. 查看运行摘要中的 **Mayhem ranked source health** 或展开步骤日志。每个来源分别报告 `name`、`patch`、`rows`、`complete` 和 `official_patch_match`。还会报告 `complete=N/3`、`official_patch`、`current_patch`、`selected`。
3. `complete=true` 代表解析出连续、无重复、数值有效的完整前十名；`official_patch_match=unknown` 代表本轮未取得腾讯官网补丁，**不能认定官方版本已校验**。同补丁的多个第三方网站可能使用相同的腾讯统计基础，并非独立比赛样本。
4. 至少两个来源完整且（官网版本可读时）与官网一致，为当前冗余目标；少于两个发出 **degraded** 警告，但最终攻略仍能正常展示时不直接判定整个客户端故障。最终英雄攻略、榜单、图标、强化等完整性不满足 smoke 合约时，**Run live Mayhem source probe** 步骤失败。
5. 出现降级或失败时，先区分网络不可达、HTML 结构变化、榜单不足 10 条和补丁过期。对比至少连续 2–3 次定时结果，必要时取近 14 天的 `mayhem-source-probe-<run-number>` 日志 artifact；不能只拿一个 PR 的绿色勾号证明长期可用。

2026-10-11 的 [`main` 实时探针 #38103524059](https://github.com/xianyumht-cmd/facm/actions/runs/38103524059) 已成功：Hexdata `0/10`、补丁未知；ARAMGG `10/10`、26.20；ARAMMayhem `10/10`、26.20；腾讯官方补丁 `unknown`；自动选择 ARAMGG。此前 10 月 9 日定时探针多次以 `Top-ten ranking is incomplete` 失败。此处记录的是观测，不意味着 Hexdata 故障原因已定位或未来六小时运行一定成功。

**排查原则：** 来源检查是既有 Windows CI 的一部分，不在 GGman 客户端新增轮询、账号凭据或新版本发布。修复数据源需在任务分支改动并重新通过 Windows Build、UI Text Contract 和实际 live probe；定时探针数据波动本身不自动触发在线客户端更新。

## Rollback / incident response

If a new release has a blocking defect:

1. stop further publishing;
2. do not mutate an existing Release asset in place;
3. prepare a new patch version from the last known-good source plus the fix;
4. use `force_update` only when the impact justifies it;
5. keep evidence/logs needed to reproduce the issue.

If CI breaks after cleanup, first check for references to removed 4.x projects/scripts/workflows rather than restoring the entire 4.x tree.

## Repository hygiene

Current worktree should not contain FACM4 solution/projects, 4.x migration/bootstrapper code, 4.x-only workflows, CAB/BOOT release tooling or the old heavyweight publisher. Git history remains intact.

## CloudBase schema migrations

CloudBase production schema changes are explicit and must be applied before a client release depends on them. Repository SQL lives under `cloudbase/sql/`. Migration idempotency varies: notably, `005_esc_profiles.sql` intentionally uses `CREATE TABLE` and must **not** be re-executed after initial installation. Inspect actual schema and RLS before any follow-up migration.

For the personal-stats/ranking feature, apply `cloudbase/sql/002_personal_stats.sql` in the existing `ggman` CloudBase PostgreSQL SQL editor before enabling a release that exposes anonymous ranking. The migration:

- adds `ggman_devices.ranking_opt_in` (default false);
- adds the `ggman_record_account` RPC, which derives owner identity from `auth.uid()` and upserts only hashed account history;
- adds the `ggman_get_personal_stats` aggregate RPC, which returns only the caller's account count/rank/population/percentile;
- grants RPC execution only to `anon` / `authenticated` and does not relax table RLS.

After applying it, verify in SQL:

```sql
SELECT column_name, data_type, column_default
FROM information_schema.columns
WHERE table_schema='public'
  AND table_name='ggman_devices'
  AND column_name='ranking_opt_in';

SELECT routine_name
FROM information_schema.routines
WHERE routine_schema='public'
  AND routine_name IN ('ggman_record_account','ggman_get_personal_stats')
ORDER BY routine_name;
```

Then run one real client acceptance: enable anonymous ranking in “我的 GGman”, connect League once, verify a hashed row appears in `ggman_account_history`, and confirm the UI receives only aggregate ranking results. Never paste service-role/server API keys into the client to bypass a migration/RLS problem.
