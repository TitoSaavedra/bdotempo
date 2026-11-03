using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Capture;

/// <summary>Prepares the pixels handed to the OCR engine: a crop of the right side of a frame, optionally enlarged.</summary>
internal static class OcrImage
{
    /// <summary>
    /// Copies the columns from <paramref name="left"/> to the right edge of the frame, enlarged <paramref name="scale"/> times
    /// with bilinear interpolation (BGRA, top row first). The game text is only a few pixels tall; the engine reads it far
    /// more reliably once the letters are bigger.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) CropScaled(GameScreenFrame frame, int left, int scale)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);
        left = Math.Clamp(left, 0, frame.Width - 1);
        var cropWidth = frame.Width - left;
        var width = cropWidth * scale;
        var height = frame.Height * scale;
        var pixels = new byte[width * height * 4];

        var sourceX = new int[width];
        var weightX = new float[width];
        for (var x = 0; x < width; x++)
        {
            var position = Math.Clamp(((x + 0.5f) / scale) - 0.5f, 0, cropWidth - 1.001f);
            sourceX[x] = (int)position;
            weightX[x] = position - sourceX[x];
        }

        Parallel.For(0, height, y =>
        {
            var position = Math.Clamp(((y + 0.5f) / scale) - 0.5f, 0, frame.Height - 1.001f);
            var sourceY = (int)position;
            var weightY = position - sourceY;
            var upper = sourceY * frame.Width;
            var lower = (sourceY + 1) * frame.Width;
            var target = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var column = left + sourceX[x];
                var topLeft = (upper + column) * 4;
                var bottomLeft = (lower + column) * 4;
                var fraction = weightX[x];
                for (var channel = 0; channel < 3; channel++)
                {
                    var top = (frame.Bgra[topLeft + channel] * (1 - fraction)) + (frame.Bgra[topLeft + 4 + channel] * fraction);
                    var bottom = (frame.Bgra[bottomLeft + channel] * (1 - fraction)) + (frame.Bgra[bottomLeft + 4 + channel] * fraction);
                    pixels[target + channel] = (byte)((top * (1 - weightY)) + (bottom * weightY) + 0.5f);
                }

                pixels[target + 3] = 255;
                target += 4;
            }
        });

        return (pixels, width, height);
    }
}
