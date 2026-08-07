using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Pets
{
    internal static class SpritePetAssetService
    {
        private static readonly HttpClient Client = CreateClient();
        private static readonly string CacheDirectory = Path.Combine(RuntimePaths.RuntimeDirectory, "animal-sprites");

        public static async Task<Bitmap> LoadAsync(AnimalPetDefinition pet, CancellationToken token)
        {
            if (pet == null || string.IsNullOrWhiteSpace(pet.SpriteUrl) || string.IsNullOrWhiteSpace(pet.SpriteFileName))
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
