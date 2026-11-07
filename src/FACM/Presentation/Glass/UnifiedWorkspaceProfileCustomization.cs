using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private CancellationTokenSource _profileCustomizationCancellation;
        private int _profileCustomizationGeneration;
        private LeagueClientSession _profileCustomizationSession;
        private LeagueProfileCustomizationService _profileCustomizationService;
        private HashSet<int> _profileChampions = new HashSet<int>();
        private HashSet<int> _profileSkins = new HashSet<int>();

        internal void OpenProfileCustomizationPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("presence-advanced");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartProfileCustomizationRead();
        }

        internal void StartProfileCustomizationRead()
        {
            if (_navigation.Current != "presence-advanced") return;
            CancelProfileCustomization();
            var owner = LeagueHubUiBridge.UnifiedDashboard;
            var session = owner == null ? null : owner.UnifiedPresenceSession;
            if (session == null) { PostProfileResult("unavailable"); return; }
            _profileCustomizationService = owner.CreateUnifiedProfileCustomizationService();
            _profileCustomizationSession = session;
            ReadProfileOptions("champions", 0);
        }

        internal void LoadProfileSkins(string raw)
        {
            if (_navigation.Current != "presence-advanced" || !TryProfileId(raw, 10000, _profileChampions))
            {
                PostProfileResult("invalid"); return;
            }
            if (_profileCustomizationCancellation != null)
            {
                PostProfileResult("busy"); return;
            }
            int championId = int.Parse(raw, CultureInfo.InvariantCulture);
            _profileSkins.Clear();
            ReadProfileOptions("skins", championId);
        }

        private async void ReadProfileOptions(string kind, int championId)
        {
            if (_navigation.Current != "presence-advanced") return;
            var owner = LeagueHubUiBridge.UnifiedDashboard;
            if (owner == null || !ProfileSessionCurrent(owner)) { PostProfileResult("session-changed"); return; }
            if (_profileCustomizationCancellation != null) return;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _profileCustomizationCancellation = source;
            var gen = _profileCustomizationGeneration;
            PostProfileResult("loading");
            try
            {
                if (kind == "champions")
                {
                    var result = await _profileCustomizationService.LoadChampionsAsync(source.Token);
                    if (!ProfileRequestCurrent(owner, source, gen))
                    {
                        if (ProfileRequestCurrent(owner, source, gen, false))
                            PostProfileResult("session-changed");
                        return;
                    }
                    var items = result.Where(item => item.Id > 0 && item.Id <= 10000)
                        .Take(300).ToArray();
                    _profileChampions = new HashSet<int>(items.Select(item => item.Id));
                    PostProfileOptions(kind, items.Select(item => new { id = item.Id, name = item.Name }).ToArray());
                }
                else if (kind == "skins")
                {
                    var result = await _profileCustomizationService.LoadSkinsAsync(championId, source.Token);
                    if (!ProfileRequestCurrent(owner, source, gen))
                    {
                        if (ProfileRequestCurrent(owner, source, gen, false))
                            PostProfileResult("session-changed");
                        return;
                    }
                    var items = result.Where(item => item.Id > 0 && item.Id <= 99999999)
                        .Take(200).ToArray();
                    _profileSkins = new HashSet<int>(items.Select(item => item.Id));
                    PostProfileOptions(kind, items.Select(item => new { id = item.Id, name = item.Name }).ToArray());
                }
            }
            catch (OperationCanceledException)
            {
                if (ProfileRequestCurrent(owner, source, gen, false)) PostProfileResult("unavailable");
            }
            catch (Exception error)
            {
                AppLog.Info("Profile customization catalog skipped: " + error.GetType().Name);
                if (ProfileRequestCurrent(owner, source, gen, false)) PostProfileResult("failed");
            }
            finally
            {
                if (ReferenceEquals(source, _profileCustomizationCancellation))
                    _profileCustomizationCancellation = null;
                source.Dispose();
            }
        }

        internal void ApplyProfileSkin(string raw)
        {
            if (_navigation.Current != "presence-advanced" || !TryProfileId(raw, 99999999, _profileSkins))
            {
                PostProfileResult("invalid"); return;
            }
            var skinId = int.Parse(raw, CultureInfo.InvariantCulture);
            ApplyProfileChange("skin", skinId);
        }

        internal void ApplyProfileCustomizationAction(string action)
        {
            if (action != "crest" && action != "tokens" && action != "emotes" && action != "banner")
            {
                PostProfileResult("invalid"); return;
            }
            ApplyProfileChange(action, 0);
        }

        private async void ApplyProfileChange(string action, int skinId)
        {
            var owner = LeagueHubUiBridge.UnifiedDashboard;
            if (_navigation.Current != "presence-advanced" || owner == null ||
                !ProfileSessionCurrent(owner) || _profileCustomizationCancellation != null)
            {
                PostProfileResult("session-changed"); return;
            }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _profileCustomizationCancellation = source;
            var gen = _profileCustomizationGeneration;
            PostProfileResult("loading");
            try
            {
                string status;
                switch (action)
                {
                    case "skin":
                        var skin = await _profileCustomizationService.ApplyBackgroundSkinAsync(skinId, source.Token);
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
                if (ProfileRequestCurrent(owner, source, gen)) PostProfileResult(status);
                else if (ProfileRequestCurrent(owner, source, gen, false)) PostProfileResult("session-changed");
            }
            catch (OperationCanceledException)
            {
                if (ProfileRequestCurrent(owner, source, gen, false)) PostProfileResult("unavailable");
            }
            catch (Exception error)
            {
                AppLog.Info("Profile customization failed: " + error.GetType().Name);
                if (ProfileRequestCurrent(owner, source, gen, false)) PostProfileResult("failed");
            }
            finally
            {
                if (ReferenceEquals(source, _profileCustomizationCancellation))
                    _profileCustomizationCancellation = null;
                source.Dispose();
            }
        }

        private bool ProfileSessionCurrent(LeagueDashboardModule owner)
        {
            var session = _profileCustomizationSession;
            return session != null && owner != null && owner.UnifiedPresenceSession != null &&
                LeagueEncounterReplayService.SessionMatches(session, owner.UnifiedPresenceSession);
        }

        private bool ProfileRequestCurrent(LeagueDashboardModule owner,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                _navigation.Current == "presence-advanced" &&
                generation == _profileCustomizationGeneration &&
                ReferenceEquals(source, _profileCustomizationCancellation) &&
                (!checkSession || ProfileSessionCurrent(owner));
        }

        private static bool TryProfileId(string raw, int max, HashSet<int> allowed)
        {
            int number;
            return !string.IsNullOrWhiteSpace(raw) && raw.Length <= 8 &&
                int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
                number > 0 && number <= max && allowed.Contains(number);
        }

        private void PostProfileOptions<T>(string kind, T[] choices)
        {
            if (!_ready || _closing || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "presence-advanced") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(
                new { type = "profile-customization-options", kind, items = choices }));
        }

        private void PostProfileResult(string status)
        {
            if (!_ready || _closing || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "presence-advanced") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(
                new { type = "profile-customization-result", status }));
        }

        internal void CancelProfileCustomization()
        {
            _profileCustomizationGeneration++;
            _profileCustomizationSession = null;
            _profileCustomizationService = null;
            _profileChampions.Clear();
            _profileSkins.Clear();
            var source = _profileCustomizationCancellation;
            _profileCustomizationCancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }
    }
}
