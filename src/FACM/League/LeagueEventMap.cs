using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
            _position = Position(entry, _mapId);
            AccessibleDescription = _position.HasValue ? AccessibleName :
                _ui.Get(Supported(_mapId) ? UiTextKeys.LeagueEventMapMissing : UiTextKeys.LeagueEventMapUnsupported);
            Invalidate();
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
            if (!_position.HasValue || _map == null)
            {
                TextRenderer.DrawText(e.Graphics, _ui.Get(Supported(_mapId) ?
                    UiTextKeys.LeagueEventMapMissing : UiTextKeys.LeagueEventMapUnsupported), Font,
                    ClientRectangle, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter);
                return;
            }
            var bounds = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
            e.Graphics.DrawImage(_map, bounds);
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
