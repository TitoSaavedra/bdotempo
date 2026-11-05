using System.Globalization;
using System.Text.Json;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Settings;

/// <summary>
/// Local settings of this PC, stored as JSON in %LOCALAPPDATA%\BDOTempo\settings.json.
/// Nothing here is game data; the game files are only read.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Black Desert data folder (the one with UserCache). Character files are written under it. Empty when none is saved:
    /// <see cref="AppSettingsStore"/> then uses the first folder found on this PC, and the setup asks for one if none is found.
    /// </summary>
    public string GameDataRoot { get; init; } = string.Empty;

    /// <summary>
    /// The account the app works with, as chosen in the settings (a numeric folder under UserCache). Null means
    /// auto-detection: the only account, else the most recently played one.
    /// </summary>
    public string? GameAccount { get; init; }

    /// <summary>Where backup sets are written. Null means "_backups" inside <see cref="GameDataRoot"/>.</summary>
    public string? BackupsRoot { get; init; }


    /// <summary>Rows and columns of the cooldown bar. The default is 2 x 10 (20 slots).</summary>
    public int CooldownRows { get; init; } = 2;

    public int CooldownColumns { get; init; } = 10;

    /// <summary>Last layout selected on the Preview page (GameUiLayoutId name), or null for General.</summary>
    public string? SelectedLayout { get; init; }

    /// <summary>The interface language: "es" or "en-US". Null means the Windows language, when the app supports it.</summary>
    public string? Language { get; init; }

    /// <summary>True when the reminder before writing to a character was turned off ("Don't show again").</summary>
    public bool SkipWriteHint { get; init; }

    /// <summary>Look for a new version when the app starts (at most once every six hours). On by default.</summary>
    public bool CheckUpdatesOnStartup { get; init; } = true;

    /// <summary>
    /// True once the initial setup was finished (or skipped). Until then the app asks for the data folder and the account
    /// at startup, even if the defaults happen to be right on this PC.
    /// </summary>
    public bool SetupCompleted { get; init; }

    /// <summary>When the last update check ran (UTC), or null when it never ran on this PC.</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    /// <summary>The newest version the last successful check found, shown in Settings.</summary>
    public string? LastSeenVersion { get; init; }

    /// <summary>Color theme: "system" (follows Windows), "light" or "dark". See <see cref="AppThemes"/>.</summary>
    public string Theme { get; init; } = AppThemes.FollowSystem;

    /// <summary>Accent color id (violet, amber, mint, rose or blue). See <see cref="AccentColors"/>.</summary>
    public string AccentColor { get; init; } = AccentColors.Default;

    /// <summary>
    /// True (the default) when the left navigation menu hides itself and slides in when the mouse reaches the left edge.
    /// False keeps the menu always visible. Settings written before this field existed are read with the default.
    /// </summary>
    public bool NavAutoHide { get; init; } = true;

    /// <summary>
    /// True (the default) when resting the pointer on a skill shows its tooltip (icon, name, cost, cooldown, damage). False turns
    /// those tooltips off; the other tooltips stay.
    /// </summary>
    public bool ShowSkillTooltips { get; init; } = true;

    /// <summary>The character chosen last (its pcid), selected again at the next start so it is picked only once. Null when none.</summary>
    public string? LastCharacterPcid { get; init; }
}

/// <summary>The color themes the app supports and how the setting maps to one of them.</summary>
public static class AppThemes
{
    /// <summary>Follow the light or dark mode of Windows.</summary>
    public const string FollowSystem = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static IReadOnlyList<string> Supported { get; } = [FollowSystem, Light, Dark];

    /// <summary>The supported theme (case-insensitive). Empty or unknown values give "system".</summary>
    public static string Normalize(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return FollowSystem;
        }

        return Supported.FirstOrDefault(supported => string.Equals(supported, theme.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? FollowSystem;
    }
}

/// <summary>The accent colors the app offers (their ids are stored in the settings) and how the setting maps to one.</summary>
public static class AccentColors
{
    public const string Violet = "violet";
    public const string Amber = "amber";
    public const string Mint = "mint";
    public const string Rose = "rose";
    public const string Blue = "blue";

    /// <summary>The accent used when the setting is empty or unknown.</summary>
    public const string Default = Violet;

    public static IReadOnlyList<string> Supported { get; } = [Violet, Amber, Mint, Rose, Blue];

    /// <summary>The supported accent id (case-insensitive). Empty or unknown values give "violet".</summary>
    public static string Normalize(string? accent)
    {
        if (string.IsNullOrWhiteSpace(accent))
        {
            return Default;
        }

        return Supported.FirstOrDefault(supported => string.Equals(supported, accent.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? Default;
    }
}

/// <summary>The languages the app ships (the .resw files) and how the setting maps to one of them.</summary>
public static class AppLanguages
{
    public const string Spanish = "es";
    public const string English = "en-US";

    public static IReadOnlyList<string> Supported { get; } = [Spanish, English];

    /// <summary>The supported language of a setting (case-insensitive), or null when the setting is empty or unsupported.</summary>
    public static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        return Supported.FirstOrDefault(supported => string.Equals(supported, language.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The language to run in: the setting when valid, else Spanish for a Spanish Windows language, else English.</summary>
    public static string Resolve(string? setting, CultureInfo systemUiCulture)
    {
        ArgumentNullException.ThrowIfNull(systemUiCulture);
        return Normalize(setting)
            ?? (systemUiCulture.TwoLetterISOLanguageName == "es" ? Spanish : English);
    }
}

/// <summary>Loads and saves <see cref="AppSettings"/>. Every change is written at once (temporary file, then move).</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly AppLogger _logger;
    private readonly object _gate = new();

    public AppSettingsStore(string path, AppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        _path = path;
        _logger = logger;
        Settings = Load();
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BDOTempo", "settings.json");

    public AppSettings Settings { get; private set; }

    /// <summary>Applies a change and saves it. A failed save is logged; the new values stay in memory.</summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_gate)
        {
            Settings = change(Settings);
            Save();
        }
    }

    private AppSettings Load()
    {
        using var scope = _logger.BeginOperation("settings.load", new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(_path),
        });

        var loaded = new AppSettings();
        try
        {
            if (!File.Exists(_path))
            {
                _logger.Info("settings.defaults", "No settings file yet; defaults are used");
            }
            else
            {
                loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            }
        }
        catch (JsonException ex)
        {
            _logger.Warn("settings.invalid", "settings.json is not valid JSON; defaults are used until the next save");
            scope.Fail(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
        }

        // No folder is ever guessed: when none is saved the setup page asks for it (it only suggests the folders it finds).
        return loaded;
    }

    private void Save()
    {
        using var scope = _logger.BeginOperation("settings.save", new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(_path),
        });

        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(Settings, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            scope.Fail(ex);
        }
    }
}
