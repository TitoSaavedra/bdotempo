namespace BdoComboSetup.Core.Characters;

/// <summary>
/// One character row as the scan read it, with the class guessed from the row icon (null when the guess was not
/// confident, or when the icon catalog could not be loaded).
/// </summary>
public sealed record CharacterRowReading(CharacterRowCandidate Row, int? ClassType);

/// <summary>What merging one page did.</summary>
public enum RowMergeOutcome
{
    /// <summary>The page was placed (by overlap or by the scroll bar) and its new rows were added.</summary>
    Added,

    /// <summary>The page was already known (the player scrolled up, or the list did not move).</summary>
    Confirmed,

    /// <summary>The page could not be placed: nothing was added.</summary>
    Unplaced,

    /// <summary>The account already has every character: nothing more is added.</summary>
    Full,
}

/// <summary>The result of merging one page: what happened, how many rows it added and how many characters are known.</summary>
public readonly record struct RowMergeResult(RowMergeOutcome Outcome, int Added, int Total);

/// <summary>
/// Accumulates the rows of the selection screen while the player scrolls the list, keyed by their position in the list.
/// A page is placed by finding where it overlaps the known rows (a contiguous run of same level and similar name); when
/// there is no overlap it can be placed by the scroll bar fraction, which gives the first visible row. Rows already known
/// are never replaced, so scrolling up and down cannot duplicate a character, and a page that cannot be placed adds
/// nothing. Every later reading of a known row is a vote: the reading seen most often wins (the earliest on a tie), so one
/// OCR slip on a frame taken mid-scroll cannot stick. The list never grows past the number of characters of the account.
/// </summary>
public sealed class CharacterRowAccumulator
{
    private readonly SortedDictionary<int, RowVotes> _known = [];

    /// <summary>The rows read so far, in list order (positions with no reading are simply absent).</summary>
    public IReadOnlyList<CharacterRowReading> Rows => _known.Values.Select(votes => votes.Best).ToList();

    /// <summary>How many characters have a reading. When it equals the account's count, the list is complete.</summary>
    public int Count => _known.Count;

    public void Reset() => _known.Clear();

    /// <summary>
    /// Merges one visible page. <paramref name="expectedCount"/> is the number of characters of the account (0: no limit);
    /// <paramref name="scrollFraction"/> is the scroll bar position (0 at the top, 1 at the bottom), or null.
    /// </summary>
    public RowMergeResult AddPage(
        IReadOnlyList<CharacterRowReading> page,
        int expectedCount,
        double? scrollFraction = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        var limit = expectedCount > 0 ? expectedCount : int.MaxValue;
        var cleaned = Clean(page);
        if (cleaned.Count == 0)
        {
            return new RowMergeResult(RowMergeOutcome.Confirmed, 0, Count);
        }

        if (Count >= limit)
        {
            return new RowMergeResult(RowMergeOutcome.Full, 0, Count);
        }

        var (placement, fromOverlap) = Place(cleaned, limit, scrollFraction);
        if (placement is not { } start || start < 0 || start + cleaned.Count > limit)
        {
            return new RowMergeResult(RowMergeOutcome.Unplaced, 0, Count);
        }

        if (!fromOverlap)
        {
            // A scroll bar placement is only accepted when it does not contradict what is known.
            for (var index = 0; index < cleaned.Count; index++)
            {
                if (_known.TryGetValue(start + index, out var known) && !SameRow(known.Best.Row, cleaned[index].Row))
                {
                    return new RowMergeResult(RowMergeOutcome.Unplaced, 0, Count);
                }
            }
        }

        var added = 0;
        for (var index = 0; index < cleaned.Count; index++)
        {
            var position = start + index;
            if (_known.TryGetValue(position, out var votes))
            {
                // A known row only gets a vote when it is the same row: a fuzzy overlap never rewrites it.
                if (SameRow(votes.Best.Row, cleaned[index].Row))
                {
                    votes.Add(cleaned[index]);
                }

                continue;
            }

            _known[position] = RowVotes.Start(cleaned[index]);
            added++;
        }

        var outcome = Count >= limit
            ? RowMergeOutcome.Full
            : added > 0 ? RowMergeOutcome.Added : RowMergeOutcome.Confirmed;
        return new RowMergeResult(outcome, added, Count);
    }

