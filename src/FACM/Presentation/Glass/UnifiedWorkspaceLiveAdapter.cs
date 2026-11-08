using System;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Per-workspace read lifetime; LeagueLiveModule remains the only LCU/SGP owner.
    internal sealed class UnifiedWorkspaceLiveAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private CancellationTokenSource _cancellation;
        private int _generation;

        internal UnifiedWorkspaceLiveAdapter(Func<bool> canPublish, Action<string> publish)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        internal void Start(LeagueLiveModule module, bool includeSgp)
        {
            Cancel();
            if (module == null) { PublishError(); return; }
            var source = new CancellationTokenSource();
            _cancellation = source;
            ReadAsync(module, includeSgp, source, _generation);
        }

        private async void ReadAsync(LeagueLiveModule module, bool includeSgp,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var session = module.UnifiedLiveSession;
                var snapshot = await module.ReadUnifiedLiveAsync(includeSgp, source.Token);
                if (!ReadCurrent(module, session, source, generation))
                {
                    if (ReadCurrent(module, null, source, generation, false)) PublishError();
                    return;
                }
                if (snapshot == null) { PublishError(); return; }
                _publish(SerializeLive(snapshot));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified live read skipped: " + exception.GetType().Name);
                if (ReadCurrent(module, null, source, generation, false)) PublishError();
            }
            finally
            {
                if (ReferenceEquals(_cancellation, source)) _cancellation = null;
                source.Dispose();
            }
        }

        private bool ReadCurrent(LeagueLiveModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !source.IsCancellationRequested && generation == _generation &&
                ReferenceEquals(_cancellation, source) && _canPublish() &&
                (!checkSession || (session == null ? module.UnifiedLiveSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLiveSession)));
        }

        private static string SerializeLive(LeagueLiveSnapshot snapshot)
        {
            return new JavaScriptSerializer().Serialize(new
            {
                type = "live-details",
                connected = snapshot.Connected,
                phase = snapshot.Phase,
                queue = snapshot.QueueName,
                mode = snapshot.GameMode,
                map = snapshot.MapName,
                gameId = snapshot.GameId,
                timer = snapshot.TimerMillisecondsLeft,
                sgp = snapshot.SgpEnriched,
                allyBans = snapshot.AllyBans.ToArray(),
                enemyBans = snapshot.EnemyBans.ToArray(),
                players = snapshot.Players.Select(player => new
                {
                    side = player.Side,
                    name = player.AccountName,
                    position = player.Position,
                    champion = player.ChampionId,
                    local = player.IsLocalPlayer
                }).Take(24).ToArray()
            });
        }

        private void PublishError()
        {
            if (_canPublish()) _publish("{\"type\":\"live-failed\"}");
        }

        internal void Cancel()
        {
            _generation++;
            var previous = _cancellation;
            _cancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            var snapshot = new LeagueLiveSnapshot
            {
                Connected = true, QueueName = "ARAM", GameMode = "ARAM",
                SgpEnriched = true, GameId = 12345
            };
            for (var index = 0; index < 26; index++)
                snapshot.Players.Add(new LeagueLivePlayerRow
                {
                    Side = "ally", DisplayName = "Test" + index, ChampionId = index + 1
                });
            var json = SerializeLive(snapshot);
            if (!json.Contains("\"type\":\"live-details\"") ||
                !json.Contains("\"queue\":\"ARAM\"") ||
                !json.Contains("\"sgp\":true") ||
                !json.Contains("\"gameId\":12345") ||
                json.Split(new[] { "\"champion\":" }, StringSplitOptions.None).Length != 25)
                throw new InvalidOperationException("Live shard JSON contract or 24-player bound changed.");
            var firstPosts = 0;
            var secondPosts = 0;
            var first = new UnifiedWorkspaceLiveAdapter(() => true,
                delegate { firstPosts++; });
            var second = new UnifiedWorkspaceLiveAdapter(() => true,
                delegate { secondPosts++; });
            first.Start(null, false);
            second.Start(null, false);
            first.Cancel();
            second.Start(null, true);
            if (firstPosts != 1 || secondPosts != 2)
                throw new InvalidOperationException("Live request adapters leaked state across owners.");
        }
    }
}
