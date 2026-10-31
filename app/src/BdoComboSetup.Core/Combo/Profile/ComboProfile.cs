namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The user's customization of one guide (one class and spec). It is stored on top of the bdoclasshub guide:
/// the guide gives the steps, the profile gives their order, which steps are kept, and the names, memos and slots.
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
