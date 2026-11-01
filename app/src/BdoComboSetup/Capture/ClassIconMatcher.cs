using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BdoComboSetup.Capture;

/// <summary>
/// Guesses the class of a character row from the icon the selection screen draws next to the level. The icons of the
/// catalog are decoded once at 16x16 as luminance signatures; the icon of the row is sampled at the same size and compared
/// by normalized correlation. A weak or ambiguous match is reported as "no class": the review table lets the user set it.
/// </summary>
internal sealed class ClassIconMatcher
{
    /// <summary>Sampling size of the signatures.</summary>
    private const int SampleSize = 16;

    /// <summary>A match below this correlation is not trusted.</summary>
    private const float MinimumScore = 0.55f;

    /// <summary>The best class must beat the second one by this margin, or the guess is ambiguous.</summary>
    private const float MinimumMargin = 0.04f;

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

        if (best < MinimumScore || best - second < MinimumMargin)
        {
            _logger.Debug("character_scan.class_weak", "The class icon was not recognized", new Dictionary<string, object?>
            {
                ["score"] = Math.Round(best, 3),
                ["margin"] = Math.Round(best - second, 3),
            });
            return null;
        }

        return bestClass;
    }

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
                ScaledWidth = SampleSize,
                ScaledHeight = SampleSize,
                InterpolationMode = BitmapInterpolationMode.Fant,
            },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return ToSignature(bitmap);
    }

    /// <summary>The luminance of the icon, with the transparency applied, normalized for the correlation.</summary>
    private static float[]? ToSignature(SoftwareBitmap bitmap)
    {
        var bytes = new byte[SampleSize * SampleSize * 4];
        var buffer = new Windows.Storage.Streams.Buffer((uint)bytes.Length);
        bitmap.CopyToBuffer(buffer);
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);

        var values = new float[SampleSize * SampleSize];
        for (var index = 0; index < values.Length; index++)
        {
            var offset = index * 4;
            values[index] = Luminance(bytes[offset + 2], bytes[offset + 1], bytes[offset]) * (bytes[offset + 3] / 255f);
        }

        return Normalize(values);
    }

    /// <summary>The icon of the row, sampled to 16x16. It sits just left of the level text.</summary>
    private static float[]? CropSignature(GameScreenFrame frame, CharacterRowCandidate row)
    {
        var height = (int)Math.Max(1, row.Bottom - row.Top);
        var left = (int)Math.Max(0, row.LevelLeft - (height * 1.25));
        var right = (int)Math.Max(left + 4, row.LevelLeft - 3);
        var top = (int)Math.Max(0, row.Top);
        var bottom = (int)Math.Min(frame.Height, row.Bottom);
        if (right - left < 6 || bottom - top < 6)
        {
            return null;
        }

        var values = new float[SampleSize * SampleSize];
        for (var y = 0; y < SampleSize; y++)
        {
            var sourceY = top + (int)((bottom - top) * (y + 0.5) / SampleSize);
            sourceY = Math.Clamp(sourceY, 0, frame.Height - 1);
            for (var x = 0; x < SampleSize; x++)
            {
                var sourceX = left + (int)((right - left) * (x + 0.5) / SampleSize);
                sourceX = Math.Clamp(sourceX, 0, frame.Width - 1);
                var offset = ((sourceY * frame.Width) + sourceX) * 4;
                values[(y * SampleSize) + x] = Luminance(frame.Bgra[offset + 2], frame.Bgra[offset + 1], frame.Bgra[offset]);
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
