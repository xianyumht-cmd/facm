# FACM Architecture

## Akari v1.5.1 invitation-wait policy (Draft #357)

`LeagueMatchmakingAutomationController` receives the native, default-on `autoMatchmakingWaitForInvitees` preference through `LeagueEfficiencyModule`. The same already-read LCU Lobby invitation states determine whether a Pending invite blocks queue entry. Disabling the wait does not bypass leader/start/real-member gates. Changing it during a Lobby episode cancels/restarts the existing phase-scoped observer, without a new loop or client. INI and recovery persist the flag; portable and cloud snapshots use a nullable field so restoring an older cloud snapshot retains Akari's default-on behavior. Quick Tools shares a single compact row for auto matchmaking and invitation waiting; saving cloud settings while the page is open refreshes both controls.

## Akari v1.5.1 queue-stop policy ownership (Draft #357)

`LeagueMatchmakingStopController` remains owned by `LeagueEfficiencyModule` and activated only through the shared Gameflow phase feed. Both optional stop strategies require the authenticated LCU search to remain `Searching` and use effective queue time excluding low-priority penalty. The fixed-duration policy reads current queue time once, delays until the outstanding configured duration and checks again at its deadline; it does not add a periodic one-second loop. The estimated policy retains its existing phase-scoped polling cadence. Immediately before any DELETE, both strategies reread the current search state and threshold. Cancellation, account/phase change and final readback stay with the existing writer; no additional client, socket or persistent worker is introduced. No real CN LCU timing or low-end performance acceptance has yet occurred.

## Login chat-appearance restoration and offline-lock ownership (Akari v1.5.1 Draft #357)

`LeagueEfficiencyModule` owns one `LeagueLoginStatusRestoreController` backed by `LeaguePresenceService` and the existing authenticated LCU presence writer. When either explicit opt-in login setting is enabled, the current Gameflow subscription starts a single cancellable chat-ready task. It observes `/lol-chat/v1/me`, waits a bounded two-second settle, reads again and independently compares signature and ranked display. Writes are **serialized** into the same presence document so the two automations cannot replace one another with stale snapshots. Conditional pre-write checks preserve intervening user/client edits; each mutation receives two post-write readbacks; no additional login polling owner, session pool or private data logging is added. Per-episode completion is reset on actual disconnect and explicit settings changes. Stored settings have validated queue/tier/division and single-line bounded signature values in INI/recovery, cloud and portable snapshots; disabling both removes all login-restore LCU traffic. To cover **concurrent** manual Presence-page edits and the independent offline controller, `LeaguePresenceService` also uses a shared process-wide asynchronous gate around its full-document read/conditional-check/mutation/PUT transaction. It releases the gate before post-write readbacks. The local synthetic multi-writer fixture tests that queued services cannot take stale reads during the first PUT and that manual availability, ranked fields and signature survive concurrent requests; this is a source guard, not yet an external-client transaction guarantee.

`LeagueOfflineStatusLockController` is a **separate optional presence consumer** on the same Gameflow event feed. It does not host its own timer, WebSocket or connection detector. Only with the persisted opt-in switch enabled, a connected Gameflow tick fetches the current chat availability once. A previously observed `offline` that changed to `away`/`chat` triggers fresh confirmation followed by an exact-availability-conditional, authenticated `PUT /lol-chat/v1/me` through `LeaguePresenceService`, with the existing two readbacks. A newly connected user whose first observed state is online is never forcibly put offline. Disconnect and disable discard the previous availability baseline and cancel in-flight I/O. This polling-sampled transition differs from Akari's direct chat event reaction, potentially missing transient flips; exact event-driven equivalence and Chinese-client timing are pending. Quick Tools owns the option; both controllers stop with `LeagueEfficiencyModule.Dispose()`.

## Optional native room-chat notice after scheduled friend invitation (Akari 1.5.1 draft)

`LeagueFriendInvitationController` optionally sends the pinned-source `celebration` notice to an existing `customGame` chat conversation **only after a verified-successful outgoing invitation**. The option is selected explicitly in the on-demand friend list and exists in memory for the current lobby only; it defaults off and does not enter cloud/INI. When disabled, it adds no chat reads or writes. When enabled, the controller fetches `/lol-chat/v1/conversations` once after success, requires exactly one validated `type=customGame` conversation, rechecks the unchanged party/self identity and cancellation state, and sends no more than one notice per attempted friend invitation. The already opt-in leader-handoff controller also uses the same read/write capability only after a successful leader promotion and an unchanged party/self identity check, never announcing an uncertain or rejected promotion. The dedicated `ILeagueLobbyNoticeWriteApi` uses `LeagueMatchmakingWriteApiClient`'s existing authenticated session/HTTP pool with an exact validated conversation path and bounded `celebration` payload; the generic matchmaking route allowlist is unchanged. No chat history or user identifiers are logged. The first outgoing-invite notice slice passed all four Windows CI workflows at `fee88131`; the leader-handoff follow-up is a separate unmerged source candidate pending CI and final live Tencent validation.

## Native scheduled friend invitation (Akari v1.5.1, Draft #357)

`LeagueEfficiencyModule` alone subscribes to the Gameflow owner and passes the same phase state to `LeagueFriendInvitationController`. The on-demand `LeagueFriendInvitationForm` fetches `/lol-lobby/v2/lobby` and `/lol-chat/v1/friends` for user selection, binding at most five PUUIDs to the current party ID and self summoner ID without disk/cloud persistence. Only while a queue exists and an LCU lobby may still exist (`Lobby`, `Matchmaking`, `ReadyCheck`), the controller evaluates at a three-second minimum cadence on that existing event; it owns no timer, socket, listener or second session provider. Every outgoing invitation revalidates the current lobby and online friendship against fresh LCU reads. `LeagueClientModule` delegates `ILeagueLobbyInviteWriteApi` to its existing `LeagueMatchmakingWriteApiClient` and session pool. This typed capability accepts only a positive summoner ID, constructs `[{"toSummonerId":id}]` JSON itself, and posts only to `/lol-lobby/v2/lobby/invitations`; the generic bodyless matchmaking writer is not widened. Lobby/identity changes, phase exit, cancellation and disposal clear ephemeral state. The compact floating window, production updater and signed release are unchanged. All four Windows CI workflows passed on code `a250a07d`; actual Tencent client behavior and measured local performance remain unverified.

## Native received-invitation and lobby-leader automation (Akari 1.5.1 draft, 2026-10-25)

`LeagueLobbyInvitationController` is owned by the existing `LeagueEfficiencyModule`, which receives one shared `LeagueDashboardModule.GameflowStateChanged` feed. When either switch is on, connected `None`/`Lobby` ticks trigger one serialized, cancelable request batch with a monotonic three-second minimum cadence; disconnected/non-lobby phases perform zero LCU reads. No independent timer, Gameflow monitor, session discovery or HttpClient is created. Received invites are filtered by `state=Pending`, `canAcceptInvitation` and an ASCII-safe invitation ID; canonical, strictly validated `<DEFAULT>`/game-type strategies choose `accept`/`decline`/`ignore` and optionally veto processing when `/lol-chat/v1/me` indicates `away` or is unreadable. The exact actionable record is fetched again before POST. Leader skip runs only in `Lobby` when `localMember.isLeader`, selects ready non-self non-bot non-spectator member first, and rechecks party identity, membership and readiness before promoting. Both decisions deduplicate action attempts per episode without leaking invite or summoner identifiers to logs. Typed POST paths are an explicit extension of `LeagueMatchmakingWriteApiClient`'s allowlist.

Local `AppSettings`/LKG, `CloudSettingsSnapshot` and portable preferences share bounded `LeagueInvitationStrategies` canonical JSON. The already scrollable `LeagueEfficiencyForm` hosts opt-in toggles and per-mode policy selectors; it does **not** affect frozen compact Glass/orb geometry. Fake-LCU regression coverage is invoked through existing league dashboard smoke and Windows performance smoke. Actual CN region LCU acceptance and CPU measurements remain unverified. The source-pinned `autoInvitation` online friend invitations are a separate future slice.

## Mode/role auto-select priority profiles and phased writes (Akari 1.5.1 draft continuation)

