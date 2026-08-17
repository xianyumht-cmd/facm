using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal enum LeagueManualApplyMode
    {
        Full,
        Build,
        Items
    }

    internal sealed class LeagueBuildApplyForm : Form
    {
        private const string ApplyBadgeText = "OP.GG // APPLY";
        private static readonly Color Background = Color.FromArgb(10, 15, 25);
        private static readonly Color Surface = Color.FromArgb(18, 27, 43);
        private static readonly Color SurfaceRaised = Color.FromArgb(25, 38, 59);
        private static readonly Color TextPrimary = Color.FromArgb(238, 243, 252);
        private static readonly Color TextMuted = Color.FromArgb(146, 161, 188);
        private static readonly Color NeonCyan = Color.FromArgb(73, 218, 255);
        private static readonly Color NeonPurple = Color.FromArgb(154, 106, 255);
        private static readonly Color Accent = Color.FromArgb(74, 121, 236);

        private readonly LeagueBuildAdvisorDataService _readService;
        private readonly LeagueBuildApplyService _applyService;
        private readonly LeagueItemSetService _itemSetService;
        private readonly ILeagueAutoApplyExecutor _fullApplyExecutor;
        private readonly LeagueAutoApplyController _autoController;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly CheckBox _autoToggle;
        private readonly Label _autoStatusValue;
        private readonly Label _contextValue;
        private readonly TextBox _spellValue;
        private readonly TextBox _runeValue;
        private readonly TextBox _itemValue;
        private readonly Label _statusValue;
        private readonly Button _fullModeButton;
        private readonly Button _buildModeButton;
        private readonly Button _itemsModeButton;
        private readonly Button _refreshButton;
        private readonly Button _applyButton;
        private LeagueBuildAdvisorSnapshot _snapshot;
        private LeagueManualApplyMode _mode = LeagueManualApplyMode.Full;
        private bool _busy;
        private bool _syncingAutoToggle;

        public LeagueBuildApplyForm(
            LeagueBuildAdvisorDataService readService,
            LeagueBuildApplyService applyService,
            LeagueItemSetService itemSetService,
            ILeagueAutoApplyExecutor fullApplyExecutor,
            LeagueAutoApplyController autoController,
            UiTextCatalog ui)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
            _itemSetService = itemSetService ?? throw new ArgumentNullException(nameof(itemSetService));
            _fullApplyExecutor = fullApplyExecutor ?? throw new ArgumentNullException(nameof(fullApplyExecutor));
            _autoController = autoController ?? throw new ArgumentNullException(nameof(autoController));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = T(LeagueBuildApplyUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(800, 650);
            MinimumSize = new Size(740, 610);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Microsoft YaHei UI", 9F);

            Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 3,
                BackColor = NeonCyan
            });

            var title = new Label
            {
                Text = T(LeagueBuildApplyUiTextKeys.Title),
                Location = new Point(28, 18),
                Size = new Size(520, 36),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var badge = new Label
            {
                Text = ApplyBadgeText,
                Location = new Point(584, 24),
                Size = new Size(180, 24),
                ForeColor = NeonPurple,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Consolas", 9F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = T(LeagueBuildApplyUiTextKeys.ModeHint),
                Location = new Point(30, 58),
                Size = new Size(734, 40),
                ForeColor = TextMuted
            };
            var modeCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.ModeSection), 101);

            _fullModeButton = CreateModeButton(
                T(LeagueBuildApplyUiTextKeys.ModeFull),
                T(LeagueBuildApplyUiTextKeys.ModeFullHint),
                new Point(30, 128));
            _buildModeButton = CreateModeButton(
                T(LeagueBuildApplyUiTextKeys.ModeBuild),
                T(LeagueBuildApplyUiTextKeys.ModeBuildHint),
                new Point(274, 128));
            _itemsModeButton = CreateModeButton(
                T(LeagueBuildApplyUiTextKeys.ModeItems),
                T(LeagueBuildApplyUiTextKeys.ModeItemsHint),
                new Point(518, 128));
            _fullModeButton.Click += delegate { SelectMode(LeagueManualApplyMode.Full); };
            _buildModeButton.Click += delegate { SelectMode(LeagueManualApplyMode.Build); };
            _itemsModeButton.Click += delegate { SelectMode(LeagueManualApplyMode.Items); };

            _autoToggle = new CheckBox
            {
                Text = T(LeagueAutoApplyUiTextKeys.Toggle),
                Location = new Point(30, 201),
                Size = new Size(292, 28),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Checked = _autoController.Enabled
            };
            _autoStatusValue = new Label
            {
                Location = new Point(326, 201),
                Size = new Size(438, 38),
                ForeColor = TextMuted,
                AutoEllipsis = true
            };
            UpdateAutoStatus(_autoController.LastStatus);
            _autoToggle.CheckedChanged += HandleAutoToggleChanged;
            _autoController.StatusChanged += HandleAutoStatusChanged;

            var contextCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Context), 238);
            _contextValue = CreateValueLabel(266, 25);

            var spellCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Spells), 294);
            _spellValue = CreateValueBox(321, 46);

            var runeCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Runes), 370);
            _runeValue = CreateValueBox(397, 48);

            var itemCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Items), 448);
            _itemValue = CreateValueBox(475, 54);

            _statusValue = new Label
            {
                Location = new Point(30, 542),
                Size = new Size(500, 58),
                ForeColor = Color.FromArgb(176, 191, 216),
                AutoEllipsis = true
            };

            _refreshButton = CreateButton(T(LeagueBuildApplyUiTextKeys.Refresh), SurfaceRaised, 110);
            _refreshButton.Location = new Point(548, 594);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshAsync(true); };

            _applyButton = CreateButton(T(LeagueBuildApplyUiTextKeys.Apply), Accent, 124);
            _applyButton.Location = new Point(666, 594);
            _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _applyButton.Enabled = false;
            _applyButton.Click += async delegate { await ApplyWithConfirmationAsync(); };

            Controls.Add(title);
            Controls.Add(badge);
            Controls.Add(hint);
            Controls.Add(modeCaption);
            Controls.Add(_fullModeButton);
            Controls.Add(_buildModeButton);
            Controls.Add(_itemsModeButton);
            Controls.Add(_autoToggle);
            Controls.Add(_autoStatusValue);
            Controls.Add(contextCaption);
            Controls.Add(_contextValue);
            Controls.Add(spellCaption);
            Controls.Add(_spellValue);
            Controls.Add(runeCaption);
            Controls.Add(_runeValue);
            Controls.Add(itemCaption);
            Controls.Add(_itemValue);
            Controls.Add(_statusValue);
            Controls.Add(_refreshButton);
            Controls.Add(_applyButton);

            ApplyWaitingState();
            UpdateModeButtons();
            Shown += async delegate { await RefreshAsync(false); };
            FormClosed += delegate
            {
                _autoController.StatusChanged -= HandleAutoStatusChanged;
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };
        }

        internal LeagueManualApplyMode SelectedModeForSmokeTest
        {
            get { return _mode; }
        }

        private Label CreateCaption(string text, int top)
        {
            return new Label
            {
                Text = text,
                Location = new Point(30, top),
                Size = new Size(734, 25),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
        }

        private Label CreateValueLabel(int top, int height)
        {
            return new Label
            {
                Location = new Point(30, top),
                Size = new Size(734, height),
                ForeColor = Color.FromArgb(206, 218, 239),
                AutoEllipsis = true
            };
        }

        private TextBox CreateValueBox(int top, int height)
        {
            return new TextBox
            {
                Location = new Point(30, top),
                Size = new Size(734, height),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Surface,
                ForeColor = TextPrimary,
                ScrollBars = ScrollBars.Vertical
            };
        }

        private Button CreateModeButton(string title, string hint, Point location)
        {
            var button = new Button
            {
                Text = title + Environment.NewLine + hint,
                Location = location,
                Size = new Size(232, 62),
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 2, 8, 2),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(55, 74, 101);
            button.FlatAppearance.MouseOverBackColor = SurfaceRaised;
            return button;
        }

        private Button CreateButton(string text, Color background, int width)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(68, 79, 101);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 83, 123);
            return button;
        }

        private void SelectMode(LeagueManualApplyMode mode)
        {
            if (_busy) return;
            _mode = mode;
            UpdateModeButtons();
            ApplySnapshot(_snapshot);
        }

        private void UpdateModeButtons()
        {
            StyleModeButton(_fullModeButton, _mode == LeagueManualApplyMode.Full, NeonCyan);
            StyleModeButton(_buildModeButton, _mode == LeagueManualApplyMode.Build, Color.FromArgb(91, 142, 255));
            StyleModeButton(_itemsModeButton, _mode == LeagueManualApplyMode.Items, NeonPurple);
            var modeText = _mode == LeagueManualApplyMode.Full
                ? T(LeagueBuildApplyUiTextKeys.ModeFull)
                : _mode == LeagueManualApplyMode.Build
                    ? T(LeagueBuildApplyUiTextKeys.ModeBuild)
                    : T(LeagueBuildApplyUiTextKeys.ModeItems);
            _applyButton.Text = T(LeagueBuildApplyUiTextKeys.Apply) + " · " + modeText;
        }

        private static void StyleModeButton(Button button, bool selected, Color accent)
        {
            button.BackColor = selected ? Color.FromArgb(27, 43, 66) : Surface;
            button.ForeColor = selected ? Color.White : Color.FromArgb(200, 213, 234);
            button.FlatAppearance.BorderSize = selected ? 2 : 1;
            button.FlatAppearance.BorderColor = selected ? accent : Color.FromArgb(55, 74, 101);
        }

        private void HandleAutoToggleChanged(object sender, EventArgs e)
        {
            if (_syncingAutoToggle || IsDisposed) return;
            try
            {
                _autoController.SetEnabled(_autoToggle.Checked);
                UpdateAutoStatus(_autoController.LastStatus);
            }
            catch (Exception exception)
            {
                AppLog.Error("League auto apply toggle failed", exception);
                _syncingAutoToggle = true;
                try { _autoToggle.Checked = _autoController.Enabled; }
                finally { _syncingAutoToggle = false; }
                UpdateAutoStatus("failed");
            }
        }

        private void HandleAutoStatusChanged(object sender, LeagueAutoApplyStatusChangedEventArgs e)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => HandleAutoStatusChanged(sender, e))); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            _syncingAutoToggle = true;
            try { _autoToggle.Checked = _autoController.Enabled; }
            finally { _syncingAutoToggle = false; }
            UpdateAutoStatus(e == null ? _autoController.LastStatus : e.Status);
        }

        private void UpdateAutoStatus(string status)
        {
            string key;
            if (string.Equals(status, "applying", StringComparison.OrdinalIgnoreCase))
                key = LeagueAutoApplyUiTextKeys.Applying;
            else if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                key = LeagueAutoApplyUiTextKeys.Succeeded;
            else if (string.Equals(status, "partial", StringComparison.OrdinalIgnoreCase))
                key = LeagueAutoApplyUiTextKeys.Partial;
            else if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                key = LeagueAutoApplyUiTextKeys.Failed;
            else if (_autoController.Enabled)
                key = LeagueAutoApplyUiTextKeys.Waiting;
            else
                key = LeagueAutoApplyUiTextKeys.Disabled;
            _autoStatusValue.Text = T(key);
        }

        private async Task RefreshAsync(bool force)
        {
            if (_busy || IsDisposed || _lifetime.IsCancellationRequested) return;
            _busy = true;
            SetButtons(false);
            try
            {
                var snapshot = await _readService.RefreshAsync(force, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                _snapshot = snapshot;
                ApplySnapshot(snapshot);
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested) ApplyWaitingState();
            }
            catch (Exception exception)
            {
                AppLog.Error("League Build Apply refresh failed", exception);
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    _statusValue.Text = FormatFailure();
            }
            finally
            {
                _busy = false;
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    SetButtons(CanApply(_snapshot));
            }
        }

        private async Task ApplyWithConfirmationAsync()
        {
            if (_busy || !CanApply(_snapshot) || IsDisposed || _lifetime.IsCancellationRequested) return;
            _busy = true;
            SetButtons(false);
            try
            {
                if (_mode == LeagueManualApplyMode.Full)
                    await ApplyFullAsync();
                else if (_mode == LeagueManualApplyMode.Items)
                    await ApplyItemsAsync();
                else
                    await ApplyBuildAsync();
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                    _statusValue.Text = T(LeagueBuildApplyUiTextKeys.ChampSelectOnly);
            }
            catch (Exception exception)
            {
                AppLog.Error("League Build Apply operation failed", exception);
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    _statusValue.Text = FormatFailure();
            }
            finally
            {
                _busy = false;
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    SetButtons(CanApply(_snapshot));
            }
        }

        private async Task ApplyBuildAsync()
        {
            _statusValue.Text = T(LeagueBuildApplyUiTextKeys.Preparing);
            var plan = await _applyService.PrepareAsync(_snapshot, _lifetime.Token);
            if (plan == null)
            {
                _statusValue.Text = T(LeagueBuildApplyUiTextKeys.NoLoadout);
                return;
            }

            var context = BuildContext(_snapshot);
            var spellPreview = string.IsNullOrWhiteSpace(plan.SpellPreview)
                ? plan.Spell1Id + " / " + plan.Spell2Id
                : plan.SpellPreview;
            var runePreview = string.IsNullOrWhiteSpace(plan.RunePreview)
                ? plan.PrimaryStyleId + " / " + plan.SecondaryStyleId
                : plan.RunePreview;
            var confirmation = string.Format(
                T(LeagueBuildApplyUiTextKeys.ConfirmFormat),
                context,
                spellPreview,
                runePreview);
            if (!Confirm(confirmation)) return;

            var result = await _applyService.ApplyAsync(plan, _lifetime.Token);
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            ApplyBuildResult(result);
        }

        private async Task ApplyItemsAsync()
        {
            _statusValue.Text = T(LeagueItemSetUiTextKeys.Preparing);
            var plan = await _itemSetService.PrepareAsync(_snapshot, _lifetime.Token);
            if (plan == null || !plan.HasItems)
            {
                _statusValue.Text = T(LeagueItemSetUiTextKeys.NoItems);
                return;
            }

            var confirmation = string.Format(
                T(LeagueBuildApplyUiTextKeys.ItemsConfirmFormat),
                BuildContext(_snapshot),
                BuildItemPreview(_snapshot.Recommendation));
            if (!Confirm(confirmation)) return;

            var result = await _itemSetService.ApplyAsync(plan, _lifetime.Token);
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            if (result != null && result.Succeeded)
                _statusValue.Text = T(LeagueBuildApplyUiTextKeys.ItemsSucceeded);
            else if (result != null && string.Equals(result.Status, "blocked", StringComparison.OrdinalIgnoreCase))
                _statusValue.Text = T(LeagueBuildApplyUiTextKeys.ContextChanged);
            else
                _statusValue.Text = string.Format(T(LeagueBuildApplyUiTextKeys.Failed), T(LeagueItemSetUiTextKeys.WriteFailed));
        }

        private async Task ApplyFullAsync()
        {
            _statusValue.Text = T(LeagueBuildApplyUiTextKeys.Preparing);
            var confirmation = string.Format(
                T(LeagueBuildApplyUiTextKeys.FullConfirmFormat),
                BuildContext(_snapshot),
                FindRecommendation(_snapshot.Recommendation, "summoner-spells"),
                FindRecommendation(_snapshot.Recommendation, "runes"),
                BuildItemPreview(_snapshot.Recommendation));
            if (!Confirm(confirmation)) return;

            var result = await _fullApplyExecutor.ExecuteAsync(_snapshot, _lifetime.Token);
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            if (result == null)
            {
                _statusValue.Text = FormatFailure();
                return;
            }
            if (string.Equals(result.BuildStatus, "blocked", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ItemSetStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                _statusValue.Text = T(LeagueBuildApplyUiTextKeys.ContextChanged);
                return;
            }

            var buildSucceeded = string.Equals(result.BuildStatus, "success", StringComparison.OrdinalIgnoreCase);
            var itemSetSucceeded = string.Equals(result.ItemSetStatus, "success", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase) && buildSucceeded && itemSetSucceeded)
            {
                _statusValue.Text = T(LeagueBuildApplyUiTextKeys.FullSucceeded);
                return;
            }
            if (!buildSucceeded && !itemSetSucceeded)
            {
                _statusValue.Text = FormatFailure();
                return;
            }

            _statusValue.Text = string.Format(
                T(LeagueBuildApplyUiTextKeys.FullPartialFormat),
                FriendlyStatus(result.BuildStatus),
                FriendlyStatus(result.ItemSetStatus));
        }

        private bool Confirm(string confirmation)
        {
            var choice = MessageBox.Show(
                this,
                confirmation,
                T(LeagueBuildApplyUiTextKeys.ConfirmTitle),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (choice == DialogResult.Yes) return true;
            _statusValue.Text = T(LeagueBuildApplyUiTextKeys.Ready);
            return false;
        }

        private void ApplySnapshot(LeagueBuildAdvisorSnapshot snapshot)
        {
            if (snapshot == null)
            {
                ApplyWaitingState();
                return;
            }

            _contextValue.Text = BuildContext(snapshot);
            _spellValue.Text = FindRecommendation(snapshot.Recommendation, "summoner-spells");
            _runeValue.Text = FindRecommendation(snapshot.Recommendation, "runes");
            _itemValue.Text = BuildItemPreview(snapshot.Recommendation);
            _statusValue.Text = CanApply(snapshot)
                ? T(LeagueBuildApplyUiTextKeys.Ready)
                : T(LeagueBuildApplyUiTextKeys.Waiting);
            _applyButton.Enabled = !_busy && CanApply(snapshot);
        }

        private void ApplyBuildResult(LeagueBuildApplyResult result)
        {
            if (result == null)
            {
                _statusValue.Text = FormatFailure();
                return;
            }
            if (string.Equals(result.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                _statusValue.Text = T(LeagueBuildApplyUiTextKeys.ContextChanged);
                return;
            }
            if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                _statusValue.Text = T(LeagueBuildApplyUiTextKeys.Succeeded);
                return;
            }
            if (result.RuneSkippedNoCapacity)
            {
                _statusValue.Text = string.Format(
                    T(result.AnyApplied
                        ? LeagueBuildApplyUiTextKeys.Partial
                        : LeagueBuildApplyUiTextKeys.Failed),
                    T(LeagueBuildApplyUiTextKeys.RuneSlotFull));
                return;
            }

            var details = string.Format(
                T(LeagueBuildApplyUiTextKeys.DetailsFormat),
                StatusText(result.RunesApplied),
                StatusText(result.SpellsApplied));
            _statusValue.Text = string.Format(
                T(string.Equals(result.Status, "partial", StringComparison.OrdinalIgnoreCase)
                    ? LeagueBuildApplyUiTextKeys.Partial
                    : LeagueBuildApplyUiTextKeys.Failed),
                details);
        }

        private string FriendlyStatus(string status)
        {
            if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                return T(LeagueBuildApplyUiTextKeys.Applied);
            if (string.Equals(status, "not-available", StringComparison.OrdinalIgnoreCase))
                return T(LeagueBuildApplyUiTextKeys.NoLoadout);
            return T(LeagueBuildApplyUiTextKeys.WriteFailed);
        }

        private string FormatFailure()
        {
            return string.Format(
                T(LeagueBuildApplyUiTextKeys.Failed),
                T(LeagueBuildApplyUiTextKeys.WriteFailed));
        }

        private string StatusText(bool applied)
        {
            return T(applied ? LeagueBuildApplyUiTextKeys.Applied : LeagueBuildApplyUiTextKeys.WriteFailed);
        }

        private void ApplyWaitingState()
        {
            _snapshot = null;
            _contextValue.Text = string.Empty;
            _spellValue.Text = string.Empty;
            _runeValue.Text = string.Empty;
            _itemValue.Text = string.Empty;
            _statusValue.Text = T(LeagueBuildApplyUiTextKeys.Waiting);
            _applyButton.Enabled = false;
        }

        private void SetButtons(bool canApply)
        {
            _refreshButton.Enabled = !_busy;
            _applyButton.Enabled = !_busy && canApply;
            _fullModeButton.Enabled = !_busy;
            _buildModeButton.Enabled = !_busy;
            _itemsModeButton.Enabled = !_busy;
        }

        private static bool CanApply(LeagueBuildAdvisorSnapshot snapshot)
        {
            return snapshot != null &&
                   snapshot.Connected &&
                   snapshot.Activity == LeagueActivityLevel.ChampSelect &&
                   snapshot.ChampionId > 0 &&
                   snapshot.Recommendation != null &&
                   string.Equals(snapshot.Status, "ready", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildContext(LeagueBuildAdvisorSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;
            var champion = string.IsNullOrWhiteSpace(snapshot.ChampionName)
                ? "#" + snapshot.ChampionId
                : snapshot.ChampionName + " #" + snapshot.ChampionId;
            return (snapshot.Phase ?? string.Empty) + " · " + champion + " · " +
                   (snapshot.Mode ?? string.Empty) + " / " + (snapshot.Position ?? string.Empty) + " · " +
                   (snapshot.Source ?? string.Empty) + " " + (snapshot.Version ?? string.Empty);
        }

        private static string FindRecommendation(LeagueBuildRecommendation recommendation, string category)
        {
            if (recommendation == null) return string.Empty;
            var row = recommendation.Rows.FirstOrDefault(item =>
                string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
            return row == null ? string.Empty : row.Recommendation ?? string.Empty;
        }

        private static string BuildItemPreview(LeagueBuildRecommendation recommendation)
        {
            if (recommendation == null) return string.Empty;
            var categories = new[] { "starter-items", "boots", "core-items" };
            var rows = recommendation.Rows
                .Where(row => row != null && categories.Contains(row.Category))
                .Select(row => row.Recommendation)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            return rows.Length == 0 ? string.Empty : string.Join(" / ", rows);
        }

        private string T(string key)
        {
            return LeagueAdvisorText.Get(_ui, key);
        }
    }
}
