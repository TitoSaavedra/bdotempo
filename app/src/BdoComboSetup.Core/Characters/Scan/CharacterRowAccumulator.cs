namespace BdoComboSetup.Core.Characters;

/// <summary>
/// One character row as the scan read it, with the class guessed from the row icon (null when the guess was not
/// confident, or when the icon catalog could not be loaded).
/// </summary>
public sealed record CharacterRowReading(CharacterRowCandidate Row, int? ClassType);

/// <summary>What merging one page did.</summary>
public enum RowMergeOutcome
{
    /// <summary>The accumulator was empty: the page became the known list.</summary>
    FirstPage,

    /// <summary>The page was already known (the player scrolled up, or the list did not move).</summary>
    Confirmed,

    /// <summary>Rows were added after the known list.</summary>
    Added,

    /// <summary>The page could not be placed with the known rows: nothing was added.</summary>
    Unplaced,

    /// <summary>The account already has every character: nothing more is added.</summary>
    Full,
}

/// <summary>The result of merging one page: what happened, how many rows it added and the total known.</summary>
public readonly record struct RowMergeResult(RowMergeOutcome Outcome, int Added, int Total);

/// <summary>
/// Accumulates the rows of the selection screen while the player scrolls the list. A page is merged by finding where it
/// overlaps the known rows (a contiguous run of same level and similar name), not by trusting its first row: a page read
/// while scrolling up is recognized as already known, and a page that cannot be placed adds nothing. The list never grows
/// past the number of characters of the account, so a repeated or misread page cannot invent characters.
/// <para>
/// Only rows with a level and a name count; a row whose name is missing is left out (the level alone is not a character).
/// </para>
/// </summary>
public sealed class CharacterRowAccumulator
{
    private readonly List<CharacterRowReading> _rows = [];

    /// <summary>Every row read so far, in list order.</summary>
    public IReadOnlyList<CharacterRowReading> Rows => _rows;

    public int Count => _rows.Count;

    public void Reset() => _rows.Clear();

    /// <summary>Merges one visible page. <paramref name="expectedCount"/> is the number of characters of the account (0: no limit).</summary>
    public RowMergeResult AddPage(IReadOnlyList<CharacterRowReading> page, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(page);
        var limit = expectedCount > 0 ? expectedCount : int.MaxValue;
        var cleaned = Clean(page);
        if (cleaned.Count == 0)
        {
            return new RowMergeResult(RowMergeOutcome.Confirmed, 0, _rows.Count);
        }

        if (_rows.Count == 0)
        {
            var take = Math.Min(cleaned.Count, limit);
            _rows.AddRange(cleaned.Take(take));
            return new RowMergeResult(
                _rows.Count >= limit ? RowMergeOutcome.Full : RowMergeOutcome.FirstPage,
                take,
                _rows.Count);
        }

        if (_rows.Count >= limit)
        {
            return new RowMergeResult(RowMergeOutcome.Full, 0, _rows.Count);
        }

        var (overlap, pageOffset, rowIndex) = FindBestOverlap(cleaned);
        if (overlap < MinimumOverlap(cleaned.Count, _rows.Count))
        {
            return new RowMergeResult(RowMergeOutcome.Unplaced, 0, _rows.Count);
        }

        // The accumulator always starts at the top of the list: a page above it cannot be placed without guessing.
        var start = rowIndex - pageOffset;
        if (start < 0 || start > _rows.Count)
        {
            return new RowMergeResult(RowMergeOutcome.Unplaced, 0, _rows.Count);
        }

        var added = 0;
        for (var index = 0; index < cleaned.Count && _rows.Count < limit; index++)
        {
            var target = start + index;
            if (target < _rows.Count)
            {
                // The row is already known: the first reading is kept, so OCR noise does not rewrite it.
                continue;
            }

            _rows.Add(cleaned[index]);
            added++;
        }

        var outcome = _rows.Count >= limit
            ? RowMergeOutcome.Full
            : added > 0 ? RowMergeOutcome.Added : RowMergeOutcome.Confirmed;
        return new RowMergeResult(outcome, added, _rows.Count);
    }

    /// <summary>Rows that can be matched: a name is required and consecutive duplicates inside the page are dropped.</summary>
    private static List<CharacterRowReading> Clean(IReadOnlyList<CharacterRowReading> page)
    {
        var result = new List<CharacterRowReading>(page.Count);
        foreach (var reading in page)
        {
            if (string.IsNullOrWhiteSpace(reading.Row.Name))
            {
                continue;
            }

            if (result.Count > 0 && SameRow(result[^1].Row, reading.Row))
            {
                continue;
            }

            result.Add(reading);
        }

        return result;
    }

    /// <summary>The longest contiguous run of the page that matches the known rows, and where it is.</summary>
    private (int Overlap, int PageOffset, int RowIndex) FindBestOverlap(List<CharacterRowReading> page)
    {
        var best = (Overlap: 0, PageOffset: 0, RowIndex: 0);
        for (var pageOffset = 0; pageOffset < page.Count; pageOffset++)
        {
            for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
            {
                var count = 0;
                while (pageOffset + count < page.Count
                    && rowIndex + count < _rows.Count
                    && SameRow(page[pageOffset + count].Row, _rows[rowIndex + count].Row))
                {
                    count++;
                }

                if (count > best.Overlap)
                {
                    best = (count, pageOffset, rowIndex);
                }
            }
        }

        return best;
    }

    /// <summary>Two rows are enough to trust an anchor, unless the page or the known list is tiny.</summary>
    private static int MinimumOverlap(int pageCount, int rowCount) =>
        pageCount >= 3 && rowCount >= 3 ? 2 : 1;

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
