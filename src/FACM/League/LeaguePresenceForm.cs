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
        private readonly LeagueProfileCustomizationService _profileService;
        private readonly LeagueRegaliaCustomizationService _regaliaService;
        private readonly LeagueChallengePreferencesService _challengePreferencesService;
        private readonly LeagueEmoteLoadoutService _emoteLoadoutService;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Label _currentValue;
        private readonly Label _statusValue;
        private readonly Button[] _choiceButtons;
        private readonly Button _refreshButton;
        private readonly TextBox _signature;
        private readonly Button _signatureSave;
        private readonly ComboBox _rankQueue;
        private readonly ComboBox _rankTier;
        private readonly ComboBox _rankDivision;
        private readonly Button _rankSave;
        private readonly Button _profileButton;
        private readonly Button _bannerButton;
        private bool _busy;

        public LeaguePresenceForm(LeaguePresenceService service, UiTextCatalog ui, ThemeDefinition theme)
            : this(service, null, null, null, null, ui, theme)
        {
        }

        public LeaguePresenceForm(
            LeaguePresenceService service,
            LeagueProfileCustomizationService profileService,
            UiTextCatalog ui,
            ThemeDefinition theme)
            : this(service, profileService, null, null, null, ui, theme)
        {
        }

        public LeaguePresenceForm(
            LeaguePresenceService service,
            LeagueProfileCustomizationService profileService,
            LeagueRegaliaCustomizationService regaliaService,
            UiTextCatalog ui,
            ThemeDefinition theme)
            : this(service, profileService, regaliaService, null, null, ui, theme)
        {
        }

        public LeaguePresenceForm(
            LeaguePresenceService service,
            LeagueProfileCustomizationService profileService,
            LeagueRegaliaCustomizationService regaliaService,
            LeagueChallengePreferencesService challengePreferencesService,
            UiTextCatalog ui,
            ThemeDefinition theme)
            : this(service, profileService, regaliaService, challengePreferencesService, null, ui, theme)
        {
        }

        public LeaguePresenceForm(
            LeaguePresenceService service,
            LeagueProfileCustomizationService profileService,
            LeagueRegaliaCustomizationService regaliaService,
            LeagueChallengePreferencesService challengePreferencesService,
            LeagueEmoteLoadoutService emoteLoadoutService,
            UiTextCatalog ui,
            ThemeDefinition theme)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _profileService = profileService;
            _regaliaService = regaliaService;
            _challengePreferencesService = challengePreferencesService;
            _emoteLoadoutService = emoteLoadoutService;
            _ui = ui ?? UiTextCatalog.Load();

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = T(LeaguePresenceUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 760);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var title = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Title),
                Location = new Point(24, 20),
                Size = new Size(280, 30),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 15F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Hint),
                Location = new Point(24, 55),
                Size = new Size(382, 42),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.5F)
            };
            _refreshButton = CreateFlatButton(T(LeaguePresenceUiTextKeys.Refresh), new Rectangle(330, 20, 76, 30));
            _refreshButton.Click += async delegate { await RefreshPresenceAsync(); };

            var currentCaption = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Current),
                Location = new Point(24, 110),
                Size = new Size(80, 22),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            _currentValue = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Waiting),
                Location = new Point(104, 108),
                Size = new Size(302, 26),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 10F, FontStyle.Bold),
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

            var signatureCaption = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Signature),
                Location = new Point(24, 329),
                Size = new Size(90, 22),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            var signatureHint = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.SignatureHint),
                Location = new Point(112, 329),
                Size = new Size(294, 22),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.8F),
                AutoEllipsis = true
            };
            _signature = new TextBox
            {
                Location = new Point(24, 356),
                Size = new Size(300, 30),
                MaxLength = LeaguePresenceService.MaximumStatusMessageLength,
                BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };
            _signatureSave = CreateFlatButton(T(LeaguePresenceUiTextKeys.SignatureSave), new Rectangle(330, 353, 76, 32));
            _signatureSave.Click += async delegate { await ApplyStatusMessageAsync(); };

            var rankCaption = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.DisplayedRank),
                Location = new Point(24, 404),
                Size = new Size(90, 22),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            var rankHint = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.DisplayedRankHint),
                Location = new Point(24, 426),
                Size = new Size(382, 36),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.8F)
            };

            Controls.Add(CreateFieldCaption(T(LeaguePresenceUiTextKeys.RankedQueue), new Rectangle(24, 466, 146, 18)));
            Controls.Add(CreateFieldCaption(T(LeaguePresenceUiTextKeys.RankedTier), new Rectangle(178, 466, 108, 18)));
            Controls.Add(CreateFieldCaption(T(LeaguePresenceUiTextKeys.RankedDivision), new Rectangle(294, 466, 52, 18)));

            _rankQueue = CreateComboBox(new Rectangle(24, 486, 146, 30));
            _rankTier = CreateComboBox(new Rectangle(178, 486, 108, 30));
            _rankDivision = CreateComboBox(new Rectangle(294, 486, 52, 30));
            PopulateRankOptions();
            _rankTier.SelectedIndexChanged += delegate { UpdateRankDivisionEnabled(); };
            _rankSave = CreateFlatButton(T(LeaguePresenceUiTextKeys.RankedSave), new Rectangle(352, 484, 54, 32));
            _rankSave.Click += async delegate { await ApplyRankedStatusAsync(); };

            var profileCaption = new Label
            {
                Text = TP(LeagueProfileCustomizationUiTextKeys.Entry),
                Location = new Point(24, 536),
                Size = new Size(100, 22),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            var profileHint = new Label
            {
                Text = TP(LeagueProfileCustomizationUiTextKeys.EntryHint),
                Location = new Point(126, 536),
                Size = new Size(194, 22),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.8F)
            };
            _profileButton = CreateFlatButton(TP(LeagueProfileCustomizationUiTextKeys.Entry), new Rectangle(330, 531, 76, 32));
            _profileButton.Enabled = _profileService != null && _regaliaService != null;
            _profileButton.Click += delegate { OpenProfileCustomization(); };

            var bannerCaption = new Label
            {
                Text = TP(LeagueProfileCustomizationUiTextKeys.BannerTitle),
                Location = new Point(24, 575),
                Size = new Size(100, 22),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
            var bannerHint = new Label
            {
                Text = TP(LeagueProfileCustomizationUiTextKeys.BannerHint),
                Location = new Point(126, 570),
                Size = new Size(194, 42),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.6F)
            };
            _bannerButton = CreateFlatButton(TP(LeagueProfileCustomizationUiTextKeys.BannerAction), new Rectangle(330, 570, 76, 32));
            _bannerButton.Enabled = _challengePreferencesService != null;
            _bannerButton.Click += async delegate { await ApplyLastSeasonBannerAsync(); };

            _statusValue = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Waiting),
                Location = new Point(24, 625),
                Size = new Size(382, 24),
                ForeColor = FacmDesignSystem.Accent,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.5F, FontStyle.Bold)
            };
            var footer = new Label
            {
                Text = T(LeaguePresenceUiTextKeys.Footer),
                Location = new Point(24, 658),
                Size = new Size(382, 78),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.8F)
            };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_refreshButton);
            Controls.Add(currentCaption);
            Controls.Add(_currentValue);
            Controls.Add(signatureCaption);
            Controls.Add(signatureHint);
            Controls.Add(_signature);
            Controls.Add(_signatureSave);
            Controls.Add(rankCaption);
            Controls.Add(rankHint);
            Controls.Add(_rankQueue);
            Controls.Add(_rankTier);
            Controls.Add(_rankDivision);
            Controls.Add(_rankSave);
            Controls.Add(profileCaption);
            Controls.Add(profileHint);
            Controls.Add(_profileButton);
            Controls.Add(bannerCaption);
            Controls.Add(bannerHint);
            Controls.Add(_bannerButton);
            Controls.Add(_statusValue);
            Controls.Add(footer);

            FacmDesignSystem.ApplyLeagueSurface(this);
            Shown += async delegate { await RefreshPresenceAsync(); };
            FormClosed += delegate { _lifetime.Cancel(); _lifetime.Dispose(); };
        }

        private void OpenProfileCustomization()
        {
            if (_busy || _profileService == null || _regaliaService == null || IsDisposed) return;
            using (var form = new LeagueProfileCustomizationForm(
                _profileService,
                _regaliaService,
                _challengePreferencesService,
                _emoteLoadoutService,
                _ui,
                null))
            {
                form.TopMost = TopMost;
                form.ShowDialog(this);
            }
        }

        private Button CreateFlatButton(string text, Rectangle bounds)
        {
            var button = new Button
            {
                Text = text,
                Location = bounds.Location,
                Size = bounds.Size,
                FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text,
                Cursor = Cursors.Hand,
                TabStop = true,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = FacmDesignSystem.BorderSoft;
            button.FlatAppearance.MouseOverBackColor = FacmDesignSystem.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = FacmDesignSystem.Blend(FacmDesignSystem.SurfaceHover, FacmDesignSystem.Accent, 0.08F);
            FacmDesignSystem.Round(button, FacmDesignSystem.ControlRadius);
            return button;
        }

        private Button CreatePresenceButton(string text, Rectangle bounds)
        {
            var button = CreateFlatButton(text, bounds);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(16, 0, 8, 0);
            button.Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold);
            return button;
        }

        private Label CreateFieldCaption(string text, Rectangle bounds)
        {
            return new Label
            {
                Text = text,
                Location = bounds.Location,
                Size = bounds.Size,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 7.6F, FontStyle.Bold)
            };
        }

        private ComboBox CreateComboBox(Rectangle bounds)
        {
            return new ComboBox
            {
                Location = bounds.Location,
                Size = bounds.Size,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8F),
                IntegralHeight = false,
                DropDownHeight = 220
            };
        }

        private void PopulateRankOptions()
        {
            AddOption(_rankQueue, T(LeaguePresenceUiTextKeys.RankedQueueSolo), "RANKED_SOLO_5x5");
            AddOption(_rankQueue, T(LeaguePresenceUiTextKeys.RankedQueueFlex), "RANKED_FLEX_SR");
            AddOption(_rankQueue, T(LeaguePresenceUiTextKeys.RankedQueueArena), "CHERRY");
            AddOption(_rankQueue, T(LeaguePresenceUiTextKeys.RankedQueueTft), "RANKED_TFT");
            AddOption(_rankQueue, T(LeaguePresenceUiTextKeys.RankedQueueTftTurbo), "RANKED_TFT_TURBO");
            AddOption(_rankQueue, T(LeaguePresenceUiTextKeys.RankedQueueTftDoubleUp), "RANKED_TFT_DOUBLE_UP");
            AddOption(_rankQueue, T(LeaguePresenceUiTextKeys.RankedQueueFlex3v3), "RANKED_FLEX_TT");

            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierIron), "IRON");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierBronze), "BRONZE");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierSilver), "SILVER");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierGold), "GOLD");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierPlatinum), "PLATINUM");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierEmerald), "EMERALD");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierDiamond), "DIAMOND");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierMaster), "MASTER");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierGrandmaster), "GRANDMASTER");
            AddOption(_rankTier, T(LeaguePresenceUiTextKeys.RankedTierChallenger), "CHALLENGER");

            AddOption(_rankDivision, "I", "I");
            AddOption(_rankDivision, "II", "II");
            AddOption(_rankDivision, "III", "III");
            AddOption(_rankDivision, "IV", "IV");

            _rankQueue.SelectedIndex = 0;
            _rankTier.SelectedIndex = 3;
            _rankDivision.SelectedIndex = 1;
            UpdateRankDivisionEnabled();
        }

        private static void AddOption(ComboBox combo, string label, string value)
        {
            combo.Items.Add(new RankOption(label, value));
        }

        private async Task RefreshPresenceAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            SetBusy(true);
            try
            {
                SetStatus(T(LeaguePresenceUiTextKeys.Waiting), FacmDesignSystem.Accent);
                var snapshot = await _service.ReadAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                ApplySnapshot(snapshot, true, true);
                if (snapshot != null && snapshot.Connected)
                    SetStatus(T(LeaguePresenceUiTextKeys.Applied), FacmDesignSystem.Success);
                else
                    SetStatus(T(LeaguePresenceUiTextKeys.Unavailable), FacmDesignSystem.TextMuted);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League presence refresh failed", exception);
                if (!IsDisposed) SetStatus(T(LeaguePresenceUiTextKeys.Unavailable), FacmDesignSystem.Warning);
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
                SetStatus(T(LeaguePresenceUiTextKeys.Waiting), FacmDesignSystem.Accent);
                var result = await _service.ApplyAsync(mode, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (result != null && result.Observed != null) ApplySnapshot(result.Observed, false, false);

                if (result == null || string.Equals(result.Status, "unavailable", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.Unavailable), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.Applied), FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.Overridden), FacmDesignSystem.Warning);
                else
                    SetStatus(T(LeaguePresenceUiTextKeys.WriteFailed), FacmDesignSystem.Error);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League presence apply failed", exception);
                if (!IsDisposed) SetStatus(T(LeaguePresenceUiTextKeys.WriteFailed), FacmDesignSystem.Error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task ApplyStatusMessageAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            SetBusy(true);
            try
            {
                SetStatus(T(LeaguePresenceUiTextKeys.Waiting), FacmDesignSystem.Accent);
                var result = await _service.ApplyStatusMessageAsync(_signature.Text, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (result != null && result.Observed != null) ApplySnapshot(result.Observed, true, false);

                if (result == null || string.Equals(result.Status, "unavailable", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.Unavailable), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.SignatureSaved), FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.SignatureOverridden), FacmDesignSystem.Warning);
                else
                    SetStatus(T(LeaguePresenceUiTextKeys.WriteFailed), FacmDesignSystem.Error);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League chat signature apply failed", exception);
                if (!IsDisposed) SetStatus(T(LeaguePresenceUiTextKeys.WriteFailed), FacmDesignSystem.Error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task ApplyRankedStatusAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            var queue = SelectedValue(_rankQueue);
            var tier = SelectedValue(_rankTier);
            var division = SelectedValue(_rankDivision);

            SetBusy(true);
            try
            {
                SetStatus(T(LeaguePresenceUiTextKeys.Waiting), FacmDesignSystem.Accent);
                var result = await _service.ApplyRankedStatusAsync(queue, tier, division, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (result != null && result.Observed != null) ApplySnapshot(result.Observed, false, true);

                if (result == null || string.Equals(result.Status, "unavailable", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.Unavailable), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.RankedSaved), FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.RankedOverridden), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "invalid", StringComparison.OrdinalIgnoreCase))
                    SetStatus(T(LeaguePresenceUiTextKeys.RankedInvalid), FacmDesignSystem.Warning);
                else
                    SetStatus(T(LeaguePresenceUiTextKeys.WriteFailed), FacmDesignSystem.Error);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League displayed-rank apply failed", exception);
                if (!IsDisposed) SetStatus(T(LeaguePresenceUiTextKeys.WriteFailed), FacmDesignSystem.Error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task ApplyLastSeasonBannerAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested || _challengePreferencesService == null) return;
            SetBusy(true);
            try
            {
                SetStatus(T(LeaguePresenceUiTextKeys.Waiting), FacmDesignSystem.Accent);
                var result = await _challengePreferencesService.ApplyLastSeasonBannerAsync(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;

                if (result == null || string.Equals(result.Status, "write-failed", StringComparison.OrdinalIgnoreCase))
                    SetStatus(TP(LeagueProfileCustomizationUiTextKeys.BannerWriteFailed), FacmDesignSystem.Error);
                else if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(TP(LeagueProfileCustomizationUiTextKeys.BannerApplied), FacmDesignSystem.Success);
                else if (string.Equals(result.Status, "overridden", StringComparison.OrdinalIgnoreCase))
                    SetStatus(TP(LeagueProfileCustomizationUiTextKeys.BannerOverridden), FacmDesignSystem.Warning);
                else if (string.Equals(result.Status, "unverified", StringComparison.OrdinalIgnoreCase))
                    SetStatus(TP(LeagueProfileCustomizationUiTextKeys.BannerUnverified), FacmDesignSystem.Warning);
                else
                    SetStatus(TP(LeagueProfileCustomizationUiTextKeys.BannerUnavailable), FacmDesignSystem.Warning);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("League last-season banner apply failed", exception);
                if (!IsDisposed)
                    SetStatus(TP(LeagueProfileCustomizationUiTextKeys.BannerWriteFailed), FacmDesignSystem.Error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void ApplySnapshot(LeaguePresenceSnapshot snapshot, bool updateSignature, bool updateRank)
        {
            if (snapshot == null || !snapshot.Connected)
            {
                _currentValue.Text = T(LeaguePresenceUiTextKeys.Unavailable);
                _currentValue.ForeColor = FacmDesignSystem.TextMuted;
                return;
            }
            _currentValue.Text = string.Format(
                T(LeaguePresenceUiTextKeys.CurrentFormat),
                DisplayMode(snapshot));
            _currentValue.ForeColor = FacmDesignSystem.Text;
            if (updateSignature && _signature != null && !_signature.IsDisposed)
                _signature.Text = snapshot.StatusMessage ?? string.Empty;
            if (updateRank)
            {
                SelectOption(_rankQueue, snapshot.RankedLeagueQueue);
                SelectOption(_rankTier, snapshot.RankedLeagueTier);
                if (!IsApexTier(SelectedValue(_rankTier)))
                    SelectOption(_rankDivision, snapshot.RankedLeagueDivision);
                UpdateRankDivisionEnabled();
            }
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

        private static string SelectedValue(ComboBox combo)
        {
            var option = combo == null ? null : combo.SelectedItem as RankOption;
            return option == null ? string.Empty : option.Value;
        }

        private static void SelectOption(ComboBox combo, string value)
        {
            if (combo == null || string.IsNullOrWhiteSpace(value)) return;
            for (var index = 0; index < combo.Items.Count; index++)
            {
                var option = combo.Items[index] as RankOption;
                if (option != null && string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = index;
                    return;
                }
            }
        }

        private static bool IsApexTier(string tier)
        {
            return string.Equals(tier, "MASTER", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tier, "GRANDMASTER", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tier, "CHALLENGER", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateRankDivisionEnabled()
        {
            if (_rankDivision == null || _rankDivision.IsDisposed) return;
            _rankDivision.Enabled = !_busy && !IsApexTier(SelectedValue(_rankTier));
        }

        private void SetStatus(string text, Color color)
        {
            if (_statusValue == null || _statusValue.IsDisposed) return;
            _statusValue.Text = text ?? string.Empty;
            _statusValue.ForeColor = color;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _refreshButton.Enabled = !busy;
            _signature.Enabled = !busy;
            _signatureSave.Enabled = !busy;
            _rankQueue.Enabled = !busy;
            _rankTier.Enabled = !busy;
            _rankDivision.Enabled = !busy;
            _rankSave.Enabled = !busy;
            _profileButton.Enabled = !busy && _profileService != null && _regaliaService != null;
            _bannerButton.Enabled = !busy && _challengePreferencesService != null;
            foreach (var button in _choiceButtons) button.Enabled = !busy;
            UpdateRankDivisionEnabled();
        }

        private string T(string key)
        {
            return LeaguePresenceText.Get(_ui, key);
        }

        private string TP(string key)
        {
            return LeagueProfileCustomizationText.Get(_ui, key);
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

        private sealed class RankOption
        {
            public RankOption(string label, string value)
            {
                Label = label ?? string.Empty;
                Value = value ?? string.Empty;
            }

            public string Label { get; private set; }
            public string Value { get; private set; }
            public override string ToString() { return Label; }
        }
    }
}
