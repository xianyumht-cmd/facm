using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueItemSetForm : Form
    {
        private readonly LeagueBuildAdvisorDataService _readService;
        private readonly LeagueItemSetService _itemSetService;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Label _contextValue;
        private readonly TextBox _previewValue;
        private readonly Label _statusValue;
        private readonly Button _refreshButton;
        private readonly Button _writeButton;
        private LeagueBuildAdvisorSnapshot _snapshot;
        private bool _busy;

        public LeagueItemSetForm(
            LeagueBuildAdvisorDataService readService,
            LeagueItemSetService itemSetService,
            UiTextCatalog ui)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            _itemSetService = itemSetService ?? throw new ArgumentNullException(nameof(itemSetService));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = T(LeagueItemSetUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(780, 500);
            MinimumSize = new Size(720, 455);
            BackColor = Color.FromArgb(14, 19, 30);
            ForeColor = Color.FromArgb(238, 243, 252);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = T(LeagueItemSetUiTextKeys.Title),
                Location = new Point(28, 20),
                Size = new Size(650, 36),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = T(LeagueItemSetUiTextKeys.Hint),
                Location = new Point(30, 60),
                Size = new Size(720, 48),
                ForeColor = Color.FromArgb(146, 161, 188)
            };

            var contextCaption = CreateCaption(T(LeagueItemSetUiTextKeys.Context), 118);
            _contextValue = new Label
            {
                Location = new Point(30, 151),
                Size = new Size(720, 30),
                ForeColor = Color.FromArgb(206, 218, 239),
                AutoEllipsis = true
            };

            var previewCaption = CreateCaption(T(LeagueItemSetUiTextKeys.Preview), 190);
            _previewValue = new TextBox
            {
                Location = new Point(30, 224),
                Size = new Size(720, 180),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(22, 29, 44),
                ForeColor = Color.FromArgb(238, 243, 252),
                ScrollBars = ScrollBars.Vertical
            };

            _statusValue = new Label
            {
                Location = new Point(30, 420),
                Size = new Size(500, 50),
                ForeColor = Color.FromArgb(176, 191, 216),
                AutoEllipsis = true
            };

            _refreshButton = CreateButton(T(LeagueItemSetUiTextKeys.Refresh), Color.FromArgb(35, 43, 60), 110);
            _refreshButton.Location = new Point(536, 438);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshAsync(true); };

            _writeButton = CreateButton(T(LeagueItemSetUiTextKeys.Write), Color.FromArgb(55, 104, 214), 110);
            _writeButton.Location = new Point(652, 438);
            _writeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _writeButton.Enabled = false;
            _writeButton.Click += async delegate { await WriteWithConfirmationAsync(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(contextCaption);
            Controls.Add(_contextValue);
            Controls.Add(previewCaption);
            Controls.Add(_previewValue);
            Controls.Add(_statusValue);
            Controls.Add(_refreshButton);
            Controls.Add(_writeButton);

            ApplyWaitingState();
            Shown += async delegate { await RefreshAsync(false); };
            FormClosed += delegate
            {
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
                Size = new Size(720, 25),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
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
                AppLog.Error("League item-set refresh failed", exception);
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    _statusValue.Text = string.Format(T(LeagueItemSetUiTextKeys.FailedFormat), T(LeagueItemSetUiTextKeys.WriteFailed));
            }
            finally
            {
                _busy = false;
                if (!IsDisposed && !_lifetime.IsCancellationRequested) SetButtons(CanWrite(_snapshot));
            }
        }

        private async Task WriteWithConfirmationAsync()
        {
            if (_busy || !CanWrite(_snapshot) || IsDisposed || _lifetime.IsCancellationRequested) return;
            _busy = true;
            SetButtons(false);
            try
            {
                _statusValue.Text = T(LeagueItemSetUiTextKeys.Preparing);
                var plan = await _itemSetService.PrepareAsync(_snapshot, _lifetime.Token);
                if (plan == null || !plan.HasItems)
                {
                    _statusValue.Text = T(LeagueItemSetUiTextKeys.NoItems);
                    return;
                }

                var preview = BuildPlanPreview(plan);
                var confirmation = string.Format(
                    T(LeagueItemSetUiTextKeys.ConfirmFormat),
                    BuildContext(_snapshot),
                    plan.Blocks.Count,
                    plan.ItemCount,
                    preview);
                var choice = MessageBox.Show(
                    this,
                    confirmation,
                    T(LeagueItemSetUiTextKeys.ConfirmTitle),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (choice != DialogResult.Yes)
                {
                    _statusValue.Text = T(LeagueItemSetUiTextKeys.Ready);
                    return;
                }

                var result = await _itemSetService.ApplyAsync(plan, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                ApplyResult(result);
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                    _statusValue.Text = T(LeagueItemSetUiTextKeys.ChampSelectOnly);
            }
            catch (Exception exception)
            {
                AppLog.Error("League item-set write failed", exception);
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    _statusValue.Text = string.Format(T(LeagueItemSetUiTextKeys.FailedFormat), T(LeagueItemSetUiTextKeys.WriteFailed));
            }
            finally
            {
                _busy = false;
                if (!IsDisposed && !_lifetime.IsCancellationRequested) SetButtons(CanWrite(_snapshot));
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
            _previewValue.Text = BuildRecommendationPreview(snapshot.Recommendation);
            _statusValue.Text = CanWrite(snapshot) ? T(LeagueItemSetUiTextKeys.Ready) : T(LeagueItemSetUiTextKeys.Waiting);
            _writeButton.Enabled = !_busy && CanWrite(snapshot);
        }

        private void ApplyResult(LeagueItemSetWriteResult result)
        {
            if (result == null)
            {
                _statusValue.Text = string.Format(T(LeagueItemSetUiTextKeys.FailedFormat), T(LeagueItemSetUiTextKeys.WriteFailed));
                return;
            }
            if (string.Equals(result.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                _statusValue.Text = T(LeagueItemSetUiTextKeys.ContextChanged);
                return;
            }
            if (result.Succeeded)
            {
                var location = (result.TargetDirectory ?? string.Empty) + "\\" + (result.FileName ?? string.Empty);
                _statusValue.Text = result.CleanupWarning
                    ? string.Format(T(LeagueItemSetUiTextKeys.CleanupWarningFormat), location)
                    : string.Format(T(LeagueItemSetUiTextKeys.SucceededFormat), location, result.RemovedOldFiles);
                return;
            }

            var reason = string.Equals(result.Error, "install-layout-unavailable", StringComparison.OrdinalIgnoreCase)
                ? T(LeagueItemSetUiTextKeys.InstallLayoutUnavailable)
                : T(LeagueItemSetUiTextKeys.WriteFailed);
            _statusValue.Text = string.Format(T(LeagueItemSetUiTextKeys.FailedFormat), reason);
        }

        private void ApplyWaitingState()
        {
            _snapshot = null;
            _contextValue.Text = string.Empty;
            _previewValue.Text = string.Empty;
            _statusValue.Text = T(LeagueItemSetUiTextKeys.Waiting);
            _writeButton.Enabled = false;
        }

        private void SetButtons(bool canWrite)
        {
            _refreshButton.Enabled = !_busy;
            _writeButton.Enabled = !_busy && canWrite;
        }

        private static bool CanWrite(LeagueBuildAdvisorSnapshot snapshot)
        {
            return snapshot != null && snapshot.Connected &&
                   snapshot.Activity == LeagueActivityLevel.ChampSelect &&
                   snapshot.ChampionId > 0 && snapshot.Recommendation != null &&
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

        private static string BuildRecommendationPreview(LeagueBuildRecommendation recommendation)
        {
            if (recommendation == null) return string.Empty;
            var categories = new[] { "starter-items", "boots", "core-items" };
            var rows = recommendation.Rows.Where(row => row != null && categories.Contains(row.Category)).ToList();
            if (rows.Count == 0) return string.Empty;
            return string.Join(Environment.NewLine, rows.Select(row => (row.Category ?? string.Empty) + ": " + (row.Recommendation ?? string.Empty)));
        }

        private static string BuildPlanPreview(LeagueItemSetPlan plan)
        {
            if (plan == null) return string.Empty;
            var builder = new StringBuilder();
            foreach (var block in plan.Blocks)
            {
                if (block == null || block.Items.Count == 0) continue;
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(block.Title ?? string.Empty);
                builder.Append(": ");
                builder.Append(string.Join(", ", block.Items.Select(id => id.ToString()).ToArray()));
            }
            return builder.ToString();
        }

        private string T(string key)
        {
            return LeagueAdvisorText.Get(_ui, key);
        }
    }
}
