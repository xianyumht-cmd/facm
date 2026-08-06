using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FACM
{
    internal static class BrandIcon
    {
        public static Icon Create()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                var bounds = new RectangleF(2F, 2F, 28F, 28F);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(bounds);
                    using (var brush = new PathGradientBrush(path))
                    {
                        brush.CenterPoint = new PointF(11F, 9F);
                        brush.CenterColor = Color.FromArgb(92, 210, 255);
                        brush.SurroundColors = new[] { Color.FromArgb(50, 82, 220) };
                        graphics.FillPath(brush, path);
                    }
                }

                using (var pen = new Pen(Color.FromArgb(155, 218, 255), 1.4F))
                {
                    graphics.DrawEllipse(pen, bounds);
                }

                using (var font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color.White))
                {
                    const string text = "F";
                    var size = graphics.MeasureString(text, font);
                    graphics.DrawString(text, font, brush, (32F - size.Width) / 2F - 0.5F, (32F - size.Height) / 2F - 1.5F);
                }

                var handle = bitmap.GetHicon();
                try
                {
                    using (var icon = Icon.FromHandle(handle))
                    {
                        return (Icon)icon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
