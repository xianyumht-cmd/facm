using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueRemotePlayerHistoryForm : Form
    {
        private readonly LeaguePlayerDataService _service;
        private readonly UiTextCatalog _ui;
        private readonly LeaguePlayerProfile _expected;
        private readonly string _puuid;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Label _playerLabel;
        private readonly Label _statusLabel;
        private readonly ListView _matches;

        internal LeagueRemotePlayerHistoryForm(LeaguePlayerDataService service, UiTextCatalog ui,
            LeaguePlayerProfile current, string targetPuuid, string targetName)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _expected = new LeaguePlayerProfile { PuuId = current == null ? null : current.PuuId };
            _puuid = targetPuuid;

            Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteHistoryTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 500);
            MinimumSize = new Size(650, 380);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            _playerLabel = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, Text = targetName ?? string.Empty,
                ForeColor = FacmDesignSystem.Text, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(FacmThemeRuntime.Current.FontName, 13F, FontStyle.Bold)
            };
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = FacmDesignSystem.TextMuted,
                Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLoading)
            };
            _matches = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, GridLines = false,
                BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text
            };
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerTime), 120);
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerMode), 150);
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerChampion), 135);
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerKda), 130);
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerResult), 90);
            _matches.Resize += (sender, args) => FitMatchColumns();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 20),
                ColumnCount = 1, RowCount = 3, BackColor = FacmDesignSystem.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(_playerLabel, 0, 0);
            layout.Controls.Add(_statusLabel, 0, 1);
            layout.Controls.Add(_matches, 0, 2);
            Controls.Add(layout);

            Shown += async (sender, args) => { FitMatchColumns(); await LoadAsync(); };
            FormClosing += (sender, args) => _lifetime.Cancel();
            Disposed += (sender, args) => _lifetime.Dispose();
        }

        internal static int[] ResolveColumnWidthsForSmokeTest(int width)
        {
            var content = Math.Max(450, width);
            var values = new[] { 19, 24, 22, 21 };
            var result = new int[5];
            var used = 0;
            for (var index = 0; index < values.Length; index++)
            {
                result[index] = content * values[index] / 100;
                used += result[index];
            }
            result[4] = content - used;
            return result;
        }

        private void FitMatchColumns()
        {
            if (_matches == null || _matches.Columns.Count != 5) return;
            var widths = ResolveColumnWidthsForSmokeTest(_matches.ClientSize.Width -
                SystemInformation.VerticalScrollBarWidth - 4);
            for (var index = 0; index < widths.Length; index++)
                _matches.Columns[index].Width = widths[index];
        }

        private async Task LoadAsync()
        {
            try
            {
                var result = await _service.LoadRemotePlayerHistoryAsync(_expected, _puuid, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (result.Status != LeagueRiotPlayerSearchStatus.Found)
                {
                    _statusLabel.Text = _ui.Get(result.Status == LeagueRiotPlayerSearchStatus.AccountChanged
                        ? UiTextKeys.LeaguePlayerRemoteAccountChanged
                        : result.Status == LeagueRiotPlayerSearchStatus.NotFound
                            ? UiTextKeys.LeaguePlayerRemoteHistoryNotFound : UiTextKeys.LeaguePlayerRemoteHistoryUnavailable);
                    return;
                }
                var profile = result.Profile;
                if (profile != null)
                {
                    _playerLabel.Text = string.IsNullOrWhiteSpace(profile.AccountName) ? _playerLabel.Text : profile.AccountName;
                    if (profile.SummonerLevel > 0)
                        _playerLabel.Text += "  ·  " + _ui.Get(UiTextKeys.LeagueDashboardLevel) + " " + profile.SummonerLevel;
                }

                var page = result.Page;
                _matches.BeginUpdate();
                try
                {
                    _matches.Items.Clear();
                    foreach (var match in page.Matches)
                    {
                        if (match == null) continue;
                        var when = match.GameCreationLocal == DateTime.MinValue
                            ? "—" : match.GameCreationLocal.ToString("MM-dd HH:mm");
                        var champion = match.ChampionId > 0
                            ? (string.IsNullOrWhiteSpace(match.ChampionName) ? "#" + match.ChampionId : match.ChampionName) : "—";
                        var kda = match.ParticipantResolved && match.StatisticsAvailable
                            ? match.Kills + "/" + match.Deaths + "/" + match.Assists : "—";
                        var outcome = match.ParticipantResolved && match.StatisticsAvailable
                            ? _ui.Get(match.Win ? UiTextKeys.LeaguePlayerWin : UiTextKeys.LeaguePlayerLoss) : "—";
                        var row = new ListViewItem(when);
                        row.SubItems.Add(string.IsNullOrWhiteSpace(match.GameMode) ? "—" : match.GameMode);
                        row.SubItems.Add(champion);
                        row.SubItems.Add(kda);
                        row.SubItems.Add(outcome);
                        _matches.Items.Add(row);
                    }
                }
                finally { _matches.EndUpdate(); }
                _statusLabel.Text = _matches.Items.Count == 0
                    ? _ui.Get(UiTextKeys.LeaguePlayerRemoteNoMatches)
                    : _ui.Get(UiTextKeys.LeaguePlayerRemoteRecentCount)
                        .Replace("{0}", _matches.Items.Count.ToString());
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed) _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteHistoryUnavailable);
            }
        }
    }
}
