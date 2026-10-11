# GGman Operations (3.5.x)

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
  "version": "3.5.72",
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

## Default-on registered background configuration sync acceptance

Default-on auto-sync originally shipped signed in `v3.5.67`; follow-up fixes to registered token renewal and local sync-state recovery shipped in `v3.5.69` and `v3.5.70`. The current signed online release is `v3.5.71` as of 2026-10-11. These checks are **post-release native field acceptance**, not prerequisites that should be marked complete without real devices. A docs-only closeout does not trigger a new release.

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
