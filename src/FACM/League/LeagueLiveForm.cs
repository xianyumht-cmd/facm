using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueLiveForm : Form
    {
        private readonly LeagueLiveDataService _service;
        private readonly LeagueBenchQuickPickService _benchQuickPick;
        private readonly UiTextCatalog _ui;
        private readonly Label _phaseLabel;
        private readonly Label _summaryLabel;
        private readonly Label _detailLabel;
        private readonly Label _bansLabel;
        private readonly Label _statusLabel;
        private readonly Panel _benchCard;
        private readonly Label _benchStateLabel;
        private readonly FlowLayoutPanel _benchFlow;
        private readonly ListView _playersList;
        private readonly Button _refreshButton;
        private readonly ToolTip _benchToolTip = new ToolTip();
        private readonly Dictionary<int, Image> _benchImages = new Dictionary<int, Image>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _refreshing;
        private bool _benchRefreshing;
        private bool _benchSwapping;
        private bool _benchActive;
        private string _benchSignature = string.Empty;
        private LeagueActivityLevel _lastActivity = LeagueActivityLevel.None;

        public LeagueLiveForm(LeagueLiveDataService service, LeagueBenchQuickPickService benchQuickPick, UiTextCatalog ui)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _benchQuickPick = benchQuickPick ?? throw new ArgumentNullException(nameof(benchQuickPick));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = _ui.Get(UiTextKeys.LeagueLiveWindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 670);
            MinimumSize = new Size(790, 600);
            MaximizeBox = true;
            BackColor = Color.FromArgb(14, 19, 30);
            ForeColor = Color.FromArgb(238, 243, 252);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = _ui.Get(UiTextKeys.LeagueLiveTitle),
                Location = new Point(28, 20),
                Size = new Size(420, 36),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.LiveHint),
                Location = new Point(30, 60),
                Size = new Size(820, 24),
                ForeColor = Color.FromArgb(146, 161, 188)
            };

            _phaseLabel = CreateInfoLabel(new Point(30, 96), new Size(820, 24), true);
            _summaryLabel = CreateInfoLabel(new Point(30, 126), new Size(820, 24), false);
            _detailLabel = CreateInfoLabel(new Point(30, 154), new Size(820, 24), false);
            _bansLabel = CreateInfoLabel(new Point(30, 182), new Size(820, 24), false);
            _statusLabel = CreateInfoLabel(new Point(30, 210), new Size(820, 22), false);

            _benchCard = new Panel
            {
                Location = new Point(28, 238),
                Size = new Size(844, 100),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(18, 27, 43)
            };
            var benchTitle = new Label
            {
                Text = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Title),
                Location = new Point(12, 8),
                Size = new Size(190, 22),
                ForeColor = Color.FromArgb(115, 224, 255),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            _benchStateLabel = new Label
            {
                Text = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Waiting),
                Location = new Point(202, 8),
                Size = new Size(626, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(146, 161, 188),
                AutoEllipsis = true
            };
            _benchFlow = new FlowLayoutPanel
            {
                Location = new Point(9, 34),
                Size = new Size(826, 58),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(13, 20, 33),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(4, 2, 4, 2)
            };
            _benchCard.Controls.Add(benchTitle);
            _benchCard.Controls.Add(_benchStateLabel);
            _benchCard.Controls.Add(_benchFlow);

            _playersList = new ListView
            {
                Location = new Point(28, 350),
                Size = new Size(844, 258),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(22, 29, 44),
                ForeColor = Color.FromArgb(238, 243, 252),
                BorderStyle = BorderStyle.FixedSingle,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = true,
                View = View.Details
            };
            _playersList.Columns.Add(_ui.Get(UiTextKeys.LeagueLiveTeam), 92);
            _playersList.Columns.Add(_ui.Get(UiTextKeys.LeagueLivePlayer), 230);
            _playersList.Columns.Add(_ui.Get(UiTextKeys.LeagueLivePosition), 120);
            _playersList.Columns.Add(_ui.Get(UiTextKeys.LeagueLiveChampion), 110);
            _playersList.Columns.Add(_ui.Get(UiTextKeys.LeagueLiveIntent), 110);
            _playersList.Columns.Add(_ui.Get(UiTextKeys.LeagueLiveSpells), 120);

            _refreshButton = CreateButton(UiTextKeys.LeagueLiveRefresh, Color.FromArgb(55, 104, 214));
            _refreshButton.Location = new Point(680, 626);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshOnceAsync(); };

            var close = CreateButton(UiTextKeys.Close, Color.FromArgb(35, 43, 60));
            close.Location = new Point(780, 626);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { Close(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_phaseLabel);
            Controls.Add(_summaryLabel);
            Controls.Add(_detailLabel);
            Controls.Add(_bansLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_benchCard);
            Controls.Add(_playersList);
            Controls.Add(_refreshButton);
            Controls.Add(close);

            ApplyWaitingState();
            Shown += async delegate
            {
                await Task.WhenAll(RunLoopAsync(), RunBenchLoopAsync());
            };
            FormClosed += delegate
            {
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _benchToolTip.Dispose();
                foreach (var image in _benchImages.Values) image.Dispose();
                _benchImages.Clear();
                _lifetime.Dispose();
            };
        }

        private Label CreateInfoLabel(Point location, Size size, bool bold)
        {
            return new Label
            {
                Location = location,
                Size = size,
                ForeColor = bold ? Color.White : Color.FromArgb(176, 191, 216),
                Font = new Font("Microsoft YaHei UI", bold ? 10F : 9F, bold ? FontStyle.Bold : FontStyle.Regular),
                AutoEllipsis = true
            };
        }

        private Button CreateButton(string key, Color background)
        {
            var button = new Button
            {
                Text = _ui.Get(key),
                Size = new Size(92, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(68, 79, 101);
            return button;
        }

        private async Task RunLoopAsync()
        {
            while (!_lifetime.IsCancellationRequested && !IsDisposed)
            {
                await RefreshOnceAsync();
                if (_lifetime.IsCancellationRequested || IsDisposed) break;
                var delay = LeagueLivePolling.ResolveDelay(_lastActivity, IsEffectivelyMinimized());
                try { await Task.Delay(delay, _lifetime.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RunBenchLoopAsync()
        {
            while (!_lifetime.IsCancellationRequested && !IsDisposed)
            {
                var minimized = IsEffectivelyMinimized();
                if (!minimized && _lastActivity != LeagueActivityLevel.InGame)
                    await RefreshBenchOnceAsync();

                if (_lifetime.IsCancellationRequested || IsDisposed) break;
                var delay = LeagueBenchQuickPickPolling.ResolveDelay(_benchActive, _lastActivity, minimized);
                try { await Task.Delay(delay, _lifetime.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RefreshOnceAsync()
        {
            if (_refreshing || IsDisposed || _lifetime.IsCancellationRequested) return;
            _refreshing = true;
            _refreshButton.Enabled = false;
            try
            {
                var snapshot = await _service.RefreshAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                ApplySnapshot(snapshot);
            }
            catch (OperationCanceledException)
            {
                // Form close / process shutdown owns cancellation.
            }
            catch (Exception exception)
            {
                AppLog.Info("League Live refresh skipped: " + exception.Message);
                if (!IsDisposed) _statusLabel.Text = _ui.Get(UiTextKeys.LeagueLiveUnknown);
            }
            finally
            {
                _refreshing = false;
                if (!IsDisposed) _refreshButton.Enabled = true;
            }
        }

        private async Task RefreshBenchOnceAsync()
        {
            if (_benchRefreshing || _benchSwapping || IsDisposed || _lifetime.IsCancellationRequested) return;
            _benchRefreshing = true;
            try
            {
                var state = await _benchQuickPick.RefreshAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                ApplyBenchState(state);
            }
            catch (OperationCanceledException)
            {
                // Form close / process shutdown owns cancellation.
            }
            catch (Exception exception)
            {
                AppLog.Info("League bench refresh skipped: " + exception.Message);
            }
            finally
            {
                _benchRefreshing = false;
            }
        }

        private void ApplySnapshot(LeagueLiveSnapshot snapshot)
        {
            if (snapshot == null)
            {
                ApplyWaitingState();
                return;
            }

            _lastActivity = snapshot.Activity;
            var unknown = _ui.Get(UiTextKeys.LeagueLiveUnknown);
            var phase = string.IsNullOrWhiteSpace(snapshot.Phase) ? unknown : snapshot.Phase;
            var budget = string.IsNullOrWhiteSpace(snapshot.BudgetName) ? unknown : snapshot.BudgetName;
            _phaseLabel.Text = _ui.Get(UiTextKeys.LeagueLivePhase) + ": " + phase + "  ·  " +
                               _ui.Get(UiTextKeys.LeagueLivePerformance) + ": " + budget;

            if (!snapshot.Connected)
            {
                ApplyWaitingState(false);
                return;
            }

            if (snapshot.Activity == LeagueActivityLevel.ChampSelect)
            {
                _summaryLabel.Text = _ui.Get(UiTextKeys.LeagueLiveChampSelect) +
                                     "  ·  " + _ui.Get(UiTextKeys.LeagueLiveGame) + " " + Value(snapshot.GameId) +
                                     "  ·  " + _ui.Get(UiTextKeys.LeagueLiveQueue) + " " + Value(snapshot.QueueId);
                var timer = string.IsNullOrWhiteSpace(snapshot.TimerPhase) ? unknown : snapshot.TimerPhase;
                if (snapshot.TimerMillisecondsLeft > 0) timer += " " + Math.Max(0, snapshot.TimerMillisecondsLeft / 1000) + "s";
                _detailLabel.Text = _ui.Get(UiTextKeys.LeagueLiveTimer) + ": " + timer +
                                    "  ·  " + _ui.Get(UiTextKeys.LeagueLiveLocalAction) + ": " + FormatAction(snapshot);
                _bansLabel.Text = _ui.Get(UiTextKeys.LeagueLiveBans) + ": " +
                                  _ui.Get(UiTextKeys.LeagueLiveAlly) + " [" + JoinInts(snapshot.AllyBans) + "]  ·  " +
                                  _ui.Get(UiTextKeys.LeagueLiveEnemy) + " [" + JoinInts(snapshot.EnemyBans) + "]";
                if (!_benchSwapping)
                    _statusLabel.Text = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.ManualOnly);

                ApplyBenchState(new LeagueBenchQuickPickState
                {
                    SessionAvailable = true,
                    BenchEnabled = snapshot.BenchEnabled,
                    LocalPlayerCellId = snapshot.LocalPlayerCellId,
                    LocalChampionId = ResolveLocalChampion(snapshot)
                }, snapshot.BenchChampionIds);
            }
            else if (snapshot.Activity == LeagueActivityLevel.InGame)
            {
                _summaryLabel.Text = _ui.Get(UiTextKeys.LeagueLiveCurrentGame) +
                                     "  ·  " + _ui.Get(UiTextKeys.LeagueLiveGame) + " " + Value(snapshot.GameId);
                var map = string.IsNullOrWhiteSpace(snapshot.MapName) ? unknown : snapshot.MapName;
                if (snapshot.MapId > 0) map += " #" + snapshot.MapId;
                var mode = string.IsNullOrWhiteSpace(snapshot.GameMode) ? unknown : snapshot.GameMode;
                _detailLabel.Text = _ui.Get(UiTextKeys.LeagueLiveMap) + ": " + map +
                                    "  ·  " + _ui.Get(UiTextKeys.LeagueLiveMode) + ": " + mode;
                var queue = string.IsNullOrWhiteSpace(snapshot.QueueName) ? Value(snapshot.QueueId) : snapshot.QueueName + " #" + snapshot.QueueId;
                _bansLabel.Text = _ui.Get(UiTextKeys.LeagueLiveQueue) + ": " + queue;
                _statusLabel.Text = _ui.Get(UiTextKeys.LeagueLiveReadOnly);
                ApplyBenchState(null);
            }
            else
            {
                _summaryLabel.Text = _ui.Get(UiTextKeys.LeagueLiveWaiting);
                _detailLabel.Text = string.Empty;
                _bansLabel.Text = string.Empty;
                _statusLabel.Text = _ui.Get(UiTextKeys.LeagueLiveReadOnly);
            }

            ApplyPlayers(snapshot);
        }

        private void ApplyBenchState(LeagueBenchQuickPickState state, IEnumerable<int> championIds = null)
        {
            if (state != null && championIds != null)
            {
                foreach (var championId in championIds)
                {
                    if (championId > 0) state.ChampionIds.Add(championId);
                }
            }

            if (state == null || !state.SessionAvailable)
            {
                _benchActive = false;
                _benchStateLabel.Text = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Waiting);
                RebuildBenchButtons(new int[0]);
                return;
            }

            if (!state.BenchEnabled)
            {
                _benchActive = false;
                _benchStateLabel.Text = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Disabled);
                RebuildBenchButtons(new int[0]);
                return;
            }

            _benchActive = true;
            var ids = state.ChampionIds.Where(value => value > 0).Distinct().ToArray();
            _benchStateLabel.Text = ids.Length == 0
                ? LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Waiting)
                : LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Hint);
            RebuildBenchButtons(ids);
        }

        private void RebuildBenchButtons(IEnumerable<int> championIds)
        {
            var ids = championIds == null ? new int[0] : championIds.Where(value => value > 0).Distinct().ToArray();
            var signature = string.Join(",", ids);
            if (string.Equals(signature, _benchSignature, StringComparison.Ordinal))
            {
                SetBenchButtonsEnabled(!_benchSwapping);
                return;
            }
            _benchSignature = signature;

            while (_benchFlow.Controls.Count > 0)
            {
                var control = _benchFlow.Controls[0];
                _benchFlow.Controls.RemoveAt(0);
                control.Dispose();
            }

            foreach (var championId in ids)
            {
                var capturedId = championId;
                var button = new Button
                {
                    Tag = capturedId,
                    Text = capturedId.ToString(),
                    Size = new Size(58, 50),
                    Margin = new Padding(2, 1, 4, 1),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(24, 35, 54),
                    ForeColor = Color.FromArgb(225, 236, 250),
                    Cursor = Cursors.Hand,
                    Font = new Font("Microsoft YaHei UI", 7F, FontStyle.Regular),
                    TextAlign = ContentAlignment.BottomCenter,
                    ImageAlign = ContentAlignment.TopCenter,
                    Enabled = !_benchSwapping,
                    TabStop = false
                };
                button.FlatAppearance.BorderColor = Color.FromArgb(54, 171, 207);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(31, 54, 77);
                button.Click += async delegate { await SwapBenchChampionAsync(capturedId); };
                _benchToolTip.SetToolTip(
                    button,
                    LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Tooltip) + " · #" + capturedId);
                _benchFlow.Controls.Add(button);

                Image cached;
                if (_benchImages.TryGetValue(capturedId, out cached)) button.Image = cached;
                else LoadBenchIconAsync(capturedId, button);
            }
        }

        private async void LoadBenchIconAsync(int championId, Button button)
        {
            try
            {
                var bytes = await _benchQuickPick.LoadChampionIconAsync(championId, _lifetime.Token);
                if (bytes == null || bytes.Length == 0 || IsDisposed || _lifetime.IsCancellationRequested) return;

                Image image;
                if (!_benchImages.TryGetValue(championId, out image))
                {
                    using (var stream = new MemoryStream(bytes, false))
                    using (var source = Image.FromStream(stream))
                        image = new Bitmap(source, new Size(34, 34));
                    _benchImages[championId] = image;
                }

                if (button != null && !button.IsDisposed && button.Parent == _benchFlow)
                    button.Image = image;
            }
            catch (OperationCanceledException)
            {
                // Form close owns cancellation.
            }
            catch (Exception exception)
            {
                AppLog.Info("League bench champion icon skipped: " + exception.Message);
            }
        }

        private async Task SwapBenchChampionAsync(int championId)
        {
            if (_benchSwapping || championId <= 0 || IsDisposed || _lifetime.IsCancellationRequested) return;
            _benchSwapping = true;
            SetBenchButtonsEnabled(false);
            _statusLabel.Text = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Swapping) + " #" + championId + "...";

            try
            {
                var result = await _benchQuickPick.TrySwapAsync(championId, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                _statusLabel.Text = FormatBenchSwapResult(result);
            }
            catch (OperationCanceledException)
            {
                // Form close owns cancellation.
            }
            catch (Exception exception)
            {
                AppLog.Info("League bench swap skipped: " + exception.Message);
                if (!IsDisposed)
                    _statusLabel.Text = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Rejected);
            }
            finally
            {
                _benchSwapping = false;
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                {
                    SetBenchButtonsEnabled(true);
                    await RefreshBenchOnceAsync();
                }
            }
        }

        private string FormatBenchSwapResult(LeagueBenchSwapResult result)
        {
            if (result == null)
                return LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Rejected);

            var suffix = " #" + result.ChampionId;
            if (result.ElapsedMilliseconds > 0) suffix += " · " + result.ElapsedMilliseconds + " ms";
            switch (result.Status)
            {
                case LeagueBenchSwapStatus.Success:
                    return LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Success) + suffix;
                case LeagueBenchSwapStatus.TargetUnavailable:
                    return LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Unavailable) + suffix;
                case LeagueBenchSwapStatus.BenchDisabled:
                    return LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Disabled);
                case LeagueBenchSwapStatus.SessionUnavailable:
                    return LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Waiting);
                case LeagueBenchSwapStatus.VerificationFailed:
                    return LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.VerifyFailed) + suffix;
                default:
                    var rejected = LeagueBenchQuickPickText.Get(_ui, LeagueBenchQuickPickUiTextKeys.Rejected) + suffix;
                    return result.StatusCode > 0 ? rejected + " · HTTP " + result.StatusCode : rejected;
            }
        }

        private void SetBenchButtonsEnabled(bool enabled)
        {
            foreach (Control control in _benchFlow.Controls)
            {
                var button = control as Button;
                if (button != null) button.Enabled = enabled;
            }
        }

        private void ApplyPlayers(LeagueLiveSnapshot snapshot)
        {
            _playersList.BeginUpdate();
            try
            {
                _playersList.Items.Clear();
                foreach (var row in snapshot.Players)
                {
                    var side = FormatSide(row.Side);
                    var player = string.IsNullOrWhiteSpace(row.AccountName)
                        ? (row.CellId >= 0 ? "#" + row.CellId : _ui.Get(UiTextKeys.LeagueLiveUnknown))
                        : row.AccountName;
                    if (row.IsLocalPlayer) player = _ui.Get(UiTextKeys.LeagueLiveLocalPlayer) + " · " + player;
                    var position = string.IsNullOrWhiteSpace(row.Position) ? row.Role : row.Position;
                    if (string.IsNullOrWhiteSpace(position)) position = _ui.Get(UiTextKeys.LeagueLiveUnknown);
                    var champion = row.ChampionId > 0 ? row.ChampionId.ToString() : _ui.Get(UiTextKeys.LeagueLiveUnknown);
                    var intent = row.ChampionPickIntent > 0 ? row.ChampionPickIntent.ToString() : _ui.Get(UiTextKeys.LeagueLiveUnknown);
                    var spells = row.Spell1Id > 0 || row.Spell2Id > 0
                        ? row.Spell1Id + " / " + row.Spell2Id
                        : _ui.Get(UiTextKeys.LeagueLiveUnknown);
                    var item = new ListViewItem(new[] { side, player, position, champion, intent, spells });
                    if (row.IsLocalPlayer) item.ForeColor = Color.FromArgb(104, 218, 169);
                    _playersList.Items.Add(item);
                }
            }
            finally
            {
                _playersList.EndUpdate();
            }
        }

        private void ApplyWaitingState(bool resetPhase = true)
        {
            _lastActivity = LeagueActivityLevel.None;
            if (resetPhase)
                _phaseLabel.Text = _ui.Get(UiTextKeys.LeagueLivePhase) + ": " + _ui.Get(UiTextKeys.LeagueLiveUnknown);
            _summaryLabel.Text = _ui.Get(UiTextKeys.LeagueLiveWaiting);
            _detailLabel.Text = string.Empty;
            _bansLabel.Text = string.Empty;
            _statusLabel.Text = _ui.Get(UiTextKeys.LeagueLiveReadOnly);
            _playersList.Items.Clear();
            ApplyBenchState(null);
        }

        private bool IsEffectivelyMinimized()
        {
            if (WindowState == FormWindowState.Minimized) return true;
            var topLevel = TopLevelControl as Form;
            return topLevel != null && topLevel != this && topLevel.WindowState == FormWindowState.Minimized;
        }

        private static int ResolveLocalChampion(LeagueLiveSnapshot snapshot)
        {
            if (snapshot == null) return 0;
            var local = snapshot.Players.FirstOrDefault(row => row != null && row.IsLocalPlayer);
            return local == null ? 0 : local.ChampionId;
        }

        private string FormatSide(string side)
        {
            if (string.Equals(side, "ally", StringComparison.Ordinal)) return _ui.Get(UiTextKeys.LeagueLiveAlly);
            if (string.Equals(side, "enemy", StringComparison.Ordinal)) return _ui.Get(UiTextKeys.LeagueLiveEnemy);
            if (string.Equals(side, "team-1", StringComparison.Ordinal)) return _ui.Get(UiTextKeys.LeagueLiveTeamOne);
            if (string.Equals(side, "team-2", StringComparison.Ordinal)) return _ui.Get(UiTextKeys.LeagueLiveTeamTwo);
            return _ui.Get(UiTextKeys.LeagueLiveUnknown);
        }

        private string FormatAction(LeagueLiveSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.LocalActionType)) return _ui.Get(UiTextKeys.LeagueLiveUnknown);
            return snapshot.LocalActionChampionId > 0
                ? snapshot.LocalActionType + " " + snapshot.LocalActionChampionId
                : snapshot.LocalActionType;
        }

        private static string JoinInts(IEnumerable<int> values)
        {
            var materialized = values == null ? new int[0] : values.Where(value => value > 0).ToArray();
            return materialized.Length == 0 ? "--" : string.Join(", ", materialized);
        }

        private string Value(long value)
        {
            return value > 0 ? value.ToString() : _ui.Get(UiTextKeys.LeagueLiveUnknown);
        }
    }
}
