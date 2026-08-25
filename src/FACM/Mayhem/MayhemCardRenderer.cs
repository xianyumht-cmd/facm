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
        private const int HeroHeight = 174;
        private const int BalanceHeight = 122;
        private const int BuildHeight = 196;
        private const int SectionHeaderHeight = 28;
        private const int AugmentCardHeight = 76;
        private const int AugmentCardGap = 6;
        private const int RouteHeight = 98;
        private const int FooterHeight = 30;

        private static readonly Color Background = Color.FromArgb(12, 17, 30);
        private static readonly Color Panel = Color.FromArgb(23, 31, 51);
        private static readonly Color PanelSoft = Color.FromArgb(28, 39, 64);
        private static readonly Color PanelStrong = Color.FromArgb(33, 47, 76);
        private static readonly Color Text = Color.FromArgb(241, 246, 252);
        private static readonly Color Muted = Color.FromArgb(155, 170, 193);
        private static readonly Color MutedDark = Color.FromArgb(105, 121, 146);
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

        internal static string BuildStrengthTextForSmokeTest(MayhemChampionResult result)
        {
            return BuildStrengthText(result ?? new MayhemChampionResult());
        }

        internal static string BuildCorePathTextForSmokeTest(MayhemChampionResult result)
        {
            EnsureRenderProjection(result);
            return BuildCorePathText(result, 3);
        }

        internal static string BuildPrimaryAugmentTextForSmokeTest(MayhemChampionResult result)
        {
            var rows = ProjectRows(result ?? new MayhemChampionResult());
            var row = SelectPrimaryAugment(rows);
            return BuildPrimaryAugmentText(row);
        }

        private static Bitmap Render(MayhemChampionResult result, IDictionary<string, Bitmap> images)
        {
            EnsureRenderProjection(result);
            var rows = ProjectRows(result);
            var groups = BuildAugmentGroups(rows);
            var routes = BuildDecisionRoutes(rows);
            var sectionsHeight = CalculateSectionsHeight(groups);
            var height = Outer + HeroHeight + Gap + BalanceHeight + Gap + BuildHeight + Gap + sectionsHeight;
            if (routes.Count > 0) height += Gap + RouteHeight;
            height += FooterHeight + Outer;

            var bitmap = new Bitmap(CardWidth, Math.Max(640, height), PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                graphics.Clear(Background);

                var y = Outer;
                DrawHero(graphics, result, rows, images, y);
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

        private static List<MayhemAugmentRow> ProjectRows(MayhemChampionResult result)
        {
            return (result.AugmentRows ?? new List<MayhemAugmentRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .OrderBy(row => row.Rank <= 0 ? int.MaxValue : row.Rank)
                .Take(10)
                .ToList();
        }

        private static void DrawHero(
            Graphics g,
            MayhemChampionResult result,
            IList<MayhemAugmentRow> rows,
            IDictionary<string, Bitmap> images,
            int y)
        {
            var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, HeroHeight);
            DrawPanel(g, rect, 14);
            DrawSquareImage(g, images, result.ChampionIconUrl, new Rectangle(Outer + 18, y + 17, 78, 78), 12, FirstChar(result.ChampionName), Cyan);

            using (var title = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subtitle = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var titleX = Outer + 112;
                var titleWidth = CardWidth - titleX - Outer - 18;
                var champion = FirstNonEmpty(result.ChampionName, result.ChampionSlug, MayhemUiCopy.EmptyCard);
                DrawText(g, FitText(g, champion, title, titleWidth), title, Text, titleX, y + 15);
                DrawText(g, MayhemUiCopy.CompactCardSubtitle, subtitle, Cyan, titleX, y + 48);
                DrawText(g, FitText(g, BuildHeroMeta(result), small, titleWidth), small, Muted, titleX, y + 70);
                DrawText(g, FitText(g, BuildSourceMeta(result), tiny, titleWidth), tiny, MutedDark, titleX, y + 89);

                DrawText(g, MayhemUiCopy.AtAGlance, subtitle, Text, Outer + 18, y + 109);
            }

            var primary = SelectPrimaryAugment(rows);
            var innerX = Outer + 18;
            var chipGap = 8;
            var chipWidth = (CardWidth - Outer * 2 - 36 - chipGap * 2) / 3;
            DrawDecisionChip(
                g,
                new Rectangle(innerX, y + 128, chipWidth, 34),
                MayhemUiCopy.StrengthPosition,
                BuildStrengthText(result),
                Cyan);
            DrawDecisionChip(
                g,
                new Rectangle(innerX + chipWidth + chipGap, y + 128, chipWidth, 34),
                MayhemUiCopy.PriorityAugment,
                BuildPrimaryAugmentText(primary),
                Purple);
            DrawDecisionChip(
                g,
                new Rectangle(innerX + (chipWidth + chipGap) * 2, y + 128, chipWidth, 34),
                MayhemUiCopy.FirstCorePath,
                BuildCorePathText(result, 2),
                Gold);
        }

        private static void DrawDecisionChip(Graphics g, Rectangle rect, string label, string value, Color accent)
        {
            using (var path = RoundedRect(rect, 8))
            using (var fill = new SolidBrush(PanelStrong))
            using (var pen = new Pen(Color.FromArgb(76, accent)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            using (var tiny = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var valueFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                DrawText(g, label, tiny, accent, rect.X + 9, rect.Y + 4);
                DrawText(g, FitText(g, FirstNonEmpty(value, MayhemUiCopy.NoValue), valueFont, rect.Width - 18), valueFont, Text, rect.X + 9, rect.Y + 17);
            }
        }

        private static string BuildHeroMeta(MayhemChampionResult result)
        {
            var pieces = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Tier)) pieces.Add(result.Tier);
            if (result.Rank.HasValue) pieces.Add(MayhemUiCopy.RankPrefix + result.Rank.Value);
            if (result.WinRate.HasValue) pieces.Add(MayhemUiCopy.HeroWinRate + " " + FormatPercent(result.WinRate));
            if (pieces.Count == 0) pieces.Add(MayhemUiCopy.CompactSummarySuffix);
            return string.Join(MayhemUiCopy.SeparatorDot, pieces);
        }

        private static string BuildSourceMeta(MayhemChampionResult result)
        {
            var patch = string.IsNullOrWhiteSpace(result.Patch) ? MayhemUiCopy.NoValue : result.Patch;
            return MayhemUiCopy.SourceOpggShort + MayhemUiCopy.SeparatorDot + MayhemUiCopy.PatchPrefix + patch + MayhemUiCopy.SeparatorDot + MayhemUiCopy.CacheFifteenMinutes;
        }

        private static string BuildStrengthText(MayhemChampionResult result)
        {
            var pieces = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Tier)) pieces.Add(result.Tier);
            if (result.Rank.HasValue) pieces.Add("#" + result.Rank.Value);
            if (result.WinRate.HasValue) pieces.Add(MayhemUiCopy.WinShort + FormatPercent(result.WinRate));
            return pieces.Count == 0 ? MayhemUiCopy.NoRanking : string.Join(MayhemUiCopy.SeparatorDot, pieces);
        }

        private static MayhemAugmentRow SelectPrimaryAugment(IList<MayhemAugmentRow> rows)
        {
            if (rows == null || rows.Count == 0) return null;
            var statistical = rows
                .Where(row => row.WinRate.HasValue || row.PickRate.HasValue)
                .OrderByDescending(row => (row.WinRate ?? 0D) * 0.72D + (row.PickRate ?? 0D) * 0.28D)
                .ThenBy(row => row.Rank <= 0 ? int.MaxValue : row.Rank)
                .FirstOrDefault();
            return statistical ?? rows.OrderBy(row => row.Rank <= 0 ? int.MaxValue : row.Rank).FirstOrDefault();
        }

        private static string BuildPrimaryAugmentText(MayhemAugmentRow row)
        {
            if (row == null) return MayhemUiCopy.NoDecisionStats;
            var pieces = new List<string> { row.Name };
            if (row.WinRate.HasValue) pieces.Add(MayhemUiCopy.WinShort + FormatPercent(row.WinRate));
            else if (row.Rank > 0) pieces.Add(MayhemUiCopy.PriorityPrefix + row.Rank);
            return string.Join(MayhemUiCopy.SeparatorDot, pieces);
        }

        private static string BuildCorePathText(MayhemChampionResult result, int limit)
        {
            var builds = result.CoreBuilds ?? new List<MayhemBuildPath>();
            var build = builds.FirstOrDefault(path => path != null && path.Items != null && path.Items.Any(item => item != null && !string.IsNullOrWhiteSpace(item.Name)));
            if (build == null) return MayhemUiCopy.NoCoreBuild;
            var names = build.Items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name.Trim())
                .Take(Math.Max(1, limit))
                .ToList();
            return names.Count == 0 ? MayhemUiCopy.NoCoreBuild : string.Join(MayhemUiCopy.CoreArrow, names);
        }

        private static void DrawBalance(Graphics g, MayhemChampionResult result, int y)
        {
            var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, BalanceHeight);
            DrawPanel(g, rect, 12);
            using (var head = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                DrawText(g, MayhemUiCopy.BalanceLayers, head, Text, Outer + 14, y + 10);
                var noteWidth = g.MeasureString(MayhemUiCopy.BalanceLayerNote, tiny).Width;
                DrawText(g, MayhemUiCopy.BalanceLayerNote, tiny, Muted, CardWidth - Outer - 14 - noteWidth, y + 12);
            }

            var baseSummary = FirstNonEmpty(result.BaseBalanceSummary, MayhemUiCopy.NoBalance);
            var mayhemSummary = FirstNonEmpty(result.MayhemBalanceSummary, result.BalanceSummary, MayhemUiCopy.NoBalance);
            var baseStatus = BalanceStatus(result.BaseBalancePatch, result.BaseBalanceComplete, result.BaseBalanceStatus);
            var mayhemStatus = BalanceStatus(FirstNonEmpty(result.RankingPatch, result.Patch), !string.IsNullOrWhiteSpace(result.RankingPatch), null);
            DrawBalanceLayer(g, MayhemUiCopy.BaseAram, baseSummary, baseStatus, y + 35, Cyan);
            DrawBalanceLayer(g, MayhemUiCopy.MayhemOnly, mayhemSummary, mayhemStatus, y + 75, Gold);
        }

        private static void DrawBalanceLayer(Graphics g, string labelText, string summary, string status, int y, Color accent)
        {
            using (var label = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                DrawText(g, labelText, label, accent, Outer + 14, y);
                var statusWidth = g.MeasureString(status, tiny).Width;
                DrawText(g, status, tiny, Muted, CardWidth - Outer - 14 - statusWidth, y);
                var chip = new Rectangle(Outer + 14, y + 16, CardWidth - Outer * 2 - 28, 20);
                using (var path = RoundedRect(chip, 6))
                using (var fill = new SolidBrush(Color.FromArgb(31, 43, 68)))
                {
                    g.FillPath(fill, path);
                }
                DrawText(g, FitText(g, summary, body, chip.Width - 16), body, Text, chip.X + 8, chip.Y + 3);
            }
        }

        private static void DrawBuild(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images, int y)
        {
            var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, BuildHeight);
            DrawPanel(g, rect, 12);
            using (var head = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                DrawText(g, MayhemUiCopy.CompactBuild, head, Text, Outer + 14, y + 10);
                var status = BuildStatus(result);
                var statusWidth = g.MeasureString(status, tiny).Width;
                DrawText(g, status, tiny, Muted, CardWidth - Outer - 14 - statusWidth, y + 12);
            }

            var builds = (result.CoreBuilds ?? new List<MayhemBuildPath>()).Where(build => build != null).Take(2).ToList();
            var innerWidth = CardWidth - Outer * 2 - 28;
            var buildGap = 8;
            var buildWidth = (innerWidth - buildGap) / 2;
            if (builds.Count == 0)
            {
                using (var body = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Pixel))
                    DrawText(g, MayhemUiCopy.NoCoreBuild, body, Muted, Outer + 14, y + 42);
            }
            else
            {
                for (var index = 0; index < builds.Count; index++)
                {
                    var bx = Outer + 14 + index * (buildWidth + buildGap);
                    DrawBuildPlan(g, result, builds[index], images, new Rectangle(bx, y + 32, buildWidth, 68), index);
                }
            }

            using (var pen = new Pen(Line)) g.DrawLine(pen, Outer + 14, y + 108, CardWidth - Outer - 14, y + 108);
            var miniY = y + 117;
            DrawBuildGroup(g, MayhemUiCopy.Starter, result.StarterItems, images, Outer + 14, miniY, 174, 3);
            DrawBuildGroup(g, MayhemUiCopy.Boots, result.BootItems, images, Outer + 196, miniY, 132, 1);
            DrawBuildGroup(g, MayhemUiCopy.Summoner, result.SummonerSpells, images, Outer + 336, miniY, 190, 2);
            DrawSkillPriority(g, result, images, Outer + 534, miniY, 276);
        }

        private static void DrawBuildPlan(
            Graphics g,
            MayhemChampionResult result,
            MayhemBuildPath build,
            IDictionary<string, Bitmap> images,
            Rectangle rect,
            int index)
        {
            using (var path = RoundedRect(rect, 9))
            using (var fill = new SolidBrush(PanelSoft))
            using (var pen = new Pen(Line))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            using (var tiny = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var rank = build.Rank > 0 ? build.Rank : index + 1;
                DrawText(g, MayhemUiCopy.BuildPlanPrefix + rank, tiny, index == 0 ? Gold : Silver, rect.X + 8, rect.Y + 5);
                var names = BuildPathText(build, 4);
                DrawText(g, FitText(g, names, body, rect.Width - 18), body, Text, rect.X + 8, rect.Y + 19);

                var iconX = rect.X + 8;
                foreach (var item in (build.Items ?? new List<MayhemBuildItem>()).Where(item => item != null).Take(5))
                {
                    DrawSquareImage(g, images, item.IconUrl, new Rectangle(iconX, rect.Y + 39, 23, 23), 4, FirstChar(item.Name), Line);
                    iconX += 27;
                }
            }
        }

        private static string BuildPathText(MayhemBuildPath build, int limit)
        {
            if (build == null || build.Items == null) return MayhemUiCopy.NoCoreBuild;
            var names = build.Items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Name.Trim())
                .Take(Math.Max(1, limit))
                .ToList();
            return names.Count == 0 ? MayhemUiCopy.NoCoreBuild : string.Join(MayhemUiCopy.CoreArrow, names);
        }

        private static void DrawBuildGroup(
            Graphics g,
            string label,
            IList<MayhemBuildItem> items,
            IDictionary<string, Bitmap> images,
            int x,
            int y,
            int width,
            int limit)
        {
            var values = (items ?? new List<MayhemBuildItem>()).Where(item => item != null).Take(limit).ToList();
            using (var tiny = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                DrawText(g, label, tiny, Muted, x, y);
                var names = values.Count == 0
                    ? MayhemUiCopy.NoValue
                    : string.Join(MayhemUiCopy.CoreArrow, values.Where(item => !string.IsNullOrWhiteSpace(item.Name)).Select(item => item.Name).Take(limit));
                DrawText(g, FitText(g, names, body, width), body, values.Count == 0 ? MutedDark : Text, x, y + 14);
                var iconX = x;
                foreach (var item in values)
                {
                    DrawSquareImage(g, images, item.IconUrl, new Rectangle(iconX, y + 34, 22, 22), 4, FirstChar(item.Name), Line);
                    iconX += 26;
                }
            }
        }

        private static void DrawSkillPriority(Graphics g, MayhemChampionResult result, IDictionary<string, Bitmap> images, int x, int y, int width)
        {
            var skills = GetSkillPriority(result);
            using (var tiny = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                DrawText(g, MayhemUiCopy.SkillPriority, tiny, Muted, x, y);
                if (skills.Count == 0)
                {
                    DrawText(g, MayhemUiCopy.NoValue, body, MutedDark, x, y + 14);
                    return;
                }

                var order = string.Join(MayhemUiCopy.CoreArrow, skills.Take(3).Select(skill => skill.Key));
                DrawText(g, FitText(g, order, body, width), body, Text, x, y + 14);
                var iconX = x;
                foreach (var skill in skills.Take(3))
                {
                    DrawSquareImage(g, images, skill.IconUrl, new Rectangle(iconX, y + 34, 22, 22), 4, skill.Key, Cyan);
                    iconX += 26;
                }
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
                    DrawText(g, MayhemUiCopy.NoAugmentRanking, font, Muted, (CardWidth - textSize.Width) / 2F, y + 27);
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
                    DrawText(g, group.Title, head, group.Accent, Outer + 14, y + 9);
                    var countText = group.Items.Count + MayhemUiCopy.ItemsSuffix;
                    var width = g.MeasureString(countText, tiny).Width;
                    DrawText(g, countText, tiny, Muted, CardWidth - Outer - 14 - width, y + 11);
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

            DrawSquareImage(g, images, row.IconUrl, new Rectangle(rect.X + 9, rect.Y + 17, 42, 42), 7, FirstChar(row.Name), accent);
            using (var nameFont = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var body = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var stat = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var textX = rect.X + 59;
                var nameWidth = rect.Width - 174;
                var priority = MayhemUiCopy.PriorityPrefix + Math.Max(1, row.Rank);
                DrawText(g, priority, body, accent, textX, rect.Y + 6);
                DrawText(g, FitText(g, row.Name, nameFont, nameWidth), nameFont, Text, textX, rect.Y + 20);
                var description = FirstNonEmpty(row.Description, MayhemUiCopy.NoDescription);
                DrawText(g, FitText(g, description, body, nameWidth), body, Muted, textX, rect.Y + 39);
                var sampleText = row.Games.HasValue && row.Games.Value > 0
                    ? MayhemUiCopy.Sample + " " + FormatGames(row.Games.Value)
                    : MayhemUiCopy.RankPrefix + Math.Max(1, row.Rank);
                DrawText(g, FitText(g, sampleText, body, nameWidth), body, MutedDark, textX, rect.Y + 56);

                var statRight = rect.Right - 10;
                var winText = FormatPercent(row.WinRate);
                var pickText = FormatPercent(row.PickRate);
                var winWidth = g.MeasureString(winText, stat).Width;
                var pickWidth = g.MeasureString(pickText, small).Width;
                DrawText(g, MayhemUiCopy.Win.Trim(), body, Muted, statRight - 66, rect.Y + 18);
                DrawText(g, winText, stat, Green, statRight - winWidth, rect.Y + 17);
                DrawText(g, MayhemUiCopy.Popularity, body, Muted, statRight - 66, rect.Y + 43);
                DrawText(g, pickText, small, accent, statRight - pickWidth, rect.Y + 43);
            }
        }

        private static void DrawRoutes(Graphics g, IList<MayhemDecisionRoute> routes, int y)
        {
            var rect = new Rectangle(Outer, y, CardWidth - Outer * 2, RouteHeight);
            DrawPanel(g, rect, 12);
            using (var head = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var tiny = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                DrawText(g, MayhemUiCopy.ChoiceDirection, head, Text, Outer + 14, y + 9);
                var noteWidth = g.MeasureString(MayhemUiCopy.SingleAugmentNote, tiny).Width;
                DrawText(g, MayhemUiCopy.SingleAugmentNote, tiny, MutedDark, CardWidth - Outer - 14 - noteWidth, y + 12);
            }

            var innerWidth = CardWidth - Outer * 2 - 28;
            var routeGap = 8;
            var routeWidth = (innerWidth - routeGap * 2) / 3;
            var accents = new[] { Cyan, Green, Gold };
            for (var index = 0; index < routes.Count && index < 3; index++)
            {
                var route = routes[index];
                var x = Outer + 14 + index * (routeWidth + routeGap);
                var routeRect = new Rectangle(x, y + 30, routeWidth, 55);
                using (var path = RoundedRect(routeRect, 9))
                using (var fill = new SolidBrush(PanelStrong))
                using (var pen = new Pen(Line))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
                using (var tiny = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var body = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    DrawText(g, route.Title, tiny, accents[index], x + 9, y + 36);
                    DrawText(g, FitText(g, route.AugmentName, body, routeWidth - 18), body, Text, x + 9, y + 52);
                    DrawText(g, FitText(g, route.Hint, tiny, routeWidth - 18), tiny, Muted, x + 9, y + 70);
                }
            }
        }

        private static void DrawFooter(Graphics g, int height)
        {
            var y = height - FooterHeight - Outer;
            using (var pen = new Pen(Line)) g.DrawLine(pen, Outer, y, CardWidth - Outer, y);
            using (var product = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var disclaimer = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                DrawText(g, MayhemUiCopy.FooterProduct, product, MutedDark, Outer, y + 11);
                var width = g.MeasureString(MayhemUiCopy.FooterDisclaimer, disclaimer).Width;
                DrawText(g, MayhemUiCopy.FooterDisclaimer, disclaimer, MutedDark, CardWidth - Outer - width, y + 11);
            }
        }

        private static void DrawText(Graphics g, string value, Font font, Color color, float x, float y)
        {
            using (var brush = new SolidBrush(color))
                g.DrawString(value ?? string.Empty, font, brush, x, y);
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
                            DrawText(g, text, font, Muted, rect.X + (rect.Width - size.Width) / 2F, rect.Y + (rect.Height - size.Height) / 2F);
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
            var prism = new AugmentGroup { Title = MayhemUiCopy.PrismSection, Accent = Purple };
            var gold = new AugmentGroup { Title = MayhemUiCopy.GoldSection, Accent = Gold };
            var silver = new AugmentGroup { Title = MayhemUiCopy.SilverSection, Accent = Silver };
            var other = new AugmentGroup { Title = MayhemUiCopy.OtherSection, Accent = Cyan };
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
            TakeRoute(routes, used, candidates.OrderByDescending(row => (row.WinRate ?? 0D) * 0.72D + (row.PickRate ?? 0D) * 0.28D), MayhemUiCopy.StableRoute, MayhemUiCopy.StableShortHint);
            TakeRoute(routes, used, candidates.OrderByDescending(row => row.WinRate ?? -1D), MayhemUiCopy.HighWinRoute, MayhemUiCopy.HighWinShortHint);
            TakeRoute(routes, used, candidates.OrderByDescending(row => row.PickRate ?? -1D), MayhemUiCopy.PopularRoute, MayhemUiCopy.PopularShortHint);
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
            if (result == null) return;
            if (result.CoreBuilds == null) result.CoreBuilds = new List<MayhemBuildPath>();
            if (result.CoreItems == null) result.CoreItems = new List<string>();
            if (result.CoreItemIconUrls == null) result.CoreItemIconUrls = new List<string>();
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
            var patchText = string.IsNullOrWhiteSpace(patch) ? MayhemUiCopy.NoValue : MayhemUiCopy.PatchPrefix + patch;
            var state = verified || string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)
                ? MayhemUiCopy.Verified
                : MayhemUiCopy.Syncing;
            return patchText + MayhemUiCopy.SeparatorDot + state;
        }

        private static string BuildStatus(MayhemChampionResult result)
        {
            var has = (result.CoreBuilds != null && result.CoreBuilds.Count > 0) ||
                      (result.StarterItems != null && result.StarterItems.Count > 0) ||
                      (result.BootItems != null && result.BootItems.Count > 0) ||
                      (result.SummonerSpells != null && result.SummonerSpells.Count > 0);
            if (!has) return MayhemUiCopy.BuildMissing;
            if (result.BuildSourceStale || string.Equals(result.BuildSourceRoute, "stale-cache", StringComparison.OrdinalIgnoreCase)) return MayhemUiCopy.BuildCache;
            return MayhemUiCopy.BuildLive;
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##") + "%" : MayhemUiCopy.NoValue;
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
            foreach (var pair in result.SkillIconUrls ?? new Dictionary<string, string>()) AddReference(values, pair.Value);
            return values.Distinct(StringComparer.OrdinalIgnoreCase).Take(28).ToList();
        }

        private static IEnumerable<string> CollectBuildReferences(MayhemChampionResult result)
        {
            var values = new List<string>();
            foreach (var build in (result.CoreBuilds ?? new List<MayhemBuildPath>()).Where(build => build != null).Take(2))
                foreach (var item in (build.Items ?? new List<MayhemBuildItem>()).Where(item => item != null).Take(5)) AddReference(values, item.IconUrl);
            foreach (var item in (result.StarterItems ?? new List<MayhemBuildItem>()).Where(item => item != null).Take(3)) AddReference(values, item.IconUrl);
            foreach (var item in (result.BootItems ?? new List<MayhemBuildItem>()).Where(item => item != null).Take(1)) AddReference(values, item.IconUrl);
            foreach (var item in (result.SummonerSpells ?? new List<MayhemBuildItem>()).Where(item => item != null).Take(2)) AddReference(values, item.IconUrl);
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
