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
        private readonly FacmGlassPanel _contextDock;
        private readonly FlowLayoutPanel _contextActions;
        private readonly Label _contextCurrent;
        private readonly Label _contextConnection;
        private readonly Label _contextPhase;
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
            ClientSize = new Size(1120, 640);
            MinimumSize = new Size(900, 580);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;
            Padding = new Padding(8);

            var header = new FacmGlassPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                Radius = 12,
                AccentGlow = true,
                Padding = Padding.Empty
            };
            header.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Title),
                Location = new Point(16, 7),
                Size = new Size(150, 20),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold)
            });
            _headerHint = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Hint),
                Location = new Point(17, 28),
                Size = new Size(1040, 17),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.9F)
            };
            header.Controls.Add(_headerHint);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 0)
            };

            var sidebar = new FacmGlassPanel
            {
                Dock = DockStyle.Left,
                Width = 130,
                Radius = 12,
                Padding = new Padding(7, 10, 7, 8)
            };
            sidebar.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Title),
                Location = new Point(11, 10),
                Size = new Size(100, 18),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.8F, FontStyle.Bold)
            });
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.SectionMatchHint, 35);
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionRecommend, LeagueHubUiTextKeys.SectionRecommendHint, 78);
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionEfficiency, LeagueHubUiTextKeys.SectionEfficiencyHint, 121);

            var mainShell = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 0, 0, 0)
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
                Height = 42,
                Padding = new Padding(12, 6, 8, 4),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                AutoScroll = false
            };

            var workspace = new Panel
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
            workspace.Controls.Add(_content);
            workspace.Controls.Add(_contextDock);

            mainCard.Controls.Add(workspace);
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
                _contextConnection.ForeColor = Color.FromArgb(244, 181, 94);
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
                Width = 232,
                Radius = 10,
                DrawBorder = true,
                Padding = Padding.Empty,
                Visible = false
            };
            dock.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextTitle),
                Location = new Point(13, 12),
                Size = new Size(202, 20),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
            });
            _contextCurrent = new Label
            {
                Location = new Point(13, 36),
                Size = new Size(202, 22),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Accent,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8.2F, FontStyle.Bold)
            };
            dock.Controls.Add(_contextCurrent);
            dock.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextStatus),
                Location = new Point(13, 74),
                Size = new Size(202, 20),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.8F, FontStyle.Bold)
            });
            _contextConnection = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextClientDisconnected),
                Location = new Point(13, 99),
                Size = new Size(202, 20),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8.2F, FontStyle.Bold)
            };
            _contextPhase = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextPhasePrefix) + " · " + LeagueHubText.Activity(_ui, FACM.Performance.LeagueActivityLevel.None),
                Location = new Point(13, 123),
                Size = new Size(202, 20),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8.2F)
            };
            dock.Controls.Add(_contextConnection);
            dock.Controls.Add(_contextPhase);
            dock.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextQuick),
                Location = new Point(13, 160),
                Size = new Size(202, 20),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.8F, FontStyle.Bold)
            });
            _contextActions = new FlowLayoutPanel
            {
                Location = new Point(11, 185),
                Size = new Size(210, 164),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent,
                Padding = Padding.Empty
            };
            dock.Controls.Add(_contextActions);
            dock.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextChampSelectHint),
                Location = new Point(13, 365),
                Size = new Size(202, 54),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.8F)
            });
            dock.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.ContextHint),
                Location = new Point(13, 430),
                Size = new Size(202, 62),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 7.5F)
            });
            return dock;
        }

        private void AddSectionButton(Panel sidebar, string sectionKey, string hintKey, int top)
        {
            var button = new FacmNavButton
            {
                Text = LeagueHubText.Get(_ui, sectionKey),
                Location = new Point(7, top),
                Size = new Size(116, 36)
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
            _subnav.Height = 42;
            foreach (var definition in views)
            {
                var captured = definition;
                var button = new FacmPillButton
                {
                    Text = ResolveViewText(captured.TextKey),
                    AutoSize = false,
                    Size = new Size(100, 29),
                    Margin = new Padding(0, 0, 6, 0)
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
                UpdateHeaderHint(_currentSectionKey);
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
            if (_contextDock == null || _contextDock.IsDisposed) return;
            var sparseView = string.Equals(_currentViewId, LeagueHubNavigation.Dashboard, StringComparison.Ordinal) ||
                             string.Equals(_currentViewId, LeagueHubNavigation.Efficiency, StringComparison.Ordinal) ||
                             string.Equals(_currentViewId, LeagueHubNavigation.Presence, StringComparison.Ordinal);
            _contextDock.Visible = sparseView && ClientSize.Width >= 1040;
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
                    Size = new Size(204, 30),
                    Margin = new Padding(0, 0, 0, 6),
                    Font = new Font(Font.FontFamily, 8F, FontStyle.Bold)
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
