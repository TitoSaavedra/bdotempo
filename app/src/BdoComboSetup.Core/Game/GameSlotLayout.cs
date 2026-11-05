namespace BdoComboSetup.Core.Game;

/// <summary>The two kinds of slot on the game screen. Each slot is its own UIData panel.</summary>
public enum SlotKind
{
    /// <summary>The skill cooldown bar: panels with only the skill icon (no frame, no background).</summary>
    Cooldown,

    /// <summary>The quick slot bar: panels with a semi-transparent slot, a handle and a key legend.</summary>
    Quick,
}

/// <summary>
/// UIData indices of the slot panels (PAGameUIPanel enum). Cooldown slots 1-10 are Index 128-137, 11-20 are 148-157.
/// Quick slots 1-20 are Index 91-110: slot 10 is Index 100 (key 0), and 101-110 is the second set.
/// </summary>
public static class GameSlotIndices
{
    public const int CooldownSlotCount = 20;
    public const int QuickSlotCount = QuickSlotKeys.SlotCount;

    public static int CooldownPanelIndex(int number)
    {
        ValidateNumber(number, CooldownSlotCount);
        return number <= 10 ? 127 + number : 137 + number;
    }

    public static int QuickPanelIndex(int number)
    {
        ValidateNumber(number, QuickSlotCount);
        return 90 + number;
    }

    private static void ValidateNumber(int number, int count)
    {
        if (number < 1 || number > count)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"Slot numbers are 1 to {count}.");
        }
    }
}

/// <summary>
/// One slot panel in game pixels. CentreX/CentreY is where the slot art is centred. Width/Height is the panel size
/// measured as 2 x (centre - top-left corner) when the row stores its centre, otherwise its SizeX/SizeY or the nominal size.
/// </summary>
public sealed record GameSlotPlacement(
    SlotKind Kind,
    int Number,
    int PanelIndex,
    bool Present,
    bool IsShow,
    double CentreX,
    double CentreY,
    double Width,
    double Height);

/// <summary>Places the 40 slot panels (20 cooldown, 20 quick) of one layout. Pure: no file or UI access.</summary>
public static class GameSlotLayoutResolver
{
    /// <summary>Art size of a cooldown slot panel in game pixels at UI scale 1.</summary>
    public const double CooldownNominalWidth = 50;

    /// <summary>Art size of a cooldown slot panel in game pixels at UI scale 1.</summary>
    public const double CooldownNominalHeight = 50;

    /// <summary>Art size of a quick slot panel in game pixels at UI scale 1.</summary>
    public const double QuickNominalWidth = 50;

    /// <summary>Art size of a quick slot panel in game pixels at UI scale 1 (handle strip above a 44 x 44 slot).</summary>
    public const double QuickNominalHeight = 65;

    public static (double Width, double Height) NominalSize(SlotKind kind) => kind == SlotKind.Cooldown
        ? (CooldownNominalWidth, CooldownNominalHeight)
        : (QuickNominalWidth, QuickNominalHeight);

    /// <summary>The 20 cooldown placements followed by the 20 quick placements. A panel without a row is Present=false.</summary>
    public static IReadOnlyList<GameSlotPlacement> Resolve(GameUiLayout? layout, double gameWidth, double gameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameHeight);

        var placements = new List<GameSlotPlacement>(GameSlotIndices.CooldownSlotCount + GameSlotIndices.QuickSlotCount);
        for (var number = 1; number <= GameSlotIndices.CooldownSlotCount; number++)
        {
            placements.Add(Place(layout, SlotKind.Cooldown, number, gameWidth, gameHeight));
        }

        for (var number = 1; number <= GameSlotIndices.QuickSlotCount; number++)
        {
            placements.Add(Place(layout, SlotKind.Quick, number, gameWidth, gameHeight));
        }

        return placements;
    }

    private static GameSlotPlacement Place(GameUiLayout? layout, SlotKind kind, int number, double gameWidth, double gameHeight)
    {
        var index = kind == SlotKind.Cooldown
            ? GameSlotIndices.CooldownPanelIndex(number)
            : GameSlotIndices.QuickPanelIndex(number);
        var (nominalWidth, nominalHeight) = NominalSize(kind);

        var panel = layout?.Find(index);
        if (panel is null || UiPanelGeometry.TopLeft(panel, gameWidth, gameHeight) is not { } corner)
        {
            return new GameSlotPlacement(kind, number, index, false, false, 0, 0, nominalWidth, nominalHeight);
        }

        if (panel.HasRelativeCentre)
        {
            var centreX = panel.RelativeX!.Value * gameWidth;
            var centreY = panel.RelativeY!.Value * gameHeight;
            var width = 2 * Math.Abs(centreX - corner.X);
            var height = 2 * Math.Abs(centreY - corner.Y);
            if (width > 0 && height > 0)
            {
                return new GameSlotPlacement(kind, number, index, true, panel.IsShow, centreX, centreY, width, height);
            }
        }

        // No usable stored centre: the corner is the top-left edge, so the centre is half a panel further in.
        var sizeX = panel.HasSize ? panel.SizeX!.Value : nominalWidth;
        var sizeY = panel.HasSize ? panel.SizeY!.Value : nominalHeight;
        return new GameSlotPlacement(kind, number, index, true, panel.IsShow, corner.X + (sizeX / 2), corner.Y + (sizeY / 2), sizeX, sizeY);
    }
}
