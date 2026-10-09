<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：GGman 3.5.58
- GitHub Release：v3.5.58
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：d8249f24a572bc80ea3df01bcd15eada9f672ce9
- 发布元数据提交：fc53f50d01f705fc33912f9231f24af9a72b67f1
- Release GGman.exe SHA-256：42199788BD35971F698375FC5218EB5F34B5542EED0AE82330B7786E428487F5
- release_notes：GGman 3.5.58：优化更新与公告页面。新版本的完整更新说明现在可以在独立的只读滚动区域中查看，不再被狭窄状态标签截断；版本检查、下载进度、安装准备与失败提示各自显示清晰状态。尚未获得可靠更新信息时不会误提示“已是最新”，检查失败会禁用失效的旧下载操作，下载或安装失败会保留醒目的错误提示并允许安全重试。相关提示框统一使用 GGman 名称。补充长版本说明、更新资格判断及失败状态的回归测试。原有自动检查设置、强制更新退出行为、镜像下载、哈希与签名验证、更新器替换/恢复，以及旧版 FACM.exe 在线更新兼容策略均未修改。
<!-- FACM_RELEASE_STATE_END -->

## 2026-10-09 — Update Center status and release notes polish (3.5.58 target, pending release)

- Issue #300; branch `feat/ggman-update-center-ux-20261009`; verified enabled public 3.5.57 before work. Scoped UX fix: keep the existing 560×620 Update Center window, GGman branding and the startup auto-update setting, but give long version notes a separate native scrollable read-only field, rebalance the announcement card, and distinguish unchecked/failed/verified/update-in-progress states. Download/installation failure now retains its error state rather than being overwritten by “update available”; metadata failure also blocks stale install actions. Added pure multiline-notes/eligibility/error-snapshot checks in the existing `UpdateMirrorSmokeTest`.
- The download target/mirror resolution, cryptographic hash/signature checks, trusted update metadata, updater replacement/restart, 3.5.40 compatibility download alias, forced update/exit behavior and persistent settings format remain unchanged. No Gameflow/LCU work in this pass. Real Windows DPI and actual download failure UI acceptance remain unverified by CI.
- Formal release target **3.5.58** only after final-head Windows Build/UI Text Contract, safe PR merge and signed publisher's public asset/signer/SHA check with enabled online manifest.


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
