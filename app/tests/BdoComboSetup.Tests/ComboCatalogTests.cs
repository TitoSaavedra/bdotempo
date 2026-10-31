using System.Globalization;
using System.Reflection;
using System.Text;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Combo;

namespace BdoComboSetup.Tests;

/// <summary>Embedded resources held in memory: name to text. Missing names return null, like the assembly does.</summary>
internal sealed class MemoryResources(IReadOnlyDictionary<string, string> files) : IEmbeddedResources
{
    public Stream? Open(string name) =>
        files.TryGetValue(name, out var text) ? new MemoryStream(Encoding.UTF8.GetBytes(text)) : null;
}

/// <summary>Records which skill and class icons were asked for and returns a fixed byte array.</summary>
internal sealed class RecordingAssets : IAssetProvider
{
    public List<(int SkillNo, string? ClassName)> SkillRequests { get; } = [];

    public Stream? OpenSkillIcon(int skillNo, string? className = null)
    {
        SkillRequests.Add((skillNo, className));
        return new MemoryStream(new byte[] { 1, 2, 3 });
    }

    public Stream? OpenClassIcon(int classType) => null;
}

public sealed class ComboCatalogTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private static class Data
    {
        public const string Catalog = """
            {
              "schema": 1,
              "classes": [
                {
                  "classType": 17, "slug": "shai", "name": { "en": "Shai", "es": null },
                  "specs": [
                    { "id": "azminek", "displayName": { "en": "Azminek", "es": "Azminek" }, "guide": "shai.talent" },
                    { "id": "talent", "displayName": { "en": "Talent", "es": "Talent" }, "guide": "shai.talent" }
                  ]
                },
                {
                  "classType": 16, "slug": "tamer", "name": { "en": "Tamer", "es": "Domadora" },
                  "specs": [
                    { "id": "awakening", "displayName": { "en": "Awakening", "es": "Despertar" }, "guide": "tamer.awakening" },
                    { "id": "missing", "displayName": { "en": "Missing", "es": "Falta" }, "guide": "tamer.missing" }
                  ]
                }
              ]
            }
            """;

        public const string Skills = """
            {
              "1937": { "nameEn": "Flow: Intimidation", "nameEs": "Flujo: Intimidación", "descriptionEs": "Proyecta el último ataque." },
              "1932": { "nameEn": "Moonlight Strike", "nameEs": "Golpe de la Luna III", "descriptionEs": null }
            }
            """;

        public const string Guide = """
            {
              "schema": 1, "classType": 16, "classSlug": "tamer", "spec": "awakening",
              "title": "Tamer Awakening", "description": "PvE Combo Guide",
              "source": "https://bdoclasshub.com/classes/tamer/awakening/index.html",
              "chains": [
                {
                  "section": "skill-rotation", "title": "Damage Combo", "notes": ["Repeat from the top."],
                  "steps": [
                    { "keys": "Hotkey", "skills": [{ "skillNo": 1937, "nameEn": "Flow: Intimidation", "nameEs": null }], "extra": "opener", "note": null },
                    { "keys": null, "skills": [{ "skillNo": 1932, "nameEn": "Moonlight Strike", "nameEs": null }], "extra": null, "note": "Dragonblood" }
                  ]
                }
              ],
              "notes": [
                { "section": "skill-rotation", "text": "Core DPS first.", "skills": [{ "skillNo": 1932, "nameEn": "Moonlight Strike", "nameEs": null }] }
              ],
              "addons": [
                { "section": "class-addons", "skill": { "skillNo": 1910, "nameEn": "Skybound Stab I", "nameEs": "Garuda I" },
                  "description": "Thrust.", "effects": [{ "label": "Speed", "value": "+10%" }] }
              ]
            }
            """;

        public const string SharedGuide = """
            { "schema": 1, "classType": 17, "classSlug": "shai", "spec": "talent", "title": "Shai Talent",
              "description": null, "source": "https://bdoclasshub.com/classes/shai/index.html",
              "chains": [], "notes": [], "addons": [] }
            """;
    }

    private static MemoryResources Bundle(params (string Name, string Text)[] extra)
    {
        var files = new Dictionary<string, string>
        {
            [BundleResourceNames.Catalog] = Data.Catalog,
            [BundleResourceNames.Skills] = Data.Skills,
            [BundleResourceNames.Guide("tamer.awakening")] = Data.Guide,
            [BundleResourceNames.Guide("shai.talent")] = Data.SharedGuide,
        };
        foreach (var (name, text) in extra)
        {
            files[name] = text;
        }

        return new MemoryResources(files);
    }

    private JsonCombosCatalog Load(IEmbeddedResources resources, IAssetProvider? assets = null) =>
        JsonCombosCatalog.Load(resources, assets ?? new RecordingAssets(), _log.Logger);

    private string LogText() => File.ReadAllText(_log.Logger.FilePath, Encoding.UTF8);

    [Fact]
    public void Classes_are_sorted_by_class_type_with_both_names_and_their_specs()
    {
        var catalog = Load(Bundle());

        Assert.Equal(new[] { 16, 17 }, catalog.Classes.Select(item => item.ClassType));
        var tamer = catalog.Classes[0];
        Assert.Equal("tamer", tamer.Slug);
        Assert.Equal("Tamer", tamer.Name.En);
        Assert.Equal("Domadora", tamer.Name.Es);
        Assert.Equal(new[] { "awakening", "missing" }, tamer.Specs.Select(spec => spec.Id));
        Assert.Equal("Despertar", tamer.Specs[0].DisplayName.Es);
        Assert.Equal("tamer.awakening", tamer.Specs[0].GuideKey);
    }

    [Fact]
    public void Missing_spanish_class_name_is_null_and_english_is_used_for_display()
    {
        var shai = Load(Bundle()).Classes.Single(item => item.Slug == "shai");

        Assert.Null(shai.Name.Es);
        Assert.Equal("Shai", shai.Name.For(CultureInfo.GetCultureInfo("es-MX")));
    }

    [Fact]
    public void Display_name_follows_the_culture_and_falls_back_to_english()
    {
        var tamer = Load(Bundle()).Classes[0].Name;

        Assert.Equal("Domadora", tamer.For(CultureInfo.GetCultureInfo("es-MX")));
        Assert.Equal("Tamer", tamer.For(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("Tamer", tamer.For(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Guide_maps_chains_steps_skills_notes_and_addons()
    {
        var catalog = Load(Bundle());

        var guide = await catalog.LoadGuideAsync(16, "awakening");

        Assert.NotNull(guide);
        Assert.Equal("Tamer Awakening", guide.Title);
        Assert.Equal("PvE Combo Guide", guide.Description);
        Assert.Equal("https://bdoclasshub.com/classes/tamer/awakening/index.html", guide.Source);

        var chain = Assert.Single(guide.Chains);
        Assert.Equal("skill-rotation", chain.Section);
        Assert.Equal("Damage Combo", chain.Title);
        Assert.Equal(new[] { "Repeat from the top." }, chain.Notes);
        Assert.Equal(2, chain.Steps.Count);
        Assert.Equal("Hotkey", chain.Steps[0].Keys);
        Assert.Equal("opener", chain.Steps[0].Extra);
        Assert.Null(chain.Steps[1].Keys);
        Assert.Equal("Dragonblood", chain.Steps[1].Note);

        var note = Assert.Single(guide.Notes);
        Assert.Equal("Core DPS first.", note.Text);
        Assert.Equal(1932, Assert.Single(note.Skills).SkillNo);

        var addon = Assert.Single(guide.Addons);
        Assert.Equal(1910, addon.Skill.SkillNo);
        Assert.Equal("Thrust.", addon.Description);
        Assert.Equal("+10%", Assert.Single(addon.Effects).Value);
    }

    [Fact]
    public async Task Skill_names_and_description_come_from_skills_json_when_the_guide_has_no_spanish_name()
    {
        var guide = await Load(Bundle()).LoadGuideAsync(16, "awakening");

        var skill = guide!.Chains[0].Steps[0].Skills[0];

        Assert.Equal(1937, skill.SkillNo);
        Assert.Equal("Flow: Intimidation", skill.Name.En);
        Assert.Equal("Flujo: Intimidación", skill.Name.Es);
        Assert.Equal("Proyecta el último ataque.", skill.DescriptionEs);
    }

    [Fact]
    public async Task Skill_without_a_description_has_none_and_keeps_its_guide_names()
    {
        var guide = await Load(Bundle()).LoadGuideAsync(16, "awakening");

        var skill = guide!.Chains[0].Steps[1].Skills[0];

        Assert.Null(skill.DescriptionEs);
        Assert.Equal("Golpe de la Luna III", skill.Name.Es);
    }

    [Fact]
    public async Task Skill_icon_is_requested_from_the_asset_provider_with_the_class_slug()
    {
        var assets = new RecordingAssets();
        var guide = await Load(Bundle(), assets).LoadGuideAsync(16, "awakening");

        using var stream = guide!.Chains[0].Steps[0].Skills[0].OpenIcon();

        Assert.NotNull(stream);
        Assert.Equal((1937, "tamer"), assets.SkillRequests.Single());
    }

    [Fact]
    public async Task Specs_that_share_a_guide_get_the_same_guide_object()
    {
        var catalog = Load(Bundle());

        var azminek = await catalog.LoadGuideAsync(17, "azminek");
        var talent = await catalog.LoadGuideAsync(17, "talent");

        Assert.NotNull(azminek);
        Assert.Same(azminek, talent);
        Assert.Equal("Shai Talent", azminek.Title);
    }

    [Fact]
    public async Task Guide_is_cached_after_the_first_load()
    {
        var catalog = Load(Bundle());

        var first = await catalog.LoadGuideAsync(16, "awakening");
        var second = await catalog.LoadGuideAsync(16, "awakening");

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Unknown_spec_returns_null_and_is_logged()
    {
        var guide = await Load(Bundle()).LoadGuideAsync(16, "succession");

        Assert.Null(guide);
        Assert.Contains("\"event\":\"combo.guide.unknown_spec\"", LogText());
    }

    [Fact]
    public async Task Spec_whose_guide_file_is_missing_returns_null_and_is_logged()
    {
        var guide = await Load(Bundle()).LoadGuideAsync(16, "missing");

        Assert.Null(guide);
        Assert.Contains("\"event\":\"combo.guide.missing\"", LogText());
    }

    [Fact]
    public async Task Invalid_guide_json_returns_null_and_logs_the_error_with_its_stack()
    {
        var resources = Bundle((BundleResourceNames.Guide("tamer.awakening"), "{ not json"));

        var guide = await Load(resources).LoadGuideAsync(16, "awakening");

        Assert.Null(guide);
        var text = LogText();
        Assert.Contains("\"event\":\"combo.guide.invalid\"", text);
        Assert.Contains("\"stack\":", text);
    }

    [Fact]
    public async Task Guide_with_another_schema_is_refused()
    {
        var resources = Bundle((BundleResourceNames.Guide("tamer.awakening"), Data.Guide.Replace("\"schema\": 1", "\"schema\": 2")));

        var guide = await Load(resources).LoadGuideAsync(16, "awakening");

        Assert.Null(guide);
        Assert.Contains("\"event\":\"combo.guide.schema\"", LogText());
    }

    [Fact]
    public void Missing_catalog_gives_no_classes_and_is_logged()
    {
        var catalog = Load(new MemoryResources(new Dictionary<string, string>()));

        Assert.Empty(catalog.Classes);
        Assert.Contains("\"event\":\"combo.catalog.missing\"", LogText());
    }

    [Fact]
    public void Invalid_catalog_json_gives_no_classes_and_logs_the_error()
    {
        var resources = new MemoryResources(new Dictionary<string, string> { [BundleResourceNames.Catalog] = "[1, 2" });

        var catalog = Load(resources);

        Assert.Empty(catalog.Classes);
        Assert.Contains("\"event\":\"combo.catalog.invalid\"", LogText());
    }

    [Fact]
    public void Skills_file_with_a_key_that_is_not_a_number_is_logged_and_skipped()
    {
        var resources = Bundle((BundleResourceNames.Skills, """{ "abc": { "nameEn": "X" }, "1937": { "nameEn": "Y" } }"""));

        var catalog = Load(resources);

        Assert.Contains("\"event\":\"combo.skills.bad_key\"", LogText());
        Assert.Equal(2, catalog.Classes.Count);
    }

    [Fact]
    public void Catalog_load_is_logged_with_its_counts()
    {
        Load(Bundle());

        Assert.Contains("\"classes\":2", LogText());
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => JsonCombosCatalog.Load(null!, new RecordingAssets(), _log.Logger));
        Assert.Throws<ArgumentNullException>(() => JsonCombosCatalog.Load(Bundle(), null!, _log.Logger));
    }

    [Fact]
    public void Embedded_bundle_fixture_loads_through_the_assembly()
    {
        var resources = new AssemblyEmbeddedResources(Assembly.GetExecutingAssembly());

        var catalog = JsonCombosCatalog.Load(resources, new RecordingAssets(), _log.Logger);

        Assert.Equal(new[] { "tamer", "shai" }, catalog.Classes.Select(item => item.Slug));
    }

    [Fact]
    public async Task Embedded_guide_fixture_loads_through_the_assembly()
    {
        var resources = new AssemblyEmbeddedResources(Assembly.GetExecutingAssembly());
        var catalog = JsonCombosCatalog.Load(resources, new RecordingAssets(), _log.Logger);

        var guide = await catalog.LoadGuideAsync(16, "awakening");

        Assert.NotNull(guide);
        Assert.Equal("Tamer Awakening", guide.Title);
        Assert.Equal(1937, guide.Chains[0].Steps[0].Skills[0].SkillNo);
    }

    [Fact]
    public void Assembly_resources_return_null_for_an_unknown_name()
    {
        var resources = new AssemblyEmbeddedResources(Assembly.GetExecutingAssembly());

        Assert.Null(resources.Open("skill-424242.png"));
    }
}