`LeagueAutoSelectProfiles` validates and canonically serializes bounded pick/ban lists, mode (`default/rift/aram/mayhem`), local assigned Rift lane, strategy and delay. It is stored as a single optional `AppSettings.LeagueAutoSelectProfiles` line; the previous simple hero preferences remain last-choice fallbacks. A native on-demand `LeagueAutoSelectProfilesForm` edits a copy and commits through `LeagueEfficiencyModule` only on Save. Main compact layout, orb, authenticated LCU owner and gameflow observer are unchanged. For a local active ChampSelect action, the existing controller resolves priorities for exact mode/lane then mode-default and global-default, checks the current LCU availability list once, observes manual selections and re-reads the current phase/action before every write. `just-show` submits `completed=false` once; `show-and-lock-in` performs a separate cancelable wait and fresh preflight before `completed=true`; immediate lock submits once. The stage timer is bounded and canceled by the existing phase lifecycle. This is a partial behavioral mapping of pinned Akari v1.5.1, not the final queue/custom/pick-intent/bench strategy implementation. Real Win10/Tencent tests are postponed until the full functionality pass by owner request, while deterministic CI remains mandatory.

## Phase-scoped native ChampSelect automatic pick/ban (Akari 1.5.1 partial parity candidate, 2026-10-25)

`LeagueAutoPickBanController` is a child of existing `LeagueEfficiencyModule`, not a new top-level observer. It consumes the already-published `LeagueDashboardModule.GameflowStateChanged` and uses the existing `ILeagueClientApi` session read path. A single 2-second `CancellationTokenSource` task exists only during connected `ChampSelect` and only with valid, explicitly enabled pick/ban champion IDs. `LeagueClientModule` implements a narrow `ILeagueChampSelectActionWriteApi` backed by the same authenticated `LeagueClientSessionProvider`: type `pick`/`ban` only, numeric action ID and hero ID, `PATCH /lol-champ-select/v1/session/actions/{id}` only. The controller checks timer `BAN_PICK`, first unfinished group, local active action, pickable/bannable ID list and a fresh pre-write session. It rejects conflicting manual selections, deduplicates action writes by episode and cancels on status/configuration/lifetime changes. Its single-target immediate-lock behavior is only the first part of the exact Akari v1.5.1 parity backlog; no separate continuous Gameflow monitor or Electron dependency is added. Sanitized `CompactTraceLog` reports bounded operation stages and HTTP status; no raw session JSON or private data enters logs. Windows 10/Tencent live acceptance and resource profiling remain prerequisites for production.

## Native Gameflow reconnect automation (Akari 1.5.1 parity candidate, 2026-10-25)

`LeagueEfficiencyModule` retains the single existing `LeagueDashboardModule.GameflowStateChanged` subscription. The new `LeagueGameflowReconnectController` is a finite, opt-in consumer of that event, not an LCU poller or second session owner. Connected `Reconnect` schedules a cancelable 10-second delay; the controller performs one shared-LCU phase GET immediately before the command to avoid a stale Gameflow event causing a write. A typed `ILeagueGameflowReconnectWriteApi` in the existing `LeagueClientModule` delegates to the reused authenticated `LeagueClientWriteApiClient`, whose allowlist now admits **only `POST /lol-gameflow/v1/reconnect`** for this action, with no request body. A returned 2xx means submitted, not verified game reconnection. Canceled/stale/unauthorized/non-2xx outcomes stop rather than looping; the next separate Reconnect episode may attempt again. `AppSettings.LeagueAutoReconnectEnabled` defaults false and is controlled from the existing Quick Tools form; the frozen compact UI is unchanged. Per-operation sanitized `CompactTraceLog` steps cover schedule, preflight, write and cancellation, with no raw LCU payload. Win10/Tencent-client acceptance and CPU/RAM measurement remain pending.

## Draft Akari 1.5.1 follow-up: shared owners, no new background loops (2026-10-25)

The compact `CompactRiftRankingService` consumes one mixed-lane public ranked HTML table through `LeaguePublicDataTransport`, rejects non-Ranked/unknown-patch responses, filters each row by the local player's validated `CompactRiftRoleService` position, and caches only complete `rift:<role>` snapshots. Its eight-second first-fetch deadline is a transport budget, not a UI lifetime: parent `CompactLivePilotController` cancellation still terminates stale work on selection/role/page transitions. `CompactLiveSourceProbe` reuses these service calls and records sanitized trace outcomes so CI can distinguish a complete mode-qualified result from a mere HTTP 200. The approved native compact geometry and render classes are unchanged.

Auto matchmaking's existing phase-bounded `LeagueMatchmakingAutomationController` still owns the only Lobby GET and queue POST. It now extracts `invitations[].state=Pending` from that same GET; when any invitation is pending, it does **not** claim the current Lobby fingerprint or send POST. The existing 3-second Lobby retry observes settlement, retaining exactly-once search for the same queue/member fingerprint. No new LCU session, task, timer, settings schema, UI, account storage or external transport is introduced. User preference to enable auto matchmaking remains mandatory.

## Experimental Glass workbench renderer (2026-10-21; opt-in)

`MainForm` is **unchanged** and retains floating ball, tray, mutex and all business entry points. `Program.Main` has a pre-module, pre-mutex preview-only branch guarded by `UseGlassWorkbenchPreview=true`, the `--glass-preview` argument and `GGMAN_GLASS_PREVIEW=1`. That branch opens only `Presentation.Glass.GlassPreviewForm`; it does not initialize host, CloudBase, LCU, League, update checks, account settings or persistent app settings. The official instance may run simultaneously without interference. The standalone frameless, modeless WinForms host initializes WebView2 only while its window is open. Its HTML is the unmodified owner-approved `Presentation/Glass/GGman-Glass-Prototype.html`; the .NET Framework 4.8 project embeds it only when `UseGlassWorkbenchPreview=true`. Its native UI also requires `--glass-preview` and `GGMAN_GLASS_PREVIEW=1`. WebView2 opens an `about:blank` string-document from the embedded content and blocks external navigation, popups, downloads and permissions. The only JS messages allowed are `close` and `drag`; no token, account, URL, filesystem, LCU or CloudBase API is exposed. On closing the form, the WinForms-owned WebView2 control is disposed normally; verify the browser process tree actually exits on native Windows, rather than assuming disposal proves it. Browser userdata is isolated under LocalAppData/FACM/GlassPreviewWebView2.

The prototype has demonstration-only switches, match history and mock CloudBase/League statuses. The full existing WinForms LeagueHub and services continue to own all functional routes and mutable operations. A future true data and command adapter requires separate design, token validation, permissions and user acceptance. Whole-process-tree performance (not just GGman.exe), Win10 19045 rendering, DPI/IME/focus and true desktop backdrop are not yet verified.


## B5 extended visual owner boundaries (Draft PR #351)

`CleanupRepairForm` remains a `CleanupModule`/MainForm-owned task surface and adjusts existing help/status typography only. `GgmanCaptchaForm` keeps token/challenge state and disposable image lifetime while replacing visual button/field geometry. `GgmanStatsPrivacyForm` reparents only existing opt-in controls to a shared noninteractive `FacmGlassPanel`; privacy defaults and save/import authorization remain external. `LeagueProfileCustomizationForm` reparents already constructed native input/action controls into four local semantic surfaces; each control retains the original click/event subscription and cancellation generation. `LeagueItemSetForm`/`LeagueBuildApplyForm` reuse `FacmDesignSystem` and `FacmActionButton`; the latter's auto-toggle/status callbacks are critical and were explicitly restored after an intermediate CI failure. Both pet pickers retain asset download/activation and animated preview ownership and use theme-derived text/surfaces instead of a separate dark palette. `LeaguePersonalStatsForm` only changes nonfunctional type hierarchy. No new transport, persistent state or background service.

## B5 cleanup review and minor operation dialogs (Draft PR #351)

CleanupReviewForm retains its SafeCleanupService-supplied immutable plan, blocked/deletable row generation, explicit Confirmed/Cancel behavior and native keyboard access. Visual colors now reference `FacmDesignSystem` semantic roles instead of process-wide hardcoded dark static fields; the destructive confirm is visually distinguished through `FacmActionButton`. BackgroundOperationDialog retains its worker continuation and no-close-before-complete invariant while inheriting canonical text/surface tokens. GameLocatorSearchDialog retains its CancellationTokenSource and progress ownership but uses the canonical text and native semantic cancel button. Theme changes do not alter cleaning authorization or task execution semantics.

## B4 reusable presentation surfaces (2026-10-17, Draft PR #351)

`DesktopLauncherEnhancer` preserves four route bindings, onboarding and context state while styling its tiles with always-visible semantic surfaces and using standard ActionButtons for footer operations. `ThemePickerForm` keeps the same preview ThemeDefinitions and Accept/Cancel contract, but uses native reusable ActionButtons, paint-time font disposal and focus/contrast feedback. `OnlineCenterForm` changes only fixed dialog geometry/reading hierarchy; mandatory update guards, installer and announcement link ownership are unchanged. `GgmanAccountForm` retains the passwordless email challenge/auth state machine and session isolation; login/session cards are now more readable. `EscSettingsForm` wraps only the two visible local settings actions in a separate native GlassPanel; hidden cloud/probe controls remain hidden, service actions untouched. These are presentation changes; native pixel/DPI/keyboard proof has not been collected.

