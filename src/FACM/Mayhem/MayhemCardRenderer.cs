using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FACM.League;

namespace FACM.Mayhem
{
    internal static class MayhemCardRenderer
    {
        public const int CardWidth = 840;
        public const int CardHeight = 1120;

        private const int Outer = 16;
        private const int Gap = 8;
        private const int HeroHeight = 118;
        private const int BalanceHeight = 144;
        private const int BuildHeight = 142;
        private const int SectionHeaderHeight = 28;
        private const int AugmentCardHeight = 64;
        private const int AugmentCardGap = 6;
        private const int RouteHeight = 96;
        private const int FooterHeight = 38;

        private static readonly Color Background = Color.FromArgb(12, 17, 30);
        private static readonly Color Panel = Color.FromArgb(23, 31, 51);
        private static readonly Color PanelSoft = Color.FromArgb(28, 39, 64);
        private static readonly Color Text = Color.FromArgb(241, 246, 252);
        private static readonly Color Muted = Color.FromArgb(155, 170, 193);
        private static readonly Color MutedDark = Color.FromArgb(122, 139, 164);
        private static readonly Color Line = Color.FromArgb(48, 63, 88);
        private static readonly Color Cyan = Color.FromArgb(63, 207, 225);
        private static readonly Color Green = Color.FromArgb(62, 211, 151);
        private static readonly Color Gold = Color.FromArgb(247, 190, 76);
        private static readonly Color Purple = Color.FromArgb(192, 132, 252);
        private static readonly Color Silver = Color.FromArgb(173, 188, 209);

        private sealed class LoadedImage : IDisposable
        {
            public string Reference { get; set; }
            public Bitmap Bitmap { get; set; }

            public void Dispose()
            {
                if (Bitmap != null) Bitmap.Dispose();
                Bitmap = null;
            }
        }

        private sealed class AugmentGroup
        {
            public string Title { get; set; }
            public string Kind { get; set; }
            public Color Accent { get; set; }
            public List<MayhemAugmentRow> Items { get; set; } = new List<MayhemAugmentRow>();
        }

        public static async Task<Bitmap> RenderAsync(
            MayhemChampionResult result,
            ILeagueClientApi leagueClient,
            CancellationToken token)
        {
            result = result ?? new MayhemChampionResult();

            var buildTask = MayhemBuildDetailsService.EnrichAsync(result, token);
            var seedTask = LoadReferencesAsync(
                CollectSeedReferences(result),
                leagueClient,
                token,
                TimeSpan.FromMilliseconds(1150));

            await Task.WhenAll(buildTask, seedTask).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var seedImages = seedTask.Result;
            var buildImages = await LoadReferencesAsync(
                CollectBuildReferences(result),
                leagueClient,
                token,
                TimeSpan.FromMilliseconds(550)).ConfigureAwait(false);

            var images = MergeImages(seedImages, buildImages);
            try
            {
                return Render(result, images);
            }
            finally
            {
                foreach (var image in images.Values) image.Dispose();
            }
        }

        internal static Bitmap RenderForSmokeTest(MayhemChampionResult result)
        {
            return Render(result ?? new MayhemChampionResult(), new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase));
        }

