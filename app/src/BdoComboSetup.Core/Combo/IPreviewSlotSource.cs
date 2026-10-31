namespace BdoComboSetup.Core.Combo;

/// <summary>
/// One skill as the preview shows it. CooldownNumber is the cooldown slot (1-20) the same skill sits in, when known;
/// it links a quick slot to its cooldown slot in the detail panel.
/// </summary>
public sealed record PreviewSlot(int? SkillNo, string? SkillName, string? Memo, string? CustomName, int? CooldownNumber = null);

/// <summary>
/// Where the preview gets the skills of its slots. The Apply page will feed the same contract later; for now
/// <see cref="ComboPreviewSlotSource"/> reads the combo being edited.
/// </summary>
public interface IPreviewSlotSource
{
    /// <summary>The skill of cooldown slot 1-20, or null when the slot is empty.</summary>
    PreviewSlot? Cooldown(int number);

    /// <summary>The skill of quick slot 1-10, or null when the quick slot is empty. Quick slots 11-20 have no source yet.</summary>
    PreviewSlot? Quick(int number);
}
