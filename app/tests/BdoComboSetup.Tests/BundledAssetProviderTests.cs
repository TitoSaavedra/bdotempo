using System.Text;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Icons;

namespace BdoComboSetup.Tests;

public sealed class BundledAssetProviderTests : IDisposable
{
    private static readonly byte[] EmbeddedSkill = [0x89, 0x50, 0x4E, 0x47, 0xEE];
    private static readonly byte[] FileSkill = [0x89, 0x50, 0x4E, 0x47, 0x01];
    private static readonly byte[] FileClass = [0x89, 0x50, 0x4E, 0x47, 0x02];

    private readonly TempLogger _log = new();
    private readonly string _primary;
    private readonly string _fallback;

    public BundledAssetProviderTests()
    {
        _primary = Path.Combine(_log.Directory, "output");
        _fallback = Path.Combine(_log.Directory, "classhub");
        Directory.CreateDirectory(_primary);
        Directory.CreateDirectory(_fallback);

        // Scrappers-style catalog: skill 1926 and class 16 have files; the file numbers differ from the skill numbers.
        File.WriteAllText(Path.Combine(_primary, "skills.json"), """
            { "1926": { "icon_path": "icons/skills/pbw_skill_1923.webp" } }
            """, Encoding.UTF8);
        File.WriteAllText(Path.Combine(_primary, "classes.json"), """
            { "16": { "icon_path": "icons/classes/class_16.webp" } }
            """, Encoding.UTF8);
        WriteFile(Path.Combine(_primary, "icons", "skills", "pbw_skill_1923.webp"), FileSkill);
        WriteFile(Path.Combine(_primary, "icons", "classes", "class_16.webp"), FileClass);

        // bdoclasshub folder: <skillNo>_<name>.webp under classes\<class>\skills\icons.
        WriteFile(Path.Combine(_fallback, "classes", "tamer", "skills", "icons", "1347_summon_heuklang.webp"), FileSkill);
    }

    public void Dispose() => _log.Dispose();

    private static void WriteFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private BundledAssetProvider Provider(byte[]? embeddedSkill = null, byte[]? embeddedClass = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (embeddedSkill is not null)
        {
            files[BundleResourceNames.SkillIcon(1926)] = embeddedSkill;
        }

        if (embeddedClass is not null)
        {
            files[BundleResourceNames.ClassIcon(16)] = embeddedClass;
        }

        var icons = IconCatalog.Load(_primary, _fallback, _log.Logger);
        return new BundledAssetProvider(new BytesResources(files), icons, _log.Logger);
    }

    private string LogText() => File.ReadAllText(_log.Logger.FilePath, Encoding.UTF8);

    [Fact]
    public void Embedded_skill_icon_is_used_before_the_file_catalog()
    {
        var provider = Provider(embeddedSkill: EmbeddedSkill);

        using var stream = provider.OpenSkillIcon(1926, "tamer");

        Assert.NotNull(stream);
        Assert.Equal(EmbeddedSkill, ReadAll(stream));
    }

    [Fact]
    public void Skill_icon_comes_from_the_file_catalog_when_it_is_not_embedded()
    {
        var provider = Provider();

        using var stream = provider.OpenSkillIcon(1926, "tamer");

        Assert.NotNull(stream);
        Assert.Equal(FileSkill, ReadAll(stream));
    }

    [Fact]
    public void Skill_icon_from_the_bdoclasshub_folder_needs_the_class_slug()
    {
        var provider = Provider();

        using var withClass = provider.OpenSkillIcon(1347, "tamer");
        using var withoutClass = provider.OpenSkillIcon(1347);

        Assert.NotNull(withClass);
        Assert.Null(withoutClass);
    }

    [Fact]
    public void Skill_with_no_icon_anywhere_gives_null_and_logs_none()
    {
        var provider = Provider();

        Assert.Null(provider.OpenSkillIcon(999999, "tamer"));
        Assert.Contains("\"source\":\"none\"", LogText());
    }

    [Fact]
    public void Embedded_class_icon_is_used_before_the_file_catalog()
    {
        var provider = Provider(embeddedClass: EmbeddedSkill);

        using var stream = provider.OpenClassIcon(16);

        Assert.NotNull(stream);
        Assert.Equal(EmbeddedSkill, ReadAll(stream));
    }

    [Fact]
    public void Class_icon_comes_from_the_file_catalog_when_it_is_not_embedded()
    {
        var provider = Provider();

        using var stream = provider.OpenClassIcon(16);

        Assert.NotNull(stream);
        Assert.Equal(FileClass, ReadAll(stream));
        Assert.Null(provider.OpenClassIcon(99));
    }

    [Fact]
    public void Each_returned_stream_is_new_and_can_be_disposed_independently()
    {
        var provider = Provider(embeddedSkill: EmbeddedSkill);

        var first = provider.OpenSkillIcon(1926);
        var second = provider.OpenSkillIcon(1926);
        first!.Dispose();

        Assert.NotNull(second);
        Assert.Equal(EmbeddedSkill, ReadAll(second));
        second.Dispose();
    }

    [Fact]
    public void Null_logger_or_resources_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new BundledAssetProvider(null!, null, _log.Logger));
        Assert.Throws<ArgumentNullException>(() => new BundledAssetProvider(new BytesResources(new Dictionary<string, byte[]>()), null, null!));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Byte resources held in memory.</summary>
    private sealed class BytesResources(IReadOnlyDictionary<string, byte[]> files) : IEmbeddedResources
    {
        public Stream? Open(string name) => files.TryGetValue(name, out var bytes) ? new MemoryStream(bytes) : null;
    }
}
