namespace BdoComboSetup.Core.Characters;

/// <summary>
/// One character row as the scan read it, with the class guessed from the row icon (null when the guess was not
/// confident, or when the icon catalog could not be loaded).
/// </summary>
public sealed record CharacterRowReading(CharacterRowCandidate Row, int? ClassType);

/// <summary>
/// Accumulates the rows of the selection screen while the player scrolls the list. The game sends no scroll events, so a
/// page is merged with what was read before by finding where its first row repeats the end of the accumulated list: the
/// rows after the overlap are the new ones. A page that is already known (the player scrolled up) adds nothing.
/// <para>
/// Rows are compared by level and by name with a small tolerance, because the OCR of the same row can differ between two
/// passes ("AhrinSong" / "AhrinS0ng").
/// </para>
/// </summary>
public sealed class CharacterRowAccumulator
{
    private readonly List<CharacterRowReading> _rows = [];

    /// <summary>Every row read so far, in list order.</summary>
    public IReadOnlyList<CharacterRowReading> Rows => _rows;

    public int Count => _rows.Count;

    public void Reset() => _rows.Clear();

    /// <summary>Merges one visible page. Returns how many rows it added (zero when the page was already known).</summary>
    public int AddPage(IReadOnlyList<CharacterRowReading> page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Count == 0)
        {
            return 0;
        }

        if (_rows.Count == 0)
        {
            _rows.AddRange(page);
            return page.Count;
        }

        // The top of the page repeats the bottom of what was read: the new rows start after the overlap.
        for (var start = _rows.Count - 1; start >= 0; start--)
        {
            if (!SameRow(_rows[start].Row, page[0].Row))
            {
                continue;
            }

            var overlap = 0;
            while (overlap < page.Count
                && start + overlap < _rows.Count
                && SameRow(_rows[start + overlap].Row, page[overlap].Row))
            {
                overlap++;
            }

            if (overlap == 0)
            {
                continue;
            }

            if (overlap >= page.Count)
            {
                return 0;
            }

            var added = page.Skip(overlap).ToList();
            _rows.AddRange(added);
            return added.Count;
        }

        // No overlap: the player scrolled further than one page, so every row is new.
        _rows.AddRange(page);
        return page.Count;
    }

    /// <summary>True when the two readings are the same row: same level and a similar name.</summary>
    public static bool SameRow(CharacterRowCandidate left, CharacterRowCandidate right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Level is int leftLevel && right.Level is int rightLevel && leftLevel != rightLevel)
        {
            return false;
        }

        var leftName = NormalizeName(left.Name);
        var rightName = NormalizeName(right.Name);
        if (leftName.Length == 0 || rightName.Length == 0)
        {
            // Without a name the level alone is too weak: two characters can share it.
            return false;
        }

        return Similar(leftName, rightName);
    }

    private static string NormalizeName(string? name) =>
        new((name ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static bool Similar(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        var longest = Math.Max(left.Length, right.Length);
        if (longest == 0)
        {
            return true;
        }

        return Levenshtein(left, right) <= Math.Max(1, longest / 4);
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
