using System.Drawing;

namespace FACM.Theming
{
    internal static class GraphicsDesignExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            if (graphics == null || brush == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var path = FacmDesignSystem.RoundedRectangle(bounds, radius))
                graphics.FillPath(brush, path);
        }
    }
}
