# Tools / Automation Gate 4 handoff — 2026-08-15

## Scope

Gate 4 adds one novice-facing persistent master switch to the existing `OP.GG 一键应用` window:

`选人时自动应用 OP.GG 推荐`

When OFF, accepted Gate 2 manual behavior is unchanged. When ON, FACM may automatically apply the current stable Champ Select OP.GG recommendation exactly once per recommendation context:

- runes;
- summoner spells;
- Gate 3 Recommended item set.

Gate 4 does **not** add a new tray/root menu item.

## Dependency / PR topology

- Issue: #106
- Draft PR: #107
- branch: `feat/opgg-auto-apply-gate4-106`
- intended PR base: `feat/opgg-itemsets-gate3-99` / Draft PR #103
- reason: Gate 4 composes the Gate 3 item-set service; do not copy it into a parallel implementation and do not merge Gate 4 before Gate 3.
- Shell UX PR #105 remains independent and is not part of this branch.

The repository workflows currently declare `pull_request.branches: [main]`, so stacked PR #107 does not automatically receive PR CI while based on Gate 3. For validation only, #107 may be temporarily retargeted to `main`, receive a real documentation/code commit that triggers the existing workflow, and then immediately be restored to the Gate 3 base. Keep Draft throughout; never merge while temporarily based on main.

## Settings contract

FACM current settings persistence is root `settings.ini` (`key=value`), not JSON.

Gate 4 adds:

`LeagueAutoApplyRecommended=True/False`

- default: `False`;
- old settings without the key parse as false;
- toggle changes save immediately through existing `AppSettings`;
- the uploaded/custom `ui-text.ini` remains copy-only. It can override Gate 4 labels/status text but never stores behavior state.

`AppSettings.ParseLines` / `BuildLines` are now deterministic helpers and smoke-tested for this key.

## Akari reference / deliberate FACM differences

League Akari current `auto-champ-config` observes `currentChampion + enabled` and applies configured runes/spells during Champ Select.

FACM borrows the explicit enabled + ChampSelect-context product idea, but intentionally does not copy risky/noisy behavior:

- no rune-page-first-page overwrite fallback;
- no Champ Select chat broadcast;
- no auto accept / pick / ban / swap / reroll / dodge / skin;
- stable-context debounce + fingerprint dedupe;
- Gate 3 item-set result is aggregated into the same attempt truthfully.

## Auto coordinator

`LeagueAutoApplyCoordinator` is a pure state machine.

Actionable snapshot requirements:

- connected;
- Performance activity = ChampSelect;
- championId > 0;
- mode / position / version available;
- OP.GG recommendation status = ready.

The same fingerprint must remain stable for at least 1.5 seconds. Fingerprint includes:

- champion id;
- queue id;
- mode;
- position;
- data version;
- sorted recommendation category/display content.

The same fingerprint receives at most one automatic attempt. Repeated polling cannot create repeated rune pages / repeated item-set writes. Champion or recommendation context changes must stabilize again before one new attempt.

A failed or partial attempt is **not** automatically retried for the unchanged fingerprint; this prevents retry storms. Manual one-click remains available.

## Performance boundary

`LeagueAutoApplyController.PollInterval = 2s`, but the Gate does not blindly poll every 2 seconds all day.

It reuses the already-running `LeagueGameflowMonitor` -> `PerformanceBudgetProvider` phase signal. `LeagueDashboardModule` starts that monitor after the first `Application.Idle`, independent of opening the Dashboard window.

Gate 4 runs its own Advisor observation only when the global budget name is `champ-select`.

Therefore:

- disabled: no Gate 4 League/OP.GG observation request;
- Desktop: zero Gate 4 observer request;
- Queueing: zero Gate 4 observer request;
- In Game: zero Gate 4 observer request/write;
- Champ Select only: serial ~2s observation until one stable fingerprint attempt.

Frozen Champ Select budget remains network 2 / image 1 / disk 1 / CPU 1 / prefetch 0.

## Shared OP.GG payload cache

The visible Advisor and Gate 4 auto executor need the same structured `/api/global/champions/...` payload. Fetching it twice would be unnecessary.

`CachingOpggBuildApi` wraps the existing `OpggBuildApiClient`:

- same-path raw payload cached for the existing 10-minute Build cache duration;
- one `SemaphoreSlim` serializes cache misses;
- successful non-empty responses only are cached;
- module owns one shared instance;
- `LeagueBuildAdvisorDataService` and `LeagueAutoApplyExecutor` both borrow it and do not dispose it;
- module disposes it after both consumers stop.

