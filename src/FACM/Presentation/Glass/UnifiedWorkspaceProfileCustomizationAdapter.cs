using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceProfileCustomizationAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private CancellationTokenSource _cancellation;
        private int _generation;
        private LeagueClientSession _session;
        private LeagueProfileCustomizationService _service;
        private HashSet<int> _champions = new HashSet<int>();
        private HashSet<int> _skins = new HashSet<int>();

        internal UnifiedWorkspaceProfileCustomizationAdapter(Func<bool> canPublish, Action<string> publish)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        internal void Start(LeagueDashboardModule owner)
        {
            if (!_canPublish()) return;
            Cancel();
            var session = owner == null ? null : owner.UnifiedPresenceSession;
            if (session == null) { PostResult("unavailable"); return; }
            _service = owner.CreateUnifiedProfileCustomizationService();
            _session = session;
            ReadOptions(owner, "champions", 0);
        }

        internal void LoadSkins(LeagueDashboardModule owner, string raw)
        {
            if (!_canPublish() || !TryProfileId(raw, 10000, _champions))
            { PostResult("invalid"); return; }
            if (_cancellation != null) { PostResult("busy"); return; }
            _skins.Clear();
            ReadOptions(owner, "skins", int.Parse(raw, CultureInfo.InvariantCulture));
        }

        private async void ReadOptions(LeagueDashboardModule owner, string kind, int championId)
        {
            if (!_canPublish()) return;
            if (owner == null || !SessionCurrent(owner)) { PostResult("session-changed"); return; }
            if (_cancellation != null) return;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _cancellation = source;
            var generation = _generation;
            PostResult("loading");
            try
            {
                if (kind == "champions")
                {
                    var result = await _service.LoadChampionsAsync(source.Token);
                    if (!Current(owner, source, generation))
                    {
                        if (Current(owner, source, generation, false)) PostResult("session-changed");
                        return;
                    }
                    var items = result.Where(item => item.Id > 0 && item.Id <= 10000).Take(300).ToArray();
                    _champions = new HashSet<int>(items.Select(item => item.Id));
                    PostOptions(kind, items.Select(item => new { id = item.Id, name = item.Name }).ToArray());
                }
                else if (kind == "skins")
                {
                    var result = await _service.LoadSkinsAsync(championId, source.Token);
                    if (!Current(owner, source, generation))
                    {
                        if (Current(owner, source, generation, false)) PostResult("session-changed");
                        return;
                    }
                    var items = result.Where(item => item.Id > 0 && item.Id <= 99999999).Take(200).ToArray();
                    _skins = new HashSet<int>(items.Select(item => item.Id));
                    PostOptions(kind, items.Select(item => new { id = item.Id, name = item.Name }).ToArray());
                }
            }
            catch (OperationCanceledException)
            {
                if (Current(owner, source, generation, false)) PostResult("unavailable");
            }
            catch (Exception exception)
            {
                AppLog.Info("Profile customization catalog skipped: " + exception.GetType().Name);
                if (Current(owner, source, generation, false)) PostResult("failed");
            }
            finally
            {
                if (ReferenceEquals(source, _cancellation)) _cancellation = null;
                source.Dispose();
            }
        }

        internal void ApplySkin(LeagueDashboardModule owner, string raw)
        {
            if (!_canPublish() || !TryProfileId(raw, 99999999, _skins))
            { PostResult("invalid"); return; }
            ApplyChange(owner, "skin", int.Parse(raw, CultureInfo.InvariantCulture));
        }

        internal void ApplyAction(LeagueDashboardModule owner, string action)
        {
            if (action != "crest" && action != "tokens" && action != "emotes" && action != "banner")
            { PostResult("invalid"); return; }
            ApplyChange(owner, action, 0);
        }

        private async void ApplyChange(LeagueDashboardModule owner, string action, int skinId)
        {
            if (!_canPublish() || owner == null || !SessionCurrent(owner) || _cancellation != null)
            { PostResult("session-changed"); return; }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _cancellation = source;
            var generation = _generation;
            PostResult("loading");
            try
            {
                string status;
                switch (action)
                {
                    case "skin":
                        var skin = await _service.ApplyBackgroundSkinAsync(skinId, source.Token);
                        status = skin == null ? "unavailable" : skin.Status;
                        break;
                    case "crest":
                        var crest = await owner.CreateUnifiedRegaliaService().RemovePrestigeCrestAsync(source.Token);
                        status = crest == null ? "unavailable" : crest.Status;
                        break;
                    case "tokens":
                        var tokens = await owner.CreateUnifiedChallengePreferencesService().ClearChallengeTokensAsync(source.Token);
                        status = tokens == null ? "unavailable" : tokens.Status;
                        break;
                    case "emotes":
                        var emotes = await owner.CreateUnifiedEmoteService().ClearEmotesAsync(source.Token);
                        status = emotes == null ? "unavailable" : emotes.Status;
                        break;
                    case "banner":
                        var banner = await owner.CreateUnifiedChallengePreferencesService().ApplyLastSeasonBannerAsync(source.Token);
                        status = banner == null ? "unavailable" : banner.Status;
                        break;
                    default: return;
                }
                if (Current(owner, source, generation)) PostResult(status);
                else if (Current(owner, source, generation, false)) PostResult("session-changed");
            }
            catch (OperationCanceledException)
            {
                if (Current(owner, source, generation, false)) PostResult("unavailable");
            }
            catch (Exception exception)
            {
                AppLog.Info("Profile customization failed: " + exception.GetType().Name);
                if (Current(owner, source, generation, false)) PostResult("failed");
            }
            finally
            {
                if (ReferenceEquals(source, _cancellation)) _cancellation = null;
                source.Dispose();
            }
        }

        private bool SessionCurrent(LeagueDashboardModule owner)
        {
            return _session != null && owner != null && owner.UnifiedPresenceSession != null &&
                LeagueEncounterReplayService.SessionMatches(_session, owner.UnifiedPresenceSession);
        }

        private bool Current(LeagueDashboardModule owner, CancellationTokenSource source,
            int generation, bool checkSession = true)
        {
            return _canPublish() && generation == _generation &&
                ReferenceEquals(source, _cancellation) && (!checkSession || SessionCurrent(owner));
        }

        private static bool TryProfileId(string raw, int max, HashSet<int> allowed)
        {
            int number;
            return !string.IsNullOrWhiteSpace(raw) && raw.Length <= 8 &&
                int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
                number > 0 && number <= max && allowed.Contains(number);
        }

        private void PostOptions<T>(string kind, T[] choices)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(
                    new { type = "profile-customization-options", kind, items = choices }));
        }

        private void PostResult(string status)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(
                    new { type = "profile-customization-result", status }));
        }

        internal void Cancel()
        {
            _generation++;
            _session = null;
            _service = null;
            _champions.Clear();
            _skins.Clear();
            var source = _cancellation;
            _cancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            if (TryProfileId("42", 10000, new HashSet<int>()) ||
                !TryProfileId("42", 10000, new HashSet<int> { 42 }) ||
                TryProfileId("42x", 10000, new HashSet<int> { 42 }))
                throw new InvalidOperationException("Profile customization ID allowlist changed.");
            var messages = 0;
            var adapter = new UnifiedWorkspaceProfileCustomizationAdapter(() => true,
                delegate { messages++; });
            adapter.Start(null);
            adapter.Cancel();
            if (messages != 1)
                throw new InvalidOperationException("Profile customization missing-session message changed.");
        }
    }
}
