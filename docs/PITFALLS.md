# FACM Pitfalls

## Do not confuse 4.x history with the current product

FACM 4.x is retired from the default working tree. A bug, constraint or build rule that existed only in 4.x must not be described as a 3.5 bug. If old Git history is consulted, first verify the behavior exists in current `src/FACM`.

## Do not reintroduce heavyweight PetHost embedding accidentally

Normal 3.5 builds must not embed `FACM.Resources.PetHost.zip`. A stale `out/PetHostBundle.zip` is not a reason to change output. `FACM.PetHost` is built/self-tested separately.

If FACM.exe suddenly grows dramatically, inspect `IncludePetHostBundle`/`RequirePetHostBundle`, embedded resources and release scripts before changing product architecture.

## Do not revive the old publisher

The current publisher is `publish-3.5-lightweight.yml` and the file request is `release/3.5-request.json`. The retired `publish-release.yml`/`release/request.json` path produced a conflicting heavyweight package.

Never reuse an existing version tag for new release bytes.

## Do not multiply Gameflow polling loops

League automation, UI and presence should consume the shared monitor. Adding a “faster” second timer can create races, duplicate requests and higher LCU load.

Improve perceived latency by reacting immediately when a phase is observed and by using the correct shared cadence.

## Do not turn configurable delay into an unbounded sleep

Akari-style auto-match/auto-accept delays belong inside the existing phase-bounded matchmaking controller. A Lobby start delay must be cancelled as soon as Lobby ownership/settings change; an accept delay must be cancelled as soon as ReadyCheck/settings change. Never implement these options with a detached background timer or a second Gameflow observer.

The default remains zero delay. User values are clamped in shared `AppSettings` (minimum party 1-5, matchmaking wait 0-60 seconds, accept wait 0-15 seconds), and changing them reuses the same episode fences so a settings toggle cannot duplicate an ambiguous write.

## Keep compact numeric-unit copy localized

Small automation controls still participate in the UI text contract. Do not hard-code unit suffixes such as Chinese `人` / `秒` in a Form merely because they are one character. Put the complete localized label in the feature text catalog (for example `自动排队最低人数（人）` or `接受对局延迟（秒）`) so custom language catalogs and the UI copy gate stay authoritative.

## Do not add arbitrary first-action sleeps

Lobby/ReadyCheck previously felt slower because of fixed initial delays. If an endpoint may lag behind Gameflow, use the phase-bounded observer/retry path rather than sleeping before every first attempt. The optional Akari-style delays are explicit user policy; they must not become hidden mandatory latency.

## Do not mark writes successful before they are known to be successful

A stable-Lobby fingerprint or ReadyCheck episode fence must protect against duplicate concurrent writes, but a true failure still needs a recovery path.

For ambiguous matchmaking writes, reconcile authoritative queue state before retrying. For ReadyCheck, reconcile final local response before repeating an accept.

## Do not treat HTTP 2xx as the only postcondition where the write can outlive the response

LCU can apply a request and the client can still see a timeout/reset. Blind retry then duplicates the action. Use postcondition reads only on failure/ambiguity so the normal success path remains fast.

## Do not confuse “close lobby” with “leave current Champion Select”

The legacy `close-lobby` efficiency action terminates LeagueClient/LeagueClientUx processes. It is not a safe implementation of “退出当前选人但保留大厅/队伍”.

Runtime Companion uses a dedicated team-builder quit writer. Official matchmaking episodes in the 2026-09/10 logs repeatedly returned HTTP 400 while practice/custom samples succeeded. A one-time `POST /lol-gameflow/v1/session/request-lobby` fallback is permitted only after a definite 400 with an observable original party ID/roster and fresh ChampSelect phase. A 2xx or any lobby existence is not enough: require settled Lobby, no ChampSelect session, and the same party ID and members. Otherwise fail closed. Never kill the League client, delete or recreate the lobby. Live Tencent matchmaking behavior remains unverified by offline tests; dodge penalties remain game-owned.

## Do not restore UI visibility you did not hide

Gameflow suppression owns only its own restore. If the user had already hidden the desktop entry, leaving the game must not force it visible.

The first InGame transition may still close a transient control center; repeated InGame heartbeats must not repeatedly close a control center the user explicitly reopened.

