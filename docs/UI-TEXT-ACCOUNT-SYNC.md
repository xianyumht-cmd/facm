# UI wording editor and email-account sync

Status: **released in signed GGman 3.5.65** (2026-10-10). SQL `cloudbase/sql/007_account_ui_text.sql` was deployed once by the operator, and online one-click update is enabled. Do not rerun the create-once schema.

## Scope

- The single entry is inside **LOL 工作台 → 我的 GGman**, after the ESC section. The area is collapsed by default, expands within the same scrollable page, and does not add an item to the app settings, tray or left navigation.
- A searchable `[Text]` editor lists the actual `UiTextCatalog.DefaultText` keys with their current and default values, preview and restore-default actions. Advanced `[Replace]` rules may be added, edited or removed. These are the two supported `ui-text.ini` sections; UI runtime's dynamic translation scope remains what `UiTextRuntime` already supports, not a new promise to intercept absolutely every string.
- A local save validates text size, control characters, and **exact indexed placeholder occurrences** (e.g. `{0}`, `{1}`). Existing `ui-text.ini` content remains the live source of truth. Editing regenerates supported key lines while preserving unknown sections/keys and comments where possible, copies the old file to `data/ui-text-backups/` first, then replaces it via a temporary file. `UiTextRuntime` detects the changed file and refreshes supported visible texts. Restore does not require a full app restart, though some non-dynamic text may be visible on the next screen open.
- Cloud capture serializes **only customized** known `[Text]` keys and advanced `[Replace]` rules (not account credentials, software settings or local paths). Upload/restore is manual. Restore previews revision, timestamp and counts, asks for confirmation and saves a local backup. Editing locally does **not** upload automatically; two devices can diverge until a deliberate upload and restore.
- Registered email UID owns the new `ggman_ui_text_profiles` table. `ggman_get_ui_text_profile()` and optimistic-CAS `ggman_set_ui_text_profile(jsonb,bigint)` are `SECURITY INVOKER`, with authenticated-only RLS and explicit role guards. No anonymous-device fallback or registration forced for local editing. The schema is a **create-once migration**; inspect for pre-existing tables/RPC before running, and never execute it repeatedly.
- Text may contain sensitive custom messages, URLs or phone numbers written by the user, so cloud-upload confirmation is mandatory. Client validates all downloaded keys and limits before applying. It never downloads scripts or executable content via this channel.

## Release checks and remaining field acceptance

- Verified: SQL 007 table and RPC security modes, RLS/role grants; mock authenticated A/B readback, isolation, optimistic conflict and increment (rolled back); production no-token and anonymous-bearer read/write calls denied by HTTP 401; signed release workflow and public hash agreement. Windows CI also checked indexed placeholders, serialization, local backups and repeated INI saving without duplicate sections.
- Still awaiting owner real-Windows field tests: text editing and visual hot reload, actual signed-in upload followed by cloud restore, account change/logout, a second logged-in Windows device, preservation of customized `ui-text.ini`, and mixed-DPI layout. Mock SQL tests cannot replace real registered A/B HTTP token isolation.
- The old ESC backup, registered personal statistics and anonymous settings schema remain unchanged. A cloud restore is manual and user-confirmed, and is not a silent background merge.

Legacy [Replace] is an advanced global compatibility mechanism; default UI mode shows `[Text]` first to avoid accidental changes.