## B3 native strategy, match and recommendation surfaces (Draft PR #351)

`MayhemLookupForm` keeps its asynchronous OP.GG/Riot enrichment, bitmap rendering, copy/save and cancellation unchanged. The search/status controls are visually grouped behind a semantic surface; the scrollable preview sizes its PictureBox to the reading viewport minus stable vertical-scrollbar reservation and preserves scroll position on resize. `MayhemLookupLayoutPolicy` owns geometry, toolbar enclosure and preview-width smoke assertions. Semantic status roles survive theme switches. `LeagueLiveForm` and `LeagueBuildAdvisorForm` retain active League polling, player rows and recommendation ownership but draw canonical semantic surface/text roles and use reusable ActionButtons. `LeaguePlayerForm` adds a styled account summary overlay without changing virtual match rendering or service cache. `LeagueRecommendationForm` retains selection/auto-apply and data reading flows while correcting theme contrast and action typography. All changes remain presentation-owned; full native accessibility/DPI and screenshot comparisons have not been performed.


## B2 Presence and automation visual surface migration (2026-10-17, Draft)

The active `LeaguePresenceForm` retains its original services, option values and click handlers, but now arranges existing native controls within full-width, scrollable, semantically grouped status, signature, ranked and profile tasks. The resize handler controls row geometry, preserving its edit/action ownership. `LeagueEfficiencyForm` keeps its 18-row settings and handlers while promoting existing contextual help from hover-only to visible descriptions within hotkey, automation and parameter rows. The shared `LeagueSoftGlassSkin` legacy compact-density pass excludes both migrated surfaces; its explicit efficiency scrolling calculation remains active. Existing CloudBase/game/update ownership is unchanged. Keyboard, theme/DPI and visual evidence remain outstanding.

## B1 game-repair native presentation prototype (2026-10-17, Draft)

The existing `LeagueGameRepairForm` retains its exact `LeagueGameRepairService` and `LeagueEfficiencyModule` action handlers. Presentation is now locally owned by one scrollable layout with two task sections. Rows lay out native ActionButtons opposite readable title+hint content or stack when width is constrained; update messages flow into a single semantic result surface. `LeagueGameRepairLayout.ValidateForSmokeTest` checks layout breakpoints in `shell-ux`. `LeagueSoftGlassSkin` excludes this migrated page from legacy compact-density mutation without altering other pages. There is no new game or network owner, polling loop or background service. Native visual, keyboard, DPI and performance comparisons are outstanding.

## Precision Workbench B1 presentation ownership (2026-10-17, Draft only)

The persistent `LeagueHubForm` retains nine route IDs, a grouped navigation rail, existing child factories and guarded child disposal. Its navigation text and row rhythm now follow the first pilot typography scale. `LeagueDashboardForm` owns its local measured layout and one vertical scroller, reusing the existing data fields, `LeagueDashboardPhaseService`, `LeagueDashboardDetailsService`, timer and cancellation owner. Overview fields reflow from two columns to stacked status; metadata remains the same four rows with labels stacked on narrow canvases; Refresh and Close are the same native actions. No fake metric tiles or network refresh from layout handlers.

`LeagueSoftGlassSkin` excludes the Dashboard from legacy density mutation while retaining legacy page behavior. `FacmThemeRuntime` remains the global theme owner, and the two pilot forms subscribe only for scoped font/geometry refresh and unsubscribe on FormClosed. This is not a new theme engine or release feature. Native DPI/idempotence acceptance is not yet established. See DESIGN.md and the B1 operations matrix.

## Replacement-rule selection identity (PR #350, 2026-10-17)

The editor uses case-insensitive search but mode-specific selection identity: plain text keys are `OrdinalIgnoreCase`, replacement rule keys are `Ordinal` to match their persisted dictionary. This distinction applies to list rebuilds after Save/Discard navigation so `Foo` and `foo` cannot be interchanged.

## Editor list rebinding after save and conflict discard (PR #350, 2026-10-17)

`UiTextEditorPanel` keeps its rendered list entries consistent with `_draft` after a successful single-key Save, including the nonrefreshing `SaveLocal(false)` branch used by Save/Discard/Cancel navigation. When a pending selection change is resolved, `Filter` rebinds the requested key against the resulting profile before rendering the destination entry; it must not render a `next` object captured before the save. Discard rereads persisted state without writing or notifying cloud sync, refreshes visible values and rebuilds selection on navigation. A failed reread blocks leaving and preserves the visible draft. This addresses delayed/missed restore notifications without broadening the cloud write policy.

## UI text editor transaction and cloud restore consistency (PR #350 candidate, 2026-10-17)

`UiTextCustomizationStore.PrepareSingleEdit` is a pure candidate constructor: it compares the target key between the editor's captured baseline and the current persisted profile, rejects same-key divergence, clones current state and validates a single text/rule edit or reset. `UiTextEditorPanel` writes that independent candidate first, then updates its saved baseline and emits `NotifyUiTextSaved`; a validation or filesystem failure leaves its baseline untouched, so Discard has no hidden stale edits to publish. Normal UI editing still persists through the existing atomic, backed-up `Apply` path.

`GgmanAutoSyncService.RestoreText` performs the existing profile apply and then asks `UiTextEditorPanel.NotifyExternalRestore` to reload an expanded editor with no visible pending edits. Any attempted Save/Reset independently reads the latest profile, so unrelated changes survive if the editor was not refreshed. Same-key external changes veto local persistence instead of silently overwriting the remote-authoritative choice. This does not change CloudBase schema, CAS or registered authentication.


## League workbench lifetime and UI-text draft ownership (P1 correction candidate, 2026-10-16)

`LeagueSoftGlassSkin.Apply` supplies a persistent chrome policy specifically for `LeagueHubForm`; quick launcher and other transient surfaces keep their separate existing ownership. `LeagueHubForm` asks the embedded `LeaguePersonalStatsForm` to resolve editor draft state **before** replacing or disposing its child view and during FormClosing. `LeaguePersonalStatsForm` delegates to its `UiTextEditorPanel`; the editor itself guards content transitions (selection, filter/mode, new rule and collapse) and only calls `UiTextCustomizationStore.Apply` / `NotifyUiTextSaved` for an explicit Save choice. Draft loss protection is distinct from `GgmanAutoSyncService`'s remote-restore deferral.

Shared `FacmActionButton` derives text contrast from the effective painted enabled fill; `FacmDesignSystem` owns the contrast calculation. Normal native focus/cancel semantics are retained for the cleanup preview. This is a candidate change, not a claim of verified Windows runtime acceptance.


## Cloud-first selection for registered software and UI text (3.5.77)

`GgmanAutoSyncService.DecideCloudFirst` is the single selection function for `ggman_registered_app_settings` and `ggman_ui_text_profiles`. A non-empty registered remote row takes priority when local differs, including first login, restored versions and different prior owners. The automatic upload exception requires a deliberate `AppSettings.Saved` or `UiTextEditorPanel` local-save notification **and** a matched prior remote version+fingerprint checkpoint belonging to the same GGman UID. CloudBase RPC CAS remains the authoritative rejection of concurrent changes. Missing server rows are never background uploaded: the existing config center surfaces a `创建云端备份` action for the missing app/text category, which rereads the remote row before attempting initial CAS. An unsaved visible UI text edit defers that category's restore without blocking other game sync. `GgmanUnifiedSyncPanel` no longer presents ordinary differences as two equivalent conflicting versions; local app path, geometry, identity, token, and privacy settings remain unsynced.

## Manual publication when the League launcher remains running (3.5.76)

`GgmanUnifiedSyncPanel.PublishGameAsync` shows a deliberate overwrite confirmation and invokes `GgmanAutoSyncService.PublishGameProfileAsync`. That service refuses capture only if `GameProfileStore.IsGameProcessAbsent()` is false, not when `LeagueClient` / `LeagueClientUx` exists. `GameProfileStore.CaptureForUpload` repeats the actual game-process guard before and after reading ESC and the allowlisted lobby YAML leaves, preventing a fresh game startup from yielding an unchecked partial snapshot. The existing registered CloudBase `GetAsync` + compare-and-swap `SetAsync` and error handling remain unchanged. A running/locked YAML file may still yield an explicit read error; do not silently upload incomplete game profiles.

## Live lobby account-specific game-profile application (3.5.75)

