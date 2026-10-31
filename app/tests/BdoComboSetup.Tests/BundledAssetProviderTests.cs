using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Assets.Pack;

namespace BdoComboSetup.Tests;

/// <summary>The icon provider over an in-memory pack (the fake of the real pack): entries by name, no loose files.</summary>
public sealed class BundledAssetProviderTests : IDisposable
{
    private static readonly byte[] SkillBytes = [0x89, 0x50, 0x4E, 0x47, 0xEE];
    private static readonly byte[] ClassBytes = [0x89, 0x50, 0x4E, 0x47, 0x02];

    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private BundledAssetProvider Provider(params (string Key, byte[] Bytes)[] entries)
    {
        var dictionary = entries.ToDictionary(entry => entry.Key, entry => entry.Bytes, StringComparer.Ordinal);
        return new BundledAssetProvider(new AssetPackResources(new InMemoryAssetPack(dictionary)), _log.Logger);
    }

    [Fact]
    public void A_skill_icon_is_read_from_the_pack_by_its_skill_number()
    {
        var provider = Provider(("skill-1926.png", SkillBytes));

        using var stream = provider.OpenSkillIcon(1926);

        Assert.Equal(SkillBytes, ReadAll(stream));
    }

    [Fact]
    public void A_class_icon_is_read_from_the_pack_by_its_class_type()
    {
        var provider = Provider(("class-16.png", ClassBytes));

        using var stream = provider.OpenClassIcon(16);

        Assert.Equal(ClassBytes, ReadAll(stream));
    }

    [Fact]
    public void The_class_slug_does_not_change_the_answer()
    {
        var provider = Provider(("skill-1347.png", SkillBytes));

        using var withClass = provider.OpenSkillIcon(1347, "tamer");
        using var withoutClass = provider.OpenSkillIcon(1347);

        Assert.Equal(ReadAll(withoutClass), ReadAll(withClass));
    }

    [Fact]
    public void A_missing_icon_is_null_so_the_caller_draws_a_glyph()
    {
        var provider = Provider(("skill-1926.png", SkillBytes));

        Assert.Null(provider.OpenSkillIcon(999999, "tamer"));
        Assert.Null(provider.OpenClassIcon(99));
    }

    [Fact]
    public void Each_open_gives_a_new_stream()
    {
        var provider = Provider(("skill-1926.png", SkillBytes));

        using var first = provider.OpenSkillIcon(1926);
        using var second = provider.OpenSkillIcon(1926);

        Assert.NotSame(first, second);
        Assert.Equal(SkillBytes, ReadAll(second));
    }

    [Fact]
    public void Constructor_arguments_are_required()
    {
        Assert.Throws<ArgumentNullException>(() => new BundledAssetProvider(null!, _log.Logger));
        Assert.Throws<ArgumentNullException>(() => new BundledAssetProvider(new AssetPackResources(InMemoryAssetPack.Empty), null!));
    }

    private static byte[] ReadAll(Stream? stream)
    {
        Assert.NotNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
