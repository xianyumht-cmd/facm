using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.League;
using FACM.Services;

namespace FACM.Mayhem
{
    /// <summary>
    /// Lightweight 3.5 adapter for the useful FACM 4 automatic ChampSelect guide behavior.
    /// It reuses the existing Mayhem data pipeline and the one existing League client session;
    /// it does not introduce FACM.App/Core/WinUI runtime dependencies or any write operation.
    /// </summary>
    internal sealed class MayhemAutomaticGuideService
    {
        private const string ChampionDetailPathPrefix = "/lol-game-data/assets/v1/champions/";
        private readonly ILeagueClientApi _leagueClient;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };

        public MayhemAutomaticGuideService(ILeagueClientApi leagueClient)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
        }

        public async Task<MayhemChampionResult> QueryForChampionIdAsync(int championId, CancellationToken token)
        {
            if (championId <= 0)
                return new MayhemChampionResult { ErrorMessage = "客户端暂未提供当前英雄。" };

            var query = await ResolveChampionQueryAsync(championId, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(query))
                return new MayhemChampionResult { ErrorMessage = "客户端暂未提供当前英雄名称。" };

            var result = await OpggMayhemService.QueryAsync(query, token).ConfigureAwait(false);
            if (result == null) return new MayhemChampionResult { Query = query, ErrorMessage = "自动攻略暂时没有结果。" };
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage)) return result;

            await RiotGameDataService.EnrichAsync(result, _leagueClient, token).ConfigureAwait(false);
            await MayhemRankedAugmentService.EnrichAsync(result, token).ConfigureAwait(false);
            await MayhemDecisionLocalizationService.EnrichAsync(result, _leagueClient, token).ConfigureAwait(false);
            Sanitize(result);
            return result;
        }

        internal async Task<string> ResolveChampionQueryAsync(int championId, CancellationToken token)
        {
            if (championId <= 0) return string.Empty;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    var bytes = await _leagueClient.TryGetBytesAsync(
                        ChampionDetailPathPrefix + championId + ".json",
                        timeout.Token).ConfigureAwait(false);
                    return ParseChampionQuery(bytes);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    return string.Empty;
                }
                catch (Exception exception)
                {
                    AppLog.Info("Automatic Mayhem champion identity lookup skipped: " + exception.Message);
                    return string.Empty;
                }
            }
        }

        internal string ParseChampionQuery(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            try
            {
                var data = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                if (data == null) return string.Empty;
                var alias = ReadString(data, "alias");
                var name = ReadString(data, "name");
                return FirstUsable(alias, name);
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static void Sanitize(MayhemChampionResult result)
        {
            if (result == null) return;
            if (LooksTechnical(result.BalanceSummary)) result.BalanceSummary = null;
            if (LooksTechnical(result.SkillOrder)) result.SkillOrder = null;
            result.CoreItems = (result.CoreItems ?? new List<string>())
                .Where(value => !LooksTechnical(value))
                .Take(8)
                .ToList();
            result.Augments = (result.Augments ?? new List<string>())
                .Where(value => !LooksTechnical(value))
                .Take(8)
                .ToList();
            result.AugmentRows = (result.AugmentRows ?? new List<MayhemAugmentRow>())
                .Where(value => value != null && !LooksTechnical(value.Name))
                .Take(40)
                .ToList();
            while (result.CoreItemIconUrls.Count > result.CoreItems.Count)
                result.CoreItemIconUrls.RemoveAt(result.CoreItemIconUrls.Count - 1);
        }

        internal static void ValidateForSmokeTest()
        {
            var service = new MayhemAutomaticGuideService(new SmokeLeagueClient());
            var bytes = Encoding.UTF8.GetBytes("{\"id\":147,\"name\":\"萨勒芬妮\",\"alias\":\"Seraphine\"}");
            var query = service.ParseChampionQuery(bytes);
            if (!string.Equals(query, "Seraphine", StringComparison.Ordinal))
                throw new InvalidOperationException("Automatic Mayhem guide did not prefer a stable champion alias.");
            if (service.ParseChampionQuery(Encoding.UTF8.GetBytes("{}")) != string.Empty)
                throw new InvalidOperationException("Automatic Mayhem guide accepted an empty champion identity.");

            var model = new MayhemChampionResult
            {
                CoreItems = new List<string> { "A", "B" },
                Augments = new List<string> { "X", "Y" },
                AugmentRows = new List<MayhemAugmentRow>
                {
                    new MayhemAugmentRow { Name = "棱彩强化", Rank = 1, Rarity = "棱彩" }
                }
            };
            Sanitize(model);
            if (model.AugmentRows.Count != 1 || model.CoreItems.Count != 2)
                throw new InvalidOperationException("Automatic Mayhem guide sanitization dropped valid guide data.");
        }

        private static string FirstUsable(params string[] values)
        {
            if (values == null) return string.Empty;
            foreach (var value in values)
            {
                var candidate = (value ?? string.Empty).Trim();
                if (candidate.Length == 0 ||
                    candidate.Equals("英雄", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Equals("Unknown champion", StringComparison.OrdinalIgnoreCase)) continue;
                return candidate;
            }
            return string.Empty;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : string.Empty;
        }

        private static bool LooksTechnical(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.ToLowerInvariant();
            return text.Contains(MayhemUiCopy.TriggerDataSource) ||
                   text.Contains(MayhemUiCopy.TriggerUnparsed) ||
                   text.Contains(MayhemUiCopy.TriggerOpggPage) ||
                   text.Contains(MayhemUiCopy.TriggerPageNoData);
        }

        private sealed class SmokeLeagueClient : ILeagueClientApi
        {
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                return Task.FromResult<byte[]>(null);
            }
        }
    }
}
