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
    internal sealed class LeagueRegaliaSnapshot
    {
        public bool Connected { get; set; }
        public string BannerType { get; set; }
        public string PreferredBannerType { get; set; }
        public string CrestType { get; set; }
        public string PreferredCrestType { get; set; }
        public int SelectedPrestigeCrest { get; set; }
        public int SummonerLevel { get; set; }
    }

    internal sealed class LeagueRegaliaApplyResult
    {
        public string Status { get; set; }
        public int SelectedPrestigeCrest { get; set; }
        public LeagueRegaliaSnapshot Observed { get; set; }
    }

    /// <summary>
    /// Explicit, user-directed summoner regalia customization. It reads the authoritative current
    /// regalia document, preserves the client's current banner type, performs one narrowly fenced
    /// write, then uses bounded first + settled readback. It never polls or rewrites in a loop.
    /// </summary>
    internal sealed class LeagueRegaliaCustomizationService
    {
        internal const string CurrentRegaliaPath = "/lol-regalia/v2/current-summoner/regalia";
        internal const int PrestigeCrestRemovalValue = 22;

        private readonly ILeagueClientApi _client;
        private readonly ILeagueRegaliaWriteApi _writer;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 };
        private readonly TimeSpan _firstVerificationDelay;
        private readonly TimeSpan _settleVerificationDelay;

        public LeagueRegaliaCustomizationService(ILeagueClientApi client, ILeagueRegaliaWriteApi writer)
            : this(client, writer, TimeSpan.FromMilliseconds(180), TimeSpan.FromMilliseconds(320))
        {
        }

        internal LeagueRegaliaCustomizationService(
            ILeagueClientApi client,
            ILeagueRegaliaWriteApi writer,
            TimeSpan firstVerificationDelay,
            TimeSpan settleVerificationDelay)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _firstVerificationDelay = firstVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : firstVerificationDelay;
            _settleVerificationDelay = settleVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : settleVerificationDelay;
        }

        public async Task<LeagueRegaliaSnapshot> ReadCurrentAsync(CancellationToken cancellationToken)
        {
            var bytes = await ReadWithTimeoutAsync(CurrentRegaliaPath, cancellationToken).ConfigureAwait(false);
            return Parse(bytes);
        }

        public async Task<LeagueRegaliaApplyResult> RemovePrestigeCrestAsync(CancellationToken cancellationToken)
        {
            var current = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (current == null || !current.Connected || string.IsNullOrWhiteSpace(current.BannerType))
            {
                AppLog.Info("League regalia read unavailable before prestige-crest change.");
                return new LeagueRegaliaApplyResult
                {
                    Status = "unavailable",
                    SelectedPrestigeCrest = PrestigeCrestRemovalValue,
                    Observed = current
                };
            }

            var payload = BuildRemovalPayload(current.BannerType);
            var response = await _writer.TrySetRegaliaAsync(payload, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                AppLog.Info("League regalia write rejected; status=" + (response == null ? 0 : response.StatusCode));
                return new LeagueRegaliaApplyResult
                {
                    Status = "write-failed",
                    SelectedPrestigeCrest = PrestigeCrestRemovalValue,
                    Observed = current
                };
            }

            if (_firstVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_firstVerificationDelay, cancellationToken).ConfigureAwait(false);
            var first = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (first == null || !first.Connected)
            {
                AppLog.Info("League regalia first readback unavailable.");
                return new LeagueRegaliaApplyResult
                {
                    Status = "unverified",
                    SelectedPrestigeCrest = PrestigeCrestRemovalValue,
                    Observed = first
                };
            }
            if (!MatchesRemoval(first, current.BannerType))
            {
                AppLog.Info("League regalia first readback did not match requested prestige-crest state.");
                return new LeagueRegaliaApplyResult
                {
                    Status = "overridden",
                    SelectedPrestigeCrest = PrestigeCrestRemovalValue,
                    Observed = first
                };
            }

            if (_settleVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_settleVerificationDelay, cancellationToken).ConfigureAwait(false);
            var settled = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (settled == null || !settled.Connected)
            {
                AppLog.Info("League regalia settled readback unavailable.");
                return new LeagueRegaliaApplyResult
                {
                    Status = "unverified",
                    SelectedPrestigeCrest = PrestigeCrestRemovalValue,
                    Observed = settled
                };
            }
            if (!MatchesRemoval(settled, current.BannerType))
            {
                AppLog.Info("League regalia prestige-crest state was overwritten by the League client.");
                return new LeagueRegaliaApplyResult
                {
                    Status = "overridden",
                    SelectedPrestigeCrest = PrestigeCrestRemovalValue,
                    Observed = settled
                };
            }

            AppLog.Info("League regalia prestige-crest change applied and verified.");
            return new LeagueRegaliaApplyResult
            {
                Status = "success",
                SelectedPrestigeCrest = PrestigeCrestRemovalValue,
                Observed = settled
            };
        }

        internal LeagueRegaliaSnapshot ParseForSmokeTest(byte[] bytes)
        {
            return Parse(bytes);
        }

        internal string BuildRemovalPayloadForSmokeTest(string currentBannerType)
        {
            return string.IsNullOrWhiteSpace(currentBannerType) ? null : BuildRemovalPayload(currentBannerType);
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

        private LeagueRegaliaSnapshot Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new LeagueRegaliaSnapshot();
            try
            {
                var root = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                if (root == null) return new LeagueRegaliaSnapshot();
                return new LeagueRegaliaSnapshot
                {
                    Connected = true,
                    BannerType = ReadString(root, "bannerType"),
                    PreferredBannerType = ReadString(root, "preferredBannerType"),
                    CrestType = ReadString(root, "crestType"),
                    PreferredCrestType = ReadString(root, "preferredCrestType"),
                    SelectedPrestigeCrest = ReadInt(root, "selectedPrestigeCrest"),
                    SummonerLevel = ReadInt(root, "summonerLevel")
                };
            }
            catch
            {
                return new LeagueRegaliaSnapshot();
            }
        }

        private string BuildRemovalPayload(string currentBannerType)
        {
            return _json.Serialize(new Dictionary<string, object>
            {
                { "preferredCrestType", "prestige" },
                { "preferredBannerType", currentBannerType },
                { "selectedPrestigeCrest", PrestigeCrestRemovalValue }
            });
        }

        private static bool MatchesRemoval(LeagueRegaliaSnapshot snapshot, string expectedBannerType)
        {
            return snapshot != null && snapshot.Connected &&
                   snapshot.SelectedPrestigeCrest == PrestigeCrestRemovalValue &&
                   string.Equals(snapshot.PreferredCrestType ?? string.Empty, "prestige", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(snapshot.PreferredBannerType ?? string.Empty, expectedBannerType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return 0;
            if (value is int) return (int)value;
            if (value is long) return unchecked((int)(long)value);
            if (value is decimal) return (int)(decimal)value;
            int parsed;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
        }
    }
}
