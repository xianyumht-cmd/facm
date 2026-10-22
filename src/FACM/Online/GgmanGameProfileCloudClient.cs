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
    internal sealed class GgmanGameRemoteProfile
    {
        internal long Version { get; set; }
        internal DateTimeOffset UpdatedAtUtc { get; set; }
        internal GameProfile Profile { get; set; }
    }

    // Registered account only. Never use the device-anonymous CloudBaseClient for 游戏配置.
    internal sealed class GgmanGameProfileCloudClient : IDisposable
    {
        private const int MaximumResponseBytes = 640 * 1024;
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes };

        internal GgmanGameProfileCloudClient()
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

        internal static GgmanAccountIdentity RequireRegisteredSession()
        {
            var account = GgmanAccountSession.Current;
            if (account == null || string.IsNullOrWhiteSpace(account.AccessToken) ||
                string.IsNullOrWhiteSpace(account.UserId))
                throw new InvalidOperationException("请先在「我的 GGman」登录邮箱账号。");
            return account;
        }

        internal async Task<GgmanGameRemoteProfile> GetAsync(GgmanAccountIdentity identity, CancellationToken token)
        {
            var payload = await SendRegisteredAsync("ggman_get_game_profile", identity,
                new Dictionary<string, object>(), token).ConfigureAwait(false);
            if (payload.Count == 0) return null;
            object data;
            object revision;
            object updated;
            if (!payload.TryGetValue("payload", out data) || data == null ||
                !payload.TryGetValue("version", out revision) ||
                !payload.TryGetValue("updated_at", out updated))
                throw new InvalidDataException("云端 游戏配置 备份格式不正确。");
            long version;
            if (!long.TryParse(Convert.ToString(revision), out version) || version <= 0)
                throw new InvalidDataException("云端 游戏配置 备份版本无效。");
            DateTimeOffset changed;
            if (!DateTimeOffset.TryParse(Convert.ToString(updated), out changed))
                throw new InvalidDataException("云端 游戏配置 备份时间无效。");
            return new GgmanGameRemoteProfile
            {
                Version = version,
                UpdatedAtUtc = changed,
                Profile = GameProfileStore.Deserialize(data)
            };
        }

        internal async Task<long> SetAsync(GgmanAccountIdentity identity, GameProfile profile,
            long expectedVersion, CancellationToken token)
        {
            if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            var body = new Dictionary<string, object>
            {
                { "p_payload", _json.DeserializeObject(GameProfileStore.Serialize(profile)) },
                { "p_expected_version", expectedVersion }
            };
            var response = await SendRegisteredAsync("ggman_set_game_profile", identity,
                body, token).ConfigureAwait(false);
            object revision;
            long version;
            if (!response.TryGetValue("version", out revision) ||
                !long.TryParse(Convert.ToString(revision), out version) || version != expectedVersion + 1)
                throw new InvalidOperationException("云端没有确认新的 游戏配置 备份版本。");
            return version;
        }

        private static HttpRequestMessage CreateRequest(string rpc, GgmanAccountIdentity identity, object body)
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.UserId) ||
                string.IsNullOrWhiteSpace(identity.AccessToken))
                throw new InvalidOperationException("没有可用的注册账号会话。");
            if (rpc != "ggman_get_game_profile" && rpc != "ggman_set_game_profile")
                throw new ArgumentException("Unknown 游戏配置 RPC.", nameof(rpc));
            var request = new HttpRequestMessage(HttpMethod.Post, "v1/rdb/rest/rpc/" + rpc);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
            request.Content = new StringContent(new JavaScriptSerializer().Serialize(body), Encoding.UTF8, "application/json");
            return request;
        }

        private Task<Dictionary<string, object>> SendRegisteredAsync(string rpc,
            GgmanAccountIdentity account, object body, CancellationToken token)
        {
#if D1_SINGLE_OWNER
            if (GgmanD1Backend.IsEnabled)
                return GgmanD1Backend.InvokeAsync(rpc, account, body, token);
#endif
            return GgmanAccountSession.ExecuteWithRefreshAsync(account, async (current, cancellation) =>
            {
                using (var request = CreateRequest(rpc, current, body))
                    return await SendAsync(request, cancellation).ConfigureAwait(false);
            }, token);
        }

        private async Task<Dictionary<string, object>> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false))
                {
                    if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                        throw new InvalidDataException("云端响应超过大小限制。");
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new GgmanAccountUnauthorizedException();
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("游戏配置 云同步未完成，云端请求状态码：" +
                            (int)response.StatusCode + "。请检查账号登录、备份版本及数据库配置。");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        var block = new byte[8192];
                        int count;
                        while ((count = await stream.ReadAsync(block, 0, block.Length, timeout.Token).ConfigureAwait(false)) != 0)
                        {
                            if (buffer.Length + count > MaximumResponseBytes)
                                throw new InvalidDataException("云端响应超过大小限制。");
                            buffer.Write(block, 0, count);
                        }
                        var value = _json.DeserializeObject(Encoding.UTF8.GetString(buffer.ToArray())) as Dictionary<string, object>;
                        if (value == null) throw new InvalidDataException("云端 游戏配置 响应格式不正确。");
                        return value;
                    }
                }
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }

    }
}
