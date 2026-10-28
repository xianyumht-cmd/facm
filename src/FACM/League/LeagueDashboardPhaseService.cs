using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;

namespace FACM.League
{
    internal sealed class LeagueDashboardPhaseService
    {
        internal const string PhasePath = "/lol-gameflow/v1/gameflow-phase";
        internal const string SessionPath = "/lol-gameflow/v1/session";
        private static readonly string[] ClientProcessNames = { "LeagueClientUx", "LeagueClient" };
        private static readonly string[] GameProcessNames = { "League of Legends" };
        private readonly ILeagueClientApi _client;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private bool _selectionQueueChecked;
        private bool _gameQueueChecked;
        private bool? _rankedQueue;

        public LeagueDashboardPhaseService(ILeagueClientApi client, PerformanceBudgetProvider budgets)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
        }

        public async Task<LeagueDashboardPhaseState> RefreshAsync(CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var bytes = await _client.TryGetBytesAsync(PhasePath, timeout.Token).ConfigureAwait(false);
                var connected = bytes != null && bytes.Length > 0;
                var phase = connected ? ParsePhase(bytes) : null;
                if (!connected || phase == "Lobby" || phase == "None" ||
                    phase == "Matchmaking" || phase == "ReadyCheck")
                {
                    _selectionQueueChecked = false;
                    _gameQueueChecked = false;
                    _rankedQueue = null;
                }
                else if (!_rankedQueue.HasValue)
                {
                    var selection = phase == "ChampSelect";
                    var game = phase == "GameStart" || phase == "InProgress" || phase == "Reconnect";
                    if ((selection && !_selectionQueueChecked) || (game && !_gameQueueChecked))
                    {
                        if (selection) _selectionQueueChecked = true;
                        if (game) _gameQueueChecked = true;
                        try
                        {
                            _rankedQueue = ParseRankedQueue(
                                await _client.TryGetBytesAsync(SessionPath, timeout.Token).ConfigureAwait(false));
                        }
                        catch (OperationCanceledException)
                        {
                            if (cancellationToken.IsCancellationRequested) throw;
                        }
                        catch (Exception)
                        {
                            // Optional queue evidence must not interrupt Gameflow observation.
                        }
                    }
                }

                var clientProcessDetected = connected;
                var gameProcessDetected = false;
                if (!connected)
                {
                    clientProcessDetected = IsAnyProcessRunning(ClientProcessNames);
                    gameProcessDetected = IsAnyProcessRunning(GameProcessNames);
                }

                var activity = LeagueGameflowActivityMapper.Map(
                    phase,
                    connected,
                    clientProcessDetected,
                    gameProcessDetected);
                _budgets.UpdateLeagueActivity(activity);
                return new LeagueDashboardPhaseState
                {
                    Connected = connected,
                    ClientProcessDetected = clientProcessDetected,
                    GameProcessDetected = gameProcessDetected,
                    Phase = phase,
                    IsRankedQueue = _rankedQueue,
                    Activity = activity,
                    BudgetName = _budgets.Current.Name,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }
        }

        internal static bool? ParseRankedQueue(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 256 * 1024) return null;
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = 256 * 1024, RecursionLimit = 32 };
                var root = json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                object data, queueObject;
                if (root == null || !root.TryGetValue("gameData", out data)) return null;
                var gameData = data as Dictionary<string, object>;
                if (gameData == null || !gameData.TryGetValue("queue", out queueObject)) return null;
                var queue = queueObject as Dictionary<string, object>;
                if (queue == null) return null;

                object rawId, rawType;
                int id;
                int? queueId = queue.TryGetValue("id", out rawId) && rawId != null &&
                    int.TryParse(Convert.ToString(rawId, CultureInfo.InvariantCulture), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out id) && id > 0 ? id : (int?)null;
                var queueType = queue.TryGetValue("type", out rawType) && rawType is string
                    ? ((string)rawType).Trim() : null;
                bool? byId = queueId.HasValue ? (bool?)(queueId.Value == 420 || queueId.Value == 440) : null;
                bool? byType = string.IsNullOrWhiteSpace(queueType) ? null :
                    (bool?)(string.Equals(queueType, "RANKED_SOLO_5x5", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(queueType, "RANKED_FLEX_SR", StringComparison.OrdinalIgnoreCase));
                if (byId.HasValue && byType.HasValue && byId.Value != byType.Value) return false;
                return byId ?? byType;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.Web.Script.Serialization.InvalidJSONPrimitiveException) { return null; }
        }

        internal string ParsePhase(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var text = Encoding.UTF8.GetString(bytes).Trim();
            if (text.Length == 0) return null;
            try { return _json.Deserialize<string>(text); }
            catch { return text.Trim('"'); }
        }

        internal static bool IsAnyProcessRunning(string[] processNames)
        {
            if (processNames == null) return false;
            foreach (var processName in processNames)
            {
                Process[] processes = null;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0) return true;
                }
                catch
                {
                    // Process presence is only a fallback signal; LCU remains authoritative when available.
                }
                finally
                {
                    if (processes != null)
                    {
                        foreach (var process in processes) process.Dispose();
                    }
                }
            }
            return false;
        }
    }
}
