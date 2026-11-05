namespace BdoComboSetup.Core.ViewModels;

/// <summary>The two boards of the Combos page: the cooldown slots and the quick positions.</summary>
public enum ComboBoard
{
    Cooldown,
    Quick,
}

/// <summary>How a step was dragged onto a board. The editor decides the result from it (see <see cref="ComboEditorViewModel.DropOnBoard"/>).</summary>
public enum BoardDrop
{
    /// <summary>From a chain card or the mobility pool: a step that already holds a position on the board is duplicated; otherwise it is placed.</summary>
    Place,

    /// <summary>From a board cell: the step moves, with the swap rule of <c>SlotAssignment</c>.</summary>
    Move,

    /// <summary>From a board cell with Ctrl held: a duplicate of the step goes to the target, and the original keeps its position.</summary>
    Copy,
}
