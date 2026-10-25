<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：GGman 3.5.78
- GitHub Release：v3.5.78
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：c19b11881dcbb397c806b0dad2c518f10c0eaa07
- 发布元数据提交：e03a6aa58d64e90ccfbe48c8809b11fd574cca60
- Release GGman.exe SHA-256：5EC945C760D75422023AECB0060C023DC8D88933A29B42796AB505162EA72F97
- release_notes：GGman 3.5.78：统一升级 GGman 工作台、游戏修复、在线状态、快捷工具、海斗攻略、战绩与推荐、悬浮启动器、主题选择、更新中心、账号与配置、清理和桌宠等界面的视觉风格；优化信息层级、文字阅读、按钮状态、分组布局及部分窗口在较窄尺寸下的排版。保留原有游戏查询、账号云同步、ESC 配置与更新机制，不强制更新。此版本先开放用户实机截图反馈，后续继续完善主题、缩放和界面细节。
<!-- FACM_RELEASE_STATE_END -->

## 2026-10-25 — Akari auto-select profile priorities and staged actions (Draft #357 continuation)

Owner clarified the acceptance cadence: continue **all Akari 1.5.1 feature development** and internal automated/Windows CI checks without asking for interim live LOL games. Prepare a consolidated final Win10/CN LOL field acceptance pass and diagnostic ZIP-based triage once every required behavior has implementation candidates; this is not permission to mark untested features complete or publish them.

The next code slice extends the initial single-hero automatic pick/ban controller with **native mode/assigned-lane priority profiles** (`default`, Rift, normal ARAM, Mayhem; Rift top/jungle/middle/bottom/support), ordered pick and ban candidate lists, and per-profile `lock-in-immediately`, `just-show`, `show-and-lock-in` strategies with bounded delay. The auto-select controller reuses the existing Gameflow observer, existing LCU session, and phase-limited 2-second task. It chooses the first LCU-available hero in priority order, checks current local action/role/mode/cell before the write, and cancels any pending stage on phase change or settings change. New preferences are JSON-validated and size-bounded in `AppSettings.LeagueAutoSelectProfiles`; invalid or unknown profiles fail closed. An on-demand native priority editor is launched from existing Quick Tools without changing the frozen orb/compact UI. It is a complex secondary settings editor, not a new top-level launcher. Existing one-hero settings remain the fallback for backwards compatibility.

Deterministic tests now additionally cover profile persistence, Riot/Tencent queue isolation, lane fallback, alternate unavailable heroes, show-only, two-stage show-and-lock, manual intervention between stages and cancellation. **Status: code candidate under Windows CI validation; no live regional confirmation or measured low-end CPU/RAM yet.** Remaining parity requirements include intent preselection, queue-specific groups, temporary disabling, bench/trade automation, presets/SGP/player data and all additional Akari v1.5.1 features listed in the fixed-source audit. Draft only; no merge or updater changes.

