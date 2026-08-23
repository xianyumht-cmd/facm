using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeaguePresenceForm : Form
    {
        private readonly LeaguePresenceService _service;
        private readonly UiTextCatalog _ui;
        private readonly ThemeDefinition _theme;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Label _currentValue;
        private readonly Label _statusValue;
        private readonly Button[] _choiceButtons;
        private readonly Button _refreshButton;
        private bool _busy;

        public LeaguePresenceForm(LeaguePresenceService service, UiTextCatalog ui, ThemeDefinition theme)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ui = ui ?? UiTextCatalog.Load();
            _theme = theme ?? ThemeCatalog.Get(ThemeCatalog.DefaultThemeId);

            Text = T(LeaguePresenceUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 438);
            BackColor = _theme.Background;
            ForeColor = _theme.TextPrimary;
            Font = new Font(_theme.FontName, 9F);

            var title = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Title),
                Location = new Point(24, 20),
                Size = new Size(280, 30),
                ForeColor = _theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, 15F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Hint),
                Location = new Point(24, 55),
                Size = new Size(382, 42),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, 8.5F)
            };
            _refreshButton = CreateFlatButton(T(LeaguePresenceUiTextKeys.Refresh), new Rectangle(330, 20, 76, 30));
            _refreshButton.Click += async delegate { await RefreshPresenceAsync(); };

            var currentCaption = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Current),
                Location = new Point(24, 110),
                Size = new Size(80, 22),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent
            };
            _currentValue = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Waiting),
                Location = new Point(104, 108),
                Size = new Size(302, 26),
                ForeColor = _theme.TextPrimary,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, 10F, FontStyle.Bold),
                AutoEllipsis = true
            };

            var modes = new[]
            {
                new Choice(LeaguePresenceMode.Online, LeaguePresenceUiTextKeys.Online),
                new Choice(LeaguePresenceMode.Away, LeaguePresenceUiTextKeys.Away),
                new Choice(LeaguePresenceMode.DoNotDisturb, LeaguePresenceUiTextKeys.DoNotDisturb),
                new Choice(LeaguePresenceMode.Mobile, LeaguePresenceUiTextKeys.Mobile),
                new Choice(LeaguePresenceMode.Offline, LeaguePresenceUiTextKeys.Offline),
                new Choice(LeaguePresenceMode.DisplayInGame, LeaguePresenceUiTextKeys.InGame)
            };
            _choiceButtons = new Button[modes.Length];
            for (var index = 0; index < modes.Length; index++)
            {
                var captured = modes[index].Mode;
                var column = index % 2;
                var row = index / 2;
                var button = CreatePresenceButton(
                    T(modes[index].TextKey),
                    new Rectangle(24 + column * 195, 151 + row * 58, 184, 48));
                button.Click += async delegate { await ApplyAsync(captured); };
                _choiceButtons[index] = button;
                Controls.Add(button);
            }

            _statusValue = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Waiting),
                Location = new Point(24, 337),
                Size = new Size(382, 24),
                ForeColor = _theme.AccentSecondary,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Font = new Font(_theme.FontName, 8.5F, FontStyle.Bold)
            };
            var footer = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Footer),
                Location = new Point(24, 372),
                Size = new Size(382, 48),
                ForeColor = _theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(_theme.FontName, 7.8F)
            };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_refreshButton);
            Controls.Add(currentCaption);
            Controls.Add(_currentValue);
            Controls.Add(_statusValue);
            Controls.Add(footer);

            Shown += async delegate { await RefreshPresenceAsync(); };
            FormClosed += delegate { _lifetime.Cancel(); _lifetime.Dispose(); };
        }

        private Button CreateFlatButton(string text, Rectangle bounds)
        {
            var button = new Button
            {
                Text = text,
                Location = bounds.Location,
                Size = bounds.Size,
                FlatStyle = FlatStyle.Flat,
                BackColor = _theme.Surface,
                ForeColor = _theme.TextPrimary,
                Cursor = Cursors.Hand,
                TabStop = false,
                Font = new Font(_theme.FontName, 8F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = _theme.SurfaceSecondary;
            button.FlatAppearance.MouseDownBackColor = _theme.Accent;
            return button;
        }

        private Button CreatePresenceButton(string text, Rectangle bounds)
        {
            var button = CreateFlatButton(text, bounds);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(16, 0, 8, 0);
            button.Font = new Font(_theme.FontName, 9.5F, FontStyle.Bold);
            return button;
        }

        private async Task RefreshPresenceAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            SetBusy(true);
            try
            {
                _statusValue.Text = T(LeaguePresenceUiTextKeys.Waiting);
                var snapshot = await _service.ReadAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                ApplySnapshot(snapshot);
                _statusValue.Text = snapshot != null && snapshot.Connected
                    ? T(LeaguePresenceUiTextKeys.Applied)
                    : T(LeaguePresenceUiTextKeys.Unavailable);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League presence refresh failed", exception);
                if (!IsDisposed) _statusValue.Text = T(LeaguePresenceUiTextKeys.Unavailable);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task ApplyAsync(LeaguePresenceMode mode)
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            SetBusy(true);
            try
            {
                _statusValue.Text = T(LeaguePresenceUiTextKeys.Waiting);
                var result = await _service.ApplyAsync(mode, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (result != null && result.Observed != null) ApplySnapshot(result.Observed);

                if (result == null || string.Equals(result.Status, "unavailable", StringComparison.OrdinalIgnoreCase))
                    _statusValue.Text = T(LeaguePresenceUiTextKeys.Unavailable);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    _statusValue.Text = T(LeaguePresenceUiTextKeys.Applied);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    _statusValue.Text = T(LeaguePresenceUiTextKeys.Overridden);
                else
                    _statusValue.Text = T(LeaguePresenceUiTextKeys.WriteFailed);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League presence apply failed", exception);
                if (!IsDisposed) _statusValue.Text = T(LeaguePresenceUiTextKeys.WriteFailed);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void ApplySnapshot(LeaguePresenceSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Connected)
            {
                _currentValue.Text = T(LeaguePresenceUiTextKeys.Unavailable);
                return;
            }
            _currentValue.Text = string.Format(
                T(LeaguePresenceUiTextKeys.CurrentFormat),
                DisplayMode(snapshot));
        }

        private string DisplayMode(LeaguePresenceSnapshot snapshot)
        {
            if (snapshot != null && string.Equals(snapshot.GameStatus, "inGame", StringComparison.OrdinalIgnoreCase))
                return T(LeaguePresenceUiTextKeys.InGame);
            var availability = snapshot == null ? string.Empty : snapshot.Availability ?? string.Empty;
            if (string.Equals(availability, "chat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(availability, "online", StringComparison.OrdinalIgnoreCase))
                return T(LeaguePresenceUiTextKeys.Online);
            if (string.Equals(availability, "away", StringComparison.OrdinalIgnoreCase))
                return T(LeaguePresenceUiTextKeys.Away);
            if (string.Equals(availability, "dnd", StringComparison.OrdinalIgnoreCase))
                return T(LeaguePresenceUiTextKeys.DoNotDisturb);
            if (string.Equals(availability, "mobile", StringComparison.OrdinalIgnoreCase))
                return T(LeaguePresenceUiTextKeys.Mobile);
            if (string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase))
                return T(LeaguePresenceUiTextKeys.Offline);
            return availability.Length == 0 ? T(LeaguePresenceUiTextKeys.Unavailable) : availability;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _refreshButton.Enabled = !busy;
            foreach (var button in _choiceButtons) button.Enabled = !busy;
        }

        private string T(string key)
        {
            return LeaguePresenceText.Get(_ui, key);
        }

        private sealed class Choice
        {
            public Choice(LeaguePresenceMode mode, string textKey)
            {
                Mode = mode;
                TextKey = textKey;
            }
            public LeaguePresenceMode Mode { get; private set; }
            public string TextKey { get; private set; }
        }
    }
}
