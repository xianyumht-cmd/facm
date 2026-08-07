using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.Pets
{
    internal sealed class DesktopHomunculusClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public DesktopHomunculusClient()
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:3100/"),
                Timeout = TimeSpan.FromSeconds(8)
            };
        }

        public async Task<bool> IsReadyAsync(CancellationToken token)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, "personas"))
                using (var response = await _client.SendAsync(request, token).ConfigureAwait(false))
                    return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task ImportVrmAsync(string sourcePath, PetDefinition pet, CancellationToken token)
        {
            var body = new
            {
                sourcePath,
                assetId = pet.AssetId,
                assetType = "vrm",
                description = "FACM / " + pet.Name + " / " + pet.License
            };
            await SendJsonAsync(HttpMethod.Post, "assets/import", body, new[] { HttpStatusCode.OK }, token).ConfigureAwait(false);
        }

        public async Task ActivatePersonaAsync(PetDefinition pet, CancellationToken token)
        {
            foreach (var item in PetCatalog.All)
            {
                if (string.Equals(item.PersonaId, pet.PersonaId, StringComparison.OrdinalIgnoreCase)) continue;
                await SendIgnoringStatusAsync(HttpMethod.Post, "personas/" + Uri.EscapeDataString(item.PersonaId) + "/despawn", null, token).ConfigureAwait(false);
            }

            var createBody = new
            {
                id = pet.PersonaId,
                name = "FACM · " + pet.Name,
                vrmAssetId = pet.AssetId
            };
            await SendJsonAsync(
                HttpMethod.Post,
                "personas",
                createBody,
                new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
                token).ConfigureAwait(false);

            await SendJsonAsync(
                HttpMethod.Post,
                "personas/" + Uri.EscapeDataString(pet.PersonaId) + "/vrm",
                new { assetId = pet.AssetId },
                new[] { HttpStatusCode.OK },
                token).ConfigureAwait(false);

            await SendJsonAsync(
                HttpMethod.Post,
                "personas/" + Uri.EscapeDataString(pet.PersonaId) + "/spawn",
                null,
                new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
                token).ConfigureAwait(false);
        }

        public async Task SubscribeClicksAsync(string personaId, Action clicked, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "personas/" + Uri.EscapeDataString(personaId) + "/events"))
            {
                request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (token.Register(response.Dispose))
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        while (!token.IsCancellationRequested)
                        {
                            string line;
                            try
                            {
                                line = await reader.ReadLineAsync().ConfigureAwait(false);
                            }
                            catch (ObjectDisposedException)
                            {
                                break;
                            }
                            catch (IOException)
                            {
                                break;
                            }
                            if (line == null) break;
                            if (!line.StartsWith("event:", StringComparison.OrdinalIgnoreCase)) continue;
                            var eventName = line.Substring(6).Trim();
                            if (!string.Equals(eventName, "pointer-click", StringComparison.OrdinalIgnoreCase)) continue;
                            if (clicked != null) clicked();
                        }
                    }
                }
            }
        }

        private async Task SendJsonAsync(HttpMethod method, string path, object body, HttpStatusCode[] accepted, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                if (body != null)
                    request.Content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
                using (var response = await _client.SendAsync(request, token).ConfigureAwait(false))
                {
                    foreach (var status in accepted)
                    {
                        if (response.StatusCode == status) return;
                    }
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (text.Length > 500) text = text.Substring(0, 500);
                    throw new InvalidOperationException("Desktop Homunculus API " + (int)response.StatusCode + ": " + text);
                }
            }
        }

        private async Task SendIgnoringStatusAsync(HttpMethod method, string path, object body, CancellationToken token)
        {
            try
            {
                using (var request = new HttpRequestMessage(method, path))
                {
                    if (body != null)
                        request.Content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
                    using (await _client.SendAsync(request, token).ConfigureAwait(false)) { }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Despawning a persona that does not exist is expected.
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
