using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The combo catalog read from the embedded asset bundle: catalog.json (classes and specs), skills.json (names and
/// Spanish descriptions) and guide-&lt;class&gt;.&lt;spec&gt;.json (one per guide). Guides are read once and cached.
/// A missing or invalid file is logged: the catalog then lists fewer classes, or the guide is null.
/// </summary>
public sealed class JsonCombosCatalog : ICombosCatalog
{
    /// <summary>The schema version this code reads. Files with another version are refused and logged.</summary>
    public const int SupportedSchema = 1;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly IEmbeddedResources _resources;
    private readonly IAssetProvider _assets;
    private readonly AppLogger _logger;
    private readonly IReadOnlyDictionary<int, SkillEntryJson> _skills;
    private readonly ConcurrentDictionary<string, ComboGuide?> _guides = new(StringComparer.Ordinal);

    private JsonCombosCatalog(
        IReadOnlyList<ClassInfo> classes,
        IReadOnlyDictionary<int, SkillEntryJson> skills,
        IEmbeddedResources resources,
        IAssetProvider assets,
        AppLogger logger)
    {
        Classes = classes;
        _skills = skills;
        _resources = resources;
        _assets = assets;
        _logger = logger;
    }

    public IReadOnlyList<ClassInfo> Classes { get; }

    /// <summary>Reads catalog.json and skills.json now. A missing or invalid file gives an empty catalog and a logged error.</summary>
    public static JsonCombosCatalog Load(IEmbeddedResources resources, IAssetProvider assets, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(logger);

        using var scope = logger.BeginOperation("combo.catalog.load");
        var classes = ReadClasses(resources, logger, scope);
        var skills = ReadSkills(resources, logger, scope);
        logger.Info("combo.catalog.loaded", "Combo catalog read", new Dictionary<string, object?>
        {
            ["classes"] = classes.Count,
            ["skills"] = skills.Count,
        });
        return new JsonCombosCatalog(classes, skills, resources, assets, logger);
    }

    public Task<ComboGuide?> LoadGuideAsync(int classType, string specId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specId);

        var classInfo = Classes.FirstOrDefault(item => item.ClassType == classType);
        var spec = classInfo?.Specs.FirstOrDefault(item => string.Equals(item.Id, specId, StringComparison.Ordinal));
        if (classInfo is null || spec is null)
        {
            _logger.Warn("combo.guide.unknown_spec", "The catalog has no guide for this class and spec", new Dictionary<string, object?>
            {
                ["class_type"] = classType,
                ["spec"] = specId,
            });
            return Task.FromResult<ComboGuide?>(null);
        }

        var guide = _guides.GetOrAdd(spec.GuideKey, _ => ReadGuide(spec.GuideKey, classInfo.Slug));
        return Task.FromResult(guide);
    }

    private ComboGuide? ReadGuide(string key, string classSlug)
    {
        using var scope = _logger.BeginOperation("combo.guide.read", new Dictionary<string, object?>
        {
            ["guide"] = key,
        });
        var name = BundleResourceNames.Guide(key);
        using var stream = _resources.Open(name);
        if (stream is null)
        {
            _logger.Warn("combo.guide.missing", "The bundle has no file for this guide", new Dictionary<string, object?>
            {
                ["guide"] = key,
            });
            return null;
        }

        try
        {
            var json = JsonSerializer.Deserialize<GuideJson>(stream, Options);
            if (json is null || json.Schema != SupportedSchema)
            {
                _logger.Warn("combo.guide.schema", "The guide file has an unsupported schema", new Dictionary<string, object?>
                {
                    ["guide"] = key,
                    ["schema"] = json?.Schema,
                });
                return null;
            }

            var guide = ToGuide(json, classSlug);
            _logger.Info("combo.guide.loaded", "Guide read", new Dictionary<string, object?>
            {
                ["guide"] = key,
                ["chains"] = guide.Chains.Count,
                ["steps"] = guide.Chains.Sum(chain => chain.Steps.Count),
                ["notes"] = guide.Notes.Count,
                ["addons"] = guide.Addons.Count,
            });
            return guide;
        }
        catch (JsonException ex)
        {
            scope.Fail(ex);
            _logger.Error("combo.guide.invalid", "The guide file is not valid JSON", ex, new Dictionary<string, object?>
            {
                ["guide"] = key,
            });
            return null;
        }
    }

    private ComboGuide ToGuide(GuideJson json, string classSlug) =>
        new(
            Title: json.Title ?? string.Empty,
            Description: json.Description,
            Source: json.Source ?? string.Empty,
            Chains: OrEmpty(json.Chains).Select(chain => ToChain(chain, classSlug)).ToList(),
            Notes: OrEmpty(json.Notes)
                .Select(note => new ComboNote(note.Section ?? string.Empty, note.Text ?? string.Empty, ToSkills(note.Skills, classSlug)))
                .ToList(),
            Addons: OrEmpty(json.Addons)
                .Where(addon => addon.Skill is not null)
                .Select(addon => new ComboAddon(
                    addon.Section ?? string.Empty,
                    ToSkill(addon.Skill!, classSlug),
                    addon.Description,
                    OrEmpty(addon.Effects).Select(effect => new ComboAddonEffect(effect.Label ?? string.Empty, effect.Value ?? string.Empty)).ToList()))
                .ToList());

    private ComboChain ToChain(ChainJson chain, string classSlug) =>
        new(
            Section: chain.Section ?? string.Empty,
            Title: chain.Title ?? string.Empty,
            Notes: OrEmpty(chain.Notes),
            Steps: OrEmpty(chain.Steps)
                .Select(step => new ComboStep(step.Keys, ToSkills(step.Skills, classSlug), step.Extra, step.Note))
                .ToList());

    private IReadOnlyList<ComboSkill> ToSkills(List<SkillRefJson>? refs, string classSlug) =>
        OrEmpty(refs).Select(reference => ToSkill(reference, classSlug)).ToList();

    private ComboSkill ToSkill(SkillRefJson reference, string classSlug)
    {
        _skills.TryGetValue(reference.SkillNo, out var entry);
        var english = string.IsNullOrWhiteSpace(reference.NameEn) ? entry?.NameEn ?? string.Empty : reference.NameEn;
        var spanish = string.IsNullOrWhiteSpace(reference.NameEs) ? entry?.NameEs : reference.NameEs;
        var skillNo = reference.SkillNo;
        return new ComboSkill(
            SkillNo: skillNo,
            Name: new BilingualText(english, spanish),
            DescriptionEs: entry?.DescriptionEs,
            OpenIcon: () => _assets.OpenSkillIcon(skillNo, classSlug));
    }

    private static IReadOnlyList<ClassInfo> ReadClasses(IEmbeddedResources resources, AppLogger logger, OperationScope scope)
    {
        using var stream = resources.Open(BundleResourceNames.Catalog);
        if (stream is null)
        {
            logger.Warn("combo.catalog.missing", "The bundle has no catalog.json; no combo guides are available");
            return [];
        }

        try
        {
            var json = JsonSerializer.Deserialize<CatalogJson>(stream, Options);
            if (json is null || json.Schema != SupportedSchema)
            {
                logger.Warn("combo.catalog.schema", "catalog.json has an unsupported schema", new Dictionary<string, object?>
                {
                    ["schema"] = json?.Schema,
                });
                return [];
            }

            return OrEmpty(json.Classes)
                .OrderBy(item => item.ClassType)
                .Select(item => new ClassInfo(
                    item.ClassType,
                    item.Slug ?? string.Empty,
                    new BilingualText(item.Name?.En ?? item.Slug ?? string.Empty, item.Name?.Es),
                    OrEmpty(item.Specs)
                        .Select(spec => new SpecInfo(
                            spec.Id ?? string.Empty,
                            new BilingualText(spec.DisplayName?.En ?? spec.Id ?? string.Empty, spec.DisplayName?.Es),
                            spec.Guide ?? string.Empty))
                        .ToList()))
                .ToList();
        }
        catch (JsonException ex)
        {
            scope.Fail(ex);
            logger.Error("combo.catalog.invalid", "catalog.json is not valid JSON; no combo guides are available", ex);
            return [];
        }
    }

    private static IReadOnlyDictionary<int, SkillEntryJson> ReadSkills(IEmbeddedResources resources, AppLogger logger, OperationScope scope)
    {
        var result = new Dictionary<int, SkillEntryJson>();
        using var stream = resources.Open(BundleResourceNames.Skills);
        if (stream is null)
        {
            logger.Warn("combo.skills.missing", "The bundle has no skills.json; skill names come from the guides only");
            return result;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, SkillEntryJson>>(stream, Options);
            foreach (var (key, entry) in raw ?? new Dictionary<string, SkillEntryJson>())
            {
                if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var skillNo))
                {
                    result[skillNo] = entry;
                }
                else
                {
                    logger.Warn("combo.skills.bad_key", "skills.json has a key that is not a skill number", new Dictionary<string, object?>
                    {
                        ["key"] = key,
                    });
                }
            }
        }
        catch (JsonException ex)
        {
            scope.Fail(ex);
            logger.Error("combo.skills.invalid", "skills.json is not valid JSON; skill names come from the guides only", ex);
        }

        return result;
    }

    private static List<T> OrEmpty<T>(List<T>? items) => items ?? [];
}
