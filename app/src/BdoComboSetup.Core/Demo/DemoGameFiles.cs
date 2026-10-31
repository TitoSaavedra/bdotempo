using System.Globalization;
using System.Text;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Demo;

/// <summary>
/// Writes a synthetic Black Desert data folder for the demo mode: two accounts, fake character names, skill numbers from the
/// bundled skill table, and the folders that the roster must ignore ("-1", a non-numeric folder, a stale folder, a sidecar
/// file). Only the demo folder is written; no game file is read or changed. The layout matches the real one, so the demo
/// runs the same code as the app.
/// </summary>
public static class DemoGameFiles
{
    public const string AccountMain = "100000001";
    public const string AccountAlt = "100000002";

    public const string PcidTamer = "10000000000000011";
    public const string PcidWarrior = "10000000000000012";
    public const string PcidShai = "10000000000000013";
    public const string PcidUnnamed = "10000000000000014";
    public const string PcidAltWarrior = "10000000000000021";

    /// <summary>Class ids of the demo characters (the game's classType values).</summary>
    public const int TamerClass = 16;
    public const int WarriorClass = 0;
    public const int ShaiClass = 17;

    private const int SkillsPerCharacter = 8;

    /// <summary>Upper bound of the crowd (the picker is for a few dozen characters; more is refused, not written).</summary>
    public const int MaxCrowd = 200;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The classes of the crowd characters, in turn (the demo's own classes, so the icons and the search have something to match).</summary>
    private static readonly int[] CrowdClasses = [16, 0, 17, 9, 20, 6, 11, 3];

    /// <summary>The names of the crowd characters, in turn; a second round gets a number after the name.</summary>
    private static readonly string[] CrowdNames =
    [
        "Lola", "Ana", "Bruno", "Celia", "Dario", "Elena", "Fabio", "Gala", "Hugo", "Irene", "Jorge", "Karla", "Luis",
        "Marta", "Nico", "Olga", "Pablo", "Rocio", "Sergio", "Tania", "Ulises", "Vera", "Wilmer", "Yara", "Zoe",
    ];

    /// <summary>The pcid of the crowd character with this index (17 digits, like the game's). Used only in the demo.</summary>
    public static string CrowdPcid(int index) => "1000000000000" + (100 + index).ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>Creates the demo data folder under <paramref name="dataRoot"/>. Existing files of the same names are replaced.</summary>
    /// <param name="crowd">Extra characters on the main account (the character picker and its search need many to show).</param>
    public static void Write(string dataRoot, SkillClassTable skills, AppLogger logger, int crowd = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(logger);
        using var scope = logger.BeginOperation("demo.game_files", new Dictionary<string, object?> { ["skill_table_entries"] = skills.Count });

        var userCache = Path.Combine(dataRoot, "UserCache");

        var main = Path.Combine(userCache, AccountMain);
        var order = new List<(string Pcid, int Order)>
        {
            (PcidTamer, 0), (PcidWarrior, 1), (PcidShai, 2), (PcidUnnamed, 3), ("10000000000000099", 4),
        };
        var labels = new List<(string Pcid, string Label)> { (PcidTamer, "Demo Tamer"), (PcidShai, "Demo Shai") };
        var characters = new List<(string Pcid, IReadOnlyList<int> Skills)>
        {
            (PcidTamer, SkillsOf(skills, TamerClass)),
            (PcidWarrior, SkillsOf(skills, WarriorClass)),
            (PcidShai, SkillsOf(skills, ShaiClass)),
            (PcidUnnamed, []),
        };
        for (var index = 0; index < Math.Clamp(crowd, 0, MaxCrowd); index++)
        {
            var pcid = CrowdPcid(index);
            var name = CrowdNames[index % CrowdNames.Length];
            var round = index / CrowdNames.Length;
            order.Add((pcid, 5 + index));
            labels.Add((pcid, round == 0 ? name : $"{name} {round + 1}"));
            characters.Add((pcid, SkillsOf(skills, CrowdClasses[index % CrowdClasses.Length])));
        }

        WriteAccount(main, order: order, labels: labels, characters: characters);
        WriteIgnoredFolders(main);

        var alt = Path.Combine(userCache, AccountAlt);
        WriteAccount(alt,
            order: [(PcidAltWarrior, 0)],
            labels: [],
            characters: [(PcidAltWarrior, SkillsOf(skills, WarriorClass))]);

        // A numeric folder without an "11" folder is not an account.
        Directory.CreateDirectory(Path.Combine(userCache, "100000003"));
        Directory.CreateDirectory(Path.Combine(userCache, "-1"));
        Directory.CreateDirectory(Path.Combine(userCache, "DemoNotAnAccount"));
    }

    /// <summary>The skill numbers a demo character uses: skills that only that class can use (none when the table is empty).</summary>
    private static IReadOnlyList<int> SkillsOf(SkillClassTable table, int classType) =>
        table.SkillsOnlyFor(classType, SkillsPerCharacter);

    private static void WriteAccount(
        string accountFolder,
        IReadOnlyList<(string Pcid, int Order)> order,
        IReadOnlyList<(string Pcid, string Label)> labels,
        IReadOnlyList<(string Pcid, IReadOnlyList<int> Skills)> characters)
    {
        Directory.CreateDirectory(accountFolder);

        var account = new StringBuilder();
        account.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Root>\r\n");
        account.Append("<CharacterOrderList Version=\"1\"/>\r\n");
        foreach (var (pcid, position) in order)
        {
            account.Append($"<CharacterOrderList CharacterNo=\"{pcid}\" Order=\"{position}\"/>\r\n");
        }

        account.Append("</Root>\r\n");
        File.WriteAllText(Path.Combine(accountFolder, "gamevariable.xml"), account.ToString(), Utf8);

        var lobby = new StringBuilder();
        lobby.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Root>\r\n<CharacterMemo Version=\"1\"/>\r\n");
        foreach (var (pcid, label) in labels)
        {
            lobby.Append($"<CharacterMemo CharacterNo=\"{pcid}\" Memo=\"{label}\"/>\r\n");
        }

        lobby.Append("</Root>\r\n");
        File.WriteAllText(Path.Combine(accountFolder, "gameVariable_lobby.xml"), lobby.ToString(), Utf8);

        var characterRoot = Path.Combine(accountFolder, "11");
        Directory.CreateDirectory(characterRoot);
        foreach (var (pcid, skills) in characters)
        {
            var folder = Path.Combine(characterRoot, pcid);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "gameVariable.xml"), CharacterFile(skills), Utf8);
        }
    }

    /// <summary>A stale folder, a sidecar file and a character folder that is not in the order list: none of them is live.</summary>
    private static void WriteIgnoredFolders(string accountFolder)
    {
        var characterRoot = Path.Combine(accountFolder, "11");
        Directory.CreateDirectory(Path.Combine(characterRoot, "1234"));
        File.WriteAllText(Path.Combine(accountFolder, "gamevariable-MSI.xml"), "<Root/>\r\n", Utf8);

        var stale = Path.Combine(characterRoot, "10000000000000098");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "gameVariable.xml"), CharacterFile([]), Utf8);
    }

    private static string CharacterFile(IReadOnlyList<int> skills)
    {
        var text = new StringBuilder();
        text.Append("<SkillCoolTimeSlot Version=\"2\">\r\n");
        for (var slot = 0; slot < GameVarsText.SlotCount; slot++)
        {
            var skillNo = slot < skills.Count ? skills[slot] : 0;
            text.Append($"\t<SkillCoolTimeSlot SlotNo=\"{slot}\" SkillNo=\"{skillNo}\"/>\r\n");
        }

        text.Append("</SkillCoolTimeSlot>\r\n");
        text.Append("<SkillCoolTimeMemo Version=\"1\"/>\r\n");
        text.Append("<QuickSlotData Version=\"3\">\r\n");
        for (var position = 0; position < skills.Count && position < 2; position++)
        {
            text.Append($"\t<QuickSlotSkillData Key=\"{position}\" SkillNo=\"{skills[skills.Count - 1 - position]}\" SkillLevel=\"1\"/>\r\n");
        }

        text.Append("</QuickSlotData>\r\n");
        return text.ToString();
    }
}
