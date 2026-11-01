using System.Globalization;
using System.Text.RegularExpressions;

namespace BdoComboSetup.Core.Characters;

/// <summary>One word the screen reader recognized, with its box and its average color in the captured frame.</summary>
public sealed record ScreenTextWord(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    double Red = 0,
    double Green = 0,
    double Blue = 0);

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
    double LevelLeft,
    string? Memo = null);

/// <summary>
/// Reads the rows of the character selection screen out of the words of an OCR pass. The screen shows, one row per
/// character, the class icon, "Nv. NN" and the name. Only rows with a level are kept: the buttons and the labels around the
/// list have none. Rows that touch the top or the bottom of the frame are dropped, because the list scrolls under them.
/// </summary>
public static partial class CharacterScreenRowParser
{
    /// <summary>The highest level the game lets a character reach; anything above is a misread number.</summary>
    private const int MaxLevel = 100;

    [GeneratedRegex(@"^(?:n[vyw]|lv)[\.,:;]?\s*([0-9lIO|]{1,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelWithNumber();

    [GeneratedRegex(@"^(?:n[vyw]|lv)[\.,:;]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelWord();

    [GeneratedRegex(@"^[0-9lIO|]{1,3}$", RegexOptions.CultureInvariant)]
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
        var bands = Bands(words);
        foreach (var band in bands)
        {
            if (ReadBand(band, frameWidth, frameHeight) is { } row)
            {
                rows.Add(row with { Memo = FindMemo(band, bands, row) });
            }
        }

        rows.Sort((left, right) => left.Top.CompareTo(right.Top));
        return MergeOverlapping(FilterHeights(rows));
    }

    /// <summary>
    /// The memo of a row is the warm (orange) line just above its level, such as "FARM" or "KUTUM". Not every character has
    /// one; the gray location line below the name is not a memo. Only color tells them apart, so no text is guessed.
    /// </summary>
    private static string? FindMemo(Band band, List<Band> bands, CharacterRowCandidate row)
    {
        var height = Math.Max(1, row.Bottom - row.Top);
        Band? best = null;
        var bestGap = double.MaxValue;
        foreach (var other in bands)
        {
            if (ReferenceEquals(other, band) || other.Bottom > band.Top - 1 || !other.IsWarm)
            {
                continue;
            }

            var gap = band.Top - other.Bottom;
            if (gap > height * 1.4 || gap >= bestGap || other.Text.Length == 0 || HasLevel(other))
            {
                continue;
            }

            best = other;
            bestGap = gap;
        }

        return best?.Text;
    }

    /// <summary>True when the band holds a level word (it is a character row, not a memo).</summary>
    private static bool HasLevel(Band band) => FindLevel(band.Words.OrderBy(word => word.X).ToList()).Level is not null;

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
        var (level, levelIndex) = FindLevel(words);
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

    /// <summary>The level word and its index in the band, or (null, -1) when the band has no level.</summary>
    private static (int? Level, int Index) FindLevel(List<ScreenTextWord> words)
    {
        for (var index = 0; index < words.Count; index++)
        {
            var text = words[index].Text.Trim();
            var combined = LevelWithNumber().Match(text);
            if (combined.Success && ParseLevel(combined.Groups[1].Value) is { } parsed)
            {
                return (parsed, index);
            }

            if (LevelWord().IsMatch(text) && index + 1 < words.Count)
            {
                var next = words[index + 1].Text.Trim();
                if (NumberWord().IsMatch(next) && ParseLevel(next) is { } following)
                {
                    return (following, index);
                }
            }
        }

        return (null, -1);
    }

    private static int? ParseLevel(string text)    {
        // OCR often reads the 1 of a level as l, I or |, and the 0 as O.
        var normalized = text
            .Replace('l', '1')
            .Replace('I', '1')
            .Replace('|', '1')
            .Replace('O', '0');
        return int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level is > 0 and <= MaxLevel
            ? level
            : null;
    }

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

        public double Top => Words.Min(word => word.Y);

        public double Bottom => Words.Max(word => word.Y + word.Height);

        /// <summary>The band's words joined in reading order (left to right).</summary>
        public string Text => string.Join(" ", Words.OrderBy(word => word.X).Select(word => word.Text.Trim()));

        /// <summary>True when the words are warm (orange/gold): the memos are, the location lines are gray.</summary>
        public bool IsWarm
        {
            get
            {
                var colored = Words.Where(word => word.Red + word.Green + word.Blue > 0).ToList();
                if (colored.Count == 0)
                {
                    return false;
                }

                var red = colored.Average(word => word.Red);
                var green = colored.Average(word => word.Green);
                var blue = colored.Average(word => word.Blue);
                return red >= 100 && red - blue >= 25 && red >= green;
            }
        }

        public void Add(ScreenTextWord word)
        {
            Words.Add(word);
            _center = Words.Average(item => item.Y + (item.Height / 2));
            Height = Words.Max(item => item.Height);
        }
    }
}
