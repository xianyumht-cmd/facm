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
    internal enum LeaguePresenceMode
    {
        Online,
        Away,
        DoNotDisturb,
        Mobile,
        Offline,
        DisplayInGame
    }

    internal sealed class LeaguePresenceSnapshot
    {
        public bool Connected { get; set; }
        public string Availability { get; set; }
        public string GameStatus { get; set; }
        public string StatusMessage { get; set; }
        public string DisplayName { get; set; }
        public string RankedLeagueQueue { get; set; }
        public string RankedLeagueTier { get; set; }
        public string RankedLeagueDivision { get; set; }
    }

    internal sealed class LeaguePresenceApplyResult
    {
        public string Status { get; set; }
        public LeaguePresenceMode Mode { get; set; }
        public LeaguePresenceSnapshot Observed { get; set; }
    }

    internal sealed class LeaguePresenceStatusMessageApplyResult
    {
        public string Status { get; set; }
        public string StatusMessage { get; set; }
        public LeaguePresenceSnapshot Observed { get; set; }
    }

    internal sealed class LeaguePresenceRankedApplyResult
    {
        public string Status { get; set; }
        public string Queue { get; set; }
        public string Tier { get; set; }
        public string Division { get; set; }
        public LeaguePresenceSnapshot Observed { get; set; }
    }

    internal sealed class LeaguePresenceService
    {
        internal const string PresencePath = "/lol-chat/v1/me";
        internal const int MaximumStatusMessageLength = 512;

        private static readonly string[] AllowedRankedQueues =
        {
            "RANKED_SOLO_5x5",
            "RANKED_FLEX_SR",
            "RANKED_TFT",
            "RANKED_FLEX_TT",
            "CHERRY",
            "RANKED_TFT_TURBO",
            "RANKED_TFT_DOUBLE_UP"
        };

        private static readonly string[] AllowedRankedTiers =
        {
            "IRON",
            "BRONZE",
            "SILVER",
            "GOLD",
            "PLATINUM",
            "EMERALD",
            "DIAMOND",
            "MASTER",
            "GRANDMASTER",
            "CHALLENGER"
        };

        private static readonly string[] AllowedRankedDivisions = { "I", "II", "III", "IV" };

        private readonly ILeagueClientApi _client;
        private readonly ILeaguePresenceWriteApi _writer;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 512 * 1024 };
        private readonly TimeSpan _firstVerificationDelay;
        private readonly TimeSpan _settleVerificationDelay;

        public LeaguePresenceService(ILeagueClientApi client, ILeaguePresenceWriteApi writer)
            : this(client, writer, TimeSpan.FromMilliseconds(180), TimeSpan.FromMilliseconds(320))
        {
        }

        internal LeaguePresenceService(
            ILeagueClientApi client,
            ILeaguePresenceWriteApi writer,
            TimeSpan firstVerificationDelay,
            TimeSpan settleVerificationDelay)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _firstVerificationDelay = firstVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : firstVerificationDelay;
            _settleVerificationDelay = settleVerificationDelay < TimeSpan.Zero ? TimeSpan.Zero : settleVerificationDelay;
        }

        internal static IReadOnlyList<string> RankedQueues { get { return AllowedRankedQueues; } }
        internal static IReadOnlyList<string> RankedTiers { get { return AllowedRankedTiers; } }
        internal static IReadOnlyList<string> RankedDivisions { get { return AllowedRankedDivisions; } }

        public async Task<LeaguePresenceSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            var root = await ReadRootAsync(cancellationToken).ConfigureAwait(false);
            return root == null ? new LeaguePresenceSnapshot() : ToSnapshot(root);
        }

        public async Task<LeaguePresenceApplyResult> ApplyAsync(
            LeaguePresenceMode mode,
            CancellationToken cancellationToken)
        {
            var root = await ReadRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null)
            {
                return new LeaguePresenceApplyResult { Status = "unavailable", Mode = mode };
            }

            ApplyMode(root, mode);
            var payload = _json.Serialize(root);
            var response = await _writer.TrySetPresenceAsync(payload, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                AppLog.Info("League presence write rejected; mode=" + mode + "; status=" + (response == null ? 0 : response.StatusCode));
                return new LeaguePresenceApplyResult { Status = "write-failed", Mode = mode };
            }

            if (_firstVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_firstVerificationDelay, cancellationToken).ConfigureAwait(false);
            var first = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!Matches(first, mode))
            {
                AppLog.Info("League presence readback did not match; mode=" + mode + "; stage=first");
                return new LeaguePresenceApplyResult { Status = "overridden", Mode = mode, Observed = first };
            }

            if (_settleVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_settleVerificationDelay, cancellationToken).ConfigureAwait(false);
            var settled = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!Matches(settled, mode))
            {
                AppLog.Info("League presence readback was overwritten by client; mode=" + mode + "; stage=settled");
                return new LeaguePresenceApplyResult { Status = "overridden", Mode = mode, Observed = settled };
            }

            AppLog.Info("League presence applied and verified; mode=" + mode);
            return new LeaguePresenceApplyResult { Status = "success", Mode = mode, Observed = settled };
        }

        public async Task<LeaguePresenceStatusMessageApplyResult> ApplyStatusMessageAsync(
            string statusMessage,
            CancellationToken cancellationToken)
        {
            var normalized = NormalizeStatusMessage(statusMessage);
            var root = await ReadRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null)
            {
                return new LeaguePresenceStatusMessageApplyResult
                {
                    Status = "unavailable",
                    StatusMessage = normalized
                };
            }

            root["statusMessage"] = normalized;
            var payload = _json.Serialize(root);
            var response = await _writer.TrySetPresenceAsync(payload, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                AppLog.Info("League chat signature write rejected; status=" + (response == null ? 0 : response.StatusCode));
                return new LeaguePresenceStatusMessageApplyResult
                {
                    Status = "write-failed",
                    StatusMessage = normalized
                };
            }

            if (_firstVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_firstVerificationDelay, cancellationToken).ConfigureAwait(false);
            var first = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!StatusMessageMatches(first, normalized))
            {
                AppLog.Info("League chat signature readback did not match; stage=first");
                return new LeaguePresenceStatusMessageApplyResult
                {
                    Status = "overridden",
                    StatusMessage = normalized,
                    Observed = first
                };
            }

            if (_settleVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_settleVerificationDelay, cancellationToken).ConfigureAwait(false);
            var settled = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!StatusMessageMatches(settled, normalized))
            {
                AppLog.Info("League chat signature readback was overwritten by client; stage=settled");
                return new LeaguePresenceStatusMessageApplyResult
                {
                    Status = "overridden",
                    StatusMessage = normalized,
                    Observed = settled
                };
            }

            AppLog.Info("League chat signature applied and verified.");
            return new LeaguePresenceStatusMessageApplyResult
            {
                Status = "success",
                StatusMessage = normalized,
                Observed = settled
            };
        }

        public async Task<LeaguePresenceRankedApplyResult> ApplyRankedStatusAsync(
            string queue,
            string tier,
            string division,
            CancellationToken cancellationToken)
        {
            string normalizedQueue;
            string normalizedTier;
            string normalizedDivision;
            if (!TryNormalizeRankedStatus(queue, tier, division, out normalizedQueue, out normalizedTier, out normalizedDivision))
            {
                return new LeaguePresenceRankedApplyResult
                {
                    Status = "invalid",
                    Queue = NormalizeRankedToken(queue),
                    Tier = NormalizeRankedToken(tier),
                    Division = NormalizeRankedToken(division)
                };
            }

            var root = await ReadRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null)
            {
                return new LeaguePresenceRankedApplyResult
                {
                    Status = "unavailable",
                    Queue = normalizedQueue,
                    Tier = normalizedTier,
                    Division = normalizedDivision
                };
            }

            ApplyRankedStatus(root, normalizedQueue, normalizedTier, normalizedDivision);
            var payload = _json.Serialize(root);
            var response = await _writer.TrySetPresenceAsync(payload, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                AppLog.Info("League displayed-rank write rejected; status=" + (response == null ? 0 : response.StatusCode));
                return new LeaguePresenceRankedApplyResult
                {
                    Status = "write-failed",
                    Queue = normalizedQueue,
                    Tier = normalizedTier,
                    Division = normalizedDivision
                };
            }

            if (_firstVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_firstVerificationDelay, cancellationToken).ConfigureAwait(false);
            var first = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!RankedStatusMatches(first, normalizedQueue, normalizedTier, normalizedDivision))
            {
                AppLog.Info("League displayed-rank readback did not match; stage=first");
                return new LeaguePresenceRankedApplyResult
                {
                    Status = "overridden",
                    Queue = normalizedQueue,
                    Tier = normalizedTier,
                    Division = normalizedDivision,
                    Observed = first
                };
            }

            if (_settleVerificationDelay > TimeSpan.Zero)
                await Task.Delay(_settleVerificationDelay, cancellationToken).ConfigureAwait(false);
            var settled = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!RankedStatusMatches(settled, normalizedQueue, normalizedTier, normalizedDivision))
            {
                AppLog.Info("League displayed-rank readback was overwritten by client; stage=settled");
                return new LeaguePresenceRankedApplyResult
                {
                    Status = "overridden",
                    Queue = normalizedQueue,
                    Tier = normalizedTier,
                    Division = normalizedDivision,
                    Observed = settled
                };
            }

            AppLog.Info("League displayed rank applied and verified; queue=" + normalizedQueue + "; tier=" + normalizedTier);
            return new LeaguePresenceRankedApplyResult
            {
                Status = "success",
                Queue = normalizedQueue,
                Tier = normalizedTier,
                Division = normalizedDivision,
                Observed = settled
            };
        }

        internal string BuildPayloadForSmokeTest(byte[] currentPresence, LeaguePresenceMode mode)
        {
            var root = ParseRoot(currentPresence);
            if (root == null) return null;
            ApplyMode(root, mode);
            return _json.Serialize(root);
        }

        internal string BuildStatusMessagePayloadForSmokeTest(byte[] currentPresence, string statusMessage)
        {
            var root = ParseRoot(currentPresence);
            if (root == null) return null;
            root["statusMessage"] = NormalizeStatusMessage(statusMessage);
            return _json.Serialize(root);
        }

        internal string BuildRankedStatusPayloadForSmokeTest(byte[] currentPresence, string queue, string tier, string division)
        {
            string normalizedQueue;
            string normalizedTier;
            string normalizedDivision;
            if (!TryNormalizeRankedStatus(queue, tier, division, out normalizedQueue, out normalizedTier, out normalizedDivision))
                return null;
            var root = ParseRoot(currentPresence);
            if (root == null) return null;
            ApplyRankedStatus(root, normalizedQueue, normalizedTier, normalizedDivision);
            return _json.Serialize(root);
        }

        internal static bool MatchesForSmokeTest(LeaguePresenceSnapshot snapshot, LeaguePresenceMode mode)
        {
            return Matches(snapshot, mode);
        }

        internal static string NormalizeStatusMessageForSmokeTest(string value)
        {
            return NormalizeStatusMessage(value);
        }

        internal static bool TryNormalizeRankedStatusForSmokeTest(
            string queue,
            string tier,
            string division,
            out string normalizedQueue,
            out string normalizedTier,
            out string normalizedDivision)
        {
            return TryNormalizeRankedStatus(queue, tier, division, out normalizedQueue, out normalizedTier, out normalizedDivision);
        }

        private async Task<Dictionary<string, object>> ReadRootAsync(CancellationToken cancellationToken)
        {
            var bytes = await _client.TryGetBytesAsync(PresencePath, cancellationToken).ConfigureAwait(false);
            return ParseRoot(bytes);
        }

        private Dictionary<string, object> ParseRoot(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static void ApplyMode(Dictionary<string, object> root, LeaguePresenceMode mode)
        {
            if (root == null) return;
            var currentAvailability = ReadString(root, "availability");
            switch (mode)
            {
                case LeaguePresenceMode.Online:
                    root["availability"] = string.Equals(currentAvailability, "online", StringComparison.OrdinalIgnoreCase)
                        ? "online"
                        : "chat";
                    SetGameStatus(root, "outOfGame");
                    break;
                case LeaguePresenceMode.Away:
                    root["availability"] = "away";
                    SetGameStatus(root, "outOfGame");
                    break;
                case LeaguePresenceMode.DoNotDisturb:
                    root["availability"] = "dnd";
                    SetGameStatus(root, "outOfGame");
                    break;
                case LeaguePresenceMode.Mobile:
                    root["availability"] = "mobile";
                    SetGameStatus(root, "outOfGame");
                    break;
                case LeaguePresenceMode.Offline:
                    root["availability"] = "offline";
                    SetGameStatus(root, "outOfGame");
                    break;
                case LeaguePresenceMode.DisplayInGame:
                    root["availability"] = "dnd";
                    SetGameStatus(root, "inGame");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static void ApplyRankedStatus(
            Dictionary<string, object> root,
            string queue,
            string tier,
            string division)
        {
            if (root == null) return;
            object lolValue;
            var lol = root.TryGetValue("lol", out lolValue) ? lolValue as Dictionary<string, object> : null;
            if (lol == null)
            {
                lol = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                root["lol"] = lol;
            }

            lol["rankedLeagueQueue"] = queue;
            lol["rankedLeagueTier"] = tier;
            if (IsApexTier(tier))
                lol.Remove("rankedLeagueDivision");
            else
                lol["rankedLeagueDivision"] = division;
        }

        private static string NormalizeStatusMessage(string value)
        {
            var normalized = (value ?? string.Empty).Replace("\0", string.Empty);
            if (normalized.Length > MaximumStatusMessageLength)
                normalized = normalized.Substring(0, MaximumStatusMessageLength);
            return normalized;
        }

        private static string NormalizeRankedToken(string value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string NormalizeRankedQueue(string value)
        {
            var candidate = (value ?? string.Empty).Trim();
            for (var index = 0; index < AllowedRankedQueues.Length; index++)
            {
                if (string.Equals(AllowedRankedQueues[index], candidate, StringComparison.OrdinalIgnoreCase))
                    return AllowedRankedQueues[index];
            }
            return string.Empty;
        }

        private static bool TryNormalizeRankedStatus(
            string queue,
            string tier,
            string division,
            out string normalizedQueue,
            out string normalizedTier,
            out string normalizedDivision)
        {
            normalizedQueue = NormalizeRankedQueue(queue);
            normalizedTier = NormalizeRankedToken(tier);
            normalizedDivision = NormalizeRankedToken(division);

            if (string.IsNullOrEmpty(normalizedQueue) ||
                Array.IndexOf(AllowedRankedTiers, normalizedTier) < 0)
                return false;

            if (IsApexTier(normalizedTier))
            {
                normalizedDivision = string.Empty;
                return true;
            }

            return Array.IndexOf(AllowedRankedDivisions, normalizedDivision) >= 0;
        }

        private static bool IsApexTier(string tier)
        {
            return string.Equals(tier, "MASTER", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tier, "GRANDMASTER", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tier, "CHALLENGER", StringComparison.OrdinalIgnoreCase);
        }

        private static bool StatusMessageMatches(LeaguePresenceSnapshot snapshot, string expected)
        {
            return snapshot != null && snapshot.Connected &&
                   string.Equals(snapshot.StatusMessage ?? string.Empty, expected ?? string.Empty, StringComparison.Ordinal);
        }

        private static bool RankedStatusMatches(
            LeaguePresenceSnapshot snapshot,
            string queue,
            string tier,
            string division)
        {
            if (snapshot == null || !snapshot.Connected) return false;
            if (!string.Equals(snapshot.RankedLeagueQueue ?? string.Empty, queue ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(snapshot.RankedLeagueTier ?? string.Empty, tier ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return false;
            return IsApexTier(tier) ||
                   string.Equals(snapshot.RankedLeagueDivision ?? string.Empty, division ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static void SetGameStatus(Dictionary<string, object> root, string gameStatus)
        {
            object lolValue;
            var lol = root.TryGetValue("lol", out lolValue) ? lolValue as Dictionary<string, object> : null;
            if (lol == null)
            {
                lol = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                root["lol"] = lol;
            }
            lol["gameStatus"] = gameStatus;
        }

        private static LeaguePresenceSnapshot ToSnapshot(Dictionary<string, object> root)
        {
            object lolValue;
            var lol = root != null && root.TryGetValue("lol", out lolValue) ? lolValue as Dictionary<string, object> : null;
            return new LeaguePresenceSnapshot
            {
                Connected = root != null,
                Availability = ReadString(root, "availability"),
                GameStatus = ReadString(lol, "gameStatus"),
                StatusMessage = ReadString(root, "statusMessage"),
                DisplayName = ReadString(root, "name"),
                RankedLeagueQueue = ReadString(lol, "rankedLeagueQueue"),
                RankedLeagueTier = ReadString(lol, "rankedLeagueTier"),
                RankedLeagueDivision = ReadString(lol, "rankedLeagueDivision")
            };
        }

        private static bool Matches(LeaguePresenceSnapshot snapshot, LeaguePresenceMode mode)
        {
            if (snapshot == null || !snapshot.Connected) return false;
            var availability = (snapshot.Availability ?? string.Empty).Trim();
            var gameStatus = (snapshot.GameStatus ?? string.Empty).Trim();
            switch (mode)
            {
                case LeaguePresenceMode.Online:
                    return string.Equals(availability, "chat", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(availability, "online", StringComparison.OrdinalIgnoreCase);
                case LeaguePresenceMode.Away:
                    return string.Equals(availability, "away", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(gameStatus, "inGame", StringComparison.OrdinalIgnoreCase);
                case LeaguePresenceMode.DoNotDisturb:
                    return string.Equals(availability, "dnd", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(gameStatus, "inGame", StringComparison.OrdinalIgnoreCase);
                case LeaguePresenceMode.Mobile:
                    return string.Equals(availability, "mobile", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(gameStatus, "inGame", StringComparison.OrdinalIgnoreCase);
                case LeaguePresenceMode.Offline:
                    return string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(gameStatus, "inGame", StringComparison.OrdinalIgnoreCase);
                case LeaguePresenceMode.DisplayInGame:
                    return string.Equals(gameStatus, "inGame", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : string.Empty;
        }
    }
}
