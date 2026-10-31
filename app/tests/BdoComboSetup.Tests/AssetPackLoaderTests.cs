using BdoComboSetup.Core.Assets.Pack;

namespace BdoComboSetup.Tests;

/// <summary>The run-time opening of assets.bdoa: present, missing, damaged. No fallback to other files.</summary>
public sealed class AssetPackLoaderTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    [Fact]
    public void A_valid_pack_opens_without_an_error_code()
    {
        var path = Path.Combine(_log.Directory, "assets.bdoa");
        File.Copy(AssetPackFixtures.Synthetic, path);

        var result = AssetPackLoader.LoadFile(path, _log.Logger);

        Assert.Null(result.ErrorCode);
        Assert.Equal(path, result.Path);
        Assert.Equal(9, result.Pack.Keys.Count);
    }

    [Fact]
    public void A_missing_pack_gives_the_missing_code_and_an_empty_pack()
    {
        var path = Path.Combine(_log.Directory, "assets.bdoa");

        var result = AssetPackLoader.LoadFile(path, _log.Logger);

        Assert.Equal(AssetPackErrorCodes.Missing, result.ErrorCode);
        Assert.Empty(result.Pack.Keys);
    }

    [Fact]
    public void A_damaged_pack_gives_its_error_code_and_an_empty_pack()
    {
        var path = Path.Combine(_log.Directory, "assets.bdoa");
        File.WriteAllText(path, "this is not a pack at all, it is just text padding to be longer than the header");

        var result = AssetPackLoader.LoadFile(path, _log.Logger);

        Assert.Equal(AssetPackErrorCodes.BadMagic, result.ErrorCode);
        Assert.Empty(result.Pack.Keys);
    }

    [Fact]
    public void A_part_next_to_the_app_is_not_accepted_as_the_pack()
    {
        var path = Path.Combine(_log.Directory, "assets.bdoa");
        File.Copy(Path.Combine(AssetPackFixtures.Parts, "assets.part02.bdoa"), path);

        var result = AssetPackLoader.LoadFile(path, _log.Logger);

        Assert.Equal(AssetPackErrorCodes.IsPart, result.ErrorCode);
    }

    [Fact]
    public void The_default_path_is_the_pack_next_to_the_exe()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "assets.bdoa"), AssetPackLoader.DefaultPath);
    }
}
