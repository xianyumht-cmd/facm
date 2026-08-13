using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Pets
{
    internal static class SpritePetAssetService
    {
        internal const string BuiltInGreenFlyUrl = "builtin://facm/greenfly-hq-v1";
        internal const int BuiltInGreenFlyFrameSize = 96;
        internal const int BuiltInGreenFlyFrameCount = 4;

        internal const string BuiltInRealBeeUrl = "builtin://facm/real-bee-gate1-v1";
        internal const int BuiltInRealBeeFrameSize = 128;
        internal const int BuiltInRealBeeFrameCount = 4;
        private const string BuiltInRealBeeResourceName = "FACM.Resources.RealBeeGate1.png";

        private static readonly HttpClient Client = CreateClient();
        private static readonly string CacheDirectory = Path.Combine(RuntimePaths.RuntimeDirectory, "animal-sprites");

        public static async Task<Bitmap> LoadAsync(AnimalPetDefinition pet, CancellationToken token)
        {
            if (pet == null) return null;

            if (string.Equals(pet.SpriteUrl, BuiltInGreenFlyUrl, StringComparison.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                return CreateBuiltInGreenFlySheet();
            }

            if (string.Equals(pet.SpriteUrl, BuiltInRealBeeUrl, StringComparison.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                return LoadBuiltInRealBeeSheet();
            }

            if (BuiltInFlyingPetArtService.IsBuiltIn(pet.SpriteUrl))
            {
                token.ThrowIfCancellationRequested();
                return BuiltInFlyingPetArtService.TryCreate(pet.SpriteUrl);
            }

            if (string.IsNullOrWhiteSpace(pet.SpriteUrl) || string.IsNullOrWhiteSpace(pet.SpriteFileName))
                return null;

            RuntimePaths.Initialize();
            Directory.CreateDirectory(CacheDirectory);
            var path = Path.Combine(CacheDirectory, SanitizeFileName(pet.SpriteFileName));

            var cached = TryLoadAndValidate(path, pet);
            if (cached != null) return cached;

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, pet.SpriteUrl))
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (bytes == null || bytes.Length < 200)
                        throw new InvalidDataException("Sprite download returned too little data.");

                    var temporary = path + ".tmp";
                    File.WriteAllBytes(temporary, bytes);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Info("Animal sprite download failed: " + pet.Id + "; " + exception.Message);
                return null;
            }

            return TryLoadAndValidate(path, pet);
        }

        internal static string CachePathForSmokeTest(AnimalPetDefinition pet)
        {
            return pet == null ? string.Empty : Path.Combine(CacheDirectory, SanitizeFileName(pet.SpriteFileName ?? string.Empty));
        }

        internal static Rectangle GetFrameRectangle(AnimalPetDefinition pet, Bitmap sheet, int frameIndex, int directionRow)
        {
            if (pet == null || sheet == null) return Rectangle.Empty;
            var columns = Math.Max(1, pet.SpriteColumns);
            var rows = Math.Max(1, pet.SpriteRows);
            var cellWidth = sheet.Width / columns;
            var cellHeight = sheet.Height / rows;
            if (cellWidth <= 0 || cellHeight <= 0) return Rectangle.Empty;

            var frameCount = Math.Max(1, Math.Min(pet.FrameCount, columns));
            frameIndex %= frameCount;
            if (frameIndex < 0) frameIndex += frameCount;

            var row = pet.DirectionalRows ? directionRow : pet.AnimationRow;
            row = Math.Max(0, Math.Min(rows - 1, row));
            return new Rectangle(frameIndex * cellWidth, row * cellHeight, cellWidth, cellHeight);
        }

        internal static Bitmap CreateBuiltInGreenFlySheetForSmokeTest()
        {
            return CreateBuiltInGreenFlySheet();
        }

        internal static Bitmap CreateBuiltInRealBeeSheetForSmokeTest()
        {
            return LoadBuiltInRealBeeSheet();
        }

        private static Bitmap LoadBuiltInRealBeeSheet()
        {
            using (var stream = typeof(SpritePetAssetService).Assembly.GetManifestResourceStream(BuiltInRealBeeResourceName))
            {
                if (stream == null)
                    throw new InvalidDataException("Embedded Real Bee Gate 1 sprite resource is missing.");

                using (var source = Image.FromStream(stream, true, true))
                {
                    var expectedWidth = BuiltInRealBeeFrameSize * BuiltInRealBeeFrameCount;
                    if (source.Width != expectedWidth || source.Height != BuiltInRealBeeFrameSize)
                        throw new InvalidDataException("Embedded Real Bee Gate 1 sprite dimensions are invalid.");

                    var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(
                            source,
                            new Rectangle(0, 0, source.Width, source.Height),
                            0,
                            0,
                            source.Width,
                            source.Height,
                            GraphicsUnit.Pixel);
                    }

                    try
                    {
                        ValidateBuiltInRealBeeSheet(bitmap);
                        return bitmap;
                    }
                    catch
                    {
                        bitmap.Dispose();
                        throw;
                    }
                }
            }
        }

        private static void ValidateBuiltInRealBeeSheet(Bitmap sheet)
        {
            if (sheet == null)
                throw new InvalidDataException("Real Bee Gate 1 sprite sheet is null.");

            var expectedWidth = BuiltInRealBeeFrameSize * BuiltInRealBeeFrameCount;
            if (sheet.Width != expectedWidth || sheet.Height != BuiltInRealBeeFrameSize)
                throw new InvalidDataException("Real Bee Gate 1 sprite sheet dimensions changed unexpectedly.");

            var rectangle = new Rectangle(0, 0, sheet.Width, sheet.Height);
            var data = sheet.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var pixels = new byte[stride * sheet.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                double baselineX = 0d;
                double baselineY = 0d;
                for (var frame = 0; frame < BuiltInRealBeeFrameCount; frame++)
                {
                    var minX = BuiltInRealBeeFrameSize;
                    var minY = BuiltInRealBeeFrameSize;
                    var maxX = -1;
                    var maxY = -1;
                    var visiblePixels = 0;
                    long bodyWeight = 0L;
                    long bodyWeightedX = 0L;
                    long bodyWeightedY = 0L;
                    var bodyPixels = 0;

                    for (var y = 0; y < BuiltInRealBeeFrameSize; y++)
                    {
                        var row = data.Stride >= 0 ? y * stride : (sheet.Height - 1 - y) * stride;
                        for (var x = 0; x < BuiltInRealBeeFrameSize; x++)
                        {
                            var sheetX = frame * BuiltInRealBeeFrameSize + x;
                            var index = row + sheetX * 4;
                            var alpha = pixels[index + 3];

                            if (alpha > 10)
                            {
                                visiblePixels++;
                                if (x < minX) minX = x;
                                if (y < minY) minY = y;
                                if (x > maxX) maxX = x;
                                if (y > maxY) maxY = y;
                            }

                            // Gate 1 uses a fixed central body band rather than colour heuristics. That makes
                            // the anchor check independent of straight-vs-premultiplied RGB conversion while
                            // excluding the high-frequency wing envelope above the body.
                            if (alpha > 80 && x >= 30 && x <= 100 && y >= 50 && y <= 82)
                            {
                                bodyWeight += alpha;
                                bodyWeightedX += (long)x * alpha;
                                bodyWeightedY += (long)y * alpha;
                                bodyPixels++;
                            }
                        }
                    }

                    if (visiblePixels < 1200 || bodyPixels < 900 || bodyWeight <= 0L || maxX < minX || maxY < minY)
                        throw new InvalidDataException("Real Bee Gate 1 frame is empty or lost its stable body band: frame=" + frame + ".");

                    const int minimumRotationMargin = 12;
                    if (minX < minimumRotationMargin || minY < minimumRotationMargin ||
                        BuiltInRealBeeFrameSize - 1 - maxX < minimumRotationMargin ||
                        BuiltInRealBeeFrameSize - 1 - maxY < minimumRotationMargin)
                    {
                        throw new InvalidDataException("Real Bee Gate 1 frame no longer has safe 360-degree rotation margins: frame=" + frame + ".");
                    }

                    var anchorX = bodyWeightedX / (double)bodyWeight;
                    var anchorY = bodyWeightedY / (double)bodyWeight;
                    if (frame == 0)
                    {
                        baselineX = anchorX;
                        baselineY = anchorY;
                    }
                    else
                    {
                        var dx = anchorX - baselineX;
                        var dy = anchorY - baselineY;
                        if (Math.Sqrt(dx * dx + dy * dy) > 2d)
                        {
                            throw new InvalidDataException(
                                "Real Bee Gate 1 body anchor drifted between wing frames: frame=" + frame +
                                "; dx=" + dx.ToString("0.00") + "; dy=" + dy.ToString("0.00") + ".");
                        }
                    }
                }
            }
            finally
            {
                sheet.UnlockBits(data);
            }
        }

        private static Bitmap CreateBuiltInGreenFlySheet()
        {
            var sheet = new Bitmap(
                BuiltInGreenFlyFrameSize * BuiltInGreenFlyFrameCount,
                BuiltInGreenFlyFrameSize,
                PixelFormat.Format32bppPArgb);

            using (var graphics = Graphics.FromImage(sheet))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

                for (var frame = 0; frame < BuiltInGreenFlyFrameCount; frame++)
                {
                    var state = graphics.Save();
                    graphics.TranslateTransform(frame * BuiltInGreenFlyFrameSize, 0f);
                    DrawGreenFlyFrame(graphics, frame);
                    graphics.Restore(state);
                }
            }

            return sheet;
        }

        private static void DrawGreenFlyFrame(Graphics graphics, int frame)
        {
            // The body anchor is intentionally identical in every frame. Only the wings move, which
            // keeps the fly visually stable while the existing FACM movement engine owns all desktop motion.
            var upperTip = new[]
            {
                new PointF(24f, 10f),
                new PointF(17f, 25f),
                new PointF(24f, 38f),
                new PointF(19f, 20f)
            }[frame % BuiltInGreenFlyFrameCount];
            var lowerTip = new[]
            {
                new PointF(24f, 86f),
                new PointF(17f, 71f),
                new PointF(24f, 58f),
                new PointF(19f, 76f)
            }[frame % BuiltInGreenFlyFrameCount];

            using (var legPen = new Pen(Color.FromArgb(215, 27, 32, 25), 2.25f))
            {
                legPen.StartCap = LineCap.Round;
                legPen.EndCap = LineCap.Round;
                DrawLeg(graphics, legPen, new PointF(53f, 39f), new PointF(37f, 24f), new PointF(23f, 19f));
                DrawLeg(graphics, legPen, new PointF(50f, 46f), new PointF(31f, 42f), new PointF(17f, 35f));
                DrawLeg(graphics, legPen, new PointF(49f, 54f), new PointF(30f, 62f), new PointF(18f, 72f));
                DrawLeg(graphics, legPen, new PointF(58f, 39f), new PointF(47f, 20f), new PointF(39f, 13f));
                DrawLeg(graphics, legPen, new PointF(58f, 56f), new PointF(47f, 75f), new PointF(39f, 83f));
                DrawLeg(graphics, legPen, new PointF(67f, 49f), new PointF(78f, 63f), new PointF(84f, 72f));
            }

            DrawWing(graphics, new PointF(55f, 42f), upperTip, true);
            DrawWing(graphics, new PointF(55f, 54f), lowerTip, false);

            using (var abdomenBrush = new LinearGradientBrush(
                new RectangleF(28f, 37f, 34f, 22f),
                Color.FromArgb(255, 89, 111, 34),
                Color.FromArgb(255, 37, 53, 24),
                LinearGradientMode.Horizontal))
            using (var abdomenPen = new Pen(Color.FromArgb(235, 24, 31, 20), 1.6f))
            {
                graphics.FillEllipse(abdomenBrush, 28f, 37f, 34f, 22f);
                graphics.DrawEllipse(abdomenPen, 28f, 37f, 34f, 22f);
            }

            using (var thoraxBrush = new LinearGradientBrush(
                new RectangleF(49f, 34f, 25f, 28f),
                Color.FromArgb(255, 72, 88, 37),
                Color.FromArgb(255, 29, 38, 24),
                LinearGradientMode.Vertical))
            using (var thoraxPen = new Pen(Color.FromArgb(240, 22, 27, 20), 1.7f))
            {
                graphics.FillEllipse(thoraxBrush, 49f, 34f, 25f, 28f);
                graphics.DrawEllipse(thoraxPen, 49f, 34f, 25f, 28f);
            }

            using (var stripePen = new Pen(Color.FromArgb(130, 18, 25, 16), 1.4f))
            {
                graphics.DrawArc(stripePen, 32f, 39f, 22f, 18f, 80f, 200f);
                graphics.DrawArc(stripePen, 38f, 39f, 17f, 18f, 80f, 200f);
            }

            using (var headBrush = new SolidBrush(Color.FromArgb(255, 61, 68, 42)))
            using (var headPen = new Pen(Color.FromArgb(240, 26, 31, 24), 1.6f))
            {
                graphics.FillEllipse(headBrush, 67f, 38f, 18f, 20f);
                graphics.DrawEllipse(headPen, 67f, 38f, 18f, 20f);
            }

            using (var eyeBrush = new SolidBrush(Color.FromArgb(255, 121, 45, 31)))
            using (var eyeHighlight = new SolidBrush(Color.FromArgb(145, 235, 166, 115)))
            {
                graphics.FillEllipse(eyeBrush, 73f, 39.5f, 9f, 8.5f);
                graphics.FillEllipse(eyeBrush, 73f, 48f, 9f, 8.5f);
                graphics.FillEllipse(eyeHighlight, 77f, 41f, 2.1f, 1.7f);
                graphics.FillEllipse(eyeHighlight, 77f, 51f, 2.1f, 1.7f);
            }

            using (var antennaPen = new Pen(Color.FromArgb(220, 29, 34, 25), 1.4f))
            {
                antennaPen.StartCap = LineCap.Round;
                antennaPen.EndCap = LineCap.Round;
                graphics.DrawLine(antennaPen, 82f, 43f, 91f, 37f);
                graphics.DrawLine(antennaPen, 82f, 53f, 91f, 59f);
            }
        }

        private static void DrawWing(Graphics graphics, PointF root, PointF tip, bool upper)
        {
            var vertical = upper ? -1f : 1f;
            using (var path = new GraphicsPath())
            {
                path.StartFigure();
                path.AddBezier(
                    root,
                    new PointF(root.X - 8f, root.Y + 2f * vertical),
                    new PointF(tip.X + 11f, tip.Y - 7f * vertical),
                    tip);
                path.AddBezier(
                    tip,
                    new PointF(tip.X + 18f, tip.Y + 5f * vertical),
                    new PointF(root.X - 1f, root.Y + 13f * vertical),
                    root);
                path.CloseFigure();

                using (var wingBrush = new SolidBrush(Color.FromArgb(112, 202, 225, 228)))
                using (var wingPen = new Pen(Color.FromArgb(145, 74, 94, 91), 1.15f))
                {
                    graphics.FillPath(wingBrush, path);
                    graphics.DrawPath(wingPen, path);
                }
            }

            using (var veinPen = new Pen(Color.FromArgb(78, 72, 91, 88), 0.95f))
            {
                graphics.DrawLine(veinPen, root, tip);
                var mid = new PointF((root.X + tip.X) * 0.5f, (root.Y + tip.Y) * 0.5f);
                graphics.DrawLine(
                    veinPen,
                    new PointF(root.X - 1f, root.Y + (upper ? -4f : 4f)),
                    new PointF(mid.X + 5f, mid.Y + (upper ? 2f : -2f)));
            }
        }

        private static void DrawLeg(Graphics graphics, Pen pen, PointF root, PointF knee, PointF foot)
        {
            graphics.DrawLine(pen, root, knee);
            graphics.DrawLine(pen, knee, foot);
        }

        private static Bitmap TryLoadAndValidate(string path, AnimalPetDefinition pet)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var source = Image.FromStream(stream))
                {
                    var bitmap = new Bitmap(source);
                    var columns = Math.Max(1, pet.SpriteColumns);
                    var rows = Math.Max(1, pet.SpriteRows);
                    if (bitmap.Width < columns || bitmap.Height < rows || bitmap.Width % columns != 0 || bitmap.Height % rows != 0)
                    {
                        bitmap.Dispose();
                        throw new InvalidDataException("Sprite sheet grid does not match the configured rows/columns.");
                    }
                    return bitmap;
                }
            }
            catch (Exception exception)
            {
                AppLog.Info("Animal sprite cache rejected: " + pet.Id + "; " + exception.Message);
                try { File.Delete(path); } catch { }
                return null;
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var character in Path.GetInvalidFileNameChars())
                name = name.Replace(character, '_');
            return name;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FACM/3.1 (+https://github.com/xianyumht-cmd/facm)");
            return client;
        }
    }
}
