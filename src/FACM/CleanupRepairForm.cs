using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal sealed class CleanupRepairForm : Form
    {
        private readonly MainForm _ownerBall;
        private readonly AppSettings _settings;
        private readonly CleanupModule _cleanup;
        private readonly Label _directoryDetail;
        private readonly Label _directoryInstruction;
        private readonly Label _driverState;
        private readonly Label _cleanupState;
        private readonly Label _statusLines;
        private readonly Label _nextStep;
        private readonly Button _directoryButton;
        private bool _driverStarted;
        private bool _cleanupCompleted;
        private bool _cleanupPartial;

        public CleanupRepairForm(MainForm ownerBall, AppSettings settings, CleanupModule cleanup)
        {
            _ownerBall = ownerBall ?? throw new ArgumentNullException(nameof(ownerBall));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));

            Text = CleanupRepairUiText.WindowTitle;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(760, 560);
            MinimumSize = MaximumSize = Size;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            var directoryCard = CreateCard();
            directoryCard.Margin = new Padding(0, 0, 0, 10);
            var directoryTitle = CreateLabel(CleanupRepairUiText.GameDirectory, 11F, FontStyle.Bold, FacmDesignSystem.Text);
            directoryTitle.Location = new Point(18, 14);
            directoryTitle.Size = new Size(220, 26);
            _directoryDetail = CreateLabel(string.Empty, 9F, FontStyle.Bold, FacmDesignSystem.Text);
            _directoryDetail.Location = new Point(18, 45);
            _directoryDetail.Size = new Size(540, 24);
            _directoryDetail.AutoEllipsis = true;
            _directoryInstruction = CreateLabel(string.Empty, 8.2F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            _directoryInstruction.Location = new Point(18, 68);
            _directoryInstruction.Size = new Size(540, 48);
            _directoryInstruction.AutoEllipsis = false;
            _directoryButton = CreateButton(CleanupRepairUiText.SelectDirectory, false);
            _directoryButton.Location = new Point(582, 43);
            _directoryButton.Size = new Size(118, 38);
            _directoryButton.Click += delegate { SelectDirectory(); };
            var detectButton = CreateButton(CleanupRepairUiText.AutoDetect, false);
            detectButton.Location = new Point(582, 84);
            detectButton.Size = new Size(118, 30);
            detectButton.Click += delegate { AutoDetectDirectory(); };
            directoryCard.Controls.Add(directoryTitle);
            directoryCard.Controls.Add(_directoryDetail);
            directoryCard.Controls.Add(_directoryInstruction);
            directoryCard.Controls.Add(_directoryButton);
            directoryCard.Controls.Add(detectButton);
            root.Controls.Add(directoryCard, 0, 0);

            var actionsCard = CreateCard();
            actionsCard.Margin = new Padding(0, 0, 0, 10);
            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 14, 18, 14),
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actionsCard.Controls.Add(actions);

            var driver = BuildAction(
                CleanupRepairUiText.DriverRepair,
                CleanupRepairUiText.DriverHint,
                out _driverState,
                delegate
                {
                    _ownerBall.RunToolA();
                    _driverStarted = true;
                    RefreshState();
                });
            driver.Margin = new Padding(0, 0, 8, 0);
            var cleanupAction = BuildAction(
                CleanupRepairUiText.EnvironmentCleanup,
                CleanupRepairUiText.CleanupHint,
                out _cleanupState,
                ExecuteCleanup);
            cleanupAction.Margin = new Padding(8, 0, 0, 0);
            actions.Controls.Add(driver, 0, 0);
            actions.Controls.Add(cleanupAction, 1, 0);
            root.Controls.Add(actionsCard, 0, 1);

            var statusCard = CreateCard();
            statusCard.Margin = new Padding(0, 0, 0, 10);
            var statusTitle = CreateLabel(CleanupRepairUiText.CurrentStatus, 10.2F, FontStyle.Bold, FacmDesignSystem.Text);
            statusTitle.Location = new Point(18, 14);
            statusTitle.Size = new Size(180, 24);
            _statusLines = CreateLabel(string.Empty, 8.7F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            _statusLines.Location = new Point(18, 43);
            _statusLines.Size = new Size(670, 60);
            _nextStep = CreateLabel(string.Empty, 9F, FontStyle.Bold, FacmDesignSystem.Accent);
            _nextStep.Location = new Point(18, 108);
            _nextStep.Size = new Size(670, 25);
            _nextStep.AutoEllipsis = true;
            statusCard.Controls.Add(statusTitle);
            statusCard.Controls.Add(_statusLines);
            statusCard.Controls.Add(_nextStep);
            root.Controls.Add(statusCard, 0, 2);

            var flowCard = CreateCard();
            var flowTitle = CreateLabel(CleanupRepairUiText.FlowTitle, 9.5F, FontStyle.Bold, FacmDesignSystem.Text);
            flowTitle.Location = new Point(18, 12);
            flowTitle.Size = new Size(150, 22);
            var flow = CreateLabel(CleanupRepairUiText.Flow, 8.5F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            flow.Location = new Point(18, 39);
            flow.Size = new Size(670, 36);
            flow.AutoEllipsis = true;
            flowCard.Controls.Add(flowTitle);
            flowCard.Controls.Add(flow);
            root.Controls.Add(flowCard, 0, 3);

            FacmDesignSystem.ApplyLeagueSurface(this);
            FacmWindowChrome.SetSubtitle(this, CleanupRepairUiText.WindowHint);
            Shown += delegate { RefreshState(); };
        }

        private FacmGlassPanel BuildAction(string title, string hint, out Label state, Action action)
        {
            var card = new FacmGlassPanel { Dock = DockStyle.Fill, DrawBorder = true };
            var titleLabel = CreateLabel(title, 11F, FontStyle.Bold, FacmDesignSystem.Text);
            titleLabel.Location = new Point(16, 12);
            titleLabel.Size = new Size(210, 26);
            var hintLabel = CreateLabel(hint, 8F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            hintLabel.Location = new Point(16, 42);
            hintLabel.Size = new Size(250, 38);
            state = CreateLabel(CleanupRepairUiText.DriverNotRun, 8.4F, FontStyle.Bold, FacmDesignSystem.TextMuted);
            state.Location = new Point(16, 86);
            state.Size = new Size(130, 22);
            var button = CreateButton(title, true);
            button.Location = new Point(176, 82);
            button.Size = new Size(114, 34);
            button.Click += delegate { if (action != null) action(); };
            card.Controls.Add(titleLabel);
            card.Controls.Add(hintLabel);
            card.Controls.Add(state);
            card.Controls.Add(button);
            return card;
        }

        private static FacmGlassPanel CreateCard()
        {
            return new FacmGlassPanel { Dock = DockStyle.Fill, DrawBorder = true, BackColor = FacmDesignSystem.Surface };
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, size, style),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button CreateButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? FacmDesignSystem.Accent : FacmDesignSystem.SurfaceRaised,
                ForeColor = primary ? Color.White : FacmDesignSystem.Text,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderColor = primary ? FacmDesignSystem.AccentSecondary : FacmDesignSystem.Border;
            return button;
        }

        private bool HasValidDirectory()
        {
            return !string.IsNullOrWhiteSpace(_settings.GamePath) && _cleanup.IsValidGameRoot(_settings.GamePath);
        }

        private void RefreshState()
        {
            var hasDirectory = HasValidDirectory();
            if (hasDirectory)
            {
                _directoryDetail.Text = string.Format(CleanupRepairUiText.DirectoryRecognized, _settings.GamePath);
                _directoryDetail.ForeColor = FacmDesignSystem.Success;
                _directoryInstruction.Text = string.Empty;
                _directoryInstruction.Visible = false;
                _directoryButton.Text = CleanupRepairUiText.ManageDirectory;
            }
            else
            {
                _directoryDetail.Text = CleanupRepairUiText.DirectoryMissing;
                _directoryDetail.ForeColor = FacmDesignSystem.TextMuted;
                _directoryInstruction.Text = CleanupRepairUiText.DirectoryPrompt + Environment.NewLine +
                                             CleanupRepairUiText.DirectoryMarkerPrompt + Environment.NewLine +
                                             CleanupRepairUiText.DirectoryExample;
                _directoryInstruction.Visible = true;
                _directoryButton.Text = CleanupRepairUiText.SelectDirectory;
            }

            _driverState.Text = _driverStarted ? CleanupRepairUiText.DriverStarted : CleanupRepairUiText.DriverNotRun;
            _driverState.ForeColor = _driverStarted ? FacmDesignSystem.Success : FacmDesignSystem.TextMuted;
            _cleanupState.Text = _cleanupCompleted
                ? CleanupRepairUiText.CleanupDone
                : (_cleanupPartial ? CleanupRepairUiText.CleanupPartial : CleanupRepairUiText.CleanupNotRun);
            _cleanupState.ForeColor = _cleanupCompleted ? FacmDesignSystem.Success : (_cleanupPartial ? FacmDesignSystem.Warning : FacmDesignSystem.TextMuted);

            _statusLines.Text = (hasDirectory ? CleanupRepairUiText.DirectoryOk : CleanupRepairUiText.DirectoryNeed) + Environment.NewLine +
                                (_driverStarted ? CleanupRepairUiText.DriverOk : CleanupRepairUiText.DriverNeed) + Environment.NewLine +
                                (_cleanupCompleted ? CleanupRepairUiText.CleanupOk : CleanupRepairUiText.CleanupNeed);

            if (!hasDirectory) _nextStep.Text = CleanupRepairUiText.NextDirectory;
            else if (!_driverStarted && !_cleanupCompleted) _nextStep.Text = CleanupRepairUiText.NextBoth;
            else if (!_driverStarted) _nextStep.Text = CleanupRepairUiText.NextDriver;
            else if (!_cleanupCompleted) _nextStep.Text = CleanupRepairUiText.NextCleanup;
            else _nextStep.Text = CleanupRepairUiText.NextFinal;
        }

        private void AutoDetectDirectory()
        {
            try
            {
                var found = _cleanup.FindGameRoot();
                if (string.IsNullOrWhiteSpace(found) || !_cleanup.IsValidGameRoot(found))
                {
                    MessageBox.Show(CleanupRepairUiText.DetectFailed, CleanupRepairUiText.Facm, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SaveDirectory(found);
                MessageBox.Show(CleanupRepairUiText.DetectSuccess, CleanupRepairUiText.Facm, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                AppLog.Error("Cleanup/repair game directory auto-detect failed", exception);
                MessageBox.Show(string.Format(CleanupRepairUiText.OperationFailed, exception.Message), CleanupRepairUiText.Facm, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SelectDirectory()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = CleanupRepairUiText.FolderDialog;
                if (HasValidDirectory()) dialog.SelectedPath = _settings.GamePath;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var resolved = _cleanup.ResolveGameRoot(dialog.SelectedPath);
                if (string.IsNullOrWhiteSpace(resolved) || !_cleanup.IsValidGameRoot(resolved))
                {
                    MessageBox.Show(CleanupRepairUiText.InvalidDirectory, CleanupRepairUiText.Facm, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SaveDirectory(resolved);
            }
        }

        private void SaveDirectory(string path)
        {
            _settings.GamePath = path ?? string.Empty;
            _settings.Save();
            RefreshState();
        }

        private void ExecuteCleanup()
        {
            try
            {
                if (!HasValidDirectory())
                {
                    SelectDirectory();
                    if (!HasValidDirectory()) return;
                }

                var running = _cleanup.GetRunningRelatedProcesses();
                if (running != null && running.Count > 0)
                {
                    MessageBox.Show(
                        string.Format(CleanupRepairUiText.RelatedProcesses, string.Join("\r\n", running)),
                        CleanupRepairUiText.Facm,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (!_cleanup.IsAdministrator)
                {
                    var confirm = MessageBox.Show(
                        CleanupRepairUiText.NeedAdmin,
                        CleanupRepairUiText.Facm,
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Information);
                    if (confirm != DialogResult.OK) return;

                    if (_cleanup.RestartElevatedForCleanup())
                    {
                        Close();
                        _ownerBall.ExitApplication();
                    }
                    else
                    {
                        MessageBox.Show(CleanupRepairUiText.ElevationFailed, CleanupRepairUiText.Facm, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                var plan = _cleanup.CreatePlan(_settings.GamePath);
                if (plan == null || plan.DeletableTargets.Count == 0)
                {
                    MessageBox.Show(CleanupRepairUiText.NoTargets, CleanupRepairUiText.Facm, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _cleanupCompleted = true;
                    _cleanupPartial = false;
                    RefreshState();
                    return;
                }

                using (var review = new CleanupReviewForm(plan))
                {
                    review.TopMost = true;
                    review.ShowDialog(this);
                    if (!review.Confirmed) return;
                }

                var result = _cleanup.Execute(plan);
                _cleanupPartial = result != null && result.Failures.Count > 0;
                _cleanupCompleted = !_cleanupPartial;
                RefreshState();

                if (_cleanupPartial)
                {
                    MessageBox.Show(
                        string.Format(CleanupRepairUiText.CleanupWithFailures, result.Failures.Count),
                        CleanupRepairUiText.Facm,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                MessageBox.Show(CleanupRepairUiText.CleanupComplete, CleanupRepairUiText.Facm, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                AppLog.Error("Cleanup/repair flow failed", exception);
                MessageBox.Show(string.Format(CleanupRepairUiText.OperationFailed, exception.Message), CleanupRepairUiText.Facm, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
