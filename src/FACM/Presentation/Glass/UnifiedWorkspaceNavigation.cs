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
            return string.Equals(route, "cleanup", StringComparison.Ordinal) ||
                LeagueHubNavigation.Views.Any(item =>
                    string.Equals(item.Id, route, StringComparison.Ordinal));
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
            if (!router.IsHome || router.Back() || !router.Open("cleanup") || !router.Back())
                throw new InvalidOperationException("Unified shell cleanup navigation did not return home.");
            for (var index = 0; index < 100; index++) router.Open(routes[index % routes.Length]);
            if (router.Depth > 16) throw new InvalidOperationException("Unified shell history is unbounded.");
            router.Home();
            var normal = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), false);
            var detail = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), true);
            var small = ResolveClientSize(new Rectangle(0, 0, 720, 580), true);
            var highDpiHome = ResolveClientSize(new Rectangle(0, 0, 1920, 1080), false, 2F);
            if (normal != new Size(400, 360) || detail != new Size(1000, 740) ||
                highDpiHome != new Size(800, 720) ||
                small.Width > 696 || small.Height > 556)
                throw new InvalidOperationException("Unified shell size must clamp to the visible work area.");
        }
    }
}
