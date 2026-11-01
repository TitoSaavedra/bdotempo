using System.Globalization;
using System.Text.RegularExpressions;

namespace BdoComboSetup.Core.Characters;

/// <summary>One word the screen reader recognized, with its box in the pixels of the captured frame.</summary>
public sealed record ScreenTextWord(string Text, double X, double Y, double Width, double Height);

/// <summary>
/// One row of the character selection screen: the level and the name, with the box to draw over the game. The box starts
/// at the class icon (left of the level) and ends after the name.
/// </summary>
/// <param name="Name">The character name, or null when the row only showed the level.</param>
/// <param name="Level">The level after "Nv." ("Lv." in the English client), or null.</param>
/// <param name="LevelLeft">The left edge of the level text: the class icon sits just left of it.</param>
public sealed record CharacterRowCandidate(
    string? Name,
    int? Level,
    double Left,
    double Top,
    double Right,
    double Bottom,
    double LevelLeft);

/// <summary>
/// Reads the rows of the character selection screen out of the words of an OCR pass. The screen shows, one row per
/// character, the class icon, "Nv. NN" and the name. Only rows with a level are kept: the buttons and the labels around the
/// list have none. Rows that touch the top or the bottom of the frame are dropped, because the list scrolls under them.
/// </summary>
public static partial class CharacterScreenRowParser
{
    /// <summary>The highest level the game lets a character reach; anything above is a misread number.</summary>
    private const int MaxLevel = 100;

    [GeneratedRegex(@"^(?:n[vy]|lv)\.?\s*(\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelWithNumber();

    [GeneratedRegex(@"^(?:n[vy]|lv)\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelWord();

    [GeneratedRegex(@"^\d{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberWord();

    [GeneratedRegex(@"[\p{L}\p{N}]", RegexOptions.CultureInvariant)]
    private static partial Regex HasLetterOrDigit();

    public static IReadOnlyList<CharacterRowCandidate> Read(
        IReadOnlyList<ScreenTextWord> words,
        double frameWidth,
        double frameHeight)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return [];
        }

        var rows = new List<CharacterRowCandidate>();
        foreach (var band in Bands(words))
        {
            if (ReadBand(band, frameWidth, frameHeight) is { } row)
            {
                rows.Add(row);
            }
        }

        rows.Sort((left, right) => left.Top.CompareTo(right.Top));
        return MergeOverlapping(FilterHeights(rows));
    }

    /// <summary>
    /// Drops rows whose height is far from the median: the list rows are all the same height, so a line of another size
    /// (a title, a button) is not a character, even when it happens to carry a level-looking word.
    /// </summary>
    private static List<CharacterRowCandidate> FilterHeights(List<CharacterRowCandidate> rows)
    {
        if (rows.Count < 3)
        {
            return rows;
        }

        var heights = rows.Select(row => row.Bottom - row.Top).OrderBy(height => height).ToList();
        var median = heights[heights.Count / 2];
        if (median <= 0)
        {
            return rows;
        }

        return rows
            .Where(row => row.Bottom - row.Top is var height && height >= median * 0.6 && height <= median * 1.6)
            .ToList();
    }

    /// <summary>Groups the words that share a vertical band (one line of the list).</summary>
    private static List<Band> Bands(IReadOnlyList<ScreenTextWord> words)
    {
        var bands = new List<Band>();
        foreach (var word in words
            .Where(word => !string.IsNullOrWhiteSpace(word.Text) && word.Width > 0 && word.Height > 0)
            .OrderBy(word => word.Y + (word.Height / 2)))
        {
            var center = word.Y + (word.Height / 2);
            var band = bands.FirstOrDefault(item =>
                Math.Abs(center - item.Center) <= Math.Max(6, Math.Min(item.Height, word.Height) * 0.6));
            if (band is null)
            {
                bands.Add(new Band(word));
            }
            else
            {
                band.Add(word);
            }
        }

        return bands;
    }

    private static CharacterRowCandidate? ReadBand(Band band, double frameWidth, double frameHeight)
    {
        var words = band.Words.OrderBy(word => word.X).ToList();
        int? level = null;
        var levelIndex = -1;
        for (var index = 0; index < words.Count; index++)
        {
            var text = words[index].Text.Trim();
            var combined = LevelWithNumber().Match(text);
            if (combined.Success && ParseLevel(combined.Groups[1].Value) is { } parsed)
            {
                level = parsed;
                levelIndex = index;
                break;
            }

            if (LevelWord().IsMatch(text) && index + 1 < words.Count)
            {
                var next = words[index + 1].Text.Trim();
                if (NumberWord().IsMatch(next) && ParseLevel(next) is { } following)
                {
                    level = following;
                    levelIndex = index;
                    break;
                }
            }
        }

        if (level is null || levelIndex < 0)
        {
            return null;
        }

        var top = band.Words.Min(word => word.Y);
        var bottom = band.Words.Max(word => word.Y + word.Height);
        if (top <= 1 || bottom >= frameHeight - 1 || bottom - top < 6)
        {
            // The row is cut by the edge of the frame: the user is between two scroll positions.
            return null;
        }

        var name = string.Join(" ", words.Skip(levelIndex + 1).Select(word => word.Text.Trim())).Trim();
        if (name.Length == 0 || !HasLetterOrDigit().IsMatch(name))
        {
            // A level without a name is not a character: it is the list header or a partially read row.
            return null;
        }

        var levelLeft = words[levelIndex].X;
        var right = band.Words.Max(word => word.X + word.Width);
        var iconWidth = Math.Max(1, bottom - top);
        var left = Math.Max(0, levelLeft - (iconWidth * 1.15));
        return new CharacterRowCandidate(
            name,
            level,
            left,
            Math.Max(0, top - 2),
            Math.Min(frameWidth, right + 8),
            Math.Min(frameHeight, bottom + 2),
            levelLeft);
    }

    private static int? ParseLevel(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level is > 0 and <= MaxLevel
            ? level
            : null;

    /// <summary>
    /// One row can arrive split in two bands (the level and the name at slightly different heights). When their boxes
    /// overlap, the one with the name is kept.
    /// </summary>
    private static List<CharacterRowCandidate> MergeOverlapping(List<CharacterRowCandidate> rows)
    {
        var merged = new List<CharacterRowCandidate>();
        foreach (var row in rows)
        {
            var index = merged.FindIndex(item => Overlaps(item, row));
            if (index < 0)
            {
                merged.Add(row);
                continue;
            }

            var current = merged[index];
            merged[index] = (row.Name?.Length ?? 0) > (current.Name?.Length ?? 0) ? row : current;
        }

        return merged;
    }

    private static bool Overlaps(CharacterRowCandidate left, CharacterRowCandidate right)
    {
        var top = Math.Max(left.Top, right.Top);
        var bottom = Math.Min(left.Bottom, right.Bottom);
        var overlap = Math.Max(0, bottom - top);
        var smallest = Math.Min(left.Bottom - left.Top, right.Bottom - right.Top);
        return smallest > 0 && overlap / smallest > 0.6;
    }

    private sealed class Band
    {
        private double _center;

        public Band(ScreenTextWord word)
        {
            Add(word);
        }

        public List<ScreenTextWord> Words { get; } = [];

        public double Center => _center;

        public double Height { get; private set; }

        public void Add(ScreenTextWord word)
        {
            Words.Add(word);
            _center = Words.Average(item => item.Y + (item.Height / 2));
            Height = Words.Max(item => item.Height);
        }
    }
}
