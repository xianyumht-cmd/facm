using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.Online
{
    internal sealed class GgmanRegisteredStats
    {
        internal long PlayedAccounts { get; set; }
        internal long ActiveDays { get; set; }
        internal long Rank { get; set; }
        internal long TotalRankedUsers { get; set; }
        internal double Percentile { get; set; }
        internal bool RankingVisible { get; set; }
    }

    internal sealed class GgmanRegisteredStatsClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 };

        internal GgmanRegisteredStatsClient()
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

        internal Task TouchAsync(GgmanAccountIdentity account, CancellationToken token)
        {
            return SendAsync("ggman_registered_touch",
                new Dictionary<string, object>(), account, token);
        }

        internal Task SetRankingVisibleAsync(GgmanAccountIdentity account, bool enabled, CancellationToken token)
        {
            return SendAsync("ggman_registered_touch",
                new Dictionary<string, object> { { "p_ranking_visible", enabled } }, account, token);
        }

        internal Task RecordAccountAsync(GgmanAccountIdentity account, string accountHash, CancellationToken token)
        {
            return SendAsync("ggman_registered_record_account",
                new Dictionary<string, object> { { "p_account_key_hash", RequireHash(accountHash) } }, account, token);
        }

        internal Task ImportLegacyAsync(GgmanAccountIdentity account, string sourceKey,
            int accountCount, IReadOnlyList<string> activeDays, CancellationToken token)
        {
            if (accountCount < 0 || accountCount > 500 || activeDays == null || activeDays.Count > 2000)
                throw new InvalidDataException("Invalid legacy account summary.");
            return SendAsync("ggman_registered_import_legacy", new Dictionary<string, object>
            {
                { "p_source_key", RequireHash(sourceKey) },
                { "p_legacy_account_count", accountCount },
                { "p_active_days", activeDays }
            }, account, token);
        }

        internal async Task<GgmanRegisteredStats> GetStatsAsync(GgmanAccountIdentity account, CancellationToken token)
        {
            var obj = await SendAsync("ggman_get_registered_personal_stats",
                new Dictionary<string, object>(), account, token).ConfigureAwait(false);
            return new GgmanRegisteredStats
            {
                PlayedAccounts = ReadLong(obj, "played_accounts"),
                ActiveDays = ReadLong(obj, "active_days"),
                Rank = ReadLong(obj, "rank"),
                TotalRankedUsers = ReadLong(obj, "total_ranked_users"),
                Percentile = ReadDouble(obj, "percentile"),
                RankingVisible = ReadBoolean(obj, "ranking_visible")
            };
        }

        internal static string CreateRegisteredAccountHash(string userId, string puuid)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(puuid) ||
                puuid.Trim().Length < 8) throw new ArgumentException("Registered account identity is incomplete.");
            return Hmac(userId.Trim(), "registered-account:" + puuid.Trim());
        }

        internal static string CreateLegacySourceKey(string userId, string deviceId)
        {
            Guid parsed;
            if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(deviceId, out parsed))
                throw new ArgumentException("Registered user and device identity are required.");
            return Hmac(userId.Trim(), "legacy-device:" + parsed.ToString("D"));
        }

        private static string Hmac(string key, string value)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
                var text = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        private static string RequireHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                throw new ArgumentException("Account key hash must have 64 hex characters.");
            for (var i = 0; i < value.Length; i++)
                if (!Uri.IsHexDigit(value[i]) || value[i] >= 'A' && value[i] <= 'F')
                    throw new ArgumentException("Account key hash must be lowercase hexadecimal.");
            return value;
        }

        private Task<Dictionary<string, object>> SendAsync(string endpoint, object body,
            GgmanAccountIdentity account, CancellationToken token)
        {
            return GgmanAccountSession.ExecuteWithRefreshAsync(account,
                (current, cancellation) => SendOnceAsync(endpoint, body, current, cancellation), token);
        }

        private async Task<Dictionary<string, object>> SendOnceAsync(string endpoint, object body,
            GgmanAccountIdentity account, CancellationToken token)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.UserId) ||
                string.IsNullOrWhiteSpace(account.AccessToken))
                throw new InvalidOperationException("请先登录 GGman 邮箱账号。");

            using (var request = new HttpRequestMessage(HttpMethod.Post, "v1/rdb/rest/rpc/" + endpoint))
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
                        throw new InvalidOperationException("注册账号统计暂时不可用（HTTP " +
                            (int)response.StatusCode + "）。");
                    if (response.Content.Headers.ContentLength > 128 * 1024)
                        throw new InvalidDataException("Statistics response exceeds limit.");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var memory = new MemoryStream())
                    {
                        var buffer = new byte[4096];
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)
                            .ConfigureAwait(false)) > 0)
                        {
                            if (memory.Length + read > 128 * 1024)
                                throw new InvalidDataException("Statistics response exceeds limit.");
                            memory.Write(buffer, 0, read);
                        }
                        var parsed = _json.DeserializeObject(Encoding.UTF8.GetString(memory.ToArray()))
                            as Dictionary<string, object>;
                        if (parsed == null) throw new InvalidDataException("Statistics response is invalid.");
                        return parsed;
                    }
                }
            }
        }

        private static long ReadLong(Dictionary<string, object> obj, string field)
        {
            object value;
            long result;
            return obj.TryGetValue(field, out value) &&
                long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? Math.Max(0, result) : 0;
        }

        private static double ReadDouble(Dictionary<string, object> obj, string field)
        {
            object value;
            double result;
            return obj.TryGetValue(field, out value) &&
                double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result : 0;
        }

        private static bool ReadBoolean(Dictionary<string, object> obj, string field)
        {
            object value;
            return obj.TryGetValue(field, out value) && value != null &&
                Convert.ToString(value, CultureInfo.InvariantCulture) == "True";
        }

        internal static void ValidateForSmokeTest()
        {
            const string registered = "registered-account-test";
            var a = CreateRegisteredAccountHash(registered, "sample-puuid-123456");
            var same = CreateRegisteredAccountHash(registered, "sample-puuid-123456");
            var b = CreateRegisteredAccountHash("another-user", "sample-puuid-123456");
            var source = CreateLegacySourceKey(registered, "d32cf3ea-6fde-477c-8f90-52282d77fa1a");
            if (a != same || a == b || a.Length != 64 || source.Length != 64 || source == a ||
                a.Contains("sample-puuid"))
                throw new InvalidOperationException("Registered account hash is not scoped and stable.");
        }

        public void Dispose() { _http.Dispose(); }
    }
}
