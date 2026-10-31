namespace BdoComboSetup.Core.Game;

/// <summary>
/// The radial cooldown sweep. The game's mask is a sheet of 100 frames: 10 x 10 cells of 52 x 52 pixels, read row by row.
/// In a frame the dark part is the remaining cooldown, drawn clockwise from 12 o'clock: frame 0 is a sliver, frame 99 is the
/// whole disc. The frame of a remaining fraction r is round(r * 99).
/// </summary>
public static class CooldownSweepFrames
{
    /// <summary>Frames in the sheet.</summary>
    public const int FrameCount = 100;

    /// <summary>Cells per row (and per column) of the sheet.</summary>
    public const int Columns = 10;

    /// <summary>Side of one frame in pixels.</summary>
    public const int CellSize = 52;

    /// <summary>Side of the whole sheet in pixels (10 cells of 52).</summary>
    public const int SheetSize = Columns * CellSize;

    /// <summary>The frame that shows a remaining fraction (0 = ready, 1 = just started). Out-of-range values are clamped.</summary>
    public static int FrameFor(double remainingRatio)
    {
        if (double.IsNaN(remainingRatio))
        {
            return 0;
        }

        var clamped = Math.Clamp(remainingRatio, 0, 1);
        return (int)Math.Round(clamped * (FrameCount - 1), MidpointRounding.AwayFromZero);
    }

    /// <summary>The top-left pixel of a frame's cell in the sheet.</summary>
    public static (int X, int Y) CellOrigin(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, FrameCount);
        return ((frame % Columns) * CellSize, (frame / Columns) * CellSize);
    }
}