`GgmanAutoSyncModule` reuses `LeagueDashboardModule.GameflowStateChanged` and `LeagueClientModule.TryGetBytesAsync` to observe the current signed-in LOL summoner once every few seconds while Gameflow is connected. The PUUID is hashed in memory, never logged or included in the cloud profile. A changed hash or transition to Lobby/None/EndOfGame schedules the already-running `GgmanAutoSyncService` timer promptly, without a second Gameflow poller. The service keys in-memory completion separately for ESC and lobby YAML by GGman session, local installation, current LOL account fingerprint and cloud revision. `GameProfileStore.RestoreEsc` refuses only an **active game process**, while `RestoreLobby` applies the allowlisted YAML leaves independently with backups and local verification; locked/unavailable YAML is retried and cannot block ESC. Registered CloudBase schema and manual-upload CAS remain unchanged. Native LCU writeback effectiveness must be confirmed separately.

## Registered manual game profiles (draft PR #345, not deployed)

`GgmanAutoSyncService` retains the registered account session and 90-second polling owner. Software preferences and `ui-text.ini` use automatic CAS syncing; game presets use a separate **manually published** registered-profile contract and never auto-upload from local ESC/YAML changes. `GameProfileStore` owns the exact YAML boolean allowlist, native game-folder mapping, snapshot validation, local backups and fail-closed process-absence checks. `GgmanGameProfileCloudClient` uses the existing registered token-refresh path with operator-deployed SQL 009 RPC CAS and account RLS. The `GgmanUnifiedSyncPanel` has an intentional combined upload action and a separate game-auto-restore checkbox. Restore while League is running is deferred; there is no claim of live LCU account-switch apply. Historical SQL 005 ESC snapshots remain untouched. Operator SQL 009 deployment, 11/11 read-only policy checks, rollback-only role-simulated A/B tests and four real HTTP 401 anonymous-denial tests have completed. Real registered bearer read/write and gameplay account-switch acceptance are pending; draft branch is not released.


## Runtime Companion guide evidence and layout

`LeagueRuntimeCompanionController` publishes selected hero guide snapshots to `LeagueRuntimeCompanionForm`. `MayhemAutomaticGuideService` resolves the hero and enriches `OpggMayhemService` with champion art, item builds, ARAM balance and augments. A selected complete current-patch top-ten snapshot is separate from the per-hero detail source; known old hero data is discarded but patchless hero data remains explicitly unverified. The selected top-ten supplies current stats only for heroes actually listed. `MayhemRankedAugmentService` tries OP.GG structured/rendered rows, then an existing bounded ARAMMayhem alternative when fewer than three entries are parsed; a richer list replaces the partial list, never an equal or smaller one. The WinForms companion keeps its fixed compact outer size and scrollable content, but sizes the base-ARAM title and status as a full-width block with measured bounded vertical height and denotes sparse augments, rather than clipping long content.

## Mayhem champion ranking source selection

`OpggMayhemService` concurrently reads Hexdata's CN-derived hero list, ARAMMayhem's hero/homepage, ARAMGG's public champion list, OP.GG's hero build and the existing Tencent official patch-note reader. `MayhemRankingSourceService` checks per-site patch labels and whole-list integrity, then selects the newest complete ten-entry snapshot. The source note identifies the ranking provider and patch (and warns when it differs from verified Tencent patch). Champion-specific build and augment enrichment retain their original independent service owners; the ranking selection does not reinterpret or average region-specific win rates. The live source probe is distinct from source-independent deterministic parser/selection fixtures. The six-hour GitHub Actions source probe performs additional bounded, parallel provider checks and records per-source patch, valid row count, and Tencent official-patch comparison in its job summary; the user-facing lookup does not gain a new polling loop. An unavailable Tencent patch is reported as unknown, not as a matching release. CI warns when complete/current ranking redundancy drops below two sources, while the end-to-end card smoke remains the actual fatal gate.

## Registered session renewal

`GgmanAccountSession` owns the process-memory registered login generation, access token and refresh token. Registered app settings, UI text, ESC profile and personal ranking RPC clients route their calls through `ExecuteWithRefreshAsync`: verify the original login generation, attempt the RPC, and only on HTTP 401 use a serialized `auth/v1/token` refresh before retrying exactly once. The refresh response must keep the exact original registered UID; a logout or new login makes stale continuations fail without updating current credentials. Cloud data revision compare-and-swap remains the authority for writes. Anonymous `CloudBaseClient` identity stays isolated.

## Product boundary

The maintained product is FACM 3.5.x lightweight: **WinForms + .NET Framework 4.8 + one FACM.exe**. The repository intentionally avoids restoring the retired 4.x WinUI/Core/Infrastructure/Platform/bootstrapper architecture.

## Solution

`FACM.sln` contains four current projects:

- `src/FACM` — main WinForms application.
- `src/FACM.ToolBundle` — validated embedded tool resources.
- `src/FACM.Updater` — small Windows updater used for atomic EXE replacement and rollback.
- `src/FACM.PetHost` — optional .NET 8/WPF VPet runtime source, validated separately.

The normal 3.5 build embeds ToolBundle but does **not** embed a self-contained PetHost ZIP.

## Main host

`Program.cs` creates one `FacmHost` and registers small modules around the existing 3.5 services. `ShellModule` owns the primary `MainForm`; the compact menu, tray and floating entry remain WinForms surfaces.

The module layer is an ownership/lifecycle boundary, not a separate 4.x application architecture. Do not split the product into a new Core/Infrastructure/Platform stack without a concrete 3.5 requirement.

## Cloud identity and sync

`CloudSyncModule` is a fail-soft consumer of Tencent CloudBase. It does not own UI and must never block GGman startup.

- `RuntimePaths.DataDirectory` is the application-local persistent data root; it is distinct from regenerable `runtime` data.
- `CloudIdentityStore` keeps a random stable `device_id` plus the CloudBase anonymous user id in `data/cloud-identity.json` and a last-known-good copy. Access tokens and refresh tokens are process memory only and are never persisted.
- `CloudBaseClient` uses the existing .NET Framework `System.Net.Http` stack and fixed HTTPS gateway for environment `ggman-d4gioqqcz434d9e4d`. Anonymous auth uses `x-device-id`; PostgreSQL requests use the returned access token.
- Remote work starts once from `Application.Idle` after the normal host/UI initialization path. Network failure, CloudBase downtime, auth failure, or RLS rejection is logged as a fail-soft skip and cannot disable League or other local features.
- P1 writes only the `ggman_devices` record (`device_id`, app version, OS version, last-seen timestamp), omits `owner_id`, and verifies the read-back owner against the authenticated CloudBase `sub`.
- PostgreSQL RLS remains the data-ownership boundary. Client code must not receive a service-role/API-key credential and must not supply `owner_id` itself.
- Settings sync, account-history sync, telemetry upload, recovery-code/hardware-fingerprint matching, and local SQLite are later scopes, not implicit P1 behavior.

### Automatic per-category registered configuration sync (shipped 3.5.67)

`GgmanAutoSyncModule` depends on `SettingsModule` and owns `GgmanAutoSyncService` for the whole application lifetime, not the My GGman page lifetime. A 5-second initial or post-login wakeup and periodic background polls reconcile the existing registered CloudBase RPCs for portable app settings (008), customized UI text (007) and ESC (005). Local `AutoConfigSyncEnabled` defaults on and is never included in portable cloud preferences; a single checkbox in `GgmanUnifiedSyncPanel` switches background behavior, with no usual manual cloud upload/restore buttons. The panel shows exceptional per-category conflict resolution only when a user must choose which side survives.

The service stores **only** per-category cloud revisions/content fingerprints and hashed owner identity in `data/account-auto-sync-state.json`. Each category also records the most recent local-account owner, preventing A/B sign-in on the same PC from silently sending A's local data into B's CloudBase row. Unchanged snapshots are no-ops, only local changes upload with CAS, only remote changes trigger a backed-up restore, concurrent changes pause that category. First login into a different cloud profile restores stock app preferences or empty text overrides but does not overwrite existing customizations without a conflict choice. ESC fingerprint ignores snapshot creation timestamps. Unsaved live text editor input is fenced, missing game directories skip ESC, and logout/disable cancels future work best-effort. All three categories remain independent; the worker does not promise atomic cloud transactions.

### Consolidated account configuration center (historical 3.5.66; superseded in 3.5.67)

Version 3.5.66 introduced `GgmanUnifiedSyncPanel` inside `LeaguePersonalStatsForm` and consolidated three manual cloud upload/restore workflows. It also added the registered-only portable preferences contract in SQL 008 and disabled anonymous software-settings auto-restore. **That manual button pair is no longer the current UI.** In 3.5.67 the same page hosts one default-on auto-sync checkbox backed by `GgmanAutoSyncService`, as specified above. Existing local ESC backup/restore and UI-text editing controls remain available; `LeagueClient/Config` YAML remains excluded.

