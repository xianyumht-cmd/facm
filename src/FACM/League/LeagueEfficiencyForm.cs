using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueEfficiencyForm : Form
    {
        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _ui;
        private readonly TextBox _exitGame;
        private readonly TextBox _closeLobby;
        private readonly CheckBox _autoHonor;
        private readonly ComboBox _honorStrategy;
        private readonly CheckBox _autoReturn;
        private readonly CheckBox _autoSearch;
        private readonly CheckBox _autoAccept;
        private readonly CheckBox _autoReconnect;
        private readonly CheckBox _autoAramTeamSide;
        private readonly CheckBox _aramTeamSideVisibleToTeam;
        private readonly TextBox _loginSignature;
        private readonly CheckBox _autoLoginSignature;
        private readonly CheckBox _autoHandleInvitations;
        private readonly CheckBox _skipInvitationsWhenAway;
        private readonly CheckBox _autoSkipLeader;
        private readonly ComboBox _invitationGameType;
        private readonly ComboBox _invitationAction;
        private readonly Dictionary<string, string> _invitationPolicies;
        private readonly CheckBox _autoPick;
        private readonly CheckBox _autoBan;
        private readonly CheckBox _autoSelectPause;
        private readonly ComboBox _pickChampion;
        private readonly ComboBox _banChampion;
        private readonly CancellationTokenSource _catalogRequest = new CancellationTokenSource();
        private readonly NumericUpDown _minPartySize;
        private readonly NumericUpDown _matchmakingDelaySeconds;
        private readonly NumericUpDown _acceptDelaySeconds;
        private readonly ComboBox _stopPolicy;
        private readonly NumericUpDown _stopAfterSeconds;
        private readonly Label _help;
        private readonly Label _status;
        private readonly Label _honorStatus;
        private readonly Label _invitationStatus;
        private bool _loading = true;
        private bool _loginSignatureDirty;

        public LeagueEfficiencyForm(LeagueEfficiencyModule module, UiTextCatalog ui)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = T(LeagueEfficiencyUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 760);
            MinimumSize = new Size(680, 650);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            FacmWindowChrome.SetSubtitle(this, T(LeagueEfficiencyUiTextKeys.Hint));

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                ColumnCount = 1,
                RowCount = 34,
                BackColor = FacmDesignSystem.Canvas,
                AutoScroll = true
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            for (var row = 0; row < 5; row++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowCount = root.RowStyles.Count;

            root.Controls.Add(new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.Title),
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            _help = new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.Hint),
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true
            };
            root.Controls.Add(_help, 0, 1);
            root.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.HotkeySection)), 0, 2);

            _exitGame = AddHotkeyRow(root, 3, T(LeagueEfficiencyUiTextKeys.ExitGame), T(LeagueEfficiencyUiTextKeys.ExitGameHint), _module.ExitGameHotkey);
            _closeLobby = AddHotkeyRow(root, 4, T(LeagueEfficiencyUiTextKeys.CloseLobby), T(LeagueEfficiencyUiTextKeys.CloseLobbyHint), _module.CloseLobbyHotkey);

            var hotkeyFooter = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = FacmDesignSystem.Canvas
            };
            hotkeyFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hotkeyFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            _status = new Label
            {
                Text = string.Empty,
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var save = CreateButton(T(LeagueEfficiencyUiTextKeys.Save), 140, FacmButtonTone.Primary);
            save.Click += delegate { SaveBindings(); };
            hotkeyFooter.Controls.Add(_status, 0, 0);
            hotkeyFooter.Controls.Add(save, 1, 0);
            root.Controls.Add(hotkeyFooter, 0, 5);

            _honorStatus = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Text = FormatHonorStatus(_module.LastHonorStatus)
            };
            root.Controls.Add(CreatePostGameHeader(), 0, 6);
            _autoHonor = AddAutomationRow(root, 7, T(LeagueEfficiencyUiTextKeys.AutoHonor), T(LeagueEfficiencyUiTextKeys.AutoHonorHint), _module.AutoHonorEnabled);
            var honorRow = (TableLayoutPanel)_autoHonor.Parent;
            honorRow.ColumnCount = 2;
            honorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            honorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
            foreach (Control child in honorRow.Controls)
                if (honorRow.GetRow(child) == 1) honorRow.SetColumnSpan(child, 2);
            _honorStrategy = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.HonorStrategyTitle),
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4, 3, 4, 3)
            };
            _honorStrategy.Items.AddRange(new object[]
            {
                T(LeagueEfficiencyUiTextKeys.HonorPreferParty),
                T(LeagueEfficiencyUiTextKeys.HonorOnlyParty),
                T(LeagueEfficiencyUiTextKeys.HonorAnyAlly),
                T(LeagueEfficiencyUiTextKeys.HonorOptOut),
                T(LeagueEfficiencyUiTextKeys.HonorIncludingOpponent)
            });
            _honorStrategy.SelectedIndex = Math.Max(0, Array.IndexOf(LeagueHonorStrategy.Choices, _module.AutoHonorStrategy));
            honorRow.Controls.Add(_honorStrategy, 1, 0);
            _autoReturn = AddAutomationRow(root, 8, T(LeagueEfficiencyUiTextKeys.AutoReturn), T(LeagueEfficiencyUiTextKeys.AutoReturnHint), _module.AutoReturnLobbyEnabled);
            _autoHonor.CheckedChanged += PostGameSettingChanged;
            _honorStrategy.SelectedIndexChanged += PostGameSettingChanged;
            _autoReturn.CheckedChanged += PostGameSettingChanged;

            root.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.NextGameSection)), 0, 9);
            _autoSearch = AddAutomationRow(root, 10, T(LeagueEfficiencyUiTextKeys.AutoMatchmaking), T(LeagueEfficiencyUiTextKeys.AutoMatchmakingHint), _module.AutoMatchmakingEnabled);
            _autoAccept = AddAutomationRow(root, 11, T(LeagueEfficiencyUiTextKeys.AutoAccept), T(LeagueEfficiencyUiTextKeys.AutoAcceptHint), _module.AutoAcceptEnabled);
            _autoSearch.CheckedChanged += MatchmakingSettingChanged;
            _autoAccept.CheckedChanged += MatchmakingSettingChanged;
            _minPartySize = AddNumberRow(root, 12, T(LeagueEfficiencyUiTextKeys.MinPartySize), T(LeagueEfficiencyUiTextKeys.MinPartySizeHint), 1m, 5m, 1m, _module.AutoMatchmakingMinPartySize, string.Empty);
            _matchmakingDelaySeconds = AddNumberRow(root, 13, T(LeagueEfficiencyUiTextKeys.MatchmakingDelay), T(LeagueEfficiencyUiTextKeys.MatchmakingDelayHint), 0m, 60m, 1m, _module.AutoMatchmakingStartDelayMs / 1000m, string.Empty);
            _acceptDelaySeconds = AddNumberRow(root, 14, T(LeagueEfficiencyUiTextKeys.AcceptDelay), T(LeagueEfficiencyUiTextKeys.AcceptDelayHint), 0m, 15m, 0.5m, _module.AutoAcceptDelayMs / 1000m, string.Empty);
            _stopPolicy = AddChoiceRow(
                root,
                15,
                T(LeagueEfficiencyUiTextKeys.MatchmakingStopPolicy),
                T(LeagueEfficiencyUiTextKeys.MatchmakingStopPolicyHint),
                new[]
                {
                    T(LeagueEfficiencyUiTextKeys.MatchmakingStopNever),
                    T(LeagueEfficiencyUiTextKeys.MatchmakingStopFixed),
                    T(LeagueEfficiencyUiTextKeys.MatchmakingStopEstimated)
                },
                StopPolicyIndex(_module.AutoMatchmakingStopPolicy));
            _stopAfterSeconds = AddNumberRow(
                root,
                16,
                T(LeagueEfficiencyUiTextKeys.MatchmakingStopAfter),
                T(LeagueEfficiencyUiTextKeys.MatchmakingStopAfterHint),
                1m,
                600m,
                1m,
                _module.AutoMatchmakingStopAfterMs / 1000m,
                string.Empty);
            _minPartySize.ValueChanged += MatchmakingSettingChanged;
            _matchmakingDelaySeconds.ValueChanged += MatchmakingSettingChanged;
            _acceptDelaySeconds.ValueChanged += MatchmakingSettingChanged;
            _stopPolicy.SelectedIndexChanged += MatchmakingSettingChanged;
            _stopAfterSeconds.ValueChanged += MatchmakingSettingChanged;
            _autoReconnect = AddAutomationRow(root, 17, T(LeagueEfficiencyUiTextKeys.AutoReconnect),
                T(LeagueEfficiencyUiTextKeys.AutoReconnectHint), _module.AutoReconnectEnabled);
            _autoReconnect.CheckedChanged += ReconnectSettingChanged;

            root.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.AutoSelectSection)), 0, 18);
            _autoPick = AddAutomationRow(root, 19, T(LeagueEfficiencyUiTextKeys.AutoPick),
                T(LeagueEfficiencyUiTextKeys.AutoPickHint), _module.AutoPickEnabled);
            _pickChampion = AddChoiceRow(root, 20, T(LeagueEfficiencyUiTextKeys.AutoPickChampion),
                T(LeagueEfficiencyUiTextKeys.AutoPickChampionHint), null, 0);
            _autoBan = AddAutomationRow(root, 21, T(LeagueEfficiencyUiTextKeys.AutoBan),
                T(LeagueEfficiencyUiTextKeys.AutoBanHint), _module.AutoBanEnabled);
            _banChampion = AddChoiceRow(root, 22, T(LeagueEfficiencyUiTextKeys.AutoBanChampion),
                T(LeagueEfficiencyUiTextKeys.AutoBanChampionHint), null, 0);
            SetChampionChoices(_pickChampion, _module.AutoPickChampionId, null);
            SetChampionChoices(_banChampion, _module.AutoBanChampionId, null);
            _autoPick.CheckedChanged += AutoSelectSettingChanged;
            _autoBan.CheckedChanged += AutoSelectSettingChanged;
            _pickChampion.SelectedIndexChanged += AutoSelectSettingChanged;
            _banChampion.SelectedIndexChanged += AutoSelectSettingChanged;

            var advanced = CreateButton(T(LeagueEfficiencyUiTextKeys.AdvancedOpen), 240, FacmButtonTone.Secondary);
            advanced.Margin = new Padding(0, 8, 0, 8);
            advanced.Click += delegate
            {
                using (var dialog = new LeagueAutoSelectProfilesForm(_module, _ui)) dialog.ShowDialog(this);
            };
            root.Controls.Add(advanced, 0, 23);
            _autoSelectPause = AddAutomationRow(root, 24, T(LeagueEfficiencyUiTextKeys.AutoSelectPause),
                T(LeagueEfficiencyUiTextKeys.AutoSelectPauseHint), _module.AutoSelectTemporarilyDisabled);
            _autoSelectPause.CheckedChanged += delegate
            {
                if (!_loading) _module.UpdateAutoSelectTemporaryPause(_autoSelectPause.Checked);
            };

            _invitationPolicies = LeagueInvitationPolicies.Parse(_module.InvitationStrategiesJson);
            var invitationHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = FacmDesignSystem.Canvas,
                Margin = Padding.Empty
            };
            invitationHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            invitationHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            invitationHeader.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.LobbyInvitationSection)), 0, 0);
            _invitationStatus = new Label
            {
                Text = string.Empty,
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };
            invitationHeader.Controls.Add(_invitationStatus, 1, 0);
            root.Controls.Add(invitationHeader, 0, 25);
            _autoHandleInvitations = AddAutomationRow(root, 26,
                T(LeagueEfficiencyUiTextKeys.HandleInvitations),
                T(LeagueEfficiencyUiTextKeys.HandleInvitationsHint), _module.AutoHandleInvitationsEnabled);
            _invitationGameType = AddChoiceRow(root, 27,
                T(LeagueEfficiencyUiTextKeys.InvitationGameType),
                T(LeagueEfficiencyUiTextKeys.InvitationGameTypeHint), LeagueInvitationPolicies.GameTypes, 0);
            _invitationAction = AddChoiceRow(root, 28,
                T(LeagueEfficiencyUiTextKeys.InvitationAction),
                T(LeagueEfficiencyUiTextKeys.InvitationActionHint), new[]
                {
                    T(LeagueEfficiencyUiTextKeys.InvitationIgnore),
                    T(LeagueEfficiencyUiTextKeys.InvitationAccept),
                    T(LeagueEfficiencyUiTextKeys.InvitationDecline)
                }, 0);
            _skipInvitationsWhenAway = AddAutomationRow(root, 29,
                T(LeagueEfficiencyUiTextKeys.SkipInvitationsWhenAway),
                T(LeagueEfficiencyUiTextKeys.SkipInvitationsWhenAwayHint), _module.SkipInvitationsWhenAway);
            _autoSkipLeader = AddAutomationRow(root, 30,
                T(LeagueEfficiencyUiTextKeys.AutoSkipLeader),
                T(LeagueEfficiencyUiTextKeys.AutoSkipLeaderHint), _module.AutoSkipLeaderEnabled);
            _autoHandleInvitations.CheckedChanged += InvitationSettingChanged;
            _skipInvitationsWhenAway.CheckedChanged += InvitationSettingChanged;
            _autoSkipLeader.CheckedChanged += InvitationSettingChanged;
            _invitationGameType.SelectedIndexChanged += delegate { RefreshInvitationAction(); };
            _invitationAction.SelectedIndexChanged += InvitationSettingChanged;
            RefreshInvitationAction();

            var scheduleFriends = CreateButton(T(LeagueEfficiencyUiTextKeys.FriendInviteOpen), 240, FacmButtonTone.Secondary);
            scheduleFriends.Margin = new Padding(0, 8, 0, 8);
            scheduleFriends.Click += delegate
            {
                using (var dialog = new LeagueFriendInvitationForm(_module, _ui)) dialog.ShowDialog(this);
            };
            root.Controls.Add(scheduleFriends, 0, 31);

            root.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.AramTeamSideSection)), 0, 32);
            _autoAramTeamSide = AddAutomationRow(root, 33, T(LeagueEfficiencyUiTextKeys.AramTeamSide),
                T(LeagueEfficiencyUiTextKeys.AramTeamSideHint), _module.AutoAramTeamSideEnabled);
            _aramTeamSideVisibleToTeam = AddAutomationRow(root, 34, T(LeagueEfficiencyUiTextKeys.AramTeamSideVisible),
                T(LeagueEfficiencyUiTextKeys.AramTeamSideVisibleHint), _module.AutoAramTeamSideVisibleToTeam);
            _autoAramTeamSide.CheckedChanged += AramSideSettingChanged;
            _aramTeamSideVisibleToTeam.CheckedChanged += AramSideSettingChanged;
            UpdateAramTeamSideControlStates();

            root.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.LoginSignatureSection)), 0, 35);
            _loginSignature = AddLoginSignatureRow(root, 36);
            _autoLoginSignature = AddAutomationRow(root, 37,
                T(LeagueEfficiencyUiTextKeys.LoginSignatureAuto),
                T(LeagueEfficiencyUiTextKeys.LoginSignatureAutoHint), _module.AutoLoginStatusMessageEnabled);
            _autoLoginSignature.CheckedChanged += LoginSignatureSettingChanged;

            _module.HonorStatusChanged += HandleHonorStatusChanged;
            AppSettings.Saved += HandleSettingsSaved;
            FormClosed += HandleFormClosed;
            Controls.Add(root);
            _loading = false;
            UpdateMatchmakingControlStates();
            _ = LoadChampionCatalogAsync();
        }

        private Control CreatePostGameHeader()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = FacmDesignSystem.Canvas,
                Margin = Padding.Empty
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            panel.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.PostGameSection)), 0, 0);
            panel.Controls.Add(_honorStatus, 1, 0);
            return panel;
        }

        private Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Control CreateSettingDescription(string title, string hint)
        {
            var copy = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));
            copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var heading = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var description = new Label
            {
                Text = hint ?? string.Empty,
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            copy.Controls.Add(heading, 0, 0);
            copy.Controls.Add(description, 0, 1);
            WireHelp(copy, hint);
            WireHelp(heading, hint);
            WireHelp(description, hint);
            return copy;
        }

        private TextBox AddHotkeyRow(TableLayoutPanel parent, int row, string title, string hint, string value)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = FacmDesignSystem.Surface,
                Padding = new Padding(12, 7, 12, 7)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));

            var titleLabel = CreateSettingDescription(title, hint);
            panel.Controls.Add(titleLabel, 0, 0);

            var box = new TextBox
            {
                Text = value ?? string.Empty,
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 3, 6, 3),
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            panel.Controls.Add(box, 1, 0);

            var capture = CreateButton(T(LeagueEfficiencyUiTextKeys.Capture), 68, FacmButtonTone.Secondary);
            capture.Click += delegate
            {
                using (var dialog = new LeagueHotkeyCaptureDialog(_ui))
                {
                    dialog.TopMost = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        box.Text = dialog.Binding == null ? string.Empty : dialog.Binding.ToString();
                }
            };
            panel.Controls.Add(capture, 2, 0);

            var clear = CreateButton(T(LeagueEfficiencyUiTextKeys.Clear), 68, FacmButtonTone.Secondary);
            clear.Click += delegate { box.Text = string.Empty; };
            panel.Controls.Add(clear, 3, 0);

            WireHelp(panel, hint);
            WireHelp(titleLabel, hint);
            WireHelp(box, hint);
            WireHelp(capture, hint);
            WireHelp(clear, hint);
            parent.Controls.Add(panel, 0, row);
            return box;
        }

        private CheckBox AddAutomationRow(TableLayoutPanel parent, int row, string title, string hint, bool value)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = FacmDesignSystem.Surface,
                Padding = new Padding(12, 7, 12, 7)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var check = new FacmToggleSwitch
            {
                Text = title,
                Checked = value,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
            };
            panel.Controls.Add(check, 0, 0);
            var description = new Label
            {
                Text = hint ?? string.Empty,
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            panel.Controls.Add(description, 0, 1);
            WireHelp(description, hint);
            WireHelp(panel, hint);
            WireHelp(check, hint);
            parent.Controls.Add(panel, 0, row);
            return check;
        }

        private NumericUpDown AddNumberRow(
            TableLayoutPanel parent,
            int row,
            string title,
            string hint,
            decimal minimum,
            decimal maximum,
            decimal increment,
            decimal value,
            string suffix)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = FacmDesignSystem.Surface,
                Padding = new Padding(12, 7, 12, 7)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            var titleLabel = CreateSettingDescription(title, hint);
            var input = new NumericUpDown
            {
                Minimum = minimum,
                Maximum = maximum,
                Increment = increment,
                DecimalPlaces = increment < 1m ? 1 : 0,
                Value = Math.Max(minimum, Math.Min(maximum, value)),
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 4, 2),
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Right
            };
            var unit = new Label
            {
                Text = suffix ?? string.Empty,
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(titleLabel, 0, 0);
            panel.Controls.Add(input, 1, 0);
            panel.Controls.Add(unit, 2, 0);
            WireHelp(panel, hint);
            WireHelp(titleLabel, hint);
            WireHelp(input, hint);
            WireHelp(unit, hint);
            parent.Controls.Add(panel, 0, row);
            return input;
        }

        private ComboBox AddChoiceRow(
            TableLayoutPanel parent,
            int row,
            string title,
            string hint,
            string[] choices,
            int selectedIndex)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = FacmDesignSystem.Surface,
                Padding = new Padding(12, 7, 12, 7)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            var titleLabel = CreateSettingDescription(title, hint);
            var input = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4, 2, 4, 2)
            };
            if (choices != null) input.Items.AddRange(choices);
            input.SelectedIndex = input.Items.Count == 0 ? -1 : Math.Max(0, Math.Min(input.Items.Count - 1, selectedIndex));
            panel.Controls.Add(titleLabel, 0, 0);
            panel.Controls.Add(input, 1, 0);
            WireHelp(panel, hint);
            WireHelp(titleLabel, hint);
            WireHelp(input, hint);
            parent.Controls.Add(panel, 0, row);
            return input;
        }

        private void WireHelp(Control control, string text)
        {
            if (control == null) return;
            control.MouseEnter += delegate
            {
                if (_help != null && !_help.IsDisposed) _help.Text = text;
            };
            control.MouseLeave += delegate
            {
                if (_help != null && !_help.IsDisposed) _help.Text = T(LeagueEfficiencyUiTextKeys.Hint);
            };
        }

        private FacmActionButton CreateButton(string text, int width, FacmButtonTone tone)
        {
            return new FacmActionButton
            {
                Text = text,
                Width = width,
                Height = 30,
                Dock = DockStyle.Fill,
                Tone = tone,
                Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
                Margin = new Padding(4, 2, 4, 2)
            };
        }

        private void SaveBindings()
        {
            string error;
            if (_module.TryUpdateBindings(_exitGame.Text, _closeLobby.Text, out error))
            {
                _exitGame.Text = _module.ExitGameHotkey;
                _closeLobby.Text = _module.CloseLobbyHotkey;
                _status.ForeColor = FacmDesignSystem.Success;
                _status.Text = T(LeagueEfficiencyUiTextKeys.Saved);
            }
            else
            {
                _status.ForeColor = FacmDesignSystem.Warning;
                _status.Text = string.Format(T(LeagueEfficiencyUiTextKeys.SaveFailed), error ?? string.Empty);
            }
        }

        private void PostGameSettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _module.UpdatePostGameSettings(_autoHonor.Checked, _autoReturn.Checked,
                LeagueHonorStrategy.Choices[Math.Max(0, _honorStrategy.SelectedIndex)]);
            _status.ForeColor = FacmDesignSystem.Success;
            _status.Text = T(LeagueEfficiencyUiTextKeys.PostGameSaved);
        }

        private void MatchmakingSettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            UpdateMatchmakingControlStates();
            _module.UpdateMatchmakingSettings(
                _autoSearch.Checked,
                _autoAccept.Checked,
                Decimal.ToInt32(_minPartySize.Value),
                Decimal.ToInt32(_matchmakingDelaySeconds.Value * 1000m),
                Decimal.ToInt32(_acceptDelaySeconds.Value * 1000m),
                SelectedStopPolicy(),
                Decimal.ToInt32(_stopAfterSeconds.Value * 1000m));
            _status.ForeColor = FacmDesignSystem.Success;
            _status.Text = T(LeagueEfficiencyUiTextKeys.NextGameSaved);
        }

        private void ReconnectSettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _module.UpdateReconnectSetting(_autoReconnect.Checked);
            _status.ForeColor = FacmDesignSystem.Success;
            _status.Text = T(LeagueEfficiencyUiTextKeys.NextGameSaved);
        }

        private void AramSideSettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            UpdateAramTeamSideControlStates();
            _module.UpdateAramTeamSideSettings(_autoAramTeamSide.Checked, _aramTeamSideVisibleToTeam.Checked);
            _status.ForeColor = FacmDesignSystem.Success;
            _status.Text = T(LeagueEfficiencyUiTextKeys.NextGameSaved);
        }

        private void UpdateAramTeamSideControlStates()
        {
            if (_aramTeamSideVisibleToTeam != null)
                _aramTeamSideVisibleToTeam.Enabled = _autoAramTeamSide != null && _autoAramTeamSide.Checked;
        }

        private TextBox AddLoginSignatureRow(TableLayoutPanel parent, int row)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = FacmDesignSystem.Surface,
                Padding = new Padding(12, 7, 12, 7)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            var title = T(LeagueEfficiencyUiTextKeys.LoginSignatureText);
            var hint = T(LeagueEfficiencyUiTextKeys.LoginSignatureTextHint);
            panel.Controls.Add(CreateSettingDescription(title, hint), 0, 0);
            var input = new TextBox
            {
                Text = _module.AutoLoginStatusMessage,
                MaxLength = LeaguePresenceService.MaximumStatusMessageLength,
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 14, 4, 4),
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = title
            };
            input.TextChanged += delegate { if (!_loading) _loginSignatureDirty = true; };
            panel.Controls.Add(input, 1, 0);
            var save = CreateButton(T(LeagueEfficiencyUiTextKeys.LoginSignatureSave), 104, FacmButtonTone.Secondary);
            save.Margin = new Padding(4, 12, 0, 4);
            save.Click += delegate { SaveLoginSignatureSetting(); };
            panel.Controls.Add(save, 2, 0);
            WireHelp(panel, hint);
            WireHelp(input, hint);
            WireHelp(save, hint);
            parent.Controls.Add(panel, 0, row);
            return input;
        }

        private void LoginSignatureSettingChanged(object sender, EventArgs e)
        {
            if (!_loading) SaveLoginSignatureSetting();
        }

        private void SaveLoginSignatureSetting()
        {
            if (_loading) return;
            _module.UpdateLoginStatusMessage(_autoLoginSignature.Checked, _loginSignature.Text);
            _loginSignatureDirty = false;
            _status.ForeColor = FacmDesignSystem.Success;
            _status.Text = T(LeagueEfficiencyUiTextKeys.LoginSignatureSaved);
        }

        private void RefreshInvitationAction()
        {
            var gameType = _invitationGameType == null ? null : _invitationGameType.SelectedItem as string;
            var action = LeagueInvitationPolicies.Resolve(_invitationPolicies, gameType);
            var previouslyLoading = _loading;
            _loading = true;
            try
            {
                _invitationAction.SelectedIndex = action == LeagueInvitationPolicies.Accept ? 1 :
                    action == LeagueInvitationPolicies.Decline ? 2 : 0;
            }
            finally { _loading = previouslyLoading; }
            UpdateInvitationControlStates();
        }

        private void HandleSettingsSaved(AppSettings settings)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                // Cloud-first restores can happen while this page is open. Rebind from the
                // actual settings object on the UI thread instead of retaining stale strategies.
                BeginInvoke(new Action(delegate
                {
                    RefreshInvitationSettingsFromModule();
                    RefreshAramTeamSideSettingsFromModule();
                    RefreshLoginSignatureSettingsFromModule();
                }));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void RefreshInvitationSettingsFromModule()
        {
            if (IsDisposed || Disposing || _loading) return;
            var saved = LeagueInvitationPolicies.Parse(_module.InvitationStrategiesJson);
            if (_autoHandleInvitations.Checked == _module.AutoHandleInvitationsEnabled &&
                _skipInvitationsWhenAway.Checked == _module.SkipInvitationsWhenAway &&
                _autoSkipLeader.Checked == _module.AutoSkipLeaderEnabled &&
                LeagueInvitationPolicies.Serialize(_invitationPolicies) == LeagueInvitationPolicies.Serialize(saved)) return;
            _loading = true;
            try
            {
                _invitationPolicies.Clear();
                foreach (var item in saved) _invitationPolicies[item.Key] = item.Value;
                _autoHandleInvitations.Checked = _module.AutoHandleInvitationsEnabled;
                _skipInvitationsWhenAway.Checked = _module.SkipInvitationsWhenAway;
                _autoSkipLeader.Checked = _module.AutoSkipLeaderEnabled;
                RefreshInvitationAction();
            }
            finally { _loading = false; }
        }

        private void RefreshAramTeamSideSettingsFromModule()
        {
            if (IsDisposed || Disposing || _loading) return;
            if (_autoAramTeamSide.Checked == _module.AutoAramTeamSideEnabled &&
                _aramTeamSideVisibleToTeam.Checked == _module.AutoAramTeamSideVisibleToTeam) return;
            _loading = true;
            try
            {
                _autoAramTeamSide.Checked = _module.AutoAramTeamSideEnabled;
                _aramTeamSideVisibleToTeam.Checked = _module.AutoAramTeamSideVisibleToTeam;
                UpdateAramTeamSideControlStates();
            }
            finally { _loading = false; }
        }

        private void RefreshLoginSignatureSettingsFromModule()
        {
            if (IsDisposed || Disposing || _loading || _loginSignatureDirty) return;
            _loading = true;
            try
            {
                _loginSignature.Text = _module.AutoLoginStatusMessage;
                _autoLoginSignature.Checked = _module.AutoLoginStatusMessageEnabled;
            }
            finally { _loading = false; }
        }

        private void UpdateInvitationControlStates()
        {
            var enabled = _autoHandleInvitations != null && _autoHandleInvitations.Checked;
            if (_invitationGameType != null) _invitationGameType.Enabled = enabled;
            if (_invitationAction != null) _invitationAction.Enabled = enabled;
            if (_skipInvitationsWhenAway != null) _skipInvitationsWhenAway.Enabled = enabled;
        }

        private void InvitationSettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            if (ReferenceEquals(sender, _invitationAction))
            {
                var gameType = _invitationGameType.SelectedItem as string;
                if (string.IsNullOrEmpty(gameType)) return;
                _invitationPolicies[gameType] = _invitationAction.SelectedIndex == 1 ? LeagueInvitationPolicies.Accept :
                    _invitationAction.SelectedIndex == 2 ? LeagueInvitationPolicies.Decline : LeagueInvitationPolicies.Ignore;
            }
            UpdateInvitationControlStates();
            try
            {
                _module.UpdateInvitationSettings(_autoHandleInvitations.Checked, _skipInvitationsWhenAway.Checked,
                    LeagueInvitationPolicies.Serialize(_invitationPolicies), _autoSkipLeader.Checked);
                _status.ForeColor = FacmDesignSystem.Success;
                _status.Text = T(LeagueEfficiencyUiTextKeys.LobbyInvitationSaved);
                _invitationStatus.ForeColor = FacmDesignSystem.Success;
                _invitationStatus.Text = T(LeagueEfficiencyUiTextKeys.LobbyInvitationSaved);
            }
            catch (Exception exception)
            {
                _status.ForeColor = FacmDesignSystem.Warning;
                _status.Text = string.Format(T(LeagueEfficiencyUiTextKeys.SaveFailed), exception.GetType().Name);
                _invitationStatus.ForeColor = FacmDesignSystem.Warning;
                _invitationStatus.Text = _status.Text;
            }
        }

        private sealed class ChampionChoice
        {
            public int Id;
            public string Name;
            public override string ToString() { return Name ?? string.Empty; }
        }

        private void SetChampionChoices(
            ComboBox combo, int selectedId, IReadOnlyList<LeagueProfileChampionOption> champions)
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                combo.Items.Add(new ChampionChoice { Id = 0, Name = T(LeagueEfficiencyUiTextKeys.ChampionNotSet) });
                var selectedIndex = 0;
                if (champions != null)
                {
                    foreach (var champion in champions)
                    {
                        if (champion == null || champion.Id <= 0 || string.IsNullOrWhiteSpace(champion.Name)) continue;
                        combo.Items.Add(new ChampionChoice { Id = champion.Id, Name = champion.Name });
                        if (champion.Id == selectedId) selectedIndex = combo.Items.Count - 1;
                    }
                }
                if (selectedId > 0 && selectedIndex == 0)
                {
                    combo.Items.Add(new ChampionChoice { Id = selectedId, Name = "#" + selectedId });
                    selectedIndex = combo.Items.Count - 1;
                }
                combo.SelectedIndex = selectedIndex;
            }
            finally { combo.EndUpdate(); }
        }

        private async Task LoadChampionCatalogAsync()
        {
            try
            {
                var champions = await _module.LoadChampionsAsync(_catalogRequest.Token);
                if (_catalogRequest.IsCancellationRequested || IsDisposed || Disposing) return;
                if (champions == null || champions.Count == 0)
                {
                    _help.Text = T(LeagueEfficiencyUiTextKeys.ChampionCatalogUnavailable);
                    return;
                }
                _loading = true;
                try
                {
                    SetChampionChoices(_pickChampion, SelectedChampionId(_pickChampion), champions);
                    SetChampionChoices(_banChampion, SelectedChampionId(_banChampion), champions);
                }
                finally { _loading = false; }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (!IsDisposed && !Disposing)
                {
                    _help.Text = T(LeagueEfficiencyUiTextKeys.ChampionCatalogUnavailable);
                    AppLog.Info("Champion catalog loading skipped: " + exception.GetType().Name);
                }
            }
        }

        private static int SelectedChampionId(ComboBox combo)
        {
            var choice = combo == null ? null : combo.SelectedItem as ChampionChoice;
            return choice == null ? 0 : choice.Id;
        }

        private void AutoSelectSettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            var pickId = SelectedChampionId(_pickChampion);
            var banId = SelectedChampionId(_banChampion);
            var profiles = LeagueAutoSelectProfiles.Parse(_module.AutoSelectProfilesJson);
            var missingPick = _autoPick.Checked && pickId <= 0 &&
                !profiles.Any(profile => profile.Picks.Count > 0);
            var missingBan = _autoBan.Checked && banId <= 0 &&
                !profiles.Any(profile => profile.Bans.Count > 0);
            var missingChampion = missingPick || missingBan;
            if (missingChampion)
            {
                _loading = true;
                try
                {
                    if (missingPick) _autoPick.Checked = false;
                    if (missingBan) _autoBan.Checked = false;
                }
                finally { _loading = false; }
            }
            try
            {
                _module.UpdateAutoSelectSettings(_autoPick.Checked, pickId, _autoBan.Checked, banId);
                _status.ForeColor = missingChampion ? FacmDesignSystem.Warning : FacmDesignSystem.Success;
                _status.Text = missingChampion ? T(LeagueEfficiencyUiTextKeys.ChampionNotSet) :
                    T(LeagueEfficiencyUiTextKeys.AutoSelectSaved);
            }
            catch (Exception exception)
            {
                _status.ForeColor = FacmDesignSystem.Warning;
                _status.Text = T(LeagueEfficiencyUiTextKeys.SaveFailed).Replace("{0}", exception.GetType().Name);
            }
        }

        private void UpdateMatchmakingControlStates()
        {
            var autoSearch = _autoSearch != null && _autoSearch.Checked;
            if (_minPartySize != null) _minPartySize.Enabled = autoSearch;
            if (_matchmakingDelaySeconds != null) _matchmakingDelaySeconds.Enabled = autoSearch;
            if (_stopPolicy != null) _stopPolicy.Enabled = autoSearch;
            if (_stopAfterSeconds != null)
                _stopAfterSeconds.Enabled = autoSearch && _stopPolicy != null && _stopPolicy.SelectedIndex == 1;
            if (_acceptDelaySeconds != null) _acceptDelaySeconds.Enabled = _autoAccept != null && _autoAccept.Checked;
        }

        private string SelectedStopPolicy()
        {
            if (_stopPolicy == null) return LeagueMatchmakingStopPolicyCodec.Never;
            if (_stopPolicy.SelectedIndex == 1) return LeagueMatchmakingStopPolicyCodec.Fixed;
            if (_stopPolicy.SelectedIndex == 2) return LeagueMatchmakingStopPolicyCodec.Estimated;
            return LeagueMatchmakingStopPolicyCodec.Never;
        }

        private static int StopPolicyIndex(string value)
        {
            var normalized = LeagueMatchmakingStopPolicyCodec.Normalize(value);
            if (normalized == LeagueMatchmakingStopPolicyCodec.Fixed) return 1;
            if (normalized == LeagueMatchmakingStopPolicyCodec.Estimated) return 2;
            return 0;
        }

        private void HandleHonorStatusChanged(LeagueHonorAttemptStatus status)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (!IsHandleCreated) return;
                BeginInvoke(new Action(delegate { ApplyHonorStatus(status); }));
            }
            catch (InvalidOperationException) { }
        }

        private void ApplyHonorStatus(LeagueHonorAttemptStatus status)
        {
            if (_honorStatus == null || _honorStatus.IsDisposed) return;
            _honorStatus.Text = FormatHonorStatus(status);
            var state = status == null ? string.Empty : status.State ?? string.Empty;
            if (string.Equals(state, "success", StringComparison.Ordinal))
                _honorStatus.ForeColor = FacmDesignSystem.Success;
            else if (string.Equals(state, "failed", StringComparison.Ordinal))
                _honorStatus.ForeColor = FacmDesignSystem.Warning;
            else if (string.Equals(state, "unknown", StringComparison.Ordinal))
                _honorStatus.ForeColor = FacmDesignSystem.Warning;
            else
                _honorStatus.ForeColor = FacmDesignSystem.TextMuted;
        }

        private string FormatHonorStatus(LeagueHonorAttemptStatus status)
        {
            if (status == null) return T(LeagueEfficiencyUiTextKeys.HonorLastNone);
            var time = status.CompletedAtUtc == default(DateTime)
                ? DateTime.Now.ToString("HH:mm")
                : status.CompletedAtUtc.ToLocalTime().ToString("HH:mm");
            if (string.Equals(status.State, "success", StringComparison.Ordinal))
                return string.Format(T(LeagueEfficiencyUiTextKeys.HonorLastSuccess), time);
            if (string.Equals(status.State, "skipped", StringComparison.Ordinal))
                return string.Format(T(LeagueEfficiencyUiTextKeys.HonorLastSkipped), time);
            if (string.Equals(status.State, "unknown", StringComparison.Ordinal))
                return string.Format(T(LeagueEfficiencyUiTextKeys.HonorLastUnknown), time);
            return string.Format(T(LeagueEfficiencyUiTextKeys.HonorLastFailed), time);
        }

        private void HandleFormClosed(object sender, FormClosedEventArgs e)
        {
            _catalogRequest.Cancel();
            _catalogRequest.Dispose();
            _module.HonorStatusChanged -= HandleHonorStatusChanged;
            AppSettings.Saved -= HandleSettingsSaved;
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
    }

    internal sealed class LeagueHotkeyCaptureDialog : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly Label _prompt;

        public LeagueHotkeyCaptureDialog(UiTextCatalog ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CaptureTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            FacmWindowChrome.SetSubtitle(this, LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CapturePrompt));
            _prompt = new Label
            {
                Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CapturePrompt),
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent
            };
            Controls.Add(_prompt);
            KeyDown += HandleKeyDown;
        }

        public LeagueHotkeyBinding Binding { get; private set; }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Escape && !e.Control && !e.Alt && !e.Shift)
            {
                Binding = LeagueHotkeyBinding.Disabled;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var candidate = LeagueHotkeyBinding.FromKeyEvent(e.KeyData);
            string parsedError;
            LeagueHotkeyBinding parsed;
            if (!LeagueHotkeyBinding.TryParse(candidate.ToString(), out parsed, out parsedError) || !parsed.Enabled)
            {
                _prompt.ForeColor = FacmDesignSystem.Warning;
                _prompt.Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CaptureUnsafe);
                return;
            }
            Binding = parsed;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
