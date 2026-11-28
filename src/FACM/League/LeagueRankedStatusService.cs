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
    internal sealed class LeagueRankedQueueLine
    {
        public string QueueType { get; set; }
        public string Tier { get; set; }
        public string Division { get; set; }
        public int LeaguePoints { get; set; }
        public bool LeaguePointsKnown { get; set; }
        public bool RecordKnown { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }

        public string Display
        {
            get
            {
                if (string.IsNullOrEmpty(Tier)) return QueueType + " 未定级";
                return QueueType + " " + Tier + " " + Division +
                    (LeaguePointsKnown ? " " + LeaguePoints + " LP" : " · 胜点未知") +
                    (RecordKnown ? " (" + Wins + "胜" + Losses + "负)" : string.Empty);
            }
        }
    }

    internal sealed class LeagueRankedSnapshot
    {
        public static readonly LeagueRankedSnapshot Unavailable = new LeagueRankedSnapshot { Available = false };

        public bool Available { get; set; }
        public IReadOnlyList<LeagueRankedQueueLine> Queues { get; set; }
    }

    internal sealed class LeagueLpChangeNotice
    {
        public static readonly LeagueLpChangeNotice None = new LeagueLpChangeNotice();

        public bool Available { get; set; }
        public int LeaguePointsDelta { get; set; }
        public string QueueType { get; set; }
        public string Tier { get; set; }
        public string Division { get; set; }
        public bool Promoted { get; set; }
        public bool Demoted { get; set; }

        public string Summary
        {
            get
            {
                if (!Available) return "无 LP 变化";
                var sign = LeaguePointsDelta >= 0 ? "+" : string.Empty;
                var text = QueueType + " " + sign + LeaguePointsDelta + " LP";
                if (Promoted) text += " · 晋级";
                else if (Demoted) text += " · 降级";
                return text;
            }
        }
    }

    /// <summary>
    /// Reads ranked standings from the local client and polls the LP-change notice that the stock
    /// client only flashes for a moment after a game.
    /// </summary>
    internal sealed class LeagueRankedStatusService
    {
        internal const string StatsPath = "/lol-ranked/v1/current-ranked-stats";
        internal const string LpChangePath = "/lol-ranked/v1/current-lp-change-notification";

        private readonly ILeagueClientApi _reader;

        public LeagueRankedStatusService(ILeagueClientApi reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<LeagueRankedSnapshot> ReadRankedAsync(CancellationToken cancellationToken)
        {
            var bytes = await _reader.TryGetBytesAsync(StatsPath, cancellationToken).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return LeagueRankedSnapshot.Unavailable;

            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes))
                    as Dictionary<string, object>;
            }
            catch (ArgumentException) { return LeagueRankedSnapshot.Unavailable; }
            catch (InvalidOperationException) { return LeagueRankedSnapshot.Unavailable; }
            if (root == null) return LeagueRankedSnapshot.Unavailable;

            var queues = new List<LeagueRankedQueueLine>();
            object queuesRaw;
            var queueMap = root.TryGetValue("queueMap", out queuesRaw)
                ? queuesRaw as Dictionary<string, object> : null;
            if (queueMap == null) return LeagueRankedSnapshot.Unavailable;
            {
                foreach (var pair in queueMap)
                {
                    var entry = pair.Value as Dictionary<string, object>;
                    if (entry == null) return LeagueRankedSnapshot.Unavailable;
                    int lp, wins, losses;
                    var lpKnown = TryReadNonnegativeInt(entry, "leaguePoints", out lp);
                    var winsKnown = TryReadNonnegativeInt(entry, "wins", out wins);
                    var lossesKnown = TryReadNonnegativeInt(entry, "losses", out losses);
                    queues.Add(new LeagueRankedQueueLine
                    {
                        QueueType = pair.Key,
                        Tier = ReadString(entry, "tier"),
                        Division = ReadString(entry, "division"),
                        LeaguePoints = lp,
                        LeaguePointsKnown = lpKnown,
                        RecordKnown = winsKnown && lossesKnown,
                        Wins = wins,
                        Losses = losses
                    });
                }
            }

            return new LeagueRankedSnapshot { Available = true, Queues = queues };
        }

        public async Task<LeagueLpChangeNotice> ReadLpChangeAsync(CancellationToken cancellationToken)
        {
            var bytes = await _reader.TryGetBytesAsync(LpChangePath, cancellationToken).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return LeagueLpChangeNotice.None;

            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes))
                    as Dictionary<string, object>;
            }
            catch (ArgumentException) { return LeagueLpChangeNotice.None; }
            catch (InvalidOperationException) { return LeagueLpChangeNotice.None; }
            if (root == null) return LeagueLpChangeNotice.None;

            var notice = new LeagueLpChangeNotice
            {
                Available = true,
                LeaguePointsDelta = (int)ReadDouble(root, "leaguePointsDelta"),
                QueueType = ReadString(root, "queueType"),
                Tier = ReadString(root, "tier"),
                Division = ReadString(root, "division"),
                Promoted = ReadBool(root, "promoted"),
                Demoted = ReadBool(root, "demoted")
            };
            AppLog.Info("LP change: " + notice.Summary);
            return notice;
        }

        private static bool TryReadNonnegativeInt(Dictionary<string, object> value, string key, out int result)
        {
            return int.TryParse(ReadString(value, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= 0;
        }

        private static bool ReadBool(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry is bool && (bool)entry;
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, CultureInfo.InvariantCulture) : null;
        }

        private static double ReadDouble(Dictionary<string, object> value, string key)
        {
            object entry;
            if (value == null || !value.TryGetValue(key, out entry) || entry == null) return 0;
            double parsed;
            return double.TryParse(Convert.ToString(entry, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }
    }
}
