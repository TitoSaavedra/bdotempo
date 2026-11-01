using System.Globalization;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Characters;

/// <summary>Where the name of a character comes from: the user, the in-game label, or the generated fallback.</summary>
public enum NameSource
{
    Custom,
    Label,
    Fallback,
}

/// <summary>Where the class of a character comes from: the user, the skills in its files, or nowhere (unknown).</summary>
public enum ClassSource
{
    Custom,
    Inferred,
    Unknown,
}

/// <summary>A live character of an account, with its name and class resolved.</summary>
public sealed record CharacterEntry(
    string Pcid,
    int Order,
    string Name,
    NameSource NameSource,
    int? ClassType,
    ClassSource ClassSource);

/// <summary>The characters of the active account, and the user's own names and classes for them.</summary>
public interface ICharacterSource
{
    /// <summary>The account the Apply page works with, or null when no account can be found.</summary>
    string? ActiveAccount();

    /// <summary>The live characters of an account, in the game's order.</summary>
    IReadOnlyList<CharacterEntry> Load(string account);

    /// <summary>Saves the user's name and class for a character. Null values clear that part.</summary>
    void SaveOverride(string account, string pcid, string? name, int? classType);

    /// <summary>Forgets every name and class the user set for an account, so the roster goes back to the game data.</summary>
    /// <returns>How many characters were reset.</returns>
    int ResetOverrides(string account);
}

