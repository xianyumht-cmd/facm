using System;
using System.Collections.Generic;
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

            AutoScaleMode = AutoScaleMode.None;
            Text = T(LeaguePresenceUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(860, 690);
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F),
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
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

            var presencePanel = CreateGroupSurface();
            presencePanel.Controls.Add(currentCaption);
            presencePanel.Controls.Add(_currentValue);
            foreach (var choice in _choiceButtons) presencePanel.Controls.Add(choice);

            var signaturePanel = CreateGroupSurface();
            signaturePanel.Controls.Add(signatureCaption);
            signaturePanel.Controls.Add(signatureHint);
            signaturePanel.Controls.Add(_signature);
            signaturePanel.Controls.Add(_signatureSave);

            var rankPanel = CreateGroupSurface();
            rankPanel.Controls.Add(rankCaption);
            rankPanel.Controls.Add(rankHint);
            foreach (var control in new Control[]
            {
                _rankQueue, _rankTier, _rankDivision, _rankSave
            }) rankPanel.Controls.Add(control);
            var rankLabels = new List<Label>();
            foreach (Control control in Controls)
            {
                var label = control as Label;
                if (label != null && label != title && label != hint && label != _statusValue &&
                    label != footer && label != profileCaption && label != profileHint &&
                    label != bannerCaption && label != bannerHint)
                    rankLabels.Add(label);
            }
            foreach (var label in rankLabels) rankPanel.Controls.Add(label);

            var profilePanel = CreateGroupSurface();
            foreach (var control in new Control[]
            {
                profileCaption, profileHint, _profileButton,
                bannerCaption, bannerHint, _bannerButton
            }) profilePanel.Controls.Add(control);

            var viewport = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas,
                TabStop = false
            };
            var original = new List<Control>();
            foreach (Control control in Controls) original.Add(control);
            foreach (var control in original)
            {
                Controls.Remove(control);
                viewport.Controls.Add(control);
            }
            foreach (var card in new[] { presencePanel, signaturePanel, rankPanel, profilePanel })
                viewport.Controls.Add(card);
            Controls.Add(viewport);

            var arranging = false;
            Action arrange = delegate
            {
                if (arranging || viewport.IsDisposed || IsDisposed || viewport.ClientSize.Width < 40) return;
                arranging = true;
                var currentScroll = Math.Max(0, viewport.VerticalScroll.Value);
                viewport.SuspendLayout();
                try
                {
                    viewport.AutoScrollPosition = Point.Empty;
                    Func<int, int> unit = number => Math.Max(1,
                        (int)Math.Round(number * Math.Max(96F, DeviceDpi) / 96F));
                    Func<Label, int, int> textHeight = (label, width) =>
                        Math.Max(unit(22), TextRenderer.MeasureText(label.Text ?? string.Empty, label.Font,
                            new Size(Math.Max(1, width), 16384),
                            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + unit(2));
                    var padding = viewport.ClientSize.Width < unit(760) ? unit(16) : unit(24);
                    var width = Math.Min(unit(900), Math.Max(unit(280),
                        viewport.ClientSize.Width - padding * 2 - SystemInformation.VerticalScrollBarWidth));
                    var inner = width - unit(32);
                    var y = padding;

                    var refreshWidth = Math.Max(unit(94),
                        TextRenderer.MeasureText(_refreshButton.Text, _refreshButton.Font).Width + unit(28));
                    var titleWidth = Math.Max(unit(100), width - refreshWidth - unit(12));
                    title.SetBounds(padding, y, titleWidth, unit(36));
                    _refreshButton.SetBounds(padding + width - refreshWidth, y,
                        refreshWidth, unit(34));
                    y += unit(44);
                    var hintHeight = textHeight(hint, width);
                    hint.SetBounds(padding, y, width, hintHeight);
                    y += hintHeight + unit(20);

                    var columns = inner >= unit(580) ? 3 : 2;
                    var gap = unit(8);
                    var choiceWidth = Math.Max(unit(90), (inner - (columns - 1) * gap) / columns);
                    var choiceHeight = unit(42);
                    currentCaption.SetBounds(unit(16), unit(14), unit(90), unit(25));
                    _currentValue.SetBounds(unit(100), unit(12),
                        Math.Max(unit(110), inner - unit(112)), unit(28));
                    for (var i = 0; i < _choiceButtons.Length; i++)
                    {
                        var row = i / columns;
                        var column = i % columns;
                        _choiceButtons[i].SetBounds(unit(16) + column * (choiceWidth + gap),
                            unit(48) + row * (choiceHeight + gap), choiceWidth, choiceHeight);
                    }
                    var presenceHeight = unit(48) + ((_choiceButtons.Length + columns - 1) / columns) *
                        (choiceHeight + gap) + unit(9);
                    presencePanel.SetBounds(padding, y, width, presenceHeight);
                    y += presenceHeight + unit(16);

                    signatureCaption.SetBounds(unit(16), unit(12), inner, unit(25));
                    var signatureHintHeight = textHeight(signatureHint, inner);
                    signatureHint.SetBounds(unit(16), unit(36), inner, signatureHintHeight);
                    var fieldY = unit(36) + signatureHintHeight + unit(9);
                    var signSaveWidth = Math.Max(unit(94),
                        TextRenderer.MeasureText(_signatureSave.Text, _signatureSave.Font).Width + unit(26));
                    _signature.SetBounds(unit(16), fieldY,
                        Math.Max(unit(96), inner - signSaveWidth - unit(10)), unit(33));
                    _signatureSave.SetBounds(unit(16) + inner - signSaveWidth,
                        fieldY, signSaveWidth, unit(34));
                    var signatureHeight = fieldY + unit(33) + unit(16);
                    signaturePanel.SetBounds(padding, y, width, signatureHeight);
                    y += signatureHeight + unit(16);

                    rankCaption.SetBounds(unit(16), unit(12), inner, unit(25));
                    var rankHintHeight = textHeight(rankHint, inner);
                    rankHint.SetBounds(unit(16), unit(36), inner, rankHintHeight);
                    var rankLabelTop = unit(36) + rankHintHeight + unit(12);
                    var rankFieldsTop = rankLabelTop + unit(23);
                    var compact = inner < unit(500);
                    var fieldGap = unit(9);
                    var saveWidth = Math.Max(unit(90),
                        TextRenderer.MeasureText(_rankSave.Text, _rankSave.Font).Width + unit(24));
                    var totalFields = compact ? inner : inner - saveWidth - fieldGap;
                    var divisionWidth = Math.Max(unit(60), totalFields / 7);
                    var queueWidth = Math.Max(unit(100), (totalFields - divisionWidth - fieldGap * 2) * 52 / 100);
                    var tierWidth = totalFields - divisionWidth - queueWidth - fieldGap * 2;
                    var x1 = unit(16), x2 = x1 + queueWidth + fieldGap;
                    var x3 = x2 + tierWidth + fieldGap;
                    var labels = new[] { _rankQueue, _rankTier, _rankDivision };
                    var fieldWidths = new[] { queueWidth, tierWidth, divisionWidth };
                    var fieldX = new[] { x1, x2, x3 };
                    for (var i = 0; i < labels.Length; i++)
                        labels[i].SetBounds(fieldX[i], rankFieldsTop, fieldWidths[i], unit(31));
                    for (var i = 0; i < rankLabels.Count && i < 3; i++)
                        rankLabels[i].SetBounds(fieldX[i], rankLabelTop, fieldWidths[i], unit(20));
                    var rankActionTop = compact ? rankFieldsTop + unit(41) : rankFieldsTop;
                    _rankSave.SetBounds(compact ? unit(16) : unit(16) + inner - saveWidth,
                        rankActionTop, saveWidth, unit(33));
                    var rankHeight = rankActionTop + unit(33) + unit(16);
                    rankPanel.SetBounds(padding, y, width, rankHeight);
                    y += rankHeight + unit(16);

                    var profileButtonWidth = Math.Max(unit(110),
                        TextRenderer.MeasureText(_profileButton.Text, _profileButton.Font).Width + unit(24));
                    var bannerButtonWidth = Math.Max(unit(110),
                        TextRenderer.MeasureText(_bannerButton.Text, _bannerButton.Font).Width + unit(24));
                    var descWidth = Math.Max(unit(140),
                        inner - Math.Max(profileButtonWidth, bannerButtonWidth) - unit(18));
                    var profileHintHeight = textHeight(profileHint, descWidth);
                    profileCaption.SetBounds(unit(16), unit(10), descWidth, unit(24));
                    profileHint.SetBounds(unit(16), unit(34), descWidth, profileHintHeight);
                    var profileRowHeight = Math.Max(unit(84), unit(34) + profileHintHeight + unit(12));
                    _profileButton.SetBounds(unit(16) + inner - profileButtonWidth,
                        unit(22), profileButtonWidth, unit(34));
                    bannerCaption.SetBounds(unit(16), profileRowHeight + unit(6), descWidth, unit(24));
                    var bannerHintHeight = textHeight(bannerHint, descWidth);
                    bannerHint.SetBounds(unit(16), profileRowHeight + unit(30), descWidth, bannerHintHeight);
                    _bannerButton.SetBounds(unit(16) + inner - bannerButtonWidth,
                        profileRowHeight + unit(17), bannerButtonWidth, unit(34));
                    var profileHeight = profileRowHeight + Math.Max(unit(84),
                        unit(30) + bannerHintHeight + unit(12));
                    profilePanel.SetBounds(padding, y, width, profileHeight);
                    y += profileHeight + unit(16);

                    var statusHeight = textHeight(_statusValue, width);
                    _statusValue.SetBounds(padding, y, width, statusHeight);
                    y += statusHeight + unit(12);
                    var footerHeight = textHeight(footer, width);
                    footer.SetBounds(padding, y, width, footerHeight);
                    viewport.AutoScrollMinSize = new Size(0, y + footerHeight + padding);
                }
                finally
                {
                    viewport.ResumeLayout(false);
                    viewport.AutoScrollPosition = new Point(0, currentScroll);
                    arranging = false;
                }
            };
            viewport.Resize += delegate { arrange(); };
            Resize += delegate { arrange(); };
            arrange();

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

        private static FacmGlassPanel CreateGroupSurface()
        {
            return new FacmGlassPanel
            {
                DrawBorder = true,
                Radius = FacmDesignSystem.CardRadius,
                BackColor = FacmDesignSystem.Surface,
                TabStop = false
            };
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold)
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F),
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