## PetHost active is not the same as visible

PetHost may be alive while hidden. Desired visibility must survive startup races. Do not stop/restart the process merely to hide it during a match.

## Mayhem percentage values are percentage points

A value such as `53.5` means `53.5%`, not `5350%`. Do not multiply by 100 in the display layer.

## Preserve the fast Mayhem path

The current cache/network/service path is intentionally retained. UI fixes should not trigger a rewrite or extra normal-path network requests unless a concrete functional defect requires it.

## Do not enrich the same ARAM balance twice

`RiotGameDataService.EnrichAsync` already starts the bounded `OpggAramBaseBalanceService` task in parallel with visual metadata. Calling the same balance service in `MayhemAutomaticGuideService` immediately before Riot enrichment looks harmless when a complete result hits the ten-minute cache, but an unavailable result can cause a second external attempt and lengthen the visible failure path.

Keep one automatic-guide enrichment owner. The Runtime Companion should render the resulting `BaseBalanceSummary`; it must not create another balance fetch loop merely because the data has a new visible section.

## Bench availability is not proof of ARAM Mayhem

Ordinary ARAM and ARAM Mayhem can both expose Bench. Starting the full Mayhem guide whenever `benchEnabled=true` leaks Mayhem-only augments/data into normal ARAM and wastes external requests. Classify the queue first: current Runtime Companion policy recognizes base ARAM separately from global/CN Mayhem queue identifiers and mode tokens, and unknown Bench queues fail closed.

Base ARAM should wait for the matching Build Advisor version before requesting its balance-only supplement so patch mismatch remains visible instead of silently accepting stale values.

## Owner-drawn UI must repaint deterministically

Transparent/low-alpha idle backgrounds can leave stale text pixels after state changes. Idle owner-draw backgrounds should cover prior content deterministically.

For compact windows, apply final geometry before the first `Show()`; showing a full historical size and cropping afterward can leave desktop compositor artifacts.

## Per-monitor transient windows must not trust pre-Show 96-DPI geometry

FACM is PerMonitorV2-aware. A transient borderless window can have the correct logical WinForms size but a different physical size after its handle is created on a 125/150/200% display. Computing upper-right placement from the logical width before `Show()` can therefore leave a Runtime Companion clipped or restore it to the wrong place.

For the Runtime Companion, let its normal `Shown` path establish DPI-scaled geometry, then fit/clamp the physical bounds before the surface becomes visible to the user. Saved monitor coordinates may legitimately be negative when a display is left of the primary monitor; negative values are not corruption. Clamp to a current `Screen.WorkingArea` when topology changes instead of resetting to `(0,0)`.

## Runtime Companion preferences must use the shared settings owner

Do not call `AppSettings.Load()` from the transient Form and do not create a companion-specific INI/JSON file. That creates two mutable settings objects and can overwrite unrelated changes when either copy saves.

Reuse the already-initialized `SettingsModule.Settings` object and its normal atomic/LKG save path. Restore pin/collapse through the Form's existing behavior owner so `TopMost`, internal state, glyphs and tooltips cannot drift apart.

## Updater migration residue is not needed

Current 3.5 updates use the embedded small updater for one-EXE replacement/rollback. Do not add back FACM 4 bootstrapper/manifest/migration arguments to solve ordinary 3.5 update issues.

## Online JSON is a compatibility surface

Clients may encounter older JSON with unknown properties. Removing a retired model property is safe when the serializer ignores unknown properties, but do not silently rename/remove fields still consumed by supported clients.

## Clean Git history separately

Removing files from `main` does not remove them from old commits, releases, tags or remote branches. History rewriting is a separate, higher-risk operation. Do not mix it into ordinary cleanup.

## Create the task branch before the first write

Do not use a placeholder file on `main` as a way to obtain a commit for a new task branch. Resolve the current `main` SHA first, create the feature branch from that SHA/ref, and only then write task files.

If an accidental no-op content change does land on `main`, reverse it with an ordinary commit and verify the resulting tree matches the intended prior tree. Do not hide the mistake with reset, force-push or history rewriting.

## Do not use IP or raw hardware identifiers as the cloud owner identity

