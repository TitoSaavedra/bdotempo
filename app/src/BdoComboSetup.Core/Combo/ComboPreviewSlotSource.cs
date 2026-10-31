namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Feeds the preview from one combo: cooldown slot k is the combo slot with Index k, and quick slot p is the combo
/// slot whose QuickPosition is p. Empty combo slots are null.
/// </summary>
public sealed class ComboPreviewSlotSource : IPreviewSlotSource
{
    private const int MainQuickSlotCount = 10;

    private readonly IReadOnlyList<SlotData> _slots;

    public ComboPreviewSlotSource(ComboDefinition combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        _slots = combo.Slots;
    }

    public PreviewSlot? Cooldown(int number)
    {
        ValidateNumber(number, 20);
        var slot = _slots.FirstOrDefault(candidate => candidate.Index == number);
        return slot is null || slot.Group == SlotGroup.Empty ? null : ToPreview(slot);
    }

    public PreviewSlot? Quick(int number)
    {
        ValidateNumber(number, MainQuickSlotCount);
        var slot = _slots.FirstOrDefault(candidate => candidate.Group != SlotGroup.Empty && candidate.QuickPosition == number);
        return slot is null ? null : ToPreview(slot);
    }

    private static PreviewSlot ToPreview(SlotData slot) =>
        new(slot.SkillNo, slot.SkillName, slot.Memo, slot.CustomName, slot.Index);

    private static void ValidateNumber(int number, int max)
    {
        if (number < 1 || number > max)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"Slot numbers are 1 to {max}.");
        }
    }
}
