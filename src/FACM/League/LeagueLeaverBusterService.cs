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
    internal sealed class LeagueLeaverBusterEntry
    {
        public string Type { get; set; }
        public string Reason { get; set; }
        public double PenaltySecondsRemaining { get; set; }
        public double PenaltySeconds { get; set; }
    }

    internal sealed class LeagueLeaverBusterStatus
    {
        public static readonly LeagueLeaverBusterStatus Unknown =
            new LeagueLeaverBusterStatus { Available = false };

        public bool Available { get; set; }
        public bool HasPenalty { get; set; }
        public double MaxSecondsRemaining { get; set; }
        public IReadOnlyList<LeagueLeaverBusterEntry> Entries { get; set; }

        public string Summary
        {
            get
            {
                if (!Available) return "排队限制状态未知";
                if (!HasPenalty) return "未报告排队限制";
                var total = (long)Math.Ceiling(MaxSecondsRemaining);
                return total > 0
                    ? "排队限制：还需等待 " + total / 60 + " 分 " + total % 60 + " 秒"
                    : "客户端报告排队限制，剩余时间未知";
            }
        }
    }

    /// <summary>
    /// Reads reported matchmaking restrictions. Notification absence does not prove eligibility.
    /// </summary>
    internal sealed class LeagueLeaverBusterService
    {
        internal const string SearchPath = LeagueQueueStatusService.SearchPath;

        private readonly ILeagueClientApi _reader;

        public LeagueLeaverBusterService(ILeagueClientApi reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<LeagueLeaverBusterStatus> ReadAsync(CancellationToken cancellationToken)
        {
            var bytes = await _reader.TryGetBytesAsync(SearchPath, cancellationToken).ConfigureAwait(false);
            return ParseSearch(bytes);
        }

        internal static LeagueLeaverBusterStatus ParseSearch(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return LeagueLeaverBusterStatus.Unknown;

            object raw;
            try
            {
                raw = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes));
            }
            catch (ArgumentException) { return LeagueLeaverBusterStatus.Unknown; }
            catch (InvalidOperationException) { return LeagueLeaverBusterStatus.Unknown; }

            var root = raw as Dictionary<string, object>;
            object errors;
            var items = root != null && root.TryGetValue("errors", out errors) ? errors as object[] : null;
            if (items == null) return LeagueLeaverBusterStatus.Unknown;

            var entries = new List<LeagueLeaverBusterEntry>();
            double maxRemaining = 0;
            foreach (var item in items)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null || string.IsNullOrWhiteSpace(ReadString(entry, "errorType")))
                    return LeagueLeaverBusterStatus.Unknown;

                var parsed = new LeagueLeaverBusterEntry
                {
                    Type = ReadString(entry, "errorType"),
                    Reason = ReadString(entry, "reason"),
                    PenaltySecondsRemaining = ReadDouble(entry, "penaltyTimeRemaining"),
                    PenaltySeconds = ReadDouble(entry, "penaltyTime")
                };
                if (parsed.PenaltySecondsRemaining > maxRemaining) maxRemaining = parsed.PenaltySecondsRemaining;
                entries.Add(parsed);
            }

            var status = new LeagueLeaverBusterStatus
            {
                Available = true,
                HasPenalty = entries.Count > 0,
                MaxSecondsRemaining = maxRemaining,
                Entries = entries
            };
            AppLog.Info("Leaver buster status: entries=" + entries.Count +
                        "; maxRemaining=" + maxRemaining.ToString(CultureInfo.InvariantCulture));
            return status;
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, CultureInfo.InvariantCulture)
                : null;
        }

        private static double ReadDouble(Dictionary<string, object> value, string key)
        {
            object entry;
            if (value == null || !value.TryGetValue(key, out entry) || entry == null) return 0;
            double parsed;
            return double.TryParse(Convert.ToString(entry, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) &&
                !double.IsNaN(parsed) && !double.IsInfinity(parsed) && parsed >= 0 && parsed <= int.MaxValue ? parsed : 0;
        }
    }
}
