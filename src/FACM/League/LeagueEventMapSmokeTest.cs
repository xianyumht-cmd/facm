using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
            using (var overlay = new Bitmap(100, 100))
            using (var graphics = Graphics.FromImage(overlay))
            {
                graphics.Clear(Color.FromArgb(90, 90, 90));
                LeagueEventMap.DrawJungleSideOverlay(graphics, new Rectangle(0, 0, 100, 100));
                var blueSide = overlay.GetPixel(15, 80);
                var redSide = overlay.GetPixel(80, 15);
                Require(blueSide.B > blueSide.R && blueSide.B > blueSide.G &&
                    redSide.R > redSide.G && redSide.R > redSide.B,
                    "Jungle spatial maps must preserve Akari's blue lower-left and red upper-right side fills.");
                var untouched = new Bitmap(1, 1);
                using (untouched)
                using (var empty = Graphics.FromImage(untouched))
                    LeagueEventMap.DrawJungleSideOverlay(empty, Rectangle.Empty);
            }
            var topLane = LeagueEventMap.Normalize(1000, 12000, 11).Value;
            var midLane = LeagueEventMap.Normalize(7410, 7440, 11).Value;
            var botLane = LeagueEventMap.Normalize(11000, 1000, 11).Value;
            Require(LeagueEventMap.LaneIndex(topLane) == 0 &&
                LeagueEventMap.LaneIndex(midLane) == 1 && LeagueEventMap.LaneIndex(botLane) == 2,
                "Jungle scatter lane colors must use the same map-zone projection as heatmap aggregation.");
            var laneHeavy = Enumerable.Range(0, 60).Select(i => new PointF(1000 + i, 12000))
                .Concat(Enumerable.Range(0, 20).Select(i => new PointF(6000 + i, 6000)))
                .Concat(Enumerable.Range(0, 20).Select(i => new PointF(11000 + i, 1000)))
                .ToArray();
            var normalizedHeavy = laneHeavy.Select(p => LeagueEventMap.Normalize(p.X, p.Y, 11).Value).ToArray();
            var balanced = LeagueEventMap.BalanceByLane(normalizedHeavy, 54);
            Require(balanced.Length == 54 &&
                balanced.Count(p => LeagueEventMap.LaneIndex(p) == 0) == 18 &&
                balanced.Count(p => LeagueEventMap.LaneIndex(p) == 1) == 18 &&
                balanced.Count(p => LeagueEventMap.LaneIndex(p) == 2) == 18 &&
                balanced[0] == normalizedHeavy[0] && balanced[17] == normalizedHeavy[59] &&
                balanced[18] == normalizedHeavy[60] && balanced[35] == normalizedHeavy[79] &&
                balanced[36] == normalizedHeavy[80] && balanced[53] == normalizedHeavy[99],
                "Balanced sampling must preserve first/last points per lane and original source ordering.");
            var sparse = normalizedHeavy.Take(60).Concat(normalizedHeavy.Skip(60).Take(3))
                .Concat(normalizedHeavy.Skip(80).Take(5)).ToArray();
            var sparseSample = LeagueEventMap.BalanceByLane(sparse, 20);
            Require(sparseSample.Length == 20 &&
                sparseSample.Count(p => LeagueEventMap.LaneIndex(p) == 0) == 12 &&
                sparseSample.Count(p => LeagueEventMap.LaneIndex(p) == 1) == 3 &&
                sparseSample.Count(p => LeagueEventMap.LaneIndex(p) == 2) == 5 &&
                LeagueEventMap.BalanceByLane(normalizedHeavy, 1).Single() == normalizedHeavy[29] &&
                LeagueEventMap.BalanceByLane(normalizedHeavy, 0).Length == 0 &&
                LeagueEventMap.BalanceByLane(null, 54).Length == 0,
                "Unfilled quotas must flow to remaining lanes, with deterministic midpoint and empty guards.");
            var repeated = Enumerable.Repeat(normalizedHeavy[0], 4)
                .Concat(new[] { normalizedHeavy[60], normalizedHeavy[80] }).ToArray();
            Require(LeagueEventMap.BalanceByLane(repeated, 4).Length == 4 &&
                LeagueEventMap.BalanceByLane(repeated, 4).Count(p => p == normalizedHeavy[0]) == 2,
                "Distinct timeline samples at identical coordinates must not collapse during lane balancing.");
            var topColor = LeagueEventMap.LaneMarkerColor(0, true, false);
            var midColor = LeagueEventMap.LaneMarkerColor(1, true, false);
            var botColor = LeagueEventMap.LaneMarkerColor(2, true, false);
            Require(topColor.R > topColor.G && midColor.R > midColor.B &&
                midColor.G > midColor.B && botColor.B > botColor.R &&
                LeagueEventMap.LaneMarkerColor(0, false, false).A < topColor.A &&
                LeagueEventMap.LaneMarkerColor(1, false, true).A <
                    LeagueEventMap.LaneMarkerColor(1, true, true).A,
                "Pinned lane marker colors must distinguish top/mid/bot and translucent dots from kills.");
            using (var marks = new Bitmap(90, 30))
            using (var g = Graphics.FromImage(marks))
            using (var topPen = new Pen(topColor, 2f))
            using (var midPen = new Pen(midColor, 2f))
            using (var botPen = new Pen(botColor, 2f))
            {
                g.Clear(Color.White);
                LeagueEventMap.DrawLaneKillMarker(g, new PointF(15, 15), 6f, topPen);
                LeagueEventMap.DrawLaneKillMarker(g, new PointF(45, 15), 6f, midPen);
                LeagueEventMap.DrawLaneKillMarker(g, new PointF(75, 15), 6f, botPen);
                Require(marks.GetPixel(15, 15).ToArgb() == topColor.ToArgb() &&
                    marks.GetPixel(45, 15).ToArgb() == midColor.ToArgb() &&
                    marks.GetPixel(75, 15).ToArgb() == botColor.ToArgb() &&
                    marks.GetPixel(15, 9).ToArgb() == Color.White.ToArgb(),
                    "Confirmed gank markers must draw lane-colored diagonal X strokes, not solid dots.");
            }
            var weighted = LeagueEventMap.BuildHeatmap(
                new[] { new PointF(7410, 7440), new PointF(7410, 7440) },
                new[] { new PointF(7410, 7440) }, 11);
            Require(weighted.Length == 1 && weighted[0].Weight == 7 &&
                Math.Abs(weighted[0].Intensity - 1f) < .0001f && weighted[0].Lane == 1,
                "Pinned 7x7 heatmap must weigh source-confirmed ganks five times minute samples.");
            var mixed = LeagueEventMap.BuildHeatmap(
                new[] { new PointF(1000, 12000), new PointF(1000, 12000),
                    new PointF(11000, 1000), new PointF(float.NaN, 1000) },
                new[] { new PointF(11000, 1000), new PointF(float.PositiveInfinity, 1000) }, 11);
            Require(mixed.Length == 2 && mixed[0].Weight == 6 && mixed[0].Lane == 2 &&
                mixed[1].Weight == 2 && mixed[1].Lane == 0 &&
                LeagueEventMap.BuildHeatmap(null, null, 11).Length == 0 &&
                LeagueEventMap.BuildHeatmap(new[] { new PointF(7410, 7440) }, null, 12).Length == 0,
                "Heatmap must reject invalid positions and unsupported maps, preserving lane color ownership.");
            var gridPoints = Enumerable.Range(0, 25).Select(i => new PointF(
                (float)((i % 7 + 0.5) * 14820 / 7), (float)(14881 - (i / 7 + 0.5) * 14881 / 7)));
            Require(LeagueEventMap.BuildHeatmap(gridPoints, null, 11).Length == 14,
                "The pinned heatmap shows at most fourteen weighted grid cells.");
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
                        var scatterPixel = bitmap.GetPixel(110, 110).ToArgb();
                        map.SetHeatmapMode(true);
                        map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        Require(map.HeatmapMode && map.HeatmapCellCount == 1 &&
                            bitmap.GetPixel(110, 110).ToArgb() != scatterPixel,
                            "The opt-in density mode must render weighted heat cells instead of scatter dots.");
                        map.SetHeatmapMode(false);
                        map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        Require(!map.HeatmapMode && bitmap.GetPixel(110, 110).ToArgb() == scatterPixel,
                            "Switching back must restore the original source-confirmed scatter layers.");
                        using (var baseline = new Bitmap(220, 220))
                        {
                            map.SetEvent(At(1000, 1000));
                            map.DrawToBitmap(baseline, new Rectangle(Point.Empty, baseline.Size));
                            var early = new[] { new PointF(7410, 7440.5f) };
                            map.SetPointCloud(null, null, early, null);
                            map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            Require(ChangedTintPixels(baseline, bitmap, true) > 3,
                                "Level-3 early kill positions must draw even when no lane-classified gank dots exist.");
                            map.SetPointCloud(null, null, null, early);
                            map.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            Require(ChangedTintPixels(baseline, bitmap, false) > 3,
                                "Level-4 early kill positions must have their independent visible ring layer.");
                        }
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
        private static int ChangedTintPixels(Bitmap left, Bitmap right, bool green)
        {
            var changed = 0;
            for (var x = 99; x <= 121; x++)
                for (var y = 99; y <= 121; y++)
                {
                    var after = right.GetPixel(x, y);
                    if (left.GetPixel(x, y).ToArgb() != after.ToArgb() &&
                        (green ? after.G > after.R + 40 && after.G > after.B + 30 :
                            after.R > after.G + 30 && after.B > after.G + 30)) changed++;
                }
            return changed;
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
