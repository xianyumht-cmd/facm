using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueBuildApplyService : IDisposable
    {
        internal const string MySelectionPath = "/lol-champ-select/v1/session/my-selection";
        internal const string PerkInventoryPath = "/lol-perks/v1/inventory";
        internal const string PerkPagesPath = "/lol-perks/v1/pages";
        internal const string PerkCreatePath = "/lol-perks/v1/pages/";
        internal const string PerkCurrentPagePath = "/lol-perks/v1/currentpage";
        internal const int FlashSpellId = 4;
        private const string OwnedRunePagePrefix = "[FACM]";
        private const int RuneSettleDelayMilliseconds = 180;
        private const int SpellSettleDelayMilliseconds = 180;

        private readonly ILeagueClientApi _client;
        private readonly ILeagueClientWriteApi _writer;
        private readonly LeagueLiveDataService _live;
        private readonly IOpggBuildApi _opgg;
        private readonly bool _ownsOpgg;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private readonly SemaphoreSlim _applyGate = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public LeagueBuildApplyService(
            ILeagueClientApi client,
            ILeagueClientWriteApi writer,
            PerformanceBudgetProvider budgets)
            : this(client, writer, budgets, new OpggBuildApiClient(), true)
        {
        }

        internal LeagueBuildApplyService(
            ILeagueClientApi client,
            ILeagueClientWriteApi writer,
            PerformanceBudgetProvider budgets,
            IOpggBuildApi opgg,
            bool ownsOpgg = false)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            if (budgets == null) throw new ArgumentNullException(nameof(budgets));
            _live = new LeagueLiveDataService(client, budgets);
            _opgg = opgg ?? throw new ArgumentNullException(nameof(opgg));
            _ownsOpgg = ownsOpgg;
        }

        /// <summary>
        /// Read-only preparation. This method never calls the LCU write boundary.
        /// It is safe to run only after the user has clicked the apply button, before confirmation.
        /// </summary>
        public async Task<LeagueBuildApplyPlan> PrepareAsync(
            LeagueBuildAdvisorSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!IsUsableChampSelectSnapshot(snapshot))
            {
                AppLog.Info("League loadout prepare skipped; reason=unusable-champ-select-snapshot");
                return null;
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    var path = LeagueBuildAdvisorDataService.BuildPath(
                        snapshot.ChampionId,
                        snapshot.Mode,
                        snapshot.Position,
                        snapshot.Version);
                    var bytes = await _opgg.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
                    var plan = ParsePlan(bytes);
                    if (plan == null || (!plan.HasSpells && !plan.HasRunes))
                    {
                        AppLog.Info(
                            "League loadout prepare unavailable; champion=" + snapshot.ChampionId +
                            "; mode=" + (snapshot.Mode ?? string.Empty) +
                            "; position=" + (snapshot.Position ?? string.Empty));
                        return null;
                    }

                    plan.ChampionId = snapshot.ChampionId;
                    plan.ChampionName = snapshot.ChampionName;
                    plan.QueueId = snapshot.QueueId;
                    plan.Mode = snapshot.Mode;
                    plan.Position = snapshot.Position;
                    plan.Version = snapshot.Version;
                    plan.SpellPreview = FindRecommendation(snapshot.Recommendation, "summoner-spells");
                    plan.RunePreview = FindRecommendation(snapshot.Recommendation, "runes");
                    AppLog.Info(
                        "League loadout prepared; champion=" + plan.ChampionId +
                        "; queue=" + plan.QueueId +
                        "; runes=" + plan.HasRunes.ToString().ToLowerInvariant() +
                        "; spells=" + plan.HasSpells.ToString().ToLowerInvariant());
                    return plan;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    AppLog.Info("League loadout prepare unavailable; reason=timeout");
                    return null;
                }
            }
        }

        /// <summary>
        /// Explicit write operation. The caller must have already obtained user confirmation.
        /// Context is re-read immediately before the first write; any phase/champion drift blocks all writes.
        /// Success is reported only after the active rune page / spell selection remains correct after a short
        /// LCU settle window. A 2xx write response by itself is never treated as proof that the client applied it.
        /// </summary>
        public async Task<LeagueBuildApplyResult> ApplyAsync(
            LeagueBuildApplyPlan plan,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = new LeagueBuildApplyResult
                {
                    Status = "failed",
                    RuneStatus = plan.HasRunes ? "not-started" : "not-available",
                    SpellStatus = plan.HasSpells ? "not-started" : "not-available"
                };

                var live = await _live.RefreshAsync(cancellationToken).ConfigureAwait(false);
                var local = live == null ? null : live.Players.FirstOrDefault(row => row.IsLocalPlayer);
                var currentChampion = LeagueBuildAdvisorDataService.ResolveChampionId(live, local);
                if (live == null || !live.Connected || live.Activity != LeagueActivityLevel.ChampSelect)
                {
                    result.Status = "blocked";
                    result.BlockReason = "champ-select-required";
                    LogApplyResult(plan, result);
                    return result;
                }
                if (plan.ChampionId <= 0 || currentChampion != plan.ChampionId)
                {
                    result.Status = "blocked";
                    result.BlockReason = "champion-changed";
                    LogApplyResult(plan, result);
                    return result;
                }
                if (plan.QueueId > 0 && live.QueueId > 0 && live.QueueId != plan.QueueId)
                {
                    result.Status = "blocked";
                    result.BlockReason = "queue-changed";
                    LogApplyResult(plan, result);
                    return result;
                }

                if (plan.HasRunes)
                    await ApplyRunesAsync(plan, result, cancellationToken).ConfigureAwait(false);
                if (plan.HasSpells)
                    await ApplySpellsAsync(plan, result, cancellationToken).ConfigureAwait(false);

                var allExpectedSucceeded = (!plan.HasRunes || result.RunesApplied) &&
                                           (!plan.HasSpells || result.SpellsApplied);
                if (allExpectedSucceeded && (plan.HasRunes || plan.HasSpells))
                    result.Status = "success";
                else if (result.AnyApplied)
                    result.Status = "partial";
                else
                    result.Status = "failed";
                LogApplyResult(plan, result);
                return result;
            }
            finally
            {
                _applyGate.Release();
            }
        }

        internal LeagueBuildApplyPlan ParsePlan(byte[] bytes)
        {
            var root = ParseObject(bytes);
            var data = ReadDictionary(root, "data");
            if (data == null) return null;

            var plan = new LeagueBuildApplyPlan();
            var spells = FirstDictionary(ReadValue(data, "summoner_spells"));
            var spellIds = ReadIntArray(ReadValue(spells, "ids"));
            if (spellIds.Count >= 2)
            {
                plan.Spell1Id = spellIds[0];
                plan.Spell2Id = spellIds[1];
            }

            var rune = FirstDictionary(ReadValue(data, "runes"));
            if (rune == null)
            {
                var page = FirstDictionary(ReadValue(data, "rune_pages"));
                rune = FirstDictionary(ReadValue(page, "builds")) ?? page;
            }
            else
            {
                rune = FirstDictionary(ReadValue(rune, "builds")) ?? rune;
            }

            if (rune != null)
            {
                plan.PrimaryStyleId = ReadInt(rune, "primary_page_id");
                plan.SecondaryStyleId = ReadInt(rune, "secondary_page_id");
                plan.PrimaryRuneIds.AddRange(ReadIntArray(ReadValue(rune, "primary_rune_ids")));
                plan.SecondaryRuneIds.AddRange(ReadIntArray(ReadValue(rune, "secondary_rune_ids")));
                plan.StatModIds.AddRange(ReadIntArray(ReadValue(rune, "stat_mod_ids")));
            }

            return plan;
        }

        internal static void PreserveFlashSlot(
            int oldSpell1Id,
            int oldSpell2Id,
            ref int newSpell1Id,
            ref int newSpell2Id)
        {
            if (newSpell1Id != FlashSpellId && newSpell2Id != FlashSpellId) return;

            if (oldSpell1Id == FlashSpellId && newSpell2Id == FlashSpellId)
            {
                var swap = newSpell1Id;
                newSpell1Id = newSpell2Id;
                newSpell2Id = swap;
            }
            else if (oldSpell2Id == FlashSpellId && newSpell1Id == FlashSpellId)
            {
                var swap = newSpell1Id;
                newSpell1Id = newSpell2Id;
                newSpell2Id = swap;
            }
        }

        private async Task ApplyRunesAsync(
            LeagueBuildApplyPlan plan,
            LeagueBuildApplyResult result,
            CancellationToken cancellationToken)
        {
            var inventoryBytes = await _client.TryGetBytesAsync(PerkInventoryPath, cancellationToken).ConfigureAwait(false);
            var inventory = ParseObject(inventoryBytes);
            if (inventory == null)
            {
                result.RuneStatus = "inventory-unavailable";
                return;
            }

            var canAddCustomPage = ReadBool(inventory, "canAddCustomPage");
            var pageName = BuildRunePageName(plan);
            var pagesBytes = await _client.TryGetBytesAsync(PerkPagesPath, cancellationToken).ConfigureAwait(false);
            var pages = ParseRunePages(pagesBytes);
            var ownedPage = FindOwnedRunePage(pages, pageName, exactNameOnly: true);
            if (ownedPage == null && !canAddCustomPage)
                ownedPage = FindOwnedRunePage(pages, pageName, exactNameOnly: false);

            var pageId = ownedPage == null ? 0 : ReadInt(ownedPage, "id");
            var reused = pageId > 0;
            if (!reused && !canAddCustomPage)
            {
                result.RuneSkippedNoCapacity = true;
                result.RuneStatus = pagesBytes == null ? "pages-unavailable" : "no-capacity";
                AppLog.Info(
                    "League rune apply skipped; reason=" + result.RuneStatus +
                    "; facmOwnedPage=false");
                return;
            }

            if (!reused)
            {
                var createJson = _json.Serialize(new Dictionary<string, object>
                {
                    { "name", pageName },
                    { "isEditable", true },
                    { "primaryStyleId", plan.PrimaryStyleId.ToString(CultureInfo.InvariantCulture) }
                });
                var created = await _writer.TrySendJsonAsync(
                    "POST",
                    PerkCreatePath,
                    createJson,
                    cancellationToken).ConfigureAwait(false);
                if (created == null || !created.IsSuccessStatusCode)
                {
                    result.RuneStatus = "create-failed";
                    return;
                }

                var createdPage = ParseObject(created.Body);
                pageId = ReadInt(createdPage, "id");
                if (pageId <= 0)
                {
                    result.RuneStatus = "create-response-invalid";
                    return;
                }
                result.CreatedRunePageId = pageId;
            }
            else
            {
                AppLog.Info(
                    "League rune page reuse; pageId=" + pageId +
                    "; exact=" + string.Equals(ReadString(ownedPage, "name"), pageName, StringComparison.OrdinalIgnoreCase).ToString().ToLowerInvariant() +
                    "; capacity=" + canAddCustomPage.ToString().ToLowerInvariant());
            }

            var selected = plan.GetSelectedPerkIds();
            var pageJson = _json.Serialize(new Dictionary<string, object>
            {
                { "id", pageId },
                { "isRecommendationOverride", false },
                { "isTemporary", false },
                { "name", pageName },
                { "primaryStyleId", plan.PrimaryStyleId },
                { "subStyleId", plan.SecondaryStyleId },
                { "selectedPerkIds", selected.ToArray() }
            });
            var updated = await _writer.TrySendJsonAsync(
                "PUT",
                PerkPagesPath + "/" + pageId.ToString(CultureInfo.InvariantCulture),
                pageJson,
                cancellationToken).ConfigureAwait(false);
            if (updated == null || !updated.IsSuccessStatusCode)
            {
                result.RuneStatus = "update-failed";
                return;
            }

            if (!await TrySelectRunePageAsync(pageId, cancellationToken).ConfigureAwait(false))
            {
                result.RuneStatus = "select-failed";
                return;
            }

            if (!await VerifyRuneSelectionSettledAsync(
                    pageId,
                    plan.PrimaryStyleId,
                    plan.SecondaryStyleId,
                    selected,
                    cancellationToken).ConfigureAwait(false))
            {
                AppLog.Warning("League rune selection did not settle after first write; pageId=" + pageId + "; retrying selection once.");
                if (!await TrySelectRunePageAsync(pageId, cancellationToken).ConfigureAwait(false) ||
                    !await VerifyRuneSelectionSettledAsync(
                        pageId,
                        plan.PrimaryStyleId,
                        plan.SecondaryStyleId,
                        selected,
                        cancellationToken).ConfigureAwait(false))
                {
                    result.RuneStatus = "verify-failed";
                    AppLog.Warning("League rune apply verification failed; pageId=" + pageId + "; FACM will not report success.");
                    return;
                }
            }

            result.RunesApplied = true;
            result.RuneStatus = reused ? "applied-reused-owned-page" : "applied";
        }

        private async Task<bool> TrySelectRunePageAsync(int pageId, CancellationToken cancellationToken)
        {
            var response = await _writer.TrySendJsonAsync(
                "PUT",
                PerkCurrentPagePath,
                pageId.ToString(CultureInfo.InvariantCulture),
                cancellationToken).ConfigureAwait(false);
            return response != null && response.IsSuccessStatusCode;
        }

        private async Task<bool> VerifyRuneSelectionSettledAsync(
            int pageId,
            int primaryStyleId,
            int secondaryStyleId,
            IList<int> selected,
            CancellationToken cancellationToken)
        {
            await Task.Delay(RuneSettleDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            var currentBytes = await _client.TryGetBytesAsync(PerkCurrentPagePath, cancellationToken).ConfigureAwait(false);
            var pagesBytes = await _client.TryGetBytesAsync(PerkPagesPath, cancellationToken).ConfigureAwait(false);
            if (!VerifyCurrentRunePage(currentBytes, pageId)) return false;
            if (!VerifyRunePage(pagesBytes, pageId, primaryStyleId, secondaryStyleId, selected)) return false;

            // A second read catches Tencent LCU accepting the request and then immediately restoring an old page.
            await Task.Delay(RuneSettleDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            currentBytes = await _client.TryGetBytesAsync(PerkCurrentPagePath, cancellationToken).ConfigureAwait(false);
            return VerifyCurrentRunePage(currentBytes, pageId);
        }

        private async Task ApplySpellsAsync(
            LeagueBuildApplyPlan plan,
            LeagueBuildApplyResult result,
            CancellationToken cancellationToken)
        {
            var beforeBytes = await _client.TryGetBytesAsync(MySelectionPath, cancellationToken).ConfigureAwait(false);
            var before = ParseObject(beforeBytes);
            if (before == null)
            {
                result.SpellStatus = "selection-unavailable";
                return;
            }

            var oldSpell1 = ReadInt(before, "spell1Id");
            var oldSpell2 = ReadInt(before, "spell2Id");
            var newSpell1 = plan.Spell1Id;
            var newSpell2 = plan.Spell2Id;
            PreserveFlashSlot(oldSpell1, oldSpell2, ref newSpell1, ref newSpell2);
            var changed = oldSpell1 != newSpell1 || oldSpell2 != newSpell2;

            if (changed && !await TryWriteSpellsAsync(newSpell1, newSpell2, cancellationToken).ConfigureAwait(false))
            {
                result.SpellStatus = "write-failed";
                return;
            }

            if (!await VerifySpellsSettledAsync(newSpell1, newSpell2, cancellationToken).ConfigureAwait(false))
            {
                AppLog.Warning(
                    "League summoner-spell selection did not settle after first write; expected=" +
                    newSpell1 + "/" + newSpell2 + "; retrying once.");
                if (!await TryWriteSpellsAsync(newSpell1, newSpell2, cancellationToken).ConfigureAwait(false) ||
                    !await VerifySpellsSettledAsync(newSpell1, newSpell2, cancellationToken).ConfigureAwait(false))
                {
                    result.SpellStatus = "verify-failed";
                    AppLog.Warning(
                        "League summoner-spell apply verification failed; expected=" + newSpell1 + "/" + newSpell2 +
                        "; FACM will not report success.");
                    return;
                }
            }

            result.SpellsApplied = true;
            result.SpellStatus = changed ? "applied" : "already-set";
        }

        private async Task<bool> TryWriteSpellsAsync(int spell1Id, int spell2Id, CancellationToken cancellationToken)
        {
            var spellJson = _json.Serialize(new Dictionary<string, object>
            {
                { "spell1Id", spell1Id },
                { "spell2Id", spell2Id }
            });
            var patched = await _writer.TrySendJsonAsync(
                "PATCH",
                MySelectionPath,
                spellJson,
                cancellationToken).ConfigureAwait(false);
            return patched != null && patched.IsSuccessStatusCode;
        }

        private async Task<bool> VerifySpellsSettledAsync(
            int expectedSpell1,
            int expectedSpell2,
            CancellationToken cancellationToken)
        {
            await Task.Delay(SpellSettleDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            if (!await VerifySpellsOnceAsync(expectedSpell1, expectedSpell2, cancellationToken).ConfigureAwait(false))
                return false;

            await Task.Delay(SpellSettleDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            return await VerifySpellsOnceAsync(expectedSpell1, expectedSpell2, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> VerifySpellsOnceAsync(
            int expectedSpell1,
            int expectedSpell2,
            CancellationToken cancellationToken)
        {
            var bytes = await _client.TryGetBytesAsync(MySelectionPath, cancellationToken).ConfigureAwait(false);
            var selection = ParseObject(bytes);
            if (selection == null) return false;
            return ReadInt(selection, "spell1Id") == expectedSpell1 && ReadInt(selection, "spell2Id") == expectedSpell2;
        }

        private bool VerifyCurrentRunePage(byte[] currentPageBytes, int pageId)
        {
            if (currentPageBytes == null || currentPageBytes.Length == 0) return false;
            var text = Encoding.UTF8.GetString(currentPageBytes).Trim();
            int directId;
            if (int.TryParse(text.Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out directId))
                return directId == pageId;

            var current = ParseObject(currentPageBytes);
            return current != null && ReadInt(current, "id") == pageId;
        }

        private bool VerifyRunePage(
            byte[] pagesBytes,
            int pageId,
            int primaryStyleId,
            int secondaryStyleId,
            IList<int> selected)
        {
            var pages = ParseRunePages(pagesBytes);
            var page = pages.FirstOrDefault(row => ReadInt(row, "id") == pageId);
            if (page == null) return false;
            if (ReadInt(page, "primaryStyleId") != primaryStyleId) return false;
            if (ReadInt(page, "subStyleId") != secondaryStyleId) return false;
            var actual = ReadIntArray(ReadValue(page, "selectedPerkIds"));
            return actual.SequenceEqual(selected ?? Array.Empty<int>());
        }

        private List<Dictionary<string, object>> ParseRunePages(byte[] pagesBytes)
        {
            var output = new List<Dictionary<string, object>>();
            if (pagesBytes == null || pagesBytes.Length == 0) return output;
            object decoded;
            try { decoded = _json.DeserializeObject(Encoding.UTF8.GetString(pagesBytes)); }
            catch { return output; }
            output.AddRange(EnumerateDictionaries(decoded));
            return output;
        }

        private static Dictionary<string, object> FindOwnedRunePage(
            IEnumerable<Dictionary<string, object>> pages,
            string desiredName,
            bool exactNameOnly)
        {
            if (pages == null) return null;
            var owned = pages.Where(page =>
            {
                var name = ReadString(page, "name");
                return !string.IsNullOrWhiteSpace(name) &&
                       name.StartsWith(OwnedRunePagePrefix, StringComparison.OrdinalIgnoreCase) &&
                       ReadInt(page, "id") > 0;
            }).ToList();
            var exact = owned.FirstOrDefault(page =>
                string.Equals(ReadString(page, "name"), desiredName, StringComparison.OrdinalIgnoreCase));
            if (exact != null || exactNameOnly) return exact;
            return owned.FirstOrDefault();
        }

        private static void LogApplyResult(LeagueBuildApplyPlan plan, LeagueBuildApplyResult result)
        {
            if (result == null) return;
            AppLog.Info(
                "League loadout apply result; status=" + (result.Status ?? string.Empty) +
                "; block=" + (result.BlockReason ?? string.Empty) +
                "; rune=" + (result.RuneStatus ?? string.Empty) +
                "; spell=" + (result.SpellStatus ?? string.Empty) +
                "; champion=" + (plan == null ? 0 : plan.ChampionId) +
                "; createdPageId=" + result.CreatedRunePageId);
        }

        private static bool IsUsableChampSelectSnapshot(LeagueBuildAdvisorSnapshot snapshot)
        {
            return snapshot != null &&
                   snapshot.Connected &&
                   snapshot.Activity == LeagueActivityLevel.ChampSelect &&
                   snapshot.ChampionId > 0 &&
                   !string.IsNullOrWhiteSpace(snapshot.Mode) &&
                   !string.IsNullOrWhiteSpace(snapshot.Position) &&
                   !string.IsNullOrWhiteSpace(snapshot.Version) &&
                   snapshot.Recommendation != null;
        }

        private static string FindRecommendation(LeagueBuildRecommendation recommendation, string category)
        {
            if (recommendation == null) return null;
            var row = recommendation.Rows.FirstOrDefault(item =>
                string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
            return row == null ? null : row.Recommendation;
        }

        private static string BuildRunePageName(LeagueBuildApplyPlan plan)
        {
            var champion = string.IsNullOrWhiteSpace(plan.ChampionName)
                ? "#" + plan.ChampionId.ToString(CultureInfo.InvariantCulture)
                : plan.ChampionName.Trim();
            var position = string.IsNullOrWhiteSpace(plan.Position) ||
                           string.Equals(plan.Position, "none", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : " - " + plan.Position.Trim();
            var value = OwnedRunePagePrefix + " " + champion + position;
            return value.Length <= 50 ? value : value.Substring(0, 50);
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            return ReadValue(source, key) as Dictionary<string, object>;
        }

        private static object ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            int parsed;
            return value != null && int.TryParse(
                       Convert.ToString(value, CultureInfo.InvariantCulture),
                       NumberStyles.Any,
                       CultureInfo.InvariantCulture,
                       out parsed)
                ? parsed
                : 0;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool ReadBool(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            bool parsed;
            return value != null && bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) && parsed;
        }

        private static List<int> ReadIntArray(object value)
        {
            var output = new List<int>();
            foreach (var item in EnumerateValues(value))
            {
                int parsed;
                if (item != null && int.TryParse(
                        Convert.ToString(item, CultureInfo.InvariantCulture),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out parsed) && parsed > 0)
                    output.Add(parsed);
            }
            return output;
        }

        private static Dictionary<string, object> FirstDictionary(object value)
        {
            return EnumerateDictionaries(value).FirstOrDefault();
        }

        private static IEnumerable<object> EnumerateValues(object value)
        {
            if (value == null) yield break;
            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array) yield return item;
                yield break;
            }
            var list = value as ArrayList;
            if (list != null)
            {
                foreach (var item in list) yield return item;
                yield break;
            }
            var enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (var item in enumerable) yield return item;
            }
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            var direct = value as Dictionary<string, object>;
            if (direct != null)
            {
                yield return direct;
                yield break;
            }
            foreach (var item in EnumerateValues(value))
            {
                var row = item as Dictionary<string, object>;
                if (row != null) yield return row;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _applyGate.Dispose();
            if (_ownsOpgg)
            {
                var disposable = _opgg as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LeagueBuildApplyService));
        }
    }
}
