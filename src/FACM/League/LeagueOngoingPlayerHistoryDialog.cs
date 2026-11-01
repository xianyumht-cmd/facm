using System;
using System.Drawing;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    // Bounded, local player-card history; the existing dynamic-message owner supplies all data.
    internal sealed class LeagueOngoingPlayerHistoryDialog : Form
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        internal LeagueOngoingPlayerHistoryDialog(UiTextCatalog ui,
            LeagueDynamicMessageSnapshot snapshot, LeagueDynamicMessagePlayer player,
            Func<bool> isCurrent,
            Func<LeaguePlayerMatchSummary, CancellationToken, Task<LeagueEncounterMatchDetailResult>> loadDetail,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset,
            Func<int, CancellationToken, Task<IReadOnlyList<LeaguePlayerMatchSummary>>> loadMore,
            int autoPages = 0)
        {
            if (ui == null || snapshot == null || player?.Player == null ||
                isCurrent == null || loadDetail == null || loadAsset == null || loadMore == null ||
                !snapshot.Players.Contains(player) ||
                !LeagueDynamicMessageService.SafePuuid(player.Player.PuuId) ||
                autoPages < 0 || autoPages > 10)
                throw new ArgumentException("A current player-card snapshot is required.");
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.OngoingRecentHistory) + " · " +
                (player.Player.GameName ?? ("#" + player.Player.CellId));
            Width = 720; Height = 490; MinimumSize = new Size(480, 350);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 2, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var matches = LeagueHistoricalMatchDryRun.CardRecentMatches(player).ToList();
            Func<string> summaryText = () => t(LeagueEfficiencyUiTextKeys.OngoingRecentSummary)
                .Replace("{0}", matches.Count.ToString(CultureInfo.CurrentCulture))
                .Replace("{1}", matches.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) ==
                    LeagueHistoryOutcome.Win).ToString(CultureInfo.CurrentCulture))
                .Replace("{2}", matches.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) ==
                    LeagueHistoryOutcome.Loss).ToString(CultureInfo.CurrentCulture));
            var summary = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = summaryText() };
            var rows = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text };
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftGame), 120);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 165);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon) + " / " +
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost), 100);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftKda), 150);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicPositions), 115);
            Action<LeaguePlayerMatchSummary> appendMatch = match =>
            {
                var outcome = LeaguePlayerHistoryAnalysis.Outcome(match);
                var row = new ListViewItem(match.GameId.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(string.IsNullOrWhiteSpace(match.ChampionName)
                    ? "#" + match.ChampionId : match.ChampionName);
                row.SubItems.Add(match.GameMode == "CHERRY" &&
                    LeagueCherryTeams.ValidPlacement(match.SubteamPlacement).HasValue
                    ? t(LeagueEfficiencyUiTextKeys.OngoingRecentPlacement).Replace("{0}",
                        match.SubteamPlacement.Value.ToString(CultureInfo.CurrentCulture))
                    : t(outcome == LeagueHistoryOutcome.Win
                        ? LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon :
                        outcome == LeagueHistoryOutcome.Loss
                        ? LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost :
                        outcome == LeagueHistoryOutcome.Remake
                        ? LeagueEfficiencyUiTextKeys.OngoingRecentRemake :
                        LeagueEfficiencyUiTextKeys.OngoingRecentAbort));
                row.SubItems.Add(match.StatisticsAvailable && match.ParticipantResolved
                    ? match.Kills + " / " + match.Deaths + " / " + match.Assists : "—");
                row.SubItems.Add(string.IsNullOrWhiteSpace(match.Position) ? "—" : match.Position);
                row.Tag = match;
                rows.Items.Add(row);
            };
            foreach (var match in matches) appendMatch(match);
            var loading = false;
            var nextIndex = snapshot.HistoryLoadCount;
            var hasMore = nextIndex < 200;
            var more = new Button { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingHistoryLoadMore), Enabled = hasMore };
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            header.Controls.Add(summary, 0, 0);
            header.Controls.Add(more, 1, 0);
            Func<CancellationToken, Task<bool>> fetchNext = async requestToken =>
            {
                if (loading || !hasMore || !isCurrent()) return false;
                loading = true; more.Enabled = false;
                try
                {
                    var page = await loadMore(nextIndex, requestToken);
                    if (IsDisposed || _lifetime.IsCancellationRequested || !isCurrent()) return false;
                    if (page == null)
                    {
                        summary.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
                        return false;
                    }
                    var additions = LeagueHistoricalMatchDryRun.CardPageAdditions(
                        matches, page, player.Player.PuuId);
                    if (additions == null)
                    {
                        summary.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
                        return false;
                    }
                    nextIndex += Math.Min(20, 200 - nextIndex);
                    foreach (var match in additions)
                    {
                        matches.Add(match);
                        appendMatch(match);
                    }
                    hasMore = additions.Count > 0 && nextIndex < 200;
                    summary.Text = summaryText();
                    return page.Count > 0;
                }
                catch (OperationCanceledException) { return false; }
                catch (Exception)
                {
                    if (!IsDisposed)
                        summary.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
                    return false;
                }
                finally
                {
                    loading = false;
                    if (!IsDisposed) more.Enabled = hasMore && nextIndex < 200 && isCurrent();
                }
            };
            more.Click += async (sender, args) => await fetchNext(_lifetime.Token);
            rows.DoubleClick += async (sender, args) =>
            {
                if (loading || !isCurrent() || rows.SelectedItems.Count != 1) return;
                var match = rows.SelectedItems[0].Tag as LeaguePlayerMatchSummary;
                if (match == null) return;
                loading = true;
                summary.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLoading);
                try
                {
                    var detail = await loadDetail(match, _lifetime.Token);
                    if (IsDisposed || !isCurrent() || _lifetime.IsCancellationRequested) return;
                    if (detail?.Status != LeagueEncounterMatchDetailStatus.Ready)
                    {
                        summary.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
                        return;
                    }
                    if (detail.EogOnly != null)
                    {
                        using (var dialog = new LeagueSgpEogOnlyDialog(ui, detail))
                            dialog.ShowDialog(this);
                    }
                    else if (detail.Participants != null && detail.Participants.Count > 0)
                    {
                        using (var dialog = new LeagueEncounterMatchScoreboardDialog(ui,
                            detail, loadAsset))
                            dialog.ShowDialog(this);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    if (!IsDisposed)
                        summary.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
                }
                finally
                {
                    loading = false;
                    if (!IsDisposed && isCurrent())
                        summary.Text = summaryText();
                }
            };
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(rows, 0, 1);
            Controls.Add(layout);
            Shown += async (sender, args) =>
            {
                if (!isCurrent()) { Close(); return; }
                if (autoPages == 0) return;
                // Auto-load only after the user opens this card, never during roster polling.
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(95));
                    for (var i = 0; i < autoPages && hasMore && !deadline.IsCancellationRequested; i++)
                        if (!await fetchNext(deadline.Token)) break;
                }
            };
            Activated += (sender, args) => { if (!isCurrent()) Close(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _lifetime.Cancel(); _lifetime.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
