using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BdoComboSetup.Services;

/// <summary>
/// Applies the saved color theme and accent color to the window. The theme is the <c>RequestedTheme</c> of the window root
/// (ElementTheme.Default follows Windows; the application theme cannot change at run time). The accent is written in place
/// into the accent brushes and colors of the glass palette (the Light and the Dark theme dictionaries of the palette merged
/// in App.xaml), so every ThemeResource bound to them updates at once, without reloading the pages.
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

    /// <summary>Brushes that show the accent itself (fills, text, selection, toggles, checks, sliders, focus bars).</summary>
    private static readonly string[] AccentKeys =
    [
        "AccentFillColorDefaultBrush", "AccentFillColorSelectedTextBackgroundBrush", "AccentTextFillColorPrimaryBrush",
        "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush", "HyperlinkForeground",
        "NavigationViewSelectionIndicatorForeground", "ListViewItemSelectionIndicatorBrush", "TextControlBorderBrushFocused",
        "TextControlElevationBorderFocusedBrush", "PreviewSlotHoverBorderBrush", "ToggleSwitchFillOn",
        "CheckBoxCheckBackgroundFillChecked", "RadioButtonOuterEllipseCheckedFill", "SliderTrackValueFill", "SliderThumbBackground",
        "AccentButtonBackground",
    ];

    /// <summary>The pointer-over shade of the accent brushes (the same key family as <see cref="AccentKeys"/>).</summary>
    private static readonly string[] AccentPointerOverKeys =
    [
        "AccentFillColorSecondaryBrush", "ListViewItemSelectionIndicatorPointerOverBrush", "ToggleSwitchFillOnPointerOver",
        "CheckBoxCheckBackgroundFillCheckedPointerOver", "RadioButtonOuterEllipseCheckedFillPointerOver",
        "SliderTrackValueFillPointerOver", "SliderThumbBackgroundPointerOver", "AccentButtonBackgroundPointerOver",
    ];

    /// <summary>The pressed shade of the accent brushes.</summary>
    private static readonly string[] AccentPressedKeys =
    [
        "AccentFillColorTertiaryBrush", "ListViewItemSelectionIndicatorPressedBrush", "ToggleSwitchFillOnPressed",
        "CheckBoxCheckBackgroundFillCheckedPressed", "RadioButtonOuterEllipseCheckedFillPressed", "SliderTrackValueFillPressed",
        "SliderThumbBackgroundPressed", "AccentButtonBackgroundPressed",
    ];

    /// <summary>Text and icons drawn on top of the accent: dark or white, by the luminance of the accent.</summary>
    private static readonly string[] OnAccentKeys =
    [
        "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed",
        "TextOnAccentFillColorPrimaryBrush", "ToggleSwitchKnobFillOn",
    ];

    /// <summary>
    /// The caption buttons of the title bar for one actual theme: the glyph colors (ARGB, the palette's text colors), the muted
    /// glyph of an inactive window, and the subtle tints of the hover and pressed backgrounds.
    /// </summary>
    private sealed record CaptionColors(string Foreground, string Inactive, string Hover, string Pressed);

    private static readonly CaptionColors LightCaption = new("#FF141325", "#FF6F6D86", "#1A141325", "#2E141325");
    private static readonly CaptionColors DarkCaption = new("#FFF1F0FB", "#FF9A98B4", "#1AFFFFFF", "#2EFFFFFF");

    private readonly AppSettingsStore _settings;
    private readonly SettingsViewModel _settingsView;
    private readonly AppLogger _logger;
    private AppWindow? _appWindow;
    private FrameworkElement? _root;

    public ThemeService(AppSettingsStore settings, SettingsViewModel settingsView, AppLogger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsView = settingsView ?? throw new ArgumentNullException(nameof(settingsView));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The accent color of one accent id, for the light or the dark theme (Settings paints its swatches with it).</summary>
    public static Color AccentFor(string accent, bool dark)
    {
        var (light, darkHex) = AccentHex[AccentColors.Normalize(accent)];
        return FromHex(dark ? darkHex : light);
    }

    /// <summary>
    /// Attaches to the root element of the window (once) and to its <see cref="AppWindow"/>, whose title bar holds the caption
    /// buttons. It applies the saved settings at once, then again when the theme or the accent changes in Settings and when the
    /// actual theme changes (Windows switched between light and dark).
    /// </summary>
    public void Attach(FrameworkElement root, AppWindow appWindow)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(appWindow);
        if (_root is not null)
        {
            return;
        }

        _root = root;
        _appWindow = appWindow;
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

        // The actual theme decides the caption colors: the explicit choice, or what Windows shows when the theme follows the system.
        var dark = requested == ElementTheme.Dark || (requested == ElementTheme.Default && _root.ActualTheme == ElementTheme.Dark);
        ApplyCaptionButtons(dark);

        var (lightDictionary, darkDictionary) = FindPalette(Application.Current.Resources);
        if (lightDictionary is null || darkDictionary is null)
        {
            _logger.Error(
                "theme.dictionary_missing",
                "The glass palette (the merged dictionary with the Light and Dark themes) is missing; the accent color was not applied",
                new InvalidOperationException("App.xaml must merge a palette dictionary that defines the Light and Dark ThemeDictionaries."));
            return;
        }

        ApplyAccent(lightDictionary, AccentFor(accent, dark: false), dark: false);
        ApplyAccent(darkDictionary, AccentFor(accent, dark: true), dark: true);

        _logger.Info("theme.applied", "Theme and accent color applied", new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["theme"] = theme,
            ["accent"] = accent,
            ["actual_theme"] = _root.ActualTheme.ToString(),
            ["caption_dark"] = dark,
        });
    }

    /// <summary>
    /// Colors the caption buttons (minimize, maximize, close) for the actual theme. The buttons have no background at rest, a
    /// subtle tint on hover and press, and the palette's text color. AppWindowTitleBar has one hover color for all three buttons,
    /// so the close button does not get its own red hover here.
    /// </summary>
    private void ApplyCaptionButtons(bool dark)
    {
        if (_appWindow is null)
        {
            return;
        }

        var colors = dark ? DarkCaption : LightCaption;
        var foreground = FromHex(colors.Foreground);
        var titleBar = _appWindow.TitleBar;
        var transparent = Color.FromArgb(0, 0, 0, 0);

        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = FromHex(colors.Inactive);
        titleBar.ButtonHoverBackgroundColor = FromHex(colors.Hover);
        titleBar.ButtonPressedBackgroundColor = FromHex(colors.Pressed);
    }

    /// <summary>
    /// The palette is the last merged dictionary of the application that has the Light and the Dark theme dictionaries.
    /// It is merged after XamlControlsResources, so its keys win over the library's own (see App.xaml).
    /// </summary>
    private static (ResourceDictionary? Light, ResourceDictionary? Dark) FindPalette(ResourceDictionary application)
    {
        for (var i = application.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var merged = application.MergedDictionaries[i];
            if (merged.ThemeDictionaries.TryGetValue(LightDictionary, out var light) && light is ResourceDictionary lightDictionary
                && merged.ThemeDictionaries.TryGetValue(DarkDictionary, out var dark) && dark is ResourceDictionary darkDictionary)
            {
                return (lightDictionary, darkDictionary);
            }
        }

        return (null, null);
    }

    /// <summary>Writes one accent into one theme of the palette: the system accent colors, the accent brushes and the glows.</summary>
    private void ApplyAccent(ResourceDictionary dictionary, Color accent, bool dark)
    {
        var white = Color.FromArgb(255, 255, 255, 255);
        var black = Color.FromArgb(255, 0, 0, 0);
        var onAccent = RelativeLuminance(accent) > OnAccentLuminanceThreshold
            ? Color.FromArgb(255, 0x16, 0x0F, 0x06)
            : white;

        // The shades: on the dark theme the pointer-over is lighter, on the light theme darker (the accent is lighter there).
        var pointerOver = dark ? Mix(accent, white, 0.12) : Mix(accent, black, 0.08);
        var pressed = dark ? Mix(accent, black, 0.12) : Mix(accent, black, 0.16);

        // The system accent colors (App resources override them; the Windows App SDK controls read them).
        SetColor(dictionary, "SystemAccentColor", accent);
        SetColor(dictionary, "SystemAccentColorLight1", Mix(accent, white, 0.15));
        SetColor(dictionary, "SystemAccentColorLight2", Mix(accent, white, 0.30));
        SetColor(dictionary, "SystemAccentColorLight3", Mix(accent, white, 0.50));
        SetColor(dictionary, "SystemAccentColorDark1", Mix(accent, black, 0.10));
        SetColor(dictionary, "SystemAccentColorDark2", Mix(accent, black, 0.25));
        SetColor(dictionary, "SystemAccentColorDark3", Mix(accent, black, 0.40));

        foreach (var key in AccentKeys)
        {
            SetSolid(dictionary, key, accent);
        }

        foreach (var key in AccentPointerOverKeys)
        {
            SetSolid(dictionary, key, pointerOver);
        }

        foreach (var key in AccentPressedKeys)
        {
            SetSolid(dictionary, key, pressed);
        }

        foreach (var key in OnAccentKeys)
        {
            SetSolid(dictionary, key, onAccent);
        }

        // The cooldown frames of the preview: the accent at the same alpha as the frames of the game surface.
        SetSolid(dictionary, "PreviewSlotFrameBrush", WithAlpha(accent, 0x73));
        SetSolid(dictionary, "PreviewSlotHoverFillBrush", WithAlpha(accent, 0x4D));

        // The accent gradient: lighter at the start, the accent at the end (the pills and the logo).
        SetGradient(dictionary, "AccentGradientBrush", Mix(accent, white, 0.22), accent);

        // The top-left glow of the window backdrop: the accent, keeping the alpha of the theme (26% dark, 30% light).
        SetGlow(dictionary, "GlassGlowAccentBrush", accent);
    }

    private void SetColor(ResourceDictionary dictionary, string key, Color color)
    {
        if (dictionary.ContainsKey(key))
        {
            dictionary[key] = color;
            return;
        }

        _logger.Warn("theme.resource_missing", "A theme color is missing; the accent does not reach it", new Dictionary<string, object?>
        {
            ["key"] = key,
        });
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

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color WithRgbOf(Color alphaSource, Color rgbSource) =>
        Color.FromArgb(alphaSource.A, rgbSource.R, rgbSource.G, rgbSource.B);

    /// <summary>Parses <c>#RRGGBB</c> (opaque) or <c>#AARRGGBB</c> into a color.</summary>
    private static Color FromHex(string hex)
    {
        var argb = hex.Length == 9 ? hex : "#FF" + hex.Substring(1);
        return Color.FromArgb(
            Convert.ToByte(argb.Substring(1, 2), 16),
            Convert.ToByte(argb.Substring(3, 2), 16),
            Convert.ToByte(argb.Substring(5, 2), 16),
            Convert.ToByte(argb.Substring(7, 2), 16));
    }

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
