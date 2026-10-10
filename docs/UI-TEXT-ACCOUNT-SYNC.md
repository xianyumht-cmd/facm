# UI wording editor and email-account sync

Status: **implementation staging**, NOT merged or released. SQL `cloudbase/sql/007_account_ui_text.sql` has not been installed. Current signed release remains 3.5.64.

## Scope

- The single entry is inside **LOL 工作台 → 我的 GGman**, after the ESC section. The area is collapsed by default, expands within the same scrollable page, and does not add an item to the app settings, tray or left navigation.
- A searchable `[Text]` editor lists the actual `UiTextCatalog.DefaultText` keys with their current and default values, preview and restore-default actions. Advanced `[Replace]` rules may be added, edited or removed. These are the two supported `ui-text.ini` sections; UI runtime's dynamic translation scope remains what `UiTextRuntime` already supports, not a new promise to intercept absolutely every string.
- A local save validates nonempty text, size, control characters, and **exact indexed placeholder occurrences** (e.g. `{0}`, `{1}`). Existing `ui-text.ini` content remains the live source of truth. Editing regenerates supported key lines while preserving unknown sections/keys and comments where possible, copies the old file to `data/ui-text-backups/` first, then replaces it via a temporary file. `UiTextRuntime` detects the changed file and refreshes supported visible texts. Restore does not require a full app restart, though some non-dynamic text may be visible on the next screen open.
- Cloud capture serializes **only customized** known `[Text]` keys and advanced `[Replace]` rules (not account credentials, software settings or local paths). Upload/restore is manual. Restore previews revision, timestamp and counts, asks for confirmation and saves a local backup. Editing locally does **not** upload automatically; two devices can diverge until a deliberate upload and restore.
- Registered email UID owns the new `ggman_ui_text_profiles` table. `ggman_get_ui_text_profile()` and optimistic-CAS `ggman_set_ui_text_profile(jsonb,bigint)` are `SECURITY INVOKER`, with authenticated-only RLS and explicit role guards. No anonymous-device fallback or registration forced for local editing. The schema is a **create-once migration**; inspect for pre-existing tables/RPC before running, and never execute it repeatedly.
- Text may contain sensitive custom messages, URLs or phone numbers written by the user, so cloud-upload confirmation is mandatory. Client validates all downloaded keys and limits before applying. It never downloads scripts or executable content via this channel.

## Required checks before signed client update

1. Review the current production schema, then apply `007_account_ui_text.sql` once in the intended CloudBase environment; verify table, RLS, policies, RPC execution role and registered-only permissions. Existing `ggman_settings_sync` and ESC tables remain unchanged.
2. Test live registered account A's upload/read and B's isolation; anonymous bearer and no-header requests denied; wrong expected revision conflict, malformed keys/placeholder payload rejected by client; opt-out/logout must not leak another account's text.
3. Windows source build and UI text contract, placeholder validation, file round-trip, high-DPI in-page editor layout, local file backup and rollback, dynamic string refresh, `[Replace]` ordering, and current-user customized content preservation. Check default/cross-client view of user-supplied `[Replace]` rules for unintended substitutions.
4. Merge reviewed PR and release a new numbered signed 3.5.x GitHub Release with public signer/hash verification before enabling the in-app updater. No detached test package as the normal delivery.

Legacy [Replace] is an advanced global compatibility mechanism; default UI mode shows `[Text]` first to avoid accidental changes.
