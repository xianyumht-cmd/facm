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
        private CancellationTokenSource _championWork;
        private int _championGeneration;
        private bool _mayhem;
        private bool _disposed;
        private int _generation;
        private int _rankRequestGeneration = -1;
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
            _rankRequestGeneration = -1;
            return _work.Token;
        }

        private void StopPending()
        {
            var previous = _work;
            _work = null;
            ++_generation;
            CancelChampion();
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
                if (info.Mayhem)
                {
                    _ = FindCurrentChampionAsync(generation, token);
                    // Prefetch rankings immediately after mode recognition, before a user opens the tab.
                    LoadRanking(generation, token);
                }
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
                else if (_mayhem && _rankRequestGeneration != _generation)
                {
                    var token = _work == null ? CancellationToken.None : _work.Token;
                    LoadRanking(_generation, token);
                }
            }
        }

        private void LoadRanking(int generation, CancellationToken token)
        {
            if (!_mayhem || _rankRequestGeneration == generation) return;
            _rankRequestGeneration = generation;
            bool stale;
            var cached = OpggMayhemService.GetCachedCompactRanking(out stale);
            _panel.UpdateRanking(cached, cached == null ? "国内优先 · 正在获取" :
                (stale ? "历史缓存 · 未核验" : "本地缓存 · 未核验"));
            _ = LoadRankingAsync(generation, token, cached, stale);
        }

        private async Task LoadRankingAsync(int generation, CancellationToken token,
            MayhemRankingSnapshot cached, bool wasStale)
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
                if (ranking != null)
                    _panel.UpdateRanking(ranking, ranking.Source + " · " + ranking.Patch + " · 未核验");
                else if (cached != null)
                    _panel.UpdateRanking(cached, wasStale ? "历史缓存 · 网络不可用" : "本地缓存 · 网络不可用");
                else
                    _panel.UpdateRanking(null, "暂无完整可验证榜单");
            });
        }

        private void CancelChampion()
        {
            var previous = _championWork;
            _championWork = null;
            ++_championGeneration;
            if (previous == null) return;
            try { previous.Cancel(); }
            finally { previous.Dispose(); }
        }

        private void QueryChampion(string champion)
        {
            if (!_mayhem || _disposed || string.IsNullOrWhiteSpace(champion)) return;
            CancelChampion();
            _championWork = new CancellationTokenSource();
            var generation = ++_championGeneration;
            var modeGeneration = _generation;
            _panel.UpdateSelectedChampionName(champion);
            _ = QueryChampionAsync(champion, modeGeneration, generation, _championWork.Token);
        }

        private async Task QueryChampionAsync(string champion, int modeGeneration, int heroGeneration,
            CancellationToken token)
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
                if (!IsCurrent(modeGeneration) || !_mayhem || heroGeneration != _championGeneration) return;
                _panel.UpdateChampion(detail != null && string.IsNullOrWhiteSpace(detail.ErrorMessage) ? detail : null);
            });
        }

        private async Task FindCurrentChampionAsync(int modeGeneration, CancellationToken token)
        {
            var heroAtStart = _championGeneration;
            try
            {
                using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    bounded.CancelAfter(TimeSpan.FromSeconds(2.8));
                    var bytes = await _league.TryGetBytesAsync("/lol-champ-select/v1/session",
                        bounded.Token).ConfigureAwait(false);
                    if (bytes == null || bytes.Length == 0) return;
                    var root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes))
                        as IDictionary<string, object>;
                    if (root == null) return;
                    var selectedId = ResolveSelectedChampionId(root);
                    var benchIds = ResolveBenchChampionIds(root);
                    if (selectedId <= 0 && benchIds.Count == 0) return;

                    // One local LCU metadata read resolves all visible bench names without
                    // N overseas champion-icon or champion-name requests.
                    var summary = await _league.TryGetBytesAsync(
                        "/lol-game-data/assets/v1/champion-summary.json",
                        bounded.Token).ConfigureAwait(false);
                    var namesById = ParseChampionSummary(summary);
                    var quickNames = new List<string>();
                    foreach (var id in benchIds)
                    {
                        string name;
                        quickNames.Add(namesById.TryGetValue(id, out name) ? name : null);
                    }
                    string selectedName;
                    namesById.TryGetValue(selectedId, out selectedName);
                    OnUi(delegate
                    {
                        if (!IsCurrent(modeGeneration) || !_mayhem ||
                            heroAtStart != _championGeneration) return;
                        _panel.UpdateQuickChampions(quickNames);
                        if (!string.IsNullOrWhiteSpace(selectedName))
                            _panel.UpdateSelectedChampionName(selectedName);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Compact local hero labels unavailable: " + exception.GetType().Name);
            }
        }

        internal static List<int> ResolveBenchChampionIds(IDictionary<string, object> session)
        {
            var result = new List<int>();
            if (session == null) return result;
            var items = Member(session, "benchChampions") as object[];
            if (items == null) return result;
            foreach (var item in items)
            {
                var row = item as IDictionary<string, object>;
                var id = AsInt(Member(row, "championId"));
                if (id <= 0 || result.Contains(id)) continue;
                result.Add(id);
                if (result.Count == 4) break;
            }
            return result;
        }

        internal static Dictionary<int, string> ParseChampionSummary(byte[] bytes)
        {
            var result = new Dictionary<int, string>();
            if (bytes == null || bytes.Length == 0 || bytes.Length > 3 * 1024 * 1024)
                return result;
            try
            {
                var data = new JavaScriptSerializer { MaxJsonLength = 3 * 1024 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[];
                if (data == null) return result;
                foreach (var item in data)
                {
                    var row = item as IDictionary<string, object>;
                    if (row == null) continue;
                    var id = AsInt(Member(row, "id"));
                    var name = Convert.ToString(Member(row, "name"));
                    if (id > 0 && !string.IsNullOrWhiteSpace(name))
                        result[id] = name.Trim();
                }
            }
            catch (Exception) { return new Dictionary<int, string>(); }
            return result;
        }

        internal static int ResolveSelectedChampionId(IDictionary<string, object> session)
        {
            if (session == null) return 0;
            if (!session.ContainsKey("localPlayerCellId")) return 0;
            var ownCell = AsInt(Member(session, "localPlayerCellId"));
            var team = Member(session, "myTeam") as object[];
            if (team == null) return 0;
            foreach (var entry in team)
            {
                var participant = entry as IDictionary<string, object>;
                if (participant == null || !participant.ContainsKey("cellId") ||
                    AsInt(Member(participant, "cellId")) != ownCell) continue;
                var id = AsInt(Member(participant, "championId"));
                return id > 0 ? id : 0;
            }
            return 0;
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

        internal static void ValidateForSmokeTest()
        {
            MayhemCompactRankingCache.ValidateForSmokeTest();
            var mayhem = new Dictionary<string, object> {
                { "gameData", new Dictionary<string, object> {
                    { "queue", new Dictionary<string, object> { { "id", 2400 } } }
                } }
            };
            var aram = new Dictionary<string, object> {
                { "gameConfig", new Dictionary<string, object> { { "queueId", 450 } } }
            };
            var unknown = new Dictionary<string, object> {
                { "gameConfig", new Dictionary<string, object> { { "queueId", 0 } } }
            };
            if (!ProjectMode(mayhem).Mayhem ||
                ProjectMode(aram).Mayhem ||
                ProjectMode(aram).Name != "普通大乱斗" ||
                ProjectMode(unknown).Name != "模式未识别")
                throw new InvalidOperationException("Compact mode mapping returned incorrect game-mode rankings.");
            var self = new Dictionary<string, object> {
                { "localPlayerCellId", 3 },
                { "myTeam", new object[] {
                    new Dictionary<string, object> { { "cellId", 2 }, { "championId", 67 } },
                    new Dictionary<string, object> { { "cellId", 3 }, { "championId", 157 } }
                } }
            };
            if (ResolveSelectedChampionId(self) != 157)
                throw new InvalidOperationException("Compact selected champion picked another player's slot.");
            var bench = new Dictionary<string, object> {
                { "benchChampions", new object[] {
                    new Dictionary<string, object> { { "championId", 157 } },
                    new Dictionary<string, object> { { "championId", 67 } },
                    new Dictionary<string, object> { { "championId", 157 } },
                    new Dictionary<string, object> { { "championId", 0 } }
                } }
            };
            var benchIds = ResolveBenchChampionIds(bench);
            if (benchIds.Count != 2 || benchIds[0] != 157 || benchIds[1] != 67)
                throw new InvalidOperationException("Quick hero bench includes duplicates or invalid slots.");
            var summary = Encoding.UTF8.GetBytes(
                "[{\"id\":157,\"name\":\"Yasuo\"},{\"id\":67,\"name\":\"Vayne\"}]");
            var names = ParseChampionSummary(summary);
            if (names.Count != 2 || names[157] != "Yasuo" || names[67] != "Vayne")
                throw new InvalidOperationException("One-shot local hero summary lookup failed.");
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
