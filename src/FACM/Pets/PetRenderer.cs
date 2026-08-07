using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace FACM.Pets
{
    internal static class PetRenderer
    {
        public static GraphicsPath CreateRegionPath(PetDefinition pet, Size size)
        {
            var path = new GraphicsPath();
            var bounds = new Rectangle(1, 1, Math.Max(2, size.Width - 2), Math.Max(2, size.Height - 2));

            switch (pet.Kind)
            {
                case PetKind.Cat:
                case PetKind.Fox:
                case PetKind.CloudBunny:
                case PetKind.Dragon:
                    path.AddEllipse(new Rectangle(bounds.Left + 3, bounds.Top + 8, bounds.Width - 6, bounds.Height - 9));
                    path.AddEllipse(new Rectangle(bounds.Left + 7, bounds.Top, bounds.Width - 14, bounds.Height / 2));
                    break;
                case PetKind.Star:
                    path.AddPolygon(CreateStarPoints(
                        new PointF(bounds.Left + bounds.Width / 2F, bounds.Top + bounds.Height / 2F),
                        Math.Min(bounds.Width, bounds.Height) * 0.48F,
                        Math.Min(bounds.Width, bounds.Height) * 0.31F,
                        -90F));
                    break;
                case PetKind.PixelBot:
                    path.AddRectangle(new Rectangle(bounds.Left + 5, bounds.Top + 5, bounds.Width - 10, bounds.Height - 10));
                    break;
                case PetKind.Ghost:
                    path.AddEllipse(new Rectangle(bounds.Left + 4, bounds.Top + 1, bounds.Width - 8, bounds.Height - 13));
                    path.AddRectangle(new Rectangle(bounds.Left + 4, bounds.Top + bounds.Height / 2, bounds.Width - 8, bounds.Height / 2 - 2));
                    break;
                default:
                    path.AddEllipse(bounds);
                    break;
            }

            path.CloseAllFigures();
            return path;
        }

        public static void Draw(Graphics graphics, PetDefinition pet, float phase, float hover, bool administrator)
        {
            graphics.SmoothingMode = pet.Pixelated ? SmoothingMode.None : SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = pet.Pixelated ? PixelOffsetMode.None : PixelOffsetMode.HighQuality;

            var width = graphics.VisibleClipBounds.Width;
            var height = graphics.VisibleClipBounds.Height;
            var bob = pet.Pixelated ? 0F : (float)Math.Sin(phase * 2.2F) * 1.6F;
            var blink = Math.Abs(Math.Sin(phase * 0.47F)) > 0.965F;
            var bounds = new RectangleF(3F, 4F + bob, width - 6F, height - 8F);

            switch (pet.Kind)
            {
                case PetKind.Jelly: DrawJelly(graphics, pet, bounds, blink, hover); break;
                case PetKind.Cat: DrawCat(graphics, pet, bounds, blink, phase); break;
                case PetKind.Fox: DrawFox(graphics, pet, bounds, blink, phase); break;
                case PetKind.Robot: DrawRobot(graphics, pet, bounds, blink, phase); break;
                case PetKind.Ghost: DrawGhost(graphics, pet, bounds, blink, phase); break;
                case PetKind.Chick: DrawChick(graphics, pet, bounds, blink, phase); break;
                case PetKind.Dragon: DrawDragon(graphics, pet, bounds, blink, phase); break;
                case PetKind.Star: DrawStar(graphics, pet, bounds, blink, phase); break;
                case PetKind.PixelBot: DrawPixelBot(graphics, pet, bounds, blink, phase); break;
                case PetKind.CloudBunny: DrawCloudBunny(graphics, pet, bounds, blink, phase); break;
            }

            DrawStatusDot(graphics, pet, width, administrator);
        }

        private static void DrawJelly(Graphics g, PetDefinition p, RectangleF b, bool blink, float hover)
        {
            var body = new RectangleF(b.X + 5, b.Y + 8, b.Width - 10, b.Height - 12);
            using (var brush = new LinearGradientBrush(body, Blend(p.Primary, Color.White, 0.12F + hover * 0.08F), p.Secondary, 110F))
            using (var pen = new Pen(p.Outline, 2F))
            {
                g.FillEllipse(brush, body);
                g.DrawEllipse(pen, body);
            }
            using (var highlight = new SolidBrush(Color.FromArgb(90, Color.White)))
                g.FillEllipse(highlight, body.X + body.Width * 0.18F, body.Y + body.Height * 0.13F, body.Width * 0.25F, body.Height * 0.15F);
            DrawFace(g, p, body, blink, 0.42F);
        }

        private static void DrawCat(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            var earW = b.Width * 0.25F;
            var sway = (float)Math.Sin(phase * 3F) * 1.5F;
            using (var brush = new SolidBrush(p.Secondary))
            using (var pen = new Pen(p.Outline, 2F))
            {
                var leftEar = new[] { new PointF(b.X + 11, b.Y + 19), new PointF(b.X + 14 + sway, b.Y + 1), new PointF(b.X + 11 + earW, b.Y + 16) };
                var rightEar = new[] { new PointF(b.Right - 11 - earW, b.Y + 16), new PointF(b.Right - 14 - sway, b.Y + 1), new PointF(b.Right - 11, b.Y + 19) };
                g.FillPolygon(brush, leftEar); g.FillPolygon(brush, rightEar);
                g.DrawPolygon(pen, leftEar); g.DrawPolygon(pen, rightEar);
            }
            var head = new RectangleF(b.X + 6, b.Y + 11, b.Width - 12, b.Height - 14);
            using (var brush = new LinearGradientBrush(head, p.Primary, p.Secondary, 90F))
            using (var pen = new Pen(p.Outline, 2F))
            {
                g.FillEllipse(brush, head); g.DrawEllipse(pen, head);
            }
            DrawFace(g, p, head, blink, 0.43F);
            using (var pen = new Pen(p.Outline, 1.2F))
            {
                var y = head.Y + head.Height * 0.63F;
                g.DrawLine(pen, head.X + head.Width * 0.17F, y, head.X + head.Width * 0.37F, y + 2);
                g.DrawLine(pen, head.Right - head.Width * 0.17F, y, head.Right - head.Width * 0.37F, y + 2);
            }
        }

        private static void DrawFox(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            var tail = new RectangleF(b.X + b.Width * 0.58F, b.Y + b.Height * 0.43F, b.Width * 0.4F, b.Height * 0.35F);
            using (var brush = new LinearGradientBrush(tail, p.Primary, p.Secondary, 45F))
            using (var pen = new Pen(p.Outline, 2F))
            {
                g.FillEllipse(brush, tail); g.DrawEllipse(pen, tail);
            }
            using (var tip = new SolidBrush(p.Accent))
                g.FillEllipse(tip, tail.Right - tail.Width * 0.35F, tail.Y + 2, tail.Width * 0.33F, tail.Height * 0.7F);
            var earOffset = (float)Math.Sin(phase * 2.5F) * 1.3F;
            using (var brush = new SolidBrush(p.Secondary))
            using (var pen = new Pen(p.Outline, 2F))
            {
                var left = new[] { new PointF(b.X + 12, b.Y + 22), new PointF(b.X + 20, b.Y + 1 + earOffset), new PointF(b.X + 34, b.Y + 18) };
                var right = new[] { new PointF(b.X + 38, b.Y + 18), new PointF(b.Right - 18, b.Y + 1 - earOffset), new PointF(b.Right - 10, b.Y + 22) };
                g.FillPolygon(brush, left); g.FillPolygon(brush, right);
                g.DrawPolygon(pen, left); g.DrawPolygon(pen, right);
            }
            var face = new RectangleF(b.X + 5, b.Y + 12, b.Width * 0.72F, b.Height - 15);
            using (var brush = new LinearGradientBrush(face, p.Primary, p.Secondary, 90F))
            using (var pen = new Pen(p.Outline, 2F))
            {
                g.FillEllipse(brush, face); g.DrawEllipse(pen, face);
            }
            using (var muzzle = new SolidBrush(p.Accent))
                g.FillEllipse(muzzle, face.X + face.Width * 0.24F, face.Y + face.Height * 0.48F, face.Width * 0.52F, face.Height * 0.34F);
            DrawFace(g, p, face, blink, 0.40F);
        }

        private static void DrawRobot(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            var antennaY = b.Y + 2 + (float)Math.Sin(phase * 3F);
            using (var pen = new Pen(p.Outline, 2F))
                g.DrawLine(pen, b.X + b.Width / 2F, antennaY + 8, b.X + b.Width / 2F, antennaY);
            using (var lamp = new SolidBrush(p.Accent))
                g.FillEllipse(lamp, b.X + b.Width / 2F - 4, antennaY - 3, 8, 8);
            var body = new RectangleF(b.X + 5, b.Y + 11, b.Width - 10, b.Height - 16);
            using (var path = Rounded(body, 12F))
            using (var brush = new LinearGradientBrush(body, p.Primary, p.Secondary, 90F))
            using (var pen = new Pen(p.Outline, 2F))
            {
                g.FillPath(brush, path); g.DrawPath(pen, path);
            }
            var screen = new RectangleF(body.X + 10, body.Y + 12, body.Width - 20, body.Height * 0.42F);
            using (var screenBrush = new SolidBrush(Color.FromArgb(35, 45, 72))) g.FillRectangle(screenBrush, screen);
            DrawEyes(g, p.Accent, screen, blink);
            using (var pen = new Pen(p.Accent, 2F))
            {
                var cy = body.Y + body.Height * 0.72F;
                g.DrawLine(pen, body.X + body.Width * 0.35F, cy, body.X + body.Width * 0.65F, cy);
            }
        }

        private static void DrawGhost(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            var body = new RectangleF(b.X + 5, b.Y + 2, b.Width - 10, b.Height - 8);
            using (var path = new GraphicsPath())
            {
                path.AddArc(body.X, body.Y, body.Width, body.Height * 0.72F, 180, 180);
                path.AddLine(body.Right, body.Y + body.Height * 0.36F, body.Right, body.Bottom - 8);
                for (var i = 0; i < 4; i++)
                {
                    var x = body.Right - i * body.Width / 4F;
                    path.AddArc(x - body.Width / 4F, body.Bottom - 16, body.Width / 4F, 16, 0, 180);
                }
                path.CloseFigure();
                using (var brush = new LinearGradientBrush(body, p.Primary, p.Secondary, 90F))
                using (var pen = new Pen(p.Outline, 2F))
                {
                    g.FillPath(brush, path); g.DrawPath(pen, path);
                }
            }
            DrawFace(g, p, body, blink, 0.38F);
            using (var glow = new SolidBrush(Color.FromArgb(55, p.Accent)))
                g.FillEllipse(glow, body.X + 8, body.Bottom - 13, body.Width - 16, 8 + (float)Math.Sin(phase * 2F));
        }

        private static void DrawChick(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            var body = new RectangleF(b.X + 5, b.Y + 8, b.Width - 10, b.Height - 12);
            using (var brush = new LinearGradientBrush(body, p.Primary, p.Secondary, 100F))
            using (var pen = new Pen(p.Outline, 2F))
            {
                g.FillEllipse(brush, body); g.DrawEllipse(pen, body);
            }
            using (var wing = new SolidBrush(p.Secondary))
            {
                g.FillEllipse(wing, body.X - 1, body.Y + body.Height * 0.42F, body.Width * 0.28F, body.Height * 0.3F);
                g.FillEllipse(wing, body.Right - body.Width * 0.27F, body.Y + body.Height * 0.42F, body.Width * 0.28F, body.Height * 0.3F);
            }
            DrawEyes(g, p.Outline, new RectangleF(body.X + body.Width * 0.2F, body.Y + body.Height * 0.28F, body.Width * 0.6F, body.Height * 0.18F), blink);
            using (var beak = new SolidBrush(Color.FromArgb(255, 130, 45)))
            {
                var points = new[] { new PointF(body.X + body.Width * 0.43F, body.Y + body.Height * 0.52F), new PointF(body.X + body.Width * 0.57F, body.Y + body.Height * 0.52F), new PointF(body.X + body.Width * 0.5F, body.Y + body.Height * 0.61F) };
                g.FillPolygon(beak, points);
            }
        }

        private static void DrawDragon(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            using (var wing = new SolidBrush(p.Secondary))
            using (var pen = new Pen(p.Outline, 2F))
            {
                var leftWing = new[] { new PointF(b.X + 20, b.Y + 29), new PointF(b.X + 1, b.Y + 18), new PointF(b.X + 11, b.Y + 47) };
                var rightWing = new[] { new PointF(b.Right - 20, b.Y + 29), new PointF(b.Right - 1, b.Y + 18), new PointF(b.Right - 11, b.Y + 47) };
                g.FillPolygon(wing, leftWing); g.FillPolygon(wing, rightWing);
                g.DrawPolygon(pen, leftWing); g.DrawPolygon(pen, rightWing);
            }
            var body = new RectangleF(b.X + 10, b.Y + 12, b.Width - 20, b.Height - 14);
            using (var brush = new LinearGradientBrush(body, p.Primary, p.Secondary, 90F))
            using (var pen = new Pen(p.Outline, 2F))
            {
                g.FillEllipse(brush, body); g.DrawEllipse(pen, body);
            }
            using (var horn = new SolidBrush(p.Accent))
            {
                g.FillPolygon(horn, new[] { new PointF(body.X + 12, body.Y + 12), new PointF(body.X + 18, body.Y - 1), new PointF(body.X + 26, body.Y + 11) });
                g.FillPolygon(horn, new[] { new PointF(body.Right - 26, body.Y + 11), new PointF(body.Right - 18, body.Y - 1), new PointF(body.Right - 12, body.Y + 12) });
            }
            DrawFace(g, p, body, blink, 0.40F);
        }

        private static void DrawStar(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            var center = new PointF(b.X + b.Width / 2F, b.Y + b.Height / 2F);
            var pulse = 1F + (float)Math.Sin(phase * 2F) * 0.025F;
            var outer = Math.Min(b.Width, b.Height) * 0.46F * pulse;
            var points = CreateStarPoints(center, outer, outer * 0.53F, -90F);
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points); path.CloseFigure();
                using (var brush = new LinearGradientBrush(b, p.Primary, p.Secondary, 45F))
                using (var pen = new Pen(p.Outline, 2F))
                {
                    g.FillPath(brush, path); g.DrawPath(pen, path);
                }
            }
            var face = new RectangleF(center.X - b.Width * 0.22F, center.Y - b.Height * 0.15F, b.Width * 0.44F, b.Height * 0.3F);
            DrawEyes(g, p.Accent, face, blink);
            using (var spark = new SolidBrush(p.Accent))
            {
                var orbit = phase * 1.3F;
                var x = center.X + (float)Math.Cos(orbit) * outer * 0.86F;
                var y = center.Y + (float)Math.Sin(orbit) * outer * 0.58F;
                g.FillEllipse(spark, x - 3, y - 3, 6, 6);
            }
        }

        private static void DrawPixelBot(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            var unit = Math.Max(3, (int)Math.Min(b.Width, b.Height) / 12);
            var x = (int)(b.X + unit);
            var y = (int)(b.Y + unit);
            var w = unit * 10;
            var h = unit * 9;
            using (var outline = new SolidBrush(p.Outline)) g.FillRectangle(outline, x, y, w, h);
            using (var body = new SolidBrush(p.Primary)) g.FillRectangle(body, x + unit, y + unit, w - unit * 2, h - unit * 2);
            using (var screen = new SolidBrush(p.Secondary)) g.FillRectangle(screen, x + unit * 2, y + unit * 2, w - unit * 4, unit * 3);
            using (var eye = new SolidBrush(p.Accent))
            {
                var eyeH = blink ? 1 : unit;
                g.FillRectangle(eye, x + unit * 3, y + unit * 3, unit, eyeH);
                g.FillRectangle(eye, x + unit * 6, y + unit * 3, unit, eyeH);
            }
            using (var accent = new SolidBrush(p.Accent))
            {
                g.FillRectangle(accent, x + unit * 3, y + unit * 6, unit * 4, unit);
                if (((int)(phase * 5F) % 2) == 0) g.FillRectangle(accent, x + unit * 5, y - unit, unit, unit);
            }
        }

        private static void DrawCloudBunny(Graphics g, PetDefinition p, RectangleF b, bool blink, float phase)
        {
            var earShift = (float)Math.Sin(phase * 2.4F) * 1.5F;
            using (var ear = new SolidBrush(p.Primary))
            using (var pen = new Pen(p.Outline, 2F))
            {
                var leftEar = new RectangleF(b.X + b.Width * 0.24F, b.Y + earShift, b.Width * 0.18F, b.Height * 0.46F);
                var rightEar = new RectangleF(b.X + b.Width * 0.58F, b.Y - earShift, b.Width * 0.18F, b.Height * 0.46F);
                g.FillEllipse(ear, leftEar); g.FillEllipse(ear, rightEar);
                g.DrawEllipse(pen, leftEar); g.DrawEllipse(pen, rightEar);
            }
            var head = new RectangleF(b.X + 5, b.Y + b.Height * 0.24F, b.Width - 10, b.Height * 0.67F);
            using (var brush = new LinearGradientBrush(head, p.Primary, p.Secondary, 90F))
            using (var pen = new Pen(p.Outline, 2F))
            {
                g.FillEllipse(brush, head); g.DrawEllipse(pen, head);
            }
            DrawFace(g, p, head, blink, 0.41F);
            using (var cheek = new SolidBrush(Color.FromArgb(120, p.Accent)))
            {
                g.FillEllipse(cheek, head.X + head.Width * 0.15F, head.Y + head.Height * 0.58F, head.Width * 0.16F, head.Height * 0.11F);
                g.FillEllipse(cheek, head.Right - head.Width * 0.31F, head.Y + head.Height * 0.58F, head.Width * 0.16F, head.Height * 0.11F);
            }
        }

        private static void DrawFace(Graphics g, PetDefinition p, RectangleF body, bool blink, float eyeYRatio)
        {
            var eyeArea = new RectangleF(body.X + body.Width * 0.24F, body.Y + body.Height * eyeYRatio, body.Width * 0.52F, body.Height * 0.18F);
            DrawEyes(g, p.Outline, eyeArea, blink);
            using (var pen = new Pen(p.Outline, 1.5F))
            {
                var cx = body.X + body.Width / 2F;
                var y = body.Y + body.Height * 0.68F;
                g.DrawArc(pen, cx - body.Width * 0.08F, y - 2, body.Width * 0.16F, body.Height * 0.12F, 10, 160);
            }
        }

        private static void DrawEyes(Graphics g, Color color, RectangleF area, bool blink)
        {
            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(color, 2F))
            {
                var eyeW = Math.Max(3F, area.Width * 0.12F);
                var eyeH = blink ? 1F : Math.Max(4F, area.Height * 0.52F);
                var leftX = area.X + area.Width * 0.18F;
                var rightX = area.Right - area.Width * 0.18F - eyeW;
                var y = area.Y + area.Height * 0.2F;
                if (blink)
                {
                    g.DrawLine(pen, leftX, y + 2, leftX + eyeW, y + 2);
                    g.DrawLine(pen, rightX, y + 2, rightX + eyeW, y + 2);
                }
                else
                {
                    g.FillEllipse(brush, leftX, y, eyeW, eyeH);
                    g.FillEllipse(brush, rightX, y, eyeW, eyeH);
                }
            }
        }

        private static void DrawStatusDot(Graphics g, PetDefinition p, float width, bool administrator)
        {
            var status = administrator ? Color.FromArgb(82, 232, 157) : Color.FromArgb(255, 190, 73);
            using (var border = new SolidBrush(Color.FromArgb(220, p.Outline)))
            using (var dot = new SolidBrush(status))
            {
                g.FillEllipse(border, width - 17, 6, 11, 11);
                g.FillEllipse(dot, width - 15, 8, 7, 7);
            }
        }

        private static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(2F, radius * 2F);
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static PointF[] CreateStarPoints(PointF center, float outer, float inner, float startDegrees)
        {
            var points = new PointF[10];
            for (var i = 0; i < points.Length; i++)
            {
                var radius = i % 2 == 0 ? outer : inner;
                var angle = (startDegrees + i * 36F) * Math.PI / 180D;
                points[i] = new PointF(center.X + (float)Math.Cos(angle) * radius, center.Y + (float)Math.Sin(angle) * radius);
            }
            return points;
        }

        private static Color Blend(Color first, Color second, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                (int)(first.A + (second.A - first.A) * amount),
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }
    }
}