        private static Bitmap Render(MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            EnsureRenderProjection(result);
            var rows = (result.AugmentRows ?? new List<MayhemAugmentRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .OrderBy(row => row.Rank <= 0 ? int.MaxValue : row.Rank)
                .Take(10)
                .ToList();
            var groups = BuildAugmentGroups(rows);
            var routes = BuildDecisionRoutes(rows);
            var sectionsHeight = CalculateSectionsHeight(groups);
            var height = Outer + HeroHeight + Gap + BalanceHeight + Gap + BuildHeight + Gap + sectionsHeight;
            if (routes.Count > 0) height += Gap + RouteHeight;
            height += FooterHeight + Outer;

            var bitmap = new Bitmap(CardWidth, Math.Max(520, height), PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                graphics.Clear(Background);

                var y = Outer;
                DrawHero(graphics, result, rows, groups, images, y);
                y += HeroHeight + Gap;
                DrawBalance(graphics, result, y);
                y += BalanceHeight + Gap;
                DrawBuild(graphics, result, images, y);
                y += BuildHeight + Gap;
                y = DrawAugmentSections(graphics, groups, images, y);
                if (routes.Count > 0)
                {
                    y += Gap;
                    DrawRoutes(graphics, routes, y);
                }
                DrawFooter(graphics, bitmap.Height);
            }
            return bitmap;
        }

        private static void DrawHero(
            Graphics g,
            MayhemChampionResult result,
            IList<MayhemAugmentRow> rows,
            IList<AugmentGroup> groups,
            IDictionary<string, Bitmap> images,
            int y)
        {
            var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, HeroHeight);
            DrawPanel(g, rect, 14);
            DrawSquareImage(g, images, result.ChampionIconUrl, new Rectangle(Outer + 18, y + 21, 76, 76), 12, FirstChar(result.ChampionName), Cyan);

            using (var title = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subtitle = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var titleX = Outer + 112;
                var metricX = CardWidth - Outer - 196;
                var titleWidth = metricX - titleX - 12;
                var champion = FirstNonEmpty(result.ChampionName, result.ChampionSlug, MayhemUiCopy.EmptyCard);
                g.DrawString(FitText(g, champion, title, titleWidth), title, new SolidBrush(Text), titleX, y + 18);
                g.DrawString(MayhemUiCopy.CompactCardSubtitle, subtitle, new SolidBrush(Cyan), titleX, y + 51);

                var rank = result.Rank.HasValue ? MayhemUiCopy.RankPrefix + result.Rank.Value : MayhemUiCopy.RankEmpty;
                var tier = FirstNonEmpty(result.Tier, MayhemUiCopy.NoTier);
                var summary = "TOP " + rows.Count + " · " + rank + " · " + tier + " · " + MayhemUiCopy.CompactSummarySuffix;
                g.DrawString(FitText(g, summary, small, titleWidth), small, new SolidBrush(Muted), titleX, y + 76);

                var patch = string.IsNullOrWhiteSpace(result.Patch) ? "—" : result.Patch;
                var sourceLine = MayhemUiCopy.SourceOpggShort + " · " + MayhemUiCopy.PatchPrefix + patch;
                g.DrawString(FitText(g, sourceLine, tiny, titleWidth), tiny, new SolidBrush(MutedDark), titleX, y + 96);

                DrawMetric(g, new Rectangle(metricX, y + 20, 94, 36), MayhemUiCopy.MetricList, "TOP " + rows.Count, Cyan);
                DrawMetric(g, new Rectangle(metricX + 102, y + 20, 94, 36), MayhemUiCopy.MetricSource, MayhemUiCopy.SourceOpggShort, Green);
                DrawMetric(g, new Rectangle(metricX, y + 62, 94, 36), MayhemUiCopy.MetricQuality, groups.Count.ToString(), Purple);
                DrawMetric(g, new Rectangle(metricX + 102, y + 62, 94, 36), MayhemUiCopy.MetricCache, MayhemUiCopy.CacheFifteenMinutes, Gold);
            }
        }

        private static void DrawBalance(Graphics g, MayhemChampionResult result, int y)
        {
            var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, BalanceHeight);
            DrawPanel(g, rect, 12);
            using (var head = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString(MayhemUiCopy.BalanceLayers, head, new SolidBrush(Text), Outer + 14, y + 10);
                var noteWidth = g.MeasureString(MayhemUiCopy.BalanceLayerNote, tiny).Width;
                g.DrawString(MayhemUiCopy.BalanceLayerNote, tiny, new SolidBrush(Muted), CardWidth - Outer - 14 - noteWidth, y + 12);
            }

