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
        private const int NormalSidebarWidth = 194;
        private const int CompactSidebarWidth = 172;
        private const int SidebarCompactThreshold = 980;

        private static readonly string[] VisibleRoutes =
        {
            LeagueHubNavigation.Dashboard,
            LeagueHubNavigation.Player,
            LeagueHubNavigation.Live,
            LeagueHubNavigation.Recommendation,
            LeagueHubNavigation.Mayhem,
            LeagueHubNavigation.Efficiency,
            LeagueHubNavigation.Presence,
            LeagueHubNavigation.Repair,
            LeagueHubNavigation.Profile
        };

        private readonly UiTextCatalog _ui;
        private readonly Dictionary<string, Func<UiTextCatalog, Form>> _factories;
        private readonly Dictionary<string, FacmNavButton> _viewButtons =
            new Dictionary<string, FacmNavButton>(StringComparer.Ordinal);
        private readonly FacmGlassPanel _sidebar;
        private readonly FlowLayoutPanel _navigation;
        private readonly Panel _content;
        private readonly Label _connectionLabel;
        private readonly Label _phaseLabel;
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
            Func<UiTextCatalog, Form> profile,
            Func<UiTextCatalog, Form> recommendation,
            Func<UiTextCatalog, Form> efficiency,
            Func<UiTextCatalog, Form> repair,
            Func<UiTextCatalog, Form> presence)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _factories = new Dictionary<string, Func<UiTextCatalog, Form>>(StringComparer.Ordinal)
            {
                { LeagueHubNavigation.Dashboard, dashboard ?? throw new ArgumentNullException(nameof(dashboard)) },
                { LeagueHubNavigation.Player, player ?? throw new ArgumentNullException(nameof(player)) },
                { LeagueHubNavigation.Live, live ?? throw new ArgumentNullException(nameof(live)) },
                { LeagueHubNavigation.Mayhem, mayhem ?? throw new ArgumentNullException(nameof(mayhem)) },
                { LeagueHubNavigation.Profile, profile ?? throw new ArgumentNullException(nameof(profile)) },
                { LeagueHubNavigation.Recommendation, recommendation ?? throw new ArgumentNullException(nameof(recommendation)) },
                { LeagueHubNavigation.Efficiency, efficiency ?? throw new ArgumentNullException(nameof(efficiency)) },
                { LeagueHubNavigation.Repair, repair ?? throw new ArgumentNullException(nameof(repair)) },
                { LeagueHubNavigation.Presence, presence ?? throw new ArgumentNullException(nameof(presence)) }
            };

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1120, 640);
            MinimumSize = new Size(900, 580);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            DoubleBuffered = true;
            Padding = new Padding(8);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = Padding.Empty
            };

            _sidebar = new FacmGlassPanel
            {
                Dock = DockStyle.Left,
                Width = NormalSidebarWidth,
                Radius = FacmDesignSystem.CardRadius,
                Padding = new Padding(8)
            };

            var brand = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Title),
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(10, 8, 0, 0),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
            };

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 59,
                BackColor = Color.Transparent,
                Padding = Padding.Empty
            };
            var divider = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = FacmDesignSystem.BorderSoft,
                TabStop = false
            };
            _connectionLabel = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDisconnected),
                Location = new Point(8, 9),
                Size = new Size(NormalSidebarWidth - 28, 19),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8F, FontStyle.Bold)
            };
            _phaseLabel = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextPhasePrefix) + " · " +
                    LeagueHubText.Activity(_ui, FACM.Performance.LeagueActivityLevel.None),
                Location = new Point(8, 30),
                Size = new Size(NormalSidebarWidth - 28, 19),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8F)
            };
            footer.Controls.Add(_connectionLabel);
            footer.Controls.Add(_phaseLabel);
            footer.Controls.Add(divider);

            _navigation = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 3, 0, 4),
                Margin = Padding.Empty
            };
            AddGroup(LeagueHubUiTextKeys.SectionMatch,
                LeagueHubNavigation.Dashboard, LeagueHubNavigation.Player, LeagueHubNavigation.Live);
            AddGroup(LeagueHubUiTextKeys.SectionRecommend,
                LeagueHubNavigation.Recommendation, LeagueHubNavigation.Mayhem);
            AddGroup(LeagueHubUiTextKeys.SectionEfficiency,
                LeagueHubNavigation.Efficiency, LeagueHubNavigation.Presence, LeagueHubNavigation.Repair);
            AddGroup(null, LeagueHubNavigation.Profile);

            _sidebar.Controls.Add(_navigation);
            _sidebar.Controls.Add(footer);
            _sidebar.Controls.Add(brand);

            var mainShell = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 0, 0, 0)
            };
            var mainCard = new FacmGlassPanel
            {
                Dock = DockStyle.Fill,
                Radius = FacmDesignSystem.CardRadius,
                Padding = Padding.Empty
            };
            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FacmDesignSystem.CanvasRaised,
                Padding = Padding.Empty
            };
            mainCard.Controls.Add(_content);
            mainShell.Controls.Add(mainCard);
            body.Controls.Add(mainShell);
            body.Controls.Add(_sidebar);
            Controls.Add(body);

            _navigation.SizeChanged += delegate { ResizeNavigationButtons(); };
            Resize += delegate { UpdateResponsiveChrome(); };
            Shown += delegate
            {
                UpdateResponsiveChrome();
                ShowView(LeagueHubNavigation.Dashboard, true);
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

        internal static int ResolveSidebarWidthForSmokeTest(int clientWidth)
        {
            return clientWidth < SidebarCompactThreshold ? CompactSidebarWidth : NormalSidebarWidth;
        }

        internal static IReadOnlyList<string> VisibleRoutesForSmokeTest()
        {
            return Array.AsReadOnly(VisibleRoutes);
        }

        internal void UpdateGameflowContext(LeagueDashboardPhaseState state)
        {
            if (_connectionLabel.IsDisposed || _phaseLabel.IsDisposed) return;
            if (state == null)
            {
                _connectionLabel.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDisconnected);
                _connectionLabel.ForeColor = FacmDesignSystem.TextMuted;
                _phaseLabel.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextPhasePrefix) + " · " +
                    LeagueHubText.Activity(_ui, FACM.Performance.LeagueActivityLevel.None);
                return;
            }

            if (state.Connected)
            {
                _connectionLabel.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientConnected);
                _connectionLabel.ForeColor = FacmDesignSystem.Success;
            }
            else if (state.ClientProcessDetected || state.GameProcessDetected)
            {
                _connectionLabel.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDetected);
                _connectionLabel.ForeColor = FacmDesignSystem.Warning;
            }
            else
            {
                _connectionLabel.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDisconnected);
                _connectionLabel.ForeColor = FacmDesignSystem.TextMuted;
            }
            _phaseLabel.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextPhasePrefix) + " · " +
                LeagueHubText.Activity(_ui, state.Activity);
        }

        private void AddGroup(string headerKey, params string[] routeIds)
        {
            if (!string.IsNullOrWhiteSpace(headerKey))
            {
                _navigation.Controls.Add(new Label
                {
                    Text = LeagueHubText.Get(_ui, headerKey),
                    Height = 24,
                    Width = 144,
                    Margin = new Padding(0, 11, 0, 1),
                    Padding = new Padding(11, 0, 0, 0),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = FacmDesignSystem.TextMuted,
                    BackColor = Color.Transparent,
                    Font = new Font(Font.FontFamily, 8F, FontStyle.Bold),
                    TabStop = false
                });
            }
            foreach (var routeId in routeIds)
            {
                var definition = LeagueHubNavigation.Views.FirstOrDefault(view =>
                    string.Equals(view.Id, routeId, StringComparison.Ordinal));
                if (definition == null)
                    throw new InvalidOperationException("Unknown workbench navigation route: " + routeId);

                var captured = definition;
                var title = LeagueHubText.Get(_ui, definition.TextKey);
                var button = new FacmNavButton
                {
                    Text = title,
                    Width = 144,
                    Height = 35,
                    Margin = new Padding(0, 0, 0, 3),
                    AccessibleName = title,
                    TabStop = true
                };
                button.Click += delegate { ShowView(captured.Id, true); };
                _navigation.Controls.Add(button);
                _viewButtons[routeId] = button;
            }
        }

        private void UpdateResponsiveChrome()
        {
            if (_sidebar.IsDisposed) return;
            _sidebar.Width = ResolveSidebarWidthForSmokeTest(ClientSize.Width);
            ResizeNavigationButtons();
        }

        private void ResizeNavigationButtons()
        {
            if (_navigation == null || _navigation.IsDisposed) return;
            var targetWidth = Math.Max(112, _navigation.ClientSize.Width - _navigation.Padding.Horizontal - 9);
            foreach (Control control in _navigation.Controls)
            {
                if (control.Width != targetWidth)
                    control.Width = targetWidth;
            }
        }

        private void ShowView(string viewId, bool ensureSection)
        {
            if (_closing || IsDisposed || string.IsNullOrWhiteSpace(viewId)) return;
            var definition = LeagueHubNavigation.Views.FirstOrDefault(view =>
                string.Equals(view.Id, viewId, StringComparison.Ordinal));
            if (definition == null) return;

            _currentSectionKey = definition.SectionKey;
            if (string.Equals(_currentViewId, viewId, StringComparison.Ordinal) &&
                _currentChild != null && !_currentChild.IsDisposed)
            {
                UpdateViewSelection();
                return;
            }

            Func<UiTextCatalog, Form> factory;
            if (!_factories.TryGetValue(viewId, out factory)) return;

            CloseCurrentChild();

            Form child = null;
            try
            {
                child = factory(_ui);
                if (child == null)
                    throw new InvalidOperationException("LOL helper view factory returned no form: " + viewId);

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
                UpdateViewSelection();
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
                throw;
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

        private void UpdateViewSelection()
        {
            foreach (var pair in _viewButtons)
                pair.Value.Selected = string.Equals(pair.Key, _currentViewId, StringComparison.Ordinal);
        }
    }
}
