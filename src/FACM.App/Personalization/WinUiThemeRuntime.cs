using FACM.Core.Personalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace FACM.App.Personalization;

public sealed class WinUiThemeRuntime : IFacmThemeRuntime
{
    private static readonly (string OwnedKey, string PlatformKey)[] SemanticBrushes =
    [
        ("FacmBackgroundBrush", "FacmPlatformBackgroundBrush"),
        ("FacmSurfaceBrush", "FacmPlatformSurfaceBrush"),
        ("FacmSurfaceSecondaryBrush", "FacmPlatformSurfaceSecondaryBrush"),
        ("FacmTextPrimaryBrush", "FacmPlatformTextPrimaryBrush"),
        ("FacmTextMutedBrush", "FacmPlatformTextMutedBrush"),
        ("FacmAccentBrush", "FacmPlatformAccentBrush"),
        ("FacmAccentTextBrush", "FacmPlatformAccentTextBrush"),
        ("FacmSurfaceSecondaryTextBrush", "FacmPlatformTextPrimaryBrush"),
        ("FacmSuccessTextBrush", "FacmPlatformAccentTextBrush"),
        ("FacmWarningTextBrush", "FacmPlatformAccentTextBrush"),
        ("FacmErrorTextBrush", "FacmPlatformAccentTextBrush"),
        ("FacmStrokeBrush", "FacmPlatformStrokeBrush"),
        ("FacmHoverBrush", "FacmPlatformSurfaceSecondaryBrush"),
        ("FacmPressedBrush", "FacmPlatformSurfaceSecondaryBrush"),
        ("FacmSelectedBrush", "FacmPlatformAccentBrush"),
        ("FacmSuccessBrush", "FacmPlatformAccentBrush"),
        ("FacmWarningBrush", "FacmPlatformAccentBrush"),
        ("FacmErrorBrush", "FacmPlatformAccentBrush")
    ];

    private readonly ResourceDictionary _resources;
    private readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Color> _platformColors = new(StringComparer.Ordinal);
    private string _currentThemeId = FacmThemeCatalog.DefaultThemeId;

    public WinUiThemeRuntime(ResourceDictionary resources)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        CaptureOwnedSemanticBrushes();
        RefreshPlatformColors();
    }

    public string CurrentThemeId => _currentThemeId;
    public bool CustomPaletteActive { get; private set; }

    public void Apply(FacmThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _currentThemeId = theme.Id;

        CaptureOwnedSemanticBrushes();
        RefreshPlatformColors();
        if (IsHighContrast())
        {
            RestorePlatformPalette();
            CustomPaletteActive = false;
            return;
        }

        SetBrush("FacmBackgroundBrush", theme.Background);
        SetBrush("FacmSurfaceBrush", theme.Surface);
        SetBrush("FacmSurfaceSecondaryBrush", theme.SurfaceSecondary);
        SetBrush("FacmTextPrimaryBrush", theme.TextPrimary);
        SetBrush("FacmTextMutedBrush", theme.TextMuted);
        SetBrush("FacmAccentBrush", theme.Accent);
        SetBrush("FacmAccentTextBrush", FacmThemeContrast.ReadableForeground(theme.Accent));
        SetBrush("FacmSurfaceSecondaryTextBrush", FacmThemeContrast.ReadableForeground(theme.SurfaceSecondary));
        SetBrush("FacmSuccessTextBrush", FacmThemeContrast.ReadableForeground(theme.Success));
        SetBrush("FacmWarningTextBrush", FacmThemeContrast.ReadableForeground(theme.Warning));
        SetBrush("FacmErrorTextBrush", FacmThemeContrast.ReadableForeground(theme.Warning));
        SetBrush("FacmStrokeBrush", theme.Border);
        SetBrush("FacmHoverBrush", theme.BackgroundSecondary);
        SetBrush("FacmPressedBrush", theme.SurfaceSecondary);
        SetBrush("FacmSelectedBrush", theme.AccentSecondary);
        SetBrush("FacmSuccessBrush", theme.Success);
        SetBrush("FacmWarningBrush", theme.Warning);
        // The catalog has a single caution tone; keep error feedback visibly distinct from the
        // normal surface while avoiding a second, separately persisted palette contract.
        SetBrush("FacmErrorBrush", theme.Warning);

        // These keys are FACM-owned resources. Never mutate a SolidColorBrush obtained from a WinUI
        // platform alias: real Win10 can expose protected WinRT brush instances whose Color setter
        // returns E_ACCESSDENIED. StaticResource consumers keep the same owned brush instances, so
        // changing Color updates already-created FACM surfaces without rebuilding the visual tree.
        _resources["FacmCardCornerRadius"] = new CornerRadius(theme.CardRadius);
        _resources["FacmControlCornerRadius"] = new CornerRadius(theme.ControlRadius);
        CustomPaletteActive = true;
    }

    private void CaptureOwnedSemanticBrushes()
    {
        foreach (var (ownedKey, platformKey) in SemanticBrushes)
        {
            if (_brushes.ContainsKey(ownedKey)) continue;

            SolidColorBrush? owned = null;
            try { owned = _resources[ownedKey] as SolidColorBrush; }
            catch { }

            if (owned is null)
            {
                var color = TryGetPlatformColor(platformKey) ?? Color.FromArgb(0, 0, 0, 0);
                owned = new SolidColorBrush(color);
                _resources[ownedKey] = owned;
            }

            _brushes[ownedKey] = owned;
        }
    }

    private void RefreshPlatformColors()
    {
        foreach (var (ownedKey, platformKey) in SemanticBrushes)
        {
            var color = TryGetPlatformColor(platformKey);
            if (color is not null)
            {
                _platformColors[ownedKey] = color.Value;
            }
            else if (!_platformColors.ContainsKey(ownedKey) && _brushes.TryGetValue(ownedKey, out var owned))
            {
                _platformColors[ownedKey] = owned.Color;
            }
        }
    }

    private Color? TryGetPlatformColor(string platformKey)
    {
        try
        {
            return (_resources[platformKey] as SolidColorBrush)?.Color;
        }
        catch
        {
            return null;
        }
    }

    private void SetBrush(string key, string hex)
    {
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(ParseColor(hex));
            _brushes[key] = brush;
            _resources[key] = brush;
            return;
        }

        brush.Color = ParseColor(hex);
    }

    private void RestorePlatformPalette()
    {
        foreach (var pair in _platformColors)
        {
            if (_brushes.TryGetValue(pair.Key, out var brush)) brush.Color = pair.Value;
        }
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