Public IP is not a stable machine identifier, and raw MAC/disk/mainboard identifiers create brittle device fingerprints with little product value. GGman cloud ownership is the CloudBase anonymous-auth `sub`, reached through a random stable portable `device_id`.

Do not upload raw IP, MAC, disk serial, motherboard serial, LCU credentials or Riot login secrets to `ggman_devices`. A future recovery fingerprint, if added, must be privacy-minimized, hashed and secondary to explicit recovery/CloudBase ownership.

## Cloud sync is never a startup dependency

`CloudSyncModule` may prepare local identity during module initialization, but remote authentication/database work starts asynchronously after the UI host is ready. CloudBase timeout, quota exhaustion, RLS rejection or internet failure must degrade to a skipped sync; it must not prevent GGman, League features or the updater from starting.

Persistent cloud/user state belongs under `data`, not `runtime`. Cache/update cleanup may regenerate `runtime`; it must never erase the portable device identity.

## CloudBase owner types must match the existing subject contract

GGman CloudBase ownership uses the existing anonymous subject as a `TEXT` value. Do not add a foreign key from `ggman_* .owner_id` to `auth.users(id)` unless the existing table uses the exact same PostgreSQL type and the product contract explicitly requires that relationship.

CloudBase authentication can expose a subject type that does not match the internal `auth.users.id` type. For RLS and RPCs, compare the existing text owner column with `auth.uid()::text` instead of changing established GGman ownership tables merely to satisfy a foreign key.

## Renaming the release EXE can strand older updater clients

`v3.5.40` was built before the updater manifest validator accepted `GGman.exe`; it accepts only GitHub Release URLs ending in `FACM.exe`. Publishing a later manifest that points only to `GGman.exe` makes 3.5.40 report update metadata retrieval failure even when the manifest and Release are otherwise healthy.

Until the minimum supported version is intentionally moved above 3.5.40, each release must publish byte-identical `GGman.exe` and `FACM.exe` assets, keep the online manifest pointed at `FACM.exe`, and verify both public assets before enabling the manifest.

## Do not turn a retention feature into hidden identity collection

Personal stats need a stable way to deduplicate League accounts, but raw PUUID/account names, IP addresses, MAC addresses, disk serials and motherboard serials are not required for the product behavior.

- Derive account history keys locally with the portable random `device_id` and HMAC-SHA256; discard the raw PUUID after derivation.
- Keep local history local by default. Anonymous cross-user ranking is a separate opt-in switch.
- Ranking endpoints must return only aggregate statistics for the current authenticated owner; never relax table RLS to make global ranking easier.
- Do not create a second Gameflow/current-summoner poll loop. Capture identity from the existing Gameflow episode and retry only on existing state events when the first read is not yet available.
- Do not add SQLite/native dependencies solely because the data is called a “database”; preserve the single-EXE contract until a real query/scale requirement justifies a storage-engine migration.


## Legacy launcher overlay and DPI geometry (2026-10-09)

The compact control center was originally an 680px legacy form visually covered by a new launcher enhancer. The enhancer used reflection into private fields/methods, making ordinary UI changes fragile; favor explicit typed integration. When changing a FlowLayoutPanel from four narrow columns to two full-width columns, include the trailing tile margin in width calculations at every theme scale, and reserve extra height for pixel rounding. Match the container's visible region and background painter to the compact launcher; otherwise the old gradient/radius can show through on Shown/Resize. Preserve the floating-ball click/drag/tray and show-without-activation semantics. Visual acceptance still requires native Windows screenshots and DPI tests; a browser mock does not establish them.


## Workbench navigation and smoke contract (2026-10-09)

`LeagueHubForm` previously combined primary categories, a second-level tab strip and a conditional right dock; width heuristics and smoke tests assumed those specific layers. A UX change that removes a layer must update the smoke contract to validate the new invariant (all nine stable routes remain directly reachable and the main content has no persistent competing dock), rather than keeping dead width-clamp tests. Embedded child Form lifecycle matters: detach the `FormClosing` handler after a deliberate navigation switch and preserve the existing `ShowView(string, bool)` bridge signature for contextual routes. WinForms `FlowLayoutPanel` needs `WrapContents=false`, explicit child widths and `AutoScroll` for DPI-constrained or localized menus; rendering still requires a native Windows review.


