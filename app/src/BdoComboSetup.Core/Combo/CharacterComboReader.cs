using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Combo;

/// <summary>One skill on a character's bar: its slot (cooldown 1..20 or quick position 1..20), its skill number and its memo.</summary>
/// <param name="Slot">The UI slot number: the cooldown slot, or the quick position.</param>
/// <param name="Memo">The memo the game keeps for the skill (XML-unescaped), or null when it has none.</param>
public sealed record BarSkill(int Slot, int SkillNo, string? Memo);

/// <summary>
/// The combo a character has in the game: the cooldown slots that hold a skill and the quick bar positions 1..20 that hold a
/// skill.
/// </summary>
public sealed record CharacterCombo(IReadOnlyList<BarSkill> Cooldown, IReadOnlyList<BarSkill> Quick);

/// <summary>
/// Reads the combo a character has in the game, for the Apply page's import. Read-only: it uses the same parsers the
/// applier and its diff use (<see cref="GameVarsText"/> and <see cref="QuickSlotData"/>) and never writes.
/// </summary>
public sealed class CharacterComboReader(GameDataLocator locator, GameFileReader reader, AppLogger logger)
{
    /// <summary>True when the game data folder exists and has its UserCache folder.</summary>
    public bool IsDataFolderReady => GameDataLocator.CheckDataRoot(locator.DataRoot) == DataRootState.Ok;

    /// <summary>
    /// The combo of one character of an account, or null when its file is missing, still locked or does not have the layout
    /// the code understands. The reason is logged.
    /// </summary>
    public CharacterCombo? Read(string account, string pcid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(pcid);
        using var scope = logger.BeginOperation("combo.import.read", new Dictionary<string, object?> { ["pcid"] = pcid });

        var text = reader.ReadText(locator.CharacterFilePath(account, pcid));
        if (text is null)
        {
            logger.Warn("combo.import.unreadable", "The character file is missing or locked; nothing is imported", new Dictionary<string, object?>
            {
                ["pcid"] = pcid,
            });
            return null;
        }

        try
        {
            var vars = GameVarsText.Parse(text);
            var memos = new Dictionary<int, string>();
            foreach (var entry in vars.Memos)
            {
                memos.TryAdd(entry.SkillNo, GameVarsText.XmlUnescapeAttr(entry.Memo));
            }

            var cooldown = vars.Slots
                .Select((skillNo, index) => (Slot: index + 1, SkillNo: skillNo))
                .Where(item => item.SkillNo > 0)
                .Select(item => new BarSkill(item.Slot, item.SkillNo, memos.GetValueOrDefault(item.SkillNo)))
                .ToList();

            var quick = QuickSlotData.Read(text)
                .Take(QuickSlotKeys.ComboPositionCount)
                .Select((entry, index) => (Position: index + 1, Entry: entry))
                .Where(item => item.Entry.Kind == QuickRecordKind.Skill && item.Entry.SkillNo is > 0)
                .Select(item => new BarSkill(item.Position, item.Entry.SkillNo!.Value, memos.GetValueOrDefault(item.Entry.SkillNo!.Value)))
                .ToList();

            logger.Info("combo.import.read_done", "Character combo read", new Dictionary<string, object?>
            {
                ["pcid"] = pcid,
                ["cooldown_slots"] = cooldown.Count,
                ["quick_slots"] = quick.Count,
            });
            return new CharacterCombo(cooldown, quick);
        }
        catch (SaveFormatException ex)
        {
            scope.Fail(ex);
            logger.Error("combo.import.format", "The character file does not have the expected layout; nothing is imported", ex, new Dictionary<string, object?>
            {
                ["pcid"] = pcid,
            });
            return null;
        }
    }
}
