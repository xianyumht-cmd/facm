using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueEventMap : Control
    {
        private readonly UiTextCatalog _ui;
        private readonly int _mapId;
        private Bitmap _map;
        private PointF? _position;
        private PointF[] _minutePositions = new PointF[0];
        private PointF[] _gankPositions = new PointF[0];
        private int[] _minuteLanes = new int[0];
        private int[] _gankLanes = new int[0];
        private PointF[] _level3Positions = new PointF[0];
        private PointF[] _level4Positions = new PointF[0];
        private bool _pointCloudMode;
        private bool _heatmapMode;
        private HeatmapCell[] _heatmapCells = new HeatmapCell[0];

        internal sealed class HeatmapCell
        {
            internal PointF Center;
            internal int Weight, Lane;
            internal float Intensity;
        }

        private sealed class HeatmapAccumulator
        {
            internal int Weight;
            internal double X, Y;
            internal readonly int[] LaneWeights = new int[3];
        }

        internal bool HeatmapMode { get { return _heatmapMode; } }
        internal int HeatmapCellCount { get { return _heatmapCells.Length; } }

        internal LeagueEventMap(UiTextCatalog ui, int mapId)
        {
            _ui = ui;
            _mapId = mapId;
            Dock = DockStyle.Fill;
            BackColor = FacmDesignSystem.SurfaceRaised;
            ForeColor = FacmDesignSystem.Text;
            AccessibleName = ui.Get(UiTextKeys.LeagueEventMap);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            if (Supported(mapId))
                using (var stream = typeof(LeagueEventMap).Assembly.GetManifestResourceStream(
                    "FACM.Resources.LeagueMaps." + mapId + ".png"))
                {
                    if (stream != null)
                        using (var image = Image.FromStream(stream)) _map = new Bitmap(image);
                }
        }

        internal void SetEvent(LeagueMatchTimelineEvent entry)
        {
            if (IsDisposed) return;
            _pointCloudMode = false;
            _minutePositions = new PointF[0];
            _gankPositions = new PointF[0];
            _minuteLanes = new int[0];
            _gankLanes = new int[0];
            _level3Positions = new PointF[0];
            _level4Positions = new PointF[0];
            _heatmapCells = new HeatmapCell[0];
            _position = Position(entry, _mapId);
            AccessibleDescription = _position.HasValue ? AccessibleName :
                _ui.Get(Supported(_mapId) ? UiTextKeys.LeagueEventMapMissing : UiTextKeys.LeagueEventMapUnsupported);
            Invalidate();
        }

        internal void SetPointCloud(IEnumerable<PointF> minutePositions, IEnumerable<PointF> gankPositions)
        {
            SetPointCloud(minutePositions, gankPositions, null, null);
        }

        internal void SetPointCloud(IEnumerable<PointF> minutePositions, IEnumerable<PointF> gankPositions,
            IEnumerable<PointF> level3Positions, IEnumerable<PointF> level4Positions)
        {
            SetPointCloud(minutePositions, gankPositions, level3Positions, level4Positions, null, null);
        }

        internal void SetPointCloud(IEnumerable<PointF> minutePositions, IEnumerable<PointF> gankPositions,
            IEnumerable<PointF> level3Positions, IEnumerable<PointF> level4Positions,
            IEnumerable<int> minuteLanes, IEnumerable<int> gankLanes)
        {
            if (IsDisposed) return;
            _pointCloudMode = true;
            _position = null;
            // Preserve source lane ownership through normalization and visible-point sampling.
            var minutes = NormalizeLanePoints(minutePositions, minuteLanes, 40 * 14, false);
            var ganks = NormalizeLanePoints(gankPositions, gankLanes,
                40 * LeagueJungleAnalysis.MaximumGankMapPoints, true);
            var minuteIndices = BalanceIndices(minutes.Item2, 54);
            var gankIndices = BalanceIndices(ganks.Item2, 100);
            _minutePositions = minuteIndices.Select(i => minutes.Item1[i]).ToArray();
            _minuteLanes = minuteIndices.Select(i => minutes.Item2[i]).ToArray();
            _gankPositions = gankIndices.Select(i => ganks.Item1[i]).ToArray();
            _gankLanes = gankIndices.Select(i => ganks.Item2[i]).ToArray();
            _level3Positions = NormalizePoints(level3Positions, 40 * LeagueJungleAnalysis.MaximumGankMapPoints);
            _level4Positions = NormalizePoints(level4Positions, 40 * LeagueJungleAnalysis.MaximumGankMapPoints);
            _heatmapCells = BuildHeatmap(minutePositions, gankPositions, _mapId, minuteLanes, gankLanes);
            UpdatePointCloudDescription();
            Invalidate();
        }

        internal void SetHeatmapMode(bool enabled)
        {
            if (IsDisposed || _heatmapMode == enabled) return;
            _heatmapMode = enabled;
            if (_pointCloudMode) UpdatePointCloudDescription();
            Invalidate();
        }

        private void UpdatePointCloudDescription()
        {
            AccessibleDescription = _ui.Get(_minutePositions.Length + _gankPositions.Length +
                _level3Positions.Length + _level4Positions.Length == 0 ?
                UiTextKeys.LeagueJungleMapEmpty : _heatmapMode ?
                UiTextKeys.LeagueJungleMapHeatmapLegend : UiTextKeys.LeagueJungleMapLegend);
        }

        // Matches the pinned GankMap 7x7 grid: minute samples weigh 1, confirmed ganks weigh 5.
        internal static HeatmapCell[] BuildHeatmap(IEnumerable<PointF> minutes, IEnumerable<PointF> ganks, int mapId)
        {
            return BuildHeatmap(minutes, ganks, mapId, null, null);
        }

        internal static HeatmapCell[] BuildHeatmap(IEnumerable<PointF> minutes, IEnumerable<PointF> ganks,
            int mapId, IEnumerable<int> minuteLanes, IEnumerable<int> gankLanes)
        {
            if (mapId != 11) return new HeatmapCell[0];
            const int grid = 7;
            var cells = new Dictionary<int, HeatmapAccumulator>();
            Action<IEnumerable<PointF>, IEnumerable<int>, int, int> add = (points, lanes, limit, weight) =>
            {
                var labels = (lanes ?? Enumerable.Empty<int>()).Take(limit).ToArray();
                var sourceIndex = 0;
                foreach (var raw in (points ?? Enumerable.Empty<PointF>()).Take(limit))
                {
                    var explicitLane = sourceIndex < labels.Length ? labels[sourceIndex] : -1;
                    sourceIndex++;
                    var normalized = Normalize(raw.X, raw.Y, mapId);
                    if (!normalized.HasValue) continue;
                    var point = normalized.Value;
                    var x = Math.Min(grid - 1, (int)(point.X * grid));
                    var y = Math.Min(grid - 1, (int)(point.Y * grid));
                    var index = y * grid + x;
                    HeatmapAccumulator cell;
                    if (!cells.TryGetValue(index, out cell))
                    {
                        cell = new HeatmapAccumulator();
                        cells.Add(index, cell);
                    }
                    cell.Weight += weight;
                    cell.X += point.X * weight;
                    cell.Y += point.Y * weight;
                    var sourceLane = weight == 5 ? LeagueJungleAnalysis.GankLane((long)raw.X, (long)raw.Y) : null;
                    var lane = explicitLane >= 0 && explicitLane < 3 ? explicitLane :
                        sourceLane ?? LeagueJungleAnalysis.MapZone((long)raw.X, (long)raw.Y);
                    cell.LaneWeights[lane] += weight;
                }
            };
            add(minutes, minuteLanes, 40 * 14, 1);
            add(ganks, gankLanes, 40 * LeagueJungleAnalysis.MaximumGankMapPoints, 5);
            if (cells.Count == 0) return new HeatmapCell[0];
            var maxWeight = cells.Values.Max(c => c.Weight);
            return cells.OrderByDescending(pair => pair.Value.Weight).ThenBy(pair => pair.Key)
                .Take(14).Select(pair =>
                {
                    var cell = pair.Value;
                    var lane = Enumerable.Range(0, 3).OrderByDescending(i => cell.LaneWeights[i]).First();
                    return new HeatmapCell {
                        Center = new PointF((float)(cell.X / cell.Weight), (float)(cell.Y / cell.Weight)),
                        Weight = cell.Weight, Lane = lane, Intensity = (float)cell.Weight / maxWeight
                    };
                }).ToArray();
        }

        // Pinned GankMap.vue shows a blue/red diagonal Rift overlay on jungle spatial maps.
        internal static void DrawJungleSideOverlay(Graphics graphics, Rectangle bounds)
        {
            if (graphics == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            var topLeft = new PointF(bounds.Left, bounds.Top);
            var bottomRight = new PointF(bounds.Right, bounds.Bottom);
            using (var blue = new SolidBrush(Color.FromArgb(20, 60, 140, 255)))
            using (var red = new SolidBrush(Color.FromArgb(20, 255, 60, 60)))
            using (var diagonal = new Pen(Color.FromArgb(89, 255, 255, 255),
                Math.Max(0.8f, bounds.Width * 0.008f)))
            {
                graphics.FillPolygon(blue, new[] { topLeft, bottomRight,
                    new PointF(bounds.Left, bounds.Bottom) });
                graphics.FillPolygon(red, new[] { topLeft, bottomRight,
                    new PointF(bounds.Right, bounds.Top) });
                diagonal.DashPattern = new[] { 5f, 3.75f };
                graphics.DrawLine(diagonal, topLeft, bottomRight);
            }
        }

        // The compact timeline keeps coordinates but not Akari's explicit MapLanePoint.lane.
        internal static int LaneIndex(PointF normalized)
        {
            return LeagueJungleAnalysis.MapZone((long)(normalized.X * 14820),
                (long)((1f - normalized.Y) * 14881));
        }

        // Gank points use the stricter source lane classification, not the broad activity zone.
        internal static int GankLaneIndex(PointF normalized)
        {
            var x = (long)Math.Round(normalized.X * 14820d);
            var y = (long)Math.Round((1d - normalized.Y) * 14881d);
            return LeagueJungleAnalysis.GankLane(x, y) ?? LeagueJungleAnalysis.MapZone(x, y);
        }

        internal static Color LaneMarkerColor(int lane, bool kill, bool dark)
        {
            if (lane == 0) return Color.FromArgb(kill ? 255 : dark ? 140 : 166,
                dark ? 248 : 220, dark ? 113 : 38, dark ? 113 : 38);
            if (lane == 1) return Color.FromArgb(kill ? 255 : dark ? 140 : 179,
                dark ? 250 : 245, dark ? 204 : 158, dark ? 21 : 11);
            return Color.FromArgb(kill ? 255 : dark ? 140 : 166,
                dark ? 96 : 37, dark ? 165 : 99, dark ? 250 : 235);
        }

        internal static void DrawLaneKillMarker(Graphics graphics, PointF center, float radius, Pen pen)
        {
            if (graphics == null || pen == null || radius <= 0) return;
            graphics.DrawLine(pen, center.X - radius, center.Y - radius,
                center.X + radius, center.Y + radius);
            graphics.DrawLine(pen, center.X - radius, center.Y + radius,
                center.X + radius, center.Y - radius);
        }

        // Pinned GankMap.vue: equal lane quotas, spare-slot redistribution and ordered picks.
        internal static PointF[] BalanceByLane(PointF[] points, int limit, bool gank = false)
        {
            if (points == null) return new PointF[0];
            var lanes = points.Select(p => gank ? GankLaneIndex(p) : LaneIndex(p)).ToArray();
            return BalanceIndices(lanes, limit).Select(i => points[i]).ToArray();
        }

        internal static int[] BalanceIndices(int[] lanes, int limit)
        {
            if (lanes == null || limit <= 0) return new int[0];
            if (lanes.Length <= limit) return Enumerable.Range(0, lanes.Length).ToArray();
            var buckets = new[] { new List<int>(), new List<int>(), new List<int>() };
            for (var i = 0; i < lanes.Length; i++)
                buckets[lanes[i] >= 0 && lanes[i] < 3 ? lanes[i] : 1].Add(i);
            var active = Enumerable.Range(0, 3).Where(lane => buckets[lane].Count > 0).ToArray();
            var picked = new int[3];
            var quota = limit >= active.Length ? limit / active.Length : 0;
            foreach (var lane in active)
                picked[lane] = Math.Min(quota, buckets[lane].Count);
            var remaining = limit - picked.Sum();
            while (remaining > 0)
            {
                var candidates = active.Where(lane => picked[lane] < buckets[lane].Count)
                    .OrderByDescending(lane => buckets[lane].Count - picked[lane])
                    .ThenByDescending(lane => buckets[lane].Count).ToArray();
                if (candidates.Length == 0) break;
                foreach (var lane in candidates)
                {
                    if (remaining == 0) break;
                    picked[lane]++;
                    remaining--;
                }
            }
            var selected = new bool[lanes.Length];
            foreach (var lane in active)
            {
                var bucket = buckets[lane];
                for (var i = 0; i < picked[lane]; i++)
                {
                    var index = picked[lane] == 1 ? (bucket.Count - 1) / 2 :
                        (int)Math.Floor((double)i * (bucket.Count - 1) / (picked[lane] - 1) + 0.5d);
                    selected[bucket[index]] = true;
                }
            }
            return Enumerable.Range(0, lanes.Length).Where(i => selected[i]).ToArray();
        }

        private Tuple<PointF[], int[]> NormalizeLanePoints(IEnumerable<PointF> positions,
            IEnumerable<int> lanes, int limit, bool gank)
        {
            var raw = (positions ?? Enumerable.Empty<PointF>()).Take(limit).ToArray();
            var labels = (lanes ?? Enumerable.Empty<int>()).Take(limit).ToArray();
            var points = new List<PointF>();
            var resolved = new List<int>();
            for (var i = 0; i < raw.Length; i++)
            {
                var point = Normalize(raw[i].X, raw[i].Y, _mapId);
                if (!point.HasValue) continue;
                points.Add(point.Value);
                resolved.Add(i < labels.Length && labels[i] >= 0 && labels[i] < 3 ? labels[i] :
                    gank ? GankLaneIndex(point.Value) : LaneIndex(point.Value));
            }
            return Tuple.Create(points.ToArray(), resolved.ToArray());
        }

        private PointF[] NormalizePoints(IEnumerable<PointF> positions, int limit)
        {
            return (positions ?? Enumerable.Empty<PointF>()).Take(limit).Select(p => Normalize(p.X, p.Y, _mapId))
                .Where(p => p.HasValue).Select(p => p.Value).ToArray();
        }

        internal static bool Supported(int mapId) { return mapId == 11 || mapId == 12 || mapId == 21; }

        // Normalized source coordinates: image scaling stays independent of DPI and panel size.
        internal static PointF? Position(LeagueMatchTimelineEvent entry, int mapId)
        {
            if (!Supported(mapId) || entry == null || entry.Data == null) return null;
            object value;
            if (!entry.Data.TryGetValue("position", out value)) return null;
            var position = value as Dictionary<string, object>;
            double x, y;
            if (!Coordinate(position, "x", out x) || !Coordinate(position, "y", out y)) return null;
            return Normalize(x, y, mapId);
        }

        internal static PointF? Normalize(double x, double y, int mapId)
        {
            if (!Supported(mapId) || double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y)) return null;
            var min = mapId == 12 ? new PointF(-28, -19) : PointF.Empty;
            var max = mapId == 11 ? new PointF(14820, 14881) :
                mapId == 12 ? new PointF(12849, 12858) : new PointF(15000, 15000);
            return new PointF((float)Math.Max(0, Math.Min(1, (x - min.X) / (max.X - min.X))),
                (float)Math.Max(0, Math.Min(1, (max.Y - y) / (max.Y - min.Y))));
        }

        private static bool Coordinate(Dictionary<string, object> data, string key, out double number)
        {
            object value;
            number = 0;
            return data != null && data.TryGetValue(key, out value) && value != null && !(value is bool) &&
                double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var padding = Math.Max(6, (int)(e.Graphics.DpiX / 16));
            var side = Math.Min(ClientSize.Width, ClientSize.Height) - 2 * padding;
            if (side <= 0) return;
            if ((!_position.HasValue && _minutePositions.Length == 0 && _gankPositions.Length == 0 &&
                _level3Positions.Length == 0 && _level4Positions.Length == 0) || _map == null)
            {
                TextRenderer.DrawText(e.Graphics, _ui.Get(!Supported(_mapId) ?
                    UiTextKeys.LeagueEventMapUnsupported : _pointCloudMode ?
                    UiTextKeys.LeagueJungleMapEmpty : UiTextKeys.LeagueEventMapMissing), Font,
                    ClientRectangle, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter);
                return;
            }
            var bounds = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
            e.Graphics.DrawImage(_map, bounds);
            if (_pointCloudMode && _mapId == 11)
                DrawJungleSideOverlay(e.Graphics, bounds);
            if (_pointCloudMode && _heatmapMode)
            {
                var laneColors = new[] { Color.FromArgb(220, 38, 38), Color.FromArgb(245, 158, 11),
                    Color.FromArgb(37, 99, 235) };
                foreach (var cell in _heatmapCells.OrderBy(c => c.Intensity))
                {
                    var diameter = Math.Max(3f, Math.Min(8f, side / 7f * (0.45f + cell.Intensity * 0.9f)));
                    var centerX = bounds.Left + cell.Center.X * bounds.Width;
                    var centerY = bounds.Top + cell.Center.Y * bounds.Height;
                    using (var brush = new SolidBrush(Color.FromArgb(
                        (int)(255f * (0.28f + cell.Intensity * 0.5f)), laneColors[cell.Lane])))
                    using (var pen = new Pen(Color.FromArgb(128, Color.White), Math.Max(1f, e.Graphics.DpiX / 96f)))
                    {
                        e.Graphics.FillEllipse(brush, centerX - diameter / 2, centerY - diameter / 2,
                            diameter, diameter);
                        e.Graphics.DrawEllipse(pen, centerX - diameter / 2, centerY - diameter / 2,
                            diameter, diameter);
                    }
                }
                return;
            }
            if (_pointCloudMode)
            {
                var dark = !FacmThemeRuntime.Current.IsLight;
                using (var topBrush = new SolidBrush(LaneMarkerColor(0, false, dark)))
                using (var midBrush = new SolidBrush(LaneMarkerColor(1, false, dark)))
                using (var botBrush = new SolidBrush(LaneMarkerColor(2, false, dark)))
                using (var topKill = new Pen(LaneMarkerColor(0, true, dark), Math.Max(2f, e.Graphics.DpiX / 48f)))
                using (var midKill = new Pen(LaneMarkerColor(1, true, dark), Math.Max(2f, e.Graphics.DpiX / 48f)))
                using (var botKill = new Pen(LaneMarkerColor(2, true, dark), Math.Max(2f, e.Graphics.DpiX / 48f)))
                using (var level3Pen = new Pen(Color.FromArgb(235, 74, 222, 128), Math.Max(2f, e.Graphics.DpiX / 48f)))
                using (var level4Pen = new Pen(Color.FromArgb(235, 232, 121, 249), Math.Max(2f, e.Graphics.DpiX / 48f)))
                {
                    var minuteRadius = Math.Max(2f, e.Graphics.DpiX / 48f);
                    var gankRadius = Math.Max(4f, e.Graphics.DpiX / 16f);
                    var minuteBrushes = new Brush[] { topBrush, midBrush, botBrush };
                    var killPens = new[] { topKill, midKill, botKill };
                    for (var i = 0; i < _minutePositions.Length; i++)
                    {
                        var p = _minutePositions[i];
                        e.Graphics.FillEllipse(minuteBrushes[_minuteLanes[i]],
                            bounds.Left + p.X * bounds.Width - minuteRadius,
                            bounds.Top + p.Y * bounds.Height - minuteRadius, 2 * minuteRadius, 2 * minuteRadius);
                    }
                    for (var i = 0; i < _gankPositions.Length; i++)
                    {
                        var p = _gankPositions[i];
                        DrawLaneKillMarker(e.Graphics,
                            new PointF(bounds.Left + p.X * bounds.Width, bounds.Top + p.Y * bounds.Height),
                            gankRadius, killPens[_gankLanes[i]]);
                    }
                    var level3Radius = Math.Max(4f, e.Graphics.DpiX / 24f);
                    var level4Radius = Math.Max(6f, e.Graphics.DpiX / 18f);
                    foreach (var p in _level3Positions)
                        e.Graphics.DrawEllipse(level3Pen, bounds.Left + p.X * bounds.Width - level3Radius,
                            bounds.Top + p.Y * bounds.Height - level3Radius, 2 * level3Radius, 2 * level3Radius);
                    foreach (var p in _level4Positions)
                        e.Graphics.DrawEllipse(level4Pen, bounds.Left + p.X * bounds.Width - level4Radius,
                            bounds.Top + p.Y * bounds.Height - level4Radius, 2 * level4Radius, 2 * level4Radius);
                }
                return;
            }
            var point = _position.Value;
            var radius = Math.Max(3, e.Graphics.DpiX / 24);
            var x = bounds.Left + point.X * bounds.Width;
            var y = bounds.Top + point.Y * bounds.Height;
            using (var brush = new SolidBrush(Color.FromArgb(147, 197, 253)))
            using (var outline = new Pen(Color.White, Math.Max(1, e.Graphics.DpiX / 96)))
            {
                e.Graphics.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
                e.Graphics.DrawEllipse(outline, x - radius, y - radius, radius * 2, radius * 2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _map?.Dispose(); _map = null; }
            base.Dispose(disposing);
        }
    }
}
