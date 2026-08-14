using System;
using System.Diagnostics;
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
        private static readonly string[] ClientProcessNames = { "LeagueClientUx", "LeagueClient" };
        private static readonly string[] GameProcessNames = { "League of Legends" };
        private readonly ILeagueClientApi _client;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

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
                var clientProcessDetected = connected || IsAnyProcessRunning(ClientProcessNames);
                var gameProcessDetected = IsAnyProcessRunning(GameProcessNames);
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
                    Activity = activity,
                    BudgetName = _budgets.Current.Name,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }
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
