namespace BdoComboSetup.Core.Game;

/// <summary>
/// UIData indices of the two bars in the account file UserCache\330\gamevariable.xml.
/// A verified index is an exact panel; null means "not verified in game yet".
/// The quick slot bar has no verified index yet: its candidates are the bottom-centre panels, and the preview
/// uses them as the default position until the real index is confirmed.
/// </summary>
public sealed record GameLayoutIndices(int? CooldownBarIndex, int? QuickSlotBarIndex, IReadOnlyList<int> QuickSlotBarCandidates)
{
    /// <summary>Bottom-centre panels seen in the General layout (IsShow=true). Confirm in game with the panel overlay.</summary>
    public static IReadOnlyList<int> DefaultQuickSlotCandidates { get; } = [100, 91, 92, 93, 94, 95, 96, 97];

    /// <summary>Nothing verified yet: no cooldown index, no quick index, quick bar candidates only.</summary>
    public static GameLayoutIndices Default { get; } = new(
        CooldownBarIndex: null,
        QuickSlotBarIndex: null,
        QuickSlotBarCandidates: DefaultQuickSlotCandidates);

    /// <summary>True only when both bars have a verified index. Candidates never count as verified.</summary>
    public bool IsVerified => CooldownBarIndex is not null && QuickSlotBarIndex is not null;
}
