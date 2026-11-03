using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BdoComboSetup.Services;

/// <summary>
/// Applies the saved color theme and accent color to the window. The theme is the <c>RequestedTheme</c> of the window root
/// (ElementTheme.Default follows Windows; the application theme cannot change at run time). The accent is written in place
/// into the brushes of the Light and the Dark theme dictionaries of App.xaml, so every ThemeResource bound to them updates at
/// once, without reloading the pages.
/// </summary>
public sealed class ThemeService
{
    private const string LightDictionary = "Light";
    private const string DarkDictionary = "Dark";

    /// <summary>Accents above this relative luminance take the dark text; the rest take white.</summary>
    private const double OnAccentLuminanceThreshold = 0.3;

    /// <summary>The accent of each id for the light and the dark theme (the same values as the swatches of Settings).</summary>
    private static readonly IReadOnlyDictionary<string, (string Light, string Dark)> AccentHex =
        new Dictionary<string, (string Light, string Dark)>(StringComparer.Ordinal)
        {
            [AccentColors.Violet] = ("#6F5CFF", "#9A8CFF"),
            [AccentColors.Amber] = ("#B8720F", "#E8A33D"),
            [AccentColors.Mint] = ("#0F8F73", "#3ECFAE"),
            [AccentColors.Rose] = ("#E5427A", "#FF7AA6"),
            [AccentColors.Blue] = ("#2B6FE0", "#5AA0FF"),
        };

    private readonly AppSettingsStore _settings;
    private readonly SettingsViewModel _settingsView;
    private readonly AppLogger _logger;
    private FrameworkElement? _root;

    public ThemeService(AppSettingsStore settings, SettingsViewModel settingsView, AppLogger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsView = settingsView ?? throw new ArgumentNullException(nameof(settingsView));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Attaches to the root element of the window (once). It applies the saved settings at once, then again when the
    /// theme or the accent changes in Settings and when the actual theme changes (Windows switched between light and dark).
    /// </summary>
    public void Attach(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_root is not null)
        {
            return;
        }

        _root = root;
        _settingsView.AppearanceChanged += (_, _) => Apply("settings_changed");
        root.ActualThemeChanged += (_, _) => Apply("actual_theme_changed");
        Apply("startup");
    }

    private void Apply(string reason)
    {
        if (_root is null)
        {
            return;
        }

        var theme = AppThemes.Normalize(_settings.Settings.Theme);
        var accent = AccentColors.Normalize(_settings.Settings.AccentColor);

        var requested = theme switch
        {
            AppThemes.Light => ElementTheme.Light,
            AppThemes.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (_root.RequestedTheme != requested)
        {
            _root.RequestedTheme = requested;
        }

        var resources = Application.Current.Resources;
        if (!resources.ThemeDictionaries.TryGetValue(LightDictionary, out var light) || light is not ResourceDictionary lightDictionary
            || !resources.ThemeDictionaries.TryGetValue(DarkDictionary, out var dark) || dark is not ResourceDictionary darkDictionary)
        {
            _logger.Error(
                "theme.dictionary_missing",
                "The Light or Dark theme dictionary is missing; the accent color was not applied",
                new InvalidOperationException("App.xaml must define the Light and Dark ThemeDictionaries."));
            return;
        }

        var (lightHex, darkHex) = AccentHex[accent];
        ApplyAccent(lightDictionary, FromHex(lightHex));
        ApplyAccent(darkDictionary, FromHex(darkHex));

        _logger.Info("theme.applied", "Theme and accent color applied", new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["theme"] = theme,
            ["accent"] = accent,
            ["actual_theme"] = _root.ActualTheme.ToString(),
        });
    }

    /// <summary>Writes one accent into the brushes of one theme dictionary. The text on the accent follows its luminance.</summary>
    private void ApplyAccent(ResourceDictionary dictionary, Color accent)
    {
        var white = Color.FromArgb(255, 255, 255, 255);
        var black = Color.FromArgb(255, 0, 0, 0);
        var onAccent = RelativeLuminance(accent) > OnAccentLuminanceThreshold
            ? Color.FromArgb(255, 0x16, 0x0F, 0x06)
            : white;

        SetSolid(dictionary, "AccentFillColorDefaultBrush", accent);
        SetSolid(dictionary, "AccentTextFillColorPrimaryBrush", accent);
        SetSolid(dictionary, "TextOnAccentFillColorPrimaryBrush", onAccent);
        SetSolid(dictionary, "AccentButtonForeground", onAccent);
        SetSolid(dictionary, "AccentButtonForegroundPointerOver", onAccent);
        SetSolid(dictionary, "AccentButtonForegroundPressed", onAccent);

        // The accent gradient: lighter at the start, the accent at the end (the primary buttons and the logo).
        var lighter = Mix(accent, white, 0.22);
        SetGradient(dictionary, "AccentGradientBrush", lighter, accent);
        SetGradient(dictionary, "AccentButtonBackground", lighter, accent);
        SetGradient(dictionary, "AccentButtonBackgroundPointerOver", Mix(accent, white, 0.32), Mix(accent, white, 0.08));
        SetGradient(dictionary, "AccentButtonBackgroundPressed", accent, Mix(accent, black, 0.12));

        // The top-left glow of the window backdrop: the accent, keeping the alpha of the theme (26% dark, 30% light).
        SetGlow(dictionary, "GlassGlowAccentBrush", accent);
    }

    private void SetSolid(ResourceDictionary dictionary, string key, Color color)
    {
        if (FindBrush<SolidColorBrush>(dictionary, key) is { } brush)
        {
            brush.Color = color;
        }
    }

    private void SetGradient(ResourceDictionary dictionary, string key, Color from, Color to)
    {
        if (FindBrush<LinearGradientBrush>(dictionary, key) is { GradientStops.Count: >= 2 } brush)
        {
            brush.GradientStops[0].Color = from;
            brush.GradientStops[1].Color = to;
        }
    }

    private void SetGlow(ResourceDictionary dictionary, string key, Color accent)
    {
        if (FindBrush<RadialGradientBrush>(dictionary, key) is { GradientStops.Count: >= 2 } brush)
        {
            brush.GradientStops[0].Color = WithRgbOf(brush.GradientStops[0].Color, accent);
            brush.GradientStops[1].Color = WithRgbOf(brush.GradientStops[1].Color, accent);
        }
    }

    private T? FindBrush<T>(ResourceDictionary dictionary, string key) where T : class
    {
        if (dictionary.TryGetValue(key, out var value) && value is T brush)
        {
            return brush;
        }

        _logger.Warn("theme.resource_missing", "A theme brush is missing; the accent does not reach it", new Dictionary<string, object?>
        {
            ["key"] = key,
        });
        return null;
    }

    private static Color WithRgbOf(Color alphaSource, Color rgbSource) =>
        Color.FromArgb(alphaSource.A, rgbSource.R, rgbSource.G, rgbSource.B);

    private static Color FromHex(string hex) => Color.FromArgb(
        255,
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16));

    /// <summary>Moves a color toward the target by the amount (0 keeps the color, 1 gives the target). Opaque.</summary>
    private static Color Mix(Color color, Color target, double amount) => Color.FromArgb(
        255,
        Blend(color.R, target.R, amount),
        Blend(color.G, target.G, amount),
        Blend(color.B, target.B, amount));

    private static byte Blend(byte from, byte to, double amount) => (byte)Math.Round(from + ((to - from) * amount));

    /// <summary>The WCAG relative luminance of an opaque sRGB color (0 black, 1 white).</summary>
    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }
}
