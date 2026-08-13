using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;

namespace FACM.League
{
    internal sealed class LeagueDashboardDetailsService
    {
        internal const string SummonerPath = "/lol-summoner/v1/current-summoner";
        internal const string SessionPath = "/lol-gameflow/v1/session";

        private readonly ILeagueClientApi _client;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };

        public LeagueDashboardDetailsService(ILeagueClientApi client, PerformanceBudgetProvider budgets)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
        }

        public async Task<LeagueDashboardSnapshot> LoadAsync(LeagueDashboardPhaseState phase, CancellationToken cancellationToken)
        {
            if (phase == null) throw new ArgumentNullException(nameof(phase));
            var snapshot = new LeagueDashboardSnapshot
            {
                Connected = phase.Connected,
                Phase = phase.Phase,
                Activity = phase.Activity,
                BudgetName = phase.BudgetName,
                UpdatedAtUtc = phase.UpdatedAtUtc
            };
            if (!phase.Connected) return snapshot;

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                byte[] summonerBytes;
                byte[] sessionBytes;
                if (_budgets.Current.NetworkConcurrency >= 2)
                {
                    var summonerTask = _client.TryGetBytesAsync(SummonerPath, timeout.Token);
                    var sessionTask = _client.TryGetBytesAsync(SessionPath, timeout.Token);
                    await Task.WhenAll(summonerTask, sessionTask).ConfigureAwait(false);
                    summonerBytes = summonerTask.Result;
                    sessionBytes = sessionTask.Result;
                }
                else
                {
                    summonerBytes = await _client.TryGetBytesAsync(SummonerPath, timeout.Token).ConfigureAwait(false);
                    sessionBytes = await _client.TryGetBytesAsync(SessionPath, timeout.Token).ConfigureAwait(false);
                }

                ApplySummoner(snapshot, summonerBytes);
                ApplySession(snapshot, sessionBytes);
                snapshot.BudgetName = _budgets.Current.Name;
                snapshot.UpdatedAtUtc = DateTime.UtcNow;
                return snapshot;
            }
        }

        internal void ApplySummoner(LeagueDashboardSnapshot snapshot, byte[] bytes)
        {
            var data = ParseObject(bytes);
            if (snapshot == null || data == null) return;
            snapshot.GameName = ReadString(data, "gameName");
            snapshot.TagLine = ReadString(data, "tagLine");
            snapshot.DisplayName = ReadString(data, "displayName");
            snapshot.SummonerLevel = ReadInt(data, "summonerLevel");
            snapshot.ProfileIconId = ReadInt(data, "profileIconId");
        }

        internal void ApplySession(LeagueDashboardSnapshot snapshot, byte[] bytes)
        {
            var data = ParseObject(bytes);
            if (snapshot == null || data == null) return;
            object mapValue;
            if (!data.TryGetValue("map", out mapValue)) return;
            var map = mapValue as Dictionary<string, object>;
            if (map == null) return;
            snapshot.PlatformId = ReadString(map, "platformId");
            snapshot.PlatformName = ReadString(map, "platformName");
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            object value;
            int result;
            return source != null && source.TryGetValue(key, out value) && value != null && int.TryParse(Convert.ToString(value), out result) ? result : 0;
        }
    }
}
