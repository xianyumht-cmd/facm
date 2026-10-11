using System;
using System.Collections.Generic;
using System.IO;
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
    internal sealed class GgmanAppSettingsRemoteProfile
    {
        internal long Version { get; set; }
        internal DateTimeOffset UpdatedAtUtc { get; set; }
        internal GgmanPortableSettingsProfile Profile { get; set; }
    }

    internal sealed class GgmanAppSettingsCloudClient : IDisposable
    {
        private const int MaxResponse = 256 * 1024;
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = MaxResponse };

        internal GgmanAppSettingsCloudClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                BaseAddress = new Uri("https://" + CloudBaseClient.EnvironmentId + ".api.tcloudbasegateway.com/"),
                Timeout = Timeout.InfiniteTimeSpan
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("GGman-Windows/3.5");
        }

        internal async Task<GgmanAppSettingsRemoteProfile> GetAsync(GgmanAccountIdentity account, CancellationToken token)
        {
            var result = await SendAsync("ggman_get_registered_app_settings", account,
                new Dictionary<string, object>(), token).ConfigureAwait(false);
            if (result.Count == 0) return null;
            object payload, revision, timestamp;
            long version;
            DateTimeOffset updated;
            if (!result.TryGetValue("payload", out payload) ||
                !result.TryGetValue("version", out revision) ||
                !result.TryGetValue("updated_at", out timestamp) ||
                !long.TryParse(Convert.ToString(revision), out version) || version <= 0 ||
                !DateTimeOffset.TryParse(Convert.ToString(timestamp), out updated))
                throw new InvalidDataException("云端软件设置配置格式不正确。");
            return new GgmanAppSettingsRemoteProfile
            {
                Profile = GgmanPortableSettingsStore.Deserialize(payload),
                Version = version,
                UpdatedAtUtc = updated
            };
        }

        internal async Task<long> SetAsync(GgmanAccountIdentity account,
            GgmanPortableSettingsProfile profile, long expectedVersion, CancellationToken token)
        {
            if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            var payload = _json.DeserializeObject(GgmanPortableSettingsStore.Serialize(profile));
            var result = await SendAsync("ggman_set_registered_app_settings", account,
                new Dictionary<string, object>
                {
                    { "p_payload", payload },
                    { "p_expected_version", expectedVersion }
                }, token).ConfigureAwait(false);
            object revision;
            long updatedVersion;
            if (!result.TryGetValue("version", out revision) ||
                !long.TryParse(Convert.ToString(revision), out updatedVersion) ||
                updatedVersion != expectedVersion + 1)
                throw new InvalidDataException("云端未确认新的软件偏好版本。");
            return updatedVersion;
        }

        private Task<Dictionary<string, object>> SendAsync(string operation,
            GgmanAccountIdentity account, object body, CancellationToken token)
        {
            return GgmanAccountSession.ExecuteWithRefreshAsync(account,
                (current, cancellation) => SendOnceAsync(operation, current, body, cancellation), token);
        }

        private async Task<Dictionary<string, object>> SendOnceAsync(string operation,
            GgmanAccountIdentity account, object body, CancellationToken token)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.UserId) ||
                string.IsNullOrWhiteSpace(account.AccessToken))
                throw new InvalidOperationException("请先登录 GGman 邮箱账号。");
            if (operation != "ggman_get_registered_app_settings" && operation != "ggman_set_registered_app_settings")
                throw new ArgumentException("Unknown portable app settings RPC.");
            using (var request = new HttpRequestMessage(HttpMethod.Post, "v1/rdb/rest/rpc/" + operation))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
                request.Content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new GgmanAccountUnauthorizedException();
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("云端软件设置同步未完成（HTTP " +
                            (int)response.StatusCode + "）。请检查账号登录或云端版本。");
                    if (response.Content.Headers.ContentLength > MaxResponse)
                        throw new InvalidDataException("云端返回的文字数据过大。");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var memory = new MemoryStream())
                    {
                        var buffer = new byte[4096];
                        int count;
                        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)
                            .ConfigureAwait(false)) > 0)
                        {
                            if (memory.Length + count > MaxResponse)
                                throw new InvalidDataException("云端返回的文字数据过大。");
                            memory.Write(buffer, 0, count);
                        }
                        var json = Encoding.UTF8.GetString(memory.ToArray());
                        var parsed = _json.DeserializeObject(json) as Dictionary<string, object>;
                        if (parsed == null) throw new InvalidDataException("云端软件设置响应不合法。");
                        return parsed;
                    }
                }
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var sample = new GgmanAccountIdentity
            {
                UserId = "sample-registered-uid",
                AccessToken = "not-a-real-token"
            };
            if (string.IsNullOrEmpty(sample.UserId) || sample.AccessToken.Length < 5)
                throw new InvalidOperationException("portable app settings sync identity fixture invalid.");
        }

        public void Dispose() { _http.Dispose(); }
    }
}
