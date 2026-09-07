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
        private const int DefaultSidebarWidth = 130;
        private const int CompactSidebarWidth = 116;
        private const int SidebarCompactThreshold = 980;
        private const int ContextMinWidth = 212;
        private const int ContextMaxWidth = 248;
        private const int ContextMinContentWidth = 620;
        private const int SubnavMinButtonWidth = 84;
        private const int SubnavMaxButtonWidth = 146;

        private readonly UiTextCatalog _ui;
        private readonly Dictionary<string, Func<UiTextCatalog, Form>> _factories;
        private readonly Dictionary<string, FacmNavButton> _sectionButtons = new Dictionary<string, FacmNavButton>(StringComparer.Ordinal);
        private readonly Dictionary<string, FacmPillButton> _viewButtons = new Dictionary<string, FacmPillButton>(StringComparer.Ordinal);
        private readonly FacmGlassPanel _sidebar;
        private readonly FlowLayoutPanel _subnav;
        private readonly Panel _workspace;
        private readonly Panel _content;
        private readonly FacmGlassPanel _contextDock;
        private FlowLayoutPanel _contextActions;
        private Label _contextCurrent;
        private Label _contextConnection;
        private Label _contextPhase;
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
                Width = DefaultSidebarWidth,
                Radius = FacmDesignSystem.CardRadius,
                Padding = new Padding(7, 10, 7, 8)
            };
            _sidebar.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Title),
                Location = new Point(11, 10),
                Size = new Size(100, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.8F, FontStyle.Bold)
            });
            AddSectionButton(_sidebar, LeagueHubUiTextKeys.SectionMatch, 35);
            AddSectionButton(_sidebar, LeagueHubUiTextKeys.SectionRecommend, 78);
            AddSectionButton(_sidebar, LeagueHubUiTextKeys.SectionEfficiency, 121);

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

            _subnav = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(12, 6, 8, 4),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                AutoScroll = false
            };

            _workspace = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = Padding.Empty
            };
            _contextDock = BuildContextDock();
            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FacmDesignSystem.CanvasRaised,
                Padding = Padding.Empty
            };
            _workspace.Controls.Add(_content);
            _workspace.Controls.Add(_contextDock);

            mainCard.Controls.Add(_workspace);
            mainCard.Controls.Add(_subnav);
            mainShell.Controls.Add(mainCard);
            body.Controls.Add(mainShell);
            body.Controls.Add(_sidebar);
            Controls.Add(body);

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

        internal static int ResolveSidebarWidthForSmokeTest(int clientWidth)
        {
            return clientWidth < SidebarCompactThreshold ? CompactSidebarWidth : DefaultSidebarWidth;
        }

        internal static int ResolveContextDockWidthForSmokeTest(int workspaceWidth)
        {
            if (workspaceWidth <= 0) return ContextMinWidth;
            return Math.Max(ContextMinWidth, Math.Min(ContextMaxWidth, workspaceWidth / 4));
        }

        internal static int ResolveSubnavButtonWidthForSmokeTest(int measuredTextWidth)
        {
            return Math.Max(SubnavMinButtonWidth, Math.Min(SubnavMaxButtonWidth, Math.Max(0, measuredTextWidth) + 28));
        }

        internal static bool ShouldShowContextDockForSmokeTest(string viewId, int workspaceWidth)
        {
            var sparseView = string.Equals(viewId, LeagueHubNavigation.Dashboard, StringComparison.Ordinal) ||
                             string.Equals(viewId, LeagueHubNavigation.Efficiency, StringComparison.Ordinal) ||
                             string.Equals(viewId, LeagueHubNavigation.Repair, StringComparison.Ordinal) ||
                             string.Equals(viewId, LeagueHubNavigation.Presence, StringComparison.Ordinal);
            if (!sparseView) return false;
            var contextWidth = ResolveContextDockWidthForSmokeTest(workspaceWidth);
            return workspaceWidth >= ContextMinContentWidth + contextWidth;
        }

        internal void UpdateGameflowContext(LeagueDashboardPhaseState state)
        {
            if (_contextConnection == null || _contextConnection.IsDisposed) return;

            if (state == null)
            {
                _contextConnection.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDisconnected);
                _contextConnection.ForeColor = FacmDesignSystem.TextMuted;
                _contextPhase.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextPhasePrefix) + " · " + LeagueHubText.Activity(_ui, FACM.Performance.LeagueActivityLevel.None);
                return;
            }

            if (state.Connected)
            {
                _contextConnection.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientConnected);
                _contextConnection.ForeColor = FacmDesignSystem.Success;
            }
            else if (state.ClientProcessDetected || state.GameProcessDetected)
            {
                _contextConnection.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDetected);
                _contextConnection.ForeColor = FacmDesignSystem.Warning;
            }
            else
            {
                _contextConnection.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDisconnected);
                _contextConnection.ForeColor = FacmDesignSystem.TextMuted;
            }

            _contextPhase.Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextPhasePrefix) + " · " + LeagueHubText.Activity(_ui, state.Activity);
        }

        private FacmGlassPanel BuildContextDock()
        {
            var dock = new FacmGlassPanel
            {
                Dock = DockStyle.Right,
                Width = ResolveContextDockWidthForSmokeTest(960),
                Radius = FacmDesignSystem.CardRadius,
                DrawBorder = true,
                Padding = Padding.Empty,
                Visible = false
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 12,
                BackColor = Color.Transparent,
                Padding = new Padding(12, 10, 10, 10),
                Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 152F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));

            layout.Controls.Add(CreateContextLabel(
                LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextTitle),
                FacmDesignSystem.Text,
                10F,
                FontStyle.Bold), 0, 0);

            _contextCurrent = CreateContextLabel(string.Empty, FacmDesignSystem.Accent, 8.2F, FontStyle.Bold);
            _contextCurrent.AutoEllipsis = true;
            layout.Controls.Add(_contextCurrent, 0, 1);

            layout.Controls.Add(CreateContextLabel(
                LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextStatus),
                FacmDesignSystem.TextMuted,
                7.8F,
                FontStyle.Bold), 0, 3);

            _contextConnection = CreateContextLabel(
                LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDisconnected),
                FacmDesignSystem.TextMuted,
                8.2F,
                FontStyle.Bold);
            layout.Controls.Add(_contextConnection, 0, 4);

            _contextPhase = CreateContextLabel(
                LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextPhasePrefix) + " · " + LeagueHubText.Activity(_ui, FACM.Performance.LeagueActivityLevel.None),
                FacmDesignSystem.Text,
                8.2F,
                FontStyle.Regular);
            layout.Controls.Add(_contextPhase, 0, 5);

            layout.Controls.Add(CreateContextLabel(
                LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextQuick),
                FacmDesignSystem.TextMuted,
                7.8F,
                FontStyle.Bold), 0, 7);

            _contextActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            layout.Controls.Add(_contextActions, 0, 8);

            var champHint = CreateContextLabel(
                LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextChampSelectHint),
                FacmDesignSystem.TextMuted,
                7.8F,
                FontStyle.Regular);
            champHint.TextAlign = ContentAlignment.BottomLeft;
            layout.Controls.Add(champHint, 0, 10);

            var contextHint = CreateContextLabel(
                LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextHint),
                FacmDesignSystem.TextMuted,
                7.5F,
                FontStyle.Regular);
            contextHint.TextAlign = ContentAlignment.TopLeft;
            layout.Controls.Add(contextHint, 0, 11);

            dock.Controls.Add(layout);
            return dock;
        }

        private Label CreateContextLabel(string text, Color color, float fontSize, FontStyle style)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, fontSize, style),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void AddSectionButton(Panel sidebar, string sectionKey, int top)
        {
            var button = new FacmNavButton
            {
                Text = LeagueHubText.Get(_ui, sectionKey),
                Location = new Point(7, top),
                Size = new Size(Math.Max(90, sidebar.ClientSize.Width - 14), 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            button.Click += delegate { ShowSection(sectionKey); };
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

            var target = views.FirstOrDefault(item => string.Equals(item.Id, _currentViewId, StringComparison.Ordinal)) ?? views[0];
            ShowView(target.Id, false);
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
            _subnav.Height = 42;
            foreach (var definition in views)
            {
                var captured = definition;
                var button = new FacmPillButton
                {
                    Text = ResolveViewText(captured.TextKey),
                    AutoSize = false,
                    Height = 29,
                    Margin = new Padding(0, 0, 6, 0)
                };
                var measured = TextRenderer.MeasureText(
                    button.Text ?? string.Empty,
                    button.Font,
                    new Size(int.MaxValue, button.Height),
                    TextFormatFlags.NoPadding).Width;
                button.Width = ResolveSubnavButtonWidthForSmokeTest(measured);
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
            }
            if (string.Equals(_currentViewId, viewId, StringComparison.Ordinal) && _currentChild != null && !_currentChild.IsDisposed)
            {
                UpdateContextActions();
                UpdateResponsiveChrome();
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
                UpdateContextActions();
                UpdateResponsiveChrome();
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
                UpdateResponsiveChrome();
                throw;
            }
        }

        private void UpdateResponsiveChrome()
        {
            if (_contextDock == null || _contextDock.IsDisposed || _workspace == null || _workspace.IsDisposed) return;

            _sidebar.Width = ResolveSidebarWidthForSmokeTest(ClientSize.Width);
            foreach (var button in _sectionButtons.Values)
                button.Width = Math.Max(90, _sidebar.ClientSize.Width - 14);

            var workspaceWidth = Math.Max(0, _workspace.ClientSize.Width);
            var contextWidth = ResolveContextDockWidthForSmokeTest(workspaceWidth);
            _contextDock.Width = contextWidth;
            _contextDock.Visible = ShouldShowContextDockForSmokeTest(_currentViewId, workspaceWidth);
            ResizeContextActions();
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

            var current = LeagueHubNavigation.Views.FirstOrDefault(item => string.Equals(item.Id, _currentViewId, StringComparison.Ordinal));
            _contextCurrent.Text = current == null
                ? string.Empty
                : LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextCurrent) + " · " + ResolveViewText(current.TextKey);

            foreach (var related in LeagueHubNavigation.RelatedViews(_currentViewId))
            {
                var captured = related;
                var button = new FacmPillButton
                {
                    Text = ResolveViewText(captured.TextKey),
                    Size = new Size(Math.Max(160, _contextDock.Width - 36), 30),
                    Margin = new Padding(0, 0, 0, 6),
                    Font = new Font(Font.FontFamily, 8F, FontStyle.Bold)
                };
                button.Click += delegate { ShowView(captured.Id, true); };
                _contextActions.Controls.Add(button);
            }
        }

        private void ResizeContextActions()
        {
            if (_contextActions == null || _contextActions.IsDisposed) return;
            var width = Math.Max(160, _contextDock.Width - 36);
            foreach (Control control in _contextActions.Controls)
                control.Width = width;
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
