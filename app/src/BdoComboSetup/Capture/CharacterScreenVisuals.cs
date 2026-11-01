using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Capture;

/// <summary>
/// Reads two things from a captured frame without OCR and without knowing the game language: whether the character
/// selection screen is open (the wide gold "enter the game" bar at the bottom right), and where the list scroll bar is
/// (the light gold thumb over the dark track at the right). Both are matched by color and shape, so they work with the
/// English, Spanish or any other client.
/// </summary>
internal static class CharacterScreenVisuals
{
    /// <summary>Below this fraction of the height the "enter the game" bar lives.</summary>
    private const double BottomBandStart = 0.80;

    /// <summary>The bar must be at least this fraction of the frame width to count (the per-character buttons are much smaller).</summary>
    private const double ButtonWidthFraction = 0.10;

    /// <summary>The scroll bar is looked for in this fraction of the width from the right edge.</summary>
    private const double ScrollBarBand = 0.10;

    /// <summary>The smallest thumb that is trusted, in pixels.</summary>
    private const int MinimumThumbHeight = 24;

    /// <summary>True when the wide gold bar of the character selection screen is visible.</summary>
    public static bool IsSelectionScreen(GameScreenFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var startX = (int)(frame.Width * 0.55);
        var startY = (int)(frame.Height * BottomBandStart);
        var needed = (int)(frame.Width * ButtonWidthFraction);
        for (var y = startY; y < frame.Height - 2; y += 2)
        {
            var run = 0;
            for (var x = startX; x < frame.Width; x += 2)
            {
                if (IsGold(frame, x, y))
                {
                    run += 2;
                    if (run >= needed)
                    {
                        return true;
                    }
                }
                else
                {
                    run = 0;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The scroll bar position as 0 (top) to 1 (bottom), measured from where the thumb is inside the span of the rows the
    /// OCR just read. Null when no thumb is found (the list fits on screen, or the bar is hidden).
    /// </summary>
    public static double? ScrollFraction(GameScreenFrame frame, IReadOnlyList<CharacterRowCandidate> rows)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count < 2)
        {
            return null;
        }

        var startX = (int)(frame.Width * (1 - ScrollBarBand));
        var bestLength = 0;
        var bestTop = 0;
        for (var x = frame.Width - 2; x >= startX; x--)
        {
            var (top, bottom) = LongestGoldRun(frame, x);
            if (bottom - top > bestLength)
            {
                bestLength = bottom - top;
                bestTop = top;
            }
        }

        if (bestLength < MinimumThumbHeight)
        {
            return null;
        }

        var listTop = rows.Min(row => row.Top);
        var listBottom = rows.Max(row => row.Bottom);
        var span = (listBottom - listTop) - bestLength;
        if (span <= 0)
        {
            // The thumb is as tall as the visible rows: the whole list fits, so it is at the top.
            return 0;
        }

        return Math.Clamp((bestTop - listTop) / span, 0, 1);
    }

    /// <summary>The longest run of thumb-gold pixels in one column, inside the list area.</summary>
    private static (int Top, int Bottom) LongestGoldRun(GameScreenFrame frame, int x)
    {
        var from = (int)(frame.Height * 0.03);
        var to = (int)(frame.Height * 0.95);
        var bestStart = 0;
        var bestEnd = 0;
        var start = -1;
        for (var y = from; y < to; y++)
        {
            if (IsGold(frame, x, y))
            {
                if (start < 0)
                {
                    start = y;
                }

                continue;
            }

            if (start >= 0)
            {
                if (y - start > bestEnd - bestStart)
                {
                    bestStart = start;
                    bestEnd = y;
                }

                start = -1;
            }
        }

        if (start >= 0 && to - start > bestEnd - bestStart)
        {
            bestStart = start;
            bestEnd = to;
        }

        return (bestStart, bestEnd);
    }

    /// <summary>The gold of the game's buttons and scroll thumb: warm, bright enough, clearly above the blue of the UI.</summary>
    private static bool IsGold(GameScreenFrame frame, int x, int y)
    {
        var offset = ((y * frame.Width) + x) * 4;
        var blue = frame.Bgra[offset];
        var green = frame.Bgra[offset + 1];
        var red = frame.Bgra[offset + 2];
        return red >= 110 && red > green && green > blue && blue < 140 && red - blue >= 40;
    }
}