    /// <summary>
    /// Where the page starts: the overlap first (exact), the scroll bar second (estimated, tried at the estimate and its
    /// neighbours until one does not contradict the known rows). Null when neither works.
    /// </summary>
    private (int? Start, bool FromOverlap) Place(
        List<CharacterRowReading> page,
        int limit,
        double? scrollFraction)
    {
        if (Count > 0)
        {
            var (overlap, pageOffset, rowIndex) = FindBestOverlap(page);
            if (overlap >= MinimumOverlap(page.Count, Count))
            {
                var start = rowIndex - pageOffset;
                if (start >= 0 && start + page.Count <= limit)
                {
                    return (start, true);
                }
            }
        }

        if (scrollFraction is { } fraction && limit != int.MaxValue)
        {
            var estimate = (int)Math.Round(fraction * Math.Max(0, limit - page.Count));
            var best = -1;
            var bestScore = int.MinValue;
            foreach (var delta in new[] { 0, -1, 1, -2, 2 })
            {
                var start = estimate + delta;
                if (start < 0 || start + page.Count > limit)
                {
                    continue;
                }

                var matches = 0;
                var conflicts = 0;
                for (var index = 0; index < page.Count; index++)
                {
                    if (!_known.TryGetValue(start + index, out var known))
                    {
                        continue;
                    }

                    if (SameRow(known.Best.Row, page[index].Row))
                    {
                        matches++;
                    }
                    else
                    {
                        conflicts++;
                    }
                }

                if (conflicts > 0)
                {
                    continue;
                }

                var score = (matches * 10) - Math.Abs(delta);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = start;
                }
            }

            if (best >= 0)
            {
                return (best, false);
            }
        }

        // The first page with no scroll bar: the list is assumed to start at the top.
        if (Count == 0)
        {
            return (0, false);
        }

        return (null, false);
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

    /// <summary>The longest contiguous run of the page that matches known consecutive rows, and where it is.</summary>
    private (int Overlap, int PageOffset, int RowIndex) FindBestOverlap(List<CharacterRowReading> page)
    {
        var best = (Overlap: 0, PageOffset: 0, RowIndex: 0);
        for (var pageOffset = 0; pageOffset < page.Count; pageOffset++)
        {
            foreach (var rowIndex in _known.Keys)
            {
                var count = 0;
                while (pageOffset + count < page.Count
                    && _known.TryGetValue(rowIndex + count, out var known)
                    && SameRow(page[pageOffset + count].Row, known.Best.Row))
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

    /// <summary>
    /// The readings of one list position and how many times each was seen. The name and level are voted as one reading; the
    /// memo is voted on its own, because the small memo text is the line the OCR garbles most.
    /// </summary>
    private sealed class RowVotes(CharacterRowReading first)
    {
        private readonly List<(CharacterRowReading Reading, int Count)> _groups = [(first, 1)];
        private readonly List<(CharacterRowCandidate Row, int Count)> _memos = [];

        /// <summary>Starts a position with its first reading, which already counts as a memo vote.</summary>
        public static RowVotes Start(CharacterRowReading first)
        {
            var votes = new RowVotes(first);
            votes.AddMemo(first.Row);
            return votes;
        }

        /// <summary>The most voted reading (the earliest on a tie), carrying the most voted memo.</summary>
        public CharacterRowReading Best
        {
            get
            {
                var best = _groups[0];
                foreach (var group in _groups)
                {
                    if (group.Count > best.Count)
                    {
                        best = group;
                    }
                }

                if (BestMemo() is not { } memo || string.Equals(memo.Memo, best.Reading.Row.Memo, StringComparison.Ordinal))
                {
                    return best.Reading;
                }

                return best.Reading with
                {
                    Row = best.Reading.Row with
                    {
                        Memo = memo.Memo,
                        MemoLeft = memo.MemoLeft,
                        MemoTop = memo.MemoTop,
                        MemoRight = memo.MemoRight,
                        MemoBottom = memo.MemoBottom,
                    },
                };
            }
        }

        public void Add(CharacterRowReading reading)
        {
            AddMemo(reading.Row);
            for (var index = 0; index < _groups.Count; index++)
            {
                if (SameText(_groups[index].Reading.Row, reading.Row))
                {
                    _groups[index] = (_groups[index].Reading, _groups[index].Count + 1);
                    return;
                }
            }

            _groups.Add((reading, 1));
        }

        private void AddMemo(CharacterRowCandidate row)
        {
            if (string.IsNullOrWhiteSpace(row.Memo))
            {
                return;
            }

            for (var index = 0; index < _memos.Count; index++)
            {
                if (string.Equals(_memos[index].Row.Memo, row.Memo, StringComparison.Ordinal))
                {
                    _memos[index] = (_memos[index].Row, _memos[index].Count + 1);
                    return;
                }
            }

            _memos.Add((row, 1));
        }

        /// <summary>The most seen memo; the earliest on a tie. The text is never judged: players write whatever they want.</summary>
        private CharacterRowCandidate? BestMemo()
        {
            (CharacterRowCandidate Row, int Count)? best = null;
            foreach (var memo in _memos)
            {
                if (best is not { } current || memo.Count > current.Count)
                {
                    best = memo;
                }
            }

            return best?.Row;
        }

        private static bool SameText(CharacterRowCandidate left, CharacterRowCandidate right) =>
            left.Level == right.Level
            && string.Equals(left.Name?.Trim(), right.Name?.Trim(), StringComparison.Ordinal);
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

    /// <summary>True when two texts are the same up to OCR noise (case, punctuation and a slip or two). Used to compare a memo
    /// read on screen with the one in the game's file; it never decides what the text should be.</summary>
    public static bool SimilarText(string? left, string? right)
    {
        var leftText = NormalizeName(left);
        var rightText = NormalizeName(right);
        return leftText.Length > 0 && rightText.Length > 0 && Similar(leftText, rightText);
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
