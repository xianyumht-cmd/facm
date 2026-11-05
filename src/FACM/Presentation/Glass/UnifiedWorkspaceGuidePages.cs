using System;
using System.Linq;
using System.Collections.Generic;
using FACM.Mayhem;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // WebView2 owns presentation only; all reads and writes retain the existing module owners.
    internal sealed partial class UnifiedWorkspaceForm
    {
        private CancellationTokenSource _mayhemCancellation;
        private int _mayhemGeneration;
        private CancellationTokenSource _recommendationCancellation;
        private CancellationTokenSource _recommendationApplyCancellation;
        private int _recommendationGeneration;
        private LeagueBuildAdvisorSnapshot _recommendationSnapshot;
        private LeagueClientSession _recommendationSession;
        private bool _recommendationBusy;

        private void OpenMayhemPage(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Mayhem);
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            PostLeaguePageStatus("mayhem-failed", "ready");
        }

        private void StartMayhemQuery(string query)
        {
            if (_navigation.Current != LeagueHubNavigation.Mayhem) return;
            CancelMayhemRead();
            query = (query ?? string.Empty).Trim();
            if (query.Length == 0 || query.Length > 80)
            {
                PostLeaguePageStatus("mayhem-failed", "invalid");
                return;
            }
            var module = LeagueHubUiBridge.UnifiedMayhem;
            if (module == null)
            {
                PostLeaguePageStatus("mayhem-failed", "unavailable");
                return;
            }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(13));
            _mayhemCancellation = source;
            LoadMayhemAsync(module, query, module.UnifiedLookupSession, source, _mayhemGeneration);
        }

        private async void LoadMayhemAsync(MayhemModule module, string query, LeagueClientSession session,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var result = await module.ReadUnifiedGuideAsync(query, source.Token);
                if (!MayhemCurrent(module, session, source, generation))
                {
                    if (MayhemCurrent(module, null, source, generation, false))
                        PostLeaguePageStatus("mayhem-failed", "context-changed");
                    return;
                }
                if (result == null || !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    PostLeaguePageStatus("mayhem-failed", "no-data");
                    return;
                }
                var rankedAugments = (result.AugmentRows ?? new List<MayhemAugmentRow>())
                    .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name)).Take(8).ToArray();
                var payload = new JavaScriptSerializer().Serialize(new
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
                    augmentDetails = rankedAugments.Select(row => new
                    {
                        name = row.Name, rank = row.Rank,
                        rarity = row.Rarity, rarityKind = row.RarityKind,
                        winRate = row.WinRate, pickRate = row.PickRate,
                        games = row.Games, description = row.Description
                    }).ToArray(),
                    top = (result.TopTen ?? new System.Collections.Generic.List<FACM.Mayhem.MayhemTopChampion>()).Take(10).Select(item => new
                    {
                        name = item.Name, rank = item.Rank,
                        tier = item.Tier, winRate = item.WinRate
                    }).ToArray()
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
                // Text arrives first. Icons use the same image-cache owner and are optional.
                // Slow or unavailable icon sources must never block the result page.
                await PostMayhemIconsAsync(module, result, rankedAugments, session, source, generation);
            }
            catch (OperationCanceledException)
            {
                // Timeout cancels the token without changing the active request generation.
                // Do not confuse that with a user navigation or explicit Cancel.
                if (!IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                    _navigation.Current == LeagueHubNavigation.Mayhem &&
                    ReferenceEquals(_mayhemCancellation, source) && generation == _mayhemGeneration)
                    PostLeaguePageStatus("mayhem-failed", "timeout");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified Mayhem query skipped: " + exception.GetType().Name);
                if (MayhemCurrent(module, null, source, generation, false))
                    PostLeaguePageStatus("mayhem-failed", "unavailable");
            }
            finally
            {
                if (ReferenceEquals(_mayhemCancellation, source)) _mayhemCancellation = null;
                source.Dispose();
            }
        }

        private async Task PostMayhemIconsAsync(MayhemModule module, MayhemChampionResult result,
            MayhemAugmentRow[] rows, LeagueClientSession session,
            CancellationTokenSource source, int generation)
        {
            if (!MayhemCurrent(module, session, source, generation)) return;
            await PostMayhemIconAsync(module, "champion", 0, result.ChampionIconUrl, session, source, generation);
            for (var index = 0; index < Math.Min(8, rows.Length); index++)
            {
                if (!MayhemCurrent(module, session, source, generation)) return;
                await PostMayhemIconAsync(module, "augment", index, rows[index].IconUrl, session, source, generation);
            }
            if (rows.Length == 0 && result.AugmentIconUrls != null)
            {
                for (var index = 0; index < Math.Min(8, result.AugmentIconUrls.Count); index++)
                {
                    if (!MayhemCurrent(module, session, source, generation)) return;
                    await PostMayhemIconAsync(module, "augment", index, result.AugmentIconUrls[index], session, source, generation);
                }
            }
            if (result.CoreItemIconUrls != null)
            {
                for (var index = 0; index < Math.Min(5, result.CoreItemIconUrls.Count); index++)
                {
                    if (!MayhemCurrent(module, session, source, generation)) return;
                    await PostMayhemIconAsync(module, "item", index, result.CoreItemIconUrls[index], session, source, generation);
                }
            }
        }

        private async Task PostMayhemIconAsync(MayhemModule module, string kind, int index, string reference,
            LeagueClientSession session, CancellationTokenSource source, int generation)
        {
            if (string.IsNullOrWhiteSpace(reference)) return;
            var image = await module.ReadUnifiedIconAsync(reference, source.Token);
            if (string.IsNullOrEmpty(image) || !MayhemCurrent(module, session, source, generation)) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "mayhem-icon", kind, index, image
            }));
        }

        private bool MayhemCurrent(MayhemModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                !source.IsCancellationRequested &&
                ReferenceEquals(_mayhemCancellation, source) && generation == _mayhemGeneration &&
                _navigation.Current == LeagueHubNavigation.Mayhem &&
                (!checkSession || (session == null ? module.UnifiedLookupSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLookupSession)));
        }

        private void CancelMayhemRead()
        {
            _mayhemGeneration++;
            var current = _mayhemCancellation;
            _mayhemCancellation = null;
            if (current != null && !current.IsCancellationRequested) current.Cancel();
        }

        private void OpenRecommendationPage(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Recommendation);
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            PostLeaguePageStatus("recommendation-failed", "loading");
            if (_ready) StartRecommendationRead(false);
        }

        private void StartRecommendationRead(bool force)
        {
            if (_navigation.Current != LeagueHubNavigation.Recommendation || _recommendationBusy) return;
            CancelRecommendationRead();
            var module = LeagueHubUiBridge.UnifiedAdvisor;
            if (module == null)
            {
                PostLeaguePageStatus("recommendation-failed", "unavailable");
                return;
            }
            var source = new CancellationTokenSource();
            _recommendationCancellation = source;
            LoadRecommendationAsync(module, module.UnifiedRecommendationSession, source,
                _recommendationGeneration, force);
        }

        private async void LoadRecommendationAsync(LeagueBuildAdvisorModule module,
            LeagueClientSession session, CancellationTokenSource source, int generation, bool force)
        {
            try
            {
                var snapshot = await module.RuntimeCompanionReadService.RefreshAsync(force, source.Token);
                if (!RecommendationCurrent(module, session, source, generation))
                {
                    if (RecommendationCurrent(module, null, source, generation, false))
                        PostLeaguePageStatus("recommendation-failed", "context-changed");
                    return;
                }
                _recommendationSnapshot = snapshot;
                _recommendationSession = session;
                var rec = snapshot == null ? null : snapshot.Recommendation;
                var payload = new JavaScriptSerializer().Serialize(new
                {
                    type = "recommendation-details",
                    status = snapshot == null ? "unavailable" : snapshot.Status,
                    champion = snapshot == null ? null : snapshot.ChampionName,
                    championId = snapshot == null ? 0 : snapshot.ChampionId,
                    mode = snapshot == null ? null : snapshot.Mode,
                    position = snapshot == null ? null : snapshot.Position,
                    version = snapshot == null ? null : snapshot.Version,
                    source = snapshot == null ? null : snapshot.Source,
                    cached = snapshot != null && snapshot.FromCache,
                    canApply = CanApplyUnifiedRecommendation(snapshot),
                    tier = rec == null ? null : rec.Tier,
                    winRate = rec == null ? null : rec.WinRate,
                    rows = rec == null ? new object[0] : rec.Rows.Take(24).Select(row => new
                    {
                        category = row.Category, title = row.Recommendation, evidence = row.Evidence
                    }).ToArray()
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified recommendation read skipped: " + exception.GetType().Name);
                if (RecommendationCurrent(module, null, source, generation, false))
                    PostLeaguePageStatus("recommendation-failed", "unavailable");
            }
            finally
            {
                if (ReferenceEquals(_recommendationCancellation, source)) _recommendationCancellation = null;
                source.Dispose();
            }
        }

        private static bool CanApplyUnifiedRecommendation(LeagueBuildAdvisorSnapshot snapshot)
        {
            return snapshot != null && snapshot.Connected &&
                snapshot.Activity == LeagueActivityLevel.ChampSelect &&
                string.Equals(snapshot.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                snapshot.Recommendation != null;
        }

        private bool RecommendationCurrent(LeagueBuildAdvisorModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                !source.IsCancellationRequested &&
                ReferenceEquals(_recommendationCancellation, source) && generation == _recommendationGeneration &&
                _navigation.Current == LeagueHubNavigation.Recommendation &&
                (!checkSession || (session == null ? module.UnifiedRecommendationSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedRecommendationSession)));
        }

        private bool RecommendationSessionCurrent(LeagueBuildAdvisorModule module, LeagueClientSession session,
            int generation)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                _navigation.Current == LeagueHubNavigation.Recommendation &&
                generation == _recommendationGeneration &&
                (session == null ? module.UnifiedRecommendationSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedRecommendationSession));
        }

        private async void ApplyUnifiedRecommendation(string json)
        {
            if (_navigation.Current != LeagueHubNavigation.Recommendation || _recommendationBusy) return;
            UnifiedRecommendationApplyChange request;
            if (!UnifiedWorkspaceSettingsContract.TryParseRecommendationApply(json, out request))
            {
                PostLeaguePageStatus("recommendation-result", "invalid");
                return;
            }
            var module = LeagueHubUiBridge.UnifiedAdvisor;
            var snapshot = _recommendationSnapshot;
            var session = _recommendationSession;
            var generation = _recommendationGeneration;
            if (module == null || !CanApplyUnifiedRecommendation(snapshot) ||
                !RecommendationSessionCurrent(module, session, generation))
            {
                PostLeaguePageStatus("recommendation-result", "context-changed");
                return;
            }

            _recommendationBusy = true;
            using (var source = new CancellationTokenSource())
            {
                _recommendationApplyCancellation = source;
                try
                {
                    LeagueBuildApplyPlan original = null;
                    LeagueItemSetPlan itemPlan = null;
                    if (request.Runes || request.Spells)
                        original = await module.RuntimeCompanionApplyService.PrepareAsync(snapshot, source.Token);
                    if (!RecommendationSessionCurrent(module, session, generation)) return;
                    if (request.Items)
                        itemPlan = await module.RuntimeCompanionItemSetService.PrepareAsync(snapshot, source.Token);
                    if (!RecommendationSessionCurrent(module, session, generation)) return;

                    LeagueBuildApplyPlan scoped = null;
                    if (original != null)
                    {
                        scoped = new LeagueBuildApplyPlan
                        {
                            ChampionId = original.ChampionId, ChampionName = original.ChampionName,
                            TraceId = original.TraceId, QueueId = original.QueueId,
                            QueueType = original.QueueType, PresetGroup = original.PresetGroup,
                            Mode = original.Mode, Position = original.Position,
                            Version = original.Version, RunePreview = original.RunePreview,
                            SpellPreview = original.SpellPreview
                        };
                        if (request.Spells)
                        {
                            scoped.Spell1Id = original.Spell1Id;
                            scoped.Spell2Id = original.Spell2Id;
                        }
                        if (request.Runes)
                        {
                            scoped.PrimaryStyleId = original.PrimaryStyleId;
                            scoped.SecondaryStyleId = original.SecondaryStyleId;
                            scoped.PresetPerkIds.AddRange(original.PresetPerkIds);
                            scoped.PrimaryRuneIds.AddRange(original.PrimaryRuneIds);
                            scoped.SecondaryRuneIds.AddRange(original.SecondaryRuneIds);
                            scoped.StatModIds.AddRange(original.StatModIds);
                        }
                    }

                    var canLoadout = scoped != null && (scoped.HasRunes || scoped.HasSpells);
                    var canItems = request.Items && itemPlan != null && itemPlan.HasItems;
                    if (!canLoadout && !canItems)
                    {
                        PostLeaguePageStatus("recommendation-result", "no-data");
                        return;
                    }
                    LeagueBuildApplyResult loadoutResult = null;
                    LeagueItemSetWriteResult itemResult = null;
                    if (canLoadout)
                        loadoutResult = await module.RuntimeCompanionApplyService.ApplyAsync(scoped, source.Token);
                    if (!RecommendationSessionCurrent(module, session, generation)) return;
                    if (canItems)
                        itemResult = await module.RuntimeCompanionItemSetService.ApplyAsync(itemPlan, source.Token);
                    if (!RecommendationSessionCurrent(module, session, generation)) return;

                    var selected = (request.Runes ? 1 : 0) + (request.Spells ? 1 : 0) + (request.Items ? 1 : 0);
                    var succeeded = (request.Runes && loadoutResult != null && loadoutResult.RunesApplied ? 1 : 0) +
                        (request.Spells && loadoutResult != null && loadoutResult.SpellsApplied ? 1 : 0) +
                        (request.Items && itemResult != null && itemResult.Succeeded ? 1 : 0);
                    var blocked = (loadoutResult != null && loadoutResult.Status == "blocked") ||
                        (itemResult != null && itemResult.Status == "blocked");
                    PostRecommendationApplyResult(request, loadoutResult, itemResult,
                        succeeded == selected ? "success" : succeeded > 0 ? "partial" :
                        blocked ? "context-changed" : "failed");
                }
                catch (OperationCanceledException)
                {
                    if (RecommendationSessionCurrent(module, session, generation))
                        PostLeaguePageStatus("recommendation-result", "context-changed");
                }
                catch (Exception exception)
                {
                    AppLog.Info("Unified recommendation apply skipped: " + exception.GetType().Name);
                    if (RecommendationSessionCurrent(module, session, generation))
                        PostLeaguePageStatus("recommendation-result", "failed");
                }
                finally
                {
                    if (ReferenceEquals(_recommendationApplyCancellation, source))
                        _recommendationApplyCancellation = null;
                    _recommendationBusy = false;
                }
            }
        }

        private void PostRecommendationApplyResult(UnifiedRecommendationApplyChange request,
            LeagueBuildApplyResult loadout, LeagueItemSetWriteResult items, string status)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != LeagueHubNavigation.Recommendation) return;
            Func<bool, bool, bool, string> outcome = (selected, available, applied) =>
                !selected ? "not-selected" : !available ? "unavailable" :
                applied ? "success" : "failed";
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "recommendation-apply-result",
                status,
                runes = outcome(request.Runes, loadout != null,
                    loadout != null && loadout.RunesApplied),
                spells = outcome(request.Spells, loadout != null,
                    loadout != null && loadout.SpellsApplied),
                items = outcome(request.Items, items != null,
                    items != null && items.Succeeded),
                runeSlotFull = loadout != null && loadout.RuneSkippedNoCapacity,
                itemCleanupWarning = items != null && items.CleanupWarning
            }));
        }

        private void CancelRecommendationRead()
        {
            _recommendationGeneration++;
            var apply = _recommendationApplyCancellation;
            _recommendationApplyCancellation = null;
            if (apply != null && !apply.IsCancellationRequested) apply.Cancel();
            _recommendationSnapshot = null;
            _recommendationSession = null;
            var source = _recommendationCancellation;
            _recommendationCancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        private void PostLeaguePageStatus(string type, string status)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type, status
            }));
        }
    }
}