## Embedded Player form geometry and match-result color (2026-10-09)

The old Player form used fixed 860x720 positions and a fixed footer, but the workbench embeds it with `DockStyle.Fill` at different sizes. When a legacy form is embedded, adapt to actual `ClientSize` rather than the original nominal form size or desktop resolution. Update footer controls, champion summary and virtual match ListView together; test minimum and larger content areas. Coloring an entire ListViewItem red/green obscures other important metadata, so use `UseItemStyleForSubItems=false` and style only the result cell. Keep virtual ListView mode and do not add a second query loop.


## Recommendation page hosted inside single-rail Hub (2026-10-09)

A legacy 900x700 recommendation Form has absolute-positioned cards, preview textboxes and bottom buttons. After embedding inside the new compact Hub, static bottom coordinates risk clipping, and theme adaptors that identify labels by their old `Top` position stop working when the layout changes. Keep scrollable content separate from a pinned action footer; use control-identity selectors for preview captions. Theme changes must reapply semantic action/result statuses rather than turning warnings and errors into generic accent text. Keep native keyboard access on checkable choices, and preserve dialog confirmation and LCU write ownership.


## Mayhem and ChampSelect chrome patch safety (2026-10-09)

`MayhemLookupLayoutPolicy` already supports one-row and stacked toolbar geometries; do not throw it away for fixed coordinates. Add tests for lower dimensions, including query/action overlap and progress/status/preview ordering, before changing user-facing styling. `LeagueChampSelectAssistantForm` has a non-activating transient episode and only appears once the Bench state is confirmed; introducing a general-purpose dialog, changing `ShowWithoutActivation`, or rearranging fixed sizes risks interrupting League play. Prefer palette/token and accessible-control changes without new polling, LCU writes, or inferred game state. Keep end-user branding GGman in error dialogs and exported filenames even where internal FACM compatibility identifiers remain.


## Narrow companion scroll cue must not perturb WinForms state or Gameflow (2026-10-09)

The companion hides native `Panel` scrollbars to keep a compact overlay; its long recipe/teammate/Mayhem content is still wheel-scrollable. A visual cue must be passive and outside the scrolling content, or it can accidentally contribute to `AutoScrollMinSize`/be translated by `AutoScrollPosition`. Keep the rail on a separate docked shell while leaving `_body` as the only scroll owner; the original form header remains a direct child for the existing `LeagueRuntimeCompanionWindowState` adapter's header discovery. Avoid new timers, data polling, write actions or different collapse/restore mechanics. Test pure thumb geometry and explicit no-overflow hiding rather than claiming live-client behavior from CI alone.


## Compact embedded recommendation footer overlap (2026-10-09)

The 3.5.53 Recommendation form wrapped content into a scrollable responsive body but kept a single-row 69px action footer at every width. At widths below roughly 420px its result label's 140px minimum could overlap the Refresh button; earlier smoke only covered 1/2/3-card columns, not the footer. In the 3.5.56 cross-page regression pass, footer layout became a pure width-to-rectangles policy, placing status above both buttons below 480px while keeping the desktop single-row footer unchanged. Regression checks explicitly enforce all control bounds, disjoint visible areas and footer heights across seven widths. A generic successful UI smoke does not imply small embedded buttons are readable or non-overlapping.


## Contextual workbench launch must not instantiate Dashboard first (2026-10-09)

`LeagueHubForm` registered a `Shown` handler that always created Dashboard. `LeagueHubUiBridge` previously appended a later `Shown` handler that switched to a requested route using reflection; thus direct links from the floating UI created and immediately closed an unnecessary Dashboard, potentially starting data work before cancellation. Instead, inject the validated initial route before the first Show and let the form's own lifecycle create just one child. Preserve default Dashboard fallback for regular and malformed requests, and cover the full nine-view route set in deterministic smoke.


## My GGman fixed-width panels inside compact Hub (2026-10-09)

