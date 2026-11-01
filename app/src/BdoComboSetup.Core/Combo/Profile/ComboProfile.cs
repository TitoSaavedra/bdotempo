namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The user's customization of one guide (one class and spec). It is stored on top of the bdoclasshub guide:
/// the guide gives the steps, the profile gives their order, which steps are kept, and the names, memos and slots.
/// A profile is always a copy: the bdoclasshub guide is never modified.
/// </summary>
public sealed record ComboProfile(int ClassType, string SpecId, IReadOnlyList<ComboProfileChain> Chains);

/// <summary>The steps of one guide chain, in the user's order. <see cref="Chain"/> is the chain's index in the guide.</summary>
public sealed record ComboProfileChain(int Chain, IReadOnlyList<ComboProfileStep> Steps);

/// <summary>
/// One step of the profile. <see cref="BaseStep"/> is the index of a guide step; a step the user added has no base step
/// and names its skill with <see cref="SkillNo"/>. Guide steps that are not listed were removed.
/// </summary>
/// <param name="CooldownSlot">1..20, or null when not on the cooldown bar.</param>
/// <param name="QuickPosition">1..9, or null when not on the quick bar.</param>
public sealed record ComboProfileStep(
    int? BaseStep,
    int? SkillNo,
    string? CustomName,
    string? Memo,
    int? CooldownSlot,
    int? QuickPosition);

/// <summary>
/// One of the <see cref="SlotCount"/> custom combos a class and spec can keep. A slot holds its own copy of the chains,
/// never a link to the bdoclasshub guide, so saving, renaming or deleting a slot cannot change the guide.
/// </summary>
/// <param name="Slot">1..10, the fixed place of the combo in the page.</param>
/// <param name="Name">
/// The user's name for the combo, or null when the page should show the default label of the slot number.
/// Names are trimmed and cut to <see cref="MaxNameLength"/> characters when saved.
/// </param>
/// <param name="SavedAt">When the slot was last saved, in UTC.</param>
/// <param name="Chains">The steps of the combo, exactly as the editor builds them.</param>
public sealed record ComboProfileSlot(
    int ClassType,
    string SpecId,
    int Slot,
    string? Name,
    DateTimeOffset SavedAt,
    IReadOnlyList<ComboProfileChain> Chains)
{
    /// <summary>The first slot. Slots are numbered from 1 so the page can show them as 1 to 10.</summary>
    public const int FirstSlot = 1;

    /// <summary>The last slot.</summary>
    public const int LastSlot = 10;

    /// <summary>How many custom combos one class and spec can keep.</summary>
    public const int SlotCount = LastSlot - FirstSlot + 1;

    /// <summary>The longest name that is kept. Longer names are cut when saved.</summary>
    public const int MaxNameLength = 60;

    /// <summary>True when <paramref name="slot"/> is one of the ten slots.</summary>
    public static bool IsValidSlot(int slot) => slot is >= FirstSlot and <= LastSlot;

    /// <summary>The chains of this slot as a profile, ready for the combo editor.</summary>
    public ComboProfile ToProfile() => new(ClassType, SpecId, Chains);
}
