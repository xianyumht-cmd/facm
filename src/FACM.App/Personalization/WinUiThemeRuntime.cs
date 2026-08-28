using FACM.Core.Personalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace FACM.App.Personalization;

public sealed class WinUiThemeRuntime : IFacmThemeRuntime
{
    private readonly ResourceDictionary _resources;
    private readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.Ordinal);
    private string _currentThemeId = FacmThemeCatalog.DefaultThemeId;

    public WinUiThemeRuntime(ResourceDictionary resources)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    public string CurrentThemeId => _currentThemeId;
    public bool CustomPaletteActive { get; private set; }

    public void Apply(FacmThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _currentThemeId = theme.Id;

        if (IsHighContrast())
        {
            CustomPaletteActive = false;
            return;
        }

        SetBrush("FacmBackgroundBrush", theme.Background);
        SetBrush("FacmSurfaceBrush", theme.Surface);
        SetBrush("FacmSurfaceSecondaryBrush", theme.SurfaceSecondary);
        SetBrush("FacmTextPrimaryBrush", theme.TextPrimary);
        SetBrush("FacmTextMutedBrush", theme.TextMuted);
        SetBrush("FacmAccentBrush", theme.Accent);
        SetBrush("FacmAccentTextBrush", "#FFFFFFFF");
        SetBrush("FacmStrokeBrush", theme.Border);

        // Existing controls keep references to the same SolidColorBrush instances and therefore repaint
        // immediately. Radius resources are value types, so the new values apply to subsequently created
        // controls/windows without replacing any system-owned WinUI resources.
        _resources["FacmCardCornerRadius"] = new CornerRadius(theme.CardRadius);
        _resources["FacmControlCornerRadius"] = new CornerRadius(theme.ControlRadius);
        CustomPaletteActive = true;
    }

    private void SetBrush(string key, string hex)
    {
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(ParseColor(hex));
            _brushes.Add(key, brush);
            _resources[key] = brush;
            return;
        }

        brush.Color = ParseColor(hex);
    }

    private static bool IsHighContrast()
    {
        try
        {
            return new AccessibilitySettings().HighContrast;
        }
        catch
        {
            // If the accessibility state is unavailable, preserve the platform palette rather than
            // forcing a custom color set during startup.
            return true;
        }
    }

    private static Color ParseColor(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!text.StartsWith('#')) throw new FormatException("Theme colors must use #RRGGBB or #AARRGGBB.");
        text = text[1..];
        if (text.Length is not (6 or 8)) throw new FormatException("Theme colors must use #RRGGBB or #AARRGGBB.");
        var offset = text.Length == 8 ? 2 : 0;
        var alpha = text.Length == 8 ? Convert.ToByte(text[..2], 16) : byte.MaxValue;
        var red = Convert.ToByte(text.Substring(offset, 2), 16);
        var green = Convert.ToByte(text.Substring(offset + 2, 2), 16);
        var blue = Convert.ToByte(text.Substring(offset + 4, 2), 16);
        return Color.FromArgb(alpha, red, green, blue);
    }
}
