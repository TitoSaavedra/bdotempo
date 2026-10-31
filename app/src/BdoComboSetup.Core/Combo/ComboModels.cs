namespace BdoComboSetup.Core.Combo;

public enum ComboMode
{
    Succession,
    Awakening,
}

/// <summary>Which bar a cooldown slot belongs to. Empty means the slot has no skill assigned.</summary>
public enum SlotGroup
{
    Combo,
    PriorityBuff,
    Empty,
}

public sealed record CharacterOption(string Name, string Pcid);

/// <summary>One cooldown slot as stored in a combo. QuickPosition is 1..9, or 0 when not on the quick bar.</summary>
/// <param name="SkillName">Null for a placeholder slot. The view shows it as "Skill N".</param>
/// <param name="SkillNo">Game skill number (the skillNo stored in gameVariable.xml). Null when unknown; it selects the icon.</param>
public sealed record SlotData(
    int Index,
    SlotGroup Group,
    string? SkillName,
    string? Memo,
    string? CustomName,
    int QuickPosition,
    int? SkillNo = null);

/// <summary>A combo for one class and mode. <see cref="ClassId"/> is the game class id used to check skills against the table.</summary>
public sealed record ComboDefinition(
    string ClassName,
    int ClassId,
    ComboMode Mode,
    string Description,
    string SourceUrl,
    IReadOnlyList<SlotData> Slots);
