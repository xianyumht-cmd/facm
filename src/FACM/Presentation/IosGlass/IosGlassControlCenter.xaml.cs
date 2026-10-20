using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FACM.League;
using FACM.Services;
using FACM.Theming;
using DrawingColor = System.Drawing.Color;
using WpfColor = System.Windows.Media.Color;

namespace FACM.Presentation.IosGlass
{
    public sealed partial class IosGlassControlCenter : Window
    {
        private readonly Action<string> _dispatch;
        private bool _closing;

        internal IosGlassControlCenter(ThemeDefinition theme, UiTextCatalog ui, Action<string> dispatch)
        {
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            InitializeComponent();

            if (theme == null) throw new ArgumentNullException(nameof(theme));
            var light = theme.IsLight;
            Resources["GlassBackgroundBrush"] = Brush(236, theme.Background);
            Resources["GlassTileBrush"] = Brush((byte)(light ? 232 : 78), theme.Surface);
            Resources["GlassHoverBrush"] = Brush((byte)(light ? 252 : 122), theme.SurfaceSecondary);
            Resources["GlassTextBrush"] = Brush(255, theme.TextPrimary);
            Resources["GlassSecondaryTextBrush"] = Brush(255, theme.TextMuted);
            Resources["GlassAccentBrush"] = Brush(255, theme.Accent);
            Resources["GlassBorderBrush"] = light
                ? Brush(90, DrawingColor.Black)
                : Brush(80, DrawingColor.White);
            Resources["GlassOuterBorderBrush"] = light
                ? Brush(96, DrawingColor.White)
                : Brush(110, DrawingColor.White);

            if (ui != null)
            {
                DashboardText.Text = LeagueHubText.Get(ui, LeagueHubUiTextKeys.Dashboard);
                MatchesText.Text = LeagueHubText.Get(ui, LeagueHubUiTextKeys.Player);
                GuidesText.Text = LeagueHubText.Get(ui, LeagueHubUiTextKeys.Mayhem);
                ToolsText.Text = LeagueHubText.Get(ui, LeagueHubUiTextKeys.Repair);
                ThemeText.Text = ui.Get(UiTextKeys.PanelTheme);
                UpdateText.Text = ui.CheckUpdate;
                LogsText.Text = ui.OpenLog;
            }
        }

        private static Brush Brush(byte opacity, DrawingColor color)
        {
            var brush = new SolidColorBrush(WpfColor.FromArgb(opacity, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private void OnActionClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || _closing) return;

            var action = button.Tag as string;
            _closing = true;
            Close();
            if (!string.IsNullOrEmpty(action) && action != "close")
                _dispatch(action);
        }

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            _closing = true;
            Close();
        }
    }
}
