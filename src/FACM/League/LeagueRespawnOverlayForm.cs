using System;
using System.Drawing;
using System.Windows.Forms;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueRespawnOverlayForm : Form
    {
        private readonly Label _countdown;
        private readonly string _format;

        internal LeagueRespawnOverlayForm(string format)
        {
            _format = format ?? "{0}";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(170, 46);
            BackColor = Color.FromArgb(24, 28, 37);
            Opacity = 0.90;
            var area = Screen.PrimaryScreen == null ? SystemInformation.WorkingArea : Screen.PrimaryScreen.WorkingArea;
            Location = new Point(Math.Max(area.Left, area.Right - Width - 28), area.Top + 92);
            _countdown = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(FacmThemeRuntime.Current.FontName, 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 214, 147),
                BackColor = Color.Transparent,
                Text = string.Empty
            };
            Controls.Add(_countdown);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080; // No activate, click-through, tool window.
                return parameters;
            }
        }

        internal void Render(LeagueRespawnTimerSample sample)
        {
            if (sample == null || !sample.IsDead)
            {
                if (Visible) Hide();
                return;
            }
            _countdown.Text = string.Format(_format, Math.Ceiling(sample.TimeLeftSeconds).ToString("0"));
            if (!Visible) Show();
        }
    }
}
