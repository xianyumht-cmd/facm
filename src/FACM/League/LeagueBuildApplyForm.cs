using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueBuildApplyForm : Form
    {
        private static readonly Color Surface = Color.FromArgb(14, 19, 30);
        private static readonly Color Card = Color.FromArgb(20, 29, 45);
        private static readonly Color CardSelected = Color.FromArgb(27, 47, 72);
        private static readonly Color AccentCyan = Color.FromArgb(48, 214, 255);
        private static readonly Color AccentViolet = Color.FromArgb(132, 94, 247);

        private readonly LeagueBuildAdvisorDataService _readService;
        private readonly LeagueBuildApplyService _applyService;
        private readonly LeagueAutoApplyController _autoController;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly CheckBox _autoToggle;
        private readonly Label _autoStatusValue;
        private readonly Label _contextValue;
        private readonly RadioButton[] _optionButtons = new RadioButton[LeagueBuildApplyService.MaxVisibleOptions];
        private readonly TextBox _spellValue;
        private readonly TextBox _runeValue;
        private readonly TextBox _itemValue;
        private readonly Label _statusValue;
        private readonly Button _refreshButton;
        private readonly Button _applyButton;
        private IReadOnlyList<LeagueBuildApplyPlan> _options = Array.Empty<LeagueBuildApplyPlan>();
        private LeagueBuildAdvisorSnapshot _snapshot;
        private bool _busy;
        private bool _syncingAutoToggle;
        private bool _bindingOptions;

        public LeagueBuildApplyForm(
            LeagueBuildAdvisorDataService readService,
            LeagueBuildApplyService applyService,
            LeagueAutoApplyController autoController,
            UiTextCatalog ui)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
            _autoController = autoController ?? throw new ArgumentNullException(nameof(autoController));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = T(LeagueBuildApplyUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(858, 700);
            MinimumSize = new Size(800, 660);
            BackColor = Surface;
            ForeColor = Color.FromArgb(238, 243, 252);
            Font = new Font("Microsoft YaHei UI", 9F);

            var accent = new Panel
            {
                Dock = DockStyle.Top,
                Height = 3,
                BackColor = AccentCyan
            };

            var title = new Label
            {
                Text = T(LeagueBuildApplyUiTextKeys.Title),
                Location = new Point(28, 18),
                Size = new Size(620, 36),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = T(LeagueBuildApplyUiTextKeys.Hint),
                Location = new Point(30, 58),
                Size = new Size(798, 32),
                ForeColor = Color.FromArgb(146, 161, 188)
            };

            _autoToggle = new CheckBox
            {
                Text = T(LeagueAutoApplyUiTextKeys.Toggle),
                Location = new Point(30, 93),
                Size = new Size(288, 26),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Checked = _autoController.Enabled
            };
            _autoStatusValue = new Label
            {
                Location = new Point(320, 92),
                Size = new Size(508, 22),
                ForeColor = Color.FromArgb(146, 161, 188),
                AutoEllipsis = true
            };
            var autoHint = new Label
            {
                Text = T(LeagueBuildApplyUiTextKeys.AutoUsesMain),
                Location = new Point(30, 118),
                Size = new Size(798, 20),
                ForeColor = Color.FromArgb(112, 139, 180)
            };
            UpdateAutoStatus(_autoController.LastStatus);
            _autoToggle.CheckedChanged += HandleAutoToggleChanged;
            _autoController.StatusChanged += HandleAutoStatusChanged;

            var contextCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Context), 147);
            _contextValue = CreateValueLabel(174, 25);

            var optionCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Options), 205);
            for (var index = 0; index < _optionButtons.Length; index++)
            {
                var optionIndex = index;
                var option = new RadioButton
                {
                    Appearance = Appearance.Button,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(30 + index * 267, 235),
                    Size = new Size(251, 78),
                    BackColor = Card,
                    ForeColor = Color.FromArgb(214, 226, 246),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(12, 5, 8, 5),
                    Cursor = Cursors.Hand,
                    AutoCheck = true,
                    TabStop = index == 0
                };
                option.FlatAppearance.BorderColor = Color.FromArgb(44, 61, 86);
                option.FlatAppearance.BorderSize = 1;
                option.FlatAppearance.CheckedBackColor = CardSelected;
                option.FlatAppearance.MouseOverBackColor = Color.FromArgb(27, 40, 60);
                option.CheckedChanged += delegate
                {
                    if (!_bindingOptions && option.Checked) HandleOptionSelected(optionIndex);
                };
                _optionButtons[index] = option;
            }

            var selectedCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.SelectedDetail), 324);
            var spellCaption = CreateSmallCaption(T(LeagueBuildApplyUiTextKeys.Spells), 355, 30);
            _spellValue = CreateValueBox(381, 48, 30, 386);
            var runeCaption = CreateSmallCaption(T(LeagueBuildApplyUiTextKeys.Runes), 355, 432);
            _runeValue = CreateValueBox(381, 48, 432, 396);

            var itemCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Items), 442);
            var itemHint = new Label
            {
                Text = T(LeagueBuildApplyUiTextKeys.ItemsHint),
                Location = new Point(30, 468),
                Size = new Size(798, 23),
                ForeColor = Color.FromArgb(112, 139, 180),
                AutoEllipsis = true
            };
            _itemValue = CreateValueBox(494, 78, 30, 798);

            _statusValue = new Label
            {
                Location = new Point(30, 588),
                Size = new Size(520, 50),
                ForeColor = Color.FromArgb(176, 191, 216),
                AutoEllipsis = true
            };

            _refreshButton = CreateButton(T(LeagueBuildApplyUiTextKeys.Refresh), Color.FromArgb(35, 43, 60), 122);
            _refreshButton.Location = new Point(580, 638);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshAsync(true); };

            _applyButton = CreateButton(T(LeagueBuildApplyUiTextKeys.Apply), Color.FromArgb(42, 107, 208), 130);
            _applyButton.Location = new Point(708, 638);
            _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _applyButton.Enabled = false;
            _applyButton.FlatAppearance.BorderColor = AccentCyan;
            _applyButton.Click += async delegate { await ApplyWithConfirmationAsync(); };

            Controls.Add(accent);
            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_autoToggle);
            Controls.Add(_autoStatusValue);
            Controls.Add(autoHint);
            Controls.Add(contextCaption);
            Controls.Add(_contextValue);
            Controls.Add(optionCaption);
            foreach (var option in _optionButtons) Controls.Add(option);
            Controls.Add(selectedCaption);
            Controls.Add(spellCaption);
            Controls.Add(_spellValue);
            Controls.Add(runeCaption);
            Controls.Add(_runeValue);
            Controls.Add(itemCaption);
            Controls.Add(itemHint);
            Controls.Add(_itemValue);
            Controls.Add(_statusValue);
            Controls.Add(_refreshButton);
            Controls.Add(_applyButton);

            ApplyWaitingState();
            Shown += async delegate { await RefreshAsync(false); };
            FormClosed += delegate
            {
                _autoController.StatusChanged -= HandleAutoStatusChanged;
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };
        }

        private Label CreateCaption(string text, int top)
        {
            return new Label
            {
                Text = text,
                Location = new Point(30, top),
                Size = new Size(798, 25),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
        }

        private Label CreateSmallCaption(string text, int top, int left)
        {
            return new Label
            {
                Text = text,
                Location = new Point(left, top),
                Size = new Size(386, 23),
                ForeColor = Color.FromArgb(192, 211, 239),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
        }

        private Label CreateValueLabel(int top, int height)
        {
            return new Label
            {
                Location = new Point(30, top),
                Size = new Size(798, height),
                ForeColor = Color.FromArgb(206, 218, 239),
                AutoEllipsis = true
            };
        }

        private TextBox CreateValueBox(int top, int height, int left, int width)
        {
            return new TextBox
            {
                Location = new Point(left, top),
                Size = new Size(width, height),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(18, 26, 40),
                ForeColor = Color.FromArgb(238, 243, 252),
                ScrollBars = ScrollBars.Vertical
            };
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
            return button;
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

                IReadOnlyList<LeagueBuildApplyPlan> options = Array.Empty<LeagueBuildApplyPlan>();
                if (CanApply(snapshot))
                    options = await _applyService.PrepareOptionsAsync(snapshot, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                BindOptions(options);
                _statusValue.Text = CanApply(snapshot) && SelectedPlan != null
                    ? T(LeagueBuildApplyUiTextKeys.Ready)
                    : T(LeagueBuildApplyUiTextKeys.Waiting);
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
                    SetButtons(CanApply(_snapshot) && SelectedPlan != null);
            }
        }

        private async Task ApplyWithConfirmationAsync()
        {
            var selected = SelectedPlan;
            if (_busy || !CanApply(_snapshot) || selected == null || IsDisposed || _lifetime.IsCancellationRequested) return;
            var selectedRank = selected.OptionRank <= 0 ? 1 : selected.OptionRank;
            _busy = true;
            SetButtons(false);
            try
            {
                _statusValue.Text = T(LeagueBuildApplyUiTextKeys.Preparing);
                var freshOptions = await _applyService.PrepareOptionsAsync(_snapshot, _lifetime.Token);
                var plan = freshOptions.FirstOrDefault(item => item.OptionRank == selectedRank);
                if (plan == null)
                {
                    _statusValue.Text = T(LeagueBuildApplyUiTextKeys.NoLoadout);
                    return;
                }

                var context = BuildContext(_snapshot);
                var spellPreview = BuildSpellPreview(plan);
                var runePreview = BuildRunePreview(plan);
                var legacyConfirmation = string.Format(
                    T(LeagueBuildApplyUiTextKeys.ConfirmFormat),
                    context,
                    spellPreview,
                    runePreview);
                var confirmation = string.Format(
                    T(LeagueBuildApplyUiTextKeys.ConfirmRankFormat),
                    OptionName(plan.OptionRank),
                    plan.OptionRank,
                    legacyConfirmation);
                var choice = MessageBox.Show(
                    this,
                    confirmation,
                    T(LeagueBuildApplyUiTextKeys.ConfirmTitle),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (choice != DialogResult.Yes)
                {
                    _statusValue.Text = T(LeagueBuildApplyUiTextKeys.Ready);
                    return;
                }

                var result = await _applyService.ApplyAsync(plan, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                ApplyResult(result);
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
                    SetButtons(CanApply(_snapshot) && SelectedPlan != null);
            }
        }

        private void ApplySnapshot(LeagueBuildAdvisorSnapshot snapshot)
        {
            if (snapshot == null)
            {
                ApplyWaitingState();
                return;
            }

            _contextValue.Text = BuildContext(snapshot);
            _itemValue.Text = BuildItemPreview(snapshot.Recommendation);
            _statusValue.Text = CanApply(snapshot)
                ? T(LeagueBuildApplyUiTextKeys.Preparing)
                : T(LeagueBuildApplyUiTextKeys.Waiting);
        }

        private void BindOptions(IReadOnlyList<LeagueBuildApplyPlan> options)
        {
            _options = options ?? Array.Empty<LeagueBuildApplyPlan>();
            _bindingOptions = true;
            try
            {
                for (var index = 0; index < _optionButtons.Length; index++)
                {
                    var rank = index + 1;
                    var plan = _options.FirstOrDefault(item => item.OptionRank == rank);
                    var button = _optionButtons[index];
                    button.Enabled = plan != null;
                    button.Checked = plan != null && index == 0;
                    button.BackColor = plan == null ? Color.FromArgb(17, 23, 35) : Card;
                    button.ForeColor = plan == null ? Color.FromArgb(90, 103, 125) : Color.FromArgb(214, 226, 246);
                    button.Text = BuildOptionCardText(rank, plan);
                }
            }
            finally
            {
                _bindingOptions = false;
            }

            if (_options.Count > 0 && !_optionButtons.Any(button => button.Checked))
            {
                var first = _options.OrderBy(item => item.OptionRank).First();
                var index = Math.Max(0, Math.Min(_optionButtons.Length - 1, first.OptionRank - 1));
                _optionButtons[index].Checked = true;
            }
            UpdateSelectedPreview();
        }

        private void HandleOptionSelected(int optionIndex)
        {
            if (optionIndex < 0 || optionIndex >= _optionButtons.Length) return;
            for (var index = 0; index < _optionButtons.Length; index++)
            {
                var selected = index == optionIndex && _optionButtons[index].Checked;
                _optionButtons[index].BackColor = selected ? CardSelected : (_optionButtons[index].Enabled ? Card : Color.FromArgb(17, 23, 35));
                _optionButtons[index].FlatAppearance.BorderColor = selected ? AccentCyan : Color.FromArgb(44, 61, 86);
            }
            UpdateSelectedPreview();
            SetButtons(CanApply(_snapshot) && SelectedPlan != null);
        }

        private void UpdateSelectedPreview()
        {
            var plan = SelectedPlan;
            if (plan == null)
            {
                _spellValue.Text = string.Empty;
                _runeValue.Text = string.Empty;
                return;
            }
            _spellValue.Text = BuildSpellPreview(plan);
            _runeValue.Text = BuildRunePreview(plan);
        }

        private LeagueBuildApplyPlan SelectedPlan
        {
            get
            {
                for (var index = 0; index < _optionButtons.Length; index++)
                {
                    if (!_optionButtons[index].Checked) continue;
                    var rank = index + 1;
                    return _options.FirstOrDefault(item => item.OptionRank == rank);
                }
                return null;
            }
        }

        private string BuildOptionCardText(int rank, LeagueBuildApplyPlan plan)
        {
            var title = OptionName(rank);
            var rankText = string.Format(T(LeagueBuildApplyUiTextKeys.OptionRankFormat), rank);
            if (plan == null)
                return title + "\r\n" + rankText + "\r\n" + T(LeagueBuildApplyUiTextKeys.OptionUnavailable);
            var stats = string.Format(
                T(LeagueBuildApplyUiTextKeys.OptionStatsFormat),
                FormatRate(plan.RunePickRate),
                FormatPlay(plan.RunePlay),
                FormatRate(plan.SpellPickRate),
                FormatPlay(plan.SpellPlay));
            return title + "\r\n" + rankText + "\r\n" + stats;
        }

        private string OptionName(int rank)
        {
            if (rank == 1) return T(LeagueBuildApplyUiTextKeys.OptionMain);
            if (rank == 2) return T(LeagueBuildApplyUiTextKeys.OptionAlternative);
            return T(LeagueBuildApplyUiTextKeys.OptionThird);
        }

        private static string FormatRate(double? value)
        {
            if (!value.HasValue) return "--";
            var rate = value.Value;
            if (Math.Abs(rate) <= 1.0) rate *= 100.0;
            return rate.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatPlay(int value)
        {
            return value > 0 ? value.ToString(CultureInfo.InvariantCulture) : "--";
        }

        private static string BuildSpellPreview(LeagueBuildApplyPlan plan)
        {
            if (plan == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(plan.SpellPreview)) return plan.SpellPreview;
            if (!plan.HasSpells) return string.Empty;
            return "#" + plan.Spell1Id.ToString(CultureInfo.InvariantCulture) + " · #" +
                   plan.Spell2Id.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildRunePreview(LeagueBuildApplyPlan plan)
        {
            if (plan == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(plan.RunePreview)) return plan.RunePreview;
            if (!plan.HasRunes) return string.Empty;
            var ids = plan.PrimaryRuneIds.Concat(plan.SecondaryRuneIds).Concat(plan.StatModIds)
                .Select(id => "#" + id.ToString(CultureInfo.InvariantCulture));
            return string.Join(" · ", ids);
        }

        private static string BuildItemPreview(LeagueBuildRecommendation recommendation)
        {
            if (recommendation == null) return string.Empty;
            var order = new[] { "starter-items", "boots", "core-items" };
            var captions = new[] { "出门", "鞋子", "核心" };
            var lines = new List<string>();
            for (var index = 0; index < order.Length; index++)
            {
                var row = recommendation.Rows.FirstOrDefault(item =>
                    string.Equals(item.Category, order[index], StringComparison.OrdinalIgnoreCase));
                if (row == null || string.IsNullOrWhiteSpace(row.Recommendation)) continue;
                var evidence = string.IsNullOrWhiteSpace(row.Evidence) ? string.Empty : "  ·  " + row.Evidence;
                lines.Add(captions[index] + "：" + row.Recommendation + evidence);
            }
            return string.Join(Environment.NewLine, lines);
        }

        private void ApplyResult(LeagueBuildApplyResult result)
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
            _options = Array.Empty<LeagueBuildApplyPlan>();
            _contextValue.Text = string.Empty;
            _spellValue.Text = string.Empty;
            _runeValue.Text = string.Empty;
            _itemValue.Text = string.Empty;
            _bindingOptions = true;
            try
            {
                for (var index = 0; index < _optionButtons.Length; index++)
                {
                    _optionButtons[index].Checked = false;
                    _optionButtons[index].Enabled = false;
                    _optionButtons[index].BackColor = Color.FromArgb(17, 23, 35);
                    _optionButtons[index].ForeColor = Color.FromArgb(90, 103, 125);
                    _optionButtons[index].Text = BuildOptionCardText(index + 1, null);
                }
            }
            finally
            {
                _bindingOptions = false;
            }
            _statusValue.Text = T(LeagueBuildApplyUiTextKeys.Waiting);
            _applyButton.Enabled = false;
        }

        private void SetButtons(bool canApply)
        {
            _refreshButton.Enabled = !_busy;
            _applyButton.Enabled = !_busy && canApply;
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

        private string T(string key)
        {
            return LeagueAdvisorText.Get(_ui, key);
        }
    }
}
