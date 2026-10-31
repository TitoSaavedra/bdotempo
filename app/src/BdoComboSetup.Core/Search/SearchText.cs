using System.Globalization;
using System.Text;

namespace BdoComboSetup.Core.Search;

/// <summary>
/// The one matching rule of every searchable selector: case-insensitive and accent-insensitive, "contains" on each field
/// (for example a character name, its class in Spanish and in English, a skill name or a skill number). An empty query
/// matches everything. Pure: no UI, no files.
/// </summary>
public static class SearchText
{
    /// <summary>
    /// The form that is compared: accents removed, lower case, single spaces, no leading or trailing space.
    /// Invalid UTF-16 (a lone surrogate) is replaced, never thrown on.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var clean = string.Concat(text.EnumerateRunes().Select(rune => rune.ToString()));
        var decomposed = clean.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    /// <summary>True when the query is empty, or its normalized form is contained in one of the normalized fields.</summary>
    public static bool Matches(string? query, IEnumerable<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var needle = Normalize(query);
        if (needle.Length == 0)
        {
            return true;
        }

        foreach (var field in fields)
        {
            if (Normalize(field).Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The items that match the query, in their original order.</summary>
    /// <param name="fields">The searchable texts of one item (names in each language, numbers, ...).</param>
    public static IReadOnlyList<T> Filter<T>(IEnumerable<T> items, string? query, Func<T, IEnumerable<string?>> fields)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(fields);
        return items.Where(item => Matches(query, fields(item))).ToList();
    }
}
