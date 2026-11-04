using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueMatchRadarProfile
    {
        internal readonly double?[] Player = new double?[7];
        internal readonly double?[] TeamAverage = new double?[7];
        internal readonly double?[] Maximum = new double?[7];
        internal readonly double?[] PlayerRatio = new double?[7];
        internal readonly double?[] TeamAverageRatio = new double?[7];
    }

    internal sealed class LeagueMatchRadarPanel : UserControl
    {
        private static readonly string[] AxisKeys = {
            LeagueEfficiencyUiTextKeys.DynamicEncounterRadarDamage,
            LeagueEfficiencyUiTextKeys.DynamicEncounterRadarTaken,
            LeagueEfficiencyUiTextKeys.DynamicEncounterRadarGold,
            LeagueEfficiencyUiTextKeys.DynamicEncounterRadarCs,
            LeagueEfficiencyUiTextKeys.DynamicEncounterRadarKda,
            LeagueEfficiencyUiTextKeys.DynamicEncounterRadarParticipation,
            LeagueEfficiencyUiTextKeys.DynamicEncounterRadarHeal
        };
        private readonly IReadOnlyList<LeaguePlayerMatchParticipant> _players;
        private readonly IReadOnlyList<LeagueMatchTeamScore> _teams;
        private readonly bool _cherry;
        private readonly Func<string, string> _t;
        private readonly Panel _canvas;
        private readonly TextBox _details;
        private readonly ToolTip _hoverTip = new ToolTip();
        private LeagueMatchRadarProfile _profile;

        internal LeagueMatchRadarPanel(IEnumerable<LeaguePlayerMatchParticipant> players,
            IEnumerable<LeagueMatchTeamScore> teams, bool cherry, Func<string, string> t)
        {
            _players = (players ?? Enumerable.Empty<LeaguePlayerMatchParticipant>()).Take(21).ToList();
            _teams = (teams ?? Enumerable.Empty<LeagueMatchTeamScore>()).Take(21).ToList();
            _cherry = cherry;
            _t = t;
            Dock = DockStyle.Fill;
            BackColor = FacmDesignSystem.CanvasRaised;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            _canvas = new Panel { Dock = DockStyle.Fill, BackColor = FacmDesignSystem.CanvasRaised,
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterRadarTitle) };
            _canvas.Paint += (sender, e) => PaintRadar(e.Graphics, _canvas.ClientRectangle, _profile, _t);
            _canvas.MouseMove += (sender, e) => {
                var tip = HitTest(_canvas.ClientRectangle, _profile, e.Location, _t);
                if (_hoverTip.GetToolTip(_canvas) != tip)
                    _hoverTip.SetToolTip(_canvas, tip ?? string.Empty);
            };
            _canvas.MouseLeave += (sender, e) => _hoverTip.SetToolTip(_canvas, string.Empty);
            _details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, TabStop = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterRadarTitle) };
            layout.Controls.Add(_canvas, 0, 0);
            layout.Controls.Add(_details, 1, 0);
            Controls.Add(layout);
            UpdateSelection(null);
        }

        internal void UpdateSelection(LeaguePlayerMatchParticipant player)
        {
            _profile = Project(_players, _teams, _cherry, player);
            _hoverTip.SetToolTip(_canvas, string.Empty);
            _details.Text = FormatDetails(_profile, _t);
            _canvas.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hoverTip.Dispose();
            base.Dispose(disposing);
        }

        internal static LeagueMatchRadarProfile Project(IEnumerable<LeaguePlayerMatchParticipant> participants,
            IEnumerable<LeagueMatchTeamScore> teams, bool cherry, LeaguePlayerMatchParticipant selected)
        {
            if (selected == null) return null;
            var players = (participants ?? Enumerable.Empty<LeaguePlayerMatchParticipant>()).Take(21).ToList();
            if (players.Count == 0 || players.Count > 20 || !players.Contains(selected) ||
                players.Any(p => p == null || p.ParticipantId <= 0) ||
                players.Select(p => p.ParticipantId).Distinct().Count() != players.Count) return null;
            var scores = (teams ?? Enumerable.Empty<LeagueMatchTeamScore>()).Take(21).ToList();
            if (scores.Count > 20) return null;
            var raw = players.ToDictionary(p => p, p => RawMetrics(p, scores, cherry));
            var result = new LeagueMatchRadarProfile();
            var group = LeagueCherryTeams.GroupId(selected, cherry);
            var members = group > 0 ? players.Where(p => LeagueCherryTeams.GroupId(p, cherry) == group).ToList() :
                new List<LeaguePlayerMatchParticipant>();
            for (var axis = 0; axis < AxisKeys.Length; axis++)
            {
                var known = raw.Values.Select(values => values[axis]).Where(value => value.HasValue).ToList();
                result.Maximum[axis] = known.Count > 0 ? known.Max() : null;
                result.Player[axis] = raw[selected][axis];
                if (members.Count > 0 && members.All(p => raw[p][axis].HasValue))
                    result.TeamAverage[axis] = members.Average(p => raw[p][axis].Value);
                if (result.Maximum[axis].HasValue)
                {
                    var denominator = result.Maximum[axis].Value > 0 ? result.Maximum[axis].Value : 1d;
                    if (result.Player[axis].HasValue)
                        result.PlayerRatio[axis] = result.Player[axis].Value / denominator;
                    if (result.TeamAverage[axis].HasValue)
                        result.TeamAverageRatio[axis] = result.TeamAverage[axis].Value / denominator;
                }
            }
            return result;
        }

        private static double?[] RawMetrics(LeaguePlayerMatchParticipant player,
            IList<LeagueMatchTeamScore> scores, bool cherry)
        {
            var group = LeagueCherryTeams.GroupId(player, cherry);
            var matching = group > 0 ? scores.Where(s => s != null && s.TeamId == group).Take(2).ToList() :
                new List<LeagueMatchTeamScore>();
            var team = matching.Count == 1 ? matching[0] : null;
            var validKda = player.Kills >= 0 && player.Deaths >= 0 && player.Assists >= 0;
            var kp = validKda && team != null && team.Kills >= 0
                ? (double?)(player.Kills + (double)player.Assists) / Math.Max(1L, team.Kills) : null;
            return new double?[] {
                player.DamageAvailable && player.DamageToChampions >= 0 ? (double?)player.DamageToChampions : null,
                ValidCount(player.TotalDamageTaken),
                player.GoldAvailable && player.GoldEarned >= 0 ? (double?)player.GoldEarned : null,
                player.CreepScoreAvailable && player.CreepScore >= 0 ? (double?)player.CreepScore : null,
                validKda ? (double?)(player.Kills + (double)player.Assists) / Math.Max(1, player.Deaths) : null,
                kp,
                ValidCount(player.TotalHeal)
            };
        }

        private static double? ValidCount(int? value)
        {
            return value.HasValue && value.Value >= 0 ? (double?)value.Value : null;
        }

        internal static string FormatDetails(LeagueMatchRadarProfile profile, Func<string, string> t)
        {
            if (profile == null) return t(LeagueEfficiencyUiTextKeys.DynamicEncounterRadarHint);
            var lines = new List<string> { t(LeagueEfficiencyUiTextKeys.DynamicEncounterRadarLegend) };
            for (var i = 0; i < AxisKeys.Length; i++)
            {
                var player = FormatMetric(profile.Player[i], i == 4 ? "0.00" : i == 5 ? "0%" : "N0");
                var average = FormatMetric(profile.TeamAverage[i], i == 4 ? "0.00" : i == 5 ? "0%" : "N0");
                var playerRatio = FormatMetric(profile.PlayerRatio[i], "0.0%");
                var averageRatio = FormatMetric(profile.TeamAverageRatio[i], "0.0%");
                lines.Add(t(AxisKeys[i]) + "：" + player + "（" + playerRatio + "） / " +
                    average + "（" + averageRatio + "）");
            }
            return string.Join("\r\n", lines);
        }

        private static string FormatMetric(double? value, string format)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "—";
        }

        internal static void PaintRadar(Graphics graphics, Rectangle bounds, LeagueMatchRadarProfile profile,
            Func<string, string> t)
        {
            PointF center;
            float radius;
            if (profile == null || !TryGeometry(bounds, out center, out radius)) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Func<int, float, PointF> point = (axis, ratio) => RadarPoint(center, radius, axis, ratio);
            using (var grid = new Pen(Color.FromArgb(140, FacmDesignSystem.Text), 1))
            {
                for (var ring = 1; ring <= 4; ring++)
                    graphics.DrawPolygon(grid, Enumerable.Range(0, 7).Select(axis => point(axis, ring / 4f)).ToArray());
                for (var axis = 0; axis < 7; axis++)
                {
                    graphics.DrawLine(grid, center, point(axis, 1));
                    var edge = point(axis, 1.22f);
                    TextRenderer.DrawText(graphics, t(AxisKeys[axis]), SystemFonts.MessageBoxFont,
                        new Rectangle((int)edge.X - 32, (int)edge.Y - 10, 64, 20),
                        FacmDesignSystem.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding |
                            TextFormatFlags.EndEllipsis);
                }
            }
            PaintSeries(graphics, point, profile.TeamAverageRatio, Color.FromArgb(113, 128, 150));
            PaintSeries(graphics, point, profile.PlayerRatio, Color.FromArgb(220, 38, 38));
        }

        internal static string HitTest(Rectangle bounds, LeagueMatchRadarProfile profile, Point cursor,
            Func<string, string> t)
        {
            PointF center;
            float radius;
            if (profile == null || !TryGeometry(bounds, out center, out radius)) return null;
            var closest = 12f * 12f;
            string tooltip = null;
            for (var series = 0; series < 2; series++)
            {
                var ratios = series == 0 ? profile.TeamAverageRatio : profile.PlayerRatio;
                if (ratios == null || ratios.Length != AxisKeys.Length) continue;
                for (var axis = 0; axis < AxisKeys.Length; axis++)
                {
                    var value = ratios[axis];
                    if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) continue;
                    var p = RadarPoint(center, radius, axis, (float)Math.Max(0, Math.Min(1, value.Value)));
                    var distance = (p.X - cursor.X) * (p.X - cursor.X) +
                        (p.Y - cursor.Y) * (p.Y - cursor.Y);
                    if (distance > closest) continue;
                    closest = distance;
                    var key = series == 0 ? LeagueEfficiencyUiTextKeys.DynamicEncounterRadarTeamTip :
                        LeagueEfficiencyUiTextKeys.DynamicEncounterRadarPlayerTip;
                    tooltip = t(key).Replace("{0}", t(AxisKeys[axis]))
                        .Replace("{1}", FormatMetric(value, "0.0%"));
                }
            }
            return tooltip;
        }

        private static bool TryGeometry(Rectangle bounds, out PointF center, out float radius)
        {
            center = PointF.Empty;
            radius = 0;
            if (bounds.Width < 145 || bounds.Height < 135) return false;
            radius = Math.Min((bounds.Width - 105) / 2f, (bounds.Height - 88) / 2f);
            if (radius < 20) return false;
            center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f + 9);
            return true;
        }

        private static PointF RadarPoint(PointF center, float radius, int axis, float ratio)
        {
            var angle = axis * 2 * Math.PI / AxisKeys.Length;
            return new PointF(center.X + (float)Math.Sin(angle) * radius * ratio,
                center.Y - (float)Math.Cos(angle) * radius * ratio);
        }

        private static void PaintSeries(Graphics graphics, Func<int, float, PointF> point,
            double?[] ratios, Color color)
        {
            if (ratios == null || ratios.Length != 7) return;
            var allKnown = ratios.All(value => value.HasValue && !double.IsNaN(value.Value) &&
                !double.IsInfinity(value.Value));
            using (var pen = new Pen(color, 2))
            {
                if (allKnown)
                {
                    var polygon = Enumerable.Range(0, 7).Select(i => point(i,
                        (float)Math.Max(0, Math.Min(1, ratios[i].Value)))).ToArray();
                    using (var fill = new SolidBrush(Color.FromArgb(38, color)))
                        graphics.FillPolygon(fill, polygon);
                    graphics.DrawPolygon(pen, polygon);
                }
                else
                {
                    for (var i = 0; i < 7; i++)
                    {
                        var next = (i + 1) % 7;
                        if (ratios[i].HasValue && ratios[next].HasValue)
                            graphics.DrawLine(pen, point(i, (float)Math.Max(0, Math.Min(1, ratios[i].Value))),
                                point(next, (float)Math.Max(0, Math.Min(1, ratios[next].Value))));
                    }
                }
                for (var i = 0; i < 7; i++)
                {
                    if (!ratios[i].HasValue) continue;
                    var p = point(i, (float)Math.Max(0, Math.Min(1, ratios[i].Value)));
                    using (var brush = new SolidBrush(color))
                        graphics.FillEllipse(brush, p.X - 3, p.Y - 3, 6, 6);
                }
            }
        }
    }
}
