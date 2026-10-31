using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.GameData;

namespace BdoComboSetup.Tests;

/// <summary>The class vote over the skill table. Synthetic table only.</summary>
public sealed class ClassInferenceTests
{
    private static readonly SkillClassTable Table = SkillClassTable.Parse(
        """{"skills": {"100": [16], "101": [16], "200": [0], "300": [0, 16]}}""");

    [Fact]
    public void The_class_with_the_most_known_skills_wins()
    {
        Assert.Equal(16, ClassInference.Infer([100, 101, 200], Table));
    }

    [Fact]
    public void A_skill_shared_by_several_classes_counts_for_less_than_a_single_class_skill()
    {
        // 300 gives half a vote to class 0 and to class 16; 200 gives one vote to class 0.
        Assert.Equal(0, ClassInference.Infer([300, 200], Table));
    }

    [Fact]
    public void A_tie_between_the_top_two_classes_is_unknown()
    {
        Assert.Null(ClassInference.Infer([100, 200], Table));
    }

    [Fact]
    public void Unknown_skills_and_empty_slots_give_no_class()
    {
        Assert.Null(ClassInference.Infer([0, 999], Table));
        Assert.Null(ClassInference.Infer([], Table));
    }

    [Fact]
    public void An_empty_table_knows_no_class()
    {
        Assert.Null(ClassInference.Infer([100], SkillClassTable.Empty));
    }
}

public sealed class SkillClassTableTests
{
    [Fact]
    public void Parses_the_skill_to_classes_map_and_answers_for_unknown_skills_with_nothing()
    {
        var table = SkillClassTable.Parse("""{"skills": {"7": [1, 2]}}""");

        Assert.Equal(1, table.Count);
        Assert.Equal(new[] { 1, 2 }, table.ClassesOf(7));
        Assert.Empty(table.ClassesOf(8));
    }

    [Fact]
    public void Skills_only_for_one_class_are_listed_in_skill_order()
    {
        var table = SkillClassTable.Parse("""{"skills": {"9": [3], "4": [3], "5": [3, 4], "6": [3]}}""");

        Assert.Equal(new[] { 4, 6 }, table.SkillsOnlyFor(3, 2));
    }

    [Fact]
    public void A_malformed_table_is_refused_with_a_format_error()
    {
        Assert.Throws<FormatException>(() => SkillClassTable.Parse("{ not json"));
        Assert.Throws<FormatException>(() => SkillClassTable.Parse("""{"other": {}}"""));
        Assert.Throws<FormatException>(() => SkillClassTable.Parse("""{"skills": {"x": [1]}}"""));
    }
}

public sealed class GameFileReaderTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    [Fact]
    public void A_missing_file_is_null_and_is_not_retried()
    {
        var calls = 0;
        var reader = new GameFileReader(_log.Logger, _ =>
        {
            calls++;
            throw new FileNotFoundException("missing");
        }, retryDelay: TimeSpan.Zero);

        Assert.Null(reader.ReadText(Path.Combine(_log.Directory, "missing.xml")));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void A_locked_file_is_retried_up_to_the_attempt_limit_then_skipped()
    {
        var calls = 0;
        var reader = new GameFileReader(_log.Logger, _ =>
        {
            calls++;
            throw new IOException("sharing violation");
        }, attempts: 3, retryDelay: TimeSpan.Zero);

        Assert.Null(reader.ReadText(Path.Combine(_log.Directory, "locked.xml")));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Zero_attempts_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameFileReader(_log.Logger, attempts: 0));
    }
}

public sealed class CharacterOverrideStoreTests : IDisposable
{
    private const string Account = "100000001";
    private const string Pcid = "20000000000000001";

    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private string Path_ => Path.Combine(_log.Directory, "characters.json");

    [Fact]
    public void Overrides_are_saved_per_account_and_survive_a_reload()
    {
        var store = new CharacterOverrideStore(Path_, _log.Logger);
        store.Set(Account, Pcid, new CharacterOverride("  Mine  ", 16));

        var reloaded = new CharacterOverrideStore(Path_, _log.Logger);

        Assert.Equal(new CharacterOverride("Mine", 16), reloaded.Get(Account, Pcid));
        Assert.Null(reloaded.Get("100000002", Pcid));
    }

    [Fact]
    public void A_name_is_cut_to_the_maximum_length_and_an_empty_name_is_not_kept()
    {
        var store = new CharacterOverrideStore(Path_, _log.Logger);
        store.Set(Account, Pcid, new CharacterOverride(new string('x', 60), null));
        Assert.Equal(CharacterOverrideStore.MaxNameLength, store.Get(Account, Pcid)!.Name!.Length);

        store.Set(Account, Pcid, new CharacterOverride("   ", null));
        Assert.Null(store.Get(Account, Pcid));
    }

    [Fact]
    public void Setting_nothing_removes_the_override()
    {
        var store = new CharacterOverrideStore(Path_, _log.Logger);
        store.Set(Account, Pcid, new CharacterOverride("Mine", null));

        store.Set(Account, Pcid, null);

        Assert.Null(store.Get(Account, Pcid));
    }

    [Fact]
    public void Account_and_character_keys_must_be_digits()
    {
        var store = new CharacterOverrideStore(Path_, _log.Logger);

        Assert.Throws<ArgumentException>(() => store.Set("-1", Pcid, new CharacterOverride("x", null)));
        Assert.Throws<ArgumentException>(() => store.Get(Account, "abc"));
    }

    [Fact]
    public void A_corrupt_file_is_moved_aside_and_the_store_starts_empty()
    {
        File.WriteAllText(Path_, "{ broken");

        var store = new CharacterOverrideStore(Path_, _log.Logger);

        Assert.Null(store.Get(Account, Pcid));
        Assert.True(File.Exists(Path_ + ".corrupt"));
        Assert.False(File.Exists(Path_));
    }
}
