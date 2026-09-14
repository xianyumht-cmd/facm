using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueChallengePreferencesSnapshot
    {
        public bool Connected { get; set; }
        public string BannerAccent { get; set; }
    }

    internal sealed class LeagueChallengePreferencesApplyResult
    {
        public string Status { get; set; }
        public string RequestedBannerAccent { get; set; }
        public LeagueChallengePreferencesSnapshot Observed { get; set; }
    }

    /// <summary>
    /// Explicit, user-directed profile challenge-preference customization. It uses the local
    /// challenge summary as authoritative readback, writes only the requested bannerAccent field,
    /// then performs bounded first + settled verification. It never polls or rewrites in a loop.
    /// </summary>
    internal sealed class LeagueChallengePreferencesService
    {
        internal const string SummaryPath = "/lol-challenges/v1/summary-player-data/local-player";
        internal const string LastSeasonBannerAccent = "2";

        private readonly ILeagueClientApi _client;
        private readonly ILeagueChallengePreferencesWriteApi _writer;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 };
        private readonly TimeSpan _firstVerificationDelay;
        private readonly TimeSpan _settleVerificationDelay;

        public LeagueChallengePreferencesService(
            ILeagueClientApi client,
            ILeagueChallengePreferencesWriteApi writer)
            : this(client, writer, TimeSpan.FromMilliseconds(180), TimeSpan.FromMilliseconds(320))
        {
        }

        internal LeagueChallengePreferencesService(
            ILeagueClientApi client,
            ILeagueChallengePreferencesWriteApi writer,
            TimeSpan firstVerificationDelay,
            TimeSpan settleVerificationDelay)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _firstVerificationDelay = firstVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : firstVerificationDelay;
            _settleVerificationDelay = settleVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : settleVerificationDelay;
        }

        public async Task<LeagueChallengePreferencesSnapshot> ReadCurrentAsync(CancellationToken cancellationToken)
        {
            var bytes = await ReadWithTimeoutAsync(SummaryPath, cancellationToken).ConfigureAwait(false);
            return Parse(bytes);
        }

        public async Task<LeagueChallengePreferencesApplyResult> ApplyLastSeasonBannerAsync(
            CancellationToken cancellationToken)
        {
            var current = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (current == null || !current.Connected)
            {
                AppLog.Info("League challenge summary unavailable before banner-accent change.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "unavailable",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = current
                };
            }

            var payload = BuildBannerPayload(LastSeasonBannerAccent);
            var response = await _writer.TryUpdatePlayerPreferencesAsync(payload, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                AppLog.Info("League challenge-preferences banner write rejected; status=" +
                            (response == null ? 0 : response.StatusCode));
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "write-failed",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = current
                };
            }

            if (_firstVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_firstVerificationDelay, cancellationToken).ConfigureAwait(false);
            var first = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (first == null || !first.Connected || string.IsNullOrWhiteSpace(first.BannerAccent))
            {
                AppLog.Info("League banner-accent first readback unavailable.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "unverified",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = first
                };
            }
            if (!MatchesBanner(first, LastSeasonBannerAccent))
            {
                AppLog.Info("League banner-accent first readback did not match requested state.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "overridden",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = first
                };
            }

            if (_settleVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_settleVerificationDelay, cancellationToken).ConfigureAwait(false);
            var settled = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (settled == null || !settled.Connected || string.IsNullOrWhiteSpace(settled.BannerAccent))
            {
                AppLog.Info("League banner-accent settled readback unavailable.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "unverified",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = settled
                };
            }
            if (!MatchesBanner(settled, LastSeasonBannerAccent))
            {
                AppLog.Info("League banner-accent state was overwritten by the League client.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "overridden",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = settled
                };
            }

            AppLog.Info("League last-season banner accent applied and verified.");
            return new LeagueChallengePreferencesApplyResult
            {
                Status = "success",
                RequestedBannerAccent = LastSeasonBannerAccent,
                Observed = settled
            };
        }

        internal LeagueChallengePreferencesSnapshot ParseForSmokeTest(byte[] bytes)
        {
            return Parse(bytes);
        }

        internal string BuildBannerPayloadForSmokeTest(string bannerAccent)
        {
            return IsValidBannerAccent(bannerAccent) ? BuildBannerPayload(bannerAccent) : null;
        }

        private async Task<byte[]> ReadWithTimeoutAsync(string path, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    return await _client.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return null;
                }
            }
        }

        private LeagueChallengePreferencesSnapshot Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new LeagueChallengePreferencesSnapshot();
            try
            {
                var root = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                if (root == null) return new LeagueChallengePreferencesSnapshot();

                var bannerAccent = ReadScalarString(root, "bannerAccent");
                if (string.IsNullOrWhiteSpace(bannerAccent))
                {
                    object preferencesValue;
                    var preferences = root.TryGetValue("preferences", out preferencesValue)
                        ? preferencesValue as Dictionary<string, object>
                        : null;
                    bannerAccent = ReadScalarString(preferences, "bannerAccent");
                }

                return new LeagueChallengePreferencesSnapshot
                {
                    Connected = true,
                    BannerAccent = bannerAccent
                };
            }
            catch
            {
                return new LeagueChallengePreferencesSnapshot();
            }
        }

        private string BuildBannerPayload(string bannerAccent)
        {
            return _json.Serialize(new Dictionary<string, object>
            {
                { "bannerAccent", bannerAccent }
            });
        }

        private static bool MatchesBanner(LeagueChallengePreferencesSnapshot snapshot, string expected)
        {
            return snapshot != null && snapshot.Connected &&
                   string.Equals(snapshot.BannerAccent ?? string.Empty, expected ?? string.Empty,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidBannerAccent(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            for (var index = 0; index < value.Length; index++)
                if (!char.IsDigit(value[index])) return false;
            return true;
        }

        private static string ReadScalarString(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return string.Empty;
            if (value is string) return ((string)value).Trim();
            if (value is int || value is long || value is decimal || value is double || value is float)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return string.Empty;
        }
    }
}
