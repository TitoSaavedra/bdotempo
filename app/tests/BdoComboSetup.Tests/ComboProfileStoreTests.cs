using System.Text;
using BdoComboSetup.Core.Combo;

namespace BdoComboSetup.Tests;

/// <summary>The profile files: round trip, one file per class and spec, and what is refused.</summary>
public sealed class ComboProfileStoreTests : IDisposable
{
    private readonly TempLogger _log = new();
    private readonly ApplyTestHelpers.ProfileFolder _folder;

    public ComboProfileStoreTests()
    {
        _folder = new ApplyTestHelpers.ProfileFolder(_log.Logger);
    }

    public void Dispose()
    {
        _folder.Dispose();
        _log.Dispose();
    }

    private ComboProfileStore Store => _folder.Store;

    private static ComboProfile Sample() => new(
        16,
        "awakening",
        [
            new ComboProfileChain(0,
            [
                new ComboProfileStep(1, null, "Intim.", "R 1", 1, 7),
                new ComboProfileStep(null, 500, null, null, 20, null),
            ]),
            new ComboProfileChain(2, []),
        ]);

    private static IReadOnlyList<string> Lines(ComboProfile profile) =>
        profile.Chains.SelectMany(chain => new[] { $"chain {chain.Chain}" }.Concat(chain.Steps.Select(step => string.Join("|",
            step.BaseStep, step.SkillNo, step.CustomName, step.Memo, step.CooldownSlot, step.QuickPosition)))).ToList();

    private string LogText() => File.ReadAllText(_log.Logger.FilePath, Encoding.UTF8);

    [Fact]
    public async Task A_saved_profile_loads_back_with_the_same_steps()
    {
        await Store.SaveAsync(Sample());

        var loaded = await Store.LoadAsync(16, "awakening");

        Assert.NotNull(loaded);
        Assert.Equal(Lines(Sample()), Lines(loaded));
        Assert.Equal(16, loaded.ClassType);
        Assert.Equal("awakening", loaded.SpecId);
    }

    [Fact]
    public async Task Each_class_and_spec_has_its_own_file_named_by_class_type_and_spec()
    {
        await Store.SaveAsync(Sample());

        Assert.EndsWith("16.awakening.json", Store.PathFor(16, "awakening"), StringComparison.Ordinal);
        Assert.True(File.Exists(Store.PathFor(16, "awakening")));
        Assert.Null(await Store.LoadAsync(16, "succession"));
    }

    [Fact]
    public async Task Saving_again_replaces_the_file_and_leaves_no_temporary_file()
    {
        await Store.SaveAsync(Sample());
        var changed = Sample() with { Chains = [new ComboProfileChain(0, [new ComboProfileStep(0, null, null, "new", null, null)])] };

        await Store.SaveAsync(changed);

        var loaded = await Store.LoadAsync(16, "awakening");
        Assert.Equal(Lines(changed), Lines(loaded!));
        Assert.False(File.Exists(Store.PathFor(16, "awakening") + ".tmp"));
    }

    [Fact]
    public async Task Saving_creates_the_folder_when_it_does_not_exist()
    {
        Assert.False(Directory.Exists(_folder.Directory));

        await Store.SaveAsync(Sample());

        Assert.True(File.Exists(Store.PathFor(16, "awakening")));
    }

    [Fact]
    public async Task Loading_a_missing_profile_gives_null_without_an_error()
    {
        Assert.Null(await Store.LoadAsync(99, "awakening"));
        Assert.DoesNotContain("\"level\":\"error\"", LogText());
    }

    [Fact]
    public async Task Delete_removes_the_file_and_reports_whether_there_was_one()
    {
        await Store.SaveAsync(Sample());

        Assert.True(Store.Delete(16, "awakening"));
        Assert.False(Store.Delete(16, "awakening"));
        Assert.Null(await Store.LoadAsync(16, "awakening"));
    }

    [Theory]
    [InlineData("Bad Spec")]
    [InlineData("../escape")]
    [InlineData("UPPER")]
    [InlineData("")]
    public void Spec_ids_that_are_not_plain_names_are_refused_because_they_become_file_names(string specId)
    {
        Assert.ThrowsAny<ArgumentException>(() => Store.PathFor(16, specId));
    }

    [Fact]
    public async Task A_profile_with_another_schema_is_ignored_and_logged()
    {
        Directory.CreateDirectory(_folder.Directory);
        await File.WriteAllTextAsync(Store.PathFor(16, "awakening"), """{ "schema": 2, "classType": 16, "spec": "awakening", "chains": [] }""");

        Assert.Null(await Store.LoadAsync(16, "awakening"));
        Assert.Contains("\"event\":\"combo.profile.schema\"", LogText());
    }

    [Fact]
    public async Task A_profile_of_another_guide_is_ignored_and_logged()
    {
        Directory.CreateDirectory(_folder.Directory);
        await File.WriteAllTextAsync(Store.PathFor(16, "awakening"), """{ "schema": 1, "classType": 17, "spec": "awakening", "chains": [] }""");

        Assert.Null(await Store.LoadAsync(16, "awakening"));
        Assert.Contains("\"event\":\"combo.profile.mismatch\"", LogText());
    }

    [Fact]
    public async Task A_damaged_profile_is_ignored_and_logged_as_an_error()
    {
        Directory.CreateDirectory(_folder.Directory);
        await File.WriteAllTextAsync(Store.PathFor(16, "awakening"), "{ not json");

        Assert.Null(await Store.LoadAsync(16, "awakening"));
        var logText = LogText();
        Assert.Contains("\"event\":\"combo.profile.invalid\"", logText);
        Assert.Contains("\"level\":\"error\"", logText);
    }
}
