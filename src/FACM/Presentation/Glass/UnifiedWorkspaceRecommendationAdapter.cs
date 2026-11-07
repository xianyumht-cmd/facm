using System;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceRecommendationAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private CancellationTokenSource _readCancellation;
        private CancellationTokenSource _applyCancellation;
        private int _generation;
        private LeagueBuildAdvisorSnapshot _snapshot;
        private LeagueClientSession _session;
        private bool _busy;

        internal UnifiedWorkspaceRecommendationAdapter(Func<bool> canPublish, Action<string> publish)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        internal void Start(LeagueBuildAdvisorModule module, bool force)
        {
            if (!_canPublish() || _busy) return;
            Cancel();
            if (module == null) { PostStatus("recommendation-failed", "unavailable"); return; }
            var source = new CancellationTokenSource();
            _readCancellation = source;
            ReadAsync(module, module.UnifiedRecommendationSession, source, _generation, force);
        }

        private async void ReadAsync(LeagueBuildAdvisorModule module,
            LeagueClientSession session, CancellationTokenSource source, int generation, bool force)
        {
            try
            {
                var snapshot = await module.RuntimeCompanionReadService.RefreshAsync(force, source.Token);
                if (!ReadCurrent(module, session, source, generation))
                {
                    if (ReadCurrent(module, null, source, generation, false))
                        PostStatus("recommendation-failed", "context-changed");
                    return;
                }
                _snapshot = snapshot;
                _session = session;
                _publish(SerializeRecommendation(snapshot));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified recommendation read skipped: " + exception.GetType().Name);
                if (ReadCurrent(module, null, source, generation, false))
                    PostStatus("recommendation-failed", "unavailable");
            }
            finally
            {
                if (ReferenceEquals(_readCancellation, source)) _readCancellation = null;
                source.Dispose();
            }
        }

        private static string SerializeRecommendation(LeagueBuildAdvisorSnapshot snapshot)
        {
            var rec = snapshot == null ? null : snapshot.Recommendation;
            return new JavaScriptSerializer().Serialize(new
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
                canApply = CanApply(snapshot),
                tier = rec == null ? null : rec.Tier,
                winRate = rec == null ? null : rec.WinRate,
                rows = rec == null ? new object[0] : rec.Rows.Take(24).Select(row => new
                {
                    category = row.Category, title = row.Recommendation, evidence = row.Evidence
                }).ToArray()
            });
        }

        private static bool CanApply(LeagueBuildAdvisorSnapshot snapshot)
        {
            return snapshot != null && snapshot.Connected &&
                snapshot.Activity == LeagueActivityLevel.ChampSelect &&
                string.Equals(snapshot.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                snapshot.Recommendation != null;
        }

        private bool ReadCurrent(LeagueBuildAdvisorModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !source.IsCancellationRequested && ReferenceEquals(_readCancellation, source) &&
                SessionCurrent(module, session, generation, checkSession);
        }

        private bool SessionCurrent(LeagueBuildAdvisorModule module, LeagueClientSession session,
            int generation, bool checkSession = true)
        {
            return _canPublish() && generation == _generation &&
                (!checkSession || (session == null ? module.UnifiedRecommendationSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedRecommendationSession)));
        }

        internal async void Apply(LeagueBuildAdvisorModule module, string json)
        {
            if (!_canPublish() || _busy) return;
            UnifiedRecommendationApplyChange request;
            if (!UnifiedWorkspaceSettingsContract.TryParseRecommendationApply(json, out request))
            {
                PostStatus("recommendation-result", "invalid");
                return;
            }
            var snapshot = _snapshot;
            var session = _session;
            var generation = _generation;
            if (module == null || !CanApply(snapshot) || !SessionCurrent(module, session, generation))
            {
                PostStatus("recommendation-result", "context-changed");
                return;
            }

            _busy = true;
            using (var source = new CancellationTokenSource())
            {
                _applyCancellation = source;
                try
                {
                    LeagueBuildApplyPlan original = null;
                    LeagueItemSetPlan itemPlan = null;
                    if (request.Runes || request.Spells)
                        original = await module.RuntimeCompanionApplyService.PrepareAsync(snapshot, source.Token);
                    if (!SessionCurrent(module, session, generation)) return;
                    if (request.Items)
                        itemPlan = await module.RuntimeCompanionItemSetService.PrepareAsync(snapshot, source.Token);
                    if (!SessionCurrent(module, session, generation)) return;

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
                        PostStatus("recommendation-result", "no-data");
                        return;
                    }
                    LeagueBuildApplyResult loadoutResult = null;
                    LeagueItemSetWriteResult itemResult = null;
                    if (canLoadout)
                        loadoutResult = await module.RuntimeCompanionApplyService.ApplyAsync(scoped, source.Token);
                    if (!SessionCurrent(module, session, generation)) return;
                    if (canItems)
                        itemResult = await module.RuntimeCompanionItemSetService.ApplyAsync(itemPlan, source.Token);
                    if (!SessionCurrent(module, session, generation)) return;

                    var selected = (request.Runes ? 1 : 0) + (request.Spells ? 1 : 0) + (request.Items ? 1 : 0);
                    var succeeded = (request.Runes && loadoutResult != null && loadoutResult.RunesApplied ? 1 : 0) +
                        (request.Spells && loadoutResult != null && loadoutResult.SpellsApplied ? 1 : 0) +
                        (request.Items && itemResult != null && itemResult.Succeeded ? 1 : 0);
                    var blocked = (loadoutResult != null && loadoutResult.Status == "blocked") ||
                        (itemResult != null && itemResult.Status == "blocked");
                    PostApplyResult(request, loadoutResult, itemResult,
                        succeeded == selected ? "success" : succeeded > 0 ? "partial" :
                        blocked ? "context-changed" : "failed");
                }
                catch (OperationCanceledException)
                {
                    if (SessionCurrent(module, session, generation))
                        PostStatus("recommendation-result", "context-changed");
                }
                catch (Exception exception)
                {
                    AppLog.Info("Unified recommendation apply skipped: " + exception.GetType().Name);
                    if (SessionCurrent(module, session, generation))
                        PostStatus("recommendation-result", "failed");
                }
                finally
                {
                    if (ReferenceEquals(_applyCancellation, source)) _applyCancellation = null;
                    _busy = false;
                }
            }
        }

        private void PostApplyResult(UnifiedRecommendationApplyChange request,
            LeagueBuildApplyResult loadout, LeagueItemSetWriteResult items, string status)
        {
            if (!_canPublish()) return;
            Func<bool, bool, bool, string> outcome = (selected, available, applied) =>
                !selected ? "not-selected" : !available ? "unavailable" : applied ? "success" : "failed";
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "recommendation-apply-result",
                status,
                runes = outcome(request.Runes, loadout != null, loadout != null && loadout.RunesApplied),
                spells = outcome(request.Spells, loadout != null, loadout != null && loadout.SpellsApplied),
                items = outcome(request.Items, items != null, items != null && items.Succeeded),
                runeSlotFull = loadout != null && loadout.RuneSkippedNoCapacity,
                itemCleanupWarning = items != null && items.CleanupWarning
            }));
        }

        internal void PostStatus(string type, string status)
        {
            if (_canPublish()) _publish(new JavaScriptSerializer().Serialize(new { type, status }));
        }

        internal void Cancel()
        {
            _generation++;
            var apply = _applyCancellation;
            _applyCancellation = null;
            if (apply != null && !apply.IsCancellationRequested) apply.Cancel();
            _snapshot = null;
            _session = null;
            var read = _readCancellation;
            _readCancellation = null;
            if (read != null && !read.IsCancellationRequested) read.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            var json = SerializeRecommendation(null);
            if (!json.Contains("\"type\":\"recommendation-details\"") ||
                !json.Contains("\"canApply\":false") ||
                !json.Contains("\"status\":\"unavailable\""))
                throw new InvalidOperationException("Recommendation serialization contract changed.");
            var posts = 0;
            var adapter = new UnifiedWorkspaceRecommendationAdapter(() => true,
                delegate { posts++; });
            adapter.Start(null, false);
            adapter.Cancel();
            if (posts != 1) throw new InvalidOperationException("Recommendation missing-module status changed.");
        }
    }
}
