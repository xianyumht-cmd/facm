using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FACM.Pets
{
    internal static class BuiltInFlyingPetArtService
    {
        public const string BeeUrl = "builtin://facm/flying/bee-v2";
        public const string DragonflyUrl = "builtin://facm/flying/dragonfly-v2";
        public const string ButterflyUrl = "builtin://facm/flying/butterfly-v2";
        public const string MothUrl = "builtin://facm/flying/moth-v2";

        public const int BeeFrameSize = 104;
        public const int DragonflyFrameSize = 128;
        public const int ButterflyFrameSize = 112;
        public const int MothFrameSize = 112;
        public const int FrameCount = 4;

        public static Bitmap TryCreate(string spriteUrl)
        {
            if (string.Equals(spriteUrl, BeeUrl, StringComparison.OrdinalIgnoreCase))
                return CreateSheet(BeeFrameSize, DrawBee);
            if (string.Equals(spriteUrl, DragonflyUrl, StringComparison.OrdinalIgnoreCase))
                return CreateSheet(DragonflyFrameSize, DrawDragonfly);
            if (string.Equals(spriteUrl, ButterflyUrl, StringComparison.OrdinalIgnoreCase))
                return CreateSheet(ButterflyFrameSize, DrawButterfly);
            if (string.Equals(spriteUrl, MothUrl, StringComparison.OrdinalIgnoreCase))
                return CreateSheet(MothFrameSize, DrawMoth);
            return null;
        }

        public static bool IsBuiltIn(string spriteUrl)
        {
            return string.Equals(spriteUrl, BeeUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(spriteUrl, DragonflyUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(spriteUrl, ButterflyUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(spriteUrl, MothUrl, StringComparison.OrdinalIgnoreCase);
        }

        private static Bitmap CreateSheet(int frameSize, Action<Graphics, int, int> draw)
        {
            var sheet = new Bitmap(frameSize * FrameCount, frameSize, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(sheet))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

                for (var frame = 0; frame < FrameCount; frame++)
                {
                    var state = graphics.Save();
                    graphics.TranslateTransform(frame * frameSize, 0f);
                    draw(graphics, frame, frameSize);
                    graphics.Restore(state);
                }
            }
            return sheet;
        }

        private static void DrawBee(Graphics graphics, int frame, int size)
        {
            var scale = size / 104f;
            var state = graphics.Save();
            graphics.ScaleTransform(scale, scale);

            var wingPhase = new[] { -14f, -5f, 5f, -7f }[frame % FrameCount];
            DrawBeeWing(graphics, new PointF(57f, 45f), new PointF(40f, 24f + wingPhase * 0.42f), true);
            DrawBeeWing(graphics, new PointF(57f, 59f), new PointF(40f, 80f - wingPhase * 0.42f), false);

            using (var legPen = RoundedPen(Color.FromArgb(218, 43, 34, 22), 2.0f))
            {
                DrawLeg(graphics, legPen, 56f, 45f, 42f, 31f, 31f, 27f);
                DrawLeg(graphics, legPen, 54f, 51f, 38f, 49f, 26f, 45f);
                DrawLeg(graphics, legPen, 55f, 58f, 41f, 72f, 29f, 77f);
                DrawLeg(graphics, legPen, 67f, 45f, 62f, 29f, 56f, 22f);
                DrawLeg(graphics, legPen, 67f, 59f, 62f, 75f, 56f, 82f);
                DrawLeg(graphics, legPen, 75f, 53f, 86f, 65f, 94f, 69f);
            }

            using (var abdomenBrush = new LinearGradientBrush(
                new RectangleF(26f, 41f, 45f, 24f),
                Color.FromArgb(255, 246, 193, 52),
                Color.FromArgb(255, 178, 106, 20),
                LinearGradientMode.Horizontal))
            using (var outline = new Pen(Color.FromArgb(235, 61, 43, 24), 1.7f))
            {
                graphics.FillEllipse(abdomenBrush, 26f, 41f, 45f, 24f);
                graphics.DrawEllipse(outline, 26f, 41f, 45f, 24f);
            }

            using (var stripe = new Pen(Color.FromArgb(235, 61, 45, 24), 4.2f))
            {
                graphics.DrawArc(stripe, 34f, 42f, 15f, 22f, 78f, 204f);
                graphics.DrawArc(stripe, 46f, 42f, 15f, 22f, 78f, 204f);
                graphics.DrawArc(stripe, 57f, 43f, 11f, 20f, 78f, 204f);
            }

            using (var thoraxBrush = new LinearGradientBrush(
                new RectangleF(62f, 39f, 24f, 27f),
                Color.FromArgb(255, 111, 78, 35),
                Color.FromArgb(255, 55, 42, 27),
                LinearGradientMode.Vertical))
            using (var thoraxOutline = new Pen(Color.FromArgb(235, 50, 37, 24), 1.5f))
            {
                graphics.FillEllipse(thoraxBrush, 62f, 39f, 24f, 27f);
                graphics.DrawEllipse(thoraxOutline, 62f, 39f, 24f, 27f);
            }

            using (var headBrush = new SolidBrush(Color.FromArgb(255, 72, 51, 29)))
            using (var eyeBrush = new SolidBrush(Color.FromArgb(255, 28, 26, 21)))
            using (var highlight = new SolidBrush(Color.FromArgb(165, 230, 198, 128)))
            {
                graphics.FillEllipse(headBrush, 80f, 42f, 18f, 21f);
                graphics.FillEllipse(eyeBrush, 89f, 43f, 6.2f, 7.0f);
                graphics.FillEllipse(eyeBrush, 89f, 55f, 6.2f, 7.0f);
                graphics.FillEllipse(highlight, 91.5f, 44.5f, 1.7f, 1.7f);
                graphics.FillEllipse(highlight, 91.5f, 57f, 1.7f, 1.7f);
            }

            using (var antenna = RoundedPen(Color.FromArgb(225, 53, 42, 27), 1.45f))
            {
                graphics.DrawBezier(antenna, new PointF(95f, 47f), new PointF(100f, 43f), new PointF(101f, 39f), new PointF(103f, 37f));
                graphics.DrawBezier(antenna, new PointF(95f, 58f), new PointF(100f, 62f), new PointF(101f, 66f), new PointF(103f, 68f));
            }

            graphics.Restore(state);
        }

        private static void DrawBeeWing(Graphics graphics, PointF root, PointF tip, bool upper)
        {
            var sign = upper ? -1f : 1f;
            using (var path = new GraphicsPath())
            {
                path.StartFigure();
                path.AddBezier(root, new PointF(root.X - 8f, root.Y + sign * 3f), new PointF(tip.X - 10f, tip.Y - sign * 5f), tip);
                path.AddBezier(tip, new PointF(tip.X + 16f, tip.Y + sign * 2f), new PointF(root.X + 2f, root.Y + sign * 13f), root);
                path.CloseFigure();

                using (var brush = new SolidBrush(Color.FromArgb(106, 224, 239, 241)))
                using (var pen = new Pen(Color.FromArgb(152, 91, 113, 111), 1.1f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(pen, path);
                }
            }

            using (var vein = new Pen(Color.FromArgb(86, 83, 106, 103), 0.9f))
            {
                graphics.DrawLine(vein, root, tip);
                var mid = new PointF((root.X + tip.X) * 0.5f, (root.Y + tip.Y) * 0.5f);
                graphics.DrawLine(vein, new PointF(root.X - 1f, root.Y + sign * 4f), new PointF(mid.X + 4f, mid.Y - sign * 2f));
            }
        }

        private static void DrawDragonfly(Graphics graphics, int frame, int size)
        {
            var scale = size / 128f;
            var state = graphics.Save();
            graphics.ScaleTransform(scale, scale);
            var phase = new[] { -8f, -2f, 7f, 1f }[frame % FrameCount];

            DrawDragonflyWing(graphics, new PointF(68f, 56f), new PointF(34f, 18f + phase), 19f, true);
            DrawDragonflyWing(graphics, new PointF(67f, 60f), new PointF(28f, 43f + phase * 0.20f), 14f, true);
            DrawDragonflyWing(graphics, new PointF(68f, 72f), new PointF(34f, 110f - phase), 19f, false);
            DrawDragonflyWing(graphics, new PointF(67f, 68f), new PointF(28f, 85f - phase * 0.20f), 14f, false);

            using (var abdomenBrush = new LinearGradientBrush(
                new RectangleF(20f, 60f, 72f, 10f),
                Color.FromArgb(255, 70, 160, 146),
                Color.FromArgb(255, 24, 73, 78),
                LinearGradientMode.Horizontal))
            using (var outline = new Pen(Color.FromArgb(235, 22, 58, 62), 1.35f))
            {
                graphics.FillRoundedCapsule(abdomenBrush, 19f, 60f, 74f, 10f);
                graphics.DrawLine(outline, 24f, 60f, 88f, 60f);
                graphics.DrawLine(outline, 24f, 70f, 88f, 70f);
            }

            using (var segmentPen = new Pen(Color.FromArgb(155, 11, 55, 55), 1f))
            {
                for (var x = 29f; x <= 78f; x += 7f)
                    graphics.DrawLine(segmentPen, x, 61.5f, x, 68.5f);
            }

            using (var thoraxBrush = new LinearGradientBrush(
                new RectangleF(82f, 53f, 25f, 25f),
                Color.FromArgb(255, 57, 137, 111),
                Color.FromArgb(255, 31, 82, 75),
                LinearGradientMode.Vertical))
            {
                graphics.FillEllipse(thoraxBrush, 82f, 53f, 25f, 25f);
            }

            using (var headBrush = new SolidBrush(Color.FromArgb(255, 52, 96, 75)))
            using (var eyeBrush = new SolidBrush(Color.FromArgb(255, 109, 45, 49)))
            using (var eyeGlow = new SolidBrush(Color.FromArgb(120, 235, 150, 135)))
            {
                graphics.FillEllipse(headBrush, 101f, 55f, 20f, 21f);
                graphics.FillEllipse(eyeBrush, 109f, 56f, 9f, 8f);
                graphics.FillEllipse(eyeBrush, 109f, 67f, 9f, 8f);
                graphics.FillEllipse(eyeGlow, 113f, 57.5f, 2f, 2f);
                graphics.FillEllipse(eyeGlow, 113f, 69f, 2f, 2f);
            }

            using (var legPen = RoundedPen(Color.FromArgb(175, 27, 55, 51), 1.35f))
            {
                DrawLeg(graphics, legPen, 91f, 72f, 85f, 82f, 79f, 87f);
                DrawLeg(graphics, legPen, 97f, 73f, 96f, 84f, 92f, 90f);
                DrawLeg(graphics, legPen, 102f, 72f, 108f, 82f, 114f, 86f);
            }

            graphics.Restore(state);
        }

        private static void DrawDragonflyWing(Graphics graphics, PointF root, PointF tip, float width, bool upper)
        {
            var sign = upper ? -1f : 1f;
            using (var path = new GraphicsPath())
            {
                path.StartFigure();
                path.AddBezier(root, new PointF(root.X - 8f, root.Y + sign * 1f), new PointF(tip.X + width, tip.Y - sign * 5f), tip);
                path.AddBezier(tip, new PointF(tip.X + width * 0.45f, tip.Y + sign * 8f), new PointF(root.X - 2f, root.Y + sign * 8f), root);
                path.CloseFigure();

                using (var brush = new SolidBrush(Color.FromArgb(78, 194, 225, 232)))
                using (var pen = new Pen(Color.FromArgb(115, 68, 111, 119), 1.0f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(pen, path);
                }
            }

            using (var vein = new Pen(Color.FromArgb(72, 59, 100, 108), 0.82f))
            {
                graphics.DrawLine(vein, root, tip);
                var mid = new PointF((root.X + tip.X) * 0.53f, (root.Y + tip.Y) * 0.53f);
                graphics.DrawLine(vein, new PointF(root.X - 3f, root.Y + sign * 4f), new PointF(mid.X + 3f, mid.Y));
                graphics.DrawLine(vein, new PointF(root.X - 7f, root.Y + sign * 6f), new PointF(tip.X + width * 0.25f, tip.Y + sign * 3f));
            }

            using (var stigma = new Pen(Color.FromArgb(150, 53, 82, 86), 2.2f))
            {
                stigma.StartCap = LineCap.Round;
                stigma.EndCap = LineCap.Round;
                graphics.DrawLine(stigma, tip.X + 3f, tip.Y + sign * 1.5f, tip.X + 9f, tip.Y + sign * 2.5f);
            }
        }

        private static void DrawButterfly(Graphics graphics, int frame, int size)
        {
            var scale = size / 112f;
            var state = graphics.Save();
            graphics.ScaleTransform(scale, scale);
            var open = new[] { 1.00f, 0.70f, 0.38f, 0.74f }[frame % FrameCount];

            DrawButterflyWing(graphics, new PointF(62f, 51f), open, true);
            DrawButterflyWing(graphics, new PointF(62f, 61f), open, false);

            using (var bodyBrush = new LinearGradientBrush(
                new RectangleF(48f, 51f, 42f, 10f),
                Color.FromArgb(255, 91, 62, 42),
                Color.FromArgb(255, 38, 30, 27),
                LinearGradientMode.Horizontal))
            using (var outline = new Pen(Color.FromArgb(220, 44, 34, 30), 1.1f))
            {
                graphics.FillEllipse(bodyBrush, 47f, 51f, 42f, 10f);
                graphics.DrawEllipse(outline, 47f, 51f, 42f, 10f);
            }

            using (var head = new SolidBrush(Color.FromArgb(255, 51, 38, 32)))
            using (var eye = new SolidBrush(Color.FromArgb(255, 20, 20, 19)))
            {
                graphics.FillEllipse(head, 84f, 49f, 14f, 14f);
                graphics.FillEllipse(eye, 92f, 51f, 3f, 3f);
                graphics.FillEllipse(eye, 92f, 58f, 3f, 3f);
            }

            using (var antenna = RoundedPen(Color.FromArgb(215, 55, 43, 34), 1.2f))
            {
                graphics.DrawBezier(antenna, new PointF(95f, 52f), new PointF(102f, 46f), new PointF(104f, 40f), new PointF(108f, 38f));
                graphics.DrawBezier(antenna, new PointF(95f, 60f), new PointF(102f, 66f), new PointF(104f, 72f), new PointF(108f, 74f));
            }

            graphics.Restore(state);
        }

        private static void DrawButterflyWing(Graphics graphics, PointF root, float open, bool upper)
        {
            var sign = upper ? -1f : 1f;
            var farY = root.Y + sign * (19f + 29f * open);
            var foreX = 24f + 8f * (1f - open);
            var hindY = root.Y + sign * (10f + 20f * open);

            using (var fore = new GraphicsPath())
            {
                fore.StartFigure();
                fore.AddBezier(root, new PointF(53f, root.Y + sign * 4f), new PointF(34f, farY - sign * 12f), new PointF(foreX, farY));
                fore.AddBezier(new PointF(foreX, farY), new PointF(14f, farY - sign * 2f), new PointF(21f, root.Y + sign * 22f), new PointF(39f, root.Y + sign * 11f));
                fore.AddBezier(new PointF(39f, root.Y + sign * 11f), new PointF(49f, root.Y + sign * 7f), new PointF(56f, root.Y + sign * 3f), root);
                fore.CloseFigure();

                using (var brush = new LinearGradientBrush(
                    new RectangleF(14f, Math.Min(root.Y, farY), 50f, Math.Abs(farY - root.Y) + 1f),
                    Color.FromArgb(232, 112, 178, 239),
                    Color.FromArgb(232, 93, 64, 171),
                    LinearGradientMode.Horizontal))
                using (var pen = new Pen(Color.FromArgb(220, 55, 55, 108), 1.4f))
                {
                    graphics.FillPath(brush, fore);
                    graphics.DrawPath(pen, fore);
                }
            }

            using (var hind = new GraphicsPath())
            {
                hind.StartFigure();
                hind.AddBezier(root, new PointF(51f, root.Y + sign * 5f), new PointF(34f, hindY + sign * 7f), new PointF(29f, hindY));
                hind.AddBezier(new PointF(29f, hindY), new PointF(25f, root.Y + sign * 13f), new PointF(42f, root.Y + sign * 8f), root);
                hind.CloseFigure();
                using (var brush = new SolidBrush(Color.FromArgb(215, 81, 78, 171)))
                using (var pen = new Pen(Color.FromArgb(190, 50, 52, 99), 1.1f))
                {
                    graphics.FillPath(brush, hind);
                    graphics.DrawPath(pen, hind);
                }
            }

            using (var vein = new Pen(Color.FromArgb(120, 54, 58, 110), 0.9f))
            {
                graphics.DrawLine(vein, root, new PointF(foreX + 6f, farY - sign * 5f));
                graphics.DrawLine(vein, root, new PointF(31f, root.Y + sign * (15f + 15f * open)));
                graphics.DrawLine(vein, new PointF(45f, root.Y + sign * 7f), new PointF(23f, root.Y + sign * (18f + 17f * open)));
            }

            using (var outerSpot = new SolidBrush(Color.FromArgb(190, 238, 194, 67)))
            using (var innerSpot = new SolidBrush(Color.FromArgb(180, 43, 45, 87)))
            {
                var spotY = root.Y + sign * (13f + 18f * open);
                graphics.FillEllipse(outerSpot, 27f, spotY - 5f, 11f, 10f);
                graphics.FillEllipse(innerSpot, 30f, spotY - 2f, 5f, 4f);
            }
        }

        private static void DrawMoth(Graphics graphics, int frame, int size)
        {
            var scale = size / 112f;
            var state = graphics.Save();
            graphics.ScaleTransform(scale, scale);
            var open = new[] { 0.96f, 0.72f, 0.50f, 0.78f }[frame % FrameCount];

            DrawMothWing(graphics, new PointF(63f, 51f), open, true);
            DrawMothWing(graphics, new PointF(63f, 61f), open, false);

            using (var body = new LinearGradientBrush(
                new RectangleF(47f, 50f, 43f, 12f),
                Color.FromArgb(255, 145, 126, 99),
                Color.FromArgb(255, 69, 59, 49),
                LinearGradientMode.Horizontal))
            {
                graphics.FillEllipse(body, 47f, 50f, 43f, 12f);
            }

            using (var thorax = new SolidBrush(Color.FromArgb(235, 116, 99, 80)))
            {
                graphics.FillEllipse(thorax, 71f, 47f, 21f, 18f);
                DrawFuzz(graphics, 76f, 50f);
                DrawFuzz(graphics, 81f, 48f);
                DrawFuzz(graphics, 84f, 55f);
                DrawFuzz(graphics, 78f, 58f);
            }

            using (var head = new SolidBrush(Color.FromArgb(255, 78, 67, 55)))
            using (var eye = new SolidBrush(Color.FromArgb(255, 30, 30, 27)))
            {
                graphics.FillEllipse(head, 86f, 49f, 15f, 15f);
                graphics.FillEllipse(eye, 94f, 51f, 3.4f, 3.4f);
                graphics.FillEllipse(eye, 94f, 59f, 3.4f, 3.4f);
            }

            DrawFeatherAntenna(graphics, new PointF(97f, 52f), true);
            DrawFeatherAntenna(graphics, new PointF(97f, 61f), false);

            graphics.Restore(state);
        }

        private static void DrawMothWing(Graphics graphics, PointF root, float open, bool upper)
        {
            var sign = upper ? -1f : 1f;
            var farY = root.Y + sign * (16f + 26f * open);
            using (var path = new GraphicsPath())
            {
                path.StartFigure();
                path.AddBezier(root, new PointF(54f, root.Y + sign * 2f), new PointF(31f, farY - sign * 8f), new PointF(20f, farY));
                path.AddBezier(new PointF(20f, farY), new PointF(15f, farY + sign * 8f), new PointF(31f, root.Y + sign * 23f), new PointF(45f, root.Y + sign * 10f));
                path.AddBezier(new PointF(45f, root.Y + sign * 10f), new PointF(54f, root.Y + sign * 6f), new PointF(59f, root.Y + sign * 2f), root);
                path.CloseFigure();

                using (var brush = new LinearGradientBrush(
                    new RectangleF(15f, Math.Min(root.Y, farY), 50f, Math.Abs(farY - root.Y) + 1f),
                    Color.FromArgb(235, 181, 164, 133),
                    Color.FromArgb(235, 101, 89, 74),
                    LinearGradientMode.Horizontal))
                using (var pen = new Pen(Color.FromArgb(215, 79, 69, 58), 1.25f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(pen, path);
                }
            }

            using (var band = new Pen(Color.FromArgb(95, 72, 66, 59), 2.2f))
            {
                var bandY = root.Y + sign * (10f + 14f * open);
                graphics.DrawBezier(band, new PointF(50f, root.Y + sign * 4f), new PointF(41f, bandY), new PointF(30f, bandY), new PointF(23f, farY - sign * 2f));
            }

            using (var marking = new SolidBrush(Color.FromArgb(125, 67, 62, 56)))
            using (var halo = new SolidBrush(Color.FromArgb(95, 214, 197, 158)))
            {
                var y = root.Y + sign * (12f + 15f * open);
                graphics.FillEllipse(halo, 27f, y - 6f, 14f, 12f);
                graphics.FillEllipse(marking, 31f, y - 3f, 7f, 6f);
            }
        }

        private static void DrawFeatherAntenna(Graphics graphics, PointF root, bool upper)
        {
            var sign = upper ? -1f : 1f;
            using (var stem = RoundedPen(Color.FromArgb(215, 91, 78, 62), 1.15f))
            {
                var p1 = new PointF(root.X + 5f, root.Y + sign * 5f);
                var p2 = new PointF(root.X + 8f, root.Y + sign * 12f);
                var tip = new PointF(root.X + 10f, root.Y + sign * 18f);
                graphics.DrawBezier(stem, root, p1, p2, tip);

                using (var barb = new Pen(Color.FromArgb(160, 104, 89, 70), 0.8f))
                {
                    graphics.DrawLine(barb, root.X + 4f, root.Y + sign * 5f, root.X + 1f, root.Y + sign * 8f);
                    graphics.DrawLine(barb, root.X + 6f, root.Y + sign * 9f, root.X + 3f, root.Y + sign * 12f);
                    graphics.DrawLine(barb, root.X + 7.5f, root.Y + sign * 13f, root.X + 5f, root.Y + sign * 16f);
                }
            }
        }

        private static void DrawFuzz(Graphics graphics, float x, float y)
        {
            using (var fuzz = new SolidBrush(Color.FromArgb(95, 210, 191, 157)))
                graphics.FillEllipse(fuzz, x, y, 5f, 4f);
        }

        private static Pen RoundedPen(Color color, float width)
        {
            return new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        }

        private static void DrawLeg(Graphics graphics, Pen pen, float x1, float y1, float x2, float y2, float x3, float y3)
        {
            graphics.DrawLine(pen, x1, y1, x2, y2);
            graphics.DrawLine(pen, x2, y2, x3, y3);
        }

        private static void FillRoundedCapsule(this Graphics graphics, Brush brush, float x, float y, float width, float height)
        {
            using (var path = new GraphicsPath())
            {
                path.AddArc(x, y, height, height, 90f, 180f);
                path.AddArc(x + width - height, y, height, height, 270f, 180f);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }
    }
}
