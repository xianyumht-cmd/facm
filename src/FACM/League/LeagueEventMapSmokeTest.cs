using System;
using System.Collections.Generic;
using System.Drawing;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueEventMapSmokeTest
    {
        internal static void Validate(LeagueMatchTimelineEvent parsed)
        {
            var point = LeagueEventMap.Position(parsed, 11);
            Require(point.HasValue && Math.Abs(point.Value.X - 500f / 14820) < .00001 &&
                Math.Abs(point.Value.Y - (14881f - 600) / 14881) < .00001,
                "Both parsed timeline sources must use the pinned map domain and inverted Y axis.");
            Require(LeagueEventMap.Position(At(-28, -19), 12) == new PointF(0, 1) &&
                LeagueEventMap.Position(At(12849, 12858), 12) == new PointF(1, 0) &&
                LeagueEventMap.Position(At(7500, 7500), 21) == new PointF(.5f, .5f) &&
                LeagueEventMap.Position(At(-100, 30000), 11) == new PointF(0, 0),
                "Howling Abyss offsets, Arena bounds and out-of-domain clipping must match the source.");
            var missing = At(1, 1);
            ((Dictionary<string, object>)missing.Data["position"]).Remove("y");
            Require(LeagueEventMap.Position(missing, 11) == null && LeagueEventMap.Position(null, 11) == null &&
                LeagueEventMap.Position(At(double.NaN, 1), 11) == null &&
                LeagueEventMap.Position(At(1, double.PositiveInfinity), 11) == null &&
                LeagueEventMap.Normalize(double.NaN, 100, 11) == null &&
                LeagueEventMap.Normalize(7500, double.PositiveInfinity, 11) == null &&
                LeagueEventMap.Normalize(7500, 7500, 13) == null &&
                LeagueEventMap.Position(parsed, 99) == null,
                "Absent, nonfinite and unsupported event positions must never become a fabricated marker.");
            foreach (var mapId in new[] { 11, 12, 21 })
                using (var map = new LeagueEventMap(UiTextCatalog.Load(), mapId))
                using (var bitmap = new Bitmap(220, 220))
                {
                    map.Size = bitmap.Size;
                    var center = mapId == 11 ? At(7410, 7440.5) :
                        mapId == 12 ? At(6410.5, 6419.5) : At(7500, 7500);
                    for (var i = 0; i < 8; i++) { map.SetEvent(parsed); map.SetEvent(null); }
                    map.SetEvent(center);
                    map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    Require(bitmap.GetPixel(110, 110).ToArgb() == Color.FromArgb(147, 197, 253).ToArgb(),
                        "Each embedded source map must render the selected marker after rapid selection changes.");
                    if (mapId == 11)
                    {
                        map.SetPointCloud(new[] { new PointF(7410, 7440.5f) },
                            new[] { new PointF(7410, 7440.5f) });
                        map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        Require(bitmap.GetPixel(110, 110).ToArgb() != Color.FromArgb(147, 197, 253).ToArgb(),
                            "The multi-match point cloud must render an independent gank layer over minute samples.");
                        map.SetPointCloud(new[] { new PointF(float.NaN, 1) },
                            new[] { new PointF(float.PositiveInfinity, 1) });
                        map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        map.SetEvent(center);
                        map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        Require(bitmap.GetPixel(110, 110).ToArgb() == Color.FromArgb(147, 197, 253).ToArgb(),
                            "Returning to one selected event must clear both previous point-cloud layers.");
                    }
                    map.SetEvent(null);
                    map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                }
        }

        private static LeagueMatchTimelineEvent At(double x, double y)
        {
            return new LeagueMatchTimelineEvent { Type = "CHAMPION_KILL", Data = new Dictionary<string, object>
                { { "position", new Dictionary<string, object> { { "x", x }, { "y", y } } } } };
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
