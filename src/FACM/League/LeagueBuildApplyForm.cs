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
    internal sealed class LeagueBuildApplyForm : Form
    {
        private readonly LeagueBuildAdvisorDataService _readService;
        private readonly LeagueBuildApplyService _applyService;
        private readonly LeagueAutoApplyController _autoController;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly CheckBox _autoToggle;
        private readonly Label _autoStatusValue;
        private readonly Label _contextValue;
        private readonly TextBox _spellValue;
        private readonly TextBox _runeValue;
        private readonly Label _statusValue;
        private readonly Button _refreshButton;
        private readonly Button _applyButton;
        private LeagueBuildAdvisorSnapshot _snapshot;
        private bool _busy;
        private bool _syncingAutoToggle;

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
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(700, 480);
            BackColor = Color.FromArgb(14, 19, 30);
            ForeColor = Color.FromArgb(238, 243, 252);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = T(LeagueBuildApplyUiTextKeys.Title),
                Location = new Point(28, 20),
                Size = new Size(620, 36),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = T(LeagueBuildApplyUiTextKeys.Hint),
                Location = new Point(30, 60),
                Size = new Size(700, 34),
                ForeColor = Color.FromArgb(146, 161, 188)
            };

            _autoToggle = new CheckBox
            {
                Text = T(LeagueAutoApplyUiTextKeys.Toggle),
                Location = new Point(30, 96),
                Size = new Size(292, 28),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Checked = _autoController.Enabled
            };
            _autoStatusValue = new Label
            {
                Location = new Point(326, 96),
                Size = new Size(404, 38),
                ForeColor = Color.FromArgb(146, 161, 188),
                AutoEllipsis = true
            };
            UpdateAutoStatus(_autoController.LastStatus);
            _autoToggle.CheckedChanged += HandleAutoToggleChanged;
            _autoController.StatusChanged += HandleAutoStatusChanged;

            var contextCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Context), 140);
            _contextValue = CreateValueLabel(170, 28);

            var spellCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Spells), 204);
            _spellValue = CreateValueBox(234, 52);

            var runeCaption = CreateCaption(T(LeagueBuildApplyUiTextKeys.Runes), 296);
            _runeValue = CreateValueBox(326, 58);

            _statusValue = new Label
            {
                Location = new Point(30, 430),
                Size = new Size(480, 44),
                ForeColor = Color.FromArgb(176, 191, 216),
                AutoEllipsis = true
            };

            _refreshButton = CreateButton(T(LeagueBuildApplyUiTextKeys.Refresh), Color.FromArgb(35, 43, 60), 110);
            _refreshButton.Location = new Point(516, 452);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshAsync(true); };

            _applyButton = CreateButton(T(LeagueBuildApplyUiTextKeys.Apply), Color.FromArgb(55, 104, 214), 110);
            _applyButton.Location = new Point(632, 452);
            _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _applyButton.Enabled = false;
            _applyButton.Click += async delegate { await ApplyWithConfirmationAsync(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_autoToggle);
            Controls.Add(_autoStatusValue);
            Controls.Add(contextCaption);
            Controls.Add(_contextValue);
            Controls.Add(spellCaption);
            Controls.Add(_spellValue);
            Controls.Add(runeCaption);
            Controls.Add(_runeValue);
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
                Size = new Size(700, 25),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
        }

        private Label CreateValueLabel(int top, int height)
        {
            return new Label
            {
                Location = new Point(30, top),
                Size = new Size(700, height),
                ForeColor = Color.FromArgb(206, 218, 239),
                AutoEllipsis = true
            };
        }

        private TextBox CreateValueBox(int top, int height)
        {
            return new TextBox
            {
                Location = new Point(30, top),
                Size = new Size(700, height),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(22, 29, 44),
                ForeColor = Color.FromArgb(238, 243, 252),
                ScrollBars = ScrollBars.Vertical
            };
        }

        private Button CreateButton(string text, Color background, int width)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 32),
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
                catch (InvalidOperationException) { }
                catch (ObjectDisposedException) { }
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
            _spellValue.Text = FindRecommendation(snapshot.Recommendation, "summoner-spells");
            _runeValue.Text = FindRecommendation(snapshot.Recommendation, "runes");
            _statusValue.Text = CanApply(snapshot)
                ? T(LeagueBuildApplyUiTextKeys.Ready)
                : T(LeagueBuildApplyUiTextKeys.Waiting);
            _applyButton.Enabled = !_busy && CanApply(snapshot);
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
            _contextValue.Text = string.Empty;
            _spellValue.Text = string.Empty;
            _runeValue.Text = string.Empty;
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

        private static string FindRecommendation(LeagueBuildRecommendation recommendation, string category)
        {
            if (recommendation == null) return string.Empty;
            var row = recommendation.Rows.FirstOrDefault(item =>
                string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
            return row == null ? string.Empty : row.Recommendation ?? string.Empty;
        }

        private string T(string key)
        {
            return LeagueAdvisorText.Get(_ui, key);
        }
    }
}
