using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueRespawnTimerSample
    {
        public bool IsDead { get; set; }
        public double TimeLeftSeconds { get; set; }
        public double TotalTimeSeconds { get; set; }
    }

    internal static class LeagueRespawnTimerProjection
    {
        internal static string ReadCurrentAccount(byte[] currentSummoner)
        {
            if (currentSummoner == null || currentSummoner.Length == 0 || currentSummoner.Length > 65536)
                return null;
            try
            {
                var data = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 12 }
                    .DeserializeObject(Encoding.UTF8.GetString(currentSummoner)) as Dictionary<string, object>;
                if (data == null) return null;
                var gameName = ReadString(data, "gameName");
                var tagLine = ReadString(data, "tagLine");
                if (!string.IsNullOrWhiteSpace(gameName) && !string.IsNullOrWhiteSpace(tagLine))
                    return gameName.Trim() + "#" + tagLine.Trim();
                var displayName = ReadString(data, "displayName");
                return string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static LeagueRespawnTimerSample Project(byte[] playerList, string accountName, LeagueRespawnTimerSample previous)
        {
            if (playerList == null || playerList.Length == 0 || playerList.Length > 131072 ||
                string.IsNullOrWhiteSpace(accountName)) return null;

            object parsed;
            try
            {
                parsed = new JavaScriptSerializer { MaxJsonLength = 131072, RecursionLimit = 20 }
                    .DeserializeObject(Encoding.UTF8.GetString(playerList));
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }

            var players = parsed as object[];
            if (players == null) return null;
            foreach (var entry in players)
            {
                var player = entry as Dictionary<string, object>;
                if (player == null) continue;
                var riotId = ReadString(player, "riotId");
                var summonerName = ReadString(player, "summonerName");
                if (!SameName(riotId, accountName) &&
                    !(string.IsNullOrEmpty(riotId) && SameName(summonerName, accountName))) continue;

                object deadValue;
                object timerValue;
                if (!player.TryGetValue("isDead", out deadValue) || !(deadValue is bool) ||
                    !player.TryGetValue("respawnTimer", out timerValue)) return null;
                double seconds;
                if (!double.TryParse(Convert.ToString(timerValue, CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) ||
                    double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 3600) return null;

                var dead = (bool)deadValue;
                if (!dead) return new LeagueRespawnTimerSample();
                var total = previous != null && previous.IsDead
                    ? Math.Max(previous.TotalTimeSeconds, seconds) : seconds;
                return new LeagueRespawnTimerSample
                {
                    IsDead = true,
                    TimeLeftSeconds = seconds,
                    TotalTimeSeconds = total
                };
            }
            return null;
        }

        private static string ReadString(Dictionary<string, object> value, string name)
        {
            object result;
            return value.TryGetValue(name, out result) ? result as string : null;
        }

        private static bool SameName(string candidate, string expected)
        {
            return !string.IsNullOrWhiteSpace(candidate) &&
                string.Equals(candidate.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Read-only loopback Live Client Data endpoint; no LCU credentials or global TLS changes.</summary>
    internal sealed class LeagueRespawnTimerReader : IDisposable
    {
        internal static readonly Uri PlayerListUri = new Uri("https://127.0.0.1:2999/liveclientdata/playerlist");
        private readonly HttpClient _client;

        internal LeagueRespawnTimerReader()
        {
            var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                request != null && request.RequestUri == PlayerListUri;
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 131072 };
        }

        internal async Task<byte[]> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var response = await _client.GetAsync(PlayerListUri, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 131072) return null;
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var memory = new System.IO.MemoryStream())
                    {
                        var buffer = new byte[4096];
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            if (memory.Length + read > 131072) return null;
                            memory.Write(buffer, 0, read);
                        }
                        return memory.ToArray();
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
            catch (HttpRequestException) { return null; }
            catch (System.IO.IOException) { return null; }
            catch (ObjectDisposedException) { return null; }
        }

        public void Dispose() { _client.Dispose(); }
    }
}
