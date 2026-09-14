using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        public string Title { get; set; }
        public string CrestBorder { get; set; }
        public int PrestigeCrestBorderLevel { get; set; }
        public List<long> ChallengeIds { get; set; }
        public Dictionary<string, object> SignedJwtPayload { get; set; }
        public bool HasTitleEvidence { get; set; }
        public bool HasCrestBorderEvidence { get; set; }
        public bool HasPrestigeCrestBorderLevelEvidence { get; set; }
        public bool HasChallengeIdsEvidence { get; set; }

        public bool CanPreservePreferences
        {
            get
            {
                return Connected &&
                       HasTitleEvidence &&
                       HasCrestBorderEvidence &&
                       HasPrestigeCrestBorderLevelEvidence &&
                       HasChallengeIdsEvidence;
            }
        }
    }

    internal sealed class LeagueChallengePreferencesApplyResult
    {
        public string Status { get; set; }
        public string RequestedBannerAccent { get; set; }
        public LeagueChallengePreferencesSnapshot Observed { get; set; }
    }

    /// <summary>
    /// Explicit, user-directed profile challenge-preference customization. Riot's
    /// update-player-preferences route is treated as a replacement-style write: FACM reads the
    /// local summary first, preserves title/challenge/crest preference fields, changes only
    /// bannerAccent, then performs bounded first + settled verification. It never polls or rewrites
    /// in a loop, and it fails closed when the current preference state cannot be reconstructed.
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
            if (current == null || !current.CanPreservePreferences)
            {
                AppLog.Info("League challenge preferences could not be reconstructed safely before banner-accent change.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "unavailable",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = current
                };
            }

            var payload = BuildBannerPayload(current, LastSeasonBannerAccent);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "unavailable",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = current
                };
            }

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
            if (!CanVerify(first))
            {
                AppLog.Info("League banner-accent first readback unavailable or incomplete.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "unverified",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = first
                };
            }
            if (!MatchesBanner(first, LastSeasonBannerAccent) || !MatchesPreservedPreferences(current, first))
            {
                AppLog.Info("League banner-accent first readback did not preserve the requested profile preference state.");
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
            if (!CanVerify(settled))
            {
                AppLog.Info("League banner-accent settled readback unavailable or incomplete.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "unverified",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = settled
                };
            }
            if (!MatchesBanner(settled, LastSeasonBannerAccent) || !MatchesPreservedPreferences(current, settled))
            {
                AppLog.Info("League banner-accent or preserved challenge preferences were overwritten by the League client.");
                return new LeagueChallengePreferencesApplyResult
                {
                    Status = "overridden",
                    RequestedBannerAccent = LastSeasonBannerAccent,
                    Observed = settled
                };
            }

            AppLog.Info("League last-season banner accent applied and verified with challenge preferences preserved.");
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

        internal string BuildBannerPayloadForSmokeTest(LeagueChallengePreferencesSnapshot current, string bannerAccent)
        {
            return BuildBannerPayload(current, bannerAccent);
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

                var preferences = ReadDictionary(root, "preferences");

                var bannerAccent = FirstNonEmpty(
                    ReadScalarString(root, "bannerAccent"),
                    ReadScalarString(root, "bannerId"),
                    ReadScalarString(preferences, "bannerAccent"),
                    ReadScalarString(preferences, "bannerId"));

                string title;
                var hasTitle = TryReadTitle(root, out title) || TryReadTitle(preferences, out title);
                if (title == "-1") title = string.Empty;

                string crestBorder;
                var hasCrest = TryReadFirstScalar(root, out crestBorder, "crestBorder", "crestId") ||
                               TryReadFirstScalar(preferences, out crestBorder, "crestBorder", "crestId");

                int prestigeLevel;
                var hasPrestige = TryReadInt(root, "prestigeCrestBorderLevel", out prestigeLevel) ||
                                  TryReadInt(preferences, "prestigeCrestBorderLevel", out prestigeLevel);

                List<long> challengeIds;
                var hasChallengeIds = TryReadChallengeIds(root, out challengeIds) ||
                                      TryReadChallengeIds(preferences, out challengeIds);

                var signedJwtPayload = ReadDictionary(root, "signedJWTPayload") ??
                                       ReadDictionary(preferences, "signedJWTPayload");

                return new LeagueChallengePreferencesSnapshot
                {
                    Connected = true,
                    BannerAccent = bannerAccent,
                    Title = title ?? string.Empty,
                    CrestBorder = crestBorder ?? string.Empty,
                    PrestigeCrestBorderLevel = prestigeLevel,
                    ChallengeIds = challengeIds ?? new List<long>(),
                    SignedJwtPayload = signedJwtPayload,
                    HasTitleEvidence = hasTitle,
                    HasCrestBorderEvidence = hasCrest,
                    HasPrestigeCrestBorderLevelEvidence = hasPrestige,
                    HasChallengeIdsEvidence = hasChallengeIds
                };
            }
            catch
            {
                return new LeagueChallengePreferencesSnapshot();
            }
        }

        private string BuildBannerPayload(LeagueChallengePreferencesSnapshot current, string bannerAccent)
        {
            if (current == null || !current.CanPreservePreferences || !IsValidBannerAccent(bannerAccent)) return null;

            var payload = new Dictionary<string, object>
            {
                { "bannerAccent", bannerAccent },
                { "title", current.Title ?? string.Empty },
                { "challengeIds", current.ChallengeIds == null ? new long[0] : current.ChallengeIds.ToArray() },
                { "crestBorder", current.CrestBorder ?? string.Empty },
                { "prestigeCrestBorderLevel", current.PrestigeCrestBorderLevel }
            };
            if (current.SignedJwtPayload != null)
                payload["signedJWTPayload"] = current.SignedJwtPayload;

            return _json.Serialize(payload);
        }

        private static bool CanVerify(LeagueChallengePreferencesSnapshot snapshot)
        {
            return snapshot != null && snapshot.CanPreservePreferences &&
                   !string.IsNullOrWhiteSpace(snapshot.BannerAccent);
        }

        private static bool MatchesBanner(LeagueChallengePreferencesSnapshot snapshot, string expected)
        {
            return snapshot != null && snapshot.Connected &&
                   string.Equals(snapshot.BannerAccent ?? string.Empty, expected ?? string.Empty,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesPreservedPreferences(
            LeagueChallengePreferencesSnapshot expected,
            LeagueChallengePreferencesSnapshot observed)
        {
            if (expected == null || observed == null || !expected.CanPreservePreferences || !observed.CanPreservePreferences)
                return false;

            return string.Equals(expected.Title ?? string.Empty, observed.Title ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(expected.CrestBorder ?? string.Empty, observed.CrestBorder ?? string.Empty, StringComparison.Ordinal) &&
                   expected.PrestigeCrestBorderLevel == observed.PrestigeCrestBorderLevel &&
                   (expected.ChallengeIds ?? new List<long>()).SequenceEqual(observed.ChallengeIds ?? new List<long>());
        }

        private static bool IsValidBannerAccent(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            for (var index = 0; index < value.Length; index++)
                if (!char.IsDigit(value[index])) return false;
            return true;
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? value as Dictionary<string, object>
                : null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            for (var index = 0; index < values.Length; index++)
                if (!string.IsNullOrWhiteSpace(values[index])) return values[index];
            return string.Empty;
        }

        private static bool TryReadTitle(Dictionary<string, object> source, out string title)
        {
            title = string.Empty;
            object value;
            if (source == null || !source.TryGetValue("title", out value) || value == null) return false;

            var titleObject = value as Dictionary<string, object>;
            if (titleObject != null)
            {
                object itemId;
                if (!titleObject.TryGetValue("itemId", out itemId) || itemId == null) return false;
                title = ConvertScalarToString(itemId);
                return title != null;
            }

            title = ConvertScalarToString(value);
            return title != null;
        }

        private static bool TryReadFirstScalar(
            Dictionary<string, object> source,
            out string result,
            params string[] keys)
        {
            result = string.Empty;
            if (source == null || keys == null) return false;
            for (var index = 0; index < keys.Length; index++)
            {
                object value;
                if (!source.TryGetValue(keys[index], out value) || value == null) continue;
                var parsed = ConvertScalarToString(value);
                if (parsed == null) continue;
                result = parsed;
                return true;
            }
            return false;
        }

        private static bool TryReadInt(Dictionary<string, object> source, string key, out int result)
        {
            result = 0;
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return false;
            try
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadChallengeIds(Dictionary<string, object> source, out List<long> ids)
        {
            ids = new List<long>();
            if (source == null) return false;

            object value;
            if (source.TryGetValue("challengeIds", out value) && TryReadIdArray(value, out ids)) return true;
            if (source.TryGetValue("selectedChallengeIds", out value) && TryReadIdArray(value, out ids)) return true;

            if (source.TryGetValue("topChallenges", out value))
            {
                var enumerable = value as IEnumerable;
                if (enumerable != null && !(value is string))
                {
                    ids = new List<long>();
                    foreach (var item in enumerable)
                    {
                        var challenge = item as Dictionary<string, object>;
                        long id;
                        if (challenge != null && TryReadLong(challenge, "id", out id) && id > 0 && ids.Count < 3)
                            ids.Add(id);
                    }
                    return true;
                }
            }

            if (source.TryGetValue("selectedChallengesString", out value) && value != null)
            {
                ids = new List<long>();
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                var parts = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (var index = 0; index < parts.Length && ids.Count < 3; index++)
                {
                    long id;
                    if (long.TryParse(parts[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0)
                        ids.Add(id);
                }
                return true;
            }

            return false;
        }

        private static bool TryReadIdArray(object value, out List<long> ids)
        {
            ids = new List<long>();
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) return false;
            foreach (var item in enumerable)
            {
                long id;
                if (!TryConvertLong(item, out id)) return false;
                if (id > 0 && ids.Count < 3) ids.Add(id);
            }
            return true;
        }

        private static bool TryReadLong(Dictionary<string, object> source, string key, out long result)
        {
            result = 0;
            object value;
            return source != null && source.TryGetValue(key, out value) && TryConvertLong(value, out result);
        }

        private static bool TryConvertLong(object value, out long result)
        {
            result = 0;
            if (value == null) return false;
            try
            {
                result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadScalarString(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return string.Empty;
            return ConvertScalarToString(value) ?? string.Empty;
        }

        private static string ConvertScalarToString(object value)
        {
            if (value is string) return ((string)value).Trim();
            if (value is int || value is long || value is decimal || value is double || value is float || value is short || value is byte)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            return null;
        }
    }
}
