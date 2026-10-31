using BdoComboSetup.Core.Icons;

namespace BdoComboSetup.Tests;

public sealed class IconCatalogTests : IDisposable
{
    private readonly TempLogger _log = new();
    private readonly string _primary;
    private readonly string _fallback;

    public IconCatalogTests()
    {
        _primary = Path.Combine(_log.Directory, "output");
        _fallback = Path.Combine(_log.Directory, "classhub");
        Directory.CreateDirectory(_primary);
        Directory.CreateDirectory(_fallback);

        // Synthetic catalog: two skills share one file (as in the export), one points to a missing file, one class icon.
        File.WriteAllText(Path.Combine(_primary, "skills.json"), """
            {
              "1937": { "level": 1, "class_id": "16", "class_table_name": "Tamer", "icon_path": "icons/skills/Tamer/pbw_skill_1937.webp", "status": "DOWNLOAD_OK" },
              "1926": { "level": 1, "class_id": "16", "class_table_name": "Tamer", "icon_path": "icons/skills/Tamer/pbw_skill_1923.webp", "status": "DOWNLOAD_OK" },
              "1923": { "level": 1, "class_id": "16", "class_table_name": "Tamer", "icon_path": "icons/skills/Tamer/pbw_skill_1923.webp", "status": "DOWNLOAD_OK" },
              "5000": { "level": 1, "class_id": "16", "class_table_name": "Tamer", "icon_path": "icons/skills/Tamer/missing.webp", "status": "DOWNLOAD_OK" }
            }
            """);
        File.WriteAllText(Path.Combine(_primary, "classes.json"), """
            {
              "16": { "icon_path": "icons/classes/Tamer/class_16.webp", "status": "DOWNLOAD_OK" }
            }
            """);
        CreateFile(Path.Combine(_primary, "icons", "skills", "Tamer", "pbw_skill_1937.webp"));
        CreateFile(Path.Combine(_primary, "icons", "skills", "Tamer", "pbw_skill_1923.webp"));
        CreateFile(Path.Combine(_primary, "icons", "classes", "Tamer", "class_16.webp"));

        // Fallback: bdoclasshub naming, <skillNo>_<name>.webp under classes\<class>\skills\icons.
        var classHubIcons = Path.Combine(_fallback, "classes", "tamer", "skills", "icons");
        Directory.CreateDirectory(classHubIcons);
        CreateFile(Path.Combine(classHubIcons, "1347_summon_heuklang.webp"));
        CreateFile(Path.Combine(classHubIcons, "231_absorb.webp"));
    }

    public void Dispose() => _log.Dispose();

    private IconCatalog Load() => IconCatalog.Load(_primary, _fallback, _log.Logger);

    [Fact]
    public void Skill_icon_is_resolved_through_skills_json_not_the_file_number()
    {
        var catalog = Load();

        // 1926 points to pbw_skill_1923.webp: the file number is not the skill number.
        Assert.Equal(Path.Combine(_primary, "icons", "skills", "Tamer", "pbw_skill_1923.webp"), catalog.GetSkillIcon(1926, "Tamer"));
        Assert.Equal(Path.Combine(_primary, "icons", "skills", "Tamer", "pbw_skill_1937.webp"), catalog.GetSkillIcon(1937, "Tamer"));
    }

    [Fact]
    public void Entries_are_counted_when_the_file_is_read()
    {
        var catalog = Load();

        Assert.Equal(4, catalog.SkillEntryCount);
        Assert.Equal(1, catalog.ClassEntryCount);
    }

    [Fact]
    public void Skill_not_in_the_primary_catalog_uses_the_classhub_folder()
    {
        var catalog = Load();

        Assert.Equal(Path.Combine(_fallback, "classes", "tamer", "skills", "icons", "1347_summon_heuklang.webp"), catalog.GetSkillIcon(1347, "Tamer"));
        Assert.Equal(Path.Combine(_fallback, "classes", "tamer", "skills", "icons", "231_absorb.webp"), catalog.GetSkillIcon(231, "Tamer"));
    }

    [Fact]
    public void Entry_pointing_to_a_missing_file_falls_back_and_then_returns_null()
    {
        var catalog = Load();

        Assert.Null(catalog.GetSkillIcon(5000, "Tamer"));
    }

    [Fact]
    public void Unknown_skill_without_class_name_returns_null()
    {
        var catalog = Load();

        Assert.Null(catalog.GetSkillIcon(999999));
        Assert.Null(catalog.GetSkillIcon(1347, className: null));
    }

    [Fact]
    public void Class_icon_is_resolved_through_classes_json()
    {
        var catalog = Load();

        Assert.Equal(Path.Combine(_primary, "icons", "classes", "Tamer", "class_16.webp"), catalog.GetClassIcon(16));
        Assert.Null(catalog.GetClassIcon(99));
    }

    [Fact]
    public void Invalid_json_is_logged_and_the_fallback_still_works()
    {
        File.WriteAllText(Path.Combine(_primary, "skills.json"), "{ not json");

        var catalog = Load();

        Assert.Equal(0, catalog.SkillEntryCount);
        Assert.Equal(Path.Combine(_fallback, "classes", "tamer", "skills", "icons", "231_absorb.webp"), catalog.GetSkillIcon(231, "Tamer"));
    }

    [Fact]
    public void Missing_roots_do_not_throw()
    {
        var catalog = IconCatalog.Load(Path.Combine(_log.Directory, "nope"), Path.Combine(_log.Directory, "nada"), _log.Logger);

        Assert.Equal(0, catalog.SkillEntryCount);
        Assert.Null(catalog.GetSkillIcon(1937, "Tamer"));
    }

    private static void CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46]);
    }
}
