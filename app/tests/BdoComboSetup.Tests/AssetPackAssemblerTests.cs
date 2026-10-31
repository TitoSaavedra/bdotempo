using BdoComboSetup.Core.Assets.Pack;

namespace BdoComboSetup.Tests;

/// <summary>
/// The assembler on the committed synthetic parts: the parity with the Python builder, and the refusals
/// (missing, wrong, from another set, a complete pack used as a part, tampered entries and index).
/// </summary>
public sealed class AssetPackAssemblerTests : IDisposable
{
    private const string PartOne = "assets.part01.bdoa";
    private const string PartTwo = "assets.part02.bdoa";
    private const string PartThree = "assets.part03.bdoa";
    private const string PartFour = "assets.part04.bdoa";

    private readonly TempLogger _log = new();
    private readonly string _work;

    public AssetPackAssemblerTests()
    {
        _work = Path.Combine(_log.Directory, "work");
        Directory.CreateDirectory(_work);
    }

    public void Dispose() => _log.Dispose();

    [Fact]
    public void Assembling_the_fixture_parts_gives_the_complete_fixture_byte_for_byte()
    {
        var parts = AssetPackFixtures.CopyParts(Path.Combine(_work, "ok"));
        var output = Path.Combine(_work, "out", "assets.bdoa");

        var result = AssetPackAssembler.Assemble(parts, output, _log.Logger);

        Assert.Equal(File.ReadAllBytes(AssetPackFixtures.Synthetic), File.ReadAllBytes(output));
        Assert.Equal(4, result.Parts);
        Assert.Equal(9, result.Entries);
        Assert.Equal(AssetPackFormat.DefaultSetId, result.SetId);
        Assert.Equal(Path.GetFullPath(output), result.Output);
        Assert.False(File.Exists(output + ".tmp"));
    }

    [Fact]
    public void The_assembled_pack_opens_with_the_runtime_reader_and_verifies()
    {
        var output = Path.Combine(_work, "assembled.bdoa");
        AssetPackAssembler.Assemble(AssetPackFixtures.Parts, output, _log.Logger);

        using var pack = AssetPack.Open(File.OpenRead(output), _log.Logger);

        Assert.Null(pack.Part);
        Assert.True(pack.Verify().IsValid);
    }

    [Fact]
    public void Assembling_replaces_an_existing_output_file()
    {
        var output = Path.Combine(_work, "assets.bdoa");
        File.WriteAllText(output, "old");

        AssetPackAssembler.Assemble(AssetPackFixtures.Parts, output, _log.Logger);

        Assert.Equal(File.ReadAllBytes(AssetPackFixtures.Synthetic), File.ReadAllBytes(output));
    }

