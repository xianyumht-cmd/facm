# GGman registered-account identity — CloudBase email OTP

**Status: unmerged/field acceptance pending (Issue #313)**

## Account product contract

- Login is optional, from **我的 GGman → GGman 账号登录**. Anonymous use of League features, local stats, original cloud device stats and `ggman_settings_sync` continues unchanged.
- Registered login uses CloudBase authentication v2 REST with the existing environment's public HTTPS gateway: `POST /auth/v1/verification` (`{email,target:"ANY"}`), `POST /auth/v1/verification/verify` (`{verification_id,verification_code}`), then `POST /auth/v1/signin` for existing users or `POST /auth/v1/signup` with email for new users. The returned `sub`/UID is the player identity, rather than the prior device-derived anonymous subject. Use `POST /auth/v1/user/signout` for explicit server logout.
- Backend decides account registration vs sign-in through the `is_user` indicator. The service's default six-digit code validity is 10 minutes; CloudBase enforces a one-minute send interval. The GUI also checks the address, binds each verification to its original email, limits attempts, and does not send codes on launch.
- First stage keeps access/refresh tokens **only in process memory**. No 'remember me' toggle, plaintext file, DPAPI persistent credentials, server secrets, session exports or debug logging of email/code/token. Restart or close GGman to sign out locally; for server revocation, click explicit **退出登录** while still signed in. If CloudBase logout fails, clear local credentials and tell the user that server revocation was not confirmed. Never use registered tokens to overwrite existing anonymous `ggman_devices`, usage telemetry, or settings sync.
- The login UI does not expose hidden user identifiers/credentials in exported diagnostics. Account code displays the authenticated UID after successful login only, to help compare two profiles during acceptance. Authentication failures must not imply login success.
- When CloudBase returns `captcha_required`, GGman fetches a bounded GIF through `POST /auth/v1/captcha/data`, displays it inside a native modal, submits the user's 4–6 character answer to `POST /auth/v1/captcha/data/verify`, and attaches the verified `captcha_token` as `x-captcha-token` to **one** retry of the original email-send request. Cancellation, invalid/expired images, a second challenge and failed verification all stop sending. There is no automatic loop or CAPTCHA bypass.

## Required CloudBase console setup (not automatable from a code commit)

1. Open your Tencent CloudBase project for **`ggman-d4gioqqcz434d9e4d`**, enter **身份认证 → 登录方式 → 邮箱验证码 → 配置发件邮箱**, choose official email forwarding or the project's preconfigured authorized SMTP, and save. Also confirm that user registration and email verification are enabled and that the appropriate email template sends a six-digit code.
2. Test with a real mailbox in GGman and confirm code delivery, verification, newly registered sign-up, later sign-in, and server logout. Force/test a CloudBase `captcha_required` episode through its official provider test controls: fetch image, submit answer, allow exactly one `x-captcha-token` retry, then confirm errors/cancellation do not send. This CAPTCHA source integration is implemented but is **not proven live**. Do not disable anti-bot protection to obtain a passing login.
3. Sign in with **the same email** from two independent Windows profiles/devices and compare the returned stable `sub`/UID; also test two different emails and verify they do not share a UID. Confirm local anonymous features remain unaffected, including offline startup and status.
4. Run normal Windows build, UI Text Contract and auth smoke, and inspect modal placement/focus at 100/125/150/200% display scaling. Only after live behavior is verified should account features be considered production-ready.

## Service-side abuse safeguards and operational limit

The login client enforces a 60-second resend cooldown per active dialog and bounded image/OTP retries to improve honest-user behavior. This is **not a server-side security boundary**, because public auth endpoints can be called without the GGman GUI. CloudBase currently documents **one code per destination per 60 seconds** and **ten sends per IP per hour**, plus `captcha_required` and `rate_limit_exceeded` error responses. Do not assume these provider defaults are configurable or that distributed abuse is impossible; confirm actual enforcement on the project's plan in CloudBase without launching load tests against real mailboxes. A single failed request must never trigger an unbounded resend, even after image validation.

Before public release, verify in the **real CloudBase console**: email sender and allowed methods, daily email/SMS usage and quotas, actual account protection, application-access restrictions, monitoring and project-budget alerts. If a strict daily global send ceiling is required and the provider cannot enforce one, stop and design an **authenticated, server-owned gateway and limit ledger**; do not put any server/admin secret into GGman or trust a client-side counter. Do not log destination email addresses, challenge tokens, OTPs, refresh tokens or raw HTTP bodies in diagnostics.

Official support references for the runtime CAPTCHA API:
- https://docs.cloudbase.net/http-api/auth/auth-get-captcha-data
- https://docs.cloudbase.net/http-api/auth/auth-verify-captcha-data
- https://docs.cloudbase.net/authentication-v2/method/captcha

## ESC cloud dependency

The experimental branch `feat/ggman-esc-config-backup-cloud-20261010` must remain **unmerged**, and its cloud buttons remain **disabled**, until account login can provide a registered UID and the new ESC table/RPC rejects anonymous subjects and validates account ownership. The trial `cloudbase/sql/005_esc_profiles.sql` migration is not applied by GitHub, and is not yet authorized for production. Never announce ESC cross-PC restore just because login's REST contract compiles.

## Official references

- https://docs.cloudbase.net/http-api/auth/auth-send-verification
- https://docs.cloudbase.net/http-api/auth/auth-verify-verification
- https://docs.cloudbase.net/http-api/auth/auth-sign-in
- https://docs.cloudbase.net/http-api/auth/auth-sign-up
- https://docs.cloudbase.net/http-api/auth/auth-sign-out
- https://docs.cloudbase.net/authentication-v2/method/email-login
