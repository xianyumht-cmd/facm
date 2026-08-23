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
    }

    internal sealed class LeaguePresenceApplyResult
    {
        public string Status { get; set; }
        public LeaguePresenceMode Mode { get; set; }
        public LeaguePresenceSnapshot Observed { get; set; }
    }

    internal sealed class LeaguePresenceService
    {
        internal const string PresencePath = "/lol-chat/v1/me";
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

        internal string BuildPayloadForSmokeTest(byte[] currentPresence, LeaguePresenceMode mode)
        {
            var root = ParseRoot(currentPresence);
            if (root == null) return null;
            ApplyMode(root, mode);
            return _json.Serialize(root);
        }

        internal static bool MatchesForSmokeTest(LeaguePresenceSnapshot snapshot, LeaguePresenceMode mode)
        {
            return Matches(snapshot, mode);
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
                DisplayName = ReadString(root, "name")
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