    [Fact]
    public void A_missing_part_is_refused_and_no_output_is_written()
    {
        var parts = AssetPackFixtures.CopyParts(Path.Combine(_work, "missing"));
        File.Delete(Path.Combine(parts, PartThree));
        var output = Path.Combine(_work, "missing.bdoa");

        var error = Assert.Throws<AssetPackException>(() => AssetPackAssembler.Assemble(parts, output, _log.Logger));

        Assert.Equal(AssetPackErrorCodes.PartMissing, error.Code);
        Assert.Contains("3", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void A_folder_without_part_files_is_refused()
    {
        var empty = Path.Combine(_work, "empty");
        Directory.CreateDirectory(empty);

        var error = Assert.Throws<AssetPackException>(() => AssetPackAssembler.Assemble(empty, Path.Combine(_work, "x.bdoa"), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.PartMissing, error.Code);
    }

    [Fact]
    public void A_missing_parts_folder_is_refused()
    {
        var error = Assert.Throws<AssetPackException>(() =>
            AssetPackAssembler.Assemble(Path.Combine(_work, "nope"), Path.Combine(_work, "x.bdoa"), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.PartsDirMissing, error.Code);
    }

    [Fact]
    public void A_part_under_the_wrong_file_name_is_refused()
    {
        var parts = AssetPackFixtures.CopyParts(Path.Combine(_work, "renamed"));
        File.Copy(Path.Combine(parts, PartTwo), Path.Combine(parts, PartThree), overwrite: true);

        var error = Assert.Throws<AssetPackException>(() => AssetPackAssembler.Assemble(parts, Path.Combine(_work, "x.bdoa"), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.PartMismatch, error.Code);
    }

    [Fact]
    public void A_complete_pack_given_as_a_part_is_refused()
    {
        var parts = AssetPackFixtures.CopyParts(Path.Combine(_work, "complete"));
        File.Copy(AssetPackFixtures.Synthetic, Path.Combine(parts, PartOne), overwrite: true);

        var error = Assert.Throws<AssetPackException>(() => AssetPackAssembler.Assemble(parts, Path.Combine(_work, "x.bdoa"), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.NotAPart, error.Code);
    }

    [Fact]
    public void A_part_of_another_set_is_refused()
    {
        var parts = AssetPackFixtures.CopyParts(Path.Combine(_work, "other"));
        File.Copy(Path.Combine(AssetPackFixtures.OtherSet, PartOne), Path.Combine(parts, PartOne), overwrite: true);

        var error = Assert.Throws<AssetPackException>(() => AssetPackAssembler.Assemble(parts, Path.Combine(_work, "x.bdoa"), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.PartMismatch, error.Code);
        Assert.Contains("another set", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tampered_entry_in_a_part_is_refused_and_no_output_is_written()
    {
        var parts = AssetPackFixtures.CopyParts(Path.Combine(_work, "tamper-entry"));
        var path = Path.Combine(parts, PartTwo);
        var bytes = File.ReadAllBytes(path);
        bytes[AssetPackFormat.HeaderSize + 20] ^= 0x01;
        File.WriteAllBytes(path, bytes);
        var output = Path.Combine(_work, "tamper-entry.bdoa");

        var error = Assert.Throws<AssetPackException>(() => AssetPackAssembler.Assemble(parts, output, _log.Logger));

        Assert.Equal(AssetPackErrorCodes.EntryAuthentication, error.Code);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void A_tampered_index_in_a_part_is_refused()
    {
        var parts = AssetPackFixtures.CopyParts(Path.Combine(_work, "tamper-index"));
        var path = Path.Combine(parts, PartFour);
        var bytes = File.ReadAllBytes(path);
        bytes[^5] ^= 0x80;
        File.WriteAllBytes(path, bytes);

        var error = Assert.Throws<AssetPackException>(() => AssetPackAssembler.Assemble(parts, Path.Combine(_work, "x.bdoa"), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.IndexAuthentication, error.Code);
    }

    [Fact]
    public void A_damaged_part_file_that_is_not_a_pack_is_refused()
    {
        var parts = AssetPackFixtures.CopyParts(Path.Combine(_work, "garbage"));
        File.WriteAllText(Path.Combine(parts, PartOne), "not a pack");

        var error = Assert.Throws<AssetPackException>(() => AssetPackAssembler.Assemble(parts, Path.Combine(_work, "x.bdoa"), _log.Logger));

        Assert.Equal(AssetPackErrorCodes.Truncated, error.Code);
    }

    [Fact]
    public void A_wrong_key_refuses_the_parts()
    {
        var wrong = AssetPackFormat.DeriveKeys(new byte[32]);

        var error = Assert.Throws<AssetPackException>(() =>
            AssetPackAssembler.Assemble(AssetPackFixtures.Parts, Path.Combine(_work, "x.bdoa"), _log.Logger, wrong));

        Assert.Equal(AssetPackErrorCodes.IndexAuthentication, error.Code);
    }

    [Fact]
    public void The_part_file_names_follow_the_part_number()
    {
        Assert.Equal("assets.part01.bdoa", AssetPackAssembler.PartFile(1));
        Assert.Equal("assets.part17.bdoa", AssetPackAssembler.PartFile(17));
        Assert.Equal("assets.part100.bdoa", AssetPackAssembler.PartFile(100));
    }
}
