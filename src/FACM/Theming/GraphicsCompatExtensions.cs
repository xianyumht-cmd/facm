using System.Drawing;

namespace FACM
{
    internal static class GraphicsCompatExtensions
    {
        public static void FillRectangle(this Graphics graphics, int x, int y, int width, int height)
        {
            using (var brush = new SolidBrush(Theming.ThemeCatalog.Get("brutalist-grid").AccentSecondary))
            {
                graphics.FillRectangle(brush, x, y, width, height);
            }
        }
    }
}
