using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.League;
using FACM.Mayhem;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Opt-in live-pilot connector. The approved compact painter is unmodified geometrically.
    // This consumes the existing session owner, rather than creating another Gameflow monitor.
    internal sealed class CompactLivePilotController : IDisposable
    {
        private readonly MainForm _owner;
        private readonly ILeagueClientApi _league;
        private readonly Action<Form> _placePanel;
        private readonly CompactApprovedLivePanelForm _panel;
        private CancellationTokenSource _work;
        private bool _mayhem;
        private bool _disposed;
        private int _generation;
        private string _mode = "模式未识别";

        internal CompactLivePilotController(MainForm owner, ILeagueClientApi league, Action<Form> placePanel)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _league = league ?? throw new ArgumentNullException(nameof(league));
            _placePanel = placePanel ?? throw new ArgumentNullException(nameof(placePanel));
            _panel = new CompactApprovedLivePanelForm();
            _panel.HideRequested += delegate { Hide(); };
            _panel.PlacementRequested += delegate { _placePanel(_panel); };
            _panel.PageOpened += HandlePageOpened;
            _panel.NavigationRequested += OpenExistingFunction;
            _panel.ChampionSelected += champion => QueryChampion(champion);
            _panel.ModeRefreshRequested += delegate { RefreshMode(); };
        }

        internal bool Visible { get { return !_disposed && _panel.Visible; } }

        internal void Show()
        {
            if (_disposed) return;
            _panel.ShowHome();
            _panel.UpdateGameMode(_mode, _mayhem);
            _placePanel(_panel);
            if (!_panel.Visible) _panel.Show();
            _panel.BringToFront();
            _panel.Activate();
            RefreshMode();
        }

        internal void Hide()
        {
            if (_disposed) return;
            StopPending();
            _panel.Hide();
        }

        private CancellationToken NewRequest(out int id)
        {
            StopPending();
            _work = new CancellationTokenSource();
            id = ++_generation;
            return _work.Token;
        }

        private void StopPending()
        {
            var previous = _work;
            _work = null;
            ++_generation;
            if (previous == null) return;
            try { previous.Cancel(); }
            finally { previous.Dispose(); }
        }

        private void RefreshMode()
        {
            if (_disposed || !_panel.Visible) return;
            int generation;
            var token = NewRequest(out generation);
            _panel.UpdateGameMode("识别模式中", false);
            _ = RefreshModeAsync(generation, token);
        }

        private async Task RefreshModeAsync(int generation, CancellationToken token)
        {
            ModeInfo info;
            try
            {
                info = await DetectModeAsync(_league, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                AppLog.Info("Compact live mode lookup unavailable: " + exception.GetType().Name);
                info = new ModeInfo();
            }
            OnUi(delegate
            {
                if (!IsCurrent(generation)) return;
                _mode = info.Name;
                _mayhem = info.Mayhem;
                _panel.UpdateGameMode(info.Name, info.Mayhem);
                if (info.Mayhem && _panel.CurrentPage == "mayhem")
                    LoadRanking(generation, token);
            });
        }

        private void HandlePageOpened(string page)
        {
            if (_disposed) return;
            if (page == "mayhem")
            {
                // An explicit hero page request refreshes the already-known queue only when unknown.
                if (!_mayhem && (_mode == "模式未识别" || _mode == "识别模式中"))
                    RefreshMode();
                else if (_mayhem)
                {
                    int generation;
                    var token = NewRequest(out generation);
                    LoadRanking(generation, token);
                }
            }
        }

        private void LoadRanking(int generation, CancellationToken token)
        {
            if (!_mayhem) return;
            _panel.UpdateRanking(null, "国内优先 · 正在获取");
            _ = LoadRankingAsync(generation, token);
        }

        private async Task LoadRankingAsync(int generation, CancellationToken token)
        {
            MayhemRankingSnapshot ranking = null;
            try { ranking = await OpggMayhemService.QueryCompactRankingAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                AppLog.Info("Compact ranking lookup unavailable: " + exception.GetType().Name);
            }
            OnUi(delegate
            {
                if (!IsCurrent(generation) || !_mayhem) return;
                var status = ranking == null ? "暂无完整可验证榜单" :
                    (ranking.Source + " · " + ranking.Patch);
                _panel.UpdateRanking(ranking, status);
            });
        }

        private void QueryChampion(string champion)
        {
            if (!_mayhem || _disposed || string.IsNullOrWhiteSpace(champion)) return;
            int generation;
            var token = NewRequest(out generation);
            _ = QueryChampionAsync(champion, generation, token);
        }

        private async Task QueryChampionAsync(string champion, int generation, CancellationToken token)
        {
            MayhemChampionResult detail = null;
            try { detail = await OpggMayhemService.QueryAsync(champion, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                AppLog.Info("Compact champion detail unavailable: " + exception.GetType().Name);
            }
            OnUi(delegate
            {
                if (!IsCurrent(generation) || !_mayhem) return;
                _panel.UpdateChampion(detail != null && string.IsNullOrWhiteSpace(detail.ErrorMessage) ? detail : null);
            });
        }

        private void OpenExistingFunction(string key)
        {
            if (_disposed || string.IsNullOrWhiteSpace(key)) return;
            var parts = key.Split(':');
            if (parts.Length != 3) return;
            int index;
            if (!int.TryParse(parts[2], out index)) return;
            string route = null;
            switch (parts[1])
            {
                case "workbench":
                    route = index == 0 ? LeagueHubNavigation.Dashboard :
                        index == 1 ? LeagueHubNavigation.Live :
                        index == 3 ? LeagueHubNavigation.Player :
                        index == 4 ? LeagueHubNavigation.Profile : null;
                    break;
                case "records": route = LeagueHubNavigation.Player; break;
                case "tools": route = LeagueHubNavigation.Efficiency; break;
                case "repair": route = LeagueHubNavigation.Repair; break;
                case "settings": route = LeagueHubNavigation.Profile; break;
            }
            if (string.IsNullOrWhiteSpace(route)) return;
            Hide();
            LeagueHubUiBridge.RequestOpen(_owner, route);
        }

        private bool IsCurrent(int generation)
        {
            return !_disposed && _panel.Visible && generation == _generation;
        }

        private void OnUi(Action action)
        {
            if (_disposed || _owner.IsDisposed || !_owner.IsHandleCreated) return;
            try { _owner.BeginInvoke(new Action(delegate { if (!_disposed) action(); })); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        internal sealed class ModeInfo
        {
            internal string Name = "模式未识别";
            internal bool Mayhem;
        }

        internal static async Task<ModeInfo> DetectModeAsync(ILeagueClientApi api, CancellationToken token)
        {
            if (api == null) return new ModeInfo();
            // Read on demand when the panel opens, not a second polling loop.
            var paths = new[] { "/lol-gameflow/v1/session", "/lol-lobby/v2/lobby" };
            foreach (var path in paths)
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(1.2));
                    byte[] bytes;
                    try { bytes = await api.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; continue; }
                    catch (Exception) { continue; }
                    if (bytes == null || bytes.Length == 0) continue;
                    try
                    {
                        var root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes));
                        var result = ProjectMode(root);
                        if (result.Name != "模式未识别") return result;
                    }
                    catch (Exception) { }
                }
            }
            return new ModeInfo();
        }

        internal static ModeInfo ProjectMode(object root)
        {
            var info = new ModeInfo();
            var data = root as IDictionary<string, object>;
            if (data == null) return info;
            // Riot gameflow session reports gameData.queue.id; lobby uses gameConfig.queueId.
            int id = 0;
            var gameData = Member(data, "gameData") as IDictionary<string, object>;
            var gameConfig = Member(data, "gameConfig") as IDictionary<string, object>;
            var queue = gameData == null ? null : Member(gameData, "queue") as IDictionary<string, object>;
            if (queue != null) id = AsInt(Member(queue, "id"));
            if (id <= 0 && gameConfig != null) id = AsInt(Member(gameConfig, "queueId"));
            var mode = gameData == null ? string.Empty : Convert.ToString(Member(gameData, "gameMode")) ?? string.Empty;
            if (LeagueQueueModePolicy.IsAramMayhem(id, mode))
            {
                info.Name = "海克斯大乱斗";
                info.Mayhem = true;
            }
            else if (id == 450 || string.Equals(mode, "ARAM", StringComparison.OrdinalIgnoreCase))
                info.Name = "普通大乱斗";
            else if (id == 400 || id == 420 || id == 430 || id == 440)
                info.Name = "召唤师峡谷";
            return info;
        }

        private static object Member(IDictionary<string, object> value, string key)
        {
            object found;
            return value != null && value.TryGetValue(key, out found) ? found : null;
        }

        private static int AsInt(object value)
        {
            int parsed;
            return value != null && int.TryParse(Convert.ToString(value), out parsed) ? parsed : 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopPending();
            _panel.Close();
            _panel.Dispose();
        }
    }
}