Smoke asserts two same-path reads cause one inner request; a different path causes the second request.

## Automatic transaction

For one stable fingerprint:

1. consume one structured OP.GG build payload;
2. parse Gate 2 rune/spell plan with existing `LeagueBuildApplyService.ParsePlan`;
3. parse Gate 3 item-set plan with existing `LeagueItemSetService.ParsePlan`;
4. apply Gate 2 first;
5. if Gate 2 reports context `blocked`, stop and do not proceed to disk;
6. otherwise apply Gate 3 item set if present;
7. aggregate full success / partial / failed honestly.

No new LCU writer is introduced. Gate 2 transport allowlist remains the hard network write boundary.

Gate 3 remains the hard disk ownership boundary: only `facm1-*` Recommended JSON, Tencent sibling-Game validation, temp/atomic/readback transaction, no user/third-party JSON deletion.

## UI

The existing `OP.GG 一键应用` form now includes:

- checkbox: `选人时自动应用 OP.GG 推荐`;
- adjacent status: disabled / waiting / applying / success / partial / failed.

The checkbox persists immediately. The auto controller continues working when the window is closed because it belongs to the League Build Advisor module lifecycle, not the form lifecycle.

Manual button + Yes/No confirmation remain unchanged.

New visible strings use scoped Gate 4 text keys and remain overridable by `ui-text.ini`.

## deterministic smoke

`LeagueAutoApplySmokeTest` is wired into `PerformanceContractSmokeTest` and covers:

- legacy settings default OFF;
- True / False parse + serialization;
- 1.5s stability window;
- exactly once per unchanged fingerprint;
- no retry storm;
- champion change -> restabilize -> one new attempt;
- recommendation change -> restabilize -> one new attempt;
- disable clears pending work;
- re-enable must stabilize again;
- In Game not actionable;
- OP.GG unavailable not actionable;
- global Desktop / Queueing / In Game budgets do not activate Gate 4 observer;
- only global Champ Select budget activates observer;
- shared raw OP.GG same-path cache prevents duplicate network request;
- success / partial / failed aggregation truthfulness;
- Gate 4 UI defaults non-empty.

Existing Performance smoke also continues to prove Gate 2 writer rejects ready-check accept, Champ Select action writes and query-string path bypass.

## CI history

First CI head `8f4b08462bf63f957ccfccb7b3b17cbbafb4b9a9`:

- UI Text #133: SUCCESS;
- Windows #1012: Release compile failed on two integration details, not on write semantics:
  1. `FacmHostSmokeTest` still used the old `LeagueBuildAdvisorModule` constructor and old dependency list;
  2. WinForms callback caught `InvalidOperationException` before derived `ObjectDisposedException`.
- both are fixed in the same Gate 4 branch; host smoke now explicitly requires Settings + LeagueClient + Performance.
- Mayhem #252 was an incidental live probe triggered by temporarily targeting main; do not infer Gate 4 behavior from it.

Final candidate CI/artifact must be recorded only after the post-fix latest HEAD completes UI Text + Windows/Performance validation.

## Tencent acceptance checklist

Do not merge before real-client acceptance. Recommended test:

1. start with switch OFF; enter Champ Select and verify nothing is auto-changed;
2. open `OP.GG 一键应用`, turn ON, close the window;
3. select/preselect a hero and leave context stable; runes + spells + item set should apply automatically without another confirmation dialog;
4. ensure one stable hero does not keep creating new rune pages or repeatedly rewriting the item set;
5. change hero; after stability, allow exactly one new auto apply;
6. check Flash D/F preservation still follows accepted Gate 2 behavior;
7. if rune capacity is full, runes must be skipped rather than overwrite an existing page;
8. enter game; Gate 4 must stop observing/writing; shop should see the Gate 3 `[OP.GG] ...` Recommended set;
9. disable the switch and verify later Champ Select changes no longer auto apply;
10. manual `预览并应用` still works independently;
11. no auto ready/pick/ban/chat behavior appears.

If this combined Tencent test proves the item set appears in the shop, that evidence may also satisfy the remaining real-client gap of Gate 3 #99/#103. Close Gate 3 first, then rebase/retarget Gate 4 to main and rerun exact-head CI before Gate 4 merge.

## Release boundary

No Release, tag, `online/version.json`, minimum-version or force-update change. Production remains FACM 3.2.0 / `force_update=false`.
