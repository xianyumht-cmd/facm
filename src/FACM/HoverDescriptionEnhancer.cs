using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FACM.League;
using FACM.Services;

namespace FACM
{
    internal static class HoverDescriptionEnhancer
    {
        private sealed class Marker { }
        private static readonly ConditionalWeakTable<Control, Marker> Applied = new ConditionalWeakTable<Control, Marker>();

        public static bool ApplyCompactMenu(CompactMenuForm menu)
        {
            if (menu == null || menu.IsDisposed || AlreadyApplied(menu)) return false;

            var ui = UiTextCatalog.Load();
            var defaultHint = ui.Get(UiTextKeys.ShellSimpleHint);
            var footerHint = Descendants(menu).OfType<Label>()
                .FirstOrDefault(label => string.Equals(label.Text, defaultHint, StringComparison.Ordinal));
            if (footerHint == null) return false;

            var changed = false;
            changed |= CompactRow(menu, footerHint, defaultHint,
                ui.Get(UiTextKeys.ShellRepairTools), ui.Get(UiTextKeys.ShellRepairHint));
            changed |= CompactRow(menu, footerHint, defaultHint,
                ui.Get(UiTextKeys.ShellLeague), ui.Get(UiTextKeys.ShellLeagueHint));
            changed |= CompactRow(menu, footerHint, defaultHint,
                ui.Get(UiTextKeys.ShellPersonalization), ui.Get(UiTextKeys.ShellPersonalizationHint));

            // 清理属于会改变本机文件的操作。说明保持常驻，只把同一条说明同步到悬停预览区。
            var cleanupTitle = ui.Get(UiTextKeys.Cleanup);
            var cleanupHint = ui.Get(UiTextKeys.CleanupHint);
            var cleanupRow = FindRow(menu, cleanupTitle, cleanupHint);
            if (cleanupRow != null)
            {
                WireHover(cleanupRow,
                    delegate { footerHint.Text = cleanupTitle + " · " + cleanupHint; },
                    delegate { footerHint.Text = defaultHint; });
            }

            return changed;
        }

        public static bool ApplyLeagueHub(Form form, UiTextCatalog ui)
        {
            if (form == null || form.IsDisposed || ui == null || AlreadyApplied(form)) return false;

            var defaultHint = LeagueHubText.Get(ui, LeagueHubUiTextKeys.Hint);
            var headerHint = Descendants(form).OfType<Label>()
                .FirstOrDefault(label => string.Equals(label.Text, defaultHint, StringComparison.Ordinal));
            if (headerHint == null) return false;

            var sections = new[]
            {
                new[] { LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.SectionMatchHint },
                new[] { LeagueHubUiTextKeys.SectionRecommend, LeagueHubUiTextKeys.SectionRecommendHint },
                new[] { LeagueHubUiTextKeys.SectionEfficiency, LeagueHubUiTextKeys.SectionEfficiencyHint }
            };

            foreach (var section in sections)
            {
                var title = LeagueHubText.Get(ui, section[0]);
                var hint = LeagueHubText.Get(ui, section[1]);
                var combined = title + "\r\n" + hint;
                var button = Descendants(form).OfType<Button>()
                    .FirstOrDefault(item => string.Equals(item.Text, combined, StringComparison.Ordinal));
                if (button == null) continue;

                button.Text = title;
                button.TextAlign = ContentAlignment.MiddleLeft;
                WireHover(button,
                    delegate { headerHint.Text = title + " · " + hint; },
                    delegate { headerHint.Text = defaultHint; });
            }

            var subnav = Descendants(form).OfType<FlowLayoutPanel>().FirstOrDefault();
            if (subnav != null)
            {
                Action<Control> wireSubnav = control =>
                {
                    var button = control as Button;
                    if (button == null) return;
                    var hint = ResolveLeagueViewHint(button.Text, ui);
                    if (string.IsNullOrWhiteSpace(hint)) return;
                    WireHover(button,
                        delegate { headerHint.Text = button.Text + " · " + hint; },
                        delegate { headerHint.Text = defaultHint; });
                };

                foreach (Control control in subnav.Controls) wireSubnav(control);
                subnav.ControlAdded += delegate(object sender, ControlEventArgs e) { wireSubnav(e.Control); };
            }

            return true;
        }

