using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal static class LeagueGameRepairLayout
    {
        internal const int ReadingWidth = 880;
        internal const int SectionInset = 16;
        internal const int ActionGap = 16;

        internal static bool StackAction(int usableWidth)
        {
            return usableWidth < 520;
        }

        internal static void ValidateForSmokeTest()
        {
#if REALTAIIZOR_REPAIR
            var field = typeof(LeagueGameRepairForm).GetField("_view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(GameRepairView))
                throw new InvalidOperationException("Repair candidate is not connected to the product form.");
            using (var view = new GameRepairView()) {
                view.Size = new Size(980, 760);
                view.CreateControl();
                view.ValidateLayout();
            }
            Console.WriteLine("GGman repair candidate: ReaLTaiizor");
#endif
            if (StackAction(520) || !StackAction(519) || !StackAction(320))
                throw new InvalidOperationException("Game repair action layout breakpoint drifted.");
            foreach (var width in new[] { 320, 519, 520, 700, 880 })
            {
                var actionWidth = Math.Min(208, Math.Max(136, width / 4));
                var textWidth = width - actionWidth - ActionGap;
                if (!StackAction(width) && textWidth < 300)
                    throw new InvalidOperationException("Game repair action descriptions lack usable reading width.");
            }
        }
    }

#if !REALTAIIZOR_REPAIR
    internal sealed class LeagueGameRepairForm : Form
    {
        private sealed class RepairRow
        {
            internal Panel Container;
            internal Label Title;
            internal Label Hint;
            internal FacmActionButton Action;
            internal Panel Separator;
        }

        private sealed class RepairSection
        {
            internal Label Heading;
            internal Panel Accent;
            internal FacmGlassPanel Surface;
            internal readonly List<RepairRow> Rows = new List<RepairRow>();
        }

        private readonly LeagueGameRepairService _repair;
        private readonly LeagueEfficiencyModule _efficiency;
        private readonly Panel _viewport;
        private readonly Label _title;
        private readonly Label _hint;
        private readonly Label _status;
        private readonly Panel _statusSurface;
        private readonly RepairSection _windowSection;
        private readonly RepairSection _lobbySection;
        private FacmStatusTone _statusTone = FacmStatusTone.Neutral;
        private Button _autoButton;
        private bool _arranging;

        public LeagueGameRepairForm(LeagueGameRepairService repair, LeagueEfficiencyModule efficiency)
        {
            _repair = repair ?? throw new ArgumentNullException(nameof(repair));
            _efficiency = efficiency ?? throw new ArgumentNullException(nameof(efficiency));

            AutoScaleMode = AutoScaleMode.None;
            Text = LeagueGameRepairUiText.Title;
            FormBorderStyle = FormBorderStyle.None;
            TopLevel = false;
            Dock = DockStyle.Fill;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F);

            _viewport = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas
            };
            _title = MakeLabel(LeagueGameRepairUiText.Title, 15F, FontStyle.Bold, FacmDesignSystem.Text);
            _hint = MakeLabel(LeagueGameRepairUiText.Hint, 9.5F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            _viewport.Controls.Add(_title);
            _viewport.Controls.Add(_hint);

            _windowSection = CreateSection(LeagueGameRepairUiText.WindowGroup);
            _windowSection.Rows.Add(CreateRow(_windowSection, LeagueGameRepairUiText.FixNow,
                LeagueGameRepairUiText.FixNowHint, RepairWindowAsync, FacmButtonTone.Primary));
            _windowSection.Rows.Add(CreateRow(_windowSection, LeagueGameRepairUiText.FixAuto,
                LeagueGameRepairUiText.FixAutoHint, ToggleAutoRepairAsync, FacmButtonTone.Secondary,
                delegate(Button button) { _autoButton = button; }));

            _lobbySection = CreateSection(LeagueGameRepairUiText.LobbyGroup);
            _lobbySection.Rows.Add(CreateRow(_lobbySection, LeagueGameRepairUiText.SkipSettlement,
                LeagueGameRepairUiText.SkipSettlementHint, SkipSettlementAsync, FacmButtonTone.Secondary));
            _lobbySection.Rows.Add(CreateRow(_lobbySection, LeagueGameRepairUiText.RestartUx,
                LeagueGameRepairUiText.RestartUxHint, RestartUxAsync, FacmButtonTone.Secondary));
            _lobbySection.Rows.Add(CreateRow(_lobbySection, LeagueGameRepairUiText.ExitGame,
                LeagueGameRepairUiText.ExitGameHint, ExitGameAsync, FacmButtonTone.Danger));

            _statusSurface = new Panel
            {
                BackColor = FacmDesignSystem.SurfaceRaised,
                TabStop = false
            };
            _status = MakeLabel(LeagueGameRepairUiText.Ready, 9F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            _statusSurface.Controls.Add(_status);
            _viewport.Controls.Add(_statusSurface);
            Controls.Add(_viewport);

            _viewport.Resize += delegate { LayoutRepair(); };
            Shown += delegate { RefreshAutoButton(); LayoutRepair(); };
            VisibleChanged += delegate { if (Visible) RefreshAutoButton(); };
            FacmThemeRuntime.ThemeChanged += RefreshRepairTheme;
            Disposed += delegate { FacmThemeRuntime.ThemeChanged -= RefreshRepairTheme; };
            FacmDesignSystem.ApplyLeagueSurface(this);
        }

        private RepairSection CreateSection(string title)
        {
            var section = new RepairSection
            {
                Heading = MakeLabel(title, 11F, FontStyle.Bold, FacmDesignSystem.Text),
                Accent = new Panel { BackColor = FacmDesignSystem.Accent, TabStop = false },
                Surface = new FacmGlassPanel
                {
                    DrawBorder = true,
                    Radius = FacmDesignSystem.CardRadius,
                    BackColor = FacmDesignSystem.Surface,
                    TabStop = false
                }
            };
            _viewport.Controls.Add(section.Heading);
            _viewport.Controls.Add(section.Accent);
            _viewport.Controls.Add(section.Surface);
            return section;
        }

        private RepairRow CreateRow(
            RepairSection section,
            string title,
            string hint,
            Func<Task> action,
            FacmButtonTone tone,
            Action<Button> capture = null)
        {
            var row = new RepairRow
            {
                Container = new Panel { BackColor = Color.Transparent, TabStop = false },
                Title = MakeLabel(title, 10F, FontStyle.Bold, FacmDesignSystem.Text),
                Hint = MakeLabel(hint, 9F, FontStyle.Regular, FacmDesignSystem.TextMuted),
                Action = new FacmActionButton
                {
                    Text = title,
                    Tone = tone,
                    TabStop = true,
                    AccessibleName = title,
                    Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold)
                },
                Separator = new Panel { BackColor = FacmDesignSystem.BorderSoft, TabStop = false }
            };
            row.Action.Click += async delegate
            {
                if (action == null || row.Action.IsDisposed) return;
                row.Action.Enabled = false;
                try { await action().ConfigureAwait(true); }
                catch (Exception exception)
                {
                    AppLog.Error("League native game repair action failed", exception);
                    SetStatus(LeagueGameRepairUiText.ActionFailed, FacmStatusTone.Error);
                }
                finally
                {
                    if (!row.Action.IsDisposed) row.Action.Enabled = true;
                }
            };
            row.Container.Controls.Add(row.Title);
            row.Container.Controls.Add(row.Hint);
            row.Container.Controls.Add(row.Action);
            row.Container.Controls.Add(row.Separator);
            section.Surface.Controls.Add(row.Container);
            if (capture != null) capture(row.Action);
            return row;
        }

        private int U(int logical)
        {
            return Math.Max(1, (int)Math.Round(logical * Math.Max(96F, DeviceDpi) / 96F));
        }

        private static int TextHeight(Label label, int width, int minHeight)
        {
            var size = TextRenderer.MeasureText(label.Text ?? string.Empty, label.Font,
                new Size(Math.Max(1, width), 16384),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return Math.Max(minHeight, size.Height + 4);
        }

        private void LayoutRepair()
        {
            if (_arranging || _viewport.IsDisposed || IsDisposed || _viewport.ClientSize.Width < 10) return;
            _arranging = true;
            var savedScroll = Math.Max(0, _viewport.VerticalScroll.Value);
            _viewport.SuspendLayout();
            try
            {
                var padding = _viewport.ClientSize.Width < U(780) ? U(16) : U(24);
                var available = Math.Max(U(180), _viewport.ClientSize.Width - 2 * padding -
                    SystemInformation.VerticalScrollBarWidth);
                var width = Math.Min(U(LeagueGameRepairLayout.ReadingWidth), available);
                var y = padding;

                var titleHeight = TextHeight(_title, width, U(30));
                _title.SetBounds(padding, y, width, titleHeight);
                y += titleHeight + U(8);
                var hintHeight = TextHeight(_hint, width, U(24));
                _hint.SetBounds(padding, y, width, hintHeight);
                y += hintHeight + U(26);

                y = LayoutSection(_windowSection, padding, y, width) + U(20);
                y = LayoutSection(_lobbySection, padding, y, width) + U(16);

                var statusHeight = Math.Max(U(44),
                    TextHeight(_status, width - U(32), U(26)) + U(16));
                _statusSurface.SetBounds(padding, y, width, statusHeight);
                _status.SetBounds(U(16), U(8), Math.Max(U(100), width - U(32)),
                    statusHeight - U(16));
                _viewport.AutoScrollMinSize = new Size(0, y + statusHeight + padding);
            }
            finally
            {
                _viewport.ResumeLayout(false);
                _viewport.AutoScrollPosition = new Point(0, savedScroll);
                _arranging = false;
            }
        }

        private int LayoutSection(RepairSection section, int left, int top, int width)
        {
            section.Accent.SetBounds(left, top + U(3), U(3), U(20));
            section.Heading.SetBounds(left + U(12), top, width - U(12),
                TextHeight(section.Heading, width - U(12), U(26)));
            var surfaceTop = top + section.Heading.Height + U(12);
            var content = Math.Max(U(160), width - U(32));
            var rowTop = U(8);
            for (var i = 0; i < section.Rows.Count; i++)
            {
                var row = section.Rows[i];
                var stacked = LeagueGameRepairLayout.StackAction((int)Math.Floor(content * 96.0 / Math.Max(96F, DeviceDpi)));
                var buttonWidth = Math.Min(U(210), Math.Max(U(136),
                    TextRenderer.MeasureText(row.Action.Text, row.Action.Font).Width + U(32)));
                var textWidth = stacked ? content : Math.Max(U(80), content - buttonWidth - U(16));
                var headingHeight = TextHeight(row.Title, textWidth, U(22));
                var hintHeight = TextHeight(row.Hint, textWidth, U(24));
                var buttonHeight = U(36);
                var labelTop = U(10);
                var actionTop = stacked ? labelTop + headingHeight + hintHeight + U(10) : U(10);
                var rowHeight = Math.Max(U(68), actionTop + buttonHeight + U(12));
                if (!stacked)
                    rowHeight = Math.Max(rowHeight, labelTop + headingHeight + hintHeight + U(14));
                row.Container.SetBounds(U(16), rowTop, content, rowHeight);
                row.Title.SetBounds(0, labelTop, textWidth, headingHeight);
                row.Hint.SetBounds(0, labelTop + headingHeight + U(2), textWidth, hintHeight);
                row.Action.SetBounds(stacked ? 0 : content - buttonWidth,
                    actionTop, Math.Min(content, buttonWidth), buttonHeight);
                row.Separator.Visible = i != section.Rows.Count - 1;
                row.Separator.SetBounds(0, rowHeight - U(1), content, U(1));
                rowTop += rowHeight;
            }
            section.Surface.SetBounds(left, surfaceTop, width, rowTop + U(8));
            return surfaceTop + section.Surface.Height;
        }

        private void RefreshRepairTheme(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated) BeginInvoke(new Action(() => RefreshRepairTheme(sender, e)));
                return;
            }
            BackColor = FacmDesignSystem.Canvas;
            _viewport.BackColor = FacmDesignSystem.Canvas;
            _title.ForeColor = FacmDesignSystem.Text;
            _hint.ForeColor = FacmDesignSystem.TextMuted;
            FacmThemeRuntime.ReplaceFont(_title, FacmThemeRuntime.Current.FontName, 15F, FontStyle.Bold);
            FacmThemeRuntime.ReplaceFont(_hint, FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Regular);
            foreach (var section in new[] { _windowSection, _lobbySection })
            {
                section.Heading.ForeColor = FacmDesignSystem.Text;
                section.Accent.BackColor = FacmDesignSystem.Accent;
                section.Surface.BackColor = FacmDesignSystem.Surface;
                FacmThemeRuntime.ReplaceFont(section.Heading, FacmThemeRuntime.Current.FontName, 11F, FontStyle.Bold);
                foreach (var row in section.Rows)
                {
                    row.Title.ForeColor = FacmDesignSystem.Text;
                    row.Hint.ForeColor = FacmDesignSystem.TextMuted;
                    row.Separator.BackColor = FacmDesignSystem.BorderSoft;
                    FacmThemeRuntime.ReplaceFont(row.Title, FacmThemeRuntime.Current.FontName, 10F, FontStyle.Bold);
                    FacmThemeRuntime.ReplaceFont(row.Hint, FacmThemeRuntime.Current.FontName, 9F, FontStyle.Regular);
                    FacmThemeRuntime.ReplaceFont(row.Action, FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold);
                }
            }
            _statusSurface.BackColor = FacmDesignSystem.SurfaceRaised;
            _status.ForeColor = ResolveStatusColor();
            FacmThemeRuntime.ReplaceFont(_status, FacmThemeRuntime.Current.FontName, 9F, FontStyle.Regular);
            LayoutRepair();
        }

        private Color ResolveStatusColor()
        {
            switch (_statusTone)
            {
                case FacmStatusTone.Success: return FacmDesignSystem.Success;
                case FacmStatusTone.Warning: return FacmDesignSystem.Warning;
                case FacmStatusTone.Error: return FacmDesignSystem.Error;
                default: return FacmDesignSystem.TextMuted;
            }
        }

        private static Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, size, style),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false
            };
        }

        private async Task RepairWindowAsync()
        {
            var result = await _repair.RepairWindowAsync(CancellationToken.None).ConfigureAwait(true);
            ShowRepairResult(result);
        }

        private Task ToggleAutoRepairAsync()
        {
            var result = _repair.SetAutoRepairEnabled(!_repair.AutoRepairEnabled);
            RefreshAutoButton();
            ShowRepairResult(result);
            return Task.CompletedTask;
        }

        private async Task SkipSettlementAsync()
        {
            var result = await _repair.SkipSettlementAsync(CancellationToken.None).ConfigureAwait(true);
            ShowRepairResult(result);
        }

        private async Task RestartUxAsync()
        {
            var result = await _repair.RestartClientUxAsync(CancellationToken.None).ConfigureAwait(true);
            ShowRepairResult(result);
        }

        private async Task ExitGameAsync()
        {
            try
            {
                var result = await _efficiency.RunExitGameAsync().ConfigureAwait(true);
                if (result == null)
                {
                    SetStatus(LeagueGameRepairUiText.ExitFailed, FacmStatusTone.Error);
                    return;
                }

                if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                    SetStatus(LeagueGameRepairUiText.ExitSuccess, FacmStatusTone.Success);
                else if (string.Equals(result.Status, "no-target", StringComparison.OrdinalIgnoreCase))
                    SetStatus(LeagueGameRepairUiText.ExitNoTarget, FacmStatusTone.Warning);
                else
                    SetStatus(LeagueGameRepairUiText.ExitFailed, FacmStatusTone.Error);
            }
            catch (Exception exception)
            {
                AppLog.Error("League one-click exit game failed", exception);
                SetStatus(LeagueGameRepairUiText.ExitFailed, FacmStatusTone.Error);
            }
        }

        private void RefreshAutoButton()
        {
            if (_autoButton == null || _autoButton.IsDisposed) return;
            _autoButton.Text = _repair.AutoRepairEnabled
                ? LeagueGameRepairUiText.FixAutoDisable
                : LeagueGameRepairUiText.FixAuto;
            LayoutRepair();
        }

        private void ShowRepairResult(LeagueGameRepairResult result)
        {
            if (result == null)
            {
                SetStatus(LeagueGameRepairUiText.ActionFailed, FacmStatusTone.Error);
                return;
            }
            var tone = result.Success
                ? (result.Changed ? FacmStatusTone.Success : FacmStatusTone.Neutral)
                : FacmStatusTone.Warning;
            SetStatus(result.Message, tone);
        }

        private void SetStatus(string text, FacmStatusTone tone)
        {
            if (_status == null || _status.IsDisposed) return;
            _statusTone = tone;
            _status.Text = text ?? string.Empty;
            _status.ForeColor = ResolveStatusColor();
            LayoutRepair();
        }

    }
#endif
}
