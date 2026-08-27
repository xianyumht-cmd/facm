using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueGameRepairForm : Form
    {
        private readonly ToolsModule _tools;
        private readonly LeagueEfficiencyModule _efficiency;
        private readonly Label _status;

        public LeagueGameRepairForm(ToolsModule tools, LeagueEfficiencyModule efficiency)
        {
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _efficiency = efficiency ?? throw new ArgumentNullException(nameof(efficiency));

            Text = LeagueGameRepairUiText.Title;
            FormBorderStyle = FormBorderStyle.None;
            TopLevel = false;
            Dock = DockStyle.Fill;
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            var intro = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var title = Label(LeagueGameRepairUiText.Title, 13F, FontStyle.Bold, FacmDesignSystem.Text);
            title.Location = new Point(2, 0);
            title.Size = new Size(180, 26);
            var hint = Label(LeagueGameRepairUiText.Hint, 8.5F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            hint.Location = new Point(2, 28);
            hint.Size = new Size(660, 22);
            intro.Controls.Add(title);
            intro.Controls.Add(hint);
            root.Controls.Add(intro, 0, 0);

            var windowCard = new FacmGlassPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), DrawBorder = true };
            var windowTitle = Label(LeagueGameRepairUiText.WindowGroup, 9.6F, FontStyle.Bold, FacmDesignSystem.Text);
            windowTitle.Location = new Point(16, 12);
            windowTitle.Size = new Size(180, 22);
            windowCard.Controls.Add(windowTitle);
            windowCard.Controls.Add(ActionButton(LeagueGameRepairUiText.FixNow, LeagueGameRepairUiText.FixNowHint, 16, 42, delegate { RunFixMode(1, LeagueGameRepairUiText.FixNow); }));
            windowCard.Controls.Add(ActionButton(LeagueGameRepairUiText.FixAuto, LeagueGameRepairUiText.FixAutoHint, 350, 42, delegate { RunFixMode(2, LeagueGameRepairUiText.FixAuto); }));
            root.Controls.Add(windowCard, 0, 1);

            var lobbyCard = new FacmGlassPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), DrawBorder = true };
            var lobbyTitle = Label(LeagueGameRepairUiText.LobbyGroup, 9.6F, FontStyle.Bold, FacmDesignSystem.Text);
            lobbyTitle.Location = new Point(16, 12);
            lobbyTitle.Size = new Size(180, 22);
            lobbyCard.Controls.Add(lobbyTitle);
            lobbyCard.Controls.Add(ActionButton(LeagueGameRepairUiText.SkipSettlement, LeagueGameRepairUiText.SkipSettlementHint, 16, 42, delegate { RunFixMode(3, LeagueGameRepairUiText.SkipSettlement); }));
            lobbyCard.Controls.Add(ActionButton(LeagueGameRepairUiText.RestartUx, LeagueGameRepairUiText.RestartUxHint, 350, 42, delegate { RunFixMode(4, LeagueGameRepairUiText.RestartUx); }));
            lobbyCard.Controls.Add(ActionButton(LeagueGameRepairUiText.ExitGame, LeagueGameRepairUiText.ExitGameHint, 16, 106, async delegate { await ExitGameAsync(); }));
            root.Controls.Add(lobbyCard, 0, 2);

            var statusCard = new FacmGlassPanel { Dock = DockStyle.Fill, DrawBorder = true };
            _status = Label(LeagueGameRepairUiText.Ready, 8.7F, FontStyle.Bold, FacmDesignSystem.TextMuted);
            _status.Dock = DockStyle.Fill;
            _status.Padding = new Padding(16, 0, 16, 0);
            statusCard.Controls.Add(_status);
            root.Controls.Add(statusCard, 0, 3);

            FacmDesignSystem.ApplyLeagueSurface(this);
        }

        private Control ActionButton(string title, string hint, int x, int y, Action action)
        {
            var panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(318, 58),
                BackColor = Color.Transparent
            };
            var button = new Button
            {
                Text = title,
                Location = new Point(0, 0),
                Size = new Size(126, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.SurfaceRaised,
                ForeColor = FacmDesignSystem.Text,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderColor = FacmDesignSystem.Border;
            button.Click += delegate { if (action != null) action(); };
            var description = Label(hint, 7.8F, FontStyle.Regular, FacmDesignSystem.TextMuted);
            description.Location = new Point(136, 0);
            description.Size = new Size(178, 55);
            panel.Controls.Add(button);
            panel.Controls.Add(description);
            return panel;
        }

        private void RunFixMode(int mode, string title)
        {
            try
            {
                _tools.RunFixLcu(mode);
                _status.Text = string.Format(LeagueGameRepairUiText.Launched, title);
                _status.ForeColor = FacmDesignSystem.Success;
            }
            catch (Exception exception)
            {
                AppLog.Error("League game repair tool launch failed; mode=" + mode, exception);
                _status.Text = string.Format(LeagueGameRepairUiText.ToolFailed, exception.Message);
                _status.ForeColor = FacmDesignSystem.Error;
            }
        }

        private async Task ExitGameAsync()
        {
            try
            {
                var result = await _efficiency.RunExitGameAsync().ConfigureAwait(true);
                if (result == null)
                {
                    _status.Text = LeagueGameRepairUiText.ExitFailed;
                    _status.ForeColor = FacmDesignSystem.Error;
                    return;
                }

                if (string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    _status.Text = LeagueGameRepairUiText.ExitSuccess;
                    _status.ForeColor = FacmDesignSystem.Success;
                }
                else if (string.Equals(result.Status, "no-target", StringComparison.OrdinalIgnoreCase))
                {
                    _status.Text = LeagueGameRepairUiText.ExitNoTarget;
                    _status.ForeColor = FacmDesignSystem.Warning;
                }
                else
                {
                    _status.Text = LeagueGameRepairUiText.ExitFailed;
                    _status.ForeColor = FacmDesignSystem.Error;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("League one-click exit game failed", exception);
                _status.Text = LeagueGameRepairUiText.ExitFailed;
                _status.ForeColor = FacmDesignSystem.Error;
            }
        }

        private static Label Label(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, size, style),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }
    }
}
