using System;
using System.Drawing;
using System.Windows.Forms;

namespace FACM.Mayhem
{
    /// <summary>
    /// Pure geometry policy for the Mayhem lookup shell. The renderer and query pipeline remain
    /// independent; this policy only keeps the toolbar and preview inside the visible WinForms area.
    /// </summary>
    internal sealed class MayhemLookupLayout
    {
        public Rectangle Query { get; set; }
        public Rectangle Search { get; set; }
        public Rectangle Cancel { get; set; }
        public Rectangle Save { get; set; }
        public Rectangle Copy { get; set; }
        public Rectangle Progress { get; set; }
        public Rectangle Status { get; set; }
        public Rectangle ImageHost { get; set; }
    }

    internal static class MayhemLookupLayoutPolicy
    {
        private const int Left = 24;
        private const int Right = 24;
        private const int Gap = 10;
        private const int QueryHeight = 36;
        private const int ButtonHeight = 40;
        private const int SearchWidth = 100;
        private const int CancelWidth = 92;
        private const int SaveWidth = 108;
        private const int CopyWidth = 108;
        private const int MinimumQueryWidth = 220;

        public static MayhemLookupLayout Resolve(int clientWidth, int clientHeight)
        {
            clientWidth = Math.Max(360, clientWidth);
            clientHeight = Math.Max(360, clientHeight);
            var usableWidth = Math.Max(160, clientWidth - Left - Right);
            var buttonBlockWidth = SearchWidth + CancelWidth + SaveWidth + CopyWidth + Gap * 3;
            var oneRow = usableWidth >= buttonBlockWidth + Gap + MinimumQueryWidth;

            var result = new MayhemLookupLayout();
            int progressY;
            int statusY;
            int imageY;

            if (oneRow)
            {
                var copyX = clientWidth - Right - CopyWidth;
                var saveX = copyX - Gap - SaveWidth;
                var cancelX = saveX - Gap - CancelWidth;
                var searchX = cancelX - Gap - SearchWidth;
                var queryWidth = Math.Max(MinimumQueryWidth, searchX - Gap - Left);

                result.Query = new Rectangle(Left, 90, queryWidth, QueryHeight);
                result.Search = new Rectangle(searchX, 88, SearchWidth, ButtonHeight);
                result.Cancel = new Rectangle(cancelX, 88, CancelWidth, ButtonHeight);
                result.Save = new Rectangle(saveX, 88, SaveWidth, ButtonHeight);
                result.Copy = new Rectangle(copyX, 88, CopyWidth, ButtonHeight);
                progressY = 139;
                statusY = 151;
                imageY = 184;
            }
            else
            {
                result.Query = new Rectangle(Left, 90, usableWidth, QueryHeight);
                var actionGap = 8;
                var actionWidth = Math.Max(48, (usableWidth - actionGap * 3) / 4);
                var actionY = 134;
                result.Search = new Rectangle(Left, actionY, actionWidth, ButtonHeight);
                result.Cancel = new Rectangle(result.Search.Right + actionGap, actionY, actionWidth, ButtonHeight);
                result.Save = new Rectangle(result.Cancel.Right + actionGap, actionY, actionWidth, ButtonHeight);
                result.Copy = new Rectangle(result.Save.Right + actionGap, actionY,
                    Math.Max(48, clientWidth - Right - (result.Save.Right + actionGap)), ButtonHeight);
                progressY = 183;
                statusY = 195;
                imageY = 228;
            }

            result.Progress = new Rectangle(Left, progressY, usableWidth, 5);
            result.Status = new Rectangle(Left, statusY, usableWidth, 26);
            result.ImageHost = new Rectangle(Left, imageY, usableWidth, Math.Max(120, clientHeight - imageY - 24));
            return result;
        }

        public static int ResolvePreviewWidth(int viewportWidth)
        {
            // Reserve the vertical scrollbar before it becomes visible. Without this reservation,
            // AutoScroll can shrink the viewport after the image is sized and create a horizontal
            // scrollbar on the next layout pass.
            return Math.Max(1, viewportWidth - 16 - SystemInformation.VerticalScrollBarWidth - 4);
        }

        public static void ValidateForSmokeTest()
        {
            ValidateLayout(1120, 820);
            ValidateLayout(920, 700);
            ValidateLayout(700, 620);

            var previewWidth = ResolvePreviewWidth(640);
            if (previewWidth <= 0 || previewWidth >= 640)
                throw new InvalidOperationException("Mayhem preview width policy no longer reserves a scrollbar-safe viewport.");
        }

        private static void ValidateLayout(int width, int height)
        {
            var layout = Resolve(width, height);
            var controls = new[] { layout.Query, layout.Search, layout.Cancel, layout.Save, layout.Copy };
            foreach (var bounds in controls)
            {
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    throw new InvalidOperationException("Mayhem toolbar produced an empty control at " + width + "px.");
                if (bounds.Left < Left || bounds.Right > width - Right)
                    throw new InvalidOperationException("Mayhem toolbar escaped the visible client width at " + width + "px.");
            }

            if (layout.Search.IntersectsWith(layout.Cancel) ||
                layout.Cancel.IntersectsWith(layout.Save) ||
                layout.Save.IntersectsWith(layout.Copy))
                throw new InvalidOperationException("Mayhem toolbar actions overlap at " + width + "px.");
            if (layout.Progress.Width <= 0 || layout.Status.Width <= 0 || layout.ImageHost.Width <= 0 || layout.ImageHost.Height <= 0)
                throw new InvalidOperationException("Mayhem responsive shell produced invalid content geometry at " + width + "px.");
        }
    }
}