### Registered account personal statistics migration (shipped in 3.5.64)

`LeaguePersonalStatsModule` continues storing local account history in `PersonalStatsStore` independently of CloudBase availability; it no longer invokes the anonymous leaderboard and enables local launch/account recording without old user-facing toggles. `GgmanRegisteredStatsClient` sends registered bearer-protected RPC requests for profile touch, per-UID deterministic account hash observation, explicit legacy count/day import, profile visibility and aggregate rank read. `GgmanStatsPrivacyForm` owns the import acknowledgement and ranking opt-out and retains existing `UsageTelemetryModule` consent (still separate/anonymous). The cloud migration `006_registered_personal_stats.sql` defines registered-only RLS tables and a tightly scoped aggregate-only SECURITY DEFINER function, leaving old device tables untouched. No user account names, raw PUUIDs, access tokens or device IDs are copied into registered statistics payloads. The operator applied SQL 006, verified RLS and catalog access, mock-role A/B isolation, and real no-credential/anonymous gateway rejection; v3.5.64 is signed and online. Real registered A/B and cross-PC field acceptance remain outstanding.

### Registered account UI (shipped in 3.5.61)

`LeaguePersonalStatsForm` embeds an optional account entry inside the League Hub. It creates `GgmanAccountForm` as an owned modal on the visible top-level host; the modal uses shared WinForms theme primitives, keeps signed-out email/OTP controls separate from the signed-in account management summary, and closes only after verified session establishment. The caller then refreshes the account-entry label. Registered tokens remain solely in `GgmanAccountSession` memory; closing the modal does not log out or change device-anonymous CloudBase clients, and (in 3.5.61) registered-only manual ESC cloud upload/restore was available after sign-in. Since 3.5.67, cloud configuration uses the background auto-sync worker described above. Failed verification leaves the dialog open; explicit logout calls the existing CloudBase sign-out endpoint before clearing the in-process identity.

### ESC real-gateway permission check (shipped in 3.5.61)

`GgmanEscCloudClient.CheckReadOnlyAccessAsync` uses the in-process registered account session to verify real RPC read access and separately probes the same read route without any bearer. `EscSettingsForm` exposes this manual non-mutating diagnostic and shows only success/failure plus the current snapshot revision. It does not validate a second registered principal or an authenticated anonymous principal; those are later external acceptance gates. No telemetry, persistence of auth tokens, background polling or direct write/restore is performed by the diagnostic.

### WeGame ESC game-config resolution correction (3.5.63)

`EscGameDirectoryLocator.FindValidatedConfig` tests an installation's `Game\\Config` before a legacy root `Config` and requires an actual allowlisted ESC file. Its parent-climb resolves game process, LeagueClient launcher, installation root and stale launcher-Config hints to the same in-game folder, while excluding `LeagueClient\\Config` YAML preferences from ESC. `EscSettingsBackup.FindConfigDirectory` first uses this same resolver before its legacy local fallback. A deterministic smoke fixture reproduces both WeGame Config folders. Cloud payloads, account RPC and active-game file handling are unchanged.

### ESC My GGman account-page controls and path resolution (3.5.62)

The existing `EscSettingsForm` is reused as a borderless non-top-level child surface within `LeaguePersonalStatsForm` below its privacy panel. The Workbench remains the sole navigation owner; old independent Settings/tray More entries are removed. The ESC surface runs a one-shot, cancellation-aware `EscGameDirectoryLocator` on show, using League process image path with WMI fallback, the configured game path, and registry uninstall hints in that order; all candidates are validated to actual local League Config directories without full-disk scanning. `EscSettingsBackup` no longer queries League process state before snapshot/recovery, but retains real file integrity/permission checks, local fallback and best-effort rollback. Disk restore while game is open is supported but game runtime state may overwrite it later. Existing registered account boundary, database RLS and cloud request protocol remain unchanged.

### ESC configuration snapshots (3.5.61 historical workflow; superseded by 3.5.67 automatic cloud sync)

`EscSettingsBackup` owns allowlisted League `Config` snapshot validation and local recovery. In 3.5.61, `EscSettingsForm` exposed manual local backup/restore and a signed-in-only cloud path from Settings/tray More. The cloud entry later moved to My GGman and is now automatic in 3.5.67; manual **local** backup/restore is retained. Registered `GgmanEscCloudClient` obtains the in-memory account bearer from `GgmanAccountSession`; it never reuses the anonymous `CloudBaseClient` session. CloudBase invoker RPCs use authenticated role, `auth.uid()` RLS and compare-and-swap `version` updates; direct REST callers cannot select an owner ID. The operator installed registered-only `005_esc_profiles.sql` and validated schema/RLS catalog grants; no-credential and anonymous-bearer live reads returned 401. Real registered A/B HTTP ownership and Riot restore remain field-verification items. See `docs/ESC-SETTINGS-BACKUP.md`.

### Personal stats and anonymous ranking

`LeaguePersonalStatsModule` is an event-driven consumer of the existing League Gameflow owner. It does not create a second phase poller. On connected Gameflow state changes it may read `/lol-summoner/v1/current-summoner`, keeps the last captured account hash as an episode fence, and records only when the observed account changes. It derives a device-scoped HMAC-SHA256 account key from the local random `device_id` and PUUID, then discards the raw PUUID.

- `PersonalStatsStore` persists long-lived local history under `data/personal-stats.json` with a last-known-good recovery copy. It stores active calendar days, first/last use timestamps and hashed account records only.
- Local personal stats are enabled by default. Cloud ranking is a separate opt-in setting and defaults off.
- Cloud account history uses `ggman_record_account`; clients never send `owner_id`. The SQL function derives ownership from `auth.uid()`.
- Global ranking uses `ggman_get_personal_stats`, a SECURITY DEFINER aggregate that returns only the current user's account count, rank, participant count and percentile. It does not expose other users' owner IDs or account hashes.
- `ggman_devices.ranking_opt_in` is the population gate. Turning ranking off removes the user from the aggregate population while keeping local history intact.
- Cloud failures remain fail-soft. The local profile continues to work when the migration, network or CloudBase service is unavailable.
- Full settings sync, general product telemetry, hardware fingerprints and raw Riot identifiers remain outside this scope.

## WinForms design system

FACM UI evolution stays inside the lightweight WinForms product. `ThemeCatalog` is the palette source, `FacmThemeRuntime` owns the active process theme, `FacmDesignSystem` owns semantic colors/geometry/common product styling, and `FacmWindowChrome` owns the normal top-level FACM window shell.

Shared interactive primitives live under `src/FACM/Theming/` and must preserve native WinForms behavior:

- `FacmActionButton` keeps `Button` click/focus/keyboard semantics while rendering primary/secondary/danger states from the shared palette.
- `FacmToggleSwitch` remains a `CheckBox`; `Checked` and `CheckedChanged` are the behavior contract.
- `FacmStatusBadge` displays semantic neutral/accent/success/warning/error states without introducing page-local palettes.
- `FacmNavButton` and `FacmPillButton` remain native `Button` controls, are keyboard reachable (`TabStop=true`), and show focus cues from the shared palette. Fix navigation behavior in these primitives rather than adding page-local workarounds.

New or materially redesigned product surfaces should use these shared tokens/primitives instead of adding private `Color.FromArgb(...)` design systems. Theme changes must refresh already-open shared controls. Visual refactors must not change feature routing, update semantics or League read/write ownership merely to achieve consistency.

### Current UI migration state

The design system is intentionally **in transition**, not fully unified yet:

- `LeagueHubForm` uses `FacmDesignSystem`, `FacmGlassPanel`, `FacmNavButton` and `FacmPillButton` for its outer shell.
- `LeagueDashboardForm` uses a primary connection/Gameflow status surface plus compact metadata rows instead of a six-card equal grid; its actions and status tones use shared primitives.
- `LeaguePlayerForm` keeps its dense ListView/virtualization structure but now uses shared semantic colors and `FacmActionButton` rather than a page-local dark palette.
- `OnlineCenterForm` and other newer surfaces use shared semantic primitives directly.
- `CompactMenuForm` still contains a legacy private fallback rendering layer (`ThemedPanel`, `ThemedButton`, gradient/theme decorations, raw `ThemeDefinition` radii), but the normal visible control-center path is owned by `DesktopLauncherEnhancer`. The enhancer hides the legacy body, overlays a flat `FacmDesignSystem.Canvas`, normalizes the visible header, uses the shared `WindowRadius`, and renders launcher tiles/context state with one restrained shared accent. Treat the hidden fallback renderer as compatibility debt, not as an approved second visual system.
- Other older League forms must be audited individually for page-local RGB colors, private button styling and rigid fixed geometry. Migrate high-value user surfaces first rather than mechanically rewriting every `Color.FromArgb` occurrence.
- Historical `ThemeCatalog` styles remain for palette compatibility, but shared visible product surfaces should not revive large glass radii, decorative dual-accent gradients, generic equal-card dashboards or pill-heavy navigation.

