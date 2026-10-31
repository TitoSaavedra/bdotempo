using System.Text.RegularExpressions;

namespace BdoComboSetup.Core.Updates;

/// <summary>
/// A version number in SemVer 2.0 form: <c>MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]</c>. A leading "v" is accepted (Git tags
/// such as "v0.2.0"). Build metadata is parsed and ignored for precedence, as the SemVer specification says.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    // The official SemVer 2.0 regular expression (semver.org), with an optional leading "v".
    private static readonly Regex Pattern = new(
        @"^[vV]?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)" +
        @"(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?" +
        @"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.CultureInvariant);

    private readonly string[] _preRelease;

    private SemanticVersion(int major, int minor, int patch, string[] preRelease, string? build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _preRelease = preRelease;
        Build = build;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The pre-release identifiers ("beta", "1"), or an empty list for a stable version.</summary>
    public IReadOnlyList<string> PreRelease => _preRelease;

    /// <summary>The build metadata ("+build.7"), or null. It does not change the precedence.</summary>
    public string? Build { get; }

    public bool IsPreRelease => _preRelease.Length > 0;

    public static SemanticVersion Parse(string text)
    {
        return TryParse(text, out var version)
            ? version!
            : throw new FormatException($"'{text}' is not a SemVer 2.0 version.");
    }

    /// <summary>Parses a version. Returns false (and no exception) for any text that is not SemVer.</summary>
    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = Pattern.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var preRelease = match.Groups[4].Success
            ? match.Groups[4].Value.Split('.')
            : [];
        var build = match.Groups[5].Success ? match.Groups[5].Value : null;
        version = new SemanticVersion(
            int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
            preRelease,
            build);
        return true;
    }

    /// <summary>
    /// SemVer precedence: numbers first, then pre-releases (a pre-release is lower than the stable version), identifier by
    /// identifier. Numeric identifiers compare as numbers and rank below alphanumeric ones; a longer list of equal
    /// identifiers ranks higher.
    /// </summary>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = Major.CompareTo(other.Major);
        if (core != 0)
        {
            return core;
        }

        core = Minor.CompareTo(other.Minor);
        if (core != 0)
        {
            return core;
        }

        core = Patch.CompareTo(other.Patch);
        if (core != 0)
        {
            return core;
        }

        if (!IsPreRelease || !other.IsPreRelease)
        {
            // A stable version is higher than any pre-release of the same numbers.
            return IsPreRelease ? -1 : other.IsPreRelease ? 1 : 0;
        }

        var count = Math.Min(_preRelease.Length, other._preRelease.Length);
        for (var i = 0; i < count; i++)
        {
            var result = CompareIdentifier(_preRelease[i], other._preRelease[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    /// <summary>Version without the "v" prefix and without build metadata, for example "0.2.0-beta.1".</summary>
    public override string ToString()
    {
        var text = $"{Major}.{Minor}.{Patch}";
        return IsPreRelease ? $"{text}-{string.Join('.', _preRelease)}" : text;
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = IsNumeric(left);
        var rightNumeric = IsNumeric(right);
        if (leftNumeric && rightNumeric)
        {
            // Numeric identifiers have no leading zeros (SemVer), so a longer one is a larger number.
            var byLength = left.Length.CompareTo(right.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static bool IsNumeric(string identifier) => identifier.All(char.IsAsciiDigit);
}
