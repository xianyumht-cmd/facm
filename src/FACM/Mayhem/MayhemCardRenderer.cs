using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FACM.League;

namespace FACM.Mayhem
{
    internal static class MayhemCardRenderer
    {
        public const int CardWidth = 1260;
        public const int CardHeight = 1540;

        private static readonly Color BackgroundA = Color.FromArgb(7, 12, 23);
        private static readonly Color BackgroundB = Color.FromArgb(17, 27, 46);
        private static readonly Color Panel = Color.FromArgb(18, 29, 48);
        private static readonly Color PanelSoft = Color.FromArgb(22, 36, 59);
        private static readonly Color Text = Color.FromArgb(241, 246, 255);
        private static readonly Color Muted = Color.FromArgb(151, 171, 205);
        private static readonly Color Blue = Color.FromArgb(88, 137, 255);
        private static readonly Color Green = Color.FromArgb(91, 210, 170);
        private static readonly Color Gold = Color.FromArgb(237, 187, 81);
        private static readonly Color Purple = Color.FromArgb(176, 115, 255);

        private sealed class LoadedImage : IDisposable
        {
            public string Reference { get; set; }
            public Bitmap Bitmap { get; set; }
            public void Dispose() { if (Bitmap != null) Bitmap.Dispose(); }
        }

