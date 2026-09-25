using System;
using System.Collections.Generic;
using System.Globalization;
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
    internal sealed class CloudBaseSession
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string Subject { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }

        public bool IsUsable(DateTimeOffset now)
        {
            return !string.IsNullOrWhiteSpace(AccessToken) &&
                   !string.IsNullOrWhiteSpace(Subject) &&
                   ExpiresAtUtc > now.AddMinutes(2);
        }
    }

    internal sealed class CloudPersonalRanking
    {
        public long played_accounts { get; set; }
        public long rank { get; set; }
        public long total_ranked_users { get; set; }
        public double percentile { get; set; }
    }

    internal sealed class CloudDeviceRow
    {
        public string owner_id { get; set; }
        public string device_id { get; set; }
        public string app_version { get; set; }
        public string os_version { get; set; }
        public string last_seen_at { get; set; }
    }

    internal sealed class CloudSettingsRemoteState
    {
        public CloudSettingsSnapshot Settings { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    internal sealed class CloudBaseClient : IDisposable
    {
        internal const string EnvironmentId = "ggman-d4gioqqcz434d9e4d";
        private const int MaximumResponseCharacters = 128 * 1024;
        private static readonly Uri Gateway = new Uri(
            "https://" + EnvironmentId + ".api.tcloudbasegateway.com/",
            UriKind.Absolute);

        private readonly HttpClient _client;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = MaximumResponseCharacters };
        private readonly SemaphoreSlim _sessionGate = new SemaphoreSlim(1, 1);
        private CloudBaseSession _session;
        private bool _disposed;

        public CloudBaseClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = false
            };
            _client = new HttpClient(handler)
            {
                BaseAddress = Gateway,
                Timeout = Timeout.InfiniteTimeSpan
            };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("GGman-Windows/3.5");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        public async Task<CloudDeviceRow> SyncDeviceAsync(
            string deviceId,
            string appVersion,
            string osVersion,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            var session = await EnsureSessionAsync(deviceId, cancellationToken).ConfigureAwait(false);
            using (var upsert = CreateDeviceUpsertRequest(
                session.AccessToken,
                deviceId,
                appVersion,
                osVersion,
                DateTimeOffset.UtcNow))
            {
                await SendAsync(upsert, "device upsert", cancellationToken).ConfigureAwait(false);
            }

            string responseText;
            using (var read = CreateDeviceReadRequest(session.AccessToken, deviceId))
            {
                responseText = await SendAsync(read, "device readback", cancellationToken).ConfigureAwait(false);
            }

            var rows = _json.Deserialize<CloudDeviceRow[]>(responseText) ?? new CloudDeviceRow[0];
            if (rows.Length != 1 ||
                !string.Equals(rows[0].device_id, deviceId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(rows[0].owner_id, session.Subject, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CloudBase device ownership verification failed.");
            }

            return rows[0];
        }

        public string CurrentSubject
        {
            get { return _session == null ? string.Empty : _session.Subject ?? string.Empty; }
        }

        public async Task RecordAccountAsync(
            string deviceId,
            PersonalStatsAccountRecord account,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (account == null || string.IsNullOrWhiteSpace(account.AccountKeyHash))
                throw new ArgumentException("Account history record is required.", nameof(account));

            var session = await EnsureSessionAsync(deviceId, cancellationToken).ConfigureAwait(false);
            var body = _json.Serialize(new Dictionary<string, object>
            {
                { "p_account_key_hash", account.AccountKeyHash },
                { "p_region", account.Region ?? string.Empty },
                { "p_first_seen_at", account.FirstSeenUtc ?? string.Empty },
                { "p_last_seen_at", account.LastSeenUtc ?? string.Empty },
                { "p_seen_count", Math.Max(1, account.SeenCount) }
            });

            using (var request = new HttpRequestMessage(HttpMethod.Post, "v1/rdb/rest/rpc/ggman_record_account"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RequireAccessToken(session.AccessToken));
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                await SendAsync(request, "account history sync", cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SetRankingOptInAsync(
            string deviceId,
            bool enabled,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var session = await EnsureSessionAsync(deviceId, cancellationToken).ConfigureAwait(false);
            var body = _json.Serialize(new Dictionary<string, object>
            {
                { "ranking_opt_in", enabled }
            });

            var relative = "v1/rdb/rest/ggman_devices?device_id=eq." + Uri.EscapeDataString(deviceId ?? string.Empty);
            using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), relative))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RequireAccessToken(session.AccessToken));
                request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                await SendAsync(request, "ranking preference sync", cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<CloudPersonalRanking> GetPersonalRankingAsync(
            string deviceId,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var session = await EnsureSessionAsync(deviceId, cancellationToken).ConfigureAwait(false);
            using (var request = new HttpRequestMessage(HttpMethod.Post, "v1/rdb/rest/rpc/ggman_get_personal_stats"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RequireAccessToken(session.AccessToken));
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                var responseText = await SendAsync(request, "personal ranking", cancellationToken).ConfigureAwait(false);
                var rows = _json.Deserialize<CloudPersonalRanking[]>(responseText) ?? new CloudPersonalRanking[0];
                return rows.Length == 0 ? null : rows[0];
            }
        }

        public async Task<CloudSettingsRemoteState> GetSettingsAsync(
            string deviceId,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var session = await EnsureSessionAsync(deviceId, cancellationToken).ConfigureAwait(false);
            using (var request = new HttpRequestMessage(HttpMethod.Post, "v1/rdb/rest/rpc/ggman_get_settings_sync"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RequireAccessToken(session.AccessToken));
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                var responseText = await SendAsync(request, "settings read", cancellationToken).ConfigureAwait(false);
                var payload = _json.DeserializeObject(responseText) as Dictionary<string, object>;
                if (payload == null || payload.Count == 0) return null;

                object settingsValue;
                object updatedValue;
                if (!payload.TryGetValue("settings", out settingsValue) ||
                    !payload.TryGetValue("updated_at", out updatedValue) ||
                    settingsValue == null || updatedValue == null)
                    return null;

                DateTimeOffset updatedAt;
                if (!DateTimeOffset.TryParse(Convert.ToString(updatedValue), out updatedAt))
                    throw new InvalidOperationException("CloudBase returned an invalid settings timestamp.");

                return new CloudSettingsRemoteState
                {
                    Settings = CloudSettingsSnapshot.Deserialize(settingsValue),
                    UpdatedAtUtc = updatedAt.ToUniversalTime()
                };
            }
        }

        public async Task<DateTimeOffset> SetSettingsAsync(
            string deviceId,
            CloudSettingsSnapshot settings,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var session = await EnsureSessionAsync(deviceId, cancellationToken).ConfigureAwait(false);
            var body = _json.Serialize(new Dictionary<string, object>
            {
                { "p_settings", settings }
            });

            using (var request = new HttpRequestMessage(HttpMethod.Post, "v1/rdb/rest/rpc/ggman_set_settings_sync"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RequireAccessToken(session.AccessToken));
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                var responseText = await SendAsync(request, "settings write", cancellationToken).ConfigureAwait(false);
                var payload = _json.DeserializeObject(responseText) as Dictionary<string, object>;
                object updatedValue;
                if (payload == null || !payload.TryGetValue("updated_at", out updatedValue) || updatedValue == null)
                    throw new InvalidOperationException("CloudBase returned an invalid settings write timestamp.");

                DateTimeOffset updatedAt;
                if (!DateTimeOffset.TryParse(Convert.ToString(updatedValue), out updatedAt))
                    throw new InvalidOperationException("CloudBase returned an invalid settings write timestamp.");
                return updatedAt.ToUniversalTime();
            }
        }

        private async Task<CloudBaseSession> EnsureSessionAsync(string deviceId, CancellationToken cancellationToken)
        {
            var current = _session;
            if (current != null && current.IsUsable(DateTimeOffset.UtcNow)) return current;

            await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                current = _session;
                if (current != null && current.IsUsable(DateTimeOffset.UtcNow)) return current;

                if (current != null && !string.IsNullOrWhiteSpace(current.RefreshToken))
                {
                    try
                    {
                        var refreshed = await RefreshAsync(deviceId, current.RefreshToken, cancellationToken).ConfigureAwait(false);
                        _session = refreshed;
                        return refreshed;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        AppLog.Info("CloudBase session refresh skipped: " + exception.GetType().Name);
                    }
                }

                var signedIn = await SignInAnonymouslyAsync(deviceId, cancellationToken).ConfigureAwait(false);
                _session = signedIn;
                return signedIn;
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        private async Task<CloudBaseSession> SignInAnonymouslyAsync(string deviceId, CancellationToken cancellationToken)
        {
            using (var request = CreateAnonymousSignInRequest(deviceId))
            {
                var responseText = await SendAsync(request, "anonymous sign-in", cancellationToken).ConfigureAwait(false);
                return ParseSession(responseText);
            }
        }

        private async Task<CloudBaseSession> RefreshAsync(
            string deviceId,
            string refreshToken,
            CancellationToken cancellationToken)
        {
            using (var request = CreateRefreshRequest(deviceId, refreshToken))
            {
                var responseText = await SendAsync(request, "token refresh", cancellationToken).ConfigureAwait(false);
                return ParseSession(responseText);
            }
        }

        private async Task<string> SendAsync(
            HttpRequestMessage request,
            string operation,
            CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using (var response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false))
                {
                    var length = response.Content.Headers.ContentLength;
                    if (length.HasValue && length.Value > MaximumResponseCharacters)
                        throw new InvalidOperationException("CloudBase response exceeded the allowed size.");

                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException(BuildFailureMessage(operation, response.StatusCode));

                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (text != null && text.Length > MaximumResponseCharacters)
                        throw new InvalidOperationException("CloudBase response exceeded the allowed size.");
                    return text ?? string.Empty;
                }
            }
        }

        private CloudBaseSession ParseSession(string text)
        {
            var response = _json.Deserialize<CloudBaseTokenResponse>(text ?? string.Empty);
            if (response == null ||
                string.IsNullOrWhiteSpace(response.access_token) ||
                string.IsNullOrWhiteSpace(response.sub))
            {
                throw new InvalidOperationException("CloudBase returned an invalid session.");
            }

            var lifetimeSeconds = response.expires_in <= 0 ? 7200 : response.expires_in;
            return new CloudBaseSession
            {
                AccessToken = response.access_token,
                RefreshToken = response.refresh_token ?? string.Empty,
                Subject = response.sub,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds)
            };
        }

        private static HttpRequestMessage CreateAnonymousSignInRequest(string deviceId)
        {
            Guid parsed;
            if (!Guid.TryParse(deviceId, out parsed))
                throw new ArgumentException("A valid device ID is required.", nameof(deviceId));

            var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/signin/anonymously");
            request.Headers.TryAddWithoutValidation("x-device-id", parsed.ToString("D"));
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return request;
        }

        private string SerializeRefreshBody(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new ArgumentException("Refresh token is required.", nameof(refreshToken));

            return _json.Serialize(new Dictionary<string, object>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            });
        }

        private HttpRequestMessage CreateRefreshRequest(string deviceId, string refreshToken)
        {
            Guid parsed;
            if (!Guid.TryParse(deviceId, out parsed))
                throw new ArgumentException("A valid device ID is required.", nameof(deviceId));

            var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/token");
            request.Headers.TryAddWithoutValidation("x-device-id", parsed.ToString("D"));
            request.Content = new StringContent(SerializeRefreshBody(refreshToken), Encoding.UTF8, "application/json");
            return request;
        }

        private HttpRequestMessage CreateDeviceUpsertRequest(
            string accessToken,
            string deviceId,
            string appVersion,
            string osVersion,
            DateTimeOffset now)
        {
            var body = _json.Serialize(new Dictionary<string, object>
            {
                { "device_id", deviceId },
                { "app_version", appVersion ?? string.Empty },
                { "os_version", osVersion ?? string.Empty },
                { "last_seen_at", now.ToString("o", CultureInfo.InvariantCulture) }
            });

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                "v1/rdb/rest/ggman_devices?on_conflict=device_id");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RequireAccessToken(accessToken));
            request.Headers.TryAddWithoutValidation(
                "Prefer",
                "resolution=merge-duplicates,return=representation,missing=default");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return request;
        }

        private static HttpRequestMessage CreateDeviceReadRequest(string accessToken, string deviceId)
        {
            var relative =
                "v1/rdb/rest/ggman_devices" +
                "?select=owner_id,device_id,app_version,os_version,last_seen_at" +
                "&device_id=eq." + Uri.EscapeDataString(deviceId ?? string.Empty);

            var request = new HttpRequestMessage(HttpMethod.Get, relative);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RequireAccessToken(accessToken));
            return request;
        }

        private static string RequireAccessToken(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("Access token is required.", nameof(accessToken));
            return accessToken.Trim();
        }

        private static string BuildFailureMessage(string operation, HttpStatusCode statusCode)
        {
            return "CloudBase " + (operation ?? "request") + " failed; status=" + (int)statusCode + ".";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session = null;
            _sessionGate.Dispose();
            _client.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CloudBaseClient));
        }

        internal static void ValidateForSmokeTest()
        {
            CloudSettingsSnapshot.ValidateForSmokeTest();

            const string access = "access-secret-for-smoke";
            const string refresh1 = "refresh-secret-one";
            const string refresh2 = "refresh-secret-two";
            const string subject = "cloud-user-smoke";
            var deviceId = Guid.NewGuid().ToString("D");

            using (var client = new CloudBaseClient())
            {
                var first = client.ParseSession(
                    "{\"token_type\":\"Bearer\",\"access_token\":\"" + access +
                    "\",\"refresh_token\":\"" + refresh1 +
                    "\",\"expires_in\":7200,\"sub\":\"" + subject + "\"}");
                Require(first.Subject == subject && first.RefreshToken == refresh1,
                    "CloudBase anonymous session parsing failed.");

                var rotated = client.ParseSession(
                    "{\"token_type\":\"Bearer\",\"access_token\":\"access-two" +
                    "\",\"refresh_token\":\"" + refresh2 +
                    "\",\"expires_in\":7200,\"sub\":\"" + subject + "\"}");
                Require(rotated.RefreshToken == refresh2 && rotated.RefreshToken != first.RefreshToken,
                    "CloudBase refresh-token rotation parsing failed.");

                using (var signIn = CreateAnonymousSignInRequest(deviceId))
                {
                    Require(signIn.Headers.Contains("x-device-id"),
                        "CloudBase anonymous sign-in request lost x-device-id.");
                    Require(signIn.Headers.Authorization == null,
                        "CloudBase anonymous sign-in must not use a server credential.");
                }

                using (var upsert = client.CreateDeviceUpsertRequest(
                    access,
                    deviceId,
                    "3.5.40.0",
                    "Windows",
                    DateTimeOffset.UtcNow))
                {
                    var body = upsert.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Require(body.IndexOf("owner_id", StringComparison.OrdinalIgnoreCase) < 0,
                        "CloudBase device write must leave owner_id to auth.uid().");
                    Require(body.IndexOf(deviceId, StringComparison.OrdinalIgnoreCase) >= 0,
                        "CloudBase device write lost device_id.");
                }

                using (var read = CreateDeviceReadRequest(access, deviceId))
                {
                    Require(read.RequestUri.ToString().IndexOf(deviceId, StringComparison.OrdinalIgnoreCase) >= 0,
                        "CloudBase device readback is not scoped to device_id.");
                }
            }

            var diagnostic = BuildFailureMessage("device readback", HttpStatusCode.Forbidden);
            Require(diagnostic.IndexOf(access, StringComparison.Ordinal) < 0 &&
                    diagnostic.IndexOf(refresh1, StringComparison.Ordinal) < 0,
                "CloudBase diagnostic text leaked a token.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class CloudBaseTokenResponse
        {
            public string token_type { get; set; }
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public int expires_in { get; set; }
            public string sub { get; set; }
        }
    }
}