# GGman registered-account identity — CloudBase email OTP

**Status: unmerged/field acceptance pending (Issue #313)**

## Account product contract

- Login is optional, from **我的 GGman → GGman 账号登录**. Anonymous use of League features, local stats, original cloud device stats and `ggman_settings_sync` continues unchanged.
- Registered login uses CloudBase authentication v2 REST with the existing environment's public HTTPS gateway: `POST /auth/v1/verification` (`{email,target:"ANY"}`), `POST /auth/v1/verification/verify` (`{verification_id,verification_code}`), then `POST /auth/v1/signin` for existing users or `POST /auth/v1/signup` with email for new users. The returned `sub`/UID is the player identity, rather than the prior device-derived anonymous subject. Use `POST /auth/v1/user/signout` for explicit server logout.
- Backend decides account registration vs sign-in through the `is_user` indicator. The service's default six-digit code validity is 10 minutes; CloudBase enforces a one-minute send interval. The GUI also checks the address, binds each verification to its original email, limits attempts, and does not send codes on launch.
- First stage keeps access/refresh tokens **only in process memory**. No 'remember me' toggle, plaintext file, DPAPI persistent credentials, server secrets, session exports or debug logging of email/code/token. Restart or close GGman to sign out locally; for server revocation, click explicit **退出登录** while still signed in. If CloudBase logout fails, clear local credentials and tell the user that server revocation was not confirmed. Never use registered tokens to overwrite existing anonymous `ggman_devices`, usage telemetry, or settings sync.
- The login UI does not expose hidden user identifiers/credentials in exported diagnostics. Account code displays the authenticated UID after successful login only, to help compare two profiles during acceptance. Authentication failures must not imply login success.

## Required CloudBase console setup (not automatable from a code commit)

1. Open your Tencent CloudBase project for **`ggman-d4gioqqcz434d9e4d`**, enter **身份认证 → 登录方式 → 邮箱验证码 → 配置发件邮箱**, choose official email forwarding or the project's preconfigured authorized SMTP, and save. Also confirm that user registration and email verification are enabled and that the appropriate email template sends a six-digit code.
2. Test with a real mailbox in GGman and confirm code delivery, verification, newly registered sign-up, later sign-in, and server logout. CloudBase may require an image CAPTCHA (`captcha_required`); this first slice does **not** have an interactive image CAPTCHA flow and must fail closed if challenged. Do not disable protections silently.
3. Sign in with **the same email** from two independent Windows profiles/devices and compare the returned stable `sub`/UID; also test two different emails and verify they do not share a UID. Confirm local anonymous features remain unaffected, including offline startup and status.
4. Run normal Windows build, UI Text Contract and auth smoke, and inspect modal placement/focus at 100/125/150/200% display scaling. Only after live behavior is verified should account features be considered production-ready.

## ESC cloud dependency

The experimental branch `feat/ggman-esc-config-backup-cloud-20261010` must remain **unmerged**, and its cloud buttons remain **disabled**, until account login can provide a registered UID and the new ESC table/RPC rejects anonymous subjects and validates account ownership. The trial `cloudbase/sql/005_esc_profiles.sql` migration is not applied by GitHub, and is not yet authorized for production. Never announce ESC cross-PC restore just because login's REST contract compiles.

## Official references

- https://docs.cloudbase.net/http-api/auth/auth-send-verification
- https://docs.cloudbase.net/http-api/auth/auth-verify-verification
- https://docs.cloudbase.net/http-api/auth/auth-sign-in
- https://docs.cloudbase.net/http-api/auth/auth-sign-up
- https://docs.cloudbase.net/http-api/auth/auth-sign-out
- https://docs.cloudbase.net/authentication-v2/method/email-login