**Initial CI feedback:** code `6ab1410d` failed [Windows Build #40186143766](https://github.com/xianyumht-cmd/facm/actions/runs/40186143766) with `CS0234` because an always-on League module referenced `CompactRiftRoleService` in the optional `Presentation/Glass` directory excluded by `FACM.csproj` in normal builds. UI Text and Mayhem Source checks passed. The follow-up moves that pure local-cell/assigned-role projection into the always-compiled League preferences service and adds unknown-role guards. Re-run both ordinary and Glass Windows builds before calling this candidate verified; see `docs/PITFALLS.md`.

## 2026-10-25 — Akari 1.5.1 automatic pick/ban candidate (Draft #357)

Continuing from the verified Draft #357 reconnect slice, implement the next **partial** Akari functional parity candidate: opt-in default-champion auto pick and auto ban on GGman's native `LeagueEfficiencyModule` and existing authenticated LCU session. Pinned Akari source owners: `auto-select/{computed-state,ban-pick-controller,action-executor}.ts` and `shared/http-api-axios-helper/league-client/champ-select.ts`, exactly at commit `14557723706ccc0e0a9d62c470141d4cb7190fcd`. Do not import its Electron runtime or change frozen GGman UI geometry.

`LeagueAutoPickBanController` observes the already-owned Gameflow state. Only while enabled and connected in ChampSelect does it run one cancelable 2-second session task. The task selects the first unfinished local `BAN_PICK` action, validates requested champion against LCU pickable/bannable IDs, rereads the session immediately before sending a narrowly constrained authenticated `PATCH /lol-champ-select/v1/session/actions/{id}`, and does not overwrite an intervening manual hero selection. Action IDs are deduplicated within the episode; leaving ChampSelect, disconnecting, changing settings or shutdown cancels outstanding work. No credentials, player details or raw responses are logged; `CompactTraceLog` records stage/status/duration under a trace ID. Both switches default **off**; existing Quick Tools gains scrollable native hero choices fetched on demand. The orb and frozen compact panels are untouched.

Deterministic `LeagueAutoPickBanSmokeTest` covers default-off persistence, narrow action writer validation, player/action ID zero, invalid/missing action fields, availability, local turn, preflight manual override, pick/ban writes, duplicate Gameflow suppression and lifecycle cancellation. This is **not** proof of actual Chinese LOL client operation. Still mandatory: role/queue/mode preferences, multiple candidates, intent/show/delayed lock, bench/trades, live Tencent acceptance and measured Win10 CPU/RAM/requests. No `main` merge, signed release or updater manifest change is authorized.

**First CI feedback at code `09767c4b`:** both Windows Build #40181211430 and Glass Candidate #40181211407 compiled the C# code, and the standalone League Dashboard smoke completed, but their later performance-contract invocation deadlocked after `[performance-smoke] START league-dashboard` until the test `Exec` 120-second deadline. UI Text and Mayhem Source workflows passed. This was not a successful CI run. Root cause investigation identified an async fake-clock smoke continuation capturing WinForms synchronization context while the caller blocked on `GetResult()`. The follow-up change uses `ConfigureAwait(false)` in smoke awaits; see `docs/PITFALLS.md` and the successful rerun below.

**Verified follow-up at code `4f525deb4ed52a2193a4cec59479b96324189503`:** the 2026-10-25 rerun passed all four GitHub workflows: [Windows Build #40181639901](https://github.com/xianyumht-cmd/facm/actions/runs/40181639901), [Glass Candidate #40181639890](https://github.com/xianyumht-cmd/facm/actions/runs/40181639890), [UI Text #40181639881](https://github.com/xianyumht-cmd/facm/actions/runs/40181639881) and [Mayhem Source #40181639869](https://github.com/xianyumht-cmd/facm/actions/runs/40181639869). The Windows log explicitly recorded `FACM League Dashboard smoke passed` followed by `[performance-smoke] START league-dashboard` and `[performance-smoke] OK league-dashboard`, proving the previous context-capture deadlock no longer reproduces in CI. The unsigned [GGman Windows review ZIP #12594535817](https://github.com/xianyumht-cmd/facm/actions/runs/40181639901/artifacts/12594535817) is **not** an authorized release. Actual Chinese LOL LCU accept/ban responses, normal and elevated users, Windows 10 19045 interaction/DPI, memory and CPU remain unverified.

## 2026-10-25 — Owner-mandated full Akari 1.5.1 parity; automatic reconnect candidate

The user clarified that **all features** of the pinned Akari v1.5.1 source commit `14557723706ccc0e0a9d62c470141d4cb7190fcd` must eventually work within GGman's current native, low-resource architecture. The previous matrix's "not practical"/"defer" categories were misread as permanent exclusions and have been replaced by a mandatory functional-parity backlog in `docs/LEAGUE-AKARI-V1.5.1-REFERENCE-AUDIT.md`. Electron-specific host mechanisms are not a GGman requirement; their user-facing outcomes are. The specified 7z artifact remains unexamined, and source inspection is not evidence of live CN operation.

This draft code slice implements one previously missing v1.5.1 feature, **opt-in delayed game reconnect**, by reusing `LeagueEfficiencyModule`'s existing Gameflow observer, one authenticated LCU writer and a 10-second cancelable task. A live-phase preflight fences the exact bodyless `POST /lol-gameflow/v1/reconnect` route; no retry loop or extra background thread is introduced. The setting defaults off, is visible on the native Quick Tools page, and has deterministic offline cancellation, stale phase, method allowlist and once-per-episode smoke tests. Traces are sanitized and bounded. These are **candidate source changes**, not proof that the Tencent client accepted a real reconnection. A new signed release, main merge or updater edit remains prohibited pending explicit owner approval and live Windows 10 checks.

**Verified candidate in [Draft PR #357](https://github.com/xianyumht-cmd/facm/pull/357):** GitHub code SHA `8499e281d3718d99aca4602fd35e45379bec2dfd` matched the locally verified complete tree `52e3e66933acc143190abd4339ff9cee9c06435d`. All four final-code CI runs completed successfully: [Windows Build #40178148651](https://github.com/xianyumht-cmd/facm/actions/runs/40178148651) (the `ContinuousIntegrationBuild` target invokes `--league-dashboard-test` and the new reconnect fake-clock suite), [UI Text #40178148576](https://github.com/xianyumht-cmd/facm/actions/runs/40178148576), [Mayhem Source #40178148585](https://github.com/xianyumht-cmd/facm/actions/runs/40178148585) and [Glass Candidate #40178148676](https://github.com/xianyumht-cmd/facm/actions/runs/40178148676). The GitHub [candidate Windows ZIP/artifact #12592789080](https://github.com/xianyumht-cmd/facm/actions/runs/40178148651/artifacts/12592789080) is for isolated testing, not an approved update. CI success is not a live reconnect or measured CPU/RAM result.

## 2026-10-25 — Akari v1.5.1 gap verification and compact ranking reliability (Draft stack)

Work starts from Draft PR #355 head `5e5cd4a52b057cfa86d31b226e7df2e1ceae41f8`; the approved compact UI, `main`, the 3.5.78 signed release and updater manifest remain untouched. The new code branch is `feat/ggman-akari151-ranked-reliability-20261025`, intended as a reviewable Draft PR targeting #355's development branch rather than a competing standalone UI.

Latest #355 CI runs at that head all succeeded (#40174485828 Mayhem source, #40174485814 UI text, #40174485830 Windows build, #40174485817 Glass candidate). Inspecting the Glass job's **actual public probe output**, however, revealed `ordinary-aram valid=True expanded=True patch=26.21 rows=10` but `rift-top valid=False rows=0`. Both public pages returned HTTP 200; the ranked table contained 864 rows. An independent unauthenticated ranked-page download took ~3.95 s for ~2.16 MB, above the Rift reader's 3.2-second deadline. This supports a timeout hypothesis, not a confirmed live-LCU or mainland network result.

Candidate changes: bounded Rift timeout becomes 8 s (ARAM 6 s), internal transport timeouts are distinguished from user cancellation in structured trace logs, the public probe includes sanitized trace outcomes and makes incomplete modes visible as GitHub warnings, and the auto-matchmaking Lobby parser waits for pending invitations using its existing read and observer rather than queueing prematurely. The Akari study is pinned to release tag/commit `14557723706ccc0e0a9d62c470141d4cb7190fcd` and its matrix is in `docs/LEAGUE-AKARI-V1.5.1-REFERENCE-AUDIT.md`. No 7z binary analysis or Tencent real-match test is claimed.

**Verified in Draft PR #357:** code head `8fa2fdddf0c9d1e104747d1f708dd6f2937ea8e9` passed Windows Build #40176052222, UI Text Contract #40176052106, Mayhem Source Probe #40176052124, and Glass Candidate #40176052137. The actual Glass public probe reported `ordinary-aram valid=True expanded=True patch=26.21 all_rows=173` and `rift-top valid=True expanded=True patch=26.21 all_rows=173`; bounded trace events recorded `aram query ok 1315 ms` and `rift query ok 470 ms` on the GitHub Windows runner. Thus the **real C# parser succeeded there**, rather than relying only on HTTP row counts. This is not a current China-client patch guarantee or a no-VPN latency measurement. [Unsigned test artifact](https://github.com/xianyumht-cmd/facm/actions/runs/40176052137/artifacts/12592570187); the build is not a formal release.

**Acceptance still pending:** Win10 19045 in-game Chinese-client role/queue and Lobby invitation behavior, non-VPN CN latency, CPU/RAM and process cleanup, and owner visual review. The candidate must not be merged, signed, released or used to change the production updater without owner approval.

## 2026-10-24 — Native compact preview white screen and independent browser entry

The user supplied an actual 380×285 white rectangle screenshot from the unsigned compact preview build on Win10. This proves the frameless host was created but **not** that HTML loaded; the white area is consistent with an unrendered WebView2 control. The subsequent user-provided `facm-20261024.log` spans 11:24:56–11:25:10 and contains `FacmHostSmokeTest`/other intentional test failures but no `Glass`/`WebView2` event and no error corresponding to the 11:40 screenshot. It is therefore not an actual runtime rendering diagnostic; the precise event-level cause is **not confirmed**. The successful Windows build, mock-resource checks and artifact packaging in run #40089720872 did not detect this runtime failure. The earlier claim that the package was ready for visual acceptance was premature.

The follow-up changes `Start-Compact-Preview.cmd` to open the same offline HTML in the installed Edge browser's app-window mode, bypassing the unverified embedded WebView2 navigation. The source HTML is also bundled next to the script. `Start-Compact-WebView2.cmd` preserves the native-host path for diagnostics only. The independent browser window can have native title chrome and its detail viewport may require manual enlargement; it is strictly a layout preview, not proof of production floating-ball behavior. Added Windows headless Edge home/Mayhem screenshots with nonwhite-pixel checks as an executable presentation smoke gate. Its green result does not establish that the original white-screen native host is fixed. Real operator screenshot acceptance is still required. No production change, release or merge.

## 2026-10-24 — Compact launcher and secondary screens, offline visual candidate

The owner requested a **nonfunctional Windows preview first**, with the existing desktop floating ball unchanged. The new opt-in `--compact` variant extends the standalone WebView2 preview on **Draft PR #355**, not production `main`. `Start-Compact-Preview.cmd` opens a 380×285 frameless launcher and expands to 548×478 for the Mayhem ranking, League workbench, match-history, quick-tools, repair and settings layout samples. The launcher has a two-by-two entry grid; the Mayhem sample has a mode header, selected champion/tier, ten explicitly fake rank rows and per-champion tier badges above four quick-choice avatars. Everything is deliberately offline mock text and illustrative placeholder portraits; no live champion, network, LCU, CloudBase, file change or ranking feature is claimed.

The accepted 49,991-byte Glass source remains hash-pinned and is **not replaced**. The compact candidate uses a second embedded resource, isolated behind `GGMAN_GLASS_PREVIEW=1` and `--glass-preview --compact`, with only close, drag and resizing messages to WinForms. CI verifies the original resource and packages both entry scripts; a Windows x64 native screenshot / DPI / clipping acceptance is required before design approval. Do not merge, publish, or redirect the official updater on the basis of this preview.

## 2026-10-21 — Opt-in Glass desktop pilot (Draft PR #355)

### 2026-10-21 real-PC WebView2 initialization regression

User submitted `GGman-Glass-Diagnostics-20261021-153525.zip` (sanitized content only; not committed). Windows 10 19045 x64, detected GGman.exe and WebView2 managed DLLs, but root native `WebView2Loader.dll` and x86 runtime loader were both 155,488 bytes. At 15:16 and 15:24 local time the preview threw `System.BadImageFormatException 0x8007000B` from `CoreWebView2Environment.CreateAsync`. Original artifact packaging only asserted loader existence; likely native architecture mismatch. The usual Runtime registry locations were also absent in the diagnostic and require a separate post-loader check if the new package still fails. PR #355 now packages the pinned NuGet's PE-verified x64 loader, removes x86 loader output, and shows the exception class/HRESULT instead of the misleading generic Runtime-install message. The corrected code head `8fba1ede99603dd472ad5545268e6dfffac014ba` passed [Glass candidate CI #39563800832](https://github.com/xianyumht-cmd/facm/actions/runs/39563800832), which verified the packaged root WebView2Loader as **AMD64 PE (194,912 bytes)** and produced [unsigned artifact #12328258494](https://github.com/xianyumht-cmd/facm/actions/runs/39563800832/artifacts/12328258494). UI text and Mayhem checks passed on the same code head; standard Windows CI was still in progress when this result was recorded. **Native retest is pending** and the installed Runtime's actual discoverability is still unknown; build success alone does not establish that WebView2 launches on the operator's machine. Do not merge or release.


Draft [PR #355](https://github.com/xianyumht-cmd/facm/pull/355), branch `feat/ggman-glass-webview2-pilot-20261021`, restores the owner's exact 49,991-byte approved HTML visual prototype as an embedded resource and adds a WinForms-hosted WebView2 **demonstration** window. Activation requires MSBuild `UseGlassWorkbenchPreview=true` and `GGMAN_GLASS_PREVIEW=1`, followed by the explicit `--glass-preview` process argument supplied by `Start-Glass-Preview.cmd`. `Program.Main` branches **before the normal single-instance mutex and before any module, registered CloudBase client, League polling, updater or account startup**, opening only a standalone HTML preview. It can coexist with the official GGman process; no credentials or settings are accessed by the preview. No existing native functional UI, nine League navigation routes, cloud handlers or updater policies are replaced. Preview browser creation is lazy; bundled HTML shows explicitly fake offline data. The only JS→host commands are window close and window drag. External WebView2 navigations/popups/downloads/permissions are disabled. It is an isolated multi-file candidate, not a valid replacement for the official compact single-EXE distribution.

The first WebView2 CI [run #39548070500](https://github.com/xianyumht-cmd/facm/actions/runs/39548070500), default Windows Build [#39548070375](https://github.com/xianyumht-cmd/facm/actions/runs/39548070375) and UI Text Contract [#39548070360](https://github.com/xianyumht-cmd/facm/actions/runs/39548070360) passed at source head `c794dcab3ff139c7f52613a4f2d3195309ce3bda`. Candidate EXE size was 2,740,224 bytes **excluding** DLL/native loader files; zipped preview artifact was approximately 4.6 MB. After the standalone pre-mutex isolation, frameless window, preserved-line-ending gate and fail-closed launcher changes, the complete **code head `f064287cfa6c4c3478db2981a9f496cc4ccddaae`** passed [Glass WebView2 CI #39549229216](https://github.com/xianyumht-cmd/facm/actions/runs/39549229216), [normal GGman Windows Build #39549229226](https://github.com/xianyumht-cmd/facm/actions/runs/39549229226), [UI Text Contract #39549229374](https://github.com/xianyumht-cmd/facm/actions/runs/39549229374) and [Mayhem Source Probe #39549229413](https://github.com/xianyumht-cmd/facm/actions/runs/39549229413). The final unsigned preview [artifact #12323330199](https://github.com/xianyumht-cmd/facm/actions/runs/39549229216/artifacts/12323330199) was packaged (~4.6 MB zipped); the candidate is expressly **not** live-Windows visually accepted. Physical Win10 19045 visual, DPI, window focus, whole-process-tree RAM/CPU/GPU, cold startup and UX review remain unverified. No formal release, merge or production cloud/game write is authorized.


## 2026-10-17 — Owner-authorized GGman 3.5.78 published; visual acceptance pending

The owner explicitly requested an official build **before** submitting Windows screenshots. PR #351 is **merged** into main via `0f92d1dd8eb2b64f03e3851f66a589d8e3e006b2`, with audited code head `250c877a3f9b5c8d7290c54fa71e2cb5ac9aab80`. Publication workflow [#39019998489](https://github.com/xianyumht-cmd/facm/actions/runs/39019998489) completed **SUCCESS**, including signed 3.5.78 build, public `GGman.exe`/`FACM.exe` hash and signer verification, and final enabled online manifest. Release: https://github.com/xianyumht-cmd/facm/releases/tag/v3.5.78 ; `force_update=false`; `minimum_version=3.0.0`. Hash is recorded in the release-state block above. **Signed release delivered; UI design work is not professionally accepted.**

All earlier same-day state entries saying "Draft PR #351", "do not merge", "not released", or "production 3.5.77" are **historical snapshots superseded by this publication**, not current instructions. The owner will provide post-update native Win10 screenshots for professional review. Remaining evidence gaps: before/after rendered surfaces, all themes, multiple DPI, keyboard/IME/focus, complex live ChampSelect/companion HUD, runtime performance and authorized functional scenarios. Do not mark these PASS from CI, and do not claim an Astra visual sign-off until real screenshot review occurs. Regressions require a new controlled hotfix or explicit rollback; do not edit online metadata or reuse the published release asset.


## 2026-10-17 — B5 continued with UI coverage expansion, B6 pending

Draft PR #351 on `feat/ggman-precision-workbench-b1-20261017` now includes cleanup-main, email captcha, registered statistics privacy, profile customization, both pet selection dialogs, item set, build apply and personal stats presentation migrations. Commits: `ec5e3045f2708451b22535686f63f7b984da2015`, `439de9d0e9c9be5c36c83d633f40301aefa5c970`, `2542e05d0440c55f9ebd8ba9e8b80c4ba4014fd9`, `09b95e4812c7349053688ec479569adc99957f34`, `85ba71dd2f47da2944e54a845abd921b59a67bec`, corrective `a3ae722de1226e7f67fdcf672a45cdb87e9f492d` and `8aea138f250fb3d90259f7a7ba4a9170308e7a4a`.

An intermediate Windows Build #39018862920 failed because the replacement of `LeagueBuildApplyForm.CreateButton` inadvertently removed three auto-apply event/status methods. All missing methods were restored from the prior known-good code in `a3ae722de1226e7f67fdcf672a45cdb87e9f492d`; Windows Build #39019063500 PASSED on that corrected head, with UI Text Contract #39019063416 passing. Do not report the failed intermediate attempt as successful. Final-head acceptance for the subsequent personal-stats/docs edits must still be checked.

B5 remains **source migration in progress**, not native visually complete. In particular `LeagueRuntimeCompanionForm` and `LeagueChampSelectAssistantForm` own small real-time game HUDs whose layout/visual performance requires separate native evidence; unvisited legacy dialogs may remain. B6 whole-product screenshot, DPI/focus/keyboard, startup/memory and rollback tests are not available. Main, signed Release and online updater remain at v3.5.77. No merge/deployment.

## 2026-10-17 — B5 first source migration (Draft PR #351)

B5 source commit `49c024f115aecc1eb96c3d1ad369ba55d8ea07d8` migrates `CleanupReviewForm`, `BackgroundOperationDialog` and `GameLocatorSearchDialog` away from private dark-only colors to shared semantic themes. Cleanup confirmation stays explicit; cancel/keyboard support is preserved; the destructive confirm button now uses the Danger tone. Background operation worker/cancel lifecycle, cleanup path validation and actual filesystem/cloud operations are not modified. Status as recorded: UI Text Contract and Mayhem Source Probe passed for this code head; Windows Build was still executing and must be verified separately. Native WinForms rendered screenshots and safety/keyboard/DPI acceptance are not available.

B5 does not yet cover all reachable minor forms; B6 cross-product visual, security, DPI and performance gates remain. No merge, official release or updater changes. Production v3.5.77 is unchanged.

## 2026-10-17 — B4 source progress: launcher, updates, theme, account and ESC

Draft PR #351 continues on branch `feat/ggman-precision-workbench-b1-20261017`; main and official v3.5.77 remain unchanged. B4 source changes: `79c566ef8653b2f37d6d8c5bcdba0e5a0dcd4d39` standardizes launcher tiles/footer, ThemePicker and widens OnlineCenter; `4fff958255ef06bd5e73866694cc7aa7f17595cf` improves GgmanAccount form layout; `fe9129a5612a3ddc2510fb190849e8f54487be51` groups visible ESC local actions; `2843bb150b257ad82ff95a055cacaee146473985` fixes new UI text-contract violations by removing redundant hardcoded accessibility labels from ThemePicker.

Intermediate Windows build #39014102313 passed on ESC code head but UI Text Contract #39014102191 failed on the duplicate a11y names; the latter was corrected, not bypassed. Required final-head CI and full native Windows visual/keyboard/DPI/account/update acceptance are pending. B4 still has reachable secondary account/theme settings and companion surfaces to audit, followed by B5 cleanup and B6 consolidated acceptance. Do not merge or publish partial UI changes.

## 2026-10-17 — B3 Mayhem and League presentation source progress (PR #351)

B3 code changes in the same Draft branch: `8f4d1fbe5d7410f796c51b7b39b24b680f9d7248` introduced Mayhem toolbar/preview layout; `3509466bbb2d53bd0a47a19dc3e2b4e29755fd91` retained status tones on theme changes; `8c1d6cf5e49200e0e673c79d9b80cd657500c1fa` migrated Live and Advisor shared colors/actions to the canonical theme; `7e133ccb11bfad25c2f63899e9937cf70420a711` improved Player account summary and list hierarchy; `779a3d724852b831de5b7f3d147278a961f6ebad` updated recommendation choice contrast/buttons.

On these code heads, earlier Windows Builds/Mayhem probes/UI Text Contract checks were green through `7e133ccb11bfad25c2f63899e9937cf70420a711`. Final B3 code head `779a3d724852b831de5b7f3d147278a961f6ebad` has UI Text Contract and Mayhem Source Probe success; Windows Build was in progress at the time this entry was recorded. Verify the final workflow conclusion before calling this code head build-accepted. Native visual, keyboard, mixed DPI and actual League interactions remain untested.

B3 still includes additional owned companion/recommendation presentation and global normalization; B4/B5 entry, update, account/settings and cleanup remain. No merge, public executable or updater modification. Production remains v3.5.77.


## 2026-10-17 — B2 source progress: Presence and Quick Tools

On the same PR #351 branch, code heads `635fd52cd8716510791e149e8efb369e447c5613` and `916d007f82030dc561d4e843a8760c41cbcfb5f4` migrate `LeaguePresenceForm` and `LeagueEfficiencyForm` away from visibly narrow/hover-dependent presentation. Presence now uses scrollable full-width group surfaces with status choices, account signature, ranked displays and appearance actions; Quick Tools displays help descriptions inside hotkey, toggle and parameter rows instead of hover-only messages. Existing service handlers and persistence semantics are retained. The shared density pass skips these migrated surfaces.

The initial Presence code commit failed Windows Release compilation due to an invalid multi-declarator `var` expression; corrected in `635fd52cd8716510791e149e8efb369e447c5613`, with both required workflows then passing. Latest B2 source commit `916d007f82030dc561d4e843a8760c41cbcfb5f4` also passed Windows Build [#39007644220](https://github.com/xianyumht-cmd/facm/actions/runs/39007644220) and UI Text Contract [#39007644251](https://github.com/xianyumht-cmd/facm/actions/runs/39007644251). No native rendering/screenshot, input/DPI or network/cloud correctness assertion follows from these CI results.

Remaining: native B2 screen evidence and B3–B6 product-wide source modernization/verification. The official release and online updater remain v3.5.77; no merge or publish.

## 2026-10-17 — PR #351 expanded to full-product visual modernization

Owner explicitly requests complete modernization of every active GGman-owned surface, rather than per-page feedback and releases. Continue using `feat/ggman-precision-workbench-b1-20261017` / Draft PR #351. B1 Hub, Dashboard, Repair candidate is only partial; B2 Quick Tools/Presence, B3 Mayhem/player/live/companion, B4 launcher/updater/account/settings, B5 cleanup/secondary UI and B6 consolidated native QA remain pending. Complete source inventory and acceptance ledger are in root DESIGN.md. Native OS menus/file dialogs are intentional platform-owned exceptions.

Owner sequencing: finalize all visuals and pre-release functional/CI/native checks, publish a **single** update, then request **one** independent Astra visual assessment. This post-release review cannot prevent defects from reaching users; prepare hotfix/rollback. Do not misrepresent an incomplete UI migration or green CI as whole-product visual acceptance. Official release/manifest remains v3.5.77; no merge/update now.

## 2026-10-17 — B1 native screenshot feedback and game-repair visual prototype (Draft)

The product owner provided eight Windows screenshots of the candidate and rated its visual change as practically imperceptible. The captured non-Dashboard pages were outside the earlier B1 scope; this is not evidence that the Dashboard itself regressed or improved. PR #351 remains **Draft**, and earlier Windows CI success is not professional visual acceptance.

In response, the **same task branch** now explicitly extends the pilot to the active `LeagueGameRepairForm`. It replaces left-clustered fixed action rectangles with two clearly headed full-width task sections, explanatory copy at readable size, right-aligned native actions, compact genuine-result status and stacked narrow-width fallback. All five existing repair handlers and the existing auto-repair state toggle are retained. The redesign is presentation-only; `LeagueSoftGlassSkin` excludes the migrated repair view from legacy density mutation while keeping unmigrated views unchanged. A narrow layout smoke contract is invoked by the `shell-ux` suite.

Native after-screenshots, theme/DPI/keyboard observations and independent visual acceptance are **not yet available**. The earlier user screenshots serve as informal baseline context, not controlled paired captures. No merge or release; production updater remains v3.5.77.

## 2026-10-17 — Precision Workbench B0/B1 candidate (NOT MERGED / NOT RELEASED)

Astra's read-only design architecture blueprint recommends the native **Precision Workbench** system and a first scoped batch limited to the persistent League Hub, grouped navigation and the default Dashboard. PR #350 was merged to main as `1194a4cf8451b1886c03cdf733e067fd38037f66` after independent delta review and owner acceptance. The signed/public production release and updater still reference **v3.5.77**; subsequent historical PR350 Draft statements below are superseded.

The new branch `feat/ggman-precision-workbench-b1-20261017` begins a lightweight visual pilot: navigation typography/row rhythm, responsive Dashboard status/details/footer layout with vertical scrolling, and exemption of the new Dashboard from legacy density mutation. Services, nine route IDs, 5-second/default Gameflow polling ownership, draft guards, CloudBase and update paths remain untouched. New `DESIGN.md` defines the source-specific visual contract.

**B0 evidence boundary:** Source inventories, existing tests and previously published EXE size exist. Actual before/after Windows screenshots, cold/warm timings, idle memory, focus/DPI tests and complete keyboard journeys have NOT been captured by this environment. CI and an independent visual review are still required. The candidate must remain Draft; do not merge or publish until native evidence and performance comparisons are collected.

## 2026-10-17 — PR #350 third review P2 correction (candidate, not released)

Third independent read-only Astra review of head `103a61b4dfb1ffff5ecb83b45e995f05065b3770` found no remaining source-level P0/P1 blocker, but identified a concrete P2: `UiTextEditorPanel.Filter` restored selections with `OrdinalIgnoreCase` even in case-sensitive replacement-rule mode, so rules `Foo` and `foo` could be confused following Save/Discard navigation. The same draft PR now restores selection with `Ordinal` in replacement mode and leaves ordinary text selection case-insensitive. In-memory editor smoke verifies Yes/No/Cancel, exact key/value, and subsequent Save/Reset effects on the intended case-distinct rule. Final-head CI and native Windows/registered CloudBase acceptance remain required. Do not merge or release based only on this candidate; production remains v3.5.77.

## 2026-10-17 — PR #350 second Astra review corrections (candidate, not released)

Astra's second read-only review of `0c9adcb28b58e433160ce9ea5f5c5cf11cad4f11` again requested changes. Confirmed P1: choosing Yes to save an edit during list selection uses `SaveLocal(false)`, advancing the whole profile baseline without updating the destination list `Entry.Current`; an unseen newer value on the destination could then be saved back as stale data and auto-uploaded. P2: after same-key conflict, Discard left stale visible state and forced manual reopen. Both must be corrected before merge.

The same PR task branch now rebinds the destination key after navigation save/discard, synchronizes visible entries to the current persisted baseline, and rereads persisted content on Discard (read failure blocks navigation without discarding the draft). Regression checks simulate missing cloud refresh and Yes -> `SaveLocal(false)` -> navigating to a newer sibling -> saving that sibling, for regular text and replacement rules, plus same-key conflict -> Discard -> immediate re-edit and a reset-write failure. These are simulated persistence checks, not native WinForms or live authenticated CloudBase acceptance. Latest commit requires fresh Windows CI and independent review. PR remains Draft; official v3.5.77 and production updater are unchanged.

## 2026-10-17 — PR #350 Astra review corrections (candidate, not released)

Independent review of PR #350 head `847670cde226d5f7888446a16c3d5c619decf900` identified two P1 blockers despite previously green CI: a failed local UI-text save mutates the in-memory full profile, so subsequent Discard/Save can leak rejected content; a registered cloud restore can leave the expanded editor retaining an obsolete full-profile snapshot, so later saving one field can overwrite unrelated newer cloud-restored values. Review verdict: request changes; do not merge.

Same task branch now uses `UiTextCustomizationStore.PrepareSingleEdit` to clone the latest persisted profile, verify that the edited key has not diverged from its previous baseline, validate the candidate and only install it in the editor after persistent write succeeds. Reset uses the same transaction path. Unrelated updated keys are preserved; conflicting updates to the **same** key veto save and preserve the visible draft. Registered text restores now invoke `UiTextEditorPanel.NotifyExternalRestore`, while save also rereads current storage as a safeguard if a callback is missed. Added in-memory WinForms editor regression scenarios for write failure -> Discard -> saving another item, invalid replace rules, cloud restore -> save, missed restore notification, same-key conflict and stale reset. A stable editable error TextKey is provided.

**CI recovery and verified candidate build (2026-10-17):** Initial GitHub Actions attempts failed before runner assignment (zero steps); these were infrastructure/billing-blocked attempts, not code failures. After the account restriction was cleared, retry attempt 2 on the same code head `9321b2fd7580d2a93c2a6919c8f7097b752ff532` passed [GGman Windows Build #38989938130](https://github.com/xianyumht-cmd/facm/actions/runs/38989938130) and [FACM UI Text Contract #38989938158](https://github.com/xianyumht-cmd/facm/actions/runs/38989938158). The Windows job log explicitly reports `shell-ux`, `ui-text-customization`, `ui-text-cloud`, `portable-settings-cloud`, and `auto-registered-sync` smoke checks passing; Release package upload succeeded. Five optional live registered/anonymous CloudBase gateway checks were skipped. This is validated compilation and deterministic smoke coverage, **not** actual authenticated two-device cloud restore or manual WinForms acceptance. Keep PR #350 draft pending fresh independent review and native focus/keyboard/DPI/cloud tests. No merge, update-manifest change, or official release; v3.5.77 remains current.


## 2026-10-16 — UI/UX audit P1 correction candidate (PR #350; NOT MERGED OR RELEASED)

- Source baseline is signed 3.5.77, `main` commit `14806c2ef593c6cdd8dec357d8ecce04913109ab`; independent source audit is draft PR #349.
- Branch `fix/ggman-ux-p1-20261016` / draft PR #350 addresses F01–F04 only: persistent League Hub no longer closes solely on external focus loss; visible unsaved UI text asks Save / Discard / Cancel before selection/filter/mode, collapse, navigation or Hub close; action text resolves readable foreground on its actual painted fill and recommendation title uses a theme token; cleanup preview exposes its actions through the native Tab order, with CancelButton set.
- The existing League/CloudBase/update owners, cloud-first policy, user data, launcher dismissal, and release manifest are unchanged. A new stable `UiTextEditorLeavePrompt` text key is registered.
- Added deterministic shared-action contrast smoke; Windows Build and UI Text Contract plus native WinForms acceptance must pass before considering merge or a separate signed release. No CI success or live-client acceptance is asserted by this entry. See `docs/OPERATIONS.md`.


## 2026-10-15 — Cloud-first software/text sync policy and honest missing-backup state (3.5.77 candidate)

- User observed GGman cloud configuration center warning `GGman 软件偏好 发现不同配置，已暂停该项自动覆盖` while expecting remote-authoritative restore on a new machine or after switching GGman/LOL accounts. The old `Decide` algorithm treated uninitialized or other-owner local state as conflict, and distinguished software vs text policy incorrectly. Cloud-absent state was also lumped into generic `Conflict`, making a nonexistent server snapshot look like a true two-sided disagreement.
- 3.5.77 uses one cloud-first selection rule for **registered portable software preferences and UI text**. If server has a profile: unchanged -> no-op/adopt, differing on fresh link/new account/new remote revision -> restore the registered user's server profile, backed up by existing stores. Upload occurs automatically only when **this logged-in session explicitly saved a local change**, after a successful checkpoint for the same registered account, while server revision + hash still match that checkpoint. CAS errors do not silently win over concurrent server edits. Loss of remote record blocks automatic overwrite.
- When server has no profile: never silently upload local/cafe state; show a `云端尚未创建备份` message and one `创建云端备份` action per missing software/text category. Clicking explicitly seeds only a currently still-absent server record; if one appeared meanwhile, it is restored instead. When a UI text editor has a genuinely unsaved visible draft, remote restore defers until the draft is saved/discarded; no misleading conflict choice.
- LOL ESC and allowlisted lobby YAML retain manual-only publishing and event-driven safe auto-restore. No CloudBase schema, JWT/RLS, updater compatibility or user data migration changes.
- CI release gate: Windows Build/performance smoke, UI Text Contract; current-revision cloud-first decision matrix tested deterministically. Real same-UID two-device app and UI text restoration are still native field acceptance rather than CI claims. Signed 3.5.77 is non-forced and must not publish until passing gates.

## 2026-10-14 — Manual game-profile publication from a running League lobby (PR pending, 3.5.76 candidate)

- Operator reported that 3.5.75 still rejects deliberate `上传大厅与 ESC 设置` with `请先完全退出英雄联盟和大厅客户端`, even when the user finishes a training match and is back at an open League lobby. This was the obsolete upload-side `AreLeagueProcessesAbsent()` guard; 3.5.75 fixed only the auto-restore side. The confirmation prompt also told users to exit the launcher.
- In `fix/live-lobby-manual-profile-publish-20261014`, the upload entry gate checks only that the actual League game process is absent. A capture wrapper checks game-process absence before and after reading ESC and allowlisted YAML; it refuses potentially partial/in-progress game writes. `LeagueClient` / `LeagueClientUx` presence is no longer an upload blocker. The dialog explicitly says to leave training/match and return to the open lobby.
- Added an isolated fixture for the manual-upload capture of both real `Game/Config` ESC and two allowlisted launcher YAML files. Keep registered CloudBase CAS, authenticated owner RLS, explicit manual-only publication, size validation, and native read failures fail-closed. No SQL changes.
- Request signed GGman 3.5.76 non-forced from the existing publisher only after Windows CI, UI text and relevant source gates are green; a game-running upload or locked YAML remains a justified rejection. Native Riot-client behavior and writeback are owner acceptance, not CI guarantees.

## 2026-10-14 — Live lobby game-profile restoration (PR #346; release 3.5.75 candidate)

- Owner clarified the essential use case: home and internet-cafe users switch LOL accounts with the launcher open and must receive the previously manually published cloud ESC preferences **upon entering the new lobby**, not after shutting down the client. The 3.5.74 all-process-absent gate contradicted that requirement.
- On `fix/live-lobby-profile-restore-20261014`, `GgmanAutoSyncModule` subscribes to the existing dashboard Gameflow feed, periodically identifies the signed-in LOL account from read-only `/lol-summoner/v1/current-summoner` (PUUID used only as an in-memory SHA256), and wakes `GgmanAutoSyncService` after an account or safe lobby-phase transition. No separate Gameflow polling loop or Riot credentials.
- Restoring `Game/Config` ESC bytes now requires the actual `League of Legends` game process to be absent, **not** that the LeagueClient/LeagueClientUx lobby be closed. After ESC succeeds it is recorded separately. The YAML allowlist write runs independently, with byte-exact backup, field-level checks and bounded retry on file lock/writeback; YAML failure must not revert ESC or block its success. Match running and cloud-account changes remain protected.
- No SQL change. SQL 009 remains the operator-deployed table, cloud game profiles remain explicitly published only, and registered GGman program preferences retain automatic sync. Candidate release request 3.5.75 is non-forced, non-prerelease; official updater activation must follow clean CI and the existing signed publisher. Actual Riot-client in-memory YAML reload and cross-LOL-account behavior still require owner field confirmation.

- The final Windows fixture exposed an unrelated 3.5.74 YAML bug: the writer used the colon offset from a trimmed line against the untrimmed source, truncating indented keys. Corrected the offset to include leading whitespace, restored the post-write assertion and added an explicit `blockNonFriendGameInvites: true` readback check. Release cannot proceed until the corrected branch CI passes.

## 2026-10-12 — GGman 3.5.74 release request prepared (same PR #345)

- User explicitly requested direct official one-click update rather than an isolated test build. Release request `release/3.5-request.json` has version `3.5.74`, non-forced and non-prerelease, preserving minimum `3.0.0`. Production publisher must merge the PR first, then build/sign/verify a new immutable Release and enable the online manifest; no release is assumed until those steps succeed.
- Pre-release safety fixes: apply a published game preset once per registered GGman session, install, remote revision and locally observed `lcu-settings.accountId` transition, with only a local in-memory hashed marker. When a game client is still running, defer restore until **both** game and launcher processes have closed. Read only allowlisted YAML leaves; preserve CRLF and BOM, take byte-exact backups, and compare the actual published subset instead of all optional local fields.
- The user performed CloudBase SQL 009 and reported 11/11 role/grant/RLS PASS; simulated A/B CAS ran with ROLLBACK. Live anonymous read/write probes got four HTTP 401 responses. Windows Build/UI text contract on the final release-request head must still be green before merge. Registered HTTP and live League client switching were not independently accepted; they remain post-release field verification requirements, and release notes do not promise active-client hot apply.

## 2026-10-12 — Game preset synchronization redesign (draft PR #345; NOT RELEASED)

- Working branch `feat/game-config-profile-sync-20261012` now implements: no background ESC upload; cloud-first GGman software preferences with save debounce and bounded exit attempt; independent local game-auto-restore checkbox; manual combined publish UI; allowlisted mainland-China lobby YAML booleans; a versioned registered game-profile CloudBase client and **operator-deployed** SQL 009 schema; client/game-process-closed-only restore with backup and validation.
- Owner supplied the four actual `LeagueClient/Config` YAML samples. Exact confirmed preference is `LCUAccountPreferences.yaml -> lol-notifications -> data -> blockNonFriendGameInvites: true`. Nine supported Boolean fields are enumerated in `GameProfileStore.Allowed`. No owner account numbers, device IDs or full YAML files are committed.
- **CloudBase deployment verified by operator (2026-10-12):** create-once SQL 009 returned successful commit; subsequent read-only role/catalog checks 11/11 PASS (RLS enabled, owner policy, anonymous grants denied, both functions SECURITY INVOKER). An owner-run simulated authenticated A/B identity and CAS transaction completed without an exception and ended with ROLLBACK (the console did not display its NOTICE, so this is not genuine token-based registered A/B acceptance).
- **Live anonymous gateway acceptance:** GitHub Windows Build [#38211027414](https://github.com/xianyumht-cmd/facm/actions/runs/38211027414) checked both game-profile RPCs with no authorization and a freshly issued anonymous CloudBase bearer; all four POST requests returned HTTP 401. The same build and UI contract [#38211027403](https://github.com/xianyumht-cmd/facm/actions/runs/38211027403) passed on commit `413818bee76743b007674bccc95c739f430624a1`. No real registered user token or data was used by the automated probe.
- **Pending before promotion:** real registered email upload/get through the isolated Windows candidate, owner multi-device/LOL-account changes, in-client writeback behavior, gameflow timing and real ESC restore. The CloudBase DB is deployed; the client PR remains draft and must not be merged or released until native acceptance. Do not run SQL 009 again.
- SQL 005/007/008, release v3.5.73, and online manifest remain unchanged. Draft [PR #345](https://github.com/xianyumht-cmd/facm/pull/345).



## 2026-10-11 — ChampSelect companion current-patch fallback (signed 3.5.73 released)

- **Field diagnosis:** owner-provided GGman 3.5.72 diagnostics showed repeated OP.GG Mayhem/build and base ARAM HTTP 403, Hexdata HTTP 503, successful ARAMGG 26.20 selected top ten, and reachable ARAMMayhem champion detail. Source-level 3.5.72 augment recovery accepted too few rows and did not provide a separate build fallback.
- **Fix:** PR #342 source `4ccdb6225c80f28f4d9d1909557f1b3d92dc7b57`, merged as `1151a339ce48ee972fb885108cef784961dec2ab`. Reuse the existing ARAMMayhem detail transport after the parallel OP.GG build task; extract up to 12 named augment rows with rarity, appearance/win rates, core item trio, starters, boots and skill priority. Track the selected complete ranking patch separately from per-champion detail patch and reject unverified/mismatched fallbacks; extract current rank/tier from the champion overview rather than history. Add deterministic current/stale patch fallback smoke tests.
- **Verified gates:** GGman Windows Build #38138244690, FACM UI Text Contract #38138244687 and actual live Mayhem Source Probe #38138244746 all succeeded. Release request PR #343 passed Windows Build #38138407536 and UI Text Contract #38138407480, then merged as `c12971ab21a166e920ad7ec35a47c8e414ff509d`.
- **Signed publisher:** GGman 3.5 Lightweight Release #38138508017 completed successfully; public `GGman.exe` and compatibility `FACM.exe` are byte-identical (2,617,240 bytes). SHA-256 `8FC0FAFB02B24F033C5C48E1C8246063AC378767D3A0828F83FF25D1FA88D704`. Online manifest `3.5.73` is enabled, minimum `3.0.0`, non-forced. No updater, administrator privilege, LCU write/quit, or cloud database change.
- **Acceptance boundary:** CI and GitHub-hosted live source tests cannot prove the owner's China-network OP.GG 403 scenario in an actual ChampSelect. Test several current non-top-ten champions and rapid rerolls with OP.GG blocked; inspect `Mayhem companion fallback:` and `Mayhem companion fallback skipped:` logs, ensure builds/augments render, and verify stale details are not passed off as current. Do not label this native acceptance complete before direct feedback.



## 2026-10-11 — Runtime Companion Mayhem regression fix (signed 3.5.72 released)

- PR #339 merged into main as `8ffab99e2fcd38ad17d823806629804dee1b6b6d`; the GGman Windows Build #38128127495, FACM UI Text Contract #38128127492 and Mayhem Source Probe #38128127508 passed, including the actual live probe step.
- Patch: preserve unversioned or same-patch per-hero stats outside the top ten while refusing explicitly stale patch data, and label unverifiable per-hero patch evidence; attempt richer backup augments if primary parsing yields fewer than three entries; show sparse-result feedback; avoid clipping long base-ARAM balance notes in the compact, scrollable ChampSelect companion.
- Release PR #340 merged as `807b0c4144c229adc5a06d7114ee5617d42fb691`; publisher #38129253593 succeeded and enabled online signed `v3.5.72` (non-forced, minimum `3.0.0`). Public `GGman.exe` and compatibility `FACM.exe` match byte-for-byte (2,609,560 bytes each) with SHA-256 `2DB961D54E7D59D9E2DBF216C918CE0445D8630A1464839703B432562874B0C7`.
- The fixes are CI-tested but not yet validated in a live user's champion-select with Vladimir and Kalista. Preserve admin-capable updater and existing multi-source ranking; do not conflate these issues with upstream Hexdata health or the independent matchmade “退” behavior.

## 2026-10-11 — 海斗榜多来源监测收尾（已合并，3.5.71 起生效）

- **已合并：** PR #337（`3c8f0e955ad4b390416d99f45595620a793a7ee5`）将 Hexdata、ARAMGG、ARAMMayhem 各来源的完整前十、补丁与官方版本核对结果写入既有六小时定时工作流。最终提交 Windows Build #38103436962、UI Text Contract #38103436971、Mayhem Source Probe #38103437015 均通过；合并后 `main` 的探针 #38103524059 和实际 `Run live Mayhem source probe` 步骤再次成功。
- **实测来源（2026-10-11）：** Hexdata 补丁未知，0/10；ARAMGG 26.20，10/10；ARAMMayhem 26.20，10/10；选择 ARAMGG。腾讯官网补丁本次未取到，`official_patch=unknown`；**不能断言两份榜单已通过腾讯官网版本核验**。多个公开站点可能转发同一底层统计数据。
- **失效边界：** 历史定时探针 #37892168662、#37935960020、#38000380496 真实报 `Top-ten ranking is incomplete`，属 3.5.71 修复前记录。当前两条公开排行传输链可用，但 Hexdata 的 0/10 原因未定；少于两条完整来源产生降级警告，最终英雄数据不完整仍使探针失败。
- **后续验收：** 等待至少 2–3 次新的六小时定时运行，确认来源健康度和官网补丁状态的趋势。未见 3.5.71 发布后足够的 `schedule` 样本，不把 PR/main push 的多次通过当成长期可靠性证明。Blitz 当前榜单尚未核实，不作为备用源。
- **历史发布状态：** 该监测改动在已签名的 `v3.5.71` 生效；当前正式版为 `v3.5.72`（见上方由发布工作流维护的版本区块）。当时的监测任务仅改动 GitHub Actions 和文档，未修改客户端、云端数据、管理员权限或在线发布清单。操作说明见 `docs/OPERATIONS.md` 的海斗来源监测章节。

## 2026-10-11 — Mayhem ranked-source reliability (signed 3.5.71 released)

- PR #334 merged as `e322ace109f99c7fb297fbbc0e513d6c5c631096`. GGman Windows Build #38102195982, FACM UI Text Contract #38102196033, and live Mayhem Source Probe #38102195973 all completed SUCCESS. The actual `Run live Mayhem source probe` step passed, not just a `continue-on-error` workflow summary. An earlier live probe #38102048643 also passed.
- The source owner still uses Hexdata's CN-derived hero rankings, ARAMMayhem homepage/champion details, OPGG builds/augments and official Tencent patch/balance notes. ARAMGG adds a public current-patch Tencent-derived ranking transport. Choose only a whole ten-row, sequential, unique, plausible list and prefer newest patch (16.20/26.20 aliases); never merge rows across publishers. ARAMMayhem homepage parser now accepts tier and movement badges. Incomplete/official-patch-mismatched data will not be cached for ten minutes. The UI note identifies source/patch and mismatch.
- Blitz.gg's last publicly verified Mayhem pages were Patch 16.4, so it is not accepted as a current-patch fallback. These public publishers may share Tencent's underlying statistics, so the change improves transport continuity rather than creating independent match samples.
- Release PR #335 merged as `80714971056dac58b7b12fcc59cf1406823ea282`. Signed publisher #38102460283 finished SUCCESS and activated non-forced online `v3.5.71` (minimum 3.0.0). Public `GGman.exe` and compatibility `FACM.exe` are both 2,602,904 bytes with identical SHA-256 `2631634F0AB07F0B9977A067F40578D7DFB19AA3572715108814EFB01A07992A`; online manifest matches.
- No new Tencent ranking endpoint, token, database migration, admin privilege or LOL client write action. Long-run provider reachability and Blitz current-patch acceptance remain follow-ups.

## 2026-10-11 — Auto-sync checkpoint and late announcement fixes (signed 3.5.70 released)

- PR #330 merged as `a579f18b0b3248af03383ce14392b4b29fba2c44`, Windows Build #38100744085 and UI Text Contract #38100744107 SUCCESS. Auto-sync checkpoint owner/cursor changes now roll back in memory on atomic state-file save failure; a later poll re-adopts authoritative cloud CAS revision. Manual conflict prompts clear only after successful action; deterministic smoke injects save failures for app/text/ESC and previous owner.
- PR #331 merged as `9c4097a692d9c32263cff51ed0eb9e27b9f8dcdc`, Windows Build #38101008393 and UI Text Contract #38101008462 SUCCESS. Update snapshots retain in-flight official HTTPS announcement requests and apply late results to the same active window only; the version check remains responsive and stale notice updates are ignored.
- PR #332 merged as `0558b49fe5ed58e4b6fb4f6b007c33876c51d467`; signed publisher #38101299679 completed SUCCESS, publicly released `v3.5.70` with matching `GGman.exe` and compatibility `FACM.exe` (2,596,760 bytes each), SHA-256 `C0EE537BC40A2007BC42BC7CAF7F552006A432089FC1E20281324B97A3EE4844`. Online manifest is enabled, non-forced, minimum 3.0.0.
- No database migration, new token storage, update elevation change or LOL match automation changes. Real disk-error, multi-PC cloud contention and delayed announcement gateway behavior still need native acceptance. Mayhem live-source repair followed and was signed in the subsequent v3.5.71 release.

## 2026-10-11 — Registered account renewal (signed 3.5.69 released)

- PR #327 merged as `bc6e5e744fe7b2388007b3d009fc72c7d12c312e` after Windows Build #38100075723 and UI Text Contract #38100075825 passed. `ggman-email-auth` smoke verifies token rotation and rejects stale same-UID relogin requests.
- App settings, UI text, ESC and registered stats RPCs retry once after HTTP 401 via a process-wide serialized registered `auth/v1/token` refresh. Tokens remain in memory, and login generation prevents logout/relogin and A→B→A races. Cloud CAS, updater elevation and SQL schemas are unchanged.
- PR #328 merged as `83a7866cd43c6675a7cb79b419661bc09ffe7cec`; signed release workflow #38100314860 succeeded. The two public 3.5.69 EXEs are byte-identical (2,575,256 bytes) with SHA-256 `02D916ED27ADB6D65751CBE2D14028884510CB5DF02E5BA839594D3713357739`. `online/version.json` is enabled at v3.5.69, non-forced, minimum 3.0.0.
- Real CloudBase access-token expiry, refresh-token rotation, same account on two PCs, and live logout/relogin still require native user acceptance; CI cannot prove provider behavior. Announcement and live Mayhem ranking issues remain separate.
- Superseded docs-only PR #326 was closed with an explanation after its release-state marker repair was verified in merged PR #327. No branch history was rewritten or deleted.

## 2026-10-11 — Bounded network response reads (signed 3.5.68 released)

- Product constraint: preserve GGman's existing administrator-capable run/update behavior. No privilege change was made.
- PR #324 merged as `96ad7feb38badbc46166ad910949020512ae8346`; Windows Build #38099123292 and UI Text Contract #38099123368 passed.
- CloudBase anonymous responses use bounded, cancellation-aware HTTP body reads (128 KiB); LCU fetches request headers first and caps streaming replies at the established 20 MiB image budget. CloudBase smoke covers in-limit and oversized bodies.
- PR #325 merged as `3933130fb05fca5160489fa1b3d16a3b163ecbb1`; signed release workflow #38099393281 succeeded. Public v3.5.68 `GGman.exe` and compatibility `FACM.exe` have identical SHA-256 `C759B8637561886B8FF841D3AF207C2F772CD4D0A619AE2CE4A3174E66A87A62` (2,566,040 bytes each); online manifest enabled, non-forced, minimum 3.0.0.
- Registered login token refresh followed in signed v3.5.69; announcement completion followed in signed v3.5.70. The live Mayhem ranking probe remains a future investigation.

## 2026-10-10 — Default-on automatic registered config synchronization (signed 3.5.67 released)

- **Owner request and delivered UX:** only a default-checked **自动同步配置** checkbox under **LOL 工作台 → 我的 GGman**, with no ordinary cloud upload/restore buttons. A long-lived `GgmanAutoSyncModule` starts after SettingsModule and runs regardless of whether My GGman is open. Unchecking cancels pending work best-effort and persists `AutoConfigSyncEnabled=False` in local `settings.ini`. The switch is device-local and is excluded from registered account portable preferences.
- **Behavior:** existing registered software preferences (SQL 008), UI text customizations (007), and LOL ESC snapshots (005) sync periodically in the background. Per-registered-UID/per-category checkpoint in `data/account-auto-sync-state.json` stores only hashed owner, remote version and normalized content hash. Unchanged profiles are true no-ops; local-only edits CAS-upload, remote-only changes restore to local with backups; conflicting edits or cross-account ownership transitions pause that category until exceptional one-time **保留本机配置 / 采用云端配置** selection. Default local preferences and empty text overrides may be restored safely during first link. ESC fingerprints ignore snapshot creation timestamps. Unsaved UI text drafts are guarded; missing game directories never delete existing remote snapshots. Network failures back off rather than retrying aggressively.
- **Verified release:** PR #321 source HEAD `6d3b0df6c87f0e9339cc7a7457906a90e21ba7b7` passed GGman Windows Build #38028169680, FACM UI Text Contract #38028169711, Mayhem Source Probe #38028169674. Windows performance smoke explicitly passed `auto-registered-sync`, `unified-config-sync`, and `portable-registered-settings`. PR merged as `8da6fb8eba8706c9888ab1ecc5f6f66d2c42fe20`. Formal signed release #38028351950 completed SUCCESS, published `v3.5.67`; `GGman.exe` and `FACM.exe` both 2,565,528 bytes, identical SHA-256 `F2BB74255BAF55D126D42A91370A628ABD789BBF8C9C2B58C6BE107864246E14`. Online manifest enabled non-forced v3.5.67. No new CloudBase SQL was required.
- **Documentation closeout (2026-10-10):** reconciled README/current release label, replaced the obsolete anonymous sync guide with the deployed registered default-on contract, and marked 3.5.66 manual-control architecture/operations notes as historical. This is **documentation-only**, with no new build, binary, tag or online manifest change. Native cross-device and account-switch acceptance remains open.
- **Field acceptance still outstanding:** native real-user login/logout and default toggle, settings/text/ESC background push and backed-up pull, real A/B account switching in one PC, conflicts, network outages and second-device same UID after cloud revision change. CI decision tests do not prove all live authenticated network/multi-device behavior. Existing email authentication intentionally remains memory-only, so each GGman restart still requires another login.
- **Not yet included:** LOL lobby `LeagueClient/Config` YAML pending safety allowlist and write-back testing.

## 2026-10-10 — Unified My GGman cloud upload / restore (signed 3.5.66 released)

- **Owner request:** a single pair of cloud controls **上传全部配置 / 恢复云端配置**, only on **LOL 工作台 → 我的 GGman**, rather than repeated sync buttons under ESC, text, and software settings. PR #320 source HEAD `93277932eb82a2b7977f6fc09bf92a223454a83e` passed GGman Windows Build #38025249078 and FACM UI Text Contract #38025248991, merged as `2cd0273bb8a94dbd89c2301185b6e64caae0aaa3`.
- **Signed official 3.5.66:** main release request `680056cc493436c0582c9c8e6d95ddc5d2cb3b84`; publisher #38026944356 completed SUCCESS. GitHub Release `v3.5.66` published with matching public `GGman.exe` and compatible `FACM.exe` 2,540,952 bytes each, SHA-256 `A0B05B2BACF3CA47E9EA5CF53054E79D7842D5EAA74A127FBD2716962CAE0B31`. Online manifest enabled version 3.5.66 with non-forced update.
- **Now shipped:** a single registered-account config center orchestrating portable GGman software preferences, `ui-text.ini` overrides/[Replace], and LOL game ESC snapshots. ESC retains local backup/restore, text editor retains local save/restore-default; their redundant cloud buttons are hidden. Per-feature cloud versions are independent, not one global atomic transaction. Before restore, the app requests confirmation and backs up local files. Missing game ESC files skip that category without clearing the existing remote ESC snapshot. Failures stop the sequence and report completed/partial progress. Legacy anonymous **software settings auto-restore is disabled**, but old anonymous device, statistics and opt-in usage telemetry data remain.
- **SQL 008 deployed and checked by owner:** CloudBase SQL screenshot reported successful create-once script ending in `COMMIT;`; catalog screenshot verified new `ggman_registered_app_settings` has RLS=true/one policy, anon SELECT=false and registered SELECT=true, and both RPCs are SECURITY INVOKER with anon EXECUTE=false/registered EXECUTE=true. Owner SQL A/B simulation returned `PASS - GGman 软件设置云同步模拟测试通过`, testing per-owner read/write, version 1->2, stale-version conflict and RLS, with transaction `ROLLBACK`. Windows Build #38025249078 validated production no-header and temporary anonymous bearer reads/writes were denied (four HTTP 401 responses). **Do not rerun SQL 008.**
- **Still outstanding real-user field acceptance:** native Windows UI layout and actual all-three-category upload/restoration, one visible cloud action pair and no duplicate UI, real registered email account A/B read/write isolation, same UID on another machine, preserved per-PC directory/geometry, user-specific custom `ui-text.ini`, partial failures, login switch. SQL A/B simulation and Windows smoke/gateway probes do not prove these real-world flows.
- **Not included:** raw `LeagueClient\\Config` lobby YAML until account/server/install-identifier field whitelists and client write-back handling have been verified. This will be added to the same unified sync pair later.

## 2026-10-10 — UI text in-page editor + registered cloud sync (signed 3.5.65 released)

- **Published:** PR #319 final head `630baf20cee8281f9be2c6bcfb48aec0ba19843b` passed Windows Build #38021069272 and UI Text Contract #38021069220, merged as `d92ef5364a240f894306a1cf90f72ab588b6eb0f`. Formal signed 3.5.65 publisher #38021777287 completed SUCCESS, published `v3.5.65` with matching public `GGman.exe` and compatible `FACM.exe` (2,522,520 bytes each; SHA-256 `637F2AD7E48EA3B814649B350B89861AD3097E6939D0C788A7B1DD67DD7A2BBB`). Online manifest enabled v3.5.65, minimum 3.0.0, non-forced.
- **UX:** user-requested collapsible inline editor in **LOL 工作台 → 我的 GGman**, after ESC; searchable `[Text]` keys, default/current preview, editor and reset, advanced `[Replace]`, local backup-before-apply, formatting placeholders and existing runtime INI hot-reload. CI tested serializing cloud profile and repeated local INI saves preserving comments and unknown sections without duplicate `[Text]`/`[Replace]` sections. Cloud upload/restore is explicit and scoped to the signed-in registered email UID; stale remote revision is rejected, unsaved local edits cannot be silently discarded by cloud actions. Existing ESC and other settings tables unaffected.
- **Production database:** operator applied create-once `cloudbase/sql/007_account_ui_text.sql` and verified `ggman_ui_text_profiles` RLS enabled, one policy, anon SELECT=false, authenticated SELECT=true; read/write RPCs SECURITY INVOKER, anon EXECUTE=false, authenticated EXECUTE=true. Operator confirmed an A/B simulated SQL transaction `PASS - GGman 文字云同步模拟测试通过` for write/readback, version increment, stale conflict and B isolation with `ROLLBACK`. Production gateway no-credential and actual anonymous bearer read/write probes returned HTTP 401 in Windows Build #38021069272. **Do not rerun SQL 007.**
- **Outstanding field acceptance:** real registered email account A/B HTTP ownership and cross-PC upload/restore, Windows UI interaction and mixed-DPI layout, custom user INI appearance in running GGman, logout/account-switch behavior. SQL mock A/B and CI cannot establish real two-account client success; collect feedback and issue a signed patch if needed.

## 2026-10-10 — Registered personal stats migration (released in signed 3.5.64)

- Owner explicitly requested removal of 3 permanent My GGman statistics switches, local usage recording by default at GGman launch and registered account statistics on email sign-in, and migration away from anonymous-device ranking before adding broader cloud config sync. Owner screenshot shows 24 local LOL accounts and 13 activity days; preserve local `personal-stats.json` and its recovery copy.
- PR #318 merged at `a7ab6114cab4a1db9d3a107128bd46b7ff822a73`. CloudBase operator applied migration `cloudbase/sql/006_registered_personal_stats.sql`: registered-only profiles, deduplicated account-hash observations, unioned active dates and source-scoped legacy imports. Dedicated RPCs require real authenticated non-anonymous CloudBase UID, while the aggregate ranking RPC reveals no other owners' rows. Older anonymous tables remain untouched and no automatic bulk data reassignment occurs.
- Client keeps local launch/account history without a toggle. A registered session can touch its profile automatically and capture newly observed LOL PUUID as UID-scoped HMAC; the old device-scoped hashes cannot be converted or compared across devices. Explicit **导入本机历史记录** confirmation transmits only legacy count, day list and a UID-scoped device pseudonym; different device legacy counts are **maxed, not added**, while new registered hashes are deduplicated by owner. This avoids artificial cross-device doubling but is a lower bound and is not guaranteed to equal all distinct historical LOL identities.
- The main statistics switches are replaced by a single **数据与隐私** action next to the ranking refresh. Registered ranking defaults on but can be disabled there. Existing anonymous *feature usage telemetry* retains independent user consent (off by default) and is **not silently linked to the registered account**; the toggle is relocated to the privacy dialog.
- **Production schema operator checkpoint (2026-10-10):** CloudBase PostgreSQL editor screenshot shows `006_registered_personal_stats.sql` executed successfully through `COMMIT;`, reported 0 affected rows (normal for DDL), and no visible SQL error. Preflight screenshot immediately beforehand showed all 8 target objects absent. **This does not yet establish** the tables/functions and their RLS/grants are correct under live accounts. Do not execute the CREATE-once SQL again; next inspect PostgreSQL catalog metadata read-only, then test real authenticated A/B and anonymous HTTP requests.
- **Operator-confirmed simulated A/B registered-statistics acceptance (2026-10-10):** production CloudBase editor returned `PASS - 注册账号统计模拟测试通过` for the transaction that ran A profile/account repeated upserts, 24-count legacy import replay and 3 active days; B per-owner read isolation and its own account count; A ranking opt-out persisted across ordinary touch; script finished with `ROLLBACK`. The prior catalog checks reported 4/4 tables with RLS enabled/anon SELECT denied/authenticated SELECT permitted, and 4/4 RPC security modes (3 INVOKER + one aggregate DEFINER) with anon EXECUTE denied. These tests validate role-simulated database behavior, **not real HTTP registered A/B identities**. A live gateway check verified all eight no-credential/anonymous bearer requests to four RPCs returned HTTP 401, with no server-side persistent writes by either test case. Do not re-run SQL 006.
- **Official signed 3.5.64 released:** final PR HEAD `9939617e99fcc71e996d4e4b0fe10fa8f718769e` passed GGman Windows Build #38018398089 (including 8 live anonymous/no-credential RPC denials) and UI Text Contract #38018398054. Publisher #38018593767 completed SUCCESS; public `GGman.exe` and `FACM.exe` both 2,493,848 bytes, SHA-256 `37DBB4A7DD91286EC519A93A4858C8A00F6A2857F99DEA31DA441D1CF60D9D4D`; online manifest enabled v3.5.64 (non-forced). SQL 006 operator deployment, catalog access, role-simulated A/B and anonymous live HTTP denial are verified. **Remaining field acceptance:** two real registered email users' HTTP isolation, actual Win10 legacy-history import/replay, second-device registered UID and account dedup, ranking opt-out UI, and native client reporting. Existing local and anonymous histories were not erased, and separate usage telemetry remains independently opt-in.

## 2026-10-10 — WeGame ESC Config selection fix (3.5.63 released)

- Owner's production 3.5.62 screenshots confirm path selection erroneously landed on `E:\\WeGameApps\\英雄联盟\\LeagueClient\\Config` (YAML client preferences only) and clicking cloud upload reported no ESC files. The real ESC files (`game.cfg`, `input.ini`, `PersistedSettings.json`) exist under the sibling `Game\\Config` (JSON 54 KiB, within current limits). Root cause confirmed in `EscGameDirectoryLocator.FindValidatedConfig`: the prior `hasEscFiles || hasGameClient` condition accepted a path merely because a client executable was found, and tested the launcher client Config before the real game Config.
- New fix accepts only folders containing at least one allowlisted ESC file; prioritizes `Game\\Config`, expressly ignores `LeagueClient\\Config` as an ESC source, and shares this correction with lower-level capture/restore path normalization. Tests reproduce WeGame's `LeagueClient\\Config` YAML and `Game\\Config` ESC sibling structure and resolve correctly from launcher exe, game exe, install root and stale client-config hint. Preserves previous legacy root Config behavior for actual ESC files. No CloudBase SQL or authentication changes.
- Owner screenshot also showed stale 3.5.61 advice (`恢复前须完全退出英雄联盟`) under the 3.5.62 interface: local `ui-text.ini` retains existing values from older templates. Added narrowly scoped migration of four exact prior stock ESC guidance strings to current defaults while preserving any personalized copy; this is not evidence of a still-present game-running process block.
- **Official 3.5.63 released:** PR #317 head `29b5a340c2788522858ce2266e7ae5824842e006` passed GGman Windows Build #38014601136 (including WeGame auto-locator and capture smokes) and FACM UI Text Contract #38014601134; merged as `b16ef913aff6a65feaeca96b9d5e955310a2df7d`. Official signed publisher #38014755512 completed SUCCESS, published `v3.5.63`, verified public `GGman.exe` and compatible `FACM.exe` (2,479,000 bytes each, SHA-256 `C3DC5FDD674EA17ECA913D4B7E168B4ED56AA40D4844A04AF723135361F983AF`) and enabled `online/version.json` for 3.5.63 (no forced update). **Real owner Windows upload remains to be verified**; CI validates local file capture but cannot prove Tencent CloudBase authenticated upload and UI state in the owner's game installation. 

## 2026-10-10 — ESC account-page relocation and live-game handling (3.5.62 released)

- User requests **ESC cloud backup/restore under LOL 工作台 → 我的 GGman**, not the settings/tray menus. Added an embedded inline ESC section beneath existing stats and privacy cards; preserved responsive scrolling, auto-enable cloud controls on sign-in change and retained registered-owner RPC, optimistic revisions and manual confirmations.
- Path detection is no longer a required manual folder selection: `EscGameDirectoryLocator` inspects League processes with main-module/WMI paths first, current configured game path second, and matching uninstall registry entries (HKCU/HKLM, both registry views) third. All hints must resolve to an existing League Config folder with allowlisted ESC files or an installed League client marker. No unrestricted disk crawl, arbitrary `Config` acceptance, admin credentials or background polling; manual folder selection and re-detect remain fallbacks.
- **Active-game handling:** remove all blanket League-process-not-running gates from snapshot, upload and restore. Preserve actual file locks/permissions, SHA-256/size validation, pre-restore local snapshot, atomic file replacement and best-effort rollback. Inform users that runtime in-memory settings may not reload until game restart and the client may write its values on exit, meaning a disk write during a match is not guaranteed persistent. Native Tencent League acceptance is still outstanding; do not claim in-game persistence is proven.
- **Released:** PR #316 head `12888e62d09103e71ca8f9ac2a3ebc6c073bf2e6` passed Windows Build #38013618766 and UI Text Contract #38013618767, merged as `a2a8f41b710e8f107f4419d9ec2afa6cd7aeaa38`. Formal signed 3.5.62 publisher #38013772954 completed SUCCESS. Public GGman.exe / compatibility FACM.exe both 2,476,440 bytes, SHA-256 `C757DEE38D8F23FA5BC3C9C74DBBDF6E2842A54A64BC8F713982D1B6AC9D2852`; online manifest enabled 3.5.62 with minimum 3.0.0 and no forced update. Real Tencent client path-detection visual and active-game write persistence remain field acceptance, not proven by CI.

## 2026-10-10 — Registered GGman account and ESC cloud backup (3.5.61 released)

- **Production release complete:** AUTH-1 PR #314 merged as `e9df6076c2525320b727648547e67f96cceb5227`; ESC-2 PR #315 merged as `69cb538ecb64eb4fb6e18926df37205fae4952c7`. Signed official GGman 3.5.61 publisher [#38012546613](https://github.com/xianyumht-cmd/facm/actions/runs/38012546613) succeeded, verified both public `GGman.exe` and backward-compatible `FACM.exe` (2,469,272 bytes; identical SHA-256 `677CAF64E2E03337118541C6B3F13F7289010B0F4BBAC41717C804F3C86BC479`), and enabled `online/version.json` for 3.5.61 (minimum 3.0.0, no forced update). This is one full-feature official release rather than detached test artifacts.
- **Account status:** owner screenshot confirms actual CloudBase email verification/sign-in and acceptable account management UI. Optional login, logout, provider CAPTCHA-on-demand with bounded retry and process-only credentials are in published source; anonymous GGman settings/history remain independent. Other provider tests (live CAPTCHA enforcement, logout revocation, two-device same registered UID, distributed email abuse) are not demonstrated by that screenshot.
- **Database status:** operator applied revised `cloudbase/sql/005_esc_profiles.sql` in environment `ggman-d4gioqqcz434d9e4d`, with screenshot verifying table/RPC presence, RLS enabled, anon SELECT=false and authenticated SELECT=true; both RPCs are SECURITY INVOKER, anon EXECUTE=false, authenticated EXECUTE=true. Operator confirmed a full mock-role A-upload/A-read/B-read/ROLLBACK transaction succeeded and B received `{}`; this is database simulation, not a second real HTTP registered account test.
- **Live gateway evidence:** GGman Windows Build PR run [#38012181403](https://github.com/xianyumht-cmd/facm/actions/runs/38012181403) called production `ggman_get_esc_profile()` with neither credentials nor a freshly generated anonymous bearer; both returned HTTP 401. The initial CI failure was an inherited PowerShell `$LASTEXITCODE` wrapper mistake, fixed at `afb0f2dc996ce00b25ca7d33745f3e5fc9b51a2b`; final-head build and gateway gate [#38012295709](https://github.com/xianyumht-cmd/facm/actions/runs/38012295709) passed.
- **Published ESC behavior:** user-initiated local snapshots/restores of allowlisted `Config` files plus registered-account cloud upload/manual restore, revision conflict guard, previews, pre-restore local backup and best-effort rollback. No background upload, anonymous ESC migration, saved account tokens or League LCU writes were introduced.
- **Open post-release acceptance:** real registered A/B HTTP ownership and simultaneous-version conflict, real Tencent/Riot League ESC settings persistence and cross-PC restore, mixed DPI behavior, and provider CAPTCHA/quotas/logout. These are not established by the signed build or SQL mock tests; collect runtime results and issue a separately verified patch if needed. Client success/error messages must not be treated as a substitute for checking actual in-game settings.

## 2026-10-10 — Cross-entry and legacy UI consistency closeout (3.5.60 published)

- Issue #300; task branch `fix/ggman-shell-entry-consistency-20261010`. Verified enabled official 3.5.59 manifest and new main baseline before editing. Fixed two concrete shell UX inconsistencies: tray "Open control center" and tray double-click previously called the toggle method (closing an already-open panel); the legacy compact League entry triggered the Hub through a side-effect in `FindGroup` and displayed a false unavailable status. Explicit tray opens now activate the panel, while only floating-ball clicks toggle. Legacy League entry now routes directly via typed `LeagueHubUiBridge`; group lookup is pure and the old dropdown remains absent.
- Reused the existing `使用指南` in tray More as well as the launcher Settings menu without introducing a new window. `ShellUxSmokeTest` covers the absent legacy League dropdown and stable More lookup; existing Hub routes and smoke remain intact. No new LCU/League requests, settings migrations, update protocol changes or floating ball behavior changes.
- **GGman 3.5.60 officially published and online-enabled:** PR #312 squash merged as `dc35ca29e8faa9dcc8b145ea6c267004addee9fa`; final task head `ce10f83be3d8f8253d7fc8e9e37d8f29935f59fc` passed GGman Windows Build #37998856350 and FACM UI Text Contract #37998856405. Signed lightweight publisher #37999091852 completed SUCCESS after validating release executable identities, Authenticode and publicly downloaded bytes, then enabled online manifest v3.5.60. Public `GGman.exe` and compatible `FACM.exe` are byte-identical, 2,402,200 bytes each, SHA-256 `659C8F26762DF509FE45381C53929FDE72DE1E0E545BD51B9AA2F71599B57165`; the online manifest retains `FACM.exe`, minimum 3.0.0 and `force_update=false`. Actual Windows tray/focus, mixed-DPI and in-game UI acceptance remain unverified by CI.


## 2026-10-09 — First-use quick launcher orientation (3.5.59 formally published)

- Issue #300; task branch `feat/ggman-first-use-orientation-20261009`. Verified public enabled 3.5.58 before changes. Existing `SettingsModule.WasSettingsCreatedThisRun` was insufficient to distinguish migration/recovery. Added `IsFreshInstallation` computed from current, recovery and legacy settings existence before `AppSettings.Load`. Only a genuinely fresh install (and not a `--cleanup` startup) can get an automatic opening of the normal floating-ball quick launcher.
- `DesktopLauncherEnhancer` adds a dismissible lightweight guidance card with '打开工作台' and '知道了' above the four original tiles; normal menus and context card keep previous geometry, onboarding menus reserve 122 logical layout units. The popup's '更多设置' label becomes '设置', and its existing submenu gains '使用指南' for manual reopen. The auto-reveal is skipped if current League gameflow suppresses the floating entry; no second window, persisted onboarding flag, LCU read, telemetry write, network request or new setting schema was introduced.
- `ShellUxSmokeTest` validates fresh-install eligibility vs existing/recovery/migration cases, plus normal/context launcher geometry and footer non-overlap. **GGman 3.5.59 officially signed, published and enabled**: PR #311 merged as `d56768244c25d3c4c559c1711944e2cc9c98e139`; final head `3c516545f9d9986207260823de9dea30e86ed883` passed GGman Windows Build #37956729991 and FACM UI Text Contract #37956729962. Official signed release workflow #37956999775 completed SUCCESS, verified public binary identity/Authenticode and matching bytes, then enabled the 3.5.59 online manifest. Release has byte-identical `GGman.exe` and compatibility `FACM.exe`, each 2,401,688 bytes, SHA-256 `DCD59D760FF0FA7D42382E2B272B05EFDA369F4E34FFF57A94C74A19CC4010B7`. `online/version.json` points to compatible `FACM.exe`, minimum 3.0.0, no forced update. Real Win10/Win11 mixed-DPI, first-launch focus and actual League-client visual acceptance remain pending.


## 2026-10-09 — Update Center status and release notes polish (3.5.58 published)

- Issue #300; branch `feat/ggman-update-center-ux-20261009`; verified enabled public 3.5.57 before work. Scoped UX fix: keep the existing 560×620 Update Center window, GGman branding and the startup auto-update setting, but give long version notes a separate native scrollable read-only field, rebalance the announcement card, and distinguish unchecked/failed/verified/update-in-progress states. Download/installation failure now retains its error state rather than being overwritten by “update available”; metadata failure also blocks stale install actions. Added pure multiline-notes/eligibility/error-snapshot checks in the existing `UpdateMirrorSmokeTest`.
- The download target/mirror resolution, cryptographic hash/signature checks, trusted update metadata, updater replacement/restart, 3.5.40 compatibility download alias, forced update/exit behavior and persistent settings format remain unchanged. No Gameflow/LCU work in this pass. Real Windows DPI and actual download failure UI acceptance remain unverified by CI.
- **GGman v3.5.58 formally published and online-enabled:** PR #310 merged as `f00bb29bcbf0852401aad9ffc8ed31cd356b9485`. Final task head `9991cf9e1a31d0b9b9d6bb9e764cd3c08f933ced` passed GGman Windows Build #37954609256 and FACM UI Text Contract #37954609063. An earlier UI copy check failed because added status literals were not keyed; the final fix registered `OnlineDownloadProgressFormat`, `OnlineInstallerStarting` and `OnlineUpdateReady` in `UiTextKeys` / `UiTextCatalog` and passed both gates. Official signed publisher #37955033754 completed SUCCESS, verified both public executable identities/signatures/bytes, and enabled `online/version.json` for 3.5.58. Both `GGman.exe` and compatibility `FACM.exe` are 2,397,592 bytes with SHA-256 `42199788BD35971F698375FC5218EB5F34B5542EED0AE82330B7786E428487F5`; online manifest points to `FACM.exe` and retains minimum 3.0.0 without forced update. Native Windows mixed-DPI, actual failing network/install paths and real update-window screenshot acceptance remain unverified by CI.


## 2026-10-09 — My GGman responsive profile page (3.5.57 published)

- Issue #300; branch `feat/ggman-my-profile-responsive-20261009`. Current verified public release before work was 3.5.56. User reports testing the previous release, without detailed DPI/client test evidence. Next distinct UI slice: resize `LeaguePersonalStatsForm` inside the existing single-sidebar workbench instead of replacing it or adding a new page. Four original metrics/history/ranking/preferences panels now adjust to available width in an AutoScroll content canvas, and status remains vertically reachable.
- Added deterministic layout bounds smoke (six widths) to the existing `FacmHostSmokeTest`. Retained local history, cloud ranking and telemetry controls, shared CloudBase/LCU owners and all preference saves; no new network activity or privacy/storage migrations.
- **GGman 3.5.57 formally published and online-enabled:** PR #309 merged as `54d5c8015345ae7381d3777db4d631e21fc5f4f3`; final task head `64924d6b1cf171be320f7f12a037a869038d1c3d` passed GGman Windows Build #37952532100 and FACM UI Text Contract #37952532047. Official signed publisher #37952764428 completed SUCCESS, including public executable signer and byte verification; `online/version.json` enabled v3.5.57. Public `GGman.exe` and compatibility `FACM.exe` are byte-identical (2,394,008 bytes; SHA-256 `6EF46E621146AC64AEDC8DAA8ABFDE0A6CD49224745FDB611C920B4BDCF15A3D`). The manifest points to the compatible `FACM.exe` asset. User had reported testing v3.5.56, but no explicit results or DPI screenshots were provided; native Windows mixed-DPI and live League visual proof for v3.5.57 remain pending.


## 2026-10-09 — Regression pass for six UI releases (3.5.56 published)

- Issue #300; task branch `fix/ggman-ui-regression-20261009`. Verified enabled official 3.5.55 manifest before work. Cross-page source and existing smoke review covered the floating-ball-first Hub navigation, player history, Recommendation, Mayhem responsive shell and narrow runtime companion. Deterministic issue found in the Recommendation footer introduced during v3.5.53: at narrow embedded widths, status text intersects the Refresh control.
- Patched `LeagueRecommendationForm` footer geometry: 94px stacked status/actions below 480 logical pixels, preserving the 69px row otherwise, with seven-width bounds/non-overlap smoke. Corrected `LeagueHubForm` / `LeagueHubUiBridge` contextual startup: requested valid route is resolved before `Shown`, avoiding an unnecessary Dashboard form creation/disposal; defaults to Dashboard for empty/invalid requests. Dashboard smoke now checks all nine routes and invalid-route fallback. No League queries, LCU writes, runtime companion behavior or primary floating entry path changed.
- **GGman 3.5.56 officially published and online-enabled**: PR #308 squash-merged as `1d439116f5ffe8a59ac843e8d6292809a4036c7e`; final task SHA `007b56fa5b5ce86fd47e45deabe3a02868252b9e` passed GGman Windows Build #37950464321 and FACM UI Text Contract #37950464331. Canonical signed release publisher #37950777958 completed successfully, publicly verified signer and bytes, and enabled `online/version.json` for 3.5.56. Both official `GGman.exe` and backward-compatible `FACM.exe` are 2,391,960 bytes; SHA-256 `47A84C626483E7DE3DA408CFA09E9927770A342E3DA038738C1B06B79DFF2075`, with the manifest referencing `FACM.exe`. Real Windows mixed-DPI and live League visual acceptance **remain pending**, not established by CI. Separate Mayhem live source issue #306 remains independent.


## 2026-10-09 — Narrow companion readable scroll affordance (3.5.55 published)

- Issue #300; task branch `feat/ggman-companion-readability-20261009`. A passive 4px scroll-progress cue is docked beside the existing `LeagueRuntimeCompanionForm` recommendations body, using existing scroll/content metrics and hidden when all sections fit. Native Gameflow/LCU presentation/read/write owners, header identity, Bench gating, 320px compact width, pin/collapse window-state persistence, and nonactivating behavior remain intact.
- Accessible labels for pin, collapse, quit-current-select, recommendation alternatives and Bench/augment page controls are synchronized with their visible behavior, with an existing localized tooltip that more content is available on scroll. Pure smoke asserts top/middle/end and no-overflow indicator bounds.
- **GGman 3.5.55 formally published and online-enabled**: PR #307 squash-merged as `935e12f097947b9c09c740ef746aa2dcc0b4bcb4`; final task head `b383a450d3639636f1b6559c1da9d092545515c2` passed GGman Windows Build #37947339766 and FACM UI Text Contract #37947339670. Official signed release workflow #37947598781 succeeded, including public executable signer/bytes and updater enablement. GitHub Release publishes byte-identical `GGman.exe` and backward-compatible `FACM.exe` (2,389,400 bytes each; SHA-256 `C1BA17DBFFC813C41425BF655079ACF510813B20F323343B3A962E1A40B8DD71`). `online/version.json` is enabled for 3.5.55 and points to the compatibility `FACM.exe` asset. Real Windows mixed-DPI screenshot and live League interaction acceptance remain pending.


## 2026-10-09 — Mayhem and ChampSelect visual convergence (3.5.54 published)

- Issue #300. Branch `feat/ggman-mayhem-companion-ux-20261009` updates `MayhemLookupForm.cs`, `MayhemLookupLayout.cs` and the legacy `LeagueChampSelectAssistantForm.cs`. Scope: public GGman names in Mayhem dialogs/image defaults; native accessible labels for search/buttons; clearer cancel status; extended responsive non-overlap smoke at smaller sizes; shared design tokens and accessible action chrome/status feedback on the Bench-gated ChampSelect assistant.
- Existing floating-ball primary shell, `ShowWithoutActivation`, fixed companion episode sizes, and canonical RuntimeCompanion, Gameflow, Bench swap, Mayhem query, LCU read/write, cache and auto-guide service owners are unchanged. Native Windows multi-DPI and real-client UI verification remain outstanding.
- **GGman v3.5.54 formally published**. PR #305 squash-merged as `265eaeff81529909970ec149f6bba985c492093c`; final PR head `61ac48c864e18d60d060c0be8aa95617956ae932` passed GGman Windows Build #37944659951 and FACM UI Text Contract #37944659869. Official signed publisher #37945011327 completed successfully, publicly verified signer/bytes, and enabled `online/version.json` v3.5.54. GitHub Release includes `GGman.exe` and compatibility `FACM.exe` (2,386,840 bytes each; SHA-256 `1F0A4BA8872A2EE84A683C07FE24710265B550FD780073642A0276C21E472DAF`). Real Windows multi-DPI and League screenshot acceptance remain pending.
- **Separate known data-source concern**: live Mayhem probe #37944660099 reported `Top-ten ranking is incomplete` from upstream live content; the prior main branch scheduled probe #37935960020 also failed. Because this is not fixed by the UX release, do not claim data-source health from publisher success. The pull-request probe has `continue-on-error`, so its overall workflow conclusion can be success even while the live probe step fails. Investigate upstream data separately.


## 2026-10-09 — Recommendation UX pass (3.5.53 published)

- Tracking Issue #300; branch `feat/ggman-recommendation-ux-20261009`. Scope: League recommendation page only. Converted fixed body to scrollable responsive 1/2/3-column selection/preview layout, pinned action/status footer, accessible native button focus, consistent design-system token colors, semantic apply/auto-apply status feedback and matching theme restyle selector. Added deterministic layout and status smoke. Kept recommendation read, confirm, apply, deduplication, auto-apply ownership, client state, updater and floating-ball main entry unchanged.
- **GGman v3.5.53 formally published:** PR #304 squash-merged as `de7505ff5f5c5bacc1f8176c69462ef666be724b`; branch final head `88b7c9d816e3880828f6eccab4e93df55227739b` passed GGman Windows Build #37942836518 and FACM UI Text Contract #37942836778. The official signed lightweight publisher #37943085536 succeeded, verifying the publicly downloaded signature and byte-identical `GGman.exe` / compatibility `FACM.exe` (2,385,816 bytes each; SHA-256 `AA7605B7B62F200DF8283D50EEAE9B6CBE0E7BB94652BC557ADD1468A37C32A9`). `online/version.json` now exposes enabled v3.5.53. Real native Windows multi-DPI screenshots and live-client visual acceptance remain pending; do not claim these are proven by CI.


## 2026-10-09 — Player history UX pass (3.5.52 published)

- Active follow-on Issue #300, branch `feat/ggman-player-page-ux-20261009`. Scope is the existing `LeaguePlayerForm` only: resize lists/columns/buttons by actual embedded form size, show explicit empty/loading status in the match area using established localized copy, and use win/loss colors only in result cells. `LeaguePlayerSmokeTest` adds deterministic bounds/column-width regression checks. No changes to match HTTP endpoints, cache, data enrichment, Gameflow ownership, cloud storage or floating-ball primary entry.
- **GGman 3.5.52 formally published:** PR #303 squash merged as `6f2ff661c300ab14040d7d37182ef8b9d82cda86`; final head `c55258a5263ccab920c43ec4aa79caa84afefa0b` passed Windows Build #37939538526 and UI Text Contract #37939538483. The canonical signed 3.5 publisher #37939819769 succeeded, with released byte-identical `GGman.exe` and `FACM.exe` (2,382,232 bytes) and SHA-256 `558F93AA78BE4796EF5F44E1D9481078C4DC5BBB468810DA16CC5AA1D9C0A43D`. Online manifest for 3.5.52 has `enabled=true` and points to compatibility asset. Actual Win10/Win11 multi-DPI and real League screenshot acceptance remain pending; never call these tests passed based on CI alone.


## 2026-10-09 — GGman workbench navigation redesign (3.5.51 published)

- Issue #300; task branch `feat/ggman-hub-single-nav-20261009`. Source change replaces League Hub's category/sidebar + top subnavigation + optional right context dock with one directly clickable grouped left rail and full-width content. Existing nine view IDs, contextual `ShowView` bridge, child Form lifecycles and Gameflow state source remain intact; no new network owner, game actions or long-running UI surface.
- Smoke tests now require route parity and compact sidebar width, rather than assertions about removed docks/tabs. Native Windows/DPI screenshots and real-client UX validation remain outstanding; automated gates must pass before publication.
- **GGman v3.5.51 formally published and verified**. PR #302 merged via squash as `276eac2b64f11d0fa71be30c694e6602bf841472`. Branch final head `894124e0b2d7e86e820980437a86aa035f6a4115` passed GGman Windows Build #37937642152 and FACM UI Text Contract #37937642173. Official publisher #37937911772 passed, verified public signer and byte-identical compatibility executables (2,378,648 bytes each; SHA-256 `C88811D80E8F07042B18C1FA56C21E6F0D480787FDCDE8952C9C9E3AC41DAC95`), and enabled the version 3.5.51 online manifest. Real Windows visual/DPI proof remains pending.


## 2026-10-09 — Floating-ball-first UX 3.5.50 (published)

- User confirmed floating ball is the **primary unobtrusive entry**; full workbench is optional on-demand. Iterations must use formal versioned online releases and durable changelogs after standard safety validation; detached candidate packages are not the accepted publication workflow.
- UX tracking: Issue #300; branch `feat/ggman-product-ux-spec-20261009`, PR #301. First implementation slice changes `CompactMenuForm.cs` and `DesktopLauncherEnhancer.cs`: typed launcher integration instead of private reflection, two-column/two-row League shortcuts, utility footer, consistent compact shell painter/rounding, full patch version in launcher header. Floating-ball ownership, process/tray exit semantics, game-state reading and League write routes are unchanged.
- R0 source design/audit docs live in `docs/GGMAN-PRODUCT-DESIGN.md` and `docs/UX-AUDIT.md`. Browser interactive mock is only a design reference; Windows-native UI fidelity and real game-client acceptance are still unverified.
- Release **v3.5.50** verified: PR #301 squash-merged as `f31f8e0b9192c2a43217baee81083dcf5b2404ce`; PR Windows Build #37935700229 and UI Text Contract passed. Official publisher #37935975938 succeeded; signed public `GGman.exe` / compatibility `FACM.exe` are byte-identical (2,381,720 bytes; SHA-256 `F13A8CC1B3B3775D6B8DEFD0D2DB2E639A27432A9C090EE624E250DCD396119E`); `online/version.json` is enabled and points to v3.5.50. Browser prototype and tests do not establish real Windows DPI/League-client screenshot fidelity, which remains follow-up work.


## 2026-10-09 — GGman product-wide UX redesign (R0 design review, no runtime change)

- Tracking Issue: #300. Design-review branch: `feat/ggman-product-ux-spec-20261009`. Current `main` / online release remains GGman 3.5.49; **do not represent this as a published UI update**.
- Source-audit and proposed design contract: `docs/UX-AUDIT.md` and `docs/GGMAN-PRODUCT-DESIGN.md`. R0 scope includes floating entry/quick access, main workbench/navigation, companion, settings, My GGman, status/feedback semantics, visual tokens, privacy, accessibility and release gates.
- An independent offline HTML interaction prototype was rendered and smoke-checked (main workbench, quick flyout and narrow companion). It uses simulated client states and does not access LCU or modify GGman; its browser tests are not Windows/League acceptance.
- Verified high-impact source concerns: launcher enhancement relies on reflection against legacy compact window fields/methods; League Hub composes primary sidebar, top secondary navigation and contextual right dock; several product pages use fixed geometry; legacy public `FACM` copy remains on an updater error path. These are source findings, not proof of exact live visual defects.
- **Next gate**: review actual GGman 3.5.49 Windows screenshots and obtain feedback on the candidate style/navigation, then implement a small shared-component/launcher pilot with required tests on a separate scoped branch. Do not merge/publish/rewrite working League owners based only on prototype approval.


# FACM Project State

## 2026-10-09 — Champion Select “退” patch (released 3.5.49; live matchmaking proof pending)

- Supplied GGman logs show successful primary quits in practice/custom sessions but repeated HTTP 400 rejections on 2026-09-24 and 2026-10-02/04/07 in matchmade ChampSelect.
- PR #298 squash merged to `main` as `9afc9470eda4e7ad3c782d7b2baf163e68c8e6d8`. The patch uses one guarded Gameflow request-lobby fallback after definite primary HTTP 400, with original party ID/member preservation and phase/session postconditions. No process kill, lobby DELETE or new lobby POST is allowed.
- Deterministic fake-LCU regression covers primary/fallback acceptance, rejection, unavailable or replaced party, stale phases, cancellation and repeated-click cooldown. Branch head `e084370e960ace2c20018843738b6bf6d27ff520` passed GGman Windows Build #37907957751 and UI Text Contract #37907957723.
- GGman 3.5.49 public release workflow #37908278265 passed, published signed/verified compatible `GGman.exe` and `FACM.exe` assets, enabled `online/version.json`, and recorded release SHA-256 `A83289E39E7D4E0B78B84BB3172093B8614D7D9083297600BFBB7C1CA0C8F6A1`.
- Tencent matchmade behavior remains **unverified on a real client**; CI and release checks establish source/build/update correctness, not server acceptance.



更新时间：2026-09-26

## 当前产品线

FACM 只维护 **3.5.x lightweight**：WinForms / .NET Framework 4.8 / 单 `FACM.exe`。4.x 已退出默认工作树、当前 CI 与发布链；历史实现只保留在 Git 历史、旧 tag/release/remote branch/旧 PR 中，不作为当前产品依据。

当前在线正式版是 **GGman 3.5.43**。在线更新已启用，`minimum_version=3.0.0`，`force_update=false`。后续实机发现问题按普通 3.5.x patch 修复，不回到 4.x 产品线。

## CloudBase 设备身份 P1（已合并并随 GGman 3.5.41 引入，3.5.43 继续包含）

- PR #288 已 squash merge 到 `main`；功能发布基础 commit 为 `6e3a39391841e7e5d2292ab00655af5a416577b7`。
- 腾讯 CloudBase 环境使用 `ggman-d4gioqqcz434d9e4d`；P1 只建立便携 `data` 目录、稳定随机 `device_id`、匿名会话和 `ggman_devices` 一次启动同步/回读验证。
- access/refresh token 不落盘；本地只持久化 `device_id`、匿名 CloudBase UID 与 last-known-good 身份副本。
- 云端同步保持 fail-soft，CloudBase/网络失败不得阻止 GGman 启动或 League 本地功能。
- 本阶段没有引入设置云同步、账号历史同步、遥测上传、SQLite、新运行时 DLL、第二 League/LCU 轮询器、服务端 API Key 或硬件/IP 指纹。
- 发布前最终任务 head `083a8532a987b3a3bd67d83f325f7e9aac4b45a4` 通过 GGman Windows Build #1957、UI Text Contract #1063 与 Mayhem Source Probe #772；正式发布工作流 #23 全部通过。
- v3.5.42 已修复 3.5.40 更新兼容：公共 Release 同时发布字节一致的 `GGman.exe` / `FACM.exe`（2,304,408 bytes，SHA-256 `497CB5BB1D953EA4CDA83C7728EAE3F266557A68A74B2828BECC091A5EEF8C1A`），`online/version.json` 已启用并指向兼容 `FACM.exe` 资产。
- 2026-09-26 已完成真实客户端 CloudBase 端到端验收：GGman 3.5.42 启动后，`ggman_devices` 成功新增 1 条真实记录，`app_version=3.5.42.0`，设备随机 `device_id` 与 CloudBase `owner_id` 均成功落库；`recovery_code_hash` / `fingerprint_hash` 仍为空，符合 P1 范围。由此确认匿名认证 → access token → PostgREST → PostgreSQL RLS → read-back 链路在真实环境已打通。

## Personal stats / “我的 GGman”（PR #290，未合并/未发布）

- 任务分支：`feat/personal-stats-profile-20260926`，基于 main `5ec5b0103937dc3bd8dab8924791efd1151b0700`；任务 PR：#290（draft）。
- 新增 `data/personal-stats.json` + last-known-good：记录 GGman 首次/最近使用、活跃日期和去重账号哈希，不持久化原始 PUUID、账号名、密码或 LCU 凭据。
- League 账号识别复用 `LeagueDashboardModule` 的唯一 Gameflow owner；只在现有 Gameflow 状态变化事件上读取 `/lol-summoner/v1/current-summoner`，用上次捕获的账号 hash 去重，因此同一客户端会话内切换账号也能被识别，同时不新增第二 Gameflow 轮询器。
- 账号 key 使用 `HMAC-SHA256(device_id, PUUID)`；客户端只在内存短暂接触原始 PUUID，云端/本地历史均只保存派生 hash。
- LOL 工作台新增“我的 GGman”页：玩过账号数、活跃天数、加入日期、匿名排行，以及“记录本地足迹 / 参与匿名排行”两个开关。Local stats 默认开启；cloud ranking 默认关闭。
- CloudBase migration：`cloudbase/sql/002_personal_stats.sql`，新增 `ggman_devices.ranking_opt_in`、`ggman_record_account` 和 `ggman_get_personal_stats`；global rank 只返回 caller 的 count/rank/population/percentile，不开放其他用户记录。
- 代码 Gate：任务代码 head `66e6d5d39ba94084313be2914008fc290e1d689a` 已通过 GGman Windows Build #1986、UI Text Contract #1092、Mayhem Source Probe #795；此前 CI 发现的 UI 文案注册、Host smoke 构造器和 Shell UX view-count 漂移均已按仓库 contract 修复。
- 2026-09-26 已在生产 CloudBase 环境执行 `cloudbase/sql/002_personal_stats.sql`；控制台验收确认两个 RPC 存在。随后 Build 1987 完成真实客户端验收：`我的 GGman` 成功记录 1 个账号、CloudBase `ggman_account_history` 出现 64 位派生 hash、匿名排行返回 1/1 与百分位，证明 current-summoner → 本地 HMAC → account RPC → ranking RPC 链路已打通。根据实机截图收口：正式 UI 不再显示 `第 N / N 名`，只保留百分比；同时将旧 `ui-text.ini` 中未改动的 FACM 品牌默认词运行时迁移到 GGman。
- 本阶段不做 general telemetry、全设置云同步、硬件/IP 指纹、SQLite/native dependency 或版本发布。

## 当前已交付行为

- Mayhem 百分比单位修正，长内容/装备/强化展示完整性改善；3.5 快速数据链未重写。
- Lobby 进入后立即评估自动寻找，不再固定等待 1500 ms。
- ReadyCheck 立即评估自动接受；失败可在同一 episode 内短间隔重试并做最终状态 reconciliation。
- Matchmaking 写失败/结果不明确时读取 queue state，避免“已生效但响应丢失”造成重复 POST。
- disconnected/null Gameflow cadence 为 3 秒；ChampSelect 约 2 秒、Queue/ReadyCheck 3 秒、InGame 10 秒。
- InGame 自动隐藏悬浮入口/桌宠，离开 InGame 后只恢复由 Gameflow 自己隐藏的入口；用户在同一 InGame 显式重开控制中心不会被 heartbeat 重复关闭。
- PetHost 启动过程中保留 desired visibility，避免游戏中晚启动闪现。
- 导航 owner-draw 残影、紧凑控制中心首次裁剪残影、LOL Hub 顶部遮挡、共享 Toggle 重影和窗口控制按钮旧像素残留均已有 deterministic 回归保护。
- 普通构建不内嵌 self-contained PetHost；轻量 FACM.exe 体积 gate <10 MiB。
- 更新 manifest 以 GitHub main 的 3.5 清单为唯一版本基准；多个传输候选选择最高有效版本，旧镜像不能把服务器版本倒退到当前客户端以下。
- UI Round 1 引入共享 `FacmActionButton` / `FacmToggleSwitch` / `FacmStatusBadge`，建立 `ThemeCatalog` / `FacmThemeRuntime` / `FacmDesignSystem` 语义边界。
- UI Round 2 的场景导航只消费 `LeagueDashboardModule` 的共享 Gameflow 状态，不新增 LCU polling、第二 League session 或新的 League 写入路径。
- 3.5.25 普通顶层 WinForms 使用共享视觉无标题栏外壳：36px 默认交互区与内容画布同色，只保留品牌、拖动、最小化/最大化/关闭；LOL Hub 不展示副标题层。
- 3.5.26 UI Reskin Pass 1：共享卡片改为纯 surface、公共圆角收紧为 window 12 / card 8 / control 6；LOL Hub 左侧导航改为轻选中面 + 2px accent indicator，顶部子导航改为紧凑标签式底部选中线；`FacmChromeButton` 与通用业务 Button 主题隔离。

## 3.5.27 UI Design System 收口

3.5.27 将 3.5.26 发布后已合并的高频可见页面统一打包为正式版本：

- PR #251：共享侧栏/顶部标签恢复键盘 Tab/focus contract；League Dashboard 从六张等权卡片改为“连接 + Gameflow”主状态区与紧凑元数据列表。
- PR #252：League Player 战绩页移除主要 page-local 深色 RGB palette 和私有按钮样式，改用共享 semantic tokens/primitives；保持高密度虚拟化列表，不把每条数据包装成卡片。
- PR #253：正常 Compact Launcher 可见增强路径覆盖旧玻璃渐变、双色强调和大圆角，使用共享 Canvas/header/tile/context 视觉；legacy fallback action 不变。
- PR #254：LOL Hub 增加 DPI 感知和自适应布局；sidebar/context/subnav 根据可用宽度分配空间，实时 Live 页优先保留高密度宽视图。
- PR #255：League Live 的 phase/status/bench/player list/actions 收敛到共享设计系统，同时保留原 polling、snapshot 和 bench-swap ownership。
- PR #256：统一 Recommendation 页面从 legacy cyan/violet presentation 收敛到共享语义色；旧双色 header glow 改为单 accent 细线；推荐选择和刷新/应用恢复键盘可达。
- PR #257：Presence 不再直接消费 page-local `ThemeDefinition` 视觉；状态改用共享 accent/success/warning/error；Presence 与 Game Repair 操作恢复 Tab 可达性。
- PR #258：Mayhem Lookup 外壳、输入框、状态和四个操作按钮迁移到共享设计系统；Search 是唯一 primary action。**MayhemCardRenderer 保持领域专用攻略图视觉与信息密度，不做模板式“统一”。** Windows Build、UI Text Contract 和 Mayhem Source Probe 均通过后合并。
- PR #259：请求并发布正式 FACM 3.5.27；发布链完成签名、公开字节校验、manifest 启用与项目 release-state 更新。

## 3.5.28 Windows 真机 UI 收尾

3.5.28 只处理 3.5.27 真机截图暴露出的具体问题，不再开启新一轮大换皮：

- PR #261：Mayhem Lookup 新增共享响应式几何策略，查询/取消/保存/复制从实际 client width 反向分配，920px 正常窗口和更窄的 Hub 嵌入区域都不再截断；攻略预览提前预留垂直滚动条宽度并只建立纵向滚动范围，避免正常窗口出现横向滚动。共享 smoke 固化 1120/920/700px 几何边界，Windows Build #1628、UI Text Contract #736、Mayhem Source Probe #477 全绿后合并。
- PR #261：共享 `Border` / `BorderSoft` 从历史主题的高饱和原始 border 向 material surface 收敛，使强调色重新集中到选中、动作和状态；不删除任何历史 ThemeCatalog id。
- PR #261：清理与修复页去掉动作区的一层外卡片，驱动修复/环境清理改用 `FacmActionButton` Secondary 语义并恢复 Tab 可达性，同时补充 DPI autoscale；清理目标、提权、进程检查、驱动工具和执行逻辑不变。
- PR #262：请求并发布正式 FACM 3.5.28；发布工作流完成 Release build/smoke、Authenticode 签名、公开字节与签名者复验，并在公开验证通过后启用在线更新。

## 3.5.38 Champion Select 秒退阵营只读公开测试

- 实现来源：PR #282，`feat/read-only-dodge-probe-20260909`。
- 2026-09-09 腾讯客户端实机日志已经抓到多次自然秒退：`dodgeData.state=StrangerDodged`，但 `dodgerId=0`；同时 `myTeam` 5 人身份完整可读、`theirTeam` 只有槽位且对方 Summoner ID 全部隐藏。
- 因此 `StrangerDodged` 本身不再被视为对方秒退证据。阵营判断必须 fail closed，没有正向证据就保持 `unknown`。
- 新 Probe 保留 `/lol-matchmaking/v1/search`，并组合 ChampSelect 原始 `myTeam` 瞬时变化、`chatDetails.chatRoomName`、房间 system/event 退出消息、聊天参与者变化和 Lobby 成员变化等只读信号。
- 正常选人阶段的会话详情只做一次基线；确认秒退后才进入约 1 秒、125 ms cadence 的短时证据 burst，避免公共版本长期高频读取消息/参与者接口。
- 普通玩家聊天正文不记录；只有与秒退诊断相关的 system/event departure 文本允许以 160 字符上限写入本地 FACM 日志。会话 ID 使用 `c1`/`c2` 等本地标签。
- Probe 只接收 `ILeagueClientApi`，不新增任何 LCU `POST` / `PUT` / `PATCH` / `DELETE`，不发送聊天、不改变匹配/ReadyCheck/选人行为，也不新增第二 Gameflow owner。
- `LeagueDodgeProbeService.ValidateForSmokeTest()` 已接入 `--league-dashboard-test`，公共发布仍要求 Windows Build 与 UI Text Contract 通过。
- 3.5.38 定位为公开实机取证版本；正常用户可通过在线更新共同积累真实 Tencent dodge evidence。最终“己方玩家秒退 / 对方玩家秒退”用户提示仍需等公开数据证明某个正向映射稳定后再产品化。

## Runtime Companion modernization（PR #283，未合并/未发布）

- 唯一任务分支：`feat/runtime-companion-20260910`；唯一任务 PR：#283，保持 draft，正式版仍是 3.5.38。
- 旧 660px 横向 ChampSelect assistant 的正常创建入口已切换为约 388px 逻辑宽度的纵向 Runtime Companion；旧实现暂时保留为未使用兼容代码，待真实 Tencent 客户端验收后再决定是否删除。
- `LeagueHubModule` 继续持有 Champion Select episode/popup 生命周期；Runtime Companion 不新增 Gameflow owner、第二 League session 或 page-local LCU write stack。
- `LeagueRuntimeCompanionController` 将现有 Bench、Build Advisor、Mayhem 数据投影为 snapshot，并用 generation/cancellation 阻止换英雄后的旧异步结果覆盖新上下文。
- Build Advisor 从同一份已获取的 OP.GG payload 中为符文、召唤师技能、出门装、鞋子、核心装和技能顺序保留最多 3 套源顺序方案；第 1 套仍是既有默认，额外方案仅用于“更多”展开，不增加网络请求。英雄头部同时投影 mode/position/patch 与源数据真实存在的 Tier、排名、胜率、登场率、禁用率；缺失统计保持未知，不伪造为 `0%`。
- Rune / Summoner Spell 就地 Apply 复用 `LeagueBuildApplyService`，继续执行用户确认、phase/champion/queue 重验与 settled postcondition。Core 的“导入装备”复用 `LeagueItemSetService`：准备阶段只读，确认后再次验证选人上下文，只维护 FACM 自有推荐装备文件，并验证最终 JSON；其他没有安全 owner 的类别继续只读。
- ARAM 基础平衡复用现有 `OpggAramBaseBalanceService` 的 bounded/10 分钟完整结果缓存，并由 `RiotGameDataService.EnrichAsync` 作为 automatic-guide 的唯一调用 owner 与视觉元数据并行获取；已移除 `MayhemAutomaticGuideService` 中的重复预调用，避免失败态触发第二次外部请求。Runtime Companion 已新增独立“大乱斗基础平衡”行：有真实 `BaseBalanceSummary` 才显示，`syncing`/`unavailable` 保持警告语义，缺失时直接省略。
- Bench 快速换英雄继续走 `LeagueBenchQuickPickService`。`benchEnabled` 既不作为 Mayhem 证明，也不再作为 ARAM/Mayhem 攻略加载的前置条件：Bench availability 只控制快速换英雄条；只要 ChampSelect session、英雄与模式有效，攻略仍按模式加载。`LeagueQueueModePolicy` 将普通 ARAM（450/ARAM）与 Mayhem（2400、CN/WeGame 3270、KIWI/ARAM_MAYHEM）分开：普通 ARAM 只读取版本绑定的基础平衡补充，Mayhem 才启动完整攻略/强化链；未知模式 fail closed。普通空状态不生成装饰性 N/A 卡片。
- Guide snapshot 被撤回、模式切换或版本绑定刷新失败时，Runtime Companion 会清除上一份 ARAM/Mayhem 专属展示；只有被 guide fallback 实际占用的推荐行会被回收，不会误删 Build Advisor 已接管的普通推荐行。
- Runtime Companion 的位置、置顶、收起状态通过共享 `AppSettings` 与现有 last-known-good recovery 持久化；不创建 Form 私有配置文件，也不重新加载第二份 settings。
- `app.manifest` 已有 PerMonitorV2。Companion 的恢复/默认定位延迟到 `Shown` 后按物理 DPI 尺寸处理，允许左侧屏幕负坐标，并在显示器拓扑变化时 clamp 到当前 working area。
- deterministic smoke 覆盖紧凑宽度/高度、100%/125%/150%/200% DPI 高度与默认锚点策略、负坐标多屏 clamp、settings round-trip/LKG recovery、snapshot clone、最多 3 套方案投影、缺失胜率不伪造、rune/spell scoped apply、item-set owner 边界，以及 ARAM balance 有值显示/无值省略。Team Builder fallback 若只返回 Bench roster 而缺少 queue/mode，会保留 generic ChampSelect 已读到的 queue/mode，避免国服 Mayhem 模式路由退化为 unknown。
- ARAM 可视化补丁之前的完整实现 head `df835a040f90caa242d1069bc9d4afbd3d680ff4` 已通过 UI Text Contract #831、Mayhem Source Probe #491 与 Windows Build #1723（含 lightweight FACM.exe verification 和 optional PetHost self-test）。当前收口 head 必须重新通过同样 Gate；旧成功记录不能替代最新代码验证。
- 剩余外部 Gate 是真实 Tencent 客户端验收：普通 Ranked 的自动出现/不抢焦点/英雄切换；Rune/召唤师技能 Apply；装备导入；ARAM/Mayhem 基础平衡/强化/Bench；关闭仅 dismiss 当前 episode；拖动/置顶/收起跨 episode 恢复，以及有条件时的多屏/125%/150%/200% DPI。未通过这些实机 Gate 前不 merge、不 bump version、不改在线 manifest、不生产发布。

历史主线：P1 合并 #241；4.x working-tree cleanup #242；3.5.21 更新一致性 #243；UI Round 1 #244；UI Round 2 #245；3.5.23 顶部布局修复 #246；3.5.24 Toggle 重绘 #247；3.5.25 一体化无边框外壳 #248；3.5.26 UI Reskin Pass 1 #249；Agent knowledge consistency #250；3.5.27 UI 收口 #251–#259；3.5.28 真机 UI 收尾 #261–#262；3.5.38 秒退阵营只读公开测试 #282。PR #283 当前仅为 Runtime Companion review task，尚未进入正式历史主线。

## 当前产品体验方向

- 继续保持 WinForms / net48 / single-EXE，不为视觉升级引入第二 UI 框架。
- `ThemeCatalog` 是 palette source，`FacmThemeRuntime` 是 process-wide active theme owner，`FacmDesignSystem` / 共享控件承载公共视觉语义。
- `FacmWindowChrome` 保留窗口行为所有权；36px 顶部交互区与内容视觉合并，不重新引入独立标题栏式副标题层。
- 顶部交互区与内容区继续使用显式互不重叠坐标，不回到依赖 Dock/Z-order 的布局。
- 悬浮入口场景化仅复用唯一 Gameflow owner；不得为了“更快”再建轮询器或第二 League session。
- 高频用户操作优先收敛到状态首页、统一 LOL Hub 与自动化设置，不增加重复入口。
- UI 参考现代 Windows / Fluent / PowerToys 的克制桌面产品行为，并用 anti-template 审计规则抑制大圆角、蓝紫双强调、卡片滥用、胶囊滥用、强制对称矩阵和无意义装饰。
- 数据密集页优先保持表格/列表密度与明确层级；领域专用可视化（例如 Mayhem 攻略图）允许保留自己的信息设计，不为了表面统一损失可读性。

### 已确认但尚未收口的 UI 技术债

- 正常可见 Compact Launcher 已由 `DesktopLauncherEnhancer` 覆盖为共享 flat 视觉；`CompactMenuForm` 内部仍保留 `ThemedPanel` / `ThemedButton`、主题渐变和 Style-specific 装饰作为 fallback/legacy 实现。只有确认 fallback 仍真实可达或存在维护价值时才继续收敛，不为了删代码冒行为回归风险。
- `ThemeCatalog` 仍保留 Glass/Luxury/Cyber/Soft/Brutalist/Holographic/Minimal/Rgb/Aurora/Synthwave 等历史 palette 兼容项。共享可见产品 surface 只消费 semantic tokens/受控 geometry；3.5.28 已进一步压低共享 border 的高饱和主题泄漏。
- Dashboard、Player、Live、Recommendation、Presence、Mayhem Lookup 等高频表面已经完成主要视觉收口；较早的 standalone `LeagueBuildAdvisorForm` / `LeagueBuildApplyForm` / `LeagueItemSetForm` 等仍可见 page-local RGB/native styling。后续先确认这些旧入口的真实可达性和用户价值，再决定是否迁移，不以“零 Color.FromArgb”为目标。
- LOL Hub 的自适应 geometry 已在 #254 引入，Mayhem 嵌入页的剩余宽度/滚动问题已在 #261 收口；后续布局工作只以新的真机 DPI/缩放/窄窗口问题为证据。
- PR #283 的旧 `LeagueChampSelectAssistantForm` 暂时保留为未使用兼容代码；只有 Runtime Companion 完成真实 Tencent 客户端验收后，才判断是否删除旧类/旧 smoke，不在同一未验收阶段提前清理回退路径。

## 当前保留组件

- `src/FACM`：主程序与 3.5 runtime。
- `src/FACM.ToolBundle`：内置工具资源。
- `src/FACM.Updater`：3.5 单 EXE 更新替换、校验、回滚。
- `src/FACM.PetHost`：可选桌宠 runtime 源码与 IPC。
- `online/version.json`、`online/announcement.json`、`online/mirrors.json`。
- 3.5 Windows Build、UI Text Contract、Mayhem Source Probe、Online Management、3.5 Lightweight Release workflows。

## 已退出的 4.x 范围

不再维护或构建：WinUI/Morphing Surface、`FACM.App/Core/Infrastructure/Platform.Windows`、native bootstrapper、CAB、多版本 `.facm/versions`、4.x migration、4.x foundation/smoke/probe、旧 heavyweight embedded-PetHost publisher。

3.5 Updater 中曾保留的 4.x migration CLI/model/bootstrapper handoff 已移除；普通 3.5 原子替换/回滚/self-test 继续保留。当前 `online/version.json` 与 3.5 lightweight publisher 都保持 migration-free。

## 当前发布状态

正式发布状态以文件顶部 `FACM_RELEASE_STATE_BEGIN/END` 自动块为唯一权威。该块由 3.5 lightweight publisher 在成功发布后维护；普通功能 PR 不手工伪造未来版本的正式发布结果。

FACM **3.5.38 已正式发布并启用在线更新**。PR #282 已合并；canonical publisher 已完成 Release build/smoke、签名、GitHub Release 发布、公共 FACM.exe 字节与签名者复验，并在复验成功后将 `online/version.json` 设为 `enabled=true`。`force_update=false`，因此这是正常可选更新而不是强制升级。

PR #283 的 Runtime Companion 仍是 **review candidate**：没有修改 `release/3.5-request.json`、`online/version.json`、版本号、tag 或 Release，也没有合并到 main。CI 通过只证明构建/smoke contract，不等于生产发布或腾讯客户端实机验收。

## 当前维护 Gate

后续修改继续满足：

1. `FACM.sln` 只引用当前 3.5 项目。
2. Windows Build PASS。
3. UI Text Contract PASS。
4. ToolBundle 嵌入正常；PetHost ZIP 不嵌入；FACM.exe <10 MiB。
5. Updater self-test PASS。
6. retained source/workflow 不依赖已删除 4.x 项目/脚本。
7. `online/version.json` 和 publisher 不重新引入 4.x migration 配置。
8. UI 重构保留原 WinForms 交互语义；不得为了视觉一致性改变 League 写入、更新协议或现有业务所有权。
9. 场景首页必须复用共享 Gameflow 状态；不得为导航速度新建轮询器或第二 League session。
10. 共享无边框顶部交互区和内容区必须保持显式不重叠；默认顶部视觉应与内容画布一致，不重新引入副标题层。
11. 新增或实质重做的 UI 不得再创建 page-local 主题体系；优先使用 `FacmDesignSystem`、共享 primitives 和明确的布局/可访问性 contract。
12. `FacmNavButton` / `FacmPillButton` 必须保持 native `Button` + `TabStop=true` 的键盘可达 contract。
13. 默认 Compact Launcher 的可见增强路径应覆盖 legacy 渐变/双强调背景并使用共享 Canvas/WindowRadius；legacy fallback rendering 不能重新成为默认可见 surface。
14. 领域专用 UI（例如 MayhemCardRenderer）应优先保留其信息密度与功能语义；共享设计系统主要约束窗口壳、导航、状态和通用控件，不强行抹平领域视觉。
15. Mayhem Lookup 的 toolbar 必须保持在 client bounds 内，攻略预览应预留纵向滚动条宽度并避免正常窗口产生横向滚动；1120/920/700px 几何由 deterministic smoke 保护。
16. Champion Select 秒退阵营 probe 必须保持 GET-only、复用唯一 Gameflow owner、普通聊天正文不记录，并对 `StrangerDodged`/缺失身份 fail closed；没有正向阵营证据不得猜测 enemy。
17. Runtime Companion 必须继续复用 `LeagueHubModule` episode owner、共享 League/Build/Settings owners；位置恢复应在 PerMonitorV2 DPI 生效后 clamp，inline write 必须走现有安全 owner。真实 Tencent 客户端验收前不得把 PR #283 当作已发布行为，也不得删除旧 assistant 回退代码。

后续若发现实机问题，按普通 3.5.x bugfix 处理并发布新的 patch 版本，不恢复 4.x 产品线。
