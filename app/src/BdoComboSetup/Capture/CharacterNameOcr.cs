using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BdoComboSetup.Capture;

/// <summary>One OCR pass: the rows of the list, the scroll bar fraction (null when not found) and whether the selection screen is open.</summary>
internal sealed record CharacterOcrPage(
    IReadOnlyList<CharacterRowReading> Rows,
    double? ScrollFraction,
    bool IsSelectionScreen);

/// <summary>
/// Reads the visible rows of the character selection screen with the Windows OCR engine. It only runs while the scan is
/// active: the game window is captured (never written to), the frame is recognized and the words are grouped into rows.
/// The frame also gives the scroll bar position and whether the wide "enter the game" bar is visible, without OCR.
/// The engine is created once; a machine without any OCR language reports it and the scan does not start.
/// </summary>
internal sealed class CharacterNameOcr
{
    /// <summary>The languages tried first, in order: the game text is Spanish (LATAM) or English.</summary>
    private static readonly string[] PreferredLanguages = ["es-MX", "es-ES", "es", "en-US", "en-GB", "en"];

    private readonly OcrEngine _engine;

    private CharacterNameOcr(OcrEngine engine)
    {
        _engine = engine;
    }

    /// <summary>The OCR engine, or null when Windows has no recognizer language installed (logged).</summary>
    public static CharacterNameOcr? TryCreate(AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            var engine = ChooseEngine();
            if (engine is null)
            {
                logger.Warn("character_scan.ocr_missing", "Windows has no OCR language installed; the scan cannot read the names");
                return null;
            }

            logger.Info("character_scan.ocr_ready", "OCR engine ready for the scan", new Dictionary<string, object?>
            {
                ["language"] = engine.RecognizerLanguage.LanguageTag,
            });
            return new CharacterNameOcr(engine);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error("character_scan.ocr_failed", "The OCR engine could not be created", ex);
            return null;
        }
    }

    /// <summary>Recognizes one captured frame: the character rows, the scroll bar position and the selection screen check.</summary>
    public async Task<CharacterOcrPage> ReadAsync(
        GameScreenFrame frame,
        ClassIconMatcher? classIcons)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var buffer = CryptographicBuffer.CreateFromByteArray(frame.Bgra);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            frame.Width,
            frame.Height,
            BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);

        var words = new List<ScreenTextWord>();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                words.Add(new ScreenTextWord(
                    word.Text,
                    word.BoundingRect.X,
                    word.BoundingRect.Y,
                    word.BoundingRect.Width,
                    word.BoundingRect.Height));
            }
        }

        var rows = CharacterScreenRowParser.Read(words, frame.Width, frame.Height);
        var readings = new List<CharacterRowReading>(rows.Count);
        foreach (var row in rows)
        {
            readings.Add(new CharacterRowReading(row, classIcons?.Match(frame, row)));
        }

        return new CharacterOcrPage(
            readings,
            CharacterScreenVisuals.ScrollFraction(frame, rows),
            CharacterScreenVisuals.IsSelectionScreen(frame));
    }

    private static OcrEngine? ChooseEngine()
    {
        var available = OcrEngine.AvailableRecognizerLanguages;
        foreach (var tag in PreferredLanguages)
        {
            var language = available.FirstOrDefault(item => string.Equals(item.LanguageTag, tag, StringComparison.OrdinalIgnoreCase));
            if (language is not null && OcrEngine.TryCreateFromLanguage(language) is { } engine)
            {
                return engine;
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }
}
