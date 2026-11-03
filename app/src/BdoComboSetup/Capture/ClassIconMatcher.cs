using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BdoComboSetup.Capture;

/// <summary>
/// Guesses the class of a character row from the icon the selection screen draws left of the level. The icon is found as
/// the bright shape in the row cell (whatever the row background is), fitted to a square without distorting it, and
/// compared with the class icons of the pack decoded the same way. A weak or ambiguous match is reported as "no class":
/// the review table lets the user set it.
/// </summary>
internal sealed class ClassIconMatcher
{
    /// <summary>Sampling size of the signatures.</summary>
    private const int SampleSize = 16;

    /// <summary>The class icons are decoded at this size before their content is fitted to the signature.</summary>
    private const int DecodeSize = 32;

    /// <summary>A match below this correlation is not trusted.</summary>
    private const float MinimumScore = 0.45f;

    /// <summary>The best class must beat the second one by this margin, or the guess is ambiguous.</summary>
    private const float MinimumMargin = 0.05f;

    /// <summary>
    /// Icons made of thin lines correlate low even when they are unmistakable. A lower score is trusted when the best class
    /// beats the second one by a wide margin (on real frames those were 0.34 to 0.45 with 0.17 or more).
    /// </summary>
    private const float LowScoreFloor = 0.33f;

    private const float LowScoreMargin = 0.15f;

    private readonly IReadOnlyList<(int ClassType, float[] Signature)> _icons;
    private readonly AppLogger _logger;

    private ClassIconMatcher(IReadOnlyList<(int ClassType, float[] Signature)> icons, AppLogger logger)
    {
        _icons = icons;
        _logger = logger;
    }

    public static async Task<ClassIconMatcher> CreateAsync(IReadOnlyList<ClassChoice> classes, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(logger);

        var icons = new List<(int ClassType, float[] Signature)>();
        foreach (var choice in classes)
        {
            try
            {
                using var stream = choice.OpenIcon();
                if (stream is null)
                {
                    continue;
                }

                if (await SignatureOfAsync(stream) is { } signature)
                {
                    icons.Add((choice.ClassType, signature));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Warn("character_scan.class_icon_failed", "A class icon could not be read for the scan", new Dictionary<string, object?>
                {
                    ["class_type"] = choice.ClassType,
                    ["error"] = ex.GetType().Name,
                });
            }
        }

        logger.Info("character_scan.class_icons", "Class icons prepared for the scan", new Dictionary<string, object?>
        {
            ["icons"] = icons.Count,
        });
        return new ClassIconMatcher(icons, logger);
    }

    /// <summary>The class of the row icon, or null when the guess is weak, ambiguous or the crop is unusable.</summary>
    public int? Match(GameScreenFrame frame, CharacterRowCandidate row)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(row);
        if (_icons.Count == 0 || CropSignature(frame, row) is not { } crop)
        {
            return null;
        }

        var best = float.NegativeInfinity;
        var second = float.NegativeInfinity;
        var bestClass = 0;
        foreach (var (classType, signature) in _icons)
        {
            var score = Correlation(crop, signature);
            if (score > best)
            {
                second = best;
                best = score;
                bestClass = classType;
            }
            else if (score > second)
            {
                second = score;
            }
        }

        var margin = best - second;
        var trusted = (best >= MinimumScore && margin >= MinimumMargin) || (best >= LowScoreFloor && margin >= LowScoreMargin);
        if (!trusted)
        {
            _logger.Debug("character_scan.class_weak", "The class icon was not recognized", new Dictionary<string, object?>
            {
                ["best_class"] = bestClass,
                ["score"] = Math.Round(best, 3),
                ["margin"] = Math.Round(best - second, 3),
            });
            return null;
        }

        _logger.Debug("character_scan.class_matched", "The class icon was recognized", new Dictionary<string, object?>
        {
            ["class_type"] = bestClass,
            ["score"] = Math.Round(best, 3),
            ["margin"] = Math.Round(best - second, 3),
        });
        return bestClass;
    }

