using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The Spanish text of the English prose of the bdoclasshub guides (<c>guide-texts-es.json</c>: English to Spanish).
/// The bundle may leave it out; then <see cref="None"/> is used and every guide stays in English.
/// </summary>
public sealed class GuideTexts
{
    /// <summary>No translations: every text stays English.</summary>
    public static readonly GuideTexts None = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly IReadOnlyDictionary<string, string> _spanish;

    private GuideTexts(IReadOnlyDictionary<string, string> spanish)
    {
        _spanish = spanish;
    }

    /// <summary>Reads the dictionary once. A missing or invalid file is logged and gives <see cref="None"/>.</summary>
    public static GuideTexts Load(IEmbeddedResources resources, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(logger);

        using var scope = logger.BeginOperation("combo.guide_texts.load");
        using var stream = resources.Open(BundleResourceNames.GuideTexts);
        if (stream is null)
        {
            logger.Warn("combo.guide_texts.missing", "The bundle has no guide-texts-es.json; the guides stay in English");
            return None;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string?>>(stream, Options) ?? new Dictionary<string, string?>();
            var spanish = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (english, text) in raw)
            {
                if (!string.IsNullOrWhiteSpace(english) && !string.IsNullOrWhiteSpace(text))
                {
                    spanish[english] = text;
                }
            }

            logger.Info("combo.guide_texts.loaded", "Guide texts read", new Dictionary<string, object?>
            {
                ["entries"] = spanish.Count,
            });
            return new GuideTexts(spanish);
        }
        catch (JsonException ex)
        {
            scope.Fail(ex);
            logger.Error("combo.guide_texts.invalid", "guide-texts-es.json is not valid JSON; the guides stay in English", ex);
            return None;
        }
    }

    /// <summary>The translation of one guide load. Only a Spanish culture translates; any other gives the English texts.</summary>
    public GuideTranslation ForCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return new GuideTranslation(_spanish, culture.TwoLetterISOLanguageName == "es");
    }
}

/// <summary>
/// The translation of one guide load, used by the view models that show guide prose. <see cref="Text"/> returns the Spanish
/// text for a Spanish page and the English text otherwise, or when the dictionary has no entry. <see cref="MissingCount"/>
/// counts the distinct English texts of a Spanish page that had no Spanish one, for the log of the load.
/// </summary>
public sealed class GuideTranslation
{
    private readonly IReadOnlyDictionary<string, string> _spanish;
    private readonly bool _spanishPage;
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);

    internal GuideTranslation(IReadOnlyDictionary<string, string> spanish, bool spanishPage)
    {
        _spanish = spanish;
        _spanishPage = spanishPage;
    }

    public int MissingCount => _missing.Count;

    /// <summary>The text in the page language. Null and blank texts come back as they are.</summary>
    [return: NotNullIfNotNull(nameof(english))]
    public string? Text(string? english)
    {
        if (!_spanishPage || string.IsNullOrWhiteSpace(english))
        {
            return english;
        }

        if (_spanish.TryGetValue(english, out var spanish))
        {
            return spanish;
        }

        _missing.Add(english);
        return english;
    }
}
