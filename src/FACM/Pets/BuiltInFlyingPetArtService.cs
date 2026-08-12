using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FACM.Pets
{
    internal static class BuiltInFlyingPetArtService
    {
        public const string BeeUrl = "builtin://facm/flying/bee-v1";
        public const string DragonflyUrl = "builtin://facm/flying/dragonfly-v1";
        public const string ButterflyUrl = "builtin://facm/flying/butterfly-v1";
        public const string MothUrl = "builtin://facm/flying/moth-v1";
        public const int DefaultFrameSize = 96;
        public const int DragonflyFrameSize = 112;
        public const int FrameCount = 4;

        public static Bitmap TryCreate(string spriteUrl)
        {
            if (string.Equals(spriteUrl, BeeUrl, StringComparison.OrdinalIgnoreCase))
                return CreateSheet(DefaultFrameSize, DrawBee);
            if (string.Equals(spriteUrl, DragonflyUrl, StringComparison.OrdinalIgnoreCase))
                return CreateSheet(DragonflyFrameSize, DrawDragonfly);
            if (string.Equals(spriteUrl, ButterflyUrl, StringComparison.OrdinalIgnoreCase))
                return CreateSheet(DefaultFrameSize, DrawButterfly);
            if (string.Equals(spriteUrl, MothUrl, StringComparison.OrdinalIgnoreCase))
                return CreateSheet(DefaultFrameSize, DrawMoth);
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
            var scale = size / 96f;
            var state = graphics.Save();
            graphics.ScaleTransform(scale, scale);

            var wingLift = new[] { -16f, -8f, 1f, -9f }[frame % FrameCount];
            DrawBeeWing(graphics, new PointF(52f, 42f), new PointF(40f, 22f + wingLift * 0.35f), true);
            DrawBeeWing(graphics, new PointF(52f, 54f), new PointF(40f, 74f - wingLift * 0.35f), false);

            using (var legPen = RoundedPen(Color.FromArgb(210, 34, 29, 20), 2f))
            {
                DrawLeg(graphics, legPen, 50f, 43f, 38f, 30f, 28f, 27f);
                DrawLeg(graphics, legPen, 49f, 48f, 34f, 47f, 24f, 43f);
                DrawLeg(graphics, legPen, 50f, 54f, 37f, 66f, 27f, 69f);
                DrawLeg(graphics, legPen, 61f, 44f, 57f, 29f, 52f, 22f);
                DrawLeg(graphics, legPen, 62f, 54f, 58f, 69f, 53f, 76f);
                DrawLeg(graphics, legPen, 69f, 49f, 79f, 60f, 86f, 65f);
            }

            using (var bodyBrush = new LinearGradientBrush(new RectangleF(30f, 37f, 43f, 22f), Color.FromArgb(255, 238, 177, 42), Color.FromArgb(255, 164, 101, 22), LinearGradientMode.Horizontal))
            using (var outline = new Pen(Color.FromArgb(235, 55, 39, 22), 1.7f))
            {
                graphics.FillEllipse(bodyBrush, 29f, 37f, 44f, 22f);
                graphics.DrawEllipse(outline, 29f, 37f, 44f, 22f);
            }

            using (var stripe = new Pen(Color.FromArgb(225, 55, 42, 24), 4.2f))
            {
                graphics.DrawArc(stripe, 38f, 38f, 14f, 20f, 78f, 205f);
                graphics.DrawArc(stripe, 50f, 38f, 13f, 20f, 78f, 205f);
            }

            using (var headBrush = new SolidBrush(Color.FromArgb(255, 64, 48, 29)))
            using (var eyeBrush = new SolidBrush(Color.FromArgb(255, 22, 24, 19)))
            {
                graphics.FillEllipse(headBrush, 68f, 40f, 17f, 18f);
                graphics.FillEllipse(eyeBrush, 76f, 41f, 5.5f, 6f);
                graphics.FillEllipse(eyeBrush, 76f, 51f, 5.5f, 6f);
            }

            using (var antenna = RoundedPen(Color.FromArgb(220, 48, 39, 26), 1.5f))
            {
                graphics.DrawLine(antenna, 82f, 43f, 91f, 37f);
                graphics.DrawLine(antenna, 82f, 54f, 91f, 60f);
            }

            graphics.Restore(state);
        }

        private static void DrawBeeWing(Graphics graphics, PointF root, PointF tip, bool upper)
        {
            var sign = upper ? -1f : 1f;
            using (var path = new GraphicsPath())
            {
                path.StartFigure();
                path.AddBezier(root, new PointF(47f, root.Y + sign * 4f), new PointF(tip.X - 9f, tip.Y - sign * 4f), tip);
                path.AddBezier(tip, new PointF(tip.X + 15f, tip.Y + sign * 1f), new PointF(59f, root.Y + sign * 12f), root);
                path.CloseFigure();
                using (var brush = new SolidBrush(Color.FromArgb(105, 220, 236, 238)))
                using (var pen = new Pen(Color.FromArgb(145, 96, 113, 108), 1.1f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(pen, path);
                }
            }
        }

        private static void DrawDragonfly(Graphics graphics, int frame, int size)
        {
            var scale = size / 112f;
            var state = graphics.Save();
            graphics.ScaleTransform(scale, scale);
            var phase = new[] { -7f, -2f, 6f, 0f }[frame % FrameCount];

            DrawDragonflyWing(graphics, new PointF(58f, 50f), new PointF(31f, 19f + phase), true, true);
            DrawDragonflyWing(graphics, new PointF(58f, 50f), new PointF(27f, 40f + phase * 0.25f), false, true);
            DrawDragonflyWing(graphics, new PointF(58f, 62f), new PointF(31f, 93f - phase), true, false);
            DrawDragonflyWing(graphics, new PointF(58f, 62f), new PointF(27f, 72f - phase * 0.25f), false, false);

            using (var abdomenBrush = new LinearGradientBrush(new RectangleF(24f, 52f, 60f, 9f), Color.FromArgb(255, 48, 132, 127), Color.FromArgb(255, 28, 70, 76), LinearGradientMode.Horizontal))
            using (var outline = new Pen(Color.FromArgb(235, 25, 48, 52), 1.4f))
            {
                graphics.FillRoundedCapsule(abdomenBrush, 23f, 52f, 61f, 9f);
                graphics.DrawLine(outline, 27f, 52f, 80f, 52f);
                graphics.DrawLine(outline, 27f, 61f, 80f, 61f);
            }

            using (var segmentPen = new Pen(Color.FromArgb(135, 15, 50, 49), 1f))
            {
                for (var x = 31f; x <= 66f; x += 7f)
                    graphics.DrawLine(segmentPen, x, 53f, x, 60f);
            }

            using (var thoraxBrush = new SolidBrush(Color.FromArgb(255, 38, 105, 91)))
            using (var headBrush = new SolidBrush(Color.FromArgb(255, 49, 90, 70)))
            using (var eyeBrush = new SolidBrush(Color.FromArgb(255, 90, 40, 39)))
            {
                graphics.FillEllipse(thoraxBrush, 72f, 46f, 20f, 21f);
                graphics.FillEllipse(headBrush, 87f, 48f, 16f, 17f);
                graphics.FillEllipse(eyeBrush, 95f, 49f, 6f, 6f);
                graphics.FillEllipse(eyeBrush, 95f, 58f, 6f, 6f);
            }

            graphics.Restore(state);
        }

        private static void DrawDragonflyWing(Graphics graphics, PointF root, PointF tip, bool longWing, bool upper)
        {
            var sign = upper ? -1f : 1f;
            using (var path = new GraphicsPath())
            {
                var width = longWing ? 15f : 11f;
                path.StartFigure();
                path.AddBezier(root, new PointF(root.X - 8f, root.Y + sign * 2f), new PointF(tip.X + width, tip.Y - sign * 4f), tip);
                path.AddBezier(tip, new PointF(tip.X + width * 0.4f, tip.Y + sign * 7f), new PointF(root.X - 2f, root.Y + sign * 7f), root);
                path.CloseFigure();
                using (var brush = new SolidBrush(Color.FromArgb(78, 185, 221, 228)))
                using (var pen = new Pen(Color.FromArgb(105, 66, 110, 118), 1f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(pen, path);
                }
            }
            using (var vein = new Pen(Color.FromArgb(65, 62, 102, 109), 0.85f))
                graphics.DrawLine(vein, root, tip);
        }

        private static void DrawButterfly(Graphics graphics, int frame, int size)
        {
            var scale = size / 96f;
            var state = graphics.Save();
            graphics.ScaleTransform(scale, scale);
            var open = new[] { 1.00f, 0.68f, 0.34f, 0.72f }[frame % FrameCount];

            DrawButterflyWing(graphics, new PointF(53f, 45f), open, true);
            DrawButterflyWing(graphics, new PointF(53f, 51f), open, false);

            using (var bodyBrush = new LinearGradientBrush(new RectangleF(42f, 44f, 35f, 8f), Color.FromArgb(255, 74, 51, 37), Color.FromArgb(255, 38, 31, 28), LinearGradientMode.Horizontal))
            {
                graphics.FillEllipse(bodyBrush, 40f, 43.5f, 36f, 9f);
            }
            using (var head = new SolidBrush(Color.FromArgb(255, 48, 36, 31)))
            using (var eye = new SolidBrush(Color.FromArgb(255, 20, 21, 20)))
            {
                graphics.FillEllipse(head, 71f, 42f, 12f, 12f);
                graphics.FillEllipse(eye, 78f, 44f, 2.8f, 2.8f);
                graphics.FillEllipse(eye, 78f, 49f, 2.8f, 2.8f);
            }
            using (var antenna = RoundedPen(Color.FromArgb(210, 53, 42, 34), 1.3f))
            {
                graphics.DrawBezier(antenna, new PointF(80f, 45f), new PointF(86f, 39f), new PointF(89f, 36f), new PointF(93f, 35f));
                graphics.DrawBezier(antenna, new PointF(80f, 51f), new PointF(86f, 57f), new PointF(89f, 60f), new PointF(93f, 61f));
            }

            graphics.Restore(state);
        }

        private static void DrawButterflyWing(Graphics graphics, PointF root, float open, bool upper)
        {
            var sign = upper ? -1f : 1f;
            var farY = root.Y + sign * (16f + 24f * open);
            var farX = 22f + 7f * (1f - open);
            using (var path = new GraphicsPath())
            {
                path.StartFigure();
                path.AddBezier(root, new PointF(46f, root.Y + sign * 4f), new PointF(28f, farY - sign * 13f), new PointF(farX, farY));
                path.AddBezier(new PointF(farX, farY), new PointF(13f, farY - sign * 3f), new PointF(22f, root.Y + sign * 18f), new PointF(36f, root.Y + sign * 8f));
                path.AddBezier(new PointF(36f, root.Y + sign * 8f), new PointF(43f, root.Y + sign * 4f), new PointF(48f, root.Y + sign * 2f), root);
                path.CloseFigure();
                using (var brush = new LinearGradientBrush(new RectangleF(14f, Math.Min(root.Y, farY), 40f, Math.Abs(farY - root.Y) + 1f), Color.FromArgb(220, 102, 161, 232), Color.FromArgb(220, 103, 70, 170), LinearGradientMode.Horizontal))
                using (var pen = new Pen(Color.FromArgb(210, 55, 55, 105), 1.3f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(pen, path);
                }
            }
            using (var spot = new SolidBrush(Color.FromArgb(180, 226, 187, 70)))
            {
                var spotY = root.Y + sign * (10f + 14f * open);
                graphics.FillEllipse(spot, 26f, spotY - 4f, 9f, 8f);
            }
        }

        private static void DrawMoth(Graphics graphics, int frame, int size)
        {
            var scale = size / 96f;
            var state = graphics.Save();
            graphics.ScaleTransform(scale, scale);
            var open = new[] { 0.96f, 0.70f, 0.48f, 0.76f }[frame % FrameCount];

            DrawMothWing(graphics, new PointF(54f, 45f), open, true);
            DrawMothWing(graphics, new PointF(54f, 51f), open, false);

            using (var body = new LinearGradientBrush(new RectangleF(39f, 43f, 39f, 10f), Color.FromArgb(255, 134, 117, 93), Color.FromArgb(255, 67, 59, 50), LinearGradientMode.Horizontal))
                graphics.FillEllipse(body, 39f, 43f, 39f, 10f);
            using (var head = new SolidBrush(Color.FromArgb(255, 82, 71, 58)))
                graphics.FillEllipse(head, 72f, 42f, 13f, 13f);
            using (var antenna = RoundedPen(Color.FromArgb(210, 91, 78, 62), 1.25f))
            {
                graphics.DrawBezier(antenna, new PointF(81f, 45f), new PointF(87f, 40f), new PointF(88f, 34f), new PointF(91f, 31f));
                graphics.DrawBezier(antenna, new PointF(81f, 52f), new PointF(87f, 57f), new PointF(88f, 62f), new PointF(91f, 65f));
            }

            graphics.Restore(state);
        }

        private static void DrawMothWing(Graphics graphics, PointF root, float open, bool upper)
        {
            var sign = upper ? -1f : 1f;
            var farY = root.Y + sign * (13f + 22f * open);
            using (var path = new GraphicsPath())
            {
                path.StartFigure();
                path.AddBezier(root, new PointF(46f, root.Y + sign * 2f), new PointF(26f, farY - sign * 7f), new PointF(18f, farY));
                path.AddBezier(new PointF(18f, farY), new PointF(14f, farY + sign * 7f), new PointF(29f, root.Y + sign * 18f), new PointF(39f, root.Y + sign * 8f));
                path.AddBezier(new PointF(39f, root.Y + sign * 8f), new PointF(46f, root.Y + sign * 5f), new PointF(50f, root.Y + sign * 2f), root);
                path.CloseFigure();
                using (var brush = new LinearGradientBrush(new RectangleF(15f, Math.Min(root.Y, farY), 41f, Math.Abs(farY - root.Y) + 1f), Color.FromArgb(230, 173, 155, 123), Color.FromArgb(225, 102, 89, 74), LinearGradientMode.Horizontal))
                using (var pen = new Pen(Color.FromArgb(210, 82, 71, 60), 1.2f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(pen, path);
                }
            }
            using (var marking = new SolidBrush(Color.FromArgb(110, 62, 58, 54)))
            {
                var y = root.Y + sign * (9f + 12f * open);
                graphics.FillEllipse(marking, 25f, y - 4f, 11f, 8f);
            }
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
