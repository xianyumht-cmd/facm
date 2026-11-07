using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Mayhem;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceMayhemAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private CancellationTokenSource _cancellation;
        private int _generation;

        internal UnifiedWorkspaceMayhemAdapter(Func<bool> canPublish, Action<string> publish)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        internal void Start(MayhemModule module, string query)
        {
            Cancel();
            query = (query ?? string.Empty).Trim();
            if (query.Length == 0 || query.Length > 80) { PostStatus("invalid"); return; }
            if (module == null) { PostStatus("unavailable"); return; }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(13));
            _cancellation = source;
            ReadAsync(module, query, module.UnifiedLookupSession, source, _generation);
        }

        private async void ReadAsync(MayhemModule module, string query, LeagueClientSession session,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var result = await module.ReadUnifiedGuideAsync(query, source.Token);
                if (!Current(module, session, source, generation))
                {
                    if (Current(module, null, source, generation, false)) PostStatus("context-changed");
                    return;
                }
                if (result == null || !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    PostStatus("no-data");
                    return;
                }
                var rows = (result.AugmentRows ?? new List<MayhemAugmentRow>())
                    .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name)).Take(8).ToArray();
                _publish(SerializeGuide(result, rows));
                await PublishIconsAsync(module, result, rows, session, source, generation);
            }
            catch (OperationCanceledException)
            {
                // A deadline may cancel its token without changing the active generation.
                if (Owned(source, generation)) PostStatus("timeout");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified Mayhem query skipped: " + exception.GetType().Name);
                if (Current(module, null, source, generation, false)) PostStatus("unavailable");
            }
            finally
            {
                if (ReferenceEquals(_cancellation, source)) _cancellation = null;
                source.Dispose();
            }
        }

        private static string SerializeGuide(MayhemChampionResult result, MayhemAugmentRow[] rows)
        {
            return new JavaScriptSerializer().Serialize(new
            {
                type = "mayhem-details",
                champion = result.ChampionName,
                patch = result.Patch,
                rankingPatch = result.RankingPatch,
                source = result.SourceNote,
                rank = result.Rank,
                tier = result.Tier,
                winRate = result.WinRate,
                pickRate = result.PickRate,
                balance = result.BalanceSummary,
                skills = result.SkillOrder,
                items = result.CoreItems == null ? new string[0] : result.CoreItems.Take(5).ToArray(),
                augments = result.Augments == null ? new string[0] : result.Augments.Take(8).ToArray(),
                augmentSource = result.AugmentSourceRoute,
                augmentStale = result.AugmentSourceStale,
                augmentDetails = rows.Select(row => new
                {
                    name = row.Name, rank = row.Rank,
                    rarity = row.Rarity, rarityKind = row.RarityKind,
                    winRate = row.WinRate, pickRate = row.PickRate,
                    games = row.Games, description = row.Description
                }).ToArray(),
                top = (result.TopTen ?? new List<MayhemTopChampion>()).Take(10).Select(item => new
                {
                    name = item.Name, rank = item.Rank,
                    tier = item.Tier, winRate = item.WinRate
                }).ToArray()
            });
        }

        private async Task PublishIconsAsync(MayhemModule module, MayhemChampionResult result,
            MayhemAugmentRow[] rows, LeagueClientSession session,
            CancellationTokenSource source, int generation)
        {
            if (!Current(module, session, source, generation)) return;
            await PublishIconAsync(module, "champion", 0, result.ChampionIconUrl, session, source, generation);
            for (var index = 0; index < Math.Min(8, rows.Length); index++)
            {
                if (!Current(module, session, source, generation)) return;
                await PublishIconAsync(module, "augment", index, rows[index].IconUrl, session, source, generation);
            }
            if (rows.Length == 0 && result.AugmentIconUrls != null)
            {
                for (var index = 0; index < Math.Min(8, result.AugmentIconUrls.Count); index++)
                {
                    if (!Current(module, session, source, generation)) return;
                    await PublishIconAsync(module, "augment", index, result.AugmentIconUrls[index], session, source, generation);
                }
            }
            if (result.CoreItemIconUrls != null)
            {
                for (var index = 0; index < Math.Min(5, result.CoreItemIconUrls.Count); index++)
                {
                    if (!Current(module, session, source, generation)) return;
                    await PublishIconAsync(module, "item", index, result.CoreItemIconUrls[index], session, source, generation);
                }
            }
        }

        private async Task PublishIconAsync(MayhemModule module, string kind, int index, string reference,
            LeagueClientSession session, CancellationTokenSource source, int generation)
        {
            if (string.IsNullOrWhiteSpace(reference)) return;
            var image = await module.ReadUnifiedIconAsync(reference, source.Token);
            if (string.IsNullOrEmpty(image) || !Current(module, session, source, generation)) return;
            _publish(new JavaScriptSerializer().Serialize(new { type = "mayhem-icon", kind, index, image }));
        }

        private bool Owned(CancellationTokenSource source, int generation)
        {
            return generation == _generation && ReferenceEquals(_cancellation, source) && _canPublish();
        }

        private bool Current(MayhemModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !source.IsCancellationRequested && Owned(source, generation) &&
                (!checkSession || (session == null ? module.UnifiedLookupSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLookupSession)));
        }

        internal void PostStatus(string status)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(new { type = "mayhem-failed", status }));
        }

        internal void Cancel()
        {
            _generation++;
            var source = _cancellation;
            _cancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            var first = 0;
            var second = 0;
            var hidden = new UnifiedWorkspaceMayhemAdapter(() => false, delegate { first++; });
            var shown = new UnifiedWorkspaceMayhemAdapter(() => true, delegate(string json)
            {
                if (!json.Contains("\"type\":\"mayhem-failed\""))
                    throw new InvalidOperationException("Mayhem status contract changed.");
                second++;
            });
            hidden.Start(null, "Ahri");
            shown.Start(null, "Ahri");
            shown.Start(null, new string('x', 81));
            hidden.Cancel();
            if (first != 0 || second != 2)
                throw new InvalidOperationException("Mayhem adapters leaked state across workbenches.");
        }
    }
}
