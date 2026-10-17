using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueProfileCustomizationForm : Form
    {
        private readonly LeagueProfileCustomizationService _service;
        private readonly LeagueRegaliaCustomizationService _regaliaService;
        private readonly LeagueChallengePreferencesService _challengePreferencesService;
        private readonly LeagueEmoteLoadoutService _emoteLoadoutService;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly ComboBox _champions;
        private readonly ComboBox _skins;
        private readonly Button _apply;
        private readonly Button _refresh;
        private readonly Button _removePrestigeCrest;
        private readonly Button _clearChallengeTokens;
        private readonly Button _clearEmotes;
        private readonly Label _status;
        private int _selectionGeneration;
        private bool _busy;
        private bool _suppressChampionChange;

        public LeagueProfileCustomizationForm(
            LeagueProfileCustomizationService service,
            LeagueRegaliaCustomizationService regaliaService,
            UiTextCatalog ui,
            ThemeDefinition theme)
            : this(service, regaliaService, null, null, ui, theme)
        {
        }

        public LeagueProfileCustomizationForm(
            LeagueProfileCustomizationService service,
            LeagueRegaliaCustomizationService regaliaService,
            LeagueChallengePreferencesService challengePreferencesService,
            UiTextCatalog ui,
            ThemeDefinition theme)
            : this(service, regaliaService, challengePreferencesService, null, ui, theme)
        {
        }

        public LeagueProfileCustomizationForm(
            LeagueProfileCustomizationService service,
            LeagueRegaliaCustomizationService regaliaService,
            LeagueChallengePreferencesService challengePreferencesService,
            LeagueEmoteLoadoutService emoteLoadoutService,
            UiTextCatalog ui,
            ThemeDefinition theme)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _regaliaService = regaliaService ?? throw new ArgumentNullException(nameof(regaliaService));
            _challengePreferencesService = challengePreferencesService;
            _emoteLoadoutService = emoteLoadoutService;
            _ui = ui ?? UiTextCatalog.Load();

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = T(LeagueProfileCustomizationUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 660);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var title = new Label
            {
                Text = T(LeagueProfileCustomizationUiTextKeys.Title),
                Location = new Point(24, 20),
                Size = new Size(465, 32),
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.Text,
                Font = new Font(FacmThemeRuntime.Current.FontName, 15F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = T(LeagueProfileCustomizationUiTextKeys.Hint),
                Location = new Point(24, 55),
                Size = new Size(472, 52),
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };

            var championTitle = CreateCaption(T(LeagueProfileCustomizationUiTextKeys.Champion), new Rectangle(24, 116, 180, 22));
            Controls.Add(championTitle);
            _champions = CreateCombo(new Rectangle(24, 140, 354, 32));
            _champions.SelectedIndexChanged += async delegate
            {
                if (!_suppressChampionChange) await LoadSelectedChampionSkinsAsync();
            };
            _refresh = CreateButton(T(LeagueProfileCustomizationUiTextKeys.Refresh), new Rectangle(387, 138, 109, 34));
            _refresh.Click += async delegate { await LoadChampionsAsync(); };

            var skinTitle = CreateCaption(T(LeagueProfileCustomizationUiTextKeys.Skin), new Rectangle(24, 184, 180, 22));
            Controls.Add(skinTitle);
            _skins = CreateCombo(new Rectangle(24, 208, 354, 32));
            _skins.SelectedIndexChanged += delegate { UpdateButtons(); };
            _apply = CreateButton(T(LeagueProfileCustomizationUiTextKeys.Apply), new Rectangle(387, 206, 109, 34));
            _apply.Click += async delegate { await ApplySelectedSkinAsync(); };

            var regaliaTitle = CreateCaption(T(LeagueProfileCustomizationUiTextKeys.RegaliaTitle), new Rectangle(24, 260, 280, 22));
            var regaliaHint = new Label
            {
                Text = T(LeagueProfileCustomizationUiTextKeys.RegaliaHint),
                Location = new Point(24, 282),
                Size = new Size(352, 62),
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };
            _removePrestigeCrest = CreateButton(
                T(LeagueProfileCustomizationUiTextKeys.RemovePrestigeCrest),
                new Rectangle(387, 292, 109, 38));
            _removePrestigeCrest.Click += async delegate { await RemovePrestigeCrestAsync(); };

            var tokensTitle = CreateCaption(T(LeagueProfileCustomizationUiTextKeys.TokensTitle), new Rectangle(24, 352, 280, 22));
            var tokensHint = new Label
            {
                Text = T(LeagueProfileCustomizationUiTextKeys.TokensHint),
                Location = new Point(24, 374),
                Size = new Size(352, 58),
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };
            _clearChallengeTokens = CreateButton(
                T(LeagueProfileCustomizationUiTextKeys.TokensAction),
                new Rectangle(387, 384, 109, 38));
            _clearChallengeTokens.Click += async delegate { await ClearChallengeTokensAsync(); };

            var emotesTitle = CreateCaption(T(LeagueProfileCustomizationUiTextKeys.EmotesTitle), new Rectangle(24, 442, 280, 22));
            var emotesHint = new Label
            {
                Text = T(LeagueProfileCustomizationUiTextKeys.EmotesHint),
                Location = new Point(24, 464),
                Size = new Size(352, 58),
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };
            _clearEmotes = CreateButton(
                T(LeagueProfileCustomizationUiTextKeys.EmotesAction),
                new Rectangle(387, 474, 109, 38));
            _clearEmotes.Click += async delegate { await ClearEmotesAsync(); };

            _status = new Label
            {
                Text = T(LeagueProfileCustomizationUiTextKeys.Loading),
                Location = new Point(24, 544),
                Size = new Size(472, 28),
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.Accent,
                AutoEllipsis = true,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold)
            };
            var footer = new Label
            {
                Text = T(LeagueProfileCustomizationUiTextKeys.Footer),
                Location = new Point(24, 580),
                Size = new Size(472, 58),
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_champions);
            Controls.Add(_refresh);
            Controls.Add(_skins);
            Controls.Add(_apply);
            Controls.Add(regaliaTitle);
            Controls.Add(regaliaHint);
            Controls.Add(_removePrestigeCrest);
            Controls.Add(tokensTitle);
            Controls.Add(tokensHint);
            Controls.Add(_clearChallengeTokens);
            Controls.Add(emotesTitle);
            Controls.Add(emotesHint);
            Controls.Add(_clearEmotes);
            Controls.Add(_status);
            Controls.Add(footer);

            AddTaskSurface(new Rectangle(16, 110, 488, 137),
                championTitle, _champions, _refresh, skinTitle, _skins, _apply);
            AddTaskSurface(new Rectangle(16, 252, 488, 95),
                regaliaTitle, regaliaHint, _removePrestigeCrest);
            AddTaskSurface(new Rectangle(16, 348, 488, 86),
                tokensTitle, tokensHint, _clearChallengeTokens);
            AddTaskSurface(new Rectangle(16, 438, 488, 86),
                emotesTitle, emotesHint, _clearEmotes);

            FacmDesignSystem.ApplyLeagueSurface(this);
            Shown += async delegate { await LoadChampionsAsync(); };
            FormClosed += delegate
            {
                _selectionGeneration++;
                _lifetime.Cancel();
                _lifetime.Dispose();
            };
            UpdateButtons();
        }

        private void AddTaskSurface(Rectangle bounds, params Control[] children)
        {
            var surface = new FacmGlassPanel
            {
                Bounds = bounds,
                BackColor = FacmDesignSystem.Surface,
                Radius = FacmDesignSystem.CardRadius,
                DrawBorder = true,
                TabStop = false
            };
            Controls.Add(surface);
            foreach (var control in children)
            {
                control.Location = new Point(control.Left - bounds.Left, control.Top - bounds.Top);
                surface.Controls.Add(control);
            }
        }

        private Label CreateCaption(string text, Rectangle bounds)
        {
            return new Label
            {
                Text = text,
                Location = bounds.Location,
                Size = bounds.Size,
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
        }

        private ComboBox CreateCombo(Rectangle bounds)
        {
            return new ComboBox
            {
                Location = bounds.Location,
                Size = bounds.Size,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F),
                IntegralHeight = false,
                DropDownHeight = 250
            };
        }

        private Button CreateButton(string text, Rectangle bounds)
        {
            return new FacmActionButton
            {
                Text = text,
                Bounds = bounds,
                Tone = FacmButtonTone.Secondary,
                Cursor = Cursors.Hand,
                TabStop = true,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold)
            };
        }

        private async Task LoadChampionsAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            var generation = ++_selectionGeneration;
            SetBusy(true);
            SetStatus(T(LeagueProfileCustomizationUiTextKeys.Loading), FacmDesignSystem.Accent);
            try
            {
                var champions = await _service.LoadChampionsAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested || generation != _selectionGeneration) return;

                _suppressChampionChange = true;
                try
                {
                    _champions.Items.Clear();
                    _skins.Items.Clear();
                    foreach (var champion in champions)
                        _champions.Items.Add(new ChampionItem(champion.Id, champion.Name));
                    if (_champions.Items.Count > 0) _champions.SelectedIndex = 0;
                }
                finally
                {
                    _suppressChampionChange = false;
                }

                if (_champions.Items.Count == 0)
                {
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.Unavailable), FacmDesignSystem.Warning);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                AppLog.Error("League profile champion catalog load failed", exception);
                if (!IsDisposed) SetStatus(T(LeagueProfileCustomizationUiTextKeys.Unavailable), FacmDesignSystem.Warning);
                return;
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }

            await LoadSelectedChampionSkinsAsync();
        }

        private async Task LoadSelectedChampionSkinsAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            var selected = _champions.SelectedItem as ChampionItem;
            if (selected == null || selected.Id <= 0)
            {
                _skins.Items.Clear();
                SetStatus(T(LeagueProfileCustomizationUiTextKeys.ChooseChampion), FacmDesignSystem.TextMuted);
                UpdateButtons();
                return;
            }

            var generation = ++_selectionGeneration;
            SetBusy(true);
            SetStatus(T(LeagueProfileCustomizationUiTextKeys.Loading), FacmDesignSystem.Accent);
            try
            {
                var skins = await _service.LoadSkinsAsync(selected.Id, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested || generation != _selectionGeneration) return;

                _skins.Items.Clear();
                foreach (var skin in skins)
                    _skins.Items.Add(new SkinItem(skin.Id, skin.Name));
                if (_skins.Items.Count > 0)
                {
                    _skins.SelectedIndex = 0;
                    SetStatus(
                        string.Format(T(LeagueProfileCustomizationUiTextKeys.CandidateFormat), selected.Name, _skins.Items.Count),
                        FacmDesignSystem.TextMuted);
                }
                else
                {
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.NoSkins), FacmDesignSystem.Warning);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League profile skin catalog load failed", exception);
                if (!IsDisposed) SetStatus(T(LeagueProfileCustomizationUiTextKeys.Unavailable), FacmDesignSystem.Warning);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task ApplySelectedSkinAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            var selected = _skins.SelectedItem as SkinItem;
            if (selected == null || selected.Id <= 0)
            {
                SetStatus(T(LeagueProfileCustomizationUiTextKeys.Invalid), FacmDesignSystem.Warning);
                return;
            }

            SetBusy(true);
            SetStatus(T(LeagueProfileCustomizationUiTextKeys.Loading), FacmDesignSystem.Accent);
            try
            {
                var result = await _service.ApplyBackgroundSkinAsync(selected.Id, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;

                if (result == null || string.Equals(result.Status, "write-failed", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.WriteFailed), FacmDesignSystem.Error);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.Applied), FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.Overridden), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "unverified", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.Unverified), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "invalid", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.Invalid), FacmDesignSystem.Warning);
                else
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.Unavailable), FacmDesignSystem.Warning);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League profile background apply failed", exception);
                if (!IsDisposed) SetStatus(T(LeagueProfileCustomizationUiTextKeys.WriteFailed), FacmDesignSystem.Error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task RemovePrestigeCrestAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            SetBusy(true);
            SetStatus(T(LeagueProfileCustomizationUiTextKeys.Loading), FacmDesignSystem.Accent);
            try
            {
                var result = await _regaliaService.RemovePrestigeCrestAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;

                if (result == null || string.Equals(result.Status, "write-failed", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.RegaliaWriteFailed), FacmDesignSystem.Error);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.RegaliaApplied), FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.RegaliaOverridden), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "unverified", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.RegaliaUnverified), FacmDesignSystem.Warning);
                else
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.RegaliaUnavailable), FacmDesignSystem.Warning);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League regalia prestige-crest apply failed", exception);
                if (!IsDisposed) SetStatus(T(LeagueProfileCustomizationUiTextKeys.RegaliaWriteFailed), FacmDesignSystem.Error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task ClearChallengeTokensAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested || _challengePreferencesService == null) return;
            SetBusy(true);
            SetStatus(T(LeagueProfileCustomizationUiTextKeys.Loading), FacmDesignSystem.Accent);
            try
            {
                var result = await _challengePreferencesService.ClearChallengeTokensAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;

                if (result == null || string.Equals(result.Status, "write-failed", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.TokensWriteFailed), FacmDesignSystem.Error);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.TokensApplied), FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.TokensOverridden), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "unverified", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.TokensUnverified), FacmDesignSystem.Warning);
                else
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.TokensUnavailable), FacmDesignSystem.Warning);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League challenge-token cleanup failed", exception);
                if (!IsDisposed) SetStatus(T(LeagueProfileCustomizationUiTextKeys.TokensWriteFailed), FacmDesignSystem.Error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task ClearEmotesAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested || _emoteLoadoutService == null) return;
            SetBusy(true);
            SetStatus(T(LeagueProfileCustomizationUiTextKeys.Loading), FacmDesignSystem.Accent);
            try
            {
                var result = await _emoteLoadoutService.ClearEmotesAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;

                if (result == null || string.Equals(result.Status, "write-failed", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.EmotesWriteFailed), FacmDesignSystem.Error);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.EmotesApplied), FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.EmotesOverridden), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "unverified", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.EmotesUnverified), FacmDesignSystem.Warning);
                else
                    SetStatus(T(LeagueProfileCustomizationUiTextKeys.EmotesUnavailable), FacmDesignSystem.Warning);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League emote cleanup failed", exception);
                if (!IsDisposed) SetStatus(T(LeagueProfileCustomizationUiTextKeys.EmotesWriteFailed), FacmDesignSystem.Error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _champions.Enabled = !busy;
            _skins.Enabled = !busy;
            _refresh.Enabled = !busy;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (_apply != null && !_apply.IsDisposed)
                _apply.Enabled = !_busy && _skins.SelectedItem is SkinItem;
            if (_removePrestigeCrest != null && !_removePrestigeCrest.IsDisposed)
                _removePrestigeCrest.Enabled = !_busy;
            if (_clearChallengeTokens != null && !_clearChallengeTokens.IsDisposed)
                _clearChallengeTokens.Enabled = !_busy && _challengePreferencesService != null;
            if (_clearEmotes != null && !_clearEmotes.IsDisposed)
                _clearEmotes.Enabled = !_busy && _emoteLoadoutService != null;
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null || _status.IsDisposed) return;
            _status.Text = text ?? string.Empty;
            _status.ForeColor = color;
        }

        private string T(string key)
        {
            return LeagueProfileCustomizationText.Get(_ui, key);
        }

        private sealed class ChampionItem
        {
            public ChampionItem(int id, string name)
            {
                Id = id;
                Name = name ?? string.Empty;
            }

            public int Id { get; private set; }
            public string Name { get; private set; }
            public override string ToString() { return Name; }
        }

        private sealed class SkinItem
        {
            public SkinItem(int id, string name)
            {
                Id = id;
                Name = name ?? string.Empty;
            }

            public int Id { get; private set; }
            public string Name { get; private set; }
            public override string ToString() { return Name; }
        }
    }
}
