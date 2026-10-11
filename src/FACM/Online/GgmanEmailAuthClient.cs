using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.Online
{
    internal sealed class GgmanCaptchaRequiredException : InvalidOperationException
    {
        internal GgmanCaptchaRequiredException() : base("CloudBase 需要图片验证码验证。") { }
    }

    internal sealed class GgmanCaptchaInvalidException : InvalidOperationException
    {
        internal GgmanCaptchaInvalidException() : base("图片验证码已失效，请重新获取。") { }
    }

    internal sealed class GgmanImageCaptcha
    {
        internal string Token { get; set; }
        internal byte[] ImageBytes { get; set; }
        internal DateTimeOffset ExpiresAtUtc { get; set; }
    }

    internal sealed class GgmanEmailChallenge
    {
        public string Email { get; set; }
        public string VerificationId { get; set; }
        public bool IsRegistered { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    internal sealed class GgmanAccountIdentity
    {
        public string Email { get; set; }
        public string UserId { get; set; }
        internal string AccessToken { get; set; }
        internal string RefreshToken { get; set; }
        internal Guid SessionKey { get; set; }
    }

    internal sealed class GgmanAccountUnauthorizedException : InvalidOperationException
    {
        internal GgmanAccountUnauthorizedException() : base("GGman 账号登录已过期。") { }
    }

    // Separate from the legacy anonymous CloudBase client; no anonymous data is migrated here.
    internal static class GgmanAccountSession
    {
        private static readonly object Sync = new object();
        private static readonly SemaphoreSlim RefreshGate = new SemaphoreSlim(1, 1);
        private static GgmanAccountIdentity _current;
        internal static event EventHandler Changed;

        private static GgmanAccountIdentity Copy(GgmanAccountIdentity identity)
        {
            return identity == null ? null : new GgmanAccountIdentity
            {
                Email = identity.Email,
                UserId = identity.UserId,
                AccessToken = identity.AccessToken,
                RefreshToken = identity.RefreshToken,
                SessionKey = identity.SessionKey
            };
        }

        internal static GgmanAccountIdentity Current
        {
            get { lock (Sync) return Copy(_current); }
        }

        internal static bool IsCurrent(GgmanAccountIdentity expected)
        {
            lock (Sync)
                return expected != null && expected.SessionKey != Guid.Empty &&
                       _current != null && _current.SessionKey == expected.SessionKey &&
                       string.Equals(_current.UserId, expected.UserId, StringComparison.Ordinal);
        }

        internal static GgmanAccountIdentity RequireCurrent(GgmanAccountIdentity expected)
        {
            lock (Sync)
            {
                if (expected == null || expected.SessionKey == Guid.Empty ||
                    _current == null || _current.SessionKey != expected.SessionKey ||
                    !string.Equals(_current.UserId, expected.UserId, StringComparison.Ordinal))
                    throw new OperationCanceledException("GGman 账号已切换，当前操作已取消。");
                return Copy(_current);
            }
        }

        internal static void Set(GgmanAccountIdentity value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.UserId) ||
                string.IsNullOrWhiteSpace(value.AccessToken))
                throw new ArgumentException("Verified CloudBase account session is required.", nameof(value));
            lock (Sync)
            {
                _current = Copy(value);
                _current.SessionKey = Guid.NewGuid();
            }
            var changed = Changed;
            if (changed != null) changed(null, EventArgs.Empty);
        }

        internal static void Clear()
        {
            lock (Sync) _current = null;
            var changed = Changed;
            if (changed != null) changed(null, EventArgs.Empty);
        }

        internal static void ClearIfCurrent(GgmanAccountIdentity expected)
        {
            lock (Sync)
            {
                if (_current == null || expected == null ||
                    _current.SessionKey != expected.SessionKey)
                    return;
                _current = null;
            }
            var changed = Changed;
            if (changed != null) changed(null, EventArgs.Empty);
        }

        private static bool ApplyRenewed(GgmanAccountIdentity expected, GgmanAccountIdentity updated)
        {
            if (updated == null || string.IsNullOrWhiteSpace(updated.AccessToken) ||
                !string.Equals(updated.UserId, expected.UserId, StringComparison.Ordinal))
                throw new InvalidOperationException("续期身份与已登录 GGman 账号不一致。");
            lock (Sync)
            {
                if (_current == null || expected.SessionKey != _current.SessionKey ||
                    _current.UserId != expected.UserId || _current.AccessToken != expected.AccessToken)
                    return false;
                _current.AccessToken = updated.AccessToken;
                _current.RefreshToken = string.IsNullOrWhiteSpace(updated.RefreshToken)
                    ? expected.RefreshToken : updated.RefreshToken;
                return true;
            }
        }

        private static async Task<GgmanAccountIdentity> RenewAsync(
            GgmanAccountIdentity previous, CancellationToken cancellationToken)
        {
            await RefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var current = RequireCurrent(previous);
                if (current.AccessToken != previous.AccessToken) return current;
                if (string.IsNullOrWhiteSpace(current.RefreshToken))
                    throw new InvalidOperationException("GGman 登录已过期，请重新验证邮箱。");

                cancellationToken.ThrowIfCancellationRequested();
                var deviceId = CloudIdentityStore.CreateDefault().LoadOrCreate().DeviceId;
                GgmanAccountIdentity updated;
                using (var client = new GgmanEmailAuthClient())
                    updated = await client.RefreshAccountAsync(current, deviceId, cancellationToken)
                        .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!ApplyRenewed(current, updated))
                    throw new OperationCanceledException("GGman 账号在续期期间已切换。");
                return RequireCurrent(previous);
            }
            finally
            {
                RefreshGate.Release();
            }
        }

        internal static async Task<T> ExecuteWithRefreshAsync<T>(
            GgmanAccountIdentity expected,
            Func<GgmanAccountIdentity, CancellationToken, Task<T>> request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            var current = RequireCurrent(expected);
            try
            {
                var result = await request(current, cancellationToken).ConfigureAwait(false);
                RequireCurrent(expected);
                return result;
            }
            catch (GgmanAccountUnauthorizedException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var renewed = await RenewAsync(current, cancellationToken).ConfigureAwait(false);
                var result = await request(renewed, cancellationToken).ConfigureAwait(false);
                RequireCurrent(expected);
                return result;
            }
        }

        internal static void ValidateRenewalForSmokeTest()
        {
            Clear();
            Set(new GgmanAccountIdentity
            {
                Email = "first@example.com", UserId = "same-user",
                AccessToken = "old-access", RefreshToken = "old-refresh"
            });
            var first = Current;
            if (!ApplyRenewed(first, new GgmanAccountIdentity
                { UserId = first.UserId, AccessToken = "new-access", RefreshToken = "new-refresh" }) ||
                Current.AccessToken != "new-access" || Current.RefreshToken != "new-refresh" ||
                !IsCurrent(first))
                throw new InvalidOperationException("Registered account refresh rotation failed.");

            if (ApplyRenewed(first, new GgmanAccountIdentity
                { UserId = first.UserId, AccessToken = "stale-access" }))
                throw new InvalidOperationException("Stale access token overwrote rotated credentials.");

            Clear();
            Set(new GgmanAccountIdentity { UserId = "same-user", AccessToken = "other-login" });
            if (IsCurrent(first) || ApplyRenewed(first, new GgmanAccountIdentity
                { UserId = first.UserId, AccessToken = "replayed-access" }))
                throw new InvalidOperationException("Logged-out session was able to restore stale credentials.");

            ClearIfCurrent(first);
            if (Current == null || Current.AccessToken != "other-login")
                throw new InvalidOperationException("Stale logout cleared a newer login.");
            var current = Current;
            try
            {
                ApplyRenewed(current, new GgmanAccountIdentity
                    { UserId = "different-user", AccessToken = "cross-account" });
                throw new InvalidOperationException("Cross-account token rotation was accepted.");
            }
            catch (InvalidOperationException error)
            {
                if (error.Message == "Cross-account token rotation was accepted.") throw;
            }
            Clear();
        }
    }

    internal sealed class GgmanEmailAuthClient : IDisposable
    {
        private const int MaxResponseLength = 192 * 1024;
        private const int MaxCaptchaImageBytes = 96 * 1024;
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = MaxResponseLength };

        internal GgmanEmailAuthClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false,
                UseCookies = false
            })
            {
                BaseAddress = new Uri("https://" + CloudBaseClient.EnvironmentId + ".api.tcloudbasegateway.com/"),
                Timeout = Timeout.InfiniteTimeSpan
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("GGman-Windows/3.5");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        internal static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
                email.IndexOfAny(new[] { '\r', '\n', ' ', '\t' }) >= 0)
                return false;
            var at = email.IndexOf('@');
            return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 3 &&
                   email.IndexOf('.', at + 2) > at + 1;
        }

        internal async Task<GgmanEmailChallenge> SendCodeAsync(string email, string deviceId, CancellationToken token,
            string captchaToken = null)
        {
            email = (email ?? string.Empty).Trim();
            if (!IsValidEmail(email)) throw new ArgumentException("请输入有效的邮箱地址。");
            using (var req = CreatePost("auth/v1/verification", new Dictionary<string, object>
            {
                { "email", email }, { "target", "ANY" }
            }, deviceId))
            {
                AttachCaptchaToken(req, captchaToken);
                var obj = await SendAsync(req, token).ConfigureAwait(false);
                var verificationId = ReadString(obj, "verification_id");
                if (string.IsNullOrWhiteSpace(verificationId))
                    throw new InvalidOperationException("邮件服务未返回有效的验证码会话。");
                var expires = ReadSeconds(obj, "expires_in", 600);
                return new GgmanEmailChallenge
                {
                    Email = email,
                    VerificationId = verificationId,
                    IsRegistered = ReadBoolean(obj, "is_user"),
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expires)
                };
            }
        }

        internal async Task<GgmanImageCaptcha> GetCaptchaAsync(string deviceId, CancellationToken cancellationToken)
        {
            using (var request = CreatePost("auth/v1/captcha/data", new Dictionary<string, object>(), deviceId))
            {
                var result = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                var value = ReadString(result, "data");
                return new GgmanImageCaptcha
                {
                    Token = RequireCaptchaToken(ReadString(result, "token")),
                    ImageBytes = DecodeCaptchaImageForSmokeTest(value),
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(ReadSeconds(result, "expires_in", 300))
                };
            }
        }

        internal async Task<string> VerifyCaptchaAsync(GgmanImageCaptcha challenge, string key,
            string deviceId, CancellationToken cancellationToken)
        {
            if (challenge == null || DateTimeOffset.UtcNow >= challenge.ExpiresAtUtc)
                throw new InvalidOperationException("图片验证码已过期，请刷新后重试。");
            key = (key ?? string.Empty).Trim();
            if (key.Length < 4 || key.Length > 6 || key.Any(c => !char.IsLetterOrDigit(c) || c > 127))
                throw new ArgumentException("请输入图片里的 4 至 6 位字母或数字。");
            using (var request = CreatePost("auth/v1/captcha/data/verify",
                new Dictionary<string, object> { { "token", RequireCaptchaToken(challenge.Token) }, { "key", key } },
                deviceId))
            {
                var result = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                return RequireCaptchaToken(ReadString(result, "captcha_token"));
            }
        }

        private static string RequireCaptchaToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 ||
                value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new InvalidOperationException("CloudBase 未返回有效的图片验证码凭证。");
            return value;
        }

        internal static byte[] DecodeCaptchaImageForSmokeTest(string dataUri)
        {
            if (string.IsNullOrWhiteSpace(dataUri) || dataUri.Length > 140000 ||
                !dataUri.StartsWith("data:image/gif;base64,", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CloudBase 返回的验证码图片格式不正确。");
            byte[] decoded;
            try { decoded = Convert.FromBase64String(dataUri.Substring("data:image/gif;base64,".Length)); }
            catch (FormatException) { throw new InvalidOperationException("CloudBase 返回的图片验证码数据损坏。"); }
            if (decoded.Length < 16 || decoded.Length > MaxCaptchaImageBytes ||
                (decoded[0] != 'G' || decoded[1] != 'I' || decoded[2] != 'F' || decoded[3] != '8') ||
                (decoded[4] != '7' && decoded[4] != '9') || decoded[5] != 'a' ||
                (decoded[6] + (decoded[7] << 8)) < 1 ||
                (decoded[8] + (decoded[9] << 8)) < 1 ||
                (decoded[6] + (decoded[7] << 8)) > 1024 ||
                (decoded[8] + (decoded[9] << 8)) > 512)
                throw new InvalidOperationException("CloudBase 返回的图片验证码无效。");
            return decoded;
        }

        internal async Task<GgmanAccountIdentity> VerifyAndLoginAsync(
            GgmanEmailChallenge challenge, string code, string deviceId, CancellationToken token)
        {
            if (challenge == null || !IsValidEmail(challenge.Email) ||
                string.IsNullOrWhiteSpace(challenge.VerificationId) ||
                DateTimeOffset.UtcNow >= challenge.ExpiresAtUtc)
                throw new InvalidOperationException("验证码已过期，请重新获取。");
            if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
                throw new ArgumentException("请输入邮件中的六位数字验证码。");
            foreach (var c in code) if (c < '0' || c > '9')
                throw new ArgumentException("验证码必须是六位数字。");

            string verificationToken;
            using (var req = CreatePost("auth/v1/verification/verify", new Dictionary<string, object>
            {
                { "verification_id", challenge.VerificationId },
                { "verification_code", code }
            }, deviceId))
            {
                var verified = await SendAsync(req, token).ConfigureAwait(false);
                verificationToken = ReadString(verified, "verification_token");
                if (string.IsNullOrWhiteSpace(verificationToken))
                    throw new InvalidOperationException("验证码校验未完成。");
            }

            var path = challenge.IsRegistered ? "auth/v1/signin" : "auth/v1/signup";
            var body = new Dictionary<string, object> { { "verification_token", verificationToken } };
            if (!challenge.IsRegistered) body.Add("email", challenge.Email);
            using (var req = CreatePost(path, body, deviceId))
            {
                var result = await SendAsync(req, token).ConfigureAwait(false);
                var userId = ReadString(result, "sub");
                var access = ReadString(result, "access_token");
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(access) ||
                    string.Equals(ReadString(result, "scope"), "anonymous", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("服务器未返回有效的注册账号身份。");
                return new GgmanAccountIdentity
                {
                    Email = challenge.Email,
                    UserId = userId,
                    AccessToken = access,
                    RefreshToken = ReadString(result, "refresh_token")
                };
            }
        }

        internal async Task<GgmanAccountIdentity> RefreshAccountAsync(
            GgmanAccountIdentity previous, string deviceId, CancellationToken token)
        {
            if (previous == null || string.IsNullOrWhiteSpace(previous.RefreshToken))
                throw new InvalidOperationException("GGman 登录已过期，请重新验证邮箱。");
            using (var request = CreatePost("auth/v1/token", new Dictionary<string, object>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", previous.RefreshToken }
            }, deviceId))
            {
                var result = await SendAsync(request, token).ConfigureAwait(false);
                var userId = ReadString(result, "sub");
                var accessToken = ReadString(result, "access_token");
                if (!string.Equals(userId, previous.UserId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(accessToken) ||
                    string.Equals(ReadString(result, "scope"), "anonymous", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("GGman 续期响应的注册账号身份无效。");
                return new GgmanAccountIdentity
                {
                    Email = previous.Email,
                    UserId = userId,
                    AccessToken = accessToken,
                    RefreshToken = ReadString(result, "refresh_token")
                };
            }
        }

        internal async Task LogoutAsync(GgmanAccountIdentity account, string deviceId, CancellationToken token)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.AccessToken)) return;
            var current = GgmanAccountSession.RequireCurrent(account);
            using (var req = CreatePost("auth/v1/user/signout",
                new Dictionary<string, object>(), deviceId))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.AccessToken);
                await SendAsync(req, token).ConfigureAwait(false);
            }
        }

        private static void AttachCaptchaToken(HttpRequestMessage request, string captchaToken)
        {
            if (string.IsNullOrEmpty(captchaToken)) return;
            RequireCaptchaToken(captchaToken);
            if (!request.Headers.TryAddWithoutValidation("x-captcha-token", captchaToken))
                throw new InvalidOperationException("无法附加图片验证码。");
        }

        private HttpRequestMessage CreatePost(string path, object body, string deviceId)
        {
            Guid parsed;
            if (!Guid.TryParse(deviceId, out parsed))
                throw new InvalidOperationException("本机 CloudBase 设备身份无效。");
            var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.TryAddWithoutValidation("x-device-id", parsed.ToString("D"));
            request.Content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
            return request;
        }

        private async Task<Dictionary<string, object>> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false))
                {
                    if (response.Content.Headers.ContentLength > MaxResponseLength)
                        throw new InvalidOperationException("CloudBase 响应过大。");
                    var body = await ReadBoundedBodyAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var code = ReadAuthErrorCodeForSmokeTest(body);
                        if (code == "rate_limit_exceeded")
                            throw new InvalidOperationException("腾讯云已限制验证码发送频率，请稍后重试。");
                        if (code == "captcha_required") throw new GgmanCaptchaRequiredException();
                        if (code == "captcha_invalid" || code == "invalid_captcha" ||
                            code == "captcha_expired" || code == "captcha_used")
                            throw new GgmanCaptchaInvalidException();
                        throw new InvalidOperationException(DescribeError(response.StatusCode));
                    }
                    var result = _json.DeserializeObject(body) as Dictionary<string, object>;
                    if (result == null) throw new InvalidOperationException("CloudBase 登录响应格式不正确。");
                    return result;
                }
            }
        }

        private static async Task<string> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
        {
            using (var stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken)
                    .ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + count > MaxResponseLength)
                        throw new InvalidOperationException("CloudBase 响应过大。");
                    buffer.Write(chunk, 0, count);
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        internal static string ReadAuthErrorCodeForSmokeTest(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Length > MaxResponseLength) return string.Empty;
            try
            {
                var envelope = new JavaScriptSerializer { MaxJsonLength = MaxResponseLength }
                    .DeserializeObject(body) as Dictionary<string, object>;
                if (envelope == null) return string.Empty;
                var top = ReadString(envelope, "error");
                if (!string.IsNullOrWhiteSpace(top)) return top;
                object value;
                var data = envelope.TryGetValue("data", out value) ? value as Dictionary<string, object> : null;
                return ReadString(data, "error");
            }
            catch { return string.Empty; }
        }

        private static string DescribeError(HttpStatusCode status)
        {
            if ((int)status == 429)
                return "验证码请求过于频繁，请稍后重试。";
            if (status == HttpStatusCode.Forbidden || status == HttpStatusCode.NotImplemented)
                return "邮箱验证码登录暂不可用。请检查 CloudBase 邮箱登录开关、邮件代发及图片验证码配置。";
            if (status == HttpStatusCode.BadRequest)
                return "请求被 CloudBase 拒绝：可能是验证码错误、过期，或需要图片验证码。请重新获取。";
            return "CloudBase 身份服务暂不可用（HTTP " + (int)status + "）。";
        }

        private static string ReadString(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value) : string.Empty;
        }

        private static bool ReadBoolean(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) &&
                   value != null && Convert.ToString(value).Equals("True", StringComparison.OrdinalIgnoreCase);
        }

        private static int ReadSeconds(Dictionary<string, object> obj, string key, int fallback)
        {
            int seconds;
            return int.TryParse(ReadString(obj, key), out seconds) && seconds > 0
                ? Math.Min(seconds, 600) : fallback;
        }

        internal static void ValidateForSmokeTest()
        {
            if (ReadAuthErrorCodeForSmokeTest("{\"error\":\"captcha_required\"}") != "captcha_required" ||
                ReadAuthErrorCodeForSmokeTest("{\"data\":{\"error\":\"captcha_invalid\"}}") != "captcha_invalid" ||
                ReadAuthErrorCodeForSmokeTest("not-json") != string.Empty)
                throw new InvalidOperationException("CloudBase captcha error parsing changed.");
            using (var emailRequest = new HttpRequestMessage(HttpMethod.Post, "auth/v1/verification"))
            {
                AttachCaptchaToken(emailRequest, null);
                if (emailRequest.Headers.Contains("x-captcha-token"))
                    throw new InvalidOperationException("Normal email send fabricated CAPTCHA headers.");
                AttachCaptchaToken(emailRequest, "verified-captcha-placeholder");
                if (!emailRequest.Headers.Contains("x-captcha-token"))
                    throw new InvalidOperationException("Challenged email send lost CAPTCHA proof.");
            }
            var fakeGif = new byte[24];
            var header = Encoding.ASCII.GetBytes("GIF89a");
            Buffer.BlockCopy(header, 0, fakeGif, 0, header.Length);
            fakeGif[6] = 160;
            fakeGif[8] = 60;
            var fakeImage = "data:image/gif;base64," + Convert.ToBase64String(fakeGif);
            if (DecodeCaptchaImageForSmokeTest(fakeImage).Length < 16)
                throw new InvalidOperationException("GIF captcha data decode failed.");
            try { DecodeCaptchaImageForSmokeTest("data:text/html;base64,SGVsbG8="); throw new InvalidOperationException("Unsafe CAPTCHA MIME accepted."); }
            catch (InvalidOperationException error) { if (error.Message == "Unsafe CAPTCHA MIME accepted.") throw; }
            if (!IsValidEmail("player@example.com") || IsValidEmail("invalid-email") ||
                IsValidEmail("player@") || IsValidEmail("x@y\n.com"))
                throw new InvalidOperationException("GGman account email input validation changed.");
            GgmanAccountSession.Clear();
            if (GgmanAccountSession.Current != null)
                throw new InvalidOperationException("Account session should start signed out.");
            var account = new GgmanAccountIdentity
            {
                Email = "player@example.com", UserId = "registered-uid",
                AccessToken = "test-token"
            };
            GgmanAccountSession.Set(account);
            if (GgmanAccountSession.Current.UserId != account.UserId)
                throw new InvalidOperationException("Registered account identity was not preserved.");
            GgmanAccountSession.Clear();
            if (GgmanAccountSession.Current != null)
                throw new InvalidOperationException("Account logout did not clear in-process identity.");
            GgmanAccountSession.ValidateRenewalForSmokeTest();
            using (var client = new GgmanEmailAuthClient())
            using (var send = client.CreatePost("auth/v1/verification",
                new Dictionary<string, object> { { "email", "player@example.com" }, { "target", "ANY" } },
                Guid.NewGuid().ToString("D")))
            {
                if (send.Headers.Authorization != null || !send.Headers.Contains("x-device-id") ||
                    send.Method != HttpMethod.Post ||
                    send.RequestUri.ToString().IndexOf("verification", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("GGman email verification request contract changed.");
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
