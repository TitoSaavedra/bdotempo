using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Views;

/// <summary>
/// What is being dragged in the editor: a pending skill of the list (its step) or the skill of a cooldown slot. The drag
/// happens inside the app, so the item is kept here instead of in the drag data.
/// </summary>
internal static class SlotDrag
{
    /// <summary>The step being dragged.</summary>
    public static ComboStepViewModel? Step { get; set; }

    /// <summary>The slot the step is dragged from, or null when it comes from the pending list.</summary>
    public static int? FromSlot { get; set; }

    public static void Clear()
    {
        Step = null;
        FromSlot = null;
    }
}
