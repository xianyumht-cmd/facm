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
                if (!Available) return "裁决之镰状态未知";
                if (!HasPenalty || Entries == null || Entries.Count == 0) return "无裁决之镰处罚";
                var total = (int)Math.Ceiling(MaxSecondsRemaining);
                return total > 0
                    ? "裁决之镰：还需等待 " + total / 60 + " 分 " + total % 60 + " 秒"
                    : "裁决之镰：处罚已到期，可重新排队";
            }
        }
    }

    /// <summary>
    /// Reads the leaver-buster penalty list. The ranked-restriction route reports only ranked
    /// bans, so a live dodge penalty shows up here and nowhere else.
    /// </summary>
    internal sealed class LeagueLeaverBusterService
    {
        internal const string NotificationsPath = "/lol-leaver-buster/v1/notifications";

        private readonly ILeagueClientApi _reader;

        public LeagueLeaverBusterService(ILeagueClientApi reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<LeagueLeaverBusterStatus> ReadAsync(CancellationToken cancellationToken)
        {
            var bytes = await _reader.TryGetBytesAsync(NotificationsPath, cancellationToken).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return LeagueLeaverBusterStatus.Unknown;

            object raw;
            try
            {
                raw = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes));
            }
            catch (ArgumentException) { return LeagueLeaverBusterStatus.Unknown; }
            catch (InvalidOperationException) { return LeagueLeaverBusterStatus.Unknown; }

            var items = raw as object[];
            if (items == null) return LeagueLeaverBusterStatus.Unknown;

            var entries = new List<LeagueLeaverBusterEntry>();
            double maxRemaining = 0;
            foreach (var item in items)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null) continue;

                var parsed = new LeagueLeaverBusterEntry
                {
                    Type = ReadString(entry, "type"),
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
                        "; maxRemaining=" + ((int)maxRemaining).ToString(CultureInfo.InvariantCulture));
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
                NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }
    }
}