The original `LeaguePersonalStatsForm` declared a 720px client width and positioned four 664px panels at x=28, with status at y=532. As an embedded child of the single-sidebar Hub, it can receive a narrower viewport and shorter height, causing clipped controls or a status line below the visible bottom. Blanketing the form with a wider minimum size does not help once `LeagueHubForm` removes embedded child minimum-size fences. Keep responsive panel/metric widths inside a single AutoScroll content host and test both the narrow minimum plus larger viewport widths. Do not modify account counts, anonymous identity, CloudBase permissions, ranking opt-in, telemetry state or preference persistence in a presentation-only patch.


## Update Center status can hide real failure or truncate signed-release notes (2026-10-09)

Before 3.5.58, `OnlineCenterForm` reused a roughly two-line fixed status label for arbitrarily long `release_notes` strings, hiding important changes; after failed download/installation its exception handler called `ApplySnapshot`, replacing the failure with a stale “update available” status. An initial empty metadata snapshot could also be labeled “already latest” without proof. Keep notes independently scrollable, distinguish metadata-not-checked/error/verified-available/verified-current states, and after installer errors show a persistent error badge/status even when the existing retry action is re-enabled. A failed refresh must discard stale actionable update metadata; do not change cryptographic verification to repair a UI feedback issue. Test pure availability/notes/error-snapshot contracts in the update smoke, then preserve public signer/SHA validation through the official publisher.


## Registered UI copy is required for update progress/status changes (2026-10-09)

PR #310 initially failed FACM UI Text Contract because newly modified `OnlineCenterForm` state assignments used direct Chinese literals for download progress, preparing the installer and ready-to-update feedback. The UI copy gate inspects changed Form lines, not merely whether an existing wrapper translates unrelated text. The remedy was to register `OnlineDownloadProgressFormat`, `OnlineInstallerStarting` and `OnlineUpdateReady` in both `UiTextKeys` and `UiTextCatalog`, then render through `UiTextRuntime.Text`. Keep new status strings in the repo's keyed text registry; check the **final head's** Windows Build and UI Text Contract before release, not earlier canceled or failed runs.


## First-use detection must predate AppSettings.Load and exclude legacy/recovery files (2026-10-09)

`AppSettings.Load` immediately saves a default settings file if no primary settings file exists. Therefore checking for 'new install' after it runs always returns false. Conversely, just checking for a missing current primary file misidentifies existing legacy installs or recovery-only users as new. Compute first-use eligibility in `SettingsModule.Initialize` before loading settings and require that all three config sources (current primary, recovery, legacy FACM path) were missing. Do not use a permanent welcome popup that appears on every version upgrade. The short guide reuses the actual launcher and its existing primary navigation owners. A newly launched gameflow-suppressed floating entry must not unexpectedly activate this panel; do not add polling/timers or new LCU owners to handle welcome states.


## Legacy League lookup side effects and tray Open toggling (2026-10-10)

Before 3.5.60, `MainForm.BuildTrayMenu` attached `ToggleMenu` to a tray command labeled “Open control center” and to tray icon double-click. That would close the launcher if it was already visible. The old compact `OpenLeagueMenu` also called `ShowShellGroup`, which called `ShellMenuGroups.FindGroup`; the latter queued the actual League Hub through a lookup side effect and returned null, so the former displayed “no available features” despite the queued navigation. Explicit Open commands now call `EnsureMenuOpenAndActive`, while the legacy League click directly calls `LeagueHubUiBridge.RequestOpen`. Group lookup must remain side-effect free and existing native menu contracts remain unchanged; a smoke checks that the retired League dropdown is absent while the More group still resolves.


## Riot server-persisted ESC settings and CloudBase device ownership (2026-10-10)

Riot's support documentation confirms `PersistedSettings.json` can represent server-persisted account settings. Merely copying `input.ini` does not reliably preserve the player's ESC keybinds, and writing while the League game/client is running may be overwritten later. Snapshot only known Config files, ask the user to close League first, preserve a complete before-restore local copy, validate SHA-256 for every file, and confirm actual behavior in a game before claiming persistence. Also, CloudBase anonymous `x-device-id` sign-in cannot independently prove a different PC belongs to the same player: do not promise cross-PC sync without a separately verified authentication/recovery design. SQL files in GitHub are **not** automatically deployed to CloudBase; verify the migration in the real console before claiming cloud RPC operational.
