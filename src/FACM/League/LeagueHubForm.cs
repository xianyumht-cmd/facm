using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueHubForm : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly Dictionary<string, Func<UiTextCatalog, Form>> _factories;
        private readonly Dictionary<string, FacmNavButton> _sectionButtons = new Dictionary<string, FacmNavButton>(StringComparer.Ordinal);
        private readonly Dictionary<string, FacmPillButton> _viewButtons = new Dictionary<string, FacmPillButton>(StringComparer.Ordinal);
        private readonly Label _headerHint;
        private readonly FlowLayoutPanel _subnav;
        private readonly Panel _content;
        private readonly FlowLayoutPanel _contextActions;
        private Form _currentChild;
        private string _currentViewId;
        private string _currentSectionKey;
        private bool _switching;
        private bool _closing;

        public LeagueHubForm(
            UiTextCatalog ui,
            Func<UiTextCatalog, Form> dashboard,
            Func<UiTextCatalog, Form> player,
            Func<UiTextCatalog, Form> live,
            Func<UiTextCatalog, Form> mayhem,
            Func<UiTextCatalog, Form> recommendation,
            Func<UiTextCatalog, Form> efficiency,
            Func<UiTextCatalog, Form> presence)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _factories = new Dictionary<string, Func<UiTextCatalog, Form>>(StringComparer.Ordinal)
            {
                { LeagueHubNavigation.Dashboard, dashboard ?? throw new ArgumentNullException(nameof(dashboard)) },
                { LeagueHubNavigation.Player, player ?? throw new ArgumentNullException(nameof(player)) },
                { LeagueHubNavigation.Live, live ?? throw new ArgumentNullException(nameof(live)) },
                { LeagueHubNavigation.Mayhem, mayhem ?? throw new ArgumentNullException(nameof(mayhem)) },
                { LeagueHubNavigation.Recommendation, recommendation ?? throw new ArgumentNullException(nameof(recommendation)) },
                { LeagueHubNavigation.Efficiency, efficiency ?? throw new ArgumentNullException(nameof(efficiency)) },
                { LeagueHubNavigation.Presence, presence ?? throw new ArgumentNullException(nameof(presence)) }
            };

            Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 700);
            MinimumSize = new Size(960, 620);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;
            Padding = new Padding(10);

            var header = new FacmGlassPanel
            {
                Dock = DockStyle.Top,
                Height = 72,
                Radius = 14,
                AccentGlow = true,
                Padding = Padding.Empty
            };

            var eyebrow = new Label
            {
                Text = _ui.AppName + "  ·  " + _ui.Get(UiTextKeys.ShellLeague),
                Location = new Point(20, 8),
                Size = new Size(280, 16),
                ForeColor = FacmDesignSystem.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.7F, FontStyle.Bold)
            };
            var title = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Title),
                Location = new Point(18, 22),
                Size = new Size(420, 27),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 15.5F, FontStyle.Bold)
            };
            _headerHint = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Hint),
                Location = new Point(20, 49),
                Size = new Size(580, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8.1F)
            };

            _contextActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 438,
                Height = 54,
                Padding = new Padding(0, 18, 14, 0),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                AutoScroll = false
            };

            header.Controls.Add(_contextActions);
            header.Controls.Add(eyebrow);
            header.Controls.Add(title);
            header.Controls.Add(_headerHint);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 10, 0, 0)
            };

            var sidebar = new FacmGlassPanel
            {
                Dock = DockStyle.Left,
                Width = 138,
                Radius = 12,
                Padding = new Padding(8, 12, 8, 10)
            };
            sidebar.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Title),
                Location = new Point(12, 12),
                Size = new Size(100, 18),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Bold)
            });
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.SectionMatchHint, 38);
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionRecommend, LeagueHubUiTextKeys.SectionRecommendHint, 84);
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionEfficiency, LeagueHubUiTextKeys.SectionEfficiencyHint, 130);

            var mainShell = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(10, 0, 0, 0)
            };
            var mainCard = new FacmGlassPanel
            {
                Dock = DockStyle.Fill,
                Radius = 12,
                Padding = Padding.Empty
            };

            _subnav = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(14, 7, 10, 5),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                AutoScroll = false
            };
            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FacmDesignSystem.CanvasRaised,
                Padding = Padding.Empty
            };

            mainCard.Controls.Add(_content);
            mainCard.Controls.Add(_subnav);
            mainShell.Controls.Add(mainCard);
            body.Controls.Add(mainShell);
            body.Controls.Add(sidebar);

            Controls.Add(body);
            Controls.Add(header);

            Resize += delegate { UpdateResponsiveChrome(); };
            Shown += delegate
            {
                UpdateResponsiveChrome();
                ShowSection(LeagueHubUiTextKeys.SectionMatch);
            };
            FormClosing += HandleHubClosing;
        }

        internal string CurrentViewIdForSmokeTest
        {
            get { return _currentViewId ?? string.Empty; }
        }

        internal string CurrentSectionForSmokeTest
        {
            get { return _currentSectionKey ?? string.Empty; }
        }

        private void AddSectionButton(Panel sidebar, string sectionKey, string hintKey, int top)
        {
            var button = new FacmNavButton
            {
                Text = LeagueHubText.Get(_ui, sectionKey),
                Location = new Point(8, top),
                Size = new Size(122, 38)
            };
            button.Click += delegate { ShowSection(sectionKey); };
            button.MouseEnter += delegate { _headerHint.Text = LeagueHubText.Get(_ui, hintKey); };
            button.MouseLeave += delegate { UpdateHeaderHint(_currentSectionKey); };
            sidebar.Controls.Add(button);
            _sectionButtons[sectionKey] = button;
        }

        private void ShowSection(string sectionKey)
        {
            if (_closing || IsDisposed || string.IsNullOrWhiteSpace(sectionKey)) return;
            var views = LeagueHubNavigation.ViewsForSection(sectionKey);
            if (views == null || views.Count == 0) return;

            _currentSectionKey = sectionKey;
            RebuildSubnav(views);
            UpdateSectionSelection();
            UpdateHeaderHint(sectionKey);

            var target = views.FirstOrDefault(item => string.Equals(item.Id, _currentViewId, StringComparison.Ordinal)) ?? views[0];
            ShowView(target.Id, false);
        }

        private void UpdateHeaderHint(string sectionKey)
        {
            if (_headerHint == null || _headerHint.IsDisposed) return;
            if (string.Equals(sectionKey, LeagueHubUiTextKeys.SectionMatch, StringComparison.Ordinal))
                _headerHint.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.SectionMatchHint);
            else if (string.Equals(sectionKey, LeagueHubUiTextKeys.SectionRecommend, StringComparison.Ordinal))
                _headerHint.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.SectionRecommendHint);
            else if (string.Equals(sectionKey, LeagueHubUiTextKeys.SectionEfficiency, StringComparison.Ordinal))
                _headerHint.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.SectionEfficiencyHint);
            else
                _headerHint.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Hint);
        }

        private void RebuildSubnav(IReadOnlyList<LeagueHubViewDefinition> views)
        {
            while (_subnav.Controls.Count > 0)
            {
                var control = _subnav.Controls[0];
                _subnav.Controls.RemoveAt(0);
                control.Dispose();
            }
            _viewButtons.Clear();

            if (views == null || views.Count <= 1)
            {
                _subnav.Visible = false;
                _subnav.Height = 0;
                return;
            }

            _subnav.Visible = true;
            _subnav.Height = 44;
            foreach (var definition in views)
            {
                var captured = definition;
                var button = new FacmPillButton
                {
                    Text = ResolveViewText(captured.TextKey),
                    AutoSize = false,
                    Size = new Size(104, 30),
                    Margin = new Padding(0, 0, 7, 0)
                };
                button.Click += delegate { ShowView(captured.Id, true); };
                _subnav.Controls.Add(button);
                _viewButtons[captured.Id] = button;
            }
            UpdateViewSelection();
        }

        private string ResolveViewText(string textKey)
        {
            if (LeagueHubText.DefaultsForSmokeTest().ContainsKey(textKey))
                return LeagueHubText.Get(_ui, textKey);
            return _ui.Get(textKey);
        }

        private void ShowView(string viewId, bool ensureSection)
        {
            if (_closing || IsDisposed || string.IsNullOrWhiteSpace(viewId)) return;
            var definition = LeagueHubNavigation.Views.FirstOrDefault(item => string.Equals(item.Id, viewId, StringComparison.Ordinal));
            if (definition == null) return;

            if (ensureSection && !string.Equals(_currentSectionKey, definition.SectionKey, StringComparison.Ordinal))
            {
                _currentSectionKey = definition.SectionKey;
                RebuildSubnav(LeagueHubNavigation.ViewsForSection(definition.SectionKey));
                UpdateSectionSelection();
                UpdateHeaderHint(definition.SectionKey);
            }
            if (string.Equals(_currentViewId, viewId, StringComparison.Ordinal) && _currentChild != null && !_currentChild.IsDisposed)
            {
                UpdateContextActions();
                return;
            }

            Func<UiTextCatalog, Form> factory;
            if (!_factories.TryGetValue(viewId, out factory)) return;

            CloseCurrentChild();

            Form child = null;
            try
            {
                child = factory(_ui);
                if (child == null) throw new InvalidOperationException("LOL helper view factory returned no form: " + viewId);

                FacmDesignSystem.ApplyLeagueSurface(child);
                child.TopLevel = false;
                child.FormBorderStyle = FormBorderStyle.None;
                child.Dock = DockStyle.Fill;
                child.ShowInTaskbar = false;
                child.TopMost = false;
                child.StartPosition = FormStartPosition.Manual;
                child.MinimumSize = Size.Empty;
                child.MaximumSize = Size.Empty;
                child.FormClosing += HandleEmbeddedClosing;

                _content.Controls.Add(child);
                _currentChild = child;
                _currentViewId = viewId;
                _currentSectionKey = definition.SectionKey;
                UpdateSectionSelection();
                UpdateViewSelection();
                UpdateHeaderHint(_currentSectionKey);
                UpdateContextActions();
                child.Show();
            }
            catch
            {
                if (child != null)
                {
                    child.FormClosing -= HandleEmbeddedClosing;
                    child.Dispose();
                }
                _currentChild = null;
                _currentViewId = null;
                UpdateViewSelection();
                UpdateContextActions();
                throw;
            }
        }

        private void UpdateResponsiveChrome()
        {
            if (_contextActions == null || _contextActions.IsDisposed) return;
            _contextActions.Visible = ClientSize.Width >= 1080;
        }

        private void UpdateContextActions()
        {
            if (_contextActions == null || _contextActions.IsDisposed) return;

            while (_contextActions.Controls.Count > 0)
            {
                var control = _contextActions.Controls[0];
                _contextActions.Controls.RemoveAt(0);
                control.Dispose();
            }

            if (string.IsNullOrWhiteSpace(_currentViewId)) return;

            _contextActions.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextTitle),
                Size = new Size(58, 28),
                Margin = new Padding(0, 0, 5, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.8F, FontStyle.Bold)
            });

            foreach (var related in LeagueHubNavigation.RelatedViews(_currentViewId))
            {
                var captured = related;
                var button = new FacmPillButton
                {
                    Text = ResolveViewText(captured.TextKey),
                    Size = new Size(78, 28),
                    Margin = new Padding(0, 0, 5, 0),
                    Font = new Font(Font.FontFamily, 7.7F, FontStyle.Bold)
                };
                button.Click += delegate { ShowView(captured.Id, true); };
                _contextActions.Controls.Add(button);
            }
        }

        private void HandleEmbeddedClosing(object sender, FormClosingEventArgs e)
        {
            if (_closing || _switching) return;
            e.Cancel = true;
            BeginInvoke(new Action(Close));
        }

        private void HandleHubClosing(object sender, FormClosingEventArgs e)
        {
            _closing = true;
            CloseCurrentChild();
        }

        private void CloseCurrentChild()
        {
            var child = _currentChild;
            _currentChild = null;
            _currentViewId = null;
            if (child == null) return;

            _switching = true;
            try
            {
                _content.Controls.Remove(child);
                if (!child.IsDisposed)
                {
                    child.Close();
                    if (!child.IsDisposed) child.Dispose();
                }
            }
            finally
            {
                _switching = false;
            }
        }

        private void UpdateSectionSelection()
        {
            foreach (var pair in _sectionButtons)
                pair.Value.Selected = string.Equals(pair.Key, _currentSectionKey, StringComparison.Ordinal);
        }

        private void UpdateViewSelection()
        {
            foreach (var pair in _viewButtons)
                pair.Value.Selected = string.Equals(pair.Key, _currentViewId, StringComparison.Ordinal);
        }
    }
}
