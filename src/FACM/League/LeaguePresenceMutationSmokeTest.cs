using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeaguePresenceMutationSmokeTest
    {
        internal static void Validate()
        {
            RunAsync().GetAwaiter().GetResult();
        }

        private static async Task RunAsync()
        {
            var api = new ContendedPresence();
            var signature = new LeaguePresenceService(api, api, TimeSpan.Zero, TimeSpan.Zero);
            var rank = new LeaguePresenceService(api, api, TimeSpan.Zero, TimeSpan.Zero);
            var manual = new LeaguePresenceService(api, api, TimeSpan.Zero, TimeSpan.Zero);
            var first = signature.ApplyStatusMessageAsync("saved", CancellationToken.None);
            if (await Task.WhenAny(api.FirstWriteStarted, Task.Delay(3000)).ConfigureAwait(false) != api.FirstWriteStarted)
                throw new InvalidOperationException("First presence writer did not reach the controlled write point.");

            var second = rank.ApplyRankedStatusAsync("RANKED_SOLO_5x5", "DIAMOND", "I", CancellationToken.None);
            var third = manual.ApplyAsync(LeaguePresenceMode.Away, CancellationToken.None);
            try
            {
                if (api.Reads != 1)
                    throw new InvalidOperationException("Another presence writer read a stale document before the first PUT finished.");
            }
            finally { api.ReleaseFirst(); }

            var combined = Task.WhenAll(first, second, third);
            if (await Task.WhenAny(combined, Task.Delay(3000)).ConfigureAwait(false) != combined)
                throw new InvalidOperationException("Concurrent presence writes did not settle in time.");
            var firstResult = await first.ConfigureAwait(false);
            var secondResult = await second.ConfigureAwait(false);
            var thirdResult = await third.ConfigureAwait(false);
            if (firstResult.Status != "success" || secondResult.Status != "success" ||
                thirdResult.Status != "success" || api.StatusMessage != "saved" ||
                api.Tier != "DIAMOND" || api.Division != "I" ||
                api.Availability != "away" || api.Writes != 3)
                throw new InvalidOperationException("Overlapping signature, rank and manual presence writes failed: signature=" +
                    firstResult.Status + "; rank=" + secondResult.Status + "; manual=" + thirdResult.Status +
                    "; saved=" + (api.StatusMessage == "saved") + "; tier=" + api.Tier +
                    "; division=" + api.Division + "; availability=" + api.Availability + "; writes=" + api.Writes +
                    "; reads=" + api.Reads + ".");
        }

        private sealed class ContendedPresence : ILeagueClientApi, ILeaguePresenceWriteApi
        {
            private readonly object _sync = new object();
            private readonly TaskCompletionSource<bool> _started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private string _signature = "original";
            private string _tier = "GOLD";
            private string _division = "II";
            private string _availability = "chat";
            private int _reads;
            private int _writes;

            internal Task FirstWriteStarted { get { return _started.Task; } }
            internal int Reads { get { lock (_sync) return _reads; } }
            internal int Writes { get { lock (_sync) return _writes; } }
            internal string StatusMessage { get { lock (_sync) return _signature; } }
            internal string Tier { get { lock (_sync) return _tier; } }
            internal string Division { get { lock (_sync) return _division; } }
            internal string Availability { get { lock (_sync) return _availability; } }

            internal void ReleaseFirst() { _release.TrySetResult(true); }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (path != LeaguePresenceService.PresencePath)
                    throw new InvalidOperationException("Unexpected presence read target.");
                lock (_sync)
                {
                    _reads++;
                    var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                    {
                        { "availability", _availability },
                        { "statusMessage", _signature },
                        { "lol", new Dictionary<string, object>
                            {
                                { "gameStatus", "outOfGame" },
                                { "rankedLeagueQueue", "RANKED_SOLO_5x5" },
                                { "rankedLeagueTier", _tier },
                                { "rankedLeagueDivision", _division }
                            }
                        }
                    });
                    return Task.FromResult(Encoding.UTF8.GetBytes(json));
                }
            }

            public async Task<LeagueClientWriteResponse> TrySetPresenceAsync(string json, CancellationToken cancellationToken)
            {
                bool first;
                lock (_sync) first = ++_writes == 1;
                if (first)
                {
                    _started.TrySetResult(true);
                    await _release.Task.ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                var lol = root == null ? null : root["lol"] as Dictionary<string, object>;
                if (lol == null) throw new InvalidOperationException("Invalid presence mutation payload.");
                lock (_sync)
                {
                    _signature = (string)root["statusMessage"];
                    _tier = (string)lol["rankedLeagueTier"];
                    _division = (string)lol["rankedLeagueDivision"];
                    _availability = (string)root["availability"];
                }
                return new LeagueClientWriteResponse { StatusCode = 204, Body = Array.Empty<byte>() };
            }
        }
    }
}
