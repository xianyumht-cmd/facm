using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FACM.League;

namespace FACM.Presentation.Glass
{
    // Single owner for web-shell navigation. Only known routes may reach native services.
    internal sealed class UnifiedWorkspaceNavigation
    {
        private readonly List<string> _history = new List<string>();
        private string _current = string.Empty;

        internal string Current { get { return _current; } }
        internal bool IsHome { get { return _current.Length == 0; } }
        internal int Depth { get { return _history.Count; } }

        internal static bool IsAllowed(string route)
        {
            return UnifiedWorkspaceShardCatalog.HasRoute(route);
        }

        internal bool Open(string route)
        {
            if (!IsAllowed(route)) return false;
            if (string.Equals(_current, route, StringComparison.Ordinal)) return true;
            if (_history.Count >= 16) _history.RemoveAt(0);
            _history.Add(_current);
            _current = route;
            return true;
        }

        internal bool Back()
        {
            if (_history.Count == 0)
            {
                if (IsHome) return false;
                _current = string.Empty;
                return true;
            }
            var last = _history.Count - 1;
            _current = _history[last];
            _history.RemoveAt(last);
            return true;
        }

        internal void Home()
        {
            _history.Clear();
            _current = string.Empty;
        }

        internal static Size ResolveClientSize(Rectangle workArea, bool detail)
        {
            return ResolveClientSize(workArea, detail, 1F);
        }

        internal static Size ResolveClientSize(Rectangle workArea, string route, float dpiScale)
        {
            int width, height;
            switch (route ?? string.Empty)
            {
                case "": width = 400; height = 360; break;
                case "more": width = 420; height = 540; break;
                case "bench": width = 500; height = 540; break;
                case "mayhem": width = 570; height = 640; break;
                case "recommendation": width = 570; height = 650; break;
                case "profile-customization": width = 560; height = 620; break;
                case "client-status": width = 420; height = 340; break;
                case "appearance": width = 520; height = 560; break;
                case "efficiency": width = 480; height = 520; break;
                case "efficiency-advanced": width = 560; height = 660; break;
                case "quick-messages": width = 560; height = 650; break;
                case "auto-selection": width = 500; height = 550; break;
                case "postgame-settings": width = 470; height = 420; break;
                case "dashboard": width = 520; height = 520; break;
                case "dashboard-advanced": width = 580; height = 650; break;
                case "presence-advanced": width = 580; height = 670; break;
                case "player-advanced": width = 690; height = 750; break;
                case "matchmaking-settings": width = 530; height = 580; break;
                case "presence": width = 510; height = 610; break;
                case "cloud-sync": width = 530; height = 620; break;
                case "updates": width = 540; height = 620; break;
                case "repair": width = 500; height = 500; break;
                case "cleanup": width = 550; height = 650; break;
                case "profile": width = 540; height = 620; break;
                case "live": width = 580; height = 650; break;
                case "live-advanced": width = 560; height = 630; break;
                case "player": width = 570; height = 650; break;
                default: width = 1000; height = 740; break;
            }
            var scale = Math.Max(1F, Math.Min(2.5F, dpiScale));
            return new Size(
                Math.Min((int)Math.Round(width * scale), Math.Max(260, workArea.Width - 24)),
                Math.Min((int)Math.Round(height * scale), Math.Max(260, workArea.Height - 24)));
        }

        internal static Size ResolveClientSize(Rectangle workArea, bool detail, float dpiScale)
        {
            // Preserve 400x360 logical-pixel home proportions across 100–200% DPI.
            var scale = Math.Max(1F, Math.Min(2.5F, dpiScale));
            var maxWidth = Math.Max(260, workArea.Width - 24);
            var maxHeight = Math.Max(260, workArea.Height - 24);
            return new Size(
                Math.Min((int)Math.Round((detail ? 1000 : 400) * scale), maxWidth),
                Math.Min((int)Math.Round((detail ? 740 : 360) * scale), maxHeight));
        }

        internal static void ValidateForSmokeTest()
        {
            var router = new UnifiedWorkspaceNavigation();
            if (!router.IsHome || router.Back() || router.Open(null) || router.Open("shell-exec"))
                throw new InvalidOperationException("Unified shell must reject unknown navigation.");
            var routes = LeagueHubNavigation.Views.Select(view => view.Id).ToArray();
            if (routes.Length != 9 || routes.Any(route => !router.Open(route)))
                throw new InvalidOperationException("Unified shell lost a League functional route.");
            for (var index = routes.Length - 1; index >= 0; index--)
            {
                if (router.Current != routes[index] || !router.Back())
                    throw new InvalidOperationException("Unified shell back stack is not stable.");
            }
            if (!router.IsHome || router.Back() || !router.Open("cleanup") || !router.Back() ||
                !router.Open("appearance") || !router.Back() ||
                !router.Open("presence-advanced") || !router.Back() ||
                !router.Open("live-advanced") || !router.Back() ||
                !router.Open("player-advanced") || !router.Back() ||
                !router.Open("client-status") || !router.Back() ||
                !router.IsHome || !router.Open("efficiency-advanced") || !router.Back() ||
                !router.Open("matchmaking-settings") || !router.Back() ||
                !router.Open("auto-selection") || !router.Back() ||
                !router.Open("postgame-settings") || !router.Back() ||
                !router.Open("dashboard-advanced") || !router.Back())
                throw new InvalidOperationException("Unified shell cleanup navigation did not return home.");
            foreach (var scale in new[] { 1F, 1.25F, 1.5F, 2F })
            {
                var narrowWorkArea = new Rectangle(0, 0, 420, 620);
                foreach (var id in new[] { "", "live-advanced", "cleanup", "quick-messages", "efficiency-advanced" })
                {
                    var bounded = ResolveClientSize(narrowWorkArea, id, scale);
                    if (bounded.Width > narrowWorkArea.Width - 24 ||
                        bounded.Height > narrowWorkArea.Height - 24 ||
                        bounded.Width < 260 || bounded.Height < 260)
                        throw new InvalidOperationException("Unified window exceeds DPI-clamped work area: " + id);
                }
            }
            for (var index = 0; index < 100; index++) router.Open(routes[index % routes.Length]);
            if (router.Depth > 16) throw new InvalidOperationException("Unified shell history is unbounded.");
            router.Home();
            var normal = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), false);
            var detail = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), true);
            var small = ResolveClientSize(new Rectangle(0, 0, 720, 580), true);
            var highDpiHome = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), false, 2F);
            var appearance = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), "appearance", 1F);
            var queue = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), "matchmaking-settings", 1F);
            var tiny = ResolveClientSize(new Rectangle(0, 0, 720, 580), "auto-selection", 1.5F);
            var browse = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), "more", 1F);
            var repairs = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), "repair", 1F);
            var mayhem = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), "mayhem", 1F);
            if (normal != new Size(400, 360) || detail != new Size(1000, 740) ||
                highDpiHome != new Size(800, 720) ||
                appearance != new Size(520, 560) ||
                queue != new Size(530, 580) ||
                browse != new Size(420, 540) ||
                repairs != new Size(500, 500) || mayhem != new Size(570, 640) ||
                tiny.Width > 696 || tiny.Height > 556 ||
                small.Width > 696 || small.Height > 556)
                throw new InvalidOperationException("Unified shell size must clamp to the visible work area.");
        }
    }
}
