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
            IList<LeaguePlayerMatchSummary> matches, string puuid, int championId, string position,
            string mode = "")
        {
            if (matches == null || matches.Count > 200 ||
                !LeagueDynamicMessageService.SafePuuid(puuid) ||
                championId < 0 || championId > 9999 ||
                (mode != "" && mode != "NORMAL" && mode != "CHERRY") ||
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
                     string.Equals(m.Position, position, StringComparison.OrdinalIgnoreCase)) &&
                    (mode == "" || (!string.IsNullOrWhiteSpace(m.GameMode) &&
                     !string.Equals(m.GameMode, "PRACTICETOOL", StringComparison.OrdinalIgnoreCase) &&
                     (mode == "CHERRY") ==
                     string.Equals(m.GameMode, "CHERRY", StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(m => m.GameCreationEpochMs).ThenByDescending(m => m.GameId)
                .ToArray();
        }

        internal static bool NeedsCollection(IList<LeaguePlayerMatchSummary> matches, string puuid,
            int championId, string position, int nextIndex, int requests, string mode = "")
        {
            return (championId > 0 || !string.IsNullOrEmpty(position) || mode != "") &&
                LeagueDynamicMessageService.SafePuuid(puuid) &&
                championId >= 0 && championId <= 9999 &&
                (mode == "" || mode == "NORMAL" || mode == "CHERRY") &&
                (string.IsNullOrEmpty(position) || Roles.Contains(position, StringComparer.Ordinal)) &&
                nextIndex >= 0 && nextIndex < 200 && requests >= 0 && requests < 5 &&
                matches != null && matches.Count <= 200 &&
                Select(matches, puuid, championId, position, mode).Count < 20;
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


    internal sealed class LeagueOngoingCardBreakdownRow
    {
        internal string Kind, Label, Mode, Position;
        internal int ChampionId;
        internal LeagueHistoricalCardAnalysis Analysis;
    }

    // The popup never initiates history reads: every row comes from the current verified card.
    internal static class LeagueOngoingCardBreakdown
    {
        internal static IReadOnlyList<LeagueOngoingCardBreakdownRow> Project(
            IList<LeaguePlayerMatchSummary> matches, string puuid)
        {
            if (matches == null || matches.Count > 200 ||
                !LeagueDynamicMessageService.SafePuuid(puuid))
                return new LeagueOngoingCardBreakdownRow[0];
            var verified = LeagueOngoingHistoryFilter.Select(matches, puuid, 0, "");
            var result = new List<LeagueOngoingCardBreakdownRow>();
            foreach (var group in verified.GroupBy(m => m.ChampionId)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            {
                var name = group.Select(m => m.ChampionName)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var safeName = new string((name ?? "")
                    .Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
                result.Add(new LeagueOngoingCardBreakdownRow {
                    Kind = "champion", ChampionId = group.Key,
                    Label = string.IsNullOrWhiteSpace(safeName) ? "#" + group.Key : safeName,
                    Analysis = LeagueHistoricalMatchDryRun.CardAnalysis(group.ToArray(), puuid)
                });
            }
            foreach (var group in verified.Where(m => new[] {
                "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"
            }.Contains(m.Position, StringComparer.Ordinal)).GroupBy(m => m.Position)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
                result.Add(new LeagueOngoingCardBreakdownRow {
                    Kind = "position", Position = group.Key, Label = group.Key,
                    Analysis = LeagueHistoricalMatchDryRun.CardAnalysis(group.ToArray(), puuid)
                });
            foreach (var group in verified.Where(m =>
                !string.IsNullOrWhiteSpace(m.GameMode) &&
                !string.Equals(m.GameMode, "PRACTICETOOL", StringComparison.OrdinalIgnoreCase))
                .GroupBy(m => string.Equals(m.GameMode, "CHERRY", StringComparison.OrdinalIgnoreCase)
                    ? "CHERRY" : "NORMAL").OrderBy(g => g.Key, StringComparer.Ordinal))
                result.Add(new LeagueOngoingCardBreakdownRow {
                    Kind = "mode", Mode = group.Key, Label = group.Key,
                    Analysis = LeagueHistoricalMatchDryRun.CardAnalysis(group.ToArray(), puuid)
                });

            var roles = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
            foreach (var group in verified.Where(m => roles.Contains(m.Position, StringComparer.Ordinal))
                .GroupBy(m => new { m.ChampionId, m.Position })
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.ChampionId)
                .ThenBy(g => g.Key.Position, StringComparer.Ordinal))
            {
                var name = group.Select(m => m.ChampionName)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var safeName = new string((name ?? "")
                    .Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
                result.Add(new LeagueOngoingCardBreakdownRow {
                    Kind = "champion-position", ChampionId = group.Key.ChampionId,
                    Position = group.Key.Position,
                    Label = string.IsNullOrWhiteSpace(safeName) ? "#" + group.Key.ChampionId : safeName,
                    Analysis = LeagueHistoricalMatchDryRun.CardAnalysis(group.ToArray(), puuid)
                });
            }
            var modes = verified.Where(m => !string.IsNullOrWhiteSpace(m.GameMode) &&
                !string.Equals(m.GameMode, "PRACTICETOOL", StringComparison.OrdinalIgnoreCase))
                .Select(m => new { Match = m, Mode = string.Equals(m.GameMode, "CHERRY",
                    StringComparison.OrdinalIgnoreCase) ? "CHERRY" : "NORMAL" }).ToArray();
            foreach (var group in modes.GroupBy(m => new { m.Match.ChampionId, m.Mode })
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.ChampionId)
                .ThenBy(g => g.Key.Mode, StringComparer.Ordinal))
            {
                var name = group.Select(m => m.Match.ChampionName)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var safeName = new string((name ?? "")
                    .Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
                result.Add(new LeagueOngoingCardBreakdownRow {
                    Kind = "champion-mode", ChampionId = group.Key.ChampionId,
                    Mode = group.Key.Mode,
                    Label = string.IsNullOrWhiteSpace(safeName) ? "#" + group.Key.ChampionId : safeName,
                    Analysis = LeagueHistoricalMatchDryRun.CardAnalysis(group.Select(m => m.Match).ToArray(), puuid)
                });
            }
            foreach (var group in modes.Where(m => roles.Contains(m.Match.Position, StringComparer.Ordinal))
                .GroupBy(m => new { m.Match.Position, m.Mode })
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Position, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Mode, StringComparer.Ordinal))
                result.Add(new LeagueOngoingCardBreakdownRow {
                    Kind = "position-mode", Position = group.Key.Position, Mode = group.Key.Mode,
                    Label = group.Key.Position,
                    Analysis = LeagueHistoricalMatchDryRun.CardAnalysis(group.Select(m => m.Match).ToArray(), puuid)
                });
            foreach (var group in modes.Where(m => roles.Contains(m.Match.Position, StringComparer.Ordinal))
                .GroupBy(m => new { m.Match.ChampionId, m.Match.Position, m.Mode })
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.ChampionId)
                .ThenBy(g => g.Key.Position, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Mode, StringComparer.Ordinal))
            {
                var name = group.Select(m => m.Match.ChampionName)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var safeName = new string((name ?? "")
                    .Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
                result.Add(new LeagueOngoingCardBreakdownRow {
                    Kind = "champion-position-mode", ChampionId = group.Key.ChampionId,
                    Position = group.Key.Position, Mode = group.Key.Mode,
                    Label = string.IsNullOrWhiteSpace(safeName) ? "#" + group.Key.ChampionId : safeName,
                    Analysis = LeagueHistoricalMatchDryRun.CardAnalysis(
                        group.Select(m => m.Match).ToArray(), puuid)
                });
            }
            return result.Where(row => row.Analysis != null).ToArray();
        }
    }

    internal sealed class LeagueOngoingCardBreakdownDialog : Form
    {
        internal bool HasSelection { get; private set; }
        internal int SelectedChampionId { get; private set; }
        internal string SelectedPosition { get; private set; }
        internal string SelectedMode { get; private set; }

        internal LeagueOngoingCardBreakdownDialog(UiTextCatalog ui,
            IList<LeaguePlayerMatchSummary> matches, string puuid)
        {
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.OngoingCardBreakdown);
            Width = 780; Height = 460; MinimumSize = new Size(540, 320);
            AutoScaleMode = AutoScaleMode.Font; StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var breakdown = LeagueOngoingCardBreakdown.Project(matches, puuid);
            foreach (var kind in new[] { "champion", "position", "mode",
                "champion-position", "champion-mode", "position-mode",
                "champion-position-mode" })
            {
                var championLabel = t(LeagueEfficiencyUiTextKeys.OngoingCardFilterChampion);
                var positionLabel = t(LeagueEfficiencyUiTextKeys.OngoingCardFilterPosition);
                var modeLabel = t(LeagueEfficiencyUiTextKeys.AdvancedMode);
                var label = kind == "champion" ? championLabel
                    : kind == "position" ? positionLabel
                    : kind == "mode" ? modeLabel
                    : kind == "champion-position" ? championLabel + " / " + positionLabel
                    : kind == "champion-mode" ? championLabel + " / " + modeLabel
                    : kind == "position-mode" ? positionLabel + " / " + modeLabel
                    : championLabel + " / " + positionLabel + " / " + modeLabel;
                var page = new TabPage(label) { BackColor = FacmDesignSystem.Canvas };
                var list = new ListView {
                    Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                    GridLines = true, BackColor = FacmDesignSystem.CanvasRaised,
                    ForeColor = FacmDesignSystem.Text
                };
                list.Columns.Add(label, 145);
                list.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftGame), 75);
                list.Columns.Add(t(LeagueEfficiencyUiTextKeys.OngoingCardWinRate), 95);
                list.Columns.Add("KDA", 85);
                list.Columns.Add("KDA CV", 85);
                list.Columns.Add(t(LeagueEfficiencyUiTextKeys.OngoingCardArenaCount), 115);
                foreach (var item in breakdown.Where(row => row.Kind == kind))
                {
                    var a = item.Analysis;
                    var modeName = item.Mode == "CHERRY"
                        ? t(LeagueEfficiencyUiTextKeys.ProfileModeCherry)
                        : t(LeagueEfficiencyUiTextKeys.ProfileModeNormal);
                    var title = kind == "mode" ? modeName
                        : kind == "position" ? LeaguePlayerHistoryAnalysis.PositionName(ui, item.Position)
                        : kind == "champion-position" ? item.Label + " / " +
                            LeaguePlayerHistoryAnalysis.PositionName(ui, item.Position)
                        : kind == "champion-mode" ? item.Label + " / " + modeName
                        : kind == "position-mode" ? LeaguePlayerHistoryAnalysis.PositionName(ui, item.Position) +
                            " / " + modeName
                        : kind == "champion-position-mode" ? item.Label + " / " +
                            LeaguePlayerHistoryAnalysis.PositionName(ui, item.Position) +
                            " / " + modeName : item.Label;
                    var row = new ListViewItem(title);
                    row.SubItems.Add(a.Games.ToString(CultureInfo.CurrentCulture));
                    row.SubItems.Add(a.WinRate.HasValue
                        ? a.WinRate.Value.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "—");
                    row.SubItems.Add(a.Kda.HasValue
                        ? a.Kda.Value.ToString("0.00", CultureInfo.CurrentCulture) : "—");
                    row.SubItems.Add(a.KdaCv.HasValue
                        ? a.KdaCv.Value.ToString("0.00", CultureInfo.CurrentCulture) : "—");
                    row.SubItems.Add(a.ArenaPlacements > 0
                        ? a.ArenaFirstPlaces + "/" + a.ArenaPlacements : "—");
                    row.Tag = item;
                    list.Items.Add(row);
                }
                Action selectGroup = () =>
                {
                    if (list.SelectedItems.Count != 1) return;
                    var selected = list.SelectedItems[0].Tag as LeagueOngoingCardBreakdownRow;
                    if (selected == null) return;
                    HasSelection = true;
                    SelectedChampionId = selected.Kind.StartsWith("champion", StringComparison.Ordinal)
                        ? selected.ChampionId : 0;
                    SelectedPosition = selected.Kind == "position" ||
                        selected.Kind == "champion-position" || selected.Kind == "position-mode" ||
                        selected.Kind == "champion-position-mode"
                        ? selected.Position : "";
                    SelectedMode = selected.Kind == "mode" ||
                        selected.Kind == "champion-mode" || selected.Kind == "position-mode" ||
                        selected.Kind == "champion-position-mode"
                        ? selected.Mode : "";
                    DialogResult = DialogResult.OK;
                    Close();
                };
                list.DoubleClick += (sender, args) => selectGroup();
                list.KeyDown += (sender, args) =>
                {
                    if (args.KeyCode != Keys.Enter) return;
                    selectGroup();
                    args.Handled = true;
                };
                page.Controls.Add(list);
                tabs.TabPages.Add(page);
            }
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2,
                Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.Controls.Add(tabs, 0, 0);
            var close = new Button { Dock = DockStyle.Right, Width = 100,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel),
                DialogResult = DialogResult.Cancel };
            root.Controls.Add(close, 0, 1);
            Controls.Add(root); CancelButton = close;
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

    internal sealed class LeagueOngoingCardMasteryRow
    {
        internal int ChampionId;
        internal string ChampionName;
        internal long Level;
        internal long Points;
    }

    // Project only the current player's explicitly fetched, source-verified mastery.
    internal static class LeagueOngoingCardMasteryProjection
    {
        internal static IReadOnlyList<LeagueOngoingCardMasteryRow> Project(
            LeagueDynamicMessagePlayer player, IList<LeaguePlayerMatchSummary> matches)
        {
            if (player?.Player == null || player.Mastery?.Masteries == null ||
                !LeagueDynamicMessageService.SafePuuid(player.Player.PuuId) ||
                player.Mastery.Masteries.Count > 8)
                return new LeagueOngoingCardMasteryRow[0];
            var names = LeagueOngoingHistoryFilter.Select(matches, player.Player.PuuId, 0, "")
                .GroupBy(match => match.ChampionId)
                .ToDictionary(group => group.Key, group => group
                    .Select(match => match.ChampionName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "");
            var seen = new HashSet<long>();
            var result = new List<LeagueOngoingCardMasteryRow>();
            foreach (var raw in player.Mastery.Masteries)
            {
                var id = LeagueMatchTimelineParser.Number(raw, "championId");
                var level = LeagueMatchTimelineParser.Number(raw, "championLevel");
                var points = LeagueMatchTimelineParser.Number(raw, "championPoints");
                var owner = LeagueMatchTimelineParser.Text(raw, "puuid");
                if (!id.HasValue || id.Value < 1 || id.Value > int.MaxValue ||
                    !seen.Add(id.Value) || !level.HasValue || level.Value < 0 ||
                    !points.HasValue || points.Value < 0 ||
                    (!string.IsNullOrEmpty(owner) &&
                     !string.Equals(owner, player.Player.PuuId, StringComparison.OrdinalIgnoreCase)))
                    return new LeagueOngoingCardMasteryRow[0];
                string name;
                names.TryGetValue((int)id.Value, out name);
                name = new string((name ?? "").Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
                result.Add(new LeagueOngoingCardMasteryRow {
                    ChampionId = (int)id.Value,
                    ChampionName = name.Length == 0 ? "#" + id.Value : name,
                    Level = level.Value,
                    Points = points.Value
                });
            }
            return result.OrderByDescending(row => row.Points)
                .ThenBy(row => row.ChampionId).ToArray();
        }
    }

    // An on-demand native player-card detail: existing verified champion usage and
    // explicitly requested mastery share one read-only, copyable surface.
    internal sealed class LeagueOngoingCardInsightsDialog : Form
    {
        internal int SelectedChampionId { get; private set; }
        internal int CollectChampionId { get; private set; }

        internal LeagueOngoingCardInsightsDialog(UiTextCatalog ui,
            LeagueDynamicMessagePlayer player, IList<LeaguePlayerMatchSummary> matches,
            bool canCollect)
        {
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = ui.Get(UiTextKeys.LeagueProfileMastery) + " · " +
                (player.Player.GameName ?? ("#" + player.Player.CellId));
            Width = 640; Height = 450; MinimumSize = new Size(450, 310);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var usagePage = new TabPage(ui.Get(UiTextKeys.LeaguePlayerAnalysisTopChampions)) {
                BackColor = FacmDesignSystem.Canvas
            };
            var usage = MakeList();
            usage.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 190);
            usage.Columns.Add(t(LeagueEfficiencyUiTextKeys.OngoingCardFilterAll), 90);
            usage.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon), 90);
            usage.Columns.Add("%", 80);
            foreach (var entry in LeagueOngoingCardBreakdown.Project(matches, player.Player.PuuId)
                .Where(entry => entry.Kind == "champion"))
            {
                var item = new ListViewItem(entry.Label) { Tag = entry.ChampionId };
                item.SubItems.Add(entry.Analysis.Games.ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(entry.Analysis.Wins.ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(entry.Analysis.WinRate.HasValue
                    ? entry.Analysis.WinRate.Value.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "—");
                usage.Items.Add(item);
            }
            usagePage.Controls.Add(usage);
            var masteryPage = new TabPage(ui.Get(UiTextKeys.LeagueProfileMastery)) {
                BackColor = FacmDesignSystem.Canvas
            };
            var mastery = MakeList();
            mastery.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 190);
            mastery.Columns.Add(ui.Get(UiTextKeys.LeagueProfileMasteryLevel), 110);
            mastery.Columns.Add(ui.Get(UiTextKeys.LeagueProfileMasteryPoints), 150);
            foreach (var entry in LeagueOngoingCardMasteryProjection.Project(player, matches))
            {
                var item = new ListViewItem(entry.ChampionName) { Tag = entry.ChampionId };
                item.SubItems.Add(entry.Level.ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(entry.Points.ToString("N0", CultureInfo.CurrentCulture));
                mastery.Items.Add(item);
            }
            if (mastery.Items.Count == 0)
                mastery.Items.Add(t(LeagueEfficiencyUiTextKeys.DynamicUnavailable));
            masteryPage.Controls.Add(mastery);
            var verifiedChampions = new HashSet<int>(
                LeagueOngoingHistoryFilter.Champions(matches, player.Player.PuuId));
            foreach (var list in new[] { usage, mastery })
            {
                Action<bool> select = collect =>
                {
                    if (list.SelectedItems.Count != 1) return;
                    var id = list.SelectedItems[0].Tag;
                    if (!(id is int) || (int)id <= 0) return;
                    if (collect && canCollect ||
                        !verifiedChampions.Contains((int)id) && canCollect)
                        CollectChampionId = (int)id;
                    else if (verifiedChampions.Contains((int)id))
                        SelectedChampionId = (int)id;
                    else return;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                list.DoubleClick += (sender, args) => select(true);
                list.KeyDown += (sender, args) =>
                {
                    if (args.KeyCode != Keys.Enter) return;
                    select(false);
                    args.Handled = true;
                };
            }
            tabs.TabPages.Add(usagePage); tabs.TabPages.Add(masteryPage);
            tabs.SelectedTab = masteryPage;
            Controls.Add(tabs);
        }

        private static ListView MakeList()
        {
            var list = new ListView {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                GridLines = true, MultiSelect = true,
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text
            };
            list.KeyDown += (sender, args) =>
            {
                if (!args.Control || args.KeyCode != Keys.C || list.SelectedItems.Count == 0) return;
                var lines = list.SelectedItems.Cast<ListViewItem>()
                    .Select(item => string.Join("\t", item.SubItems.Cast<ListViewItem.ListViewSubItem>()
                        .Select(cell => cell.Text)));
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
                args.Handled = true;
            };
            return list;
        }
    }

    internal sealed class LeagueOngoingPlayerHistoryDialog : Form
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        internal LeagueOngoingPlayerHistoryDialog(UiTextCatalog ui,
            LeagueDynamicMessageSnapshot snapshot, LeagueDynamicMessagePlayer player,
            Func<bool> isCurrent,
            Func<LeaguePlayerMatchSummary, CancellationToken, Task<LeagueEncounterMatchDetailResult>> loadDetail,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset,
            Func<int, CancellationToken, Task<IReadOnlyList<LeaguePlayerMatchSummary>>> loadMore,
            int autoPages = 0, Func<int, string, string, Form> openFullCollection = null,
            Func<CancellationToken, Task<bool>> loadMastery = null)
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
            var selectedMode = "";
            Func<IReadOnlyList<LeaguePlayerMatchSummary>> visibleMatches = () =>
                LeagueOngoingHistoryFilter.Select(matches, player.Player.PuuId,
                    selectedChampion, selectedPosition, selectedMode);
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
                if (analysis.NormalWinRate.HasValue)
                    expanded += Environment.NewLine + t(LeagueEfficiencyUiTextKeys.ProfileModeNormal) + ": " +
                        analysis.NormalWinRate.Value.ToString("0.0", CultureInfo.CurrentCulture) +
                        "% (" + (analysis.NormalWins + analysis.NormalLosses).ToString(
                            CultureInfo.CurrentCulture) + ")";
                if (analysis.CherryWinRate.HasValue)
                    expanded += Environment.NewLine + t(LeagueEfficiencyUiTextKeys.ProfileModeCherry) + ": " +
                        analysis.CherryWinRate.Value.ToString("0.0", CultureInfo.CurrentCulture) +
                        "% (" + (analysis.CherryWins + analysis.CherryLosses).ToString(
                            CultureInfo.CurrentCulture) + ")";
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
            var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6 };
            foreach (var width in new[] { 13f, 21f, 13f, 21f, 12f, 20f })
                filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            var championLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingCardFilterChampion) };
            var positionLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingCardFilterPosition) };
            var championChoice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            var positionChoice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            var modeChoice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            modeChoice.Items.Add(t(LeagueEfficiencyUiTextKeys.OngoingCardFilterAll));
            modeChoice.Items.Add(t(LeagueEfficiencyUiTextKeys.ProfileModeNormal));
            modeChoice.Items.Add(t(LeagueEfficiencyUiTextKeys.ProfileModeCherry));
            modeChoice.SelectedIndex = 0;
            filters.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.AdvancedMode),
                TextAlign = ContentAlignment.MiddleLeft }, 4, 0);
            filters.Controls.Add(modeChoice, 5, 0);
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
                    modeChoice.SelectedIndex = selectedMode == "CHERRY" ? 2 :
                        selectedMode == "NORMAL" ? 1 : 0;
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
            modeChoice.SelectedIndexChanged += (sender, args) =>
            {
                if (updatingChoices) return;
                selectedMode = modeChoice.SelectedIndex == 2 ? "CHERRY" :
                    modeChoice.SelectedIndex == 1 ? "NORMAL" : "";
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
                        selectedChampion, selectedPosition, nextIndex, 0, selectedMode);
                fullCollect.Enabled = !collecting && !loading && isCurrent() &&
                    openFullCollection != null &&
                    (selectedMode != "" || selectedChampion > 0 ||
                     !string.IsNullOrEmpty(selectedPosition));
            };
            fullCollect.Click += (sender, args) =>
            {
                if (!fullCollect.Enabled || !isCurrent()) return;
                using (var dialog = openFullCollection(selectedChampion, selectedPosition, selectedMode))
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
                                    selectedChampion, selectedPosition, nextIndex, requests,
                                    selectedMode) ||
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
            var statsRow = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = loadMastery == null ? 2 : 3 };
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            if (loadMastery != null)
                statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            statsRow.Controls.Add(stats, 0, 0);
            var breakdownButton = new Button { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingCardBreakdown) };
            breakdownButton.Click += (sender, args) =>
            {
                if (!isCurrent() || loading || collecting) return;
                using (var dialog = new LeagueOngoingCardBreakdownDialog(ui,
                    matches, player.Player.PuuId))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK || !dialog.HasSelection ||
                        !isCurrent()) return;
                    selectedChampion = dialog.SelectedChampionId;
                    selectedPosition = dialog.SelectedPosition;
                    selectedMode = dialog.SelectedMode;
                    rebuildChoices();
                    refreshRows();
                }
            };
            statsRow.Controls.Add(breakdownButton, 1, 0);
            if (loadMastery != null)
            {
                var masteryButton = new Button { Dock = DockStyle.Fill,
                    Text = ui.Get(UiTextKeys.LeagueProfileMastery) };
                masteryButton.Click += async (sender, args) =>
                {
                    if (!isCurrent() || loading || collecting || !masteryButton.Enabled) return;
                    masteryButton.Enabled = false;
                    try
                    {
                        await loadMastery(_lifetime.Token);
                        if (IsDisposed || _lifetime.IsCancellationRequested || !isCurrent()) return;
                        using (var dialog = new LeagueOngoingCardInsightsDialog(
                            ui, player, matches, openFullCollection != null))
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK || !isCurrent()) return;
                            if (dialog.CollectChampionId > 0 && openFullCollection != null)
                            {
                                using (var collection = openFullCollection(
                                    dialog.CollectChampionId, "", ""))
                                    if (collection != null) collection.ShowDialog(this);
                            }
                            else if (dialog.SelectedChampionId > 0)
                            {
                                selectedChampion = dialog.SelectedChampionId;
                                selectedPosition = "";
                                selectedMode = "";
                                rebuildChoices();
                                refreshRows();
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }
                    finally
                    {
                        if (!IsDisposed) masteryButton.Enabled = true;
                    }
                };
                statsRow.Controls.Add(masteryButton, 2, 0);
            }
            layout.Controls.Add(statsRow, 0, 2);
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
