using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Core.Characters;

/// <summary>
/// Tells whether a captured frame is black: the capture of a window that cannot be read (or a frame the game has not drawn)
/// is almost all black pixels. A sample of the pixels is enough; the character selection screen is never that dark.
/// </summary>
public static class FrameDarkness
{
    /// <summary>One pixel in this many is sampled, in both directions.</summary>
    private const int SampleStep = 8;

    /// <summary>A pixel is black when its brightest channel is below this value.</summary>
    private const int BlackLevel = 12;

    /// <summary>The share of sampled pixels that must be black for the frame to count as black.</summary>
    private const double BlackShare = 0.97;

    public static bool IsMostlyBlack(GameScreenFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var sampled = 0;
        var black = 0;
        for (var y = 0; y < frame.Height; y += SampleStep)
        {
            for (var x = 0; x < frame.Width; x += SampleStep)
            {
                var offset = ((y * frame.Width) + x) * 4;
                var brightest = Math.Max(frame.Bgra[offset], Math.Max(frame.Bgra[offset + 1], frame.Bgra[offset + 2]));
                sampled++;
                if (brightest < BlackLevel)
                {
                    black++;
                }
            }
        }

        return sampled > 0 && black >= sampled * BlackShare;
    }
}
