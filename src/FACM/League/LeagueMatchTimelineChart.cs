using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueTimelineSeries
    {
        internal string Name;
        internal int ColorIndex;
        internal bool IsAverage;
        internal double?[] Values;
    }

    internal static class LeagueTimelineMetrics
    {
        internal static double? Value(Dictionary<string, object> frame, int metric)
        {
            if (metric == 0) return LeagueMatchTimelineParser.Number(frame, "totalGold");
            if (metric == 1)
            {
                var lane = LeagueMatchTimelineParser.Number(frame, "minionsKilled");
                var jungle = LeagueMatchTimelineParser.Number(frame, "jungleMinionsKilled");
                return lane.HasValue && jungle.HasValue ? (double?)lane.Value + jungle.Value : null;
            }
            if (metric == 2) return LeagueMatchTimelineParser.Number(frame, "xp");
            object damage;
            var stats = frame != null && frame.TryGetValue("damageStats", out damage)
                ? damage as Dictionary<string, object> : null;
            return metric == 3 ? LeagueMatchTimelineParser.Number(stats, "totalDamageDoneToChampions") :
                metric == 4 ? LeagueMatchTimelineParser.Number(stats, "totalDamageTaken") : null;
        }

        internal static double?[] Series(LeagueMatchTimeline timeline, IEnumerable<int> ids, int metric, bool average)
        {
            var participants = ids.Distinct().Where(id => id > 0 && id <= 20).ToArray();
            if (timeline == null || participants.Length == 0) return new double?[0];
            return timeline.Frames.Select(frame =>
            {
                var values = new List<double>();
                foreach (var id in participants)
                {
                    Dictionary<string, object> participant;
                    if (!frame.Participants.TryGetValue(id, out participant)) continue;
                    var value = Value(participant, metric);
                    if (value.HasValue) values.Add(value.Value);
                }
                return values.Count == 0 ? (double?)null : average
                    ? Math.Round(values.Average(), 0, MidpointRounding.AwayFromZero) : values[0];
            }).ToArray();
        }
    }

    internal sealed class LeagueMatchTimelineChartPanel : UserControl
    {
        private readonly ComboBox _metric;
        private readonly CheckedListBox _series;
        private readonly LeagueTimelineChart _chart;
        private readonly List<LeaguePlayerMatchParticipant> _participants;
        private readonly bool _cherry;
        private readonly HashSet<int> _players = new HashSet<int>();
        private readonly HashSet<int> _teams = new HashSet<int>();
        private LeagueMatchTimeline _timeline;
        private bool _updating;

        internal LeagueMatchTimelineChartPanel(UiTextCatalog ui, List<LeaguePlayerMatchParticipant> participants,
            bool cherry)
        {
            _participants = participants;
            _cherry = cherry;
            Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            _chart = new LeagueTimelineChart(ui) { Dock = DockStyle.Fill };
            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _metric = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = ui.Get(UiTextKeys.LeagueTimelineMetric),
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text };
            foreach (var key in new[] { UiTextKeys.LeagueTimelineGold, UiTextKeys.LeaguePlayerCs,
                UiTextKeys.LeagueTimelineXp, UiTextKeys.LeagueTimelineDamage, UiTextKeys.LeagueTimelineTaken })
                _metric.Items.Add(ui.Get(key));
            _metric.SelectedIndex = 0;
            _metric.SelectedIndexChanged += (sender, args) => RefreshChart();
            var all = new CheckBox { Dock = DockStyle.Fill, Text = ui.Get(UiTextKeys.LeagueTimelineSelectPlayers) };
            all.CheckedChanged += (sender, args) =>
            {
                _updating = true;
                for (var index = 0; index < _series.Items.Count; index++)
                {
                    var choice = _series.Items[index] as SeriesChoice;
                    if (choice == null || choice.IsTeam) continue;
                    _series.SetItemChecked(index, all.Checked);
                    if (all.Checked) _players.Add(choice.Id); else _players.Remove(choice.Id);
                }
                _updating = false;
                RefreshChart();
            };
            _series = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = ui.Get(UiTextKeys.LeagueTimelineComparison) };
            var groups = participants.Where(player => LeagueCherryTeams.GroupId(player, cherry) > 0)
                .GroupBy(player => LeagueCherryTeams.GroupId(player, cherry))
                .OrderBy(group => cherry ? LeagueCherryTeams.Placement(group) ?? int.MaxValue : 0)
                .ThenBy(group => group.Key);
            foreach (var team in groups)
            {
                _teams.Add(team.Key);
                var label = cherry ? LeagueEfficiencyText.Get(ui, LeagueEfficiencyUiTextKeys.DynamicEncounterCherryTeam)
                    .Replace("{0}", team.Key.ToString()) + " · " +
                    LeagueEncounterMatchScoreboardDialog.FormatCherryPlacement(LeagueCherryTeams.Placement(team),
                        key => LeagueEfficiencyText.Get(ui, key)) :
                    ui.Get(UiTextKeys.LeagueTimelineTeamAverage).Replace("{0}", team.Key.ToString());
                _series.Items.Add(new SeriesChoice { Id = team.Key, IsTeam = true,
                    Label = label }, true);
            }
            foreach (var player in participants)
                _series.Items.Add(new SeriesChoice { Id = player.ParticipantId,
                    Label = string.IsNullOrEmpty(player.PlayerName) ? "#" + player.ChampionId : player.PlayerName });
            _series.ItemCheck += (sender, args) =>
            {
                if (_updating) return;
                var choice = (SeriesChoice)_series.Items[args.Index];
                var selected = choice.IsTeam ? _teams : _players;
                if (args.NewValue == CheckState.Checked) selected.Add(choice.Id); else selected.Remove(choice.Id);
                RefreshChart();
            };
            tools.Controls.Add(_metric, 0, 0); tools.Controls.Add(all, 0, 1); tools.Controls.Add(_series, 0, 2);
            layout.Controls.Add(_chart, 0, 0); layout.Controls.Add(tools, 1, 0);
            Controls.Add(layout);
        }

        internal void SetTimeline(LeagueMatchTimeline timeline) { _timeline = timeline; RefreshChart(); }
        private void RefreshChart()
        {
            if (_chart == null) return;
            var series = new List<LeagueTimelineSeries>();
            if (_timeline != null)
            {
                foreach (SeriesChoice choice in _series.Items)
                {
                    if (!(choice.IsTeam ? _teams : _players).Contains(choice.Id)) continue;
                    var ids = choice.IsTeam ? _participants.Where(player => LeagueCherryTeams.GroupId(player, _cherry) == choice.Id)
                        .Select(player => player.ParticipantId) : new[] { choice.Id };
                    series.Add(new LeagueTimelineSeries { Name = choice.Label, IsAverage = choice.IsTeam,
                        ColorIndex = choice.IsTeam ? _participants.Count + (_cherry ? choice.Id : choice.Id / 100) :
                            choice.Id - 1,
                        Values = LeagueTimelineMetrics.Series(_timeline, ids, _metric.SelectedIndex, choice.IsTeam) });
                }
            }
            _chart.SetData(_timeline?.Frames.Select(frame => frame.Timestamp).ToArray() ?? new long[0], series);
        }
        private sealed class SeriesChoice
        {
            internal int Id;
            internal bool IsTeam;
            internal string Label;
            public override string ToString() { return Label; }
        }
    }

    internal sealed class LeagueTimelineChart : Control
    {
        private static readonly Color[] Palette = { Color.DodgerBlue, Color.OrangeRed, Color.MediumSeaGreen,
            Color.MediumPurple, Color.DarkOrange, Color.Teal, Color.CornflowerBlue, Color.OliveDrab,
            Color.DarkViolet, Color.IndianRed, Color.DeepSkyBlue, Color.Goldenrod };
        private readonly ToolTip _tooltip = new ToolTip();
        private readonly string _empty;
        private long[] _times = new long[0];
        private List<LeagueTimelineSeries> _series = new List<LeagueTimelineSeries>();
        private int _hover = -1;
        private Rectangle Plot => new Rectangle(58, 16, Math.Max(1, ClientSize.Width - 72), Math.Max(1, ClientSize.Height - 54));

        internal LeagueTimelineChart(UiTextCatalog ui)
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = FacmDesignSystem.SurfaceRaised;
            ForeColor = FacmDesignSystem.Text;
            AccessibleName = ui.Get(UiTextKeys.LeagueTimelineComparison);
            AccessibleRole = AccessibleRole.Graphic;
            _empty = ui.Get(UiTextKeys.LeagueTimelineNoSamples);
            MouseLeave += (sender, args) => { _tooltip.Hide(this); _hover = -1; };
        }
        internal void SetData(long[] times, List<LeagueTimelineSeries> series)
        {
            _times = times; _series = series; _hover = -1; _tooltip.Hide(this); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (_times.Length == 0 || !_series.Any(series => series.Values.Any(value => value.HasValue)))
            {
                TextRenderer.DrawText(args.Graphics, _empty, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            var plot = Plot;
            if (plot.Width < 60 || plot.Height < 40) return;
            var maximum = Math.Max(1d, _series.SelectMany(series => series.Values).Where(value => value.HasValue)
                .Max(value => value.Value));
            var duration = Math.Max(1d, (double)_times[_times.Length - 1] - _times[0]);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var grid = new Pen(FacmDesignSystem.BorderSoft))
            {
                for (var tick = 0; tick <= 4; tick++)
                {
                    var y = plot.Bottom - plot.Height * tick / 4;
                    args.Graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(args.Graphics, (maximum * tick / 4).ToString("N0", CultureInfo.CurrentCulture),
                        Font, new Rectangle(0, y - Font.Height / 2, plot.Left - 6, Font.Height + 2), ForeColor,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    var timestamp = _times.Length == 1 ? _times[0] : _times[(int)((_times.Length - 1) * tick / 4d)];
                    var x = plot.Left + (float)(((double)timestamp - _times[0]) / duration * plot.Width);
                    TextRenderer.DrawText(args.Graphics, LeagueMatchTimelineParser.Time(timestamp), Font,
                        new Rectangle((int)x - 23, plot.Bottom + 6, 46, Font.Height + 3), ForeColor,
                        TextFormatFlags.HorizontalCenter);
                }
            }
            foreach (var series in _series)
            {
                using (var pen = new Pen(Palette[Math.Abs(series.ColorIndex) % Palette.Length], series.IsAverage ? 2.6F : 1.5F))
                {
                    if (series.IsAverage) pen.DashStyle = DashStyle.Dash;
                    PointF? previous = null;
                    for (var index = 0; index < Math.Min(_times.Length, series.Values.Length); index++)
                    {
                        if (!series.Values[index].HasValue) { previous = null; continue; }
                        var point = new PointF(plot.Left + (float)(((double)_times[index] - _times[0]) / duration * plot.Width),
                            plot.Bottom - (float)(series.Values[index].Value / maximum * plot.Height));
                        if (previous.HasValue) args.Graphics.DrawLine(pen, previous.Value, point);
                        else args.Graphics.DrawEllipse(pen, point.X - 1, point.Y - 1, 2, 2);
                        previous = point;
                    }
                }
            }
        }
        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            if (_times.Length == 0 || !Plot.Contains(args.Location)) { _tooltip.Hide(this); _hover = -1; return; }
            var target = _times[0] + (double)(args.X - Plot.Left) / Plot.Width * ((double)_times[_times.Length - 1] - _times[0]);
            var index = 0;
            var distance = double.MaxValue;
            for (var candidate = 0; candidate < _times.Length; candidate++)
            {
                var current = Math.Abs(_times[candidate] - target);
                if (current < distance) { distance = current; index = candidate; }
            }
            if (index == _hover) return;
            _hover = index;
            var values = _series.Where(series => index < series.Values.Length && series.Values[index].HasValue)
                .Select(series => series.Name + ": " + series.Values[index].Value.ToString("N0", CultureInfo.CurrentCulture));
            _tooltip.Show(LeagueMatchTimelineParser.Time(_times[index]) + "\r\n" + string.Join("\r\n", values),
                this, args.X + 12, args.Y + 12, 3000);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _tooltip.Dispose();
            base.Dispose(disposing);
        }
    }
}
