using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Pets
{
    internal static class AnimalPetArtService
    {
        private static readonly HttpClient Client = CreateClient();
        private static readonly string CacheDirectory = Path.Combine(RuntimePaths.RuntimeDirectory, "animal-art");

        public static async Task<Bitmap> LoadAsync(AnimalPetDefinition pet, CancellationToken token)
        {
            if (pet == null || string.IsNullOrWhiteSpace(pet.ArtworkUrl) || string.IsNullOrWhiteSpace(pet.ArtworkFileName))
                return null;

            RuntimePaths.Initialize();
            Directory.CreateDirectory(CacheDirectory);
            var path = Path.Combine(CacheDirectory, pet.ArtworkFileName);

            var cached = TryLoad(path);
            if (cached != null) return cached;

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, pet.ArtworkUrl))
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (bytes == null || bytes.Length < 512) return null;

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
                AppLog.Info("Animal artwork download skipped: " + pet.Id + "; " + exception.Message);
                return null;
            }

            return TryLoad(path);
        }

        internal static string CachePathForSmokeTest(AnimalPetDefinition pet)
        {
            return pet == null ? string.Empty : Path.Combine(CacheDirectory, pet.ArtworkFileName ?? string.Empty);
        }

        private static Bitmap TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var source = Image.FromStream(stream))
                    return new Bitmap(source);
            }
            catch
            {
                try { File.Delete(path); } catch { }
                return null;
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FACM/3.1");
            return client;
        }
    }
}
