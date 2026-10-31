using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Icons;

/// <summary>
/// Finds skill and class icons on disk. The primary source is the Scrappers export: skills.json maps a skillNo
/// to an icon path (relative to the root), and classes.json maps a classId to one. The file number of an icon is
/// not the skillNo, so the path is always read from skills.json. The fallback is the bdoclasshub download
/// (classes\&lt;class&gt;\skills\icons\&lt;skillNo&gt;_*.webp). Returns null when nothing is found; the caller draws a glyph.
/// </summary>
public sealed class IconCatalog
{
    private const string SkillsFile = "skills.json";
    private const string ClassesFile = "classes.json";

    private readonly IReadOnlyDictionary<string, CatalogEntry> _skills;
    private readonly IReadOnlyDictionary<string, CatalogEntry> _classes;
    private readonly string _primaryRoot;
    private readonly string _fallbackRoot;
    private readonly AppLogger _logger;

    private IconCatalog(
        string primaryRoot,
        string fallbackRoot,
        IReadOnlyDictionary<string, CatalogEntry> skills,
        IReadOnlyDictionary<string, CatalogEntry> classes,
        AppLogger logger)
    {
        _primaryRoot = primaryRoot;
        _fallbackRoot = fallbackRoot;
        _skills = skills;
        _classes = classes;
        _logger = logger;
    }

    /// <summary>Number of skill entries read from skills.json (0 when the file is missing).</summary>
    public int SkillEntryCount => _skills.Count;

    /// <summary>Number of class entries read from classes.json (0 when the file is missing).</summary>
    public int ClassEntryCount => _classes.Count;

    public static IconCatalog Load(string primaryRoot, string fallbackRoot, AppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackRoot);
        ArgumentNullException.ThrowIfNull(logger);

        using var scope = logger.BeginOperation("icons.catalog.load", new Dictionary<string, object?>
        {
            ["primary_root_exists"] = Directory.Exists(primaryRoot),
            ["fallback_root_exists"] = Directory.Exists(fallbackRoot),
        });

        var skills = ReadEntries(Path.Combine(primaryRoot, SkillsFile), logger, scope);
        var classes = ReadEntries(Path.Combine(primaryRoot, ClassesFile), logger, scope);
        logger.Info("icons.catalog.loaded", "Icon catalog read", new Dictionary<string, object?>
        {
            ["skills"] = skills.Count,
            ["classes"] = classes.Count,
        });

        return new IconCatalog(primaryRoot, fallbackRoot, skills, classes, logger);
    }

    /// <summary>Absolute path of the skill icon, or null when neither source has it.</summary>
    public string? GetSkillIcon(int skillNo, string? className = null)
    {
        var key = skillNo.ToString(CultureInfo.InvariantCulture);
        if (_skills.TryGetValue(key, out var entry) && !string.IsNullOrWhiteSpace(entry.IconPath))
        {
            var path = ResolvePrimary(entry.IconPath);
            if (File.Exists(path))
            {
                _logger.Debug("icons.skill.resolved", "Skill icon found in the primary catalog", new Dictionary<string, object?>
                {
                    ["skill_no"] = skillNo,
                    ["source"] = "primary",
                });
                return path;
            }

            _logger.Warn("icons.skill.missing_file", "skills.json points to an icon file that is not on disk", new Dictionary<string, object?>
            {
                ["skill_no"] = skillNo,
            });
        }

        var fallback = FindClassHubIcon(skillNo, className);
        _logger.Debug("icons.skill.resolved", fallback is null ? "No skill icon found" : "Skill icon found in the fallback folder", new Dictionary<string, object?>
        {
            ["skill_no"] = skillNo,
            ["source"] = fallback is null ? "none" : "classhub",
        });
        return fallback;
    }

    /// <summary>Absolute path of the class icon for a classId, or null.</summary>
    public string? GetClassIcon(int classId)
    {
        var key = classId.ToString(CultureInfo.InvariantCulture);
        if (!_classes.TryGetValue(key, out var entry) || string.IsNullOrWhiteSpace(entry.IconPath))
        {
            return null;
        }

        var path = ResolvePrimary(entry.IconPath);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The catalogs store paths with "/" (relative to the export root); Windows paths use the platform separator.</summary>
    private string ResolvePrimary(string relativePath) =>
        Path.GetFullPath(Path.Combine(_primaryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private string? FindClassHubIcon(int skillNo, string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        var folder = className.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        var iconDirectory = Path.Combine(_fallbackRoot, "classes", folder, "skills", "icons");
        if (!Directory.Exists(iconDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(iconDirectory, $"{skillNo.ToString(CultureInfo.InvariantCulture)}_*.webp")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static IReadOnlyDictionary<string, CatalogEntry> ReadEntries(string path, AppLogger logger, OperationScope scope)
    {
        try
        {
            if (!File.Exists(path))
            {
                logger.Warn("icons.catalog.missing", "Catalog file not found; the fallback folder is used", new Dictionary<string, object?>
                {
                    ["file"] = Path.GetFileName(path),
                });
                return new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
            }

            var entries = JsonSerializer.Deserialize<Dictionary<string, CatalogEntry>>(File.ReadAllText(path));
            return entries ?? new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            logger.Warn("icons.catalog.invalid", "Catalog file is not valid JSON; the fallback folder is used", new Dictionary<string, object?>
            {
                ["file"] = Path.GetFileName(path),
            });
            scope.Fail(ex);
            return new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            return new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        }
    }

    internal sealed class CatalogEntry
    {
        [JsonPropertyName("icon_path")]
        public string? IconPath { get; init; }
    }
}
