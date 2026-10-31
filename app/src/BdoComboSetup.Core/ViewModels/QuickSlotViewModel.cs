using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>One of the nine quick bar positions. Slot is the cooldown slot assigned to it, or null.</summary>
public sealed partial class QuickSlotViewModel : ObservableObject
{
    public QuickSlotViewModel(QuickSlotInput input, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(localizer);

        Input = input;
        Label = input.Kind == QuickInputKind.Mouse
            ? input.MouseButton ?? throw new InvalidOperationException("Mouse quick inputs need a button name.")
            : localizer.Format("Preview_KeyLabel", input.Position);
    }

    public QuickSlotInput Input { get; }

    public int Position => Input.Position;

    /// <summary>Mouse button name (M1..M6) or a localized key label.</summary>
    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlot))]
    private SlotViewModel? _slot;

    public bool HasSlot => Slot is not null;

    /// <summary>Top-left of the quick slot relative to the quick bar.</summary>
    [ObservableProperty]
    private PixelOffset _offset;

    [ObservableProperty]
    private double _size;
}
