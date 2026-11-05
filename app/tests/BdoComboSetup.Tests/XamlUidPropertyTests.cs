using System.Xml.Linq;

namespace BdoComboSetup.Tests;

/// <summary>
/// Guards the x:Uid convention. Every resource key "Page.Element.Property" of a XAML element with x:Uid "Page/Element" must name
/// a property that element really has, so a key such as "Preview.HiddenToggle.Tooltip" (a property ToggleSwitch does not have,
/// which made the XAML parser throw) fails the build instead of the app. Both languages must define every x:Uid.
/// </summary>
public sealed class XamlUidPropertyTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The properties each element type may take from x:Uid, as WinUI exposes them. Attached properties are written with their
    /// owner type ("ToolTipService.ToolTip"). Add a type here when a new element uses x:Uid; the test names the missing type.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProperties = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["TextBlock"] = ["Text"],
        ["Button"] = ["Content"],
        ["HyperlinkButton"] = ["Content"],
        ["CheckBox"] = ["Content"],
        ["NavigationViewItem"] = ["Content"],
        ["ToggleSwitch"] = ["Header", "OnContent", "OffContent"],
        ["ComboBox"] = ["Header", "PlaceholderText"],
        ["TextBox"] = ["Header", "PlaceholderText"],
        ["InfoBar"] = ["Title", "Message"],
        ["TitleBar"] = ["Title", "Subtitle"],
        ["Border"] = [],
        ["ProgressBar"] = [],
        ["SelectorBarItem"] = ["Text"],
        ["MenuFlyoutItem"] = ["Text"],
    };

    /// <summary>Attached and automation properties any element may take through x:Uid.</summary>
    private static readonly string[] CommonProperties = ["ToolTipService.ToolTip", "AutomationProperties.Name", "AutomationProperties.HelpText"];

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

    private static IReadOnlySet<string> Keys(string language)
    {
        var path = Path.Combine(AppRoot(), "src", "BdoComboSetup", "Strings", language, "Resources.resw");
        return XDocument.Load(path).Root!.Elements("data").Select(element => element.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<XElement> UidElements()
    {
        var views = Path.Combine(AppRoot(), "src", "BdoComboSetup");
        var files = Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        foreach (var file in files)
        {
            foreach (var element in XDocument.Load(file).Descendants().Where(element => element.Attribute(XamlNamespace + "Uid") is not null))
            {
                yield return element;
            }
        }
    }

    [Fact]
    public void Every_x_Uid_element_type_is_listed_with_its_properties()
    {
        var types = UidElements().Select(element => element.Name.LocalName).Distinct(StringComparer.Ordinal).ToList();

        Assert.NotEmpty(types);
        Assert.DoesNotContain(types, type => !AllowedProperties.ContainsKey(type));
    }

    [Fact]
    public void Every_resource_key_of_an_x_Uid_names_a_property_of_its_element()
    {
        var english = Keys("en-US");
        var problems = new List<string>();
        foreach (var element in UidElements())
        {
            var uid = element.Attribute(XamlNamespace + "Uid")!.Value;
            var prefix = uid.Replace('/', '.') + ".";
            var allowed = AllowedProperties.GetValueOrDefault(element.Name.LocalName, []).Concat(CommonProperties).ToHashSet(StringComparer.Ordinal);
            foreach (var key in english.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var property = key[prefix.Length..];
                if (!allowed.Contains(property))
                {
                    problems.Add($"{uid} ({element.Name.LocalName}): {key} names '{property}', which the element does not have");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_x_Uid_has_at_least_one_key_in_both_languages()
    {
        var english = Keys("en-US");
        var spanish = Keys("es");
        var uids = UidElements().Select(element => element.Attribute(XamlNamespace + "Uid")!.Value).Distinct(StringComparer.Ordinal).ToList();

        Assert.NotEmpty(uids);
        foreach (var uid in uids)
        {
            var prefix = uid.Replace('/', '.') + ".";
            Assert.True(english.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)), $"No English key for {uid}");
            Assert.True(spanish.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)), $"No Spanish key for {uid}");
        }
    }
}