        private static bool CompactRow(
            Control root,
            Label footerHint,
            string defaultHint,
            string title,
            string hint)
        {
            var row = FindRow(root, title, hint);
            if (row == null) return false;

            var titleLabel = row.Controls.OfType<Label>()
                .FirstOrDefault(label => string.Equals(label.Text, title, StringComparison.Ordinal));
            var hintLabel = row.Controls.OfType<Label>()
                .FirstOrDefault(label => string.Equals(label.Text, hint, StringComparison.Ordinal));
            if (titleLabel == null || hintLabel == null) return false;

            hintLabel.Visible = false;
            titleLabel.Top = Math.Max(0, (row.ClientSize.Height - titleLabel.Height) / 2);
            WireHover(row,
                delegate { footerHint.Text = title + " · " + hint; },
                delegate { footerHint.Text = defaultHint; });
            return true;
        }

        private static Control FindRow(Control root, string title, string hint)
        {
            foreach (var control in Descendants(root))
            {
                var labels = control.Controls.OfType<Label>().ToArray();
                if (labels.Any(label => string.Equals(label.Text, title, StringComparison.Ordinal)) &&
                    labels.Any(label => string.Equals(label.Text, hint, StringComparison.Ordinal)))
                    return control;
            }
            return null;
        }

        private static string ResolveLeagueViewHint(string text, UiTextCatalog ui)
        {
            if (string.Equals(text, LeagueHubText.Get(ui, LeagueHubUiTextKeys.Dashboard), StringComparison.Ordinal))
                return ui.Get(UiTextKeys.LeagueDashboardHint);
            if (string.Equals(text, ui.Get(UiTextKeys.LeaguePlayerMenu), StringComparison.Ordinal))
                return ui.Get(UiTextKeys.LeaguePlayerHint);
            if (string.Equals(text, ui.Get(UiTextKeys.LeagueLiveMenu), StringComparison.Ordinal))
                return ui.Get(UiTextKeys.LeagueLiveHint);
            if (string.Equals(text, ui.Get(UiTextKeys.MayhemRanking), StringComparison.Ordinal))
                return LeagueHubText.Get(ui, LeagueHubUiTextKeys.SectionMatchHint);
            if (string.Equals(text, LeagueHubText.Get(ui, LeagueHubUiTextKeys.Recommendation), StringComparison.Ordinal))
                return LeagueHubText.Get(ui, LeagueHubUiTextKeys.SectionRecommendHint);
            if (string.Equals(text, LeagueEfficiencyText.Get(ui, LeagueEfficiencyUiTextKeys.Menu), StringComparison.Ordinal))
                return LeagueHubText.Get(ui, LeagueHubUiTextKeys.SectionEfficiencyHint);
            return string.Empty;
        }

        private static void WireHover(Control root, Action enter, Action leave)
        {
            if (root == null) return;
            EventHandler onEnter = delegate { if (enter != null) enter(); };
            EventHandler onLeave = delegate
            {
                if (root.IsDisposed) return;
                try
                {
                    root.BeginInvoke(new Action(delegate
                    {
                        if (root.IsDisposed) return;
                        var screen = root.RectangleToScreen(root.ClientRectangle);
                        if (!screen.Contains(Cursor.Position) && leave != null) leave();
                    }));
                }
                catch { }
            };

            foreach (var control in SelfAndDescendants(root))
            {
                control.MouseEnter += onEnter;
                control.MouseLeave += onLeave;
            }
        }

        private static IEnumerable<Control> SelfAndDescendants(Control root)
        {
            yield return root;
            foreach (var child in Descendants(root)) yield return child;
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            if (root == null) yield break;
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private static bool AlreadyApplied(Control control)
        {
            Marker marker;
            if (Applied.TryGetValue(control, out marker)) return true;
            Applied.Add(control, new Marker());
            return false;
        }
    }
}
