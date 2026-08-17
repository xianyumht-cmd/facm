using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueHubForm : Form
    {
        private const string HubBadgeText = "FACM // LEAGUE CONTROL";
        private static readonly Color HubBackground = Color.FromArgb(10, 15, 25);
        private static readonly Color HubSurface = Color.FromArgb(14, 22, 36);
        private static readonly Color HubSurfaceRaised = Color.FromArgb(20, 31, 49);
        private static readonly Color NeonCyan = Color.FromArgb(73, 218, 255);
        private static readonly Color NeonPurple = Color.FromArgb(154, 106, 255);
        private static readonly Color TextPrimary = Color.FromArgb(238, 243, 252);
        private static readonly Color TextMuted = Color.FromArgb(139, 157, 190);

        private readonly UiTextCatalog _ui;
        private readonly Dictionary<string, Func<UiTextCatalog, Form>> _factories;
        private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly Panel _content;
        private Form _currentChild;
        private string _currentViewId;
        private bool _switching;
        private bool _closing;

        public LeagueHubForm(
            UiTextCatalog ui,
            Func<UiTextCatalog, Form> dashboard,
            Func<UiTextCatalog, Form> player,
            Func<UiTextCatalog, Form> live,
            Func<UiTextCatalog, Form> mayhem,
            Func<UiTextCatalog, Form> advisor,
            Func<UiTextCatalog, Form> apply,
            Func<UiTextCatalog, Form> itemSet,
            Func<UiTextCatalog, Form> efficiency)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _factories = new Dictionary<string, Func<UiTextCatalog, Form>>(StringComparer.Ordinal)
            {
                { LeagueHubNavigation.Dashboard, dashboard ?? throw new ArgumentNullException(nameof(dashboard)) },
                { LeagueHubNavigation.Player, player ?? throw new ArgumentNullException(nameof(player)) },
                { LeagueHubNavigation.Live, live ?? throw new ArgumentNullException(nameof(live)) },
                { LeagueHubNavigation.Mayhem, mayhem ?? throw new ArgumentNullException(nameof(mayhem)) },
                { LeagueHubNavigation.Advisor, advisor ?? throw new ArgumentNullException(nameof(advisor)) },
                { LeagueHubNavigation.Apply, apply ?? throw new ArgumentNullException(nameof(apply)) },
                { LeagueHubNavigation.ItemSet, itemSet ?? throw new ArgumentNullException(nameof(itemSet)) },
                { LeagueHubNavigation.Efficiency, efficiency ?? throw new ArgumentNullException(nameof(efficiency)) }
            };

            Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 800);
            MinimumSize = new Size(980, 720);
            BackColor = HubBackground;
            ForeColor = TextPrimary;
            Font = new Font("Microsoft YaHei UI", 9F);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = Color.FromArgb(12, 20, 34)
            };
            header.Controls.Add(new Panel
            {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = NeonCyan
            });
            header.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Title),
                Location = new Point(28, 12),
                Size = new Size(460, 30),
                ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 16F, FontStyle.Bold)
            });
            header.Controls.Add(new Label
            {
                Text = HubBadgeText,
                Location = new Point(796, 16),
                Size = new Size(250, 22),
                ForeColor = NeonPurple,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Consolas", 9F, FontStyle.Bold)
            });
            header.Controls.Add(new Label
            {
                Text = LeagueHubText.Get(_ui, LeagueHubUiTextKeys.Hint),
                Location = new Point(29, 44),
                Size = new Size(760, 20),
                ForeColor = TextMuted
            });
            var headerGlow = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            headerGlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            headerGlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            headerGlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            headerGlow.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = NeonCyan }, 0, 0);
            headerGlow.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(73, 123, 255) }, 1, 0);
            headerGlow.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = NeonPurple }, 2, 0);
            header.Controls.Add(headerGlow);

            var sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 196,
                Padding = new Padding(12, 12, 12, 12),
                BackColor = Color.FromArgb(12, 20, 34)
            };
            sidebar.Controls.Add(new Panel
            {
                Dock = DockStyle.Right,
                Width = 1,
                BackColor = Color.FromArgb(46, 76, 111)
            });

            var y = 10;
            AddSection(sidebar, LeagueHubUiTextKeys.SectionMatch, ref y);
            AddViewButton(sidebar, LeagueHubNavigation.Dashboard, ref y);
            AddViewButton(sidebar, LeagueHubNavigation.Player, ref y);
            AddViewButton(sidebar, LeagueHubNavigation.Live, ref y);
            AddViewButton(sidebar, LeagueHubNavigation.Mayhem, ref y);

            y += 8;
            AddSection(sidebar, LeagueHubUiTextKeys.SectionRecommend, ref y);
            AddViewButton(sidebar, LeagueHubNavigation.Advisor, ref y);
            AddViewButton(sidebar, LeagueHubNavigation.Apply, ref y);
            AddViewButton(sidebar, LeagueHubNavigation.ItemSet, ref y);

            y += 8;
            AddSection(sidebar, LeagueHubUiTextKeys.SectionEfficiency, ref y);
            AddViewButton(sidebar, LeagueHubNavigation.Efficiency, ref y);

            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = HubBackground,
                Padding = new Padding(1, 0, 0, 0)
            };

            Controls.Add(_content);
            Controls.Add(sidebar);
            Controls.Add(header);

            Shown += delegate { ShowView(LeagueHubNavigation.Dashboard); };
            FormClosing += HandleHubClosing;
        }

        internal string CurrentViewIdForSmokeTest
        {
            get { return _currentViewId ?? string.Empty; }
        }

        private void AddSection(Panel sidebar, string textKey, ref int y)
        {
            var label = new Label
            {
                Text = LeagueHubText.Get(_ui, textKey),
                Location = new Point(12, y),
                Size = new Size(164, 24),
                ForeColor = Color.FromArgb(107, 142, 183),
                Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            sidebar.Controls.Add(label);
            y += 26;
        }

        private void AddViewButton(Panel sidebar, string viewId, ref int y)
        {
            var definition = LeagueHubNavigation.Views.First(item => string.Equals(item.Id, viewId, StringComparison.Ordinal));
            var button = new Button
            {
                Text = ResolveViewText(viewId, definition.TextKey),
                Location = new Point(12, y),
                Size = new Size(164, 38),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                BackColor = HubSurface,
                ForeColor = Color.FromArgb(209, 220, 239),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.MouseOverBackColor = HubSurfaceRaised;
            button.Click += delegate { ShowView(viewId); };
            sidebar.Controls.Add(button);
            _buttons[viewId] = button;
            y += 42;
        }

        private string ResolveViewText(string viewId, string textKey)
        {
            if (string.Equals(viewId, LeagueHubNavigation.Efficiency, StringComparison.Ordinal))
                return LeagueEfficiencyText.Get(_ui, textKey);
            if (string.Equals(viewId, LeagueHubNavigation.ItemSet, StringComparison.Ordinal))
                return LeagueAdvisorText.Get(_ui, textKey);
            if (LeagueHubText.DefaultsForSmokeTest().ContainsKey(textKey))
                return LeagueHubText.Get(_ui, textKey);
            return _ui.Get(textKey);
        }

        private void ShowView(string viewId)
        {
            if (_closing || IsDisposed || string.IsNullOrWhiteSpace(viewId)) return;
            if (string.Equals(_currentViewId, viewId, StringComparison.Ordinal) && _currentChild != null && !_currentChild.IsDisposed)
                return;

            Func<UiTextCatalog, Form> factory;
            if (!_factories.TryGetValue(viewId, out factory)) return;

            CloseCurrentChild();

            Form child = null;
            try
            {
                child = factory(_ui);
                if (child == null) throw new InvalidOperationException("League Hub view factory returned no form: " + viewId);

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
                UpdateSelection();
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
                UpdateSelection();
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

        private void UpdateSelection()
        {
            foreach (var pair in _buttons)
            {
                var selected = string.Equals(pair.Key, _currentViewId, StringComparison.Ordinal);
                pair.Value.BackColor = selected ? Color.FromArgb(30, 48, 77) : HubSurface;
                pair.Value.ForeColor = selected ? Color.White : Color.FromArgb(209, 220, 239);
                pair.Value.FlatAppearance.BorderSize = selected ? 1 : 0;
                pair.Value.FlatAppearance.BorderColor = selected ? NeonCyan : HubSurface;
            }
        }
    }
}