/// <summary>
/// Builds the character selector of an account. Live characters are the pcids of the account's CharacterOrderList
/// (gamevariable.xml) that also have a 17-digit folder with a gameVariable.xml under 11\. The stale folders, the
/// sidecar files (gamevariable-*.xml) and the characters not in the order list are ignored.
/// Name: the user's name, then the in-game label (CharacterMemo of gameVariable_lobby.xml), then "ClassName N".
/// Class: the user's class, then the vote over the skill numbers of the character's files, then unknown.
/// Files are read only; every read retries briefly when the game is writing them.
/// </summary>
public sealed partial class CharacterRoster(
    GameDataLocator locator,
    CharacterOverrideStore overrides,
    GameFileReader reader,
    SkillClassTable skillClasses,
    ICombosCatalog catalog,
    ILocalizer localizer,
    CultureInfo culture,
    AppLogger logger) : ICharacterSource
{
    public const string AccountFileName = "gamevariable.xml";
    public const string LobbyFileName = "gameVariable_lobby.xml";
    public const string CharacterFolderName = "11";

    /// <summary>The fallback counter key of the characters whose class is unknown (class ids are never negative).</summary>
    private const int UnknownClassKey = -1;

    [GeneratedRegex("<CharacterOrderList\\s+CharacterNo=\"([0-9]+)\"\\s+Order=\"([0-9]+)\"\\s*/>", RegexOptions.CultureInvariant)]
    private static partial Regex OrderEntryRe();

    [GeneratedRegex("<CharacterMemo\\s+CharacterNo=\"([0-9]+)\"\\s+Memo=\"([^\"]*)\"\\s*/>", RegexOptions.CultureInvariant)]
    private static partial Regex LabelEntryRe();

    [GeneratedRegex("<SkillCoolTimeSlot\\s+SlotNo=\"[0-9]+\"\\s+SkillNo=\"([0-9]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex SlotSkillRe();

    [GeneratedRegex("<SkillCoolTimeMemo\\s+SkillNo=\"([0-9]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex MemoSkillRe();

    [GeneratedRegex("<QuickSlotSkillData\\b[^>]*?\\bSkillNo=\"([0-9]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuickSkillRe();

    [GeneratedRegex("\\A[0-9]{17}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PcidRe();

    private readonly AppLogger _logger = logger;

    public string? ActiveAccount() => locator.TryResolveAccount();

    public void SaveOverride(string account, string pcid, string? name, int? classType)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(pcid);

        var value = name is null && classType is null ? null : new CharacterOverride(name, classType);
        overrides.Set(account, pcid, value);
        _logger.Info("characters.override_saved", "Character name or class saved", new Dictionary<string, object?>
        {
            ["custom_name"] = CharacterOverrideStore.NormalizeName(name) is not null,
            ["custom_class"] = classType is not null,
        });
    }

    public int ResetOverrides(string account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return overrides.Clear(account);
    }

    public IReadOnlyList<CharacterEntry> Load(string account)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        using var scope = _logger.BeginOperation("characters.load", new Dictionary<string, object?> { ["account"] = account });

        var root = Path.Combine(locator.UserCacheDir, account);
        var order = ReadOrder(root);
        var labels = ReadLabels(root);
        var folders = FolderPcids(root);

        var live = order
            .Where(entry => folders.Contains(entry.Key))
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
        var ignored = order.Count - live.Count;

        var drafts = new List<Draft>(live.Count);
        foreach (var (pcid, position) in live)
        {
            var skills = SkillNumbers(Path.Combine(root, CharacterFolderName, pcid, GameDataLocator.CharacterFileName));
            var inferred = ClassInference.Infer(skills, skillClasses);
            var custom = overrides.Get(account, pcid);
            var classType = custom?.ClassType ?? inferred;
            var classSource = custom?.ClassType is not null
                ? ClassSource.Custom
                : inferred is not null ? ClassSource.Inferred : ClassSource.Unknown;

            var customName = CharacterOverrideStore.NormalizeName(custom?.Name);
            var label = labels.GetValueOrDefault(pcid);
            drafts.Add(new Draft(pcid, position, classType, classSource, customName, label));
        }

        var entries = new List<CharacterEntry>(drafts.Count);
        // The fallback number counts the characters of one class (unknown class: its own count), in roster order.
        var fallbackCount = new Dictionary<int, int>();
        foreach (var draft in drafts)
        {
            var countKey = draft.ClassType ?? UnknownClassKey;
            fallbackCount[countKey] = fallbackCount.GetValueOrDefault(countKey) + 1;
            var (name, source) = draft.CustomName is not null
                ? (draft.CustomName, NameSource.Custom)
                : draft.Label is not null
                    ? (draft.Label, NameSource.Label)
                    : (FallbackName(draft.ClassType, fallbackCount[countKey]), NameSource.Fallback);
            entries.Add(new CharacterEntry(draft.Pcid, draft.Position, name, source, draft.ClassType, draft.ClassSource));
        }

        _logger.Info("characters.loaded", "Characters of the account loaded", new Dictionary<string, object?>
        {
            ["account"] = account,
            ["live"] = entries.Count,
            ["ignored_order_entries"] = ignored,
            ["custom_names"] = entries.Count(e => e.NameSource == NameSource.Custom),
            ["labels"] = entries.Count(e => e.NameSource == NameSource.Label),
            ["class_custom"] = entries.Count(e => e.ClassSource == ClassSource.Custom),
            ["class_inferred"] = entries.Count(e => e.ClassSource == ClassSource.Inferred),
            ["class_unknown"] = entries.Count(e => e.ClassSource == ClassSource.Unknown),
            ["skill_table_entries"] = skillClasses.Count,
        });
        return entries;
    }

    private string FallbackName(int? classType, int number)
    {
        if (classType is null)
        {
            return localizer.Format("Characters.UnknownFallback", number);
        }

        var className = catalog.Classes.FirstOrDefault(info => info.ClassType == classType)?.Name.For(culture);
        return className is null
            ? localizer.Format("Characters.UnknownFallback", number)
            : localizer.Format("Characters.ClassFallback", className, number);
    }

    /// <summary>pcid to order, first occurrence wins. Only 17-digit pcids are kept.</summary>
    private Dictionary<string, int> ReadOrder(string root)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        var text = reader.ReadText(Path.Combine(root, AccountFileName));
        if (text is null)
        {
            _logger.Warn("characters.no_order_list", "The account file has no readable character order list");
            return order;
        }

        foreach (Match match in OrderEntryRe().Matches(text))
        {
            var pcid = match.Groups[1].Value;
            if (!PcidRe().IsMatch(pcid) || order.ContainsKey(pcid))
            {
                continue;
            }

            order[pcid] = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        return order;
    }

    /// <summary>pcid to the in-game label, when the label is not empty.</summary>
    private Dictionary<string, string> ReadLabels(string root)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = reader.ReadText(Path.Combine(root, LobbyFileName));
        if (text is null)
        {
            return labels;
        }

        foreach (Match match in LabelEntryRe().Matches(text))
        {
            var pcid = match.Groups[1].Value;
            var label = GameVarsText.XmlUnescapeAttr(match.Groups[2].Value).Trim();
            if (PcidRe().IsMatch(pcid) && label.Length > 0 && !labels.ContainsKey(pcid))
            {
                labels[pcid] = label;
            }
        }

        return labels;
    }

    /// <summary>The 17-digit folders under 11 that hold a gameVariable.xml.</summary>
    private HashSet<string> FolderPcids(string root)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        var characterRoot = Path.Combine(root, CharacterFolderName);
        if (!Directory.Exists(characterRoot))
        {
            return folders;
        }

        foreach (var directory in Directory.EnumerateDirectories(characterRoot))
        {
            var pcid = Path.GetFileName(directory);
            if (PcidRe().IsMatch(pcid) && File.Exists(Path.Combine(directory, GameDataLocator.CharacterFileName)))
            {
                folders.Add(pcid);
            }
        }

        return folders;
    }

    /// <summary>The skill numbers of the cooldown bar, the cooldown memos and the quick bar of one character file.</summary>
    private IReadOnlyList<int> SkillNumbers(string characterFile)
    {
        var text = reader.ReadText(characterFile);
        if (text is null)
        {
            return [];
        }

        var numbers = new List<int>();
        foreach (var pattern in new[] { SlotSkillRe(), MemoSkillRe(), QuickSkillRe() })
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var skillNo) && skillNo > 0)
                {
                    numbers.Add(skillNo);
                }
            }
        }

        return numbers;
    }

    private sealed record Draft(
        string Pcid,
        int Position,
        int? ClassType,
        ClassSource ClassSource,
        string? CustomName,
        string? Label);
}
