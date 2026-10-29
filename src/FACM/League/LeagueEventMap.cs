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
            if (IsDisposed) return;
            _pointCloudMode = true;
            _position = null;
            _minutePositions = NormalizePoints(minutePositions, 40 * 14);
            _gankPositions = NormalizePoints(gankPositions, 40 * LeagueJungleAnalysis.MaximumGankMapPoints);
            _level3Positions = NormalizePoints(level3Positions, 40 * LeagueJungleAnalysis.MaximumGankMapPoints);
            _level4Positions = NormalizePoints(level4Positions, 40 * LeagueJungleAnalysis.MaximumGankMapPoints);
            _heatmapCells = BuildHeatmap(minutePositions, gankPositions, _mapId);
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
            if (mapId != 11) return new HeatmapCell[0];
            const int grid = 7;
            var cells = new Dictionary<int, HeatmapAccumulator>();
            Action<IEnumerable<PointF>, int, int> add = (points, limit, weight) =>
            {
                foreach (var raw in (points ?? Enumerable.Empty<PointF>()).Take(limit))
                {
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
                    cell.LaneWeights[LeagueJungleAnalysis.MapZone((long)raw.X, (long)raw.Y)] += weight;
                }
            };
            add(minutes, 40 * 14, 1);
            add(ganks, 40 * LeagueJungleAnalysis.MaximumGankMapPoints, 5);
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
                using (var minuteBrush = new SolidBrush(Color.FromArgb(130, 147, 197, 253)))
                using (var gankBrush = new SolidBrush(Color.FromArgb(180, 251, 146, 60)))
                using (var level3Pen = new Pen(Color.FromArgb(235, 74, 222, 128), Math.Max(2f, e.Graphics.DpiX / 48f)))
                using (var level4Pen = new Pen(Color.FromArgb(235, 232, 121, 249), Math.Max(2f, e.Graphics.DpiX / 48f)))
                {
                    var minuteRadius = Math.Max(2f, e.Graphics.DpiX / 48f);
                    var gankRadius = Math.Max(3f, e.Graphics.DpiX / 32f);
                    foreach (var p in _minutePositions)
                        e.Graphics.FillEllipse(minuteBrush, bounds.Left + p.X * bounds.Width - minuteRadius,
                            bounds.Top + p.Y * bounds.Height - minuteRadius, 2 * minuteRadius, 2 * minuteRadius);
                    foreach (var p in _gankPositions)
                        e.Graphics.FillEllipse(gankBrush, bounds.Left + p.X * bounds.Width - gankRadius,
                            bounds.Top + p.Y * bounds.Height - gankRadius, 2 * gankRadius, 2 * gankRadius);
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
