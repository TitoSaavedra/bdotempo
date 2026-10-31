namespace BdoComboSetup.Core.Combo;

public enum QuickInputKind
{
    Mouse,
    Key,
}

/// <summary>The physical input bound to a quick slot position. MouseButton is set only for mouse inputs.</summary>
public sealed record QuickSlotInput(int Position, QuickInputKind Kind, string? MouseButton);

/// <summary>Quick bar layout: positions 1-4 are M1-M4, 5 and 6 are keys, 7 and 8 are M5 and M6, 9 is a key.</summary>
public static class QuickSlotMap
{
    public const int Count = 9;

    public static IReadOnlyList<QuickSlotInput> Inputs { get; } =
    [
        new(1, QuickInputKind.Mouse, "M1"),
        new(2, QuickInputKind.Mouse, "M2"),
        new(3, QuickInputKind.Mouse, "M3"),
        new(4, QuickInputKind.Mouse, "M4"),
        new(5, QuickInputKind.Key, null),
        new(6, QuickInputKind.Key, null),
        new(7, QuickInputKind.Mouse, "M5"),
        new(8, QuickInputKind.Mouse, "M6"),
        new(9, QuickInputKind.Key, null),
    ];

    public static QuickSlotInput InputFor(int position)
    {
        if (position is < 1 or > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Quick slot positions are 1 to 9.");
        }

        return Inputs[position - 1];
    }
}
