using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueHubForm : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly Dictionary<string, Func<UiTextCatalog, Form>> _factories;
        private readonly Dictionary<string, Button> _sectionButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> _viewButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Label _headerHint;
        private readonly FlowLayoutPanel _subnav;
        private readonly Panel _content;
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
            ClientSize = new Size(1160, 820);
            MinimumSize = new Size(1000, 720);
            BackColor = Color.FromArgb(8, 13, 23);
            ForeColor = Color.FromArgb(238, 243, 252);
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;

            var header = new HubHeaderPanel
            {
                Dock = DockStyle.Top,
                Height = 86,
                BackColor = Color.FromArgb(12, 19, 32)
            };
            header.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Title),
                Location = new Point(28, 14),
                Size = new Size(500, 34),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 17F, FontStyle.Bold)
            });
            _headerHint = new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Hint),
                Location = new Point(30, 50),
                Size = new Size(930, 23),
                ForeColor = Color.FromArgb(138, 158, 194),
                BackColor = Color.Transparent
            };
            header.Controls.Add(_headerHint);

            var sidebar = new HubSidebarPanel
            {
                Dock = DockStyle.Left,
                Width = 184,
                Padding = new Padding(14, 18, 14, 14),
                BackColor = Color.FromArgb(12, 19, 32)
            };
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.SectionMatchHint, 20);
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionRecommend, LeagueHubUiTextKeys.SectionRecommendHint, 82);
            AddSectionButton(sidebar, LeagueHubUiTextKeys.SectionEfficiency, LeagueHubUiTextKeys.SectionEfficiencyHint, 144);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(9, 14, 24)
            };
            _subnav = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 54,
                Padding = new Padding(24, 10, 18, 6),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(11, 18, 30)
            };
            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 15, 25),
                Padding = Padding.Empty
            };
            body.Controls.Add(_content);
            body.Controls.Add(_subnav);

            Controls.Add(body);
            Controls.Add(sidebar);
            Controls.Add(header);

            Shown += delegate { ShowSection(LeagueHubUiTextKeys.SectionMatch); };
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
            var button = new Button
            {
                Text = LeagueHubText.Get(_ui, sectionKey),
                Location = new Point(14, top),
                Size = new Size(150, 50),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1 },
                BackColor = Color.FromArgb(17, 27, 44),
                ForeColor = Color.FromArgb(210, 222, 242),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 8, 0),
                Cursor = Cursors.Hand,
                TabStop = false,
                Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold)
            };
            button.Click += delegate { ShowSection(sectionKey); };
            button.MouseEnter += delegate
            {
                _headerHint.Text = LeagueHubText.Get(_ui, hintKey);
                if (!string.Equals(_currentSectionKey, sectionKey, StringComparison.Ordinal))
                    button.BackColor = Color.FromArgb(23, 36, 58);
            };
            button.MouseLeave += delegate
            {
                UpdateSectionSelection();
                UpdateHeaderHint(_currentSectionKey);
            };
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
            _subnav.Height = 54;
            foreach (var definition in views)
            {
                var captured = definition;
                var button = new Button
                {
                    Text = ResolveViewText(captured.Id, captured.TextKey),
                    AutoSize = false,
                    Size = new Size(112, 32),
                    Margin = new Padding(0, 0, 8, 0),
                    FlatStyle = FlatStyle.Flat,
                    FlatAppearance = { BorderSize = 1 },
                    BackColor = Color.FromArgb(18, 28, 45),
                    ForeColor = Color.FromArgb(190, 207, 235),
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                button.Click += delegate { ShowView(captured.Id, true); };
                button.MouseEnter += delegate
                {
                    if (!string.Equals(_currentViewId, captured.Id, StringComparison.Ordinal))
                        button.BackColor = Color.FromArgb(24, 39, 62);
                };
                button.MouseLeave += delegate { UpdateViewSelection(); };
                _subnav.Controls.Add(button);
                _viewButtons[captured.Id] = button;
            }
            UpdateViewSelection();
        }

        private string ResolveViewText(string viewId, string textKey)
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
                ShowSection(definition.SectionKey);
                return;
            }
            if (string.Equals(_currentViewId, viewId, StringComparison.Ordinal) && _currentChild != null && !_currentChild.IsDisposed)
                return;

            Func<UiTextCatalog, Form> factory;
            if (!_factories.TryGetValue(viewId, out factory)) return;

            CloseCurrentChild();

            Form child = null;
            try
            {
                child = factory(_ui);
                if (child == null) throw new InvalidOperationException("LOL helper view factory returned no form: " + viewId);

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

        private void UpdateSectionSelection()
        {
            foreach (var pair in _sectionButtons)
            {
                var selected = string.Equals(pair.Key, _currentSectionKey, StringComparison.Ordinal);
                pair.Value.BackColor = selected ? Color.FromArgb(27, 48, 84) : Color.FromArgb(17, 27, 44);
                pair.Value.ForeColor = selected ? Color.White : Color.FromArgb(210, 222, 242);
                pair.Value.FlatAppearance.BorderColor = selected ? Color.FromArgb(73, 218, 255) : Color.FromArgb(37, 52, 74);
            }
        }

        private void UpdateViewSelection()
        {
            foreach (var pair in _viewButtons)
            {
                var selected = string.Equals(pair.Key, _currentViewId, StringComparison.Ordinal);
                pair.Value.BackColor = selected ? Color.FromArgb(45, 66, 143) : Color.FromArgb(18, 28, 45);
                pair.Value.ForeColor = selected ? Color.White : Color.FromArgb(190, 207, 235);
                pair.Value.FlatAppearance.BorderColor = selected ? Color.FromArgb(139, 92, 246) : Color.FromArgb(43, 58, 80);
            }
        }

        private sealed class HubHeaderPanel : Panel
        {
            public HubHeaderPanel()
            {
                DoubleBuffered = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width <= 0 || Height <= 0) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (var cyan = new SolidBrush(Color.FromArgb(22, 65, 214, 255)))
                    e.Graphics.FillEllipse(cyan, Width - 360, -90, 300, 180);
                using (var violet = new SolidBrush(Color.FromArgb(18, 151, 71, 255)))
                    e.Graphics.FillEllipse(violet, Width - 180, -70, 240, 160);
                using (var bar = new LinearGradientBrush(
                    new Rectangle(0, Height - 4, Width, 4),
                    Color.FromArgb(58, 216, 255),
                    Color.FromArgb(145, 76, 255),
                    LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(bar, 0, Height - 4, Width, 4);
            }
        }

        private sealed class HubSidebarPanel : Panel
        {
            public HubSidebarPanel()
            {
                DoubleBuffered = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Height <= 0) return;
                using (var bar = new LinearGradientBrush(
                    new Rectangle(Width - 2, 0, 2, Height),
                    Color.FromArgb(58, 216, 255),
                    Color.FromArgb(111, 74, 255),
                    LinearGradientMode.Vertical))
                    e.Graphics.FillRectangle(bar, Width - 2, 0, 2, Height);
            }
        }
    }
}
