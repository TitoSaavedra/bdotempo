using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BdoComboSetup.Tests;

/// <summary>
/// Checks the two .resw files against each other and the Apply keys used by the code: same keys in English and Spanish,
/// every value filled, and every Apply.* key the view models or the Apply page use exists.
/// </summary>
public sealed class ResourceParityTests
{
    private static readonly Regex KeyLiteral = new("\"(Apply\\.[A-Za-z0-9.]+)\"", RegexOptions.CultureInvariant);
    private static readonly Regex UidLiteral = new("x:Uid=\"(Apply\\.[A-Za-z0-9.]+)\"", RegexOptions.CultureInvariant);

    private static string AppRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "BdoComboSetup", "Strings")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The app folder was not found above the test output.");
    }

    private static string Resw(string language) =>
        Path.Combine(AppRoot(), "src", "BdoComboSetup", "Strings", language, "Resources.resw");

    private static IReadOnlyDictionary<string, string> Values(string language)
    {
        var document = XDocument.Load(Resw(language));
        return document.Root!.Elements("data").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static IEnumerable<string> CodeFiles(string relative, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(AppRoot(), relative), pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void English_and_Spanish_have_the_same_keys()
    {
        var english = Values("en-US").Keys.ToHashSet(StringComparer.Ordinal);
        var spanish = Values("es").Keys.ToHashSet(StringComparer.Ordinal);

        Assert.Empty(english.Except(spanish));
        Assert.Empty(spanish.Except(english));
    }

    [Fact]
    public void Every_value_is_filled_in_both_languages()
    {
        foreach (var language in new[] { "en-US", "es" })
        {
            var empty = Values(language).Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).ToList();
            Assert.True(empty.Count == 0, $"{language} has empty values: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void Apply_keys_use_the_Apply_prefix_and_no_underscores()
    {
        var keys = Values("en-US").Keys.Where(key => key.StartsWith("Apply", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.StartsWith("Apply.", key, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_Apply_key_used_by_the_view_models_exists_in_both_languages()
    {
        var english = Values("en-US");
        var spanish = Values("es");
        var used = CodeFiles(Path.Combine("src", "BdoComboSetup.Core", "ViewModels"), "*.cs")
            .SelectMany(path => KeyLiteral.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(used);
        var missingEnglish = used.Where(key => !english.ContainsKey(key)).ToList();
        var missingSpanish = used.Where(key => !spanish.ContainsKey(key)).ToList();
        Assert.Empty(missingEnglish);
        Assert.Empty(missingSpanish);
    }

    [Fact]
    public void Every_x_Uid_on_the_Apply_page_has_its_properties_in_both_languages()
    {
        var english = Values("en-US");
        var spanish = Values("es");
        var xaml = File.ReadAllText(Path.Combine(AppRoot(), "src", "BdoComboSetup", "Views", "ApplyComboPage.xaml"));
        var uids = UidLiteral.Matches(xaml).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

        Assert.NotEmpty(uids);
        foreach (var uid in uids)
        {
            Assert.True(english.Keys.Any(key => key.StartsWith(uid + ".", StringComparison.Ordinal)), $"No English key for {uid}");
            Assert.True(spanish.Keys.Any(key => key.StartsWith(uid + ".", StringComparison.Ordinal)), $"No Spanish key for {uid}");
        }
    }
}
