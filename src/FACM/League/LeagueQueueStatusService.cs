using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueQueueStatus
    {
        public static readonly LeagueQueueStatus Unavailable = new LeagueQueueStatus { Available = false };

        public bool Available { get; set; }
        public bool InQueue { get; set; }
        public bool ElapsedKnown { get; set; }
        public bool EstimateKnown { get; set; }
        public double EstimatedSeconds { get; set; }
        public double SecondsInQueue { get; set; }
        public int QueueId { get; set; }
        public double PenaltySecondsRemaining { get; set; }
        public string SearchState { get; set; }

        public string Summary
        {
            get
            {
                if (!Available) return "队列状态不可用";
                if (!InQueue) return "未在排队";
                var queued = (int)Math.Round(SecondsInQueue);
                var estimate = (int)Math.Round(EstimatedSeconds);
                var text = ElapsedKnown ? "排队中 " + queued / 60 + ":" + (queued % 60).ToString("00", CultureInfo.InvariantCulture) : "排队中";
                text += EstimateKnown && estimate > 0 ? " · 预计 " + estimate + " 秒" : " · 预计时间未知";
                return text;
            }
        }
    }

    /// <summary>
    /// Reads the live matchmaking search document. The stock client hides the estimate and the
    /// low-priority penalty behind a vague status line, but both fields ship in this payload.
    /// </summary>
    internal sealed class LeagueQueueStatusService
    {
        internal const string SearchPath = "/lol-matchmaking/v1/search";

        private readonly ILeagueClientApi _reader;

        public LeagueQueueStatusService(ILeagueClientApi reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<LeagueQueueStatus> ReadAsync(CancellationToken cancellationToken)
        {
            var bytes = await _reader.TryGetBytesAsync(SearchPath, cancellationToken).ConfigureAwait(false);
            return ParseSearch(bytes);
        }

        internal static LeagueQueueStatus ParseSearch(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return LeagueQueueStatus.Unavailable;

            System.Collections.Generic.Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes))
                    as System.Collections.Generic.Dictionary<string, object>;
            }
            catch (ArgumentException) { return LeagueQueueStatus.Unavailable; }
            catch (InvalidOperationException) { return LeagueQueueStatus.Unavailable; }
            object inQueue;
            if (root == null || !root.TryGetValue("isCurrentlyInQueue", out inQueue) || !(inQueue is bool)) return LeagueQueueStatus.Unavailable;
            double elapsed, estimate;
            var elapsedKnown = TryReadDuration(root, "timeInQueue", out elapsed);
            var estimateKnown = TryReadDuration(root, "estimatedQueueTime", out estimate);

            object lowRaw;
            var low = root.TryGetValue("lowPriorityData", out lowRaw)
                ? lowRaw as System.Collections.Generic.Dictionary<string, object> : null;

            return new LeagueQueueStatus
            {
                Available = true,
                InQueue = (bool)inQueue,
                ElapsedKnown = elapsedKnown,
                EstimateKnown = estimateKnown,
                EstimatedSeconds = estimate,
                SecondsInQueue = elapsed,
                QueueId = (int)ReadDouble(root, "queueId"),
                PenaltySecondsRemaining = ReadDouble(low, "penaltyTimeRemaining"),
                SearchState = ReadString(root, "searchState")
            };
        }

        private static bool TryReadDuration(System.Collections.Generic.Dictionary<string, object> value, string key, out double result)
        {
            result = 0;
            var raw = ReadString(value, key);
            double parsed;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0 || parsed > int.MaxValue) return false;
            result = parsed;
            return true;
        }

        private static string ReadString(System.Collections.Generic.Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, CultureInfo.InvariantCulture) : null;
        }

        private static double ReadDouble(System.Collections.Generic.Dictionary<string, object> value, string key)
        {
            object entry;
            if (value == null || !value.TryGetValue(key, out entry) || entry == null) return 0;
            double parsed;
            return double.TryParse(Convert.ToString(entry, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }
    }
}
