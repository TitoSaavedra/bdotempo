using System.Text;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.GameData;

namespace BdoComboSetup.Tests;

/// <summary>
/// The character selector: live characters, names and classes, read from synthetic folders only (never the game's).
/// </summary>
public sealed class CharacterRosterTests : IDisposable
{
    private const string Account = "100000001";
    private const string PcidA = "20000000000000001";
    private const string PcidB = "20000000000000002";
    private const string PcidC = "20000000000000003";
    private const string PcidD = "20000000000000004";

    /// <summary>Synthetic skill table: 100 and 101 belong to class 16, 200 to class 0, 300 to classes 0 and 16.</summary>
    private const string TableJson = """
        {"maxClassesPerGroup": 9, "skills": {"100": [16], "101": [16], "200": [0], "300": [0, 16]}}
        """;

    private readonly TempLogger _log = new();
    private readonly SkillClassTable _table = SkillClassTable.Parse(TableJson);

    public void Dispose() => _log.Dispose();

    private string Root => _log.Directory;

    private string AccountFolder => Path.Combine(Root, "UserCache", Account);

    private CharacterRoster Roster(GameFileReader? reader = null, CharacterOverrideStore? overrides = null) => new(
        new GameDataLocator(Root, Account, _log.Logger),
        overrides ?? new CharacterOverrideStore(Path.Combine(Root, "characters.json"), _log.Logger),
        reader ?? new GameFileReader(_log.Logger, retryDelay: TimeSpan.Zero),
        _table,
        FakeCombosCatalog.Sample(),
        new KeyLocalizer(),
        System.Globalization.CultureInfo.InvariantCulture,
        _log.Logger);

    /// <summary>The account file: a CharacterOrderList line per pcid, with its order.</summary>
    private void WriteOrder(params (string Pcid, int Order)[] entries)
    {
        var text = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Root>\r\n<CharacterOrderList Version=\"1\"/>\r\n");
        foreach (var (pcid, order) in entries)
        {
            text.Append($"<CharacterOrderList CharacterNo=\"{pcid}\" Order=\"{order}\"/>\r\n");
        }