The target direction is one restrained modern Windows desktop product language influenced by Fluent/PowerToys interaction behavior while staying native WinForms. External design skills may inform audit criteria, but Web-only implementation advice (CSS/React/GSAP/etc.) does not define FACM architecture.

## League runtime

League features share one client/session boundary and one Gameflow monitor.

Important rules:

- Features consume the shared Gameflow state; do not create competing phase polling loops.
- Connected phase reads are authoritative. Process presence is only a fallback when LCU is temporarily unavailable.
- Cadence is activity-based: disconnected/reacquisition 3s, Queue/ReadyCheck 3s, ChampSelect about 2s, InGame 10s, ordinary client state about 5s.
- Lobby/ReadyCheck automation evaluates immediately when the relevant phase is observed.
- Stable Lobby membership uses a fingerprint to prevent duplicate search writes.
- Failed/ambiguous matchmaking writes reconcile `/lol-matchmaking/v1/search` before deciding to retry.
- ReadyCheck attempts use an episode fence; true failures can retry after a short delay, while a final local response prevents duplicate writes.

### Runtime Companion (task PR #283)

The Runtime Companion is a narrow transient **consumer/orchestration surface**, not another League runtime.

- `LeagueHubModule` remains the automatic Champion Select episode owner. It decides when one companion instance may exist and when leaving Champion Select closes it.
- `LeagueLiveModule` constructs the companion from the already-initialized Live/Bench services; it does not create another League session.
- `LeagueRuntimeCompanionController` projects the existing Bench, Build Advisor and Mayhem owners into defensively copied presentation snapshots. Generation/cancellation rules reject stale champion/build/guide work.
- build recommendation reads reuse `LeagueBuildAdvisorModule.RuntimeCompanionReadService` and the module-owned shared `CachingOpggBuildApi`. The same OP.GG payload may project up to three source-ordered alternatives for supported categories; expansion is presentation-only and does not fan out another request.
- Bench swaps reuse `LeagueBenchQuickPickService`. Bench availability controls only the quick-swap strip; it does not gate ARAM/Mayhem guide relevance when a valid ChampSelect session/champion and mode are known. Explicit rune/summoner-spell actions reuse `LeagueBuildApplyService`; the first source row remains the apply default and the existing owner retains phase/champion/queue revalidation plus settled postcondition verification.
- core-build import reuses `LeagueItemSetService`; preparation is read-only, the user confirms explicitly, live context is revalidated before the first filesystem write, only FACM-owned recommendation files are changed, and the committed JSON is verified. Categories without a deliberately wired safe owner remain display-only.
- explicit quit-current-Champion-Select uses `LeagueChampSelectQuitService` and dedicated `ILeagueChampSelectQuitWriteApi`. The writer allowlists the primary `POST /lol-lobby-team-builder/champ-select/v1/session/quit` and one guarded `POST /lol-gameflow/v1/session/request-lobby` fallback after definite HTTP 400 with original party identity/roster observed. It never deletes/recreates a lobby or kills a client process. The service verifies settled Lobby phase, absent ChampSelect session, and matching original party ID/members. When original identity is unavailable the fallback is disabled; no second Gameflow poller is introduced.
- base ARAM balance is supplementary presentation data. `RiotGameDataService.EnrichAsync` is the single full-Mayhem enrichment owner: it starts `OpggAramBaseBalanceService.EnrichAsync` once in parallel with visual metadata, using that service's bounded ten-minute complete-result cache. `MayhemAutomaticGuideService` must not pre-call the balance service and then call Riot enrichment again.
- Bench availability is not a game-mode classifier. `LeagueQueueModePolicy` separates ordinary ARAM (queue 450 / ARAM) from ARAM Mayhem (observed global queue 2400, CN/WeGame queue 3270, or KIWI/ARAM_MAYHEM mode tokens). Ordinary ARAM requests only its version-bound base-balance supplement; Mayhem uses the full Mayhem guide/augment pipeline. Unsupported Bench modes fail closed instead of displaying Mayhem data.
- the companion renders base ARAM balance only when a real `BaseBalanceSummary` exists. `syncing`/`unavailable` states remain explicit warning states; missing data is omitted rather than rendered as a fake `0` or `no modifier` result.
- the transient window has no taskbar entry and keeps `ShowWithoutActivation`; pin/collapse/drag are presentation concerns only.

Window preferences are also shared ownership rather than Form-local state. `LeagueBuildAdvisorModule.RuntimeCompanionSettings` exposes the already-loaded `SettingsModule.Settings` object; `LeagueRuntimeCompanionWindowState` persists position, pin and collapse values through `AppSettings.Save()` and the existing last-known-good recovery file. It never opens a private settings file.

FACM already declares PerMonitorV2 awareness in `app.manifest`. Runtime Companion placement therefore occurs from the Form's `Shown` path, after WinForms has established DPI-scaled physical geometry. Saved coordinates may be negative for displays left of the primary monitor; restore and user drag are clamped to the chosen monitor's current working area. Expanded height is capped against physical working-area height after DPI scaling so 125/150/200% configurations do not reuse a 96-DPI pre-show size assumption.

### Champion Select dodge evidence

The 3.5.38 public field-test probe remains a **consumer** of `LeagueDashboardModule`'s shared Gameflow state. `LeagueDodgeProbeService` owns the episode lifecycle and `LeagueDodgeSideEvidenceProbe` owns short-lived read-only evidence correlation. Neither owns Gameflow or a separate League session.

The probe reads only local LCU GET endpoints:

- `/lol-matchmaking/v1/search` for `dodgeData`;
- `/lol-champ-select/v1/session` for visible `myTeam` / `theirTeam` identity and `chatDetails.chatRoomName`;
- `/lol-chat/v1/conversations` plus selected conversation `messages` / `participants` for room system/departure evidence;
- `/lol-lobby/v2/lobby` for positive party/lobby member departure evidence.

Normal sampling is bounded: conversation details are baselined once and the normal loop relies primarily on conversation `lastMessage`; only a confirmed dodge opens an approximately one-second high-frequency evidence burst. This is intentionally separate from the process-wide Gameflow cadence and does not create a second phase poller.

Tencent live evidence has shown `StrangerDodged` with `dodgerId=0`, a complete visible ally identity set, and hidden opponent identities. The classifier therefore uses positive evidence and fails closed. `StrangerDodged`, hidden opponent IDs, or the absence of an ally departure signal are not sufficient enemy evidence.

The probe is read-only by construction because it receives `ILeagueClientApi`, not a League write interface. It must not send chat, accept/decline ReadyCheck, start/cancel matchmaking, alter Champion Select, or introduce any LCU `POST`/`PUT`/`PATCH`/`DELETE` path.

## Mayhem / ChampSelect

Mayhem keeps the established 3.5 service/cache/network path. UI code must treat win-rate values as **0..100 percentage points** and must not multiply by 100 again.

Do not replace the current fast service with a 4.x data stack merely for architectural consistency. UI completeness, cancellation, bounded waits, cache behavior and fail-soft handling are the relevant contracts.

## Desktop entry and pets

`DesktopEntryGameflowPolicy` owns in-game suppression semantics:

- first InGame transition suppresses desktop entry surfaces;
- restore ownership is recorded only when Gameflow hid something that had been visible;
- repeated InGame observations do not repeatedly close a control center explicitly reopened by the user;
- leaving InGame restores only Gameflow-owned visibility.

`PetsModule`/`AnimalPetManager` expose active and visible state. `VPetHostClient` tracks requested visibility so hide/show intent survives host startup races.

## Update architecture

`OnlineService` reads version/announcement/mirror metadata. `UpdateInstaller` downloads the approved 3.5 Release EXE, verifies it and extracts the embedded `FACM.Updater.exe`. The updater waits for FACM to exit, atomically replaces the EXE, validates the result, restarts it and can roll back on early failure.

There is no current 4.x migration/bootstrapper mode. Unknown legacy JSON properties are ignored rather than used as runtime migration instructions.

## Build contracts

CI must enforce:

- ToolBundle input integrity and embedded resource presence.
- optional PetHost build/self-test.
- no `FACM.Resources.PetHost.zip` in ordinary FACM.exe.
- FACM.exe <10 MiB.
- host, League dashboard/automation, performance, updater, floating-ball, pet and Mayhem smoke tests.
- shared control primitive contract checks, including keyboard-reachable navigation, anti-regression geometry/chrome repaint rules.
- desktop launcher definition/geometry rules and shared compact geometry constraints.
- UI text contract.

