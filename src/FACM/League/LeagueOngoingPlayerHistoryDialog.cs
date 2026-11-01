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
    // Filters only verified, already loaded player-card evidence; paging always keeps its source cursor.
    internal static class LeagueOngoingHistoryFilter
    {
        private static readonly string[] Roles = { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

        internal static IReadOnlyList<LeaguePlayerMatchSummary> Select(
            IList<LeaguePlayerMatchSummary> matches, string puuid, int championId, string position)
        {
            if (matches == null || matches.Count > 200 ||
                !LeagueDynamicMessageService.SafePuuid(puuid) ||
                championId < 0 || championId > 9999 ||
                (!string.IsNullOrEmpty(position) && !Roles.Contains(position, StringComparer.Ordinal)))
                return new LeaguePlayerMatchSummary[0];
            return matches.Where(m => m != null && m.GameId > 0 && m.ChampionId > 0 &&
                    (string.IsNullOrEmpty(m.PuuId) || string.Equals(m.PuuId, puuid, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(m.GameType) ||
                     string.Equals(m.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase)) &&
                    LeaguePlayerHistoryAnalysis.Outcome(m) != LeagueHistoryOutcome.Unknown)
                .GroupBy(m => m.GameId).Where(g => g.Count() == 1).Select(g => g.First())
                .Where(m => (championId == 0 || m.ChampionId == championId) &&
                    (string.IsNullOrEmpty(position) ||
                     string.Equals(m.Position, position, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(m => m.GameCreationEpochMs).ThenByDescending(m => m.GameId)
                .ToArray();
        }

        internal static bool NeedsCollection(IList<LeaguePlayerMatchSummary> matches, string puuid,
            int championId, string position, int nextIndex, int requests)
        {
            return (championId > 0 || !string.IsNullOrEmpty(position)) &&
                LeagueDynamicMessageService.SafePuuid(puuid) &&
                championId >= 0 && championId <= 9999 &&
                (string.IsNullOrEmpty(position) || Roles.Contains(position, StringComparer.Ordinal)) &&
                nextIndex >= 0 && nextIndex < 200 && requests >= 0 && requests < 5 &&
                matches != null && matches.Count <= 200 &&
                Select(matches, puuid, championId, position).Count < 20;
        }

        internal static int[] Champions(IList<LeaguePlayerMatchSummary> matches, string puuid)
        {
            return Select(matches, puuid, 0, "").GroupBy(m => m.ChampionId)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => g.Key).ToArray();
        }

        internal static string[] Positions(IList<LeaguePlayerMatchSummary> matches, string puuid)
        {
            var rows = Select(matches, puuid, 0, "");
            return Roles.Where(role => rows.Any(m =>
                string.Equals(m.Position, role, StringComparison.OrdinalIgnoreCase))).ToArray();
        }
    }

    internal sealed class LeagueOngoingHistoryFilterOption
    {
        internal int ChampionId;
        internal string Position;
        internal string Label;
        public override string ToString() { return Label ?? ""; }
    }

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
            int autoPages = 0, Func<int, string, Form> openFullCollection = null)
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
            Width = 780; Height = 490; MinimumSize = new Size(520, 350);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 4, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var matches = player.RosterCardHistory != null &&
                player.RosterCardNextIndex >= snapshot.HistoryLoadCount
                ? player.RosterCardHistory.ToList()
                : LeagueHistoricalMatchDryRun.CardRecentMatches(player).ToList();
            var selectedChampion = 0;
            var selectedPosition = "";
            Func<IReadOnlyList<LeaguePlayerMatchSummary>> visibleMatches = () =>
                LeagueOngoingHistoryFilter.Select(matches, player.Player.PuuId,
                    selectedChampion, selectedPosition);
            Func<string> summaryText = () =>
            {
                var visible = visibleMatches();
                return t(LeagueEfficiencyUiTextKeys.OngoingRecentSummary)
                    .Replace("{0}", visible.Count.ToString(CultureInfo.CurrentCulture))
                    .Replace("{1}", visible.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) ==
                        LeagueHistoryOutcome.Win).ToString(CultureInfo.CurrentCulture))
                    .Replace("{2}", visible.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) ==
                        LeagueHistoryOutcome.Loss).ToString(CultureInfo.CurrentCulture));
            };
            var summary = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = summaryText() };
            var stats = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted };
            var statsTip = new ToolTip();
            Action refreshStats = () =>
            {
                var analysis = LeagueHistoricalMatchDryRun.CardAnalysis(visibleMatches(), player.Player.PuuId);
                if (analysis == null)
                {
                    stats.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
                    return;
                }
                var detail = t(LeagueEfficiencyUiTextKeys.OngoingCardStats)
                    .Replace("{0}", analysis.WinRate.HasValue
                        ? analysis.WinRate.Value.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "—")
                    .Replace("{1}", analysis.Wins.ToString(CultureInfo.CurrentCulture))
                    .Replace("{2}", analysis.Losses.ToString(CultureInfo.CurrentCulture))
                    .Replace("{3}", analysis.Kda.HasValue
                        ? analysis.Kda.Value.ToString("0.00", CultureInfo.CurrentCulture) : "—")
                    .Replace("{4}", analysis.StatisticsGames.ToString(CultureInfo.CurrentCulture));
                if (analysis.ArenaPlacements > 0)
                    detail += " · " + t(LeagueEfficiencyUiTextKeys.OngoingCardArenaFirst)
                        .Replace("{0}", analysis.ArenaFirstPlaces.ToString(CultureInfo.CurrentCulture))
                        .Replace("{1}", analysis.ArenaPlacements.ToString(CultureInfo.CurrentCulture));
                if (analysis.MostPlayedPositions.Count > 0)
                    detail += " · " + t(LeagueEfficiencyUiTextKeys.OngoingCardPositions)
                        .Replace("{0}", string.Join(" / ", analysis.MostPlayedPositions
                            .Select(p => p.Key + " " + p.Value.ToString(CultureInfo.CurrentCulture))));
                stats.Text = detail;
                var expanded = detail;
                if (analysis.KdaCv.HasValue)
                    expanded += Environment.NewLine + "KDA CV: " +
                        analysis.KdaCv.Value.ToString("0.00", CultureInfo.CurrentCulture);
                stats.AccessibleDescription = expanded;
                statsTip.SetToolTip(stats, expanded);
            };
            refreshStats();
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
            var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            var championLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingCardFilterChampion) };
            var positionLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingCardFilterPosition) };
            var championChoice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            var positionChoice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            filters.Controls.Add(championLabel, 0, 0);
            filters.Controls.Add(championChoice, 1, 0);
            filters.Controls.Add(positionLabel, 2, 0);
            filters.Controls.Add(positionChoice, 3, 0);
            Action updateCollect = null;
            Action refreshRows = () =>
            {
                rows.BeginUpdate();
                try
                {
                    rows.Items.Clear();
                    foreach (var match in visibleMatches()) appendMatch(match);
                }
                finally { rows.EndUpdate(); }
                summary.Text = summaryText();
                refreshStats();
                if (updateCollect != null) updateCollect();
            };
            var updatingChoices = false;
            Action rebuildChoices = () =>
            {
                updatingChoices = true;
                try
                {
                    championChoice.Items.Clear();
                    championChoice.Items.Add(new LeagueOngoingHistoryFilterOption
                    { ChampionId = 0, Label = t(LeagueEfficiencyUiTextKeys.OngoingCardFilterAll) });
                    var verifiedChoices = LeagueOngoingHistoryFilter.Select(
                        matches, player.Player.PuuId, 0, "");
                    foreach (var id in LeagueOngoingHistoryFilter.Champions(matches, player.Player.PuuId))
                    {
                        var row = verifiedChoices.First(m => m.ChampionId == id);
                        championChoice.Items.Add(new LeagueOngoingHistoryFilterOption
                        {
                            ChampionId = id,
                            Label = string.IsNullOrWhiteSpace(row.ChampionName)
                                ? "#" + id.ToString(CultureInfo.InvariantCulture) : row.ChampionName
                        });
                    }
                    championChoice.SelectedIndex = 0;
                    for (var i = 0; i < championChoice.Items.Count; i++)
                        if (((LeagueOngoingHistoryFilterOption)championChoice.Items[i]).ChampionId == selectedChampion)
                            championChoice.SelectedIndex = i;
                    positionChoice.Items.Clear();
                    positionChoice.Items.Add(new LeagueOngoingHistoryFilterOption
                    { Position = "", Label = t(LeagueEfficiencyUiTextKeys.OngoingCardFilterAll) });
                    foreach (var role in LeagueOngoingHistoryFilter.Positions(matches, player.Player.PuuId))
                        positionChoice.Items.Add(new LeagueOngoingHistoryFilterOption
                        { Position = role, Label = LeaguePlayerHistoryAnalysis.PositionName(ui, role) });
                    positionChoice.SelectedIndex = 0;
                    for (var i = 0; i < positionChoice.Items.Count; i++)
                        if (((LeagueOngoingHistoryFilterOption)positionChoice.Items[i]).Position == selectedPosition)
                            positionChoice.SelectedIndex = i;
                }
                finally { updatingChoices = false; }
            };
            championChoice.SelectedIndexChanged += (sender, args) =>
            {
                if (updatingChoices) return;
                selectedChampion = ((LeagueOngoingHistoryFilterOption)championChoice.SelectedItem).ChampionId;
                refreshRows();
            };
            positionChoice.SelectedIndexChanged += (sender, args) =>
            {
                if (updatingChoices) return;
                selectedPosition = ((LeagueOngoingHistoryFilterOption)positionChoice.SelectedItem).Position;
                refreshRows();
            };
            rebuildChoices();
            refreshRows();
            var loading = false;
            var nextIndex = player.RosterCardHistory != null &&
                player.RosterCardNextIndex >= snapshot.HistoryLoadCount
                ? player.RosterCardNextIndex : snapshot.HistoryLoadCount;
            var hasMore = nextIndex < 200 && !player.RosterCardExhausted;
            var more = new Button { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingHistoryLoadMore), Enabled = hasMore };
            var collect = new Button { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingCardCollect), Enabled = false };
            var collecting = false;
            var fullCollect = new Button { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingCardCollect) + " · 1000" };
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            header.Controls.Add(summary, 0, 0);
            header.Controls.Add(more, 1, 0);
            header.Controls.Add(collect, 2, 0);
            header.Controls.Add(fullCollect, 3, 0);
            updateCollect = () =>
            {
                collect.Enabled = !collecting && !loading && hasMore && isCurrent() &&
                    LeagueOngoingHistoryFilter.NeedsCollection(matches, player.Player.PuuId,
                        selectedChampion, selectedPosition, nextIndex, 0);
                fullCollect.Enabled = !collecting && !loading && isCurrent() &&
                    openFullCollection != null &&
                    (selectedChampion > 0 || !string.IsNullOrEmpty(selectedPosition));
            };
            fullCollect.Click += (sender, args) =>
            {
                if (!fullCollect.Enabled || !isCurrent()) return;
                using (var dialog = openFullCollection(selectedChampion, selectedPosition))
                    if (dialog != null) dialog.ShowDialog(this);
                if (!IsDisposed) updateCollect();
            };
            updateCollect();
            Func<CancellationToken, Task<bool>> fetchNext = async requestToken =>
            {
                if (loading || !hasMore || !isCurrent()) return false;
                loading = true; more.Enabled = false; collect.Enabled = false;
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
                    foreach (var match in additions) matches.Add(match);
                    hasMore = additions.Count > 0 && nextIndex < 200;
                    player.RosterCardHistory = matches.ToList();
                    player.RosterCardNextIndex = nextIndex;
                    player.RosterCardExhausted = !hasMore;
                    rebuildChoices();
                    refreshRows();
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
                    if (!IsDisposed)
                    {
                        more.Enabled = hasMore && nextIndex < 200 && isCurrent() && !collecting;
                        updateCollect();
                    }
                }
            };
            more.Click += async (sender, args) => await fetchNext(_lifetime.Token);
            collect.Click += async (sender, args) =>
            {
                if (collecting || loading || !isCurrent()) return;
                collecting = true;
                more.Enabled = false;
                updateCollect();
                try
                {
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                    {
                        deadline.CancelAfter(TimeSpan.FromSeconds(60));
                        for (var requests = 0; requests < 5; requests++)
                        {
                            if (deadline.IsCancellationRequested ||
                                !LeagueOngoingHistoryFilter.NeedsCollection(matches, player.Player.PuuId,
                                    selectedChampion, selectedPosition, nextIndex, requests) ||
                                !hasMore || !await fetchNext(deadline.Token)) break;
                        }
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    collecting = false;
                    if (!IsDisposed)
                    {
                        more.Enabled = hasMore && !loading && isCurrent();
                        updateCollect();
                    }
                }
            };
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
            layout.Controls.Add(filters, 0, 1);
            layout.Controls.Add(stats, 0, 2);
            layout.Controls.Add(rows, 0, 3);
            Controls.Add(layout);
            Shown += async (sender, args) =>
            {
                if (!isCurrent()) { Close(); return; }
                if (autoPages == 0) return;
                // Auto-load only after the user opens this card, never during roster polling.
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(95));
                    foreach (var offset in LeagueHistoricalMatchDryRun.AutomaticCardPageOffsets(
                        snapshot.HistoryLoadCount, autoPages).Where(offset => offset >= nextIndex))
                    {
                        if (!hasMore || deadline.IsCancellationRequested || nextIndex != offset ||
                            !await fetchNext(deadline.Token)) break;
                    }
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