    /// <summary>Decodes a class icon and fits its opaque content into the signature square.</summary>
    private static async Task<float[]?> SignatureOfAsync(Stream png)
    {
        using var memory = new MemoryStream();
        await png.CopyToAsync(memory);
        var bytes = memory.ToArray();

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform
            {
                ScaledWidth = DecodeSize,
                ScaledHeight = DecodeSize,
                InterpolationMode = BitmapInterpolationMode.Fant,
            },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var pixels = new byte[DecodeSize * DecodeSize * 4];
        var buffer = new Windows.Storage.Streams.Buffer((uint)pixels.Length);
        bitmap.CopyToBuffer(buffer);
        using (var reader = DataReader.FromBuffer(buffer))
        {
            reader.ReadBytes(pixels);
        }

        var values = new float[DecodeSize * DecodeSize];
        var left = DecodeSize;
        var top = DecodeSize;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < DecodeSize; y++)
        {
            for (var x = 0; x < DecodeSize; x++)
            {
                var offset = ((y * DecodeSize) + x) * 4;
                var alpha = pixels[offset + 3];
                values[(y * DecodeSize) + x] = Luminance(pixels[offset + 2], pixels[offset + 1], pixels[offset]) * (alpha / 255f);
                if (alpha > 40)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < left || bottom < top
            ? null
            : Fit(values, DecodeSize, left, top, right, bottom);
    }

    /// <summary>
    /// The icon of the row: the bright shape in the cell left of the level text. The cell is found from the level position
    /// and the row height, so it works with the selected (gold) row and with the dark rows.
    /// </summary>
    private static float[]? CropSignature(GameScreenFrame frame, CharacterRowCandidate row)
    {
        var height = (int)Math.Max(1, row.Bottom - row.Top);
        var left = (int)Math.Max(0, row.LevelLeft - (height * 3.0));
        var right = (int)Math.Min(frame.Width - 1, row.LevelLeft - 4);
        var top = (int)Math.Max(0, row.Top - (height * 1.1));
        var bottom = (int)Math.Min(frame.Height - 1, row.Bottom + (height * 1.1));
        var width = right - left + 1;
        var cropHeight = bottom - top + 1;
        if (width < 8 || cropHeight < 8)
        {
            return null;
        }

        var values = new float[width * cropHeight];
        for (var y = 0; y < cropHeight; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (((top + y) * frame.Width) + (left + x)) * 4;
                values[(y * width) + x] = Luminance(frame.Bgra[offset + 2], frame.Bgra[offset + 1], frame.Bgra[offset]);
            }
        }

        var sorted = values.ToArray();
        Array.Sort(sorted);
        var background = sorted[sorted.Length / 2];
        var threshold = Math.Max(background + 35, 70);
        var iconLeft = width;
        var iconTop = cropHeight;
        var iconRight = -1;
        var iconBottom = -1;
        for (var y = 0; y < cropHeight; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (values[(y * width) + x] <= threshold)
                {
                    continue;
                }

                iconLeft = Math.Min(iconLeft, x);
                iconTop = Math.Min(iconTop, y);
                iconRight = Math.Max(iconRight, x);
                iconBottom = Math.Max(iconBottom, y);
            }
        }

        if (iconRight < iconLeft || iconBottom < iconTop)
        {
            return null;
        }

        var iconWidth = iconRight - iconLeft + 1;
        var iconHeight = iconBottom - iconTop + 1;
        var ratio = iconWidth / (float)iconHeight;
        if (iconWidth < 6 || iconHeight < 6 || ratio is < 0.3f or > 3.4f)
        {
            // Too small, or a text line slipped into the cell.
            return null;
        }

        return Fit(values, width, iconLeft, iconTop, iconRight, iconBottom);
    }

    /// <summary>Fits the content box into the square signature, keeping the aspect ratio and centring it.</summary>
    private static float[]? Fit(float[] source, int sourceWidth, int left, int top, int right, int bottom)
    {
        var width = right - left + 1;
        var height = bottom - top + 1;
        if (width < 3 || height < 3)
        {
            return null;
        }

        var scale = SampleSize / (float)Math.Max(width, height);
        var fittedWidth = Math.Max(1, (int)Math.Round(width * scale));
        var fittedHeight = Math.Max(1, (int)Math.Round(height * scale));
        var offsetX = (SampleSize - fittedWidth) / 2;
        var offsetY = (SampleSize - fittedHeight) / 2;
        var values = new float[SampleSize * SampleSize];
        for (var y = 0; y < fittedHeight; y++)
        {
            for (var x = 0; x < fittedWidth; x++)
            {
                var sourceX = left + (int)(x / scale);
                var sourceY = top + (int)(y / scale);
                values[((offsetY + y) * SampleSize) + offsetX + x] = source[(sourceY * sourceWidth) + sourceX];
            }
        }

        return Normalize(values);
    }

    private static float Luminance(byte red, byte green, byte blue) => (0.299f * red) + (0.587f * green) + (0.114f * blue);

    /// <summary>Subtracts the mean and divides by the length, so two signatures compare shape, not brightness.</summary>
    private static float[]? Normalize(float[] values)
    {
        var mean = values.Average();
        var sum = 0f;
        for (var index = 0; index < values.Length; index++)
        {
            values[index] -= mean;
            sum += values[index] * values[index];
        }

        if (sum <= 0.0001f)
        {
            return null;
        }

        var length = MathF.Sqrt(sum);
        for (var index = 0; index < values.Length; index++)
        {
            values[index] /= length;
        }

        return values;
    }

    private static float Correlation(float[] left, float[] right)
    {
        var sum = 0f;
        for (var index = 0; index < left.Length; index++)
        {
            sum += left[index] * right[index];
        }

        return sum;
    }
}