        text.Append("</Root>\r\n");
        Directory.CreateDirectory(AccountFolder);
        File.WriteAllText(Path.Combine(AccountFolder, "gamevariable.xml"), text.ToString());
    }

    private void WriteLabels(params (string Pcid, string Label)[] labels)
    {
        var text = new StringBuilder("<Root>\r\n<CharacterMemo Version=\"1\"/>\r\n");
        foreach (var (pcid, label) in labels)
        {
            text.Append($"<CharacterMemo CharacterNo=\"{pcid}\" Memo=\"{label}\"/>\r\n");
        }

        text.Append("</Root>\r\n");
        File.WriteAllText(Path.Combine(AccountFolder, "gameVariable_lobby.xml"), text.ToString());
    }

    /// <summary>A character file whose cooldown bar holds the given skill numbers.</summary>
    private void WriteCharacter(string pcid, params int[] skills)
    {
        var folder = Path.Combine(AccountFolder, "11", pcid);
        Directory.CreateDirectory(folder);
        var text = new StringBuilder("<SkillCoolTimeSlot Version=\"2\">\r\n");
        for (var slot = 0; slot < 20; slot++)
        {
            var skill = slot < skills.Length ? skills[slot] : 0;
            text.Append($"\t<SkillCoolTimeSlot SlotNo=\"{slot}\" SkillNo=\"{skill}\"/>\r\n");
        }

        text.Append("</SkillCoolTimeSlot>\r\n");
        File.WriteAllText(Path.Combine(folder, "gameVariable.xml"), text.ToString());
    }

    [Fact]
    public void Live_characters_are_the_order_list_intersected_with_the_folders_in_game_order()
    {
        WriteOrder((PcidA, 1), (PcidB, 0), (PcidC, 2), (PcidD, 3));
        WriteCharacter(PcidA, 100);
        WriteCharacter(PcidB, 200);
        // C has no folder; D has a folder without gameVariable.xml.
        Directory.CreateDirectory(Path.Combine(AccountFolder, "11", PcidD));
        // A stale folder and a non-17-digit folder are not live.
        WriteCharacter("1234", 100);
        WriteCharacter("20000000000000099", 100);
        // A sidecar file is not read; its order entries do not count.
        File.WriteAllText(Path.Combine(AccountFolder, "gamevariable-MSI.xml"), "<CharacterOrderList CharacterNo=\"20000000000000005\" Order=\"0\"/>");

        var entries = Roster().Load(Account);

        Assert.Equal(new[] { PcidB, PcidA }, entries.Select(entry => entry.Pcid));
        Assert.Equal(new[] { 0, 1 }, entries.Select(entry => entry.Order));
    }

    [Fact]
    public void The_name_is_the_user_name_first_then_the_in_game_label_then_the_fallback()
    {
        WriteOrder((PcidA, 0), (PcidB, 1), (PcidC, 2));
        WriteLabels((PcidB, "Label B"));
        WriteCharacter(PcidA);
        WriteCharacter(PcidB);
        WriteCharacter(PcidC);
        var overrides = new CharacterOverrideStore(Path.Combine(Root, "characters.json"), _log.Logger);
        overrides.Set(Account, PcidA, new CharacterOverride("My name", null));
        overrides.Set(Account, PcidB, new CharacterOverride("My B", null));

        var entries = Roster(overrides: overrides).Load(Account);

        Assert.Equal(("My name", NameSource.Custom), (entries[0].Name, entries[0].NameSource));
        Assert.Equal(("My B", NameSource.Custom), (entries[1].Name, entries[1].NameSource));
        Assert.Equal(NameSource.Fallback, entries[2].NameSource);
        // The fallback number counts every character of the same class, named or not: C is the third unknown.
        Assert.Equal("Characters.UnknownFallback(3)", entries[2].Name);
    }

    [Fact]
    public void Without_a_user_name_the_in_game_label_is_used()
    {
        WriteOrder((PcidB, 0));
        WriteLabels((PcidB, "Label &amp; B"));
        WriteCharacter(PcidB);

        var entry = Roster().Load(Account).Single();

        Assert.Equal(("Label & B", NameSource.Label), (entry.Name, entry.NameSource));
    }

    [Fact]
    public void The_class_is_the_user_class_first_then_the_vote_then_unknown()
    {
        WriteOrder((PcidA, 0), (PcidB, 1), (PcidC, 2), (PcidD, 3));
        WriteCharacter(PcidA, 100, 101);
        WriteCharacter(PcidB, 200);
        WriteCharacter(PcidC, 0);
        // A tie between class 0 and class 16 (300 is shared by both, 100 is class 16 and 200 class 0).
        WriteCharacter(PcidD, 100, 200);
        var overrides = new CharacterOverrideStore(Path.Combine(Root, "characters.json"), _log.Logger);
        overrides.Set(Account, PcidA, new CharacterOverride(null, 0));

        var entries = Roster(overrides: overrides).Load(Account);

        Assert.Equal(0, entries[0].ClassType);
        Assert.Equal(ClassSource.Custom, entries[0].ClassSource);
        Assert.Equal(0, entries[1].ClassType);
        Assert.Equal(ClassSource.Inferred, entries[1].ClassSource);
        Assert.Null(entries[2].ClassType);
        Assert.Equal(ClassSource.Unknown, entries[2].ClassSource);
        Assert.Null(entries[3].ClassType);
        Assert.Equal(ClassSource.Unknown, entries[3].ClassSource);
    }

    [Fact]
    public void A_name_and_class_saved_once_are_remembered_and_clearing_them_restores_the_game_values()
    {
        WriteOrder((PcidA, 0));
        WriteCharacter(PcidA, 100);
        var roster = Roster();

        roster.SaveOverride(Account, PcidA, "Saved", 0);
        var saved = roster.Load(Account).Single();
        roster.SaveOverride(Account, PcidA, null, null);
        var cleared = roster.Load(Account).Single();

        Assert.Equal("Saved", saved.Name);
        Assert.Equal(NameSource.Custom, saved.NameSource);
        Assert.Equal(0, saved.ClassType);
        Assert.Equal(ClassSource.Custom, saved.ClassSource);
        Assert.Equal(NameSource.Fallback, cleared.NameSource);
        Assert.Equal(16, cleared.ClassType);
        Assert.Equal(ClassSource.Inferred, cleared.ClassSource);
    }

    [Fact]
    public void A_locked_order_file_is_retried_and_read_on_the_last_try()
    {
        WriteOrder((PcidB, 0));
        WriteCharacter(PcidB);
        var failures = 2;
        var reader = new GameFileReader(_log.Logger, path =>
        {
            if (failures-- > 0)
            {
                throw new IOException("the game is writing the file");
            }

            return File.ReadAllText(path);
        }, retryDelay: TimeSpan.Zero);

        var entries = Roster(reader).Load(Account);

        Assert.Equal(new[] { PcidB }, entries.Select(entry => entry.Pcid));
    }

    [Fact]
    public void A_file_that_stays_locked_gives_no_characters_and_no_exception()
    {
        WriteOrder((PcidB, 0));
        WriteCharacter(PcidB);
        var reader = new GameFileReader(_log.Logger, _ => throw new IOException("locked"), retryDelay: TimeSpan.Zero);

        var entries = Roster(reader).Load(Account);

        Assert.Empty(entries);
    }

    [Fact]
    public void The_active_account_is_the_chosen_one_or_the_only_one_and_none_when_neither_exists()
    {
        WriteOrder((PcidB, 0));
        WriteCharacter(PcidB);
        Assert.Equal(Account, Roster().ActiveAccount());

        var missing = new CharacterRoster(
            new GameDataLocator(Root, "100000009", _log.Logger),
            new CharacterOverrideStore(Path.Combine(Root, "characters.json"), _log.Logger),
            new GameFileReader(_log.Logger, retryDelay: TimeSpan.Zero),
            _table,
            FakeCombosCatalog.Sample(),
            new KeyLocalizer(),
            System.Globalization.CultureInfo.InvariantCulture,
            _log.Logger);
        Assert.Null(missing.ActiveAccount());
    }
}
