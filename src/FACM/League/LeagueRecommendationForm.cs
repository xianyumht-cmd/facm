using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueRecommendationForm : Form
    {
        private readonly LeagueBuildAdvisorDataService _readService;
        private readonly LeagueBuildApplyService _applyService;
        private readonly LeagueItemSetService _itemSetService;
        private readonly LeagueAutoApplyController _autoController;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private readonly CheckBox _runesChoice;
        private readonly CheckBox _spellsChoice;
        private readonly CheckBox _itemsChoice;
        private readonly CheckBox _autoToggle;
        private readonly Label _autoStatus;
        private readonly Label _contextValue;
        private readonly TextBox _runePreview;
        private readonly TextBox _spellPreview;
        private readonly TextBox _itemPreview;
        private readonly Label _skillsValue;
        private readonly Label _countersValue;
        private readonly Label _statusValue;
        private readonly Button _refreshButton;
        private readonly Button _applyButton;

        private LeagueBuildAdvisorSnapshot _snapshot;
        private bool _busy;
        private bool _syncingAutoToggle;

        public LeagueRecommendationForm(
            LeagueBuildAdvisorDataService readService,
            LeagueBuildApplyService applyService,
            LeagueItemSetService itemSetService,
            LeagueAutoApplyController autoController,
            UiTextCatalog ui)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
            _itemSetService = itemSetService ?? throw new ArgumentNullException(nameof(itemSetService));
            _autoController = autoController ?? throw new ArgumentNullException(nameof(autoController));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = T(LeagueRecommendationUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 700);
            MinimumSize = new Size(860, 650);
            BackColor = Color.FromArgb(10, 15, 25);
            ForeColor = Color.FromArgb(235, 242, 255);
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;

            var header = new RecommendationHeaderPanel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.FromArgb(13, 20, 34)
            };
            header.Controls.Add(new Label
            {
                Text = T(LeagueRecommendationUiTextKeys.Title),
                Location = new Point(28, 18),
                Size = new Size(520, 32),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold)
            });
            header.Controls.Add(new Label
            {
                Text = T(LeagueRecommendationUiTextKeys.Hint),
                Location = new Point(30, 54),
                Size = new Size(820, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(143, 164, 200),
                BackColor = Color.Transparent,
                AutoEllipsis = true
            });

            var chooseCaption = CreateCaption(T(LeagueRecommendationUiTextKeys.Choose), 112);
            _runesChoice = CreateChoice(
                T(LeagueRecommendationUiTextKeys.Runes),
                T(LeagueRecommendationUiTextKeys.RunesHint),
                new Point(28, 142));
            _spellsChoice = CreateChoice(
                T(LeagueRecommendationUiTextKeys.Spells),
                T(LeagueRecommendationUiTextKeys.SpellsHint),
                new Point(306, 142));
            _itemsChoice = CreateChoice(
                T(LeagueRecommendationUiTextKeys.Items),
                T(LeagueRecommendationUiTextKeys.ItemsHint),
                new Point(584, 142));

            _runesChoice.Checked = true;
            _spellsChoice.Checked = true;
            _itemsChoice.Checked = true;
            _runesChoice.CheckedChanged += ChoiceChanged;
            _spellsChoice.CheckedChanged += ChoiceChanged;
            _itemsChoice.CheckedChanged += ChoiceChanged;

            _autoToggle = new CheckBox
            {
                Text = LeagueAdvisorText.Get(_ui, LeagueAutoApplyUiTextKeys.Toggle),
                Location = new Point(30, 224),
                Size = new Size(300, 28),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Checked = _autoController.Enabled
            };
            _autoStatus = new Label
            {
                Location = new Point(334, 224),
                Size = new Size(518, 24),
                ForeColor = Color.FromArgb(129, 224, 255),
                AutoEllipsis = true
            };
            var autoHint = new Label
            {
                Text = T(LeagueRecommendationUiTextKeys.AutoHint),
                Location = new Point(30, 252),
                Size = new Size(822, 22),
                ForeColor = Color.FromArgb(112, 129, 160),
                AutoEllipsis = true
            };
            _autoToggle.CheckedChanged += HandleAutoToggleChanged;
            _autoController.StatusChanged += HandleAutoStatusChanged;
            UpdateAutoStatus(_autoController.LastStatus);

            var contextCaption = CreateCaption(T(LeagueRecommendationUiTextKeys.Context), 286);
            _contextValue = new Label
            {
                Location = new Point(30, 313),
                Size = new Size(822, 26),
                ForeColor = Color.FromArgb(205, 220, 245),
                AutoEllipsis = true
            };

            // Keep captions outside native TextBox windows. Overlaying Labels on a multiline TextBox
            // can disappear at some DPI/scaling combinations because the native edit control owns its
            // own HWND and paint order.
            var runeLabel = CreatePreviewTitle(T(LeagueRecommendationUiTextKeys.Runes), 28, 348);
            var spellLabel = CreatePreviewTitle(T(LeagueRecommendationUiTextKeys.Spells), 306, 348);
            var itemLabel = CreatePreviewTitle(T(LeagueRecommendationUiTextKeys.Items), 584, 348);
            _runePreview = CreatePreviewBox(new Rectangle(28, 372, 268, 78));
            _spellPreview = CreatePreviewBox(new Rectangle(306, 372, 268, 78));
            _itemPreview = CreatePreviewBox(new Rectangle(584, 372, 268, 78));

            var extraCaption = CreateCaption(T(LeagueRecommendationUiTextKeys.Extra), 466);
            var skillsCaption = new Label
            {
                Text = T(LeagueRecommendationUiTextKeys.Skills),
                Location = new Point(30, 495),
                Size = new Size(100, 22),
                ForeColor = Color.FromArgb(142, 164, 200)
            };
            _skillsValue = new Label
            {
                Location = new Point(132, 495),
                Size = new Size(720, 22),
                ForeColor = Color.FromArgb(224, 232, 247),
                AutoEllipsis = true
            };
            var countersCaption = new Label
            {
                Text = T(LeagueRecommendationUiTextKeys.Counters),
                Location = new Point(30, 523),
                Size = new Size(100, 22),
                ForeColor = Color.FromArgb(142, 164, 200)
            };
            _countersValue = new Label
            {
                Location = new Point(132, 523),
                Size = new Size(720, 22),
                ForeColor = Color.FromArgb(224, 232, 247),
                AutoEllipsis = true
            };

            _statusValue = new Label
            {
                Location = new Point(30, 570),
                Size = new Size(560, 54),
                ForeColor = Color.FromArgb(154, 185, 231),
                AutoEllipsis = true
            };

            _refreshButton = CreateButton(T(LeagueRecommendationUiTextKeys.Refresh), false);
            _refreshButton.Location = new Point(630, 590);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshAsync(true); };

            _applyButton = CreateButton(T(LeagueRecommendationUiTextKeys.ApplySelected), true);
            _applyButton.Location = new Point(742, 590);
            _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _applyButton.Enabled = false;
            _applyButton.Click += async delegate { await ApplySelectedAsync(); };

            Controls.Add(_applyButton);
            Controls.Add(_refreshButton);
            Controls.Add(_statusValue);
            Controls.Add(_countersValue);
            Controls.Add(countersCaption);
            Controls.Add(_skillsValue);
            Controls.Add(skillsCaption);
            Controls.Add(extraCaption);
            Controls.Add(_itemPreview);
            Controls.Add(_spellPreview);
            Controls.Add(_runePreview);
            Controls.Add(itemLabel);
            Controls.Add(spellLabel);
            Controls.Add(runeLabel);
            Controls.Add(_contextValue);
            Controls.Add(contextCaption);
            Controls.Add(autoHint);
            Controls.Add(_autoStatus);
            Controls.Add(_autoToggle);
            Controls.Add(_itemsChoice);
            Controls.Add(_spellsChoice);
            Controls.Add(_runesChoice);
            Controls.Add(chooseCaption);
            Controls.Add(header);

            ApplyWaitingState();
            UpdateChoiceStyles();
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
                Size = new Size(822, 25),
                ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
            };
        }

        private CheckBox CreateChoice(string title, string hint, Point location)
        {
            return new CheckBox
            {
                Appearance = Appearance.Button,
                Text = title + "\r\n" + hint,
                Location = location,
                Size = new Size(268, 66),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1 },
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 2, 10, 2),
                ForeColor = Color.FromArgb(218, 229, 247),
                BackColor = Color.FromArgb(18, 27, 43),
                Cursor = Cursors.Hand
            };
        }

        private TextBox CreatePreviewBox(Rectangle bounds)
        {
            return new TextBox
            {
                Location = bounds.Location,
                Size = bounds.Size,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(16, 24, 39),
                ForeColor = Color.FromArgb(222, 232, 249),
                ScrollBars = ScrollBars.Vertical
            };
        }

        private Label CreatePreviewTitle(string text, int left, int top)
        {
            return new Label
            {
                Text = text,
                Location = new Point(left, top),
                Size = new Size(268, 20),
                ForeColor = Color.FromArgb(112, 224, 255),
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold)
            };
        }

        private Button CreateButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(106, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(58, 91, 218) : Color.FromArgb(28, 39, 58),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(94, 219, 255) : Color.FromArgb(63, 78, 105);
            return button;
        }

        private void ChoiceChanged(object sender, EventArgs e)
        {
            UpdateChoiceStyles();
            if (!_busy) SetButtons(CanApply(_snapshot));
        }

        private void UpdateChoiceStyles()
        {
            StyleChoice(_runesChoice);
            StyleChoice(_spellsChoice);
            StyleChoice(_itemsChoice);
        }

        private static void StyleChoice(CheckBox choice)
        {
            if (choice == null) return;
            choice.BackColor = choice.Checked ? Color.FromArgb(27, 49, 84) : Color.FromArgb(18, 27, 43);
            choice.ForeColor = choice.Checked ? Color.White : Color.FromArgb(174, 190, 217);
            choice.FlatAppearance.BorderColor = choice.Checked ? Color.FromArgb(83, 221, 255) : Color.FromArgb(48, 64, 88);
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
                AppLog.Error("League recommendation auto-apply toggle failed", exception);
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
            _autoStatus.Text = LeagueAdvisorText.Get(_ui, key);
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
                AppLog.Error("League recommendation refresh failed", exception);
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    _statusValue.Text = T(LeagueRecommendationUiTextKeys.Failed);
            }
            finally
            {
                _busy = false;
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    SetButtons(CanApply(_snapshot));
            }
        }

        private async Task ApplySelectedAsync()
        {
            if (_busy || !CanApply(_snapshot) || IsDisposed || _lifetime.IsCancellationRequested) return;
            if (!_runesChoice.Checked && !_spellsChoice.Checked && !_itemsChoice.Checked)
            {
                _statusValue.Text = T(LeagueRecommendationUiTextKeys.NoneSelected);
                return;
            }

            _busy = true;
            SetButtons(false);
            try
            {
                _statusValue.Text = T(LeagueRecommendationUiTextKeys.Preparing);

                LeagueBuildApplyPlan loadoutPlan = null;
                LeagueItemSetPlan itemPlan = null;
                if (_runesChoice.Checked || _spellsChoice.Checked)
                    loadoutPlan = await _applyService.PrepareAsync(_snapshot, _lifetime.Token);
                if (_itemsChoice.Checked)
                    itemPlan = await _itemSetService.PrepareAsync(_snapshot, _lifetime.Token);

                var scopedPlan = CreateScopedPlan(loadoutPlan, _runesChoice.Checked, _spellsChoice.Checked);
                var canRunLoadout = scopedPlan != null && (scopedPlan.HasRunes || scopedPlan.HasSpells);
                var canRunItems = itemPlan != null && itemPlan.HasItems;
                if (!canRunLoadout && !canRunItems)
                {
                    _statusValue.Text = T(LeagueRecommendationUiTextKeys.NoAvailable);
                    return;
                }

                if (MessageBox.Show(
                        this,
                        BuildConfirmation(loadoutPlan, itemPlan),
                        T(LeagueRecommendationUiTextKeys.ConfirmTitle),
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    _statusValue.Text = T(LeagueRecommendationUiTextKeys.Ready);
                    return;
                }

                LeagueBuildApplyResult loadoutResult = null;
                LeagueItemSetWriteResult itemResult = null;
                if (canRunLoadout)
                    loadoutResult = await _applyService.ApplyAsync(scopedPlan, _lifetime.Token);
                if (canRunItems)
                    itemResult = await _itemSetService.ApplyAsync(itemPlan, _lifetime.Token);

                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                ApplyCombinedResult(loadoutPlan, loadoutResult, itemPlan, itemResult);
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                    _statusValue.Text = T(LeagueRecommendationUiTextKeys.ContextChanged);
            }
            catch (Exception exception)
            {
                AppLog.Error("League unified recommendation apply failed", exception);
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    _statusValue.Text = T(LeagueRecommendationUiTextKeys.Failed);
            }
            finally
            {
                _busy = false;
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    SetButtons(CanApply(_snapshot));
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
            _runePreview.Text = DisplayRecommendation(snapshot.Recommendation, "runes");
            _spellPreview.Text = DisplayRecommendation(snapshot.Recommendation, "summoner-spells");
            _itemPreview.Text = BuildItemPreview(snapshot.Recommendation);
            _skillsValue.Text = DisplayRecommendation(snapshot.Recommendation, "skills");
            _countersValue.Text = DisplayRecommendation(snapshot.Recommendation, "counters");
            _statusValue.Text = CanApply(snapshot)
                ? T(LeagueRecommendationUiTextKeys.Ready)
                : T(LeagueRecommendationUiTextKeys.Waiting);
        }

        private void ApplyWaitingState()
        {
            _snapshot = null;
            _contextValue.Text = string.Empty;
            _runePreview.Text = string.Empty;
            _spellPreview.Text = string.Empty;
            _itemPreview.Text = string.Empty;
            _skillsValue.Text = string.Empty;
            _countersValue.Text = string.Empty;
            _statusValue.Text = T(LeagueRecommendationUiTextKeys.Waiting);
            _applyButton.Enabled = false;
        }

        private void ApplyCombinedResult(
            LeagueBuildApplyPlan loadoutPlan,
            LeagueBuildApplyResult loadoutResult,
            LeagueItemSetPlan itemPlan,
            LeagueItemSetWriteResult itemResult)
        {
            var selected = 0;
            var succeeded = 0;

            if (_runesChoice.Checked)
            {
                selected++;
                if (loadoutPlan != null && loadoutPlan.HasRunes && loadoutResult != null && loadoutResult.RunesApplied) succeeded++;
            }
            if (_spellsChoice.Checked)
            {
                selected++;
                if (loadoutPlan != null && loadoutPlan.HasSpells && loadoutResult != null && loadoutResult.SpellsApplied) succeeded++;
            }
            if (_itemsChoice.Checked)
            {
                selected++;
                if (itemPlan != null && itemPlan.HasItems && itemResult != null && itemResult.Succeeded) succeeded++;
            }

            var blocked = (loadoutResult != null && string.Equals(loadoutResult.Status, "blocked", StringComparison.OrdinalIgnoreCase)) ||
                          (itemResult != null && string.Equals(itemResult.Status, "blocked", StringComparison.OrdinalIgnoreCase));
            if (succeeded == 0 && blocked)
            {
                _statusValue.Text = T(LeagueRecommendationUiTextKeys.ContextChanged);
                return;
            }

            var text = succeeded == selected
                ? T(LeagueRecommendationUiTextKeys.Success)
                : succeeded > 0
                    ? T(LeagueRecommendationUiTextKeys.Partial)
                    : T(LeagueRecommendationUiTextKeys.Failed);
            if (loadoutResult != null && loadoutResult.RuneSkippedNoCapacity)
                text += "  " + T(LeagueRecommendationUiTextKeys.RuneSlotFull);
            _statusValue.Text = text;
        }

        private string BuildConfirmation(LeagueBuildApplyPlan loadoutPlan, LeagueItemSetPlan itemPlan)
        {
            var builder = new StringBuilder();
            builder.AppendLine(T(LeagueRecommendationUiTextKeys.ConfirmIntro));
            builder.AppendLine();
            AppendConfirmLine(builder, T(LeagueRecommendationUiTextKeys.Runes), _runesChoice.Checked,
                loadoutPlan != null && loadoutPlan.HasRunes ? loadoutPlan.RunePreview : null);
            AppendConfirmLine(builder, T(LeagueRecommendationUiTextKeys.Spells), _spellsChoice.Checked,
                loadoutPlan != null && loadoutPlan.HasSpells ? loadoutPlan.SpellPreview : null);
            AppendConfirmLine(builder, T(LeagueRecommendationUiTextKeys.Items), _itemsChoice.Checked,
                itemPlan != null && itemPlan.HasItems ? itemPlan.Title : null);
            builder.AppendLine();
            builder.Append(BuildContext(_snapshot));
            return builder.ToString();
        }

        private void AppendConfirmLine(StringBuilder builder, string label, bool selected, string preview)
        {
            builder.Append(label).Append(": ");
            if (!selected)
            {
                builder.AppendLine(T(LeagueRecommendationUiTextKeys.NotSelected));
                return;
            }
            if (string.IsNullOrWhiteSpace(preview))
            {
                builder.AppendLine(T(LeagueRecommendationUiTextKeys.Unavailable));
                return;
            }
            builder.Append(T(LeagueRecommendationUiTextKeys.Selected)).Append(" · ").AppendLine(preview);
        }

        private static LeagueBuildApplyPlan CreateScopedPlan(LeagueBuildApplyPlan source, bool includeRunes, bool includeSpells)
        {
            if (source == null) return null;
            var plan = new LeagueBuildApplyPlan
            {
                ChampionId = source.ChampionId,
                ChampionName = source.ChampionName,
                QueueId = source.QueueId,
                Mode = source.Mode,
                Position = source.Position,
                Version = source.Version,
                RunePreview = source.RunePreview,
                SpellPreview = source.SpellPreview
            };

            if (includeSpells)
            {
                plan.Spell1Id = source.Spell1Id;
                plan.Spell2Id = source.Spell2Id;
            }
            if (includeRunes)
            {
                plan.PrimaryStyleId = source.PrimaryStyleId;
                plan.SecondaryStyleId = source.SecondaryStyleId;
                plan.PrimaryRuneIds.AddRange(source.PrimaryRuneIds);
                plan.SecondaryRuneIds.AddRange(source.SecondaryRuneIds);
                plan.StatModIds.AddRange(source.StatModIds);
            }
            return plan;
        }

        private string BuildItemPreview(LeagueBuildRecommendation recommendation)
        {
            var starter = FindRecommendation(recommendation, "starter-items");
            var core = FindRecommendation(recommendation, "core-items");
            if (string.IsNullOrWhiteSpace(starter) && string.IsNullOrWhiteSpace(core))
                return T(LeagueRecommendationUiTextKeys.Unavailable);
            return string.Format(
                T(LeagueRecommendationUiTextKeys.ItemSummaryFormat),
                string.IsNullOrWhiteSpace(starter) ? "--" : starter,
                string.IsNullOrWhiteSpace(core) ? "--" : core);
        }

        private string DisplayRecommendation(LeagueBuildRecommendation recommendation, string category)
        {
            var value = FindRecommendation(recommendation, category);
            return string.IsNullOrWhiteSpace(value) ? T(LeagueRecommendationUiTextKeys.Unavailable) : value;
        }

        private static string FindRecommendation(LeagueBuildRecommendation recommendation, string category)
        {
            if (recommendation == null) return string.Empty;
            var row = recommendation.Rows.FirstOrDefault(item =>
                string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
            return row == null ? string.Empty : row.Recommendation ?? string.Empty;
        }

        private static string BuildContext(LeagueBuildAdvisorSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;
            var champion = string.IsNullOrWhiteSpace(snapshot.ChampionName)
                ? "#" + snapshot.ChampionId
                : snapshot.ChampionName + " #" + snapshot.ChampionId;
            return champion + " · " + (snapshot.Mode ?? string.Empty) + " / " + (snapshot.Position ?? string.Empty) +
                   " · " + (snapshot.Source ?? string.Empty) + " " + (snapshot.Version ?? string.Empty);
        }

        private void SetButtons(bool canApply)
        {
            _refreshButton.Enabled = !_busy;
            _applyButton.Enabled = !_busy && canApply && (_runesChoice.Checked || _spellsChoice.Checked || _itemsChoice.Checked);
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

        private string T(string key)
        {
            return LeagueRecommendationText.Get(_ui, key);
        }

        private sealed class RecommendationHeaderPanel : Panel
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width <= 0) return;
                using (var glow = new LinearGradientBrush(
                    new Rectangle(0, Height - 4, Width, 4),
                    Color.FromArgb(73, 215, 255),
                    Color.FromArgb(142, 72, 255),
                    LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillRectangle(glow, 0, Height - 4, Width, 4);
                }
            }
        }
    }
}
