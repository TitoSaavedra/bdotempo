using System.Text.Json;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Settings;

/// <summary>
/// Local settings of this PC, stored as JSON in %LOCALAPPDATA%\BdoComboSetup\settings.json.
/// Nothing here is game data; the game files are only read.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Default icon root: the skill and class icons exported by the Scrappers project. Configurable.</summary>
    public const string DefaultSkillIconRoot = @"X:\Github\Personal\Scrappers\output";

    /// <summary>Fallback icon root: the bdoclasshub pages downloaded by the scraper. Configurable.</summary>
    public const string DefaultClassHubRoot = @"X:\Github\Personal\bdo-combo-setup\data\classhub\classes";

    /// <summary>Account folder under UserCache that holds gamevariable.xml.</summary>
    public const string DefaultGameAccountFolder = "330";

    /// <summary>Default Black Desert data folder (the one that holds UserCache and _backups).</summary>
    public const string DefaultGameDataRoot = @"C:\Users\TitoS\OneDrive\Documentos\Black Desert";

    /// <summary>Default skill table, exported by the Scrappers project.</summary>
    public const string DefaultSkillsJsonPath = @"D:\extracted_bdo\class_skills.json";

    public string SkillIconRoot { get; init; } = DefaultSkillIconRoot;

    public string ClassHubRoot { get; init; } = DefaultClassHubRoot;

    public string GameAccountFolder { get; init; } = DefaultGameAccountFolder;

    /// <summary>Black Desert data folder (the one with UserCache). Character files are written under it.</summary>
    public string GameDataRoot { get; init; } = DefaultGameDataRoot;

    /// <summary>Account used by the apply page. Null means auto-detection (the only account, else the most recently played).</summary>
    public string? GameAccount { get; init; }

    /// <summary>Where backup sets are written. Null means "_backups" inside <see cref="GameDataRoot"/>.</summary>
    public string? BackupsRoot { get; init; }

    /// <summary>Skill table (class_skills.json) that combos are checked against. Apply is refused without it.</summary>
    public string SkillsJsonPath { get; init; } = DefaultSkillsJsonPath;

    /// <summary>Rows and columns of the cooldown bar. The default is 2 x 10 (20 slots).</summary>
    public int CooldownRows { get; init; } = 2;

    public int CooldownColumns { get; init; } = 10;

    /// <summary>Last layout selected on the Preview page (GameUiLayoutId name), or null for General.</summary>
    public string? SelectedLayout { get; init; }
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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BdoComboSetup", "settings.json");

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

        try
        {
            if (!File.Exists(_path))
            {
                _logger.Info("settings.defaults", "No settings file yet; defaults are used");
                return new AppSettings();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
            return loaded ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            _logger.Warn("settings.invalid", "settings.json is not valid JSON; defaults are used until the next save");
            scope.Fail(ex);
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            return new AppSettings();
        }
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