        public static async Task<Bitmap> RenderAsync(
            MayhemChampionResult result,
            ILeagueClientApi leagueClient,
            CancellationToken token)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var loaded = await LoadImagesAsync(result, leagueClient, token).ConfigureAwait(false);
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
            return Render(result ?? new MayhemChampionResult(), new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase));
        }

        private static async Task<List<LoadedImage>> LoadImagesAsync(
            MayhemChampionResult result,
            ILeagueClientApi leagueClient,
            CancellationToken token)
        {
            var references = new List<string>();
            AddReference(references, result.ChampionIconUrl);
            AddReference(references, result.ChampionSplashUrl);
            foreach (var pair in result.SkillIconUrls) AddReference(references, pair.Value);
            foreach (var value in result.CoreItemIconUrls) AddReference(references, value);
            foreach (var value in result.AugmentIconUrls) AddReference(references, value);
            foreach (var row in result.AugmentRows ?? new List<MayhemAugmentRow>()) AddReference(references, row == null ? null : row.IconUrl);
            foreach (var top in result.TopTen ?? new List<MayhemTopChampion>()) AddReference(references, top == null ? null : top.IconUrl);

            var distinct = references.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(48)
                .ToArray();
            var tasks = distinct.Select(reference => LoadOneAsync(reference, leagueClient, token)).ToArray();
            var images = await Task.WhenAll(tasks).ConfigureAwait(false);
            return images.ToList();
        }

        private static async Task<LoadedImage> LoadOneAsync(string reference, ILeagueClientApi leagueClient, CancellationToken token)
        {
            try
            {
                return new LoadedImage
                {
                    Reference = reference,
                    Bitmap = await MayhemImageCache.GetAsync(reference, leagueClient, token).ConfigureAwait(false)
                };
            }
            catch (OperationCanceledException) { throw; }
            catch { return new LoadedImage { Reference = reference }; }
        }

        private static void AddReference(ICollection<string> list, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value);
        }

        private static Bitmap Render(MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var bitmap = new Bitmap(CardWidth, CardHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                DrawBackground(g);
                DrawHero(g, result, images);
                DrawDecisionRoutes(g, result);
                DrawAugmentBoard(g, result, images);
                DrawCompactBuild(g, result, images);
                DrawTopRanking(g, result, images);
                DrawFooter(g, result);
            }
            return bitmap;
        }

        private static void DrawBackground(Graphics g)
        {
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, CardWidth, CardHeight), BackgroundA, BackgroundB, 90F))
                g.FillRectangle(brush, 0, 0, CardWidth, CardHeight);
            using (var glow = new SolidBrush(Color.FromArgb(24, 77, 111, 255)))
            {
                g.FillEllipse(glow, -160, -210, 620, 500);
                g.FillEllipse(glow, 920, -180, 520, 430);
            }
        }

        private static void DrawHero(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var hero = new Rectangle(30, 28, 1200, 250);
            using (var path = RoundedRect(hero, 26))
            {
                g.SetClip(path);
                Bitmap splash;
                if (TryGet(images, result.ChampionSplashUrl, out splash)) DrawImageCover(g, splash, hero);
                else
                {
                    using (var fill = new LinearGradientBrush(hero, Color.FromArgb(36, 64, 110), Color.FromArgb(15, 25, 43), 0F))
                        g.FillRectangle(fill, hero);
                }
                using (var overlay = new LinearGradientBrush(hero, Color.FromArgb(238, 6, 12, 24), Color.FromArgb(84, 6, 12, 24), 0F))
                    g.FillRectangle(overlay, hero);
                g.ResetClip();
                using (var pen = new Pen(Color.FromArgb(80, 119, 165, 255))) g.DrawPath(pen, path);
            }

            DrawSquareImage(g, images, result.ChampionIconUrl, new Rectangle(58, 66, 138, 138), 24, FirstChar(result.ChampionName));
            using (var title = new Font("Microsoft YaHei UI", 30F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subtitle = new Font("Microsoft YaHei UI", 15F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var badge = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString(FirstNonEmpty(result.ChampionName, "等待查询"), title, Brushes.White, 226, 60);
                g.DrawString("海克斯大乱斗 · 强化决策攻略", subtitle, new SolidBrush(Color.FromArgb(197, 211, 236)), 228, 105);
                DrawBadge(g, "版本 " + FirstNonEmpty(result.RankingPatch, result.Patch, "—"), new Point(226, 144), 126, badge, Color.FromArgb(51, 93, 166));
                DrawBadge(g, FirstNonEmpty(result.Tier, "暂无梯队"), new Point(362, 144), 108, badge, Color.FromArgb(92, 66, 151));
                DrawBadge(g, result.Rank.HasValue ? "排行 #" + result.Rank.Value : "排行 —", new Point(480, 144), 112, badge, Color.FromArgb(35, 119, 111));
            }

            DrawMetric(g, new Rectangle(750, 60, 202, 92), "英雄胜率", FormatPercent(result.WinRate), Green);
            DrawMetric(g, new Rectangle(972, 60, 202, 92), "选用率", FormatPercent(result.PickRate), Blue);

            using (var label = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 13F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString("当前平衡", label, new SolidBrush(Muted), 228, 196);
                DrawWrappedText(g, FirstNonEmpty(result.BalanceSummary, "暂无额外平衡调整"), body, Text, new RectangleF(228, 218, 930, 42), 2);
            }
        }

        private static void DrawDecisionRoutes(Graphics g, MayhemChampionResult result)
        {
            var panel = new Rectangle(30, 298, 1200, 172);
            DrawPanel(g, panel);
            DrawSectionTitle(g, "这一局怎么选", 56, 318);
            using (var note = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
                g.DrawString("根据单强化胜率与选择率给出方向，不代表三强化组合胜率。", note, new SolidBrush(Muted), 218, 323);

            var routes = (result.AugmentRoutes ?? new List<MayhemDecisionRoute>()).Take(3).ToList();
            var fallbackTitles = new[] { "稳健首选", "高胜上限", "热门容错" };
            for (var i = 0; i < 3; i++)
            {
                var rect = new Rectangle(56 + i * 382, 356, 358, 90);
                var route = i < routes.Count ? routes[i] : null;
                DrawRouteCard(g, rect, route, fallbackTitles[i], i);
            }
        }

        private static void DrawRouteCard(Graphics g, Rectangle rect, MayhemDecisionRoute route, string fallbackTitle, int index)
        {
            var accent = index == 0 ? Green : index == 1 ? Purple : Gold;
            using (var path = RoundedRect(rect, 18))
            using (var fill = new SolidBrush(Color.FromArgb(34, accent.R, accent.G, accent.B)))
            using (var pen = new Pen(Color.FromArgb(90, accent.R, accent.G, accent.B), 1F))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            using (var title = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var name = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var hint = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString(route == null ? fallbackTitle : route.Title, title, new SolidBrush(accent), rect.X + 16, rect.Y + 12);
                g.DrawString(route == null ? "暂无足够数据" : Ellipsis(route.AugmentName, 18), name, Brushes.White, rect.X + 16, rect.Y + 34);
                g.DrawString(route == null ? "查询后自动生成" : Ellipsis(route.Hint, 25), hint, new SolidBrush(Muted), rect.X + 16, rect.Y + 62);
            }
        }

        private static void DrawAugmentBoard(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var panel = new Rectangle(30, 490, 1200, 742);
            DrawPanel(g, panel);
            DrawSectionTitle(g, "强化符文决策榜", 56, 512);

            var rows = (result.AugmentRows ?? new List<MayhemAugmentRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .OrderBy(row => row.Rank <= 0 ? int.MaxValue : row.Rank)
                .Take(12)
                .ToList();
            var prism = rows.Count(row => string.Equals(row.Rarity, "棱彩", StringComparison.OrdinalIgnoreCase));
            var gold = rows.Count(row => string.Equals(row.Rarity, "黄金", StringComparison.OrdinalIgnoreCase));
            var silver = rows.Count(row => string.Equals(row.Rarity, "白银", StringComparison.OrdinalIgnoreCase));
            using (var summary = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var source = SourceLabel(result);
                g.DrawString("TOP " + rows.Count + "   ·   棱彩 " + prism + " / 黄金 " + gold + " / 白银 " + silver + "   ·   " + source,
                    summary, new SolidBrush(Muted), 266, 518);
            }

            if (rows.Count == 0)
            {
                using (var font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold, GraphicsUnit.Pixel))
                    g.DrawString("暂无强化排行，基础攻略仍可正常使用", font, new SolidBrush(Color.FromArgb(166, 181, 207)), 56, 590);
                return;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var col = i % 2;
                var rowIndex = i / 2;
                var rect = new Rectangle(56 + col * 574, 558 + rowIndex * 106, 548, 92);
                DrawAugmentRow(g, rect, rows[i], images);
            }
        }

        private static void DrawAugmentRow(Graphics g, Rectangle rect, MayhemAugmentRow row, IDictionary<string, Bitmap> images)
        {
            using (var path = RoundedRect(rect, 15))
            using (var brush = new SolidBrush(PanelSoft))
            using (var pen = new Pen(Color.FromArgb(45, 136, 166, 215)))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            DrawSquareImage(g, images, row.IconUrl, new Rectangle(rect.X + 12, rect.Y + 14, 62, 62), 13, FirstChar(row.Name));
            using (var rank = new Font("Segoe UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var name = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var stat = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var desc = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString("#" + Math.Max(1, row.Rank), rank, new SolidBrush(Color.FromArgb(180, 196, 225)), rect.X + 84, rect.Y + 11);
                g.DrawString(Ellipsis(row.Name, 18), name, Brushes.White, rect.X + 118, rect.Y + 9);
                DrawRarityPill(g, row.Rarity, rect.Right - 72, rect.Y + 10);
                var stats = "胜率 " + FormatPercent(row.WinRate) + "   选择 " + FormatPercent(row.PickRate);
                if (row.Games.HasValue && row.Games.Value > 0) stats += "   " + FormatGames(row.Games.Value) + " 局";
                g.DrawString(stats, stat, new SolidBrush(Green), rect.X + 84, rect.Y + 35);
                DrawWrappedText(g, FirstNonEmpty(row.Description, "暂无效果说明"), desc, Muted,
                    new RectangleF(rect.X + 84, rect.Y + 56, rect.Width - 98, 28), 2);
            }
        }

        private static void DrawRarityPill(Graphics g, string rarity, int x, int y)
        {
            var text = FirstNonEmpty(rarity, "未知");
            var color = text.Contains("棱") ? Purple : text.Contains("金") ? Gold : text.Contains("银") ? Color.FromArgb(172, 190, 214) : Muted;
            using (var font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var path = RoundedRect(new Rectangle(x, y, 56, 22), 10))
            using (var fill = new SolidBrush(Color.FromArgb(32, color.R, color.G, color.B)))
            using (var pen = new Pen(Color.FromArgb(95, color.R, color.G, color.B)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, new SolidBrush(color), x + (56 - size.Width) / 2F, y + 3);
            }
        }

        private static void DrawCompactBuild(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var panel = new Rectangle(30, 1252, 760, 224);
            DrawPanel(g, panel);
            DrawSectionTitle(g, "技能与出装", 56, 1272);
            using (var label = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString("技能", label, new SolidBrush(Muted), 56, 1310);
                DrawSkills(g, result, images, 102, 1301);
                DrawWrappedText(g, FirstNonEmpty(result.SkillOrder, "暂无加点顺序"), body, Text, new RectangleF(430, 1308, 330, 36), 2);
                g.DrawString("核心装备", label, new SolidBrush(Muted), 56, 1370);
                DrawCompactItems(g, result.CoreItems, result.CoreItemIconUrls, images, 132, 1358);
                var balance = FirstNonEmpty(result.MayhemBalanceSummary, result.BaseBalanceSummary, string.Empty);
                if (!string.IsNullOrWhiteSpace(balance))
                    DrawWrappedText(g, balance, body, Muted, new RectangleF(56, 1430, 694, 34), 2);
            }
        }

        private static void DrawTopRanking(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            var panel = new Rectangle(810, 1252, 420, 224);
            DrawPanel(g, panel);
            DrawSectionTitle(g, "版本胜率前五", 834, 1272);
            var top = (result.TopTen ?? new List<MayhemTopChampion>()).OrderBy(item => item.Rank).Take(5).ToList();
            using (var font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                for (var i = 0; i < 5; i++)
                {
                    var y = 1307 + i * 31;
                    if (i >= top.Count)
                    {
                        g.DrawString((i + 1) + "   暂无数据", small, new SolidBrush(Color.FromArgb(105, 121, 149)), 834, y + 5);
                        continue;
                    }
                    var item = top[i];
                    g.DrawString(item.Rank.ToString(CultureInfo.InvariantCulture), font, new SolidBrush(i < 3 ? Gold : Muted), 834, y + 5);
                    DrawSquareImage(g, images, item.IconUrl, new Rectangle(864, y, 26, 26), 6, FirstChar(item.Name));
                    g.DrawString(Ellipsis(item.Name, 11), font, Brushes.White, 900, y + 4);
                    var win = FormatPercent(item.WinRate);
                    var width = g.MeasureString(win, font).Width;
                    g.DrawString(win, font, new SolidBrush(Green), 1204 - width, y + 4);
                }
            }
        }

        private static void DrawSkills(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images, int x, int y)
        {
            var keys = new[] { "Q", "W", "E", "R" };
            using (var font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                for (var i = 0; i < keys.Length; i++)
                {
                    string reference;
                    result.SkillIconUrls.TryGetValue(keys[i], out reference);
                    var rect = new Rectangle(x + i * 62, y, 44, 44);
                    DrawSquareImage(g, images, reference, rect, 9, keys[i]);
                    g.DrawString(keys[i], font, Brushes.White, rect.Right - 10, rect.Bottom - 13);
                }
            }
        }

        private static void DrawCompactItems(Graphics g, IList<string> names, IList<string> references, IDictionary<string, Bitmap> images, int x, int y)
        {
            names = names ?? new List<string>();
            references = references ?? new List<string>();
            for (var i = 0; i < Math.Min(5, Math.Max(names.Count, references.Count)); i++)
            {
                var reference = i < references.Count ? references[i] : null;
                var name = i < names.Count ? names[i] : "";
                DrawSquareImage(g, images, reference, new Rectangle(x + i * 74, y, 52, 52), 10, FirstChar(name));
            }
            if (names.Count == 0 && references.Count == 0)
            {
                using (var font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Pixel))
                    g.DrawString("暂无装备数据", font, new SolidBrush(Muted), x, y + 17);
            }
        }

        private static string SourceLabel(MayhemChampionResult result)
        {
            if (result == null) return "来源：—";
            var route = result.AugmentSourceRoute;
            if (string.Equals(route, "fresh-cache", StringComparison.OrdinalIgnoreCase)) return "本地缓存 · 15 分钟内";
            if (string.Equals(route, "stale-cache", StringComparison.OrdinalIgnoreCase) || result.AugmentSourceStale) return "离线缓存 · 上次可用数据";
            return "OP.GG Global · 实时";
        }

        private static void DrawFooter(Graphics g, MayhemChampionResult result)
        {
            using (var font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var source = "FACM · 海斗攻略";
                if (!string.IsNullOrWhiteSpace(result.SourceNote)) source += " · " + Ellipsis(result.SourceNote, 48);
                g.DrawString(source, font, new SolidBrush(Color.FromArgb(116, 136, 169)), 34, 1504);
                var note = "强化路线由单项胜率/选择率推导，仅作当前版本决策参考";
                var width = g.MeasureString(note, font).Width;
                g.DrawString(note, font, new SolidBrush(Color.FromArgb(116, 136, 169)), CardWidth - 34 - width, 1504);
            }
        }

        private static void DrawMetric(Graphics g, Rectangle rect, string title, string value, Color accent)
        {
            using (var path = RoundedRect(rect, 18))
            using (var fill = new SolidBrush(Color.FromArgb(115, 12, 23, 39)))
            using (var pen = new Pen(Color.FromArgb(95, accent.R, accent.G, accent.B)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            using (var label = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var number = new Font("Segoe UI", 25F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString(title, label, new SolidBrush(Muted), rect.X + 16, rect.Y + 13);
                g.DrawString(value, number, new SolidBrush(accent), rect.X + 16, rect.Y + 38);
            }
        }

        private static void DrawBadge(Graphics g, string text, Point point, int width, Font font, Color color)
        {
            var rect = new Rectangle(point.X, point.Y, width, 30);
            using (var path = RoundedRect(rect, 13))
            using (var fill = new SolidBrush(Color.FromArgb(190, color)))
            {
                g.FillPath(fill, path);
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, Brushes.White, rect.X + (width - size.Width) / 2F, rect.Y + 6);
            }
        }

        private static void DrawPanel(Graphics g, Rectangle rect)
        {
            using (var path = RoundedRect(rect, 22))
            using (var fill = new SolidBrush(Color.FromArgb(222, Panel)))
            using (var pen = new Pen(Color.FromArgb(55, 132, 163, 211)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        private static void DrawSectionTitle(Graphics g, string text, int x, int y)
        {
            using (var font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold, GraphicsUnit.Pixel))
                g.DrawString(text, font, Brushes.White, x, y);
        }

        private static void DrawSquareImage(Graphics g, IDictionary<string, Bitmap> images, string reference, Rectangle rect, int radius, string fallback)
        {
            using (var path = RoundedRect(rect, radius))
            {
                g.SetClip(path);
                Bitmap image;
                if (TryGet(images, reference, out image)) DrawImageCover(g, image, rect);
                else
                {
                    using (var fill = new LinearGradientBrush(rect, Color.FromArgb(52, 72, 112), Color.FromArgb(31, 45, 72), 90F))
                        g.FillRectangle(fill, rect);
                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        using (var font = new Font("Microsoft YaHei UI", Math.Max(11, rect.Width / 3F), FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            var size = g.MeasureString(fallback, font);
                            g.DrawString(fallback, font, new SolidBrush(Color.FromArgb(220, 232, 249)), rect.X + (rect.Width - size.Width) / 2F, rect.Y + (rect.Height - size.Height) / 2F);
                        }
                    }
                }
                g.ResetClip();
                using (var pen = new Pen(Color.FromArgb(65, 156, 183, 230))) g.DrawPath(pen, path);
            }
        }

        private static void DrawImageCover(Graphics g, Bitmap bitmap, Rectangle rect)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0) return;
            var scale = Math.Max(rect.Width / (double)bitmap.Width, rect.Height / (double)bitmap.Height);
            var width = (int)Math.Ceiling(bitmap.Width * scale);
            var height = (int)Math.Ceiling(bitmap.Height * scale);
            var target = new Rectangle(rect.X + (rect.Width - width) / 2, rect.Y + (rect.Height - height) / 2, width, height);
            g.DrawImage(bitmap, target);
        }

        private static bool TryGet(IDictionary<string, Bitmap> images, string reference, out Bitmap bitmap)
        {
            bitmap = null;
            return !string.IsNullOrWhiteSpace(reference) && images != null && images.TryGetValue(reference, out bitmap) && bitmap != null;
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
            arc.X = rect.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawWrappedText(Graphics g, string text, Font font, Color color, RectangleF rect, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 1) words = text.Select(ch => ch.ToString()).ToArray();
            var lines = new List<string>();
            var current = string.Empty;
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : (words.Length == text.Length ? current + word : current + " " + word);
                if (g.MeasureString(candidate, font).Width <= rect.Width || current.Length == 0)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                    if (lines.Count >= maxLines) break;
                }
            }
            if (lines.Count < maxLines && current.Length > 0) lines.Add(current);
            if (lines.Count > maxLines) lines = lines.Take(maxLines).ToList();
            using (var brush = new SolidBrush(color))
            {
                var lineHeight = font.GetHeight(g) + 1;
                for (var i = 0; i < lines.Count; i++)
                    g.DrawString(EllipsisByWidth(g, lines[i], font, rect.Width), font, brush, rect.X, rect.Y + i * lineHeight);
            }
        }

        private static string EllipsisByWidth(Graphics g, string value, Font font, float width)
        {
            if (string.IsNullOrEmpty(value) || g.MeasureString(value, font).Width <= width) return value;
            var text = value;
            while (text.Length > 1 && g.MeasureString(text + "…", font).Width > width) text = text.Substring(0, text.Length - 1);
            return text + "…";
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "—";
        }

        private static string FormatGames(int value)
        {
            return value >= 10000 ? (value / 10000d).ToString("0.#", CultureInfo.InvariantCulture) + "万" : value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string FirstChar(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "?" : value.Trim().Substring(0, 1).ToUpperInvariant();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values == null ? string.Empty : values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string Ellipsis(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, Math.Max(1, max - 1)) + "…";
        }
    }
}