            var baseSummary = FirstNonEmpty(result.BaseBalanceSummary, MayhemUiCopy.NoBalance);
            var mayhemSummary = FirstNonEmpty(result.MayhemBalanceSummary, result.BalanceSummary, MayhemUiCopy.NoBalance);
            var baseStatus = BalanceStatus(result.BaseBalancePatch, result.BaseBalanceComplete, result.BaseBalanceStatus);
            var mayhemStatus = BalanceStatus(FirstNonEmpty(result.RankingPatch, result.Patch), !string.IsNullOrWhiteSpace(result.RankingPatch), null);
            DrawBalanceLayer(g, MayhemUiCopy.BaseAram, baseSummary, baseStatus, y + 35, Cyan);
            DrawBalanceLayer(g, MayhemUiCopy.MayhemOnly, mayhemSummary, mayhemStatus, y + 87, Gold);
        }

        private static void DrawBalanceLayer(Graphics g, string labelText, string summary, string status, int y, Color accent)
        {
            using (var label = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString(labelText, label, new SolidBrush(accent), Outer + 14, y);
                var statusWidth = g.MeasureString(status, tiny).Width;
                g.DrawString(status, tiny, new SolidBrush(Muted), CardWidth - Outer - 14 - statusWidth, y);
                var chip = new Rectangle(Outer + 14, y + 16, 520, 22);
                using (var path = RoundedRect(chip, 6))
                using (var fill = new SolidBrush(Color.FromArgb(31, 43, 68)))
                {
                    g.FillPath(fill, path);
                }
                g.DrawString(FitText(g, summary, body, chip.Width - 16), body, new SolidBrush(Text), chip.X + 8, chip.Y + 4);
            }
        }

        private static void DrawBuild(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images, int y)
        {
            var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, BuildHeight);
            DrawPanel(g, rect, 12);
            using (var head = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString(MayhemUiCopy.CompactBuild, head, new SolidBrush(Text), Outer + 14, y + 10);
                var status = BuildStatus(result);
                var statusWidth = g.MeasureString(status, tiny).Width;
                g.DrawString(status, tiny, new SolidBrush(Muted), CardWidth - Outer - 14 - statusWidth, y + 12);
            }

            using (var small = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var coreY = y + 31;
                g.DrawString(MayhemUiCopy.Core, small, new SolidBrush(Cyan), Outer + 14, coreY + 7);
                var builds = result.CoreBuilds.Take(2).ToList();
                if (builds.Count == 0)
                {
                    g.DrawString(MayhemUiCopy.NoCoreBuild, tiny, new SolidBrush(Muted), Outer + 64, coreY + 8);
                }
                else
                {
                    for (var index = 0; index < builds.Count; index++)
                    {
                        var build = builds[index];
                        var bx = Outer + 64 + index * 340;
                        g.DrawString("#" + Math.Max(1, build.Rank), tiny, new SolidBrush(index == 0 ? Gold : Silver), bx, coreY + 7);
                        var iconX = bx + 24;
                        foreach (var item in build.Items.Take(5))
                        {
                            DrawSquareImage(g, images, item.IconUrl, new Rectangle(iconX, coreY, 30, 30), 5, FirstChar(item.Name), Line);
                            iconX += 34;
                        }
                    }
                }

                var dividerY = y + 70;
                using (var pen = new Pen(Line)) g.DrawLine(pen, Outer + 14, dividerY, CardWidth - Outer - 14, dividerY);

                var groupY = y + 80;
                DrawBuildGroup(g, MayhemUiCopy.Starter, result.StarterItems, images, Outer + 14, groupY, 3);
                DrawBuildGroup(g, MayhemUiCopy.Boots, result.BootItems, images, Outer + 202, groupY, 1);
                DrawBuildGroup(g, MayhemUiCopy.Summoner, result.SummonerSpells, images, Outer + 300, groupY, 2);
                DrawSkillPriority(g, result, images, Outer + 462, groupY);
            }
        }

        private static void DrawBuildGroup(
            Graphics g,
            string label,
            IList<MayhemBuildItem> items,
            IDictionary<string, Bitmap> images,
            int x,
            int y,
            int limit)
        {
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString(label, tiny, new SolidBrush(Muted), x, y);
                var iconX = x;
                var values = items ?? new List<MayhemBuildItem>();
                foreach (var item in values.Take(limit))
                {
                    DrawSquareImage(g, images, item.IconUrl, new Rectangle(iconX, y + 18, 26, 26), 5, FirstChar(item.Name), Line);
                    iconX += 30;
                }
                if (values.Count == 0) g.DrawString(MayhemUiCopy.NoValue, tiny, new SolidBrush(MutedDark), x, y + 26);
            }
        }

        private static void DrawSkillPriority(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images, int x, int y)
        {
            var skills = GetSkillPriority(result);
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var small = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.DrawString(MayhemUiCopy.SkillPriority, tiny, new SolidBrush(Muted), x, y);
                if (skills.Count == 0)
                {
                    g.DrawString(MayhemUiCopy.NoValue, tiny, new SolidBrush(MutedDark), x, y + 26);
                    return;
                }

                var iconX = x;
                for (var index = 0; index < skills.Count && index < 3; index++)
                {
                    var skill = skills[index];
                    DrawSquareImage(g, images, skill.IconUrl, new Rectangle(iconX, y + 18, 26, 26), 5, skill.Key, Cyan);
                    g.DrawString(skill.Key, tiny, new SolidBrush(Cyan), iconX + 8, y + 45);
                    if (index < skills.Count - 1) g.DrawString(MayhemUiCopy.SkillSeparator, tiny, new SolidBrush(Muted), iconX + 31, y + 27);
                    iconX += 42;
                }
                var order = string.Join(" > ", skills.Take(3).Select(skill => skill.Key));
                var width = g.MeasureString(order, small).Width;
                g.DrawString(order, small, new SolidBrush(Text), CardWidth - Outer - 14 - width, y + 26);
            }
        }

        private static int DrawAugmentSections(Graphics g, IList<AugmentGroup> groups, IDictionary<string, Bitmap> images, int y)
        {
            if (groups.Count == 0)
            {
                var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, 72);
                DrawPanel(g, rect, 12);
                using (var font = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    var textSize = g.MeasureString(MayhemUiCopy.NoAugmentRanking, font);
                    g.DrawString(MayhemUiCopy.NoAugmentRanking, font, new SolidBrush(Muted), (CardWidth - textSize.Width) / 2F, y + 27);
                }
                return y + 72;
            }

            foreach (var group in groups)
            {
                var rowCount = (group.Items.Count + 1) / 2;
                var sectionHeight = SectionHeaderHeight + rowCount * AugmentCardHeight + Math.Max(0, rowCount - 1) * AugmentCardGap + 8;
                var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, sectionHeight);
                DrawPanel(g, rect, 12);
                using (var head = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    g.DrawString(group.Title, head, new SolidBrush(group.Accent), Outer + 14, y + 9);
                    var countText = group.Items.Count + MayhemUiCopy.ItemsSuffix;
                    var width = g.MeasureString(countText, tiny).Width;
                    g.DrawString(countText, tiny, new SolidBrush(Muted), CardWidth - Outer - 14 - width, y + 11);
                }

                var innerWidth = CardWidth - Outer * 2 - 28;
                var columnGap = 8;
                var cardWidth = (innerWidth - columnGap) / 2;
                var baseY = y + SectionHeaderHeight;
                for (var index = 0; index < group.Items.Count; index++)
                {
                    var rowIndex = index / 2;
                    var column = index % 2;
                    var x = Outer + 14 + column * (cardWidth + columnGap);
                    var cardY = baseY + rowIndex * (AugmentCardHeight + AugmentCardGap);
                    DrawAugmentCard(g, new Rectangle(x, cardY, cardWidth, AugmentCardHeight), group.Items[index], group.Accent, images);
                }
                y += sectionHeight + Gap;
            }
            return y - Gap;
        }

        private static void DrawAugmentCard(
            Graphics g,
            Rectangle rect,
            MayhemAugmentRow row,
            Color accent,
            IDictionary<string, Bitmap> images)
        {
            using (var path = RoundedRect(rect, 10))
            using (var fill = new SolidBrush(PanelSoft))
            using (var pen = new Pen(Line))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            DrawSquareImage(g, images, row.IconUrl, new Rectangle(rect.X + 9, rect.Y + 12, 40, 40), 7, FirstChar(row.Name), accent);
            using (var nameFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var stat = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var nameWidth = rect.Width - 164;
                g.DrawString(FitText(g, row.Name, nameFont, nameWidth), nameFont, new SolidBrush(Text), rect.X + 58, rect.Y + 7);
                var description = FirstNonEmpty(row.Description, MayhemUiCopy.NoDescription);
                g.DrawString(FitText(g, description, body, nameWidth), body, new SolidBrush(Muted), rect.X + 58, rect.Y + 29);
                var sampleText = row.Games.HasValue && row.Games.Value > 0
                    ? MayhemUiCopy.Sample + " " + FormatGames(row.Games.Value)
                    : "#" + Math.Max(1, row.Rank);
                g.DrawString(FitText(g, sampleText, body, nameWidth), body, new SolidBrush(MutedDark), rect.X + 58, rect.Y + 47);

                var statX = rect.Right - 10;
                var winText = FormatPercent(row.WinRate);
                var pickText = FormatPercent(row.PickRate);
                var winWidth = g.MeasureString(winText, stat).Width;
                var pickWidth = g.MeasureString(pickText, small).Width;
                g.DrawString(MayhemUiCopy.Win.Trim(), body, new SolidBrush(Muted), statX - 64, rect.Y + 8);
                g.DrawString(winText, stat, new SolidBrush(Green), statX - winWidth, rect.Y + 7);
                g.DrawString(MayhemUiCopy.Popularity, body, new SolidBrush(Muted), statX - 64, rect.Y + 34);
                g.DrawString(pickText, small, new SolidBrush(accent), statX - pickWidth, rect.Y + 34);
            }
        }

        private static void DrawRoutes(Graphics g, IList<MayhemDecisionRoute> routes, int y)
        {
            var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, RouteHeight);
            DrawPanel(g, rect, 12);
            using (var head = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString(MayhemUiCopy.ChoiceDirection, head, new SolidBrush(Text), Outer + 14, y + 9);
                var noteWidth = g.MeasureString(MayhemUiCopy.SingleAugmentNote, tiny).Width;
                g.DrawString(MayhemUiCopy.SingleAugmentNote, tiny, new SolidBrush(Muted), CardWidth - Outer - 14 - noteWidth, y + 11);
            }

            var innerWidth = CardWidth - Outer * 2 - 28;
            var routeGap = 8;
            var routeWidth = (innerWidth - routeGap * 2) / 3;
            var accents = new[] { Cyan, Green, Gold };
            for (var index = 0; index < routes.Count && index < 3; index++)
            {
                var route = routes[index];
                var x = Outer + 14 + index * (routeWidth + routeGap);
                var routeRect = new Rectangle(x, y + 29, routeWidth, 54);
                using (var path = RoundedRect(routeRect, 9))
                using (var fill = new SolidBrush(PanelSoft))
                using (var pen = new Pen(Line))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
                using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var body = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.DrawString(route.Title, tiny, new SolidBrush(accents[index]), x + 9, y + 36);
                    g.DrawString(FitText(g, route.AugmentName, body, routeWidth - 18), body, new SolidBrush(Text), x + 9, y + 52);
                    g.DrawString(FitText(g, route.Hint, tiny, routeWidth - 18), tiny, new SolidBrush(Muted), x + 9, y + 70);
                }
            }
        }

        private static void DrawFooter(Graphics g, int height)
        {
            var y = height - FooterHeight - Outer;
            using (var pen = new Pen(Line)) g.DrawLine(pen, Outer, y, CardWidth - Outer, y);
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.DrawString(MayhemUiCopy.FooterProduct, tiny, new SolidBrush(Muted), Outer, y + 14);
                var width = g.MeasureString(MayhemUiCopy.FooterDisclaimer, tiny).Width;
                g.DrawString(MayhemUiCopy.FooterDisclaimer, tiny, new SolidBrush(Muted), CardWidth - Outer - width, y + 14);
            }
        }

        private static void DrawMetric(Graphics g, Rectangle rect, string label, string value, Color accent)
        {
            using (var path = RoundedRect(rect, 9))
            using (var fill = new SolidBrush(PanelSoft))
            using (var pen = new Pen(Line))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var metric = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
                g.DrawString(label, tiny, new SolidBrush(Muted), rect.X + 9, rect.Y + 4);
                g.DrawString(FitText(g, value, metric, rect.Width - 18), metric, new SolidBrush(accent), rect.X + 9, rect.Y + 17);
            }
        }

        private static void DrawPanel(Graphics g, Rectangle rect, int radius)
        {
            using (var path = RoundedRect(rect, radius))
            using (var fill = new SolidBrush(Panel))
            using (var pen = new Pen(Line))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        private static void DrawSquareImage(
            Graphics g,
            IDictionary<string, Bitmap> images,
            string reference,
            Rectangle rect,
            int radius,
            string fallback,
            Color border)
        {
            using (var path = RoundedRect(rect, radius))
            {
                g.SetClip(path);
                Bitmap bitmap;
                if (!string.IsNullOrWhiteSpace(reference) && images.TryGetValue(reference, out bitmap) && bitmap != null)
                {
                    DrawImageCover(g, bitmap, rect);
                }
                else
                {
                    using (var fill = new LinearGradientBrush(rect, Color.FromArgb(43, 54, 78), Color.FromArgb(31, 42, 66), 90F))
                        g.FillRectangle(fill, rect);
                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        using (var font = new Font("Microsoft YaHei UI", Math.Max(9F, rect.Width / 3.2F), FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            var text = fallback.Substring(0, 1);
                            var size = g.MeasureString(text, font);
                            g.DrawString(text, font, new SolidBrush(Muted), rect.X + (rect.Width - size.Width) / 2F, rect.Y + (rect.Height - size.Height) / 2F);
                        }
                    }
                }
                g.ResetClip();
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
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

        private static IList<AugmentGroup> BuildAugmentGroups(IList<MayhemAugmentRow> rows)
        {
            var prism = new AugmentGroup { Title = MayhemUiCopy.PrismSection, Kind = "prism", Accent = Purple };
            var gold = new AugmentGroup { Title = MayhemUiCopy.GoldSection, Kind = "gold", Accent = Gold };
            var silver = new AugmentGroup { Title = MayhemUiCopy.SilverSection, Kind = "silver", Accent = Silver };
            var other = new AugmentGroup { Title = MayhemUiCopy.OtherSection, Kind = "other", Accent = Cyan };
            foreach (var row in rows)
            {
                switch (RarityKind(row.Rarity))
                {
                    case "prism": prism.Items.Add(row); break;
                    case "gold": gold.Items.Add(row); break;
                    case "silver": silver.Items.Add(row); break;
                    default: other.Items.Add(row); break;
                }
            }
            return new[] { prism, gold, silver, other }.Where(group => group.Items.Count > 0).ToList();
        }

        private static string RarityKind(string rarity)
        {
            var value = (rarity ?? string.Empty).Trim().ToLowerInvariant();
            if (value.Contains("prism") || value.Contains("棱") || value == MayhemUiCopy.Prism.ToLowerInvariant()) return "prism"; // ui-text-contract: allow
            if (value.Contains("gold") || value.Contains("黄金") || value == "金") return "gold"; // ui-text-contract: allow
            if (value.Contains("silver") || value.Contains("白银") || value == "银") return "silver"; // ui-text-contract: allow
            return "other";
        }

        private static List<MayhemDecisionRoute> BuildDecisionRoutes(IList<MayhemAugmentRow> rows)
        {
            var candidates = rows.Where(row => row.WinRate.HasValue || row.PickRate.HasValue).ToList();
            var routes = new List<MayhemDecisionRoute>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TakeRoute(routes, used, candidates.OrderByDescending(row => (row.WinRate ?? 0) * 0.72 + (row.PickRate ?? 0) * 0.28), MayhemUiCopy.StableRoute, MayhemUiCopy.StableShortHint);
            TakeRoute(routes, used, candidates.OrderByDescending(row => row.WinRate ?? -1), MayhemUiCopy.HighWinRoute, MayhemUiCopy.HighWinShortHint);
            TakeRoute(routes, used, candidates.OrderByDescending(row => row.PickRate ?? -1), MayhemUiCopy.PopularRoute, MayhemUiCopy.PopularShortHint);
            return routes;
        }

        private static void TakeRoute(
            List<MayhemDecisionRoute> routes,
            HashSet<string> used,
            IEnumerable<MayhemAugmentRow> ordered,
            string title,
            string hint)
        {
            foreach (var row in ordered)
            {
                if (string.IsNullOrWhiteSpace(row.Name) || !used.Add(row.Name)) continue;
                routes.Add(new MayhemDecisionRoute { Title = title, AugmentName = row.Name, Hint = hint });
                return;
            }
        }

        private static int CalculateSectionsHeight(IList<AugmentGroup> groups)
        {
            if (groups.Count == 0) return 72;
            var total = 0;
            foreach (var group in groups)
            {
                var rows = (group.Items.Count + 1) / 2;
                total += SectionHeaderHeight + rows * AugmentCardHeight + Math.Max(0, rows - 1) * AugmentCardGap + 8;
                total += Gap;
            }
            return total - Gap;
        }

        private static void EnsureRenderProjection(MayhemChampionResult result)
        {
            if (result.CoreBuilds.Count == 0 && result.CoreItems.Count > 0)
            {
                var path = new MayhemBuildPath { Rank = 1 };
                for (var index = 0; index < result.CoreItems.Count && index < 5; index++)
                {
                    path.Items.Add(new MayhemBuildItem
                    {
                        Name = result.CoreItems[index],
                        IconUrl = index < result.CoreItemIconUrls.Count ? result.CoreItemIconUrls[index] : null
                    });
                }
                result.CoreBuilds.Add(path);
            }
        }

        private static List<MayhemSkillPriority> GetSkillPriority(MayhemChampionResult result)
        {
            var output = (result.SkillPriority ?? new List<MayhemSkillPriority>())
                .Where(skill => skill != null && !string.IsNullOrWhiteSpace(skill.Key) && !string.Equals(skill.Key, "R", StringComparison.OrdinalIgnoreCase))
                .GroupBy(skill => skill.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(3)
                .ToList();
            if (output.Count >= 3 || string.IsNullOrWhiteSpace(result.SkillOrder)) return output;

            var seen = new HashSet<string>(output.Select(skill => skill.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var character in result.SkillOrder.ToUpperInvariant())
            {
                var key = character.ToString();
                if ((key != "Q" && key != "W" && key != "E") || !seen.Add(key)) continue;
                string icon;
                result.SkillIconUrls.TryGetValue(key, out icon);
                output.Add(new MayhemSkillPriority { Key = key, Name = key, IconUrl = icon });
                if (output.Count >= 3) break;
            }
            return output;
        }

        private static string BalanceStatus(string patch, bool verified, string status)
        {
            var patchText = string.IsNullOrWhiteSpace(patch) ? "—" : MayhemUiCopy.PatchPrefix + patch;
            var state = verified || string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)
                ? MayhemUiCopy.Verified
                : MayhemUiCopy.Syncing;
            return patchText + " · " + state;
        }

        private static string BuildStatus(MayhemChampionResult result)
        {
            var has = result.CoreBuilds.Count > 0 || result.StarterItems.Count > 0 || result.BootItems.Count > 0 || result.SummonerSpells.Count > 0;
            if (!has) return MayhemUiCopy.BuildMissing;
            if (result.BuildSourceStale || string.Equals(result.BuildSourceRoute, "stale-cache", StringComparison.OrdinalIgnoreCase)) return MayhemUiCopy.BuildCache;
            return MayhemUiCopy.BuildLive;
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##") + "%" : "—";
        }

        private static string FormatGames(int value)
        {
            if (value >= 10000) return (value / 10000D).ToString("0.#") + MayhemUiCopy.TenThousand;
            return value.ToString();
        }

        private static string FitText(Graphics g, string value, Font font, float maxWidth)
        {
            var text = value ?? string.Empty;
            if (maxWidth <= 8 || g.MeasureString(text, font).Width <= maxWidth) return text;
            const string suffix = "...";
            while (text.Length > 1 && g.MeasureString(text + suffix, font).Width > maxWidth)
                text = text.Substring(0, text.Length - 1);
            return text + suffix;
        }

        private static string FirstChar(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Substring(0, 1);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static IEnumerable<string> CollectSeedReferences(MayhemChampionResult result)
        {
            var values = new List<string>();
            AddReference(values, result.ChampionIconUrl);
            foreach (var row in (result.AugmentRows ?? new List<MayhemAugmentRow>()).Take(10)) AddReference(values, row == null ? null : row.IconUrl);
            foreach (var pair in result.SkillIconUrls) AddReference(values, pair.Value);
            return values.Distinct(StringComparer.OrdinalIgnoreCase).Take(28).ToList();
        }

        private static IEnumerable<string> CollectBuildReferences(MayhemChampionResult result)
        {
            var values = new List<string>();
            foreach (var build in (result.CoreBuilds ?? new List<MayhemBuildPath>()).Take(2))
                foreach (var item in build.Items.Take(5)) AddReference(values, item.IconUrl);
            foreach (var item in (result.StarterItems ?? new List<MayhemBuildItem>()).Take(3)) AddReference(values, item.IconUrl);
            foreach (var item in (result.BootItems ?? new List<MayhemBuildItem>()).Take(1)) AddReference(values, item.IconUrl);
            foreach (var item in (result.SummonerSpells ?? new List<MayhemBuildItem>()).Take(2)) AddReference(values, item.IconUrl);
            foreach (var skill in GetSkillPriority(result)) AddReference(values, skill.IconUrl);
            return values.Distinct(StringComparer.OrdinalIgnoreCase).Take(18).ToList();
        }

        private static void AddReference(ICollection<string> values, string reference)
        {
            if (!string.IsNullOrWhiteSpace(reference)) values.Add(reference);
        }

        private static async Task<Dictionary<string, Bitmap>> LoadReferencesAsync(
            IEnumerable<string> references,
            ILeagueClientApi leagueClient,
            CancellationToken token,
            TimeSpan budget)
        {
            var values = references == null
                ? new List<string>()
                : references.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var output = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
            if (values.Count == 0) return output;

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(budget);
                var tasks = values.Select(reference => LoadOneAsync(reference, leagueClient, timeout.Token, token)).ToArray();
                var loaded = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (var item in loaded)
                {
                    if (item == null || item.Bitmap == null || string.IsNullOrWhiteSpace(item.Reference)) continue;
                    if (output.ContainsKey(item.Reference)) item.Dispose();
                    else output[item.Reference] = item.Bitmap;
                }
            }
            return output;
        }

        private static async Task<LoadedImage> LoadOneAsync(
            string reference,
            ILeagueClientApi leagueClient,
            CancellationToken budgetToken,
            CancellationToken userToken)
        {
            try
            {
                var bitmap = await MayhemImageCache.GetAsync(reference, leagueClient, budgetToken).ConfigureAwait(false);
                return new LoadedImage { Reference = reference, Bitmap = bitmap };
            }
            catch (OperationCanceledException)
            {
                if (userToken.IsCancellationRequested) throw;
                return new LoadedImage { Reference = reference };
            }
            catch
            {
                return new LoadedImage { Reference = reference };
            }
        }

        private static Dictionary<string, Bitmap> MergeImages(
            IDictionary<string, Bitmap> first,
            IDictionary<string, Bitmap> second)
        {
            var output = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
            if (first != null)
            {
                foreach (var pair in first)
                    if (pair.Value != null) output[pair.Key] = pair.Value;
            }
            if (second != null)
            {
                foreach (var pair in second)
                {
                    if (pair.Value == null) continue;
                    Bitmap existing;
                    if (output.TryGetValue(pair.Key, out existing)) pair.Value.Dispose();
                    else output[pair.Key] = pair.Value;
                }
            }
            return output;
        }
    }
}