`--facm-host-test` covers Runtime Companion settings serialization/recovery and deterministic DPI/multi-monitor window-state geometry. `--league-dashboard-test` covers Runtime Companion presentation/controller projection invariants in addition to the deterministic dodge-probe classifier/departure-text smoke. The Windows build remains the executable gate; real Tencent-client acceptance is still required before the task PR is considered shippable.

## State ownership rules

Prefer one owner per mutable runtime concern:

- one Gameflow monitor;
- one shell/MainForm;
- one updater replacement path;
- one desired pet visibility state;
- one online version manifest;
- one canonical 3.5 lightweight publisher;
- one shared settings object for Runtime Companion window preferences;
- one shared UI design-system direction rather than page-local theme engines.

When asynchronous work can finish after context changes, use cancellation/generation/fingerprint/postcondition checks rather than adding arbitrary sleeps.


## Floating-ball-first quick launcher (3.5.50 integration)

`MainForm` remains the single shell owner: floating ball and tray are the default entry, and `CompactMenuForm` is a transient anchored flyout. `DesktopLauncherEnhancer.Apply` transforms the prepared compact window into a two-row launcher **before** `MainForm.PositionMenu` and `Show`, avoiding first-frame geometry jumps. It now consumes typed `CompactMenuForm.LauncherTheme`, `LauncherOwner`, `LauncherSettings`, `LauncherCleanup`, and explicit menu actions instead of reflecting on private fields and methods. Four main action targets route via `LeagueHubUiBridge` to existing stable view IDs; footer actions retain the cleanup/repair, personalization and more-settings paths. Source-level visual adjustments do not create any new League Gameflow polling, LCU write owner, cloud dependency, persistent full-size dashboard or runtime resource. The legacy non-launcher CompactMenuForm path remains available as a guarded fallback.


## LOL workbench single-rail navigation (3.5.51)

`LeagueHubForm` owns one persistent grouped primary navigation rail with all nine existing stable `LeagueHubNavigation` view IDs, a client-status footer and one fill-width content canvas. The old global secondary tab strip and persistent context action dock have been removed; each view's factory, Gameflow owner, contextual launch through `LeagueHubUiBridge.RequestOpen(owner, viewId)`, and embedded child lifecycle remain unchanged. This is a transient workbench opened from the floating-ball-first shell, not a resident dashboard or separate League session. The main content area has no new polling or LCU writes. Navigation shows Match (dashboard/player/live), Guide (recommendation/mayhem), Automation (efficiency/presence/repair), and the separated My GGman view; internal section-key compatibility remains intact. The navigation rail compacts at narrow window widths and scrolls vertically if constrained by DPI.


## Player history page presentation (3.5.52)

`LeaguePlayerForm` continues to consume `LeaguePlayerDataService` through the existing profile/match history cache, pagination, progressive enrichment and lifetime cancellation contracts. Its UI layout now recalculates match/stats lists, footer buttons, empty/loading state, and seven match table column widths from the actual embedded form client size; the virtualized recent match ListView remains unchanged. Winner/loser semantic colors highlight only the result cell instead of coloring all visible match details. No new network requests, background polling, or data ownership were added. `LeaguePlayerSmokeTest` validates the layout bounds and responsive column-width constraints without creating a real League client. Native WinForms/DPI verification is still required after automated build checks.


## Recommendation workbench presentation (3.5.53)

`LeagueRecommendationForm` remains the single owner of recommendation display state, selection, guarded confirmation, refresh and routed League apply service calls. Its content is hosted inside a vertically scrollable WinForms panel with 1/2/3 responsive preview/choice columns; an independent fixed-height bottom action bar retains Refresh, Apply and explicit result feedback at narrow embedded dimensions. `LeagueRecommendationDesignEnhancer` styles labels inside the inner content panel by control identity rather than pre-refactor absolute Y positions, and refreshes semantic feedback tones on theme change. Shared `FacmDesignSystem` tokens replace legacy independent neon colors. No new Gameflow or network pollers, data owners, LCU writes, auto-apply timing changes or dependency bundles. Existing read/apply and preview smoke contracts remain valid, and a layout/status policy smoke covers breakpoints and semantic result colors.


## Mayhem lookup and legacy ChampSelect assistant UI (3.5.54)

`MayhemLookupForm` continues using the existing `MayhemLookupLayoutPolicy` for responsive query/actions/status/preview placement; no new layout owner or image pipeline. Query, cancel, save and copy now expose native accessible labels, pending cancellation uses a warning status, and user-visible dialog/file names use GGman branding. `MayhemLookupLayoutPolicy.ValidateForSmokeTest` covers narrow-width query/action and status/preview non-overlap. The separate 660px legacy `LeagueChampSelectAssistantForm` keeps its current automatic-show, ShowWithoutActivation, Bench-only gate, fixed compact/expanded sizes, drag logic and request/read/write owners, but now uses shared `FacmDesignSystem` surfaces/colors, shared native-looking action buttons and semantic swap/guide feedback. The modern `LeagueRuntimeCompanionForm` remains untouched, as do all gameflow and LCU pathways. These are bounded presentation/feedback changes, not a redraw of the runtime companion or a live-client screenshot validation.


## Real-time narrow companion scroll visibility (3.5.55)

`LeagueRuntimeCompanionForm` retains its fixed 320px-width nonactivating WinForms overlay, Gameflow-derived snapshot consumer and existing scrollable recommendation body. A thin decorative scroll-progress rail lives outside the scrolling body in a local dock-fill shell; it computes thumb geometry from the current viewport, content height and scroll offset, and hides itself when all recommendations fit. It is not an interactive scrollbar and adds no polling or network work. The original compact header remains a direct Form child so `LeagueRuntimeCompanionWindowState` still resolves pin/collapse controls and persists/restores position as before. Localized button accessible names follow their current action (including pin/unpin, collapse/expand, recommendation alternatives, augment pages, Bench pages), with original click handlers unchanged. The scroll-geometry helper has deterministic smoke assertions for top/mid/end and no-overflow. This change does not touch quit-from-ChampSelect, loadout/item-set writes or League data service ownership.


## Recommendation footer layout regression guard (3.5.56)

The Recommendation page's scroll body and fixed footer remain owned by `LeagueRecommendationForm`. `ResolveActionBarLayoutForSmokeTest` now determines status/Refresh/Apply rectangles and total action-bar height from the embedded client width; the bottom action bar switches from 69px single-row to 94px stacked status/buttons below 480px. `LayoutActionBar` applies exactly that policy. `LeagueBuildAdvisorSmokeTest.ValidateRecommendationPageLayout` proves footer elements are on-screen and nonoverlapping at 280, 320, 400, 479, 480, 680 and 920 logical pixels. It does not change recommendations, write confirmation, theme system, Gameflow, floating entry, League network calls or auto-apply lifecycle.


## Hub contextual launch without a throwaway Dashboard (3.5.56)

`LeagueHubForm` now resolves an optional initial route before its first `Shown` event and creates only that route's child. `LeagueHubUiBridge` passes a verified contextual route through the form's typed `SetInitialView` method immediately after construction rather than installing an additional `Shown` handler that previously opened Dashboard and instantly discarded it before showing the requested page. Invalid/empty requests still start on Dashboard. All nine route factory mappings, ownership, child close/dispose behavior and Gameflow observers remain unchanged. The existing Dashboard smoke verifies every contextual route and unknown-route fallback without launching a real League process.


## My GGman profile page presentation (3.5.57)

`LeaguePersonalStatsForm` remains the local-first owner of already-projected account counts, active-day statistics, recent account rows, cloud ranking opt-in, optional usage telemetry switch and status. The formerly 720px fixed main canvas is now hosted in a single scrollable WinForms area; four existing panels share a computed content width and the three top summary metrics allocate non-overlapping horizontal slots. Its content has enough height to preserve the status message and refresh button when the Hub's embedded viewport is shorter than the original standalone window. `LeaguePersonalStatsForm.ValidateForSmokeTest` verifies the width and metric bounds under narrow and wide host dimensions, wired into `FacmHostSmokeTest`. No new routing, API calls, identity changes, data migration, cloud ranking or privacy preference writes are introduced.


## Update Center readable state and release notes (3.5.58)

