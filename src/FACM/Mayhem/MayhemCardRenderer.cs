using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.Mayhem
{
    internal static class MayhemCardRenderer
    {
        public const int CardWidth = 1260;
        public const int CardHeight = 980;

        private sealed class LoadedImage : IDisposable
        {
            public string Reference { get; set; }
            public Bitmap Bitmap { get; set; }
            public void Dispose()
            {
                if (Bitmap != null) Bitmap.Dispose();
            }
        }

        public static async Task<Bitmap> RenderAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var loaded = await LoadImagesAsync(result, token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var map = loaded.Where(item => item.Bitmap != null && !string.IsNullOrWhiteSpace(item.Reference))
                    .GroupBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Bitmap, StringComparer.OrdinalIgnoreCase);
                return Render(result, map);
            }
            finally
            {
                foreach (var item in loaded) item.Dispose();
            }
        }

        public static Bitmap RenderForSmokeTest(MayhemChampionResult result)
        {
            return Render(result, new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase));
        }

        private static async Task<List<LoadedImage>> LoadImagesAsync(MayhemChampionResult result, CancellationToken token)
        {
            var references = new List<string>();
            AddReference(references, result.ChampionIconUrl);
            AddReference(references, result.ChampionSplashUrl);
            foreach (var pair in result.SkillIconUrls) AddReference(references, pair.Value);
            foreach (var value in result.CoreItemIconUrls) AddReference(references, value);
            foreach (var value in result.AugmentIconUrls) AddReference(references, value);
            foreach (var top in result.TopTen) AddReference(references, top.IconUrl);

            var distinct = references.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .ToArray();
            var tasks = distinct.Select(reference => LoadOneAsync(reference, token)).ToArray();
            var images = await Task.WhenAll(tasks).ConfigureAwait(false);
            return images.ToList();
        }

        private static async Task<LoadedImage> LoadOneAsync(string reference, CancellationToken token)
        {
            try
            {
                return new LoadedImage { Reference = reference, Bitmap = await MayhemImageCache.GetAsync(reference, token).ConfigureAwait(false) };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new LoadedImage { Reference = reference };
            }
        }

        private static void AddReference(List<string> list, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value);
        }

        private static Bitmap Render(MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var bitmap = new Bitmap(CardWidth, CardHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                DrawBackground(graphics);
                DrawHero(graphics, result, images);
                DrawBuildSection(graphics, result, images);
                DrawRankingSection(graphics, result, images);
                DrawFooter(graphics, result);
            }
            return bitmap;
        }

        private static void DrawBackground(Graphics g)
        {
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, CardWidth, CardHeight),
                Color.FromArgb(8, 14, 26), Color.FromArgb(20, 29, 48), 90F))
                g.FillRectangle(brush, 0, 0, CardWidth, CardHeight);

            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(-180, -260, 780, 640);
                using (var brush = new PathGradientBrush(glow))
                {
                    brush.CenterColor = Color.FromArgb(90, 56, 116, 255);
                    brush.SurroundColors = new[] { Color.FromArgb(0, 56, 116, 255) };
                    g.FillPath(brush, glow);
                }
            }
        }

        private static void DrawHero(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var hero = new Rectangle(32, 30, 1196, 292);
            using (var path = RoundedRect(hero, 28))
            {
                g.SetClip(path);
                Bitmap splash;
                if (TryGet(images, result.ChampionSplashUrl, out splash))
                    DrawImageCover(g, splash, hero);
                else
                {
                    using (var fallback = new LinearGradientBrush(hero, Color.FromArgb(37, 64, 108), Color.FromArgb(16, 27, 47), 0F))
                        g.FillRectangle(fallback, hero);
                }
                using (var overlay = new LinearGradientBrush(hero, Color.FromArgb(230, 7, 12, 24), Color.FromArgb(55, 7, 12, 24), 0F))
                    g.FillRectangle(overlay, hero);
                g.ResetClip();
                using (var pen = new Pen(Color.FromArgb(70, 124, 168, 255), 1F)) g.DrawPath(pen, path);
            }

            var iconRect = new Rectangle(64, 74, 154, 154);
            DrawSquareImage(g, images, result.ChampionIconUrl, iconRect, 24, FirstChar(result.ChampionName));

            using (var titleFont = new Font("Microsoft YaHei UI", 27F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subFont = new Font("Microsoft YaHei UI", 15F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var badgeFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString(string.IsNullOrWhiteSpace(result.ChampionName) ? "未知英雄" : result.ChampionName, titleFont, Brushes.White, 246, 79);
                g.DrawString("海斗排行榜 · 当前版本攻略卡", subFont, new SolidBrush(Color.FromArgb(193, 207, 233)), 248, 124);

                var patch = FirstNonEmpty(result.RankingPatch, result.Patch, "当前版本");
                DrawBadge(g, "版本 " + patch, new Point(246, 164), 112, badgeFont, Color.FromArgb(52, 98, 174));
                DrawBadge(g, result.TierOr("暂无梯队"), new Point(368, 164), 102, badgeFont, Color.FromArgb(89, 70, 155));
                DrawBadge(g, result.Rank.HasValue ? "排行 #" + result.Rank.Value : "排行 —", new Point(480, 164), 112, badgeFont, Color.FromArgb(42, 126, 119));
            }

            DrawMetric(g, new Rectangle(720, 76, 218, 112), "胜率", FormatPercent(result.WinRate), Color.FromArgb(56, 169, 139));
            DrawMetric(g, new Rectangle(956, 76, 218, 112), "选用率", FormatPercent(result.PickRate), Color.FromArgb(85, 119, 224));

            using (var sectionFont = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textFont = new Font("Microsoft YaHei UI", 14F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString("当前平衡调整", sectionFont, new SolidBrush(Color.FromArgb(177, 197, 232)), 246, 222);
                var balance = string.IsNullOrWhiteSpace(result.BalanceSummary) ? "暂无额外平衡调整" : result.BalanceSummary;
                DrawWrappedText(g, balance, textFont, Color.White, new RectangleF(246, 248, 900, 54), 2);
            }
        }

        private static void DrawBuildSection(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var left = new Rectangle(32, 342, 760, 530);
            DrawPanel(g, left);
            DrawSectionTitle(g, "推荐玩法", 58, 366);

            using (var labelFont = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bodyFont = new Font("Microsoft YaHei UI", 14F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString("技能加点", labelFont, new SolidBrush(Color.FromArgb(151, 177, 221)), 58, 408);
                DrawSkills(g, result, images, 58, 438);
                var order = string.IsNullOrWhiteSpace(result.SkillOrder) ? "暂无加点顺序" : result.SkillOrder;
                DrawWrappedText(g, order, bodyFont, Color.White, new RectangleF(58, 508, 690, 46), 2);

                g.DrawString("核心装备", labelFont, new SolidBrush(Color.FromArgb(151, 177, 221)), 58, 574);
                DrawIconList(g, result.CoreItems, result.CoreItemIconUrls, images, 58, 606, 5);

                g.DrawString("强化符文", labelFont, new SolidBrush(Color.FromArgb(151, 177, 221)), 58, 716);
                DrawIconList(g, result.Augments, result.AugmentIconUrls, images, 58, 748, 5);
            }
        }

        private static void DrawRankingSection(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var right = new Rectangle(812, 342, 416, 530);
            DrawPanel(g, right);
            DrawSectionTitle(g, "当前版本胜率前十", 838, 366);

            using (var small = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var medium = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var number = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var top = result.TopTen.OrderBy(item => item.Rank).Take(10).ToList();
                for (var i = 0; i < 10; i++)
                {
                    var y = 410 + i * 43;
                    using (var line = new Pen(Color.FromArgb(32, 255, 255, 255))) g.DrawLine(line, 838, y + 38, 1202, y + 38);
                    if (i >= top.Count)
                    {
                        g.DrawString((i + 1).ToString(CultureInfo.InvariantCulture), number, new SolidBrush(Color.FromArgb(98, 115, 145)), 840, y + 9);
                        g.DrawString("暂无数据", small, new SolidBrush(Color.FromArgb(105, 119, 143)), 904, y + 12);
                        continue;
                    }

                    var champion = top[i];
                    g.DrawString(champion.Rank.ToString(CultureInfo.InvariantCulture), number, new SolidBrush(i < 3 ? Color.FromArgb(255, 211, 111) : Color.FromArgb(181, 194, 220)), 840, y + 9);
                    DrawSquareImage(g, images, champion.IconUrl, new Rectangle(876, y + 3, 34, 34), 8, FirstChar(champion.Name));
                    g.DrawString(Ellipsis(champion.Name, 12), medium, Brushes.White, 920, y + 6);
                    g.DrawString(string.IsNullOrWhiteSpace(champion.Tier) ? "—" : champion.Tier, small, new SolidBrush(Color.FromArgb(143, 165, 201)), 920, y + 23);
                    var win = champion.WinRate.HasValue ? champion.WinRate.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "—";
                    var size = g.MeasureString(win, medium);
                    g.DrawString(win, medium, new SolidBrush(Color.FromArgb(111, 218, 178)), 1198 - size.Width, y + 12);
                }
            }
        }

        private static void DrawSkills(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images, int x, int y)
        {
            var keys = new[] { "Q", "W", "E", "R" };
            using (var keyFont = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    string reference;
                    result.SkillIconUrls.TryGetValue(key, out reference);
                    var rect = new Rectangle(x + i * 86, y, 58, 58);
                    DrawSquareImage(g, images, reference, rect, 12, key);
                    using (var badgeBrush = new SolidBrush(Color.FromArgb(225, 10, 17, 30))) g.FillEllipse(badgeBrush, rect.Right - 18, rect.Bottom - 18, 22, 22);
                    g.DrawString(key, keyFont, Brushes.White, rect.Right - 14, rect.Bottom - 17);
                }
            }
        }

        private static void DrawIconList(Graphics g, IList<string> names, IList<string> references, IDictionary<string, Bitmap> images, int x, int y, int max)
        {
            using (var nameFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var count = Math.Min(max, names == null ? 0 : names.Count);
                if (count == 0)
                {
                    g.DrawString("暂无推荐", nameFont, new SolidBrush(Color.FromArgb(125, 143, 174)), x, y + 22);
                    return;
                }
                for (var i = 0; i < count; i++)
                {
                    var itemX = x + i * 136;
                    var reference = references != null && i < references.Count ? references[i] : null;
                    DrawSquareImage(g, images, reference, new Rectangle(itemX, y, 54, 54), 11, FirstChar(names[i]));
                    DrawWrappedText(g, names[i], nameFont, Color.FromArgb(224, 232, 247), new RectangleF(itemX - 5, y + 60, 116, 38), 2, StringAlignment.Center);
                    if (i < count - 1)
                    {
                        using (var arrowFont = new Font("Segoe UI Symbol", 15F, FontStyle.Bold, GraphicsUnit.Pixel))
                            g.DrawString("›", arrowFont, new SolidBrush(Color.FromArgb(91, 117, 160)), itemX + 104, y + 18);
                    }
                }
            }
        }

        private static void DrawMetric(Graphics g, Rectangle rect, string label, string value, Color accent)
        {
            using (var path = RoundedRect(rect, 18))
            using (var fill = new SolidBrush(Color.FromArgb(165, 12, 21, 37)))
            using (var border = new Pen(Color.FromArgb(90, accent), 1F))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            using (var labelFont = new Font("Microsoft YaHei UI", 13F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var valueFont = new Font("Segoe UI", 28F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString(label, labelFont, new SolidBrush(Color.FromArgb(157, 177, 210)), rect.X + 18, rect.Y + 18);
                g.DrawString(value, valueFont, new SolidBrush(accent), rect.X + 17, rect.Y + 49);
            }
        }

        private static void DrawSectionTitle(Graphics g, string title, int x, int y)
        {
            using (var font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString(title, font, Brushes.White, x, y);
            }
        }

        private static void DrawPanel(Graphics g, Rectangle rect)
        {
            using (var path = RoundedRect(rect, 22))
            using (var fill = new SolidBrush(Color.FromArgb(205, 13, 21, 36)))
            using (var pen = new Pen(Color.FromArgb(55, 122, 153, 211), 1F))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        private static void DrawBadge(Graphics g, string text, Point location, int width, Font font, Color color)
        {
            var rect = new Rectangle(location.X, location.Y, width, 34);
            using (var path = RoundedRect(rect, 12))
            using (var fill = new SolidBrush(Color.FromArgb(195, color))) g.FillPath(fill, path);
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.White, location.X + (width - size.Width) / 2F, location.Y + 7);
        }

        private static void DrawSquareImage(Graphics g, IDictionary<string, Bitmap> images, string reference, Rectangle rect, int radius, string placeholder)
        {
            using (var path = RoundedRect(rect, radius))
            {
                g.SetClip(path);
                Bitmap bitmap;
                if (TryGet(images, reference, out bitmap)) DrawImageCover(g, bitmap, rect);
                else
                {
                    using (var brush = new LinearGradientBrush(rect, Color.FromArgb(55, 73, 112), Color.FromArgb(32, 41, 64), 45F)) g.FillRectangle(brush, rect);
                    using (var font = new Font("Microsoft YaHei UI", Math.Max(12, rect.Width / 3), FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        var text = string.IsNullOrWhiteSpace(placeholder) ? "?" : placeholder;
                        var size = g.MeasureString(text, font);
                        g.DrawString(text, font, new SolidBrush(Color.FromArgb(196, 211, 239)), rect.X + (rect.Width - size.Width) / 2F, rect.Y + (rect.Height - size.Height) / 2F);
                    }
                }
                g.ResetClip();
                using (var pen = new Pen(Color.FromArgb(75, 170, 193, 235), 1F)) g.DrawPath(pen, path);
            }
        }

        private static void DrawImageCover(Graphics g, Image image, Rectangle target)
        {
            if (image == null || image.Width <= 0 || image.Height <= 0) return;
            var scale = Math.Max((float)target.Width / image.Width, (float)target.Height / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            var destination = new RectangleF(target.X + (target.Width - width) / 2F, target.Y + (target.Height - height) / 2F, width, height);
            g.DrawImage(image, destination);
        }

        private static bool TryGet(IDictionary<string, Bitmap> images, string reference, out Bitmap bitmap)
        {
            bitmap = null;
            return !string.IsNullOrWhiteSpace(reference) && images != null && images.TryGetValue(reference, out bitmap) && bitmap != null;
        }

        private static void DrawFooter(Graphics g, MayhemChampionResult result)
        {
            using (var font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var strong = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString("FACM · 海斗排行榜", strong, new SolidBrush(Color.FromArgb(172, 191, 224)), 40, 916);
                var patch = FirstNonEmpty(result.RankingPatch, result.Patch, "当前版本");
                var text = "版本 " + patch + " · 查询结果会自动缓存 10 分钟";
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, new SolidBrush(Color.FromArgb(111, 130, 161)), CardWidth - 40 - size.Width, 916);
            }
        }

        private static void DrawWrappedText(Graphics g, string text, Font font, Color color, RectangleF bounds, int maxLines, StringAlignment alignment = StringAlignment.Near)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var value = text.Trim();
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = alignment, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit })
            {
                g.DrawString(value, font, brush, bounds, format);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(2, radius * 2);
            var arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "—";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string FirstChar(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "?" : value.Trim().Substring(0, 1).ToUpperInvariant();
        }

        private static string Ellipsis(string value, int length)
        {
            if (string.IsNullOrWhiteSpace(value)) return "未知英雄";
            return value.Length <= length ? value : value.Substring(0, Math.Max(1, length - 1)) + "…";
        }

        private static string TierOr(this MayhemChampionResult result, string fallback)
        {
            return string.IsNullOrWhiteSpace(result.Tier) ? fallback : result.Tier + " 梯队";
        }
    }
}