`OnlineCenterForm` remains the existing signed 3.5.x update dialog and keeps `OnlineService` as the sole metadata reader and `UpdateInstaller` as the download/signature/atomic-replacement owner. Its 560×620 client surface uses a slightly taller version card and shorter announcement card, with the same forced-update exit behavior. The version card now separates compact verification/download status from a selectable, read-only, independently scrollable multiline release-notes field; announcements retain their own scrollable region. `CanInstallUpdateForSmokeTest` gates manual/automatic update actions on a verified available manifest and no metadata error; `ResolveReleaseNotesForSmokeTest` exposes entire publisher notes without forcing multiline text into a clipped status label. On metadata failure, a fresh error-only snapshot prevents a stale available-update action; on installer failure, an explicit inline error and badge persist alongside the existing error dialog. `UpdateMirrorSmokeTest` covers these state contracts. No manifest schema, mirror choice, approved URL, download hash/signature, updater payload or compatibility asset changes.


## Fresh-install quick-access guidance (3.5.59)

`SettingsModule.IsFreshInstallation` is resolved before `AppSettings.Load` creates a new settings file. The decision requires no primary settings, no recovery settings and no legacy FACM settings; migrations and restored installations do not receive first-use UI. After shell initialization, `Program` opts `MainForm` into one initial lightweight introduction. `MainForm.HandleShown` schedules the existing `CompactMenuForm` launcher to open once, only if the shell is not in a suppressed gameflow episode or cleanup startup. `DesktopLauncherEnhancer` inserts a short native card above its existing four primary shortcut tiles, while shifting launcher/footer geometry together and preserving the floating-ball-first process entry, tray recovery and contextual launch routes. The card has two explicit choices: open the already-owned workbench or dismiss, which returns the launcher to its normal dimensions. An optional '使用指南' entry in the existing '设置' popup lets users reopen the same card later; no extra window, network request, custom persisted preference, new League observer or notification timer is introduced. The shared `UiTextKeys`/`UiTextCatalog` define all new public copy, and `ShellUxSmokeTest` proves fresh-vs-migrated eligibility and context/non-context layout bounds.


## Tray and legacy launcher entry semantics (3.5.60)

The compact desktop launcher remains the primary floating-ball entry; a ball click intentionally **toggles** its existing panel. By contrast, the tray's **Open control center** item and tray icon double-click now call `MainForm.EnsureMenuOpenAndActive` so an already-open panel is surfaced rather than unexpectedly dismissed. The tray's existing **More** group now exposes the same on-demand `使用指南` card as the launcher Settings popup via `MainForm.OpenGettingStarted`. The old compact League button directly invokes the typed `LeagueHubUiBridge.RequestOpen(owner, string.Empty)` path. `ShellMenuGroups.FindGroup` is now a pure menu lookup; the retired League dropdown returns null without indirectly scheduling a Hub window. Both active and legacy surfaces still target the single nine-route `LeagueHubForm`, with the existing modal lifecycle and nonactivating in-game companion unchanged.

## Akari 1.5.1 champion loadout parity candidate (Draft #357)

`LeagueChampionSpellPresets` and `LeagueChampionRunePresets` own independent bounded, mode-keyed catalogs; each can supply a ranked-default fallback. `LeagueBuildApplyForm` captures live LCU spell slots and all nine rune perk IDs through the existing `LeagueBuildApplyService`, without writes during capture. `LeagueAutoApplyController` merges the saved halves into a single plan, one stabilized one-shot decision and one already-existing ChampSelect-only two-second observation lifecycle. This implementation does not create a new background worker, change the 56px floating orb or alter the approved compact/secondary window geometry. For runes the existing apply service creates or reuses only application-owned pages; it rejects unsafe capacity. Local recovery, portable/cloud preferences and deterministic smoke assertions cover persistence. Tencent gameplay acceptance and real CPU/RAM measurement are intentionally deferred to the owner's final consolidated test.

## Akari 1.5.1 honor policies (Draft #357)

The existing `LeaguePostGameAutomationController` remains the only postgame owner. Its opt-in auto-honor action allocates up to the ballot's available votes to distinct eligible players according to `LeagueHonorStrategy` (party preference, party-only, any ally, no vote or ally/opponent), using one conditional read of Akari's end-of-game lobby-party endpoint when the choice depends on party membership. All writes still flow through `ILeaguePostGameWriteApi`: the primary Honor V2 path waits for target-specific readback **between votes**; the legacy fallback is selected only before a successful V2 vote and submits the ballot **once after the eligible honors**. An unverified V2 result blocks the rest of the batch. The native League Efficiency form uses an inline combo box in the existing honor row; local, cloud and portable settings store the same bounded strategy. Unknown settings normalize to party-preferred and automatic honor remains disabled by default. Live regional multi-vote and route acceptance remain unverified.

## Akari 1.5.1 login-time signature restore (Draft #357)

`LeagueLoginStatusRestoreController` belongs to `LeagueEfficiencyModule` and reuses its authoritative `GameflowStateChanged` subscription. Its opt-in configuration is stored by the existing `SettingsModule`, including INI/recovery, cloud and portable save/restore; the scrollable Quick Tools form keeps unsaved text drafts across asynchronous external preference refreshes. While connected and enabled, it tries `LeaguePresenceService.ReadAsync` at bounded intervals until chat-me exists, waits two seconds and rereads the status, then calls `ApplyStatusMessageIfUnchangedAsync` only if the message was not manually changed during that settle window. The existing presence writer sends a single user-authorized full presence PUT and verifies two post-write snapshots. The whole attempt is guarded by a generation token, cancels on disconnect or setting change, and never repeatedly rewrites a client override within one episode. No additional always-on worker or chat event WebSocket was introduced. This covers Akari's login-time signature action, not its login-time displayed-rank option or incoming-message autoreply.

## Akari 1.5.1 ARAM side notification (Draft #357)

`LeagueAramTeamSideController` is owned by `LeagueEfficiencyModule` and consumes its existing cloned Gameflow snapshots. It starts bounded, cancellable LCU reads only for enabled ChampSelect, with no independent polling timer; its first successful/uncertain chat submission exhausts the current episode until the phase exits. It independently checks `GET /lol-gameflow/v1/session` for `ARAM` or `KIWI`, `GET /lol-champ-select/v1/session` for an eligible bench-enabled local team `1` or `2`, and `GET /lol-chat/v1/conversations` for a unique safe `championSelect` conversation. All three are revalidated before a single POST. `ILeagueChampSelectSideWriteApi` is a narrow typed capability on the already-shared authenticated matchmaking transport, with local `celebration` or team-visible `chat` JSON. Settings are explicit, disabled by default, and restored through the existing cloud/INI infrastructure. Dynamic player identifiers, conversations and chat response bodies are not logged. Regional live semantics and UI scaling still need the consolidated owner test.


## Optional registered GGman account (Issue #313, released in 3.5.61)

`GgmanAccountForm` is an optional modal from `LeaguePersonalStatsForm` (“我的 GGman”). It uses the dedicated `GgmanEmailAuthClient` over the same CloudBase HTTPS gateway but does not pass credential-bearing sessions to the existing anonymous `CloudBaseClient`. Email flow is explicit: send → optional CloudBase `captcha_required` with bounded image/verify native modal and one `x-captcha-token` retry → verify six-digit OTP → CloudBase sign-in or sign-up → obtain registered subject UID. `GgmanAccountSession` stores a registered identity only in process memory, distinct from `CloudIdentityStore`'s anonymous `device_id`. Logout calls CloudBase signout and always clears local session. No new runtime pollers, server secrets, auth tables, persistent token files, or changes to League Gameflow/LCU session ownership. Existing anonymous device records, local personal stats and GGman preference sync intentionally do not migrate or merge. Production deployment occurred in 3.5.61 after owner-verified email sign-in; two-device UID, provider CAPTCHA challenge and logout revocation remain post-release acceptance items. See `docs/GGMAN-ACCOUNT-AUTH.md`.

## Optional LCU private-chat event owner (2026-10-26)

`LeagueEfficiencyModule` owns one `LeagueAutoReplyController` which also dispatches direct presence events to the existing offline-lock controller. Explicitly enabled non-empty replies or the separate offline-lock switch acquire one shared loopback TLS/WAMP stream; existing Gameflow observations start/recover/cancel it. After subscription, offline locking seeds its baseline with one presence GET, then consumes events instead of sampling presence on every Gameflow tick. The stream validates the HTTP WebSocket upgrade, buffers at most 64 KiB per event, drains oversized unrelated events, and dispatches sequentially for bounded backpressure. Certificate handling is scoped to that TLS stream for net48 compatibility. No new HttpClient pool, permanent polling task or dedicated OS thread is introduced. The private writer reuses the existing matchmaking pool while requiring the originating session and a safe URI segment. Reply delivery is submitted/unverified rather than inferred from status alone. Both settings off means no event connection, session discovery or message reads by this owner.
