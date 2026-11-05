using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Game;

/// <summary>
/// Reads the UI panels of the four layouts from UserCache\&lt;account&gt;\gamevariable.xml.
/// The file is not a valid XML document (several top-level fragments), so it is read line by line:
/// the General rows are the UIData entries between "&lt;UIData Version=" and the column-0 "&lt;/UIData&gt;",
/// the presets are the UISettingPresetN entries between "&lt;UISettingPreset Version=" and "&lt;/UISettingPreset&gt;".
/// </summary>
public static partial class GameUiLayoutReader
{
    public const string FileName = "gamevariable.xml";

    /// <summary>The file of an account under a Black Desert data folder: DataRoot\UserCache\&lt;account&gt;\gamevariable.xml.</summary>
    public static string PathIn(string dataRoot, string accountFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountFolder);
        return Path.Combine(dataRoot, "UserCache", accountFolder, FileName);
    }

    /// <summary>Parses the file text. Never throws for malformed rows: they are counted and skipped.</summary>
    public static GameUiLayoutSet Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var general = new List<UiPanel>();
        var presetBlocks = new Dictionary<int, List<UiPanel>>();
        var malformed = 0;

        var inGeneral = false;
        var inPresets = false;
        foreach (var line in text.Split('\n').Select(raw => raw.TrimEnd('\r')))
        {
            if (line.StartsWith("<UIData Version=", StringComparison.Ordinal))
            {
                inGeneral = true;
                continue;
            }

            if (line.StartsWith("<UISettingPreset Version=", StringComparison.Ordinal))
            {
                inPresets = true;
                continue;
            }

            if (inGeneral && line.StartsWith("</UIData>", StringComparison.Ordinal))
            {
                inGeneral = false;
                continue;
            }

            if (inPresets && line.StartsWith("</UISettingPreset>", StringComparison.Ordinal))
            {
                inPresets = false;
                continue;
            }

            if (inGeneral)
            {
                if (!GeneralRowPattern().IsMatch(line))
                {
                    continue;
                }

                if (ReadRow(line) is { } row)
                {
                    general.Add(row);
                }
                else
                {
                    malformed++;
                }

                continue;
            }

            if (inPresets)
            {
                var match = PresetRowPattern().Match(line);
                if (!match.Success)
                {
                    continue;
                }

                if (ReadRow(line) is not { } row)
                {
                    malformed++;
                    continue;
                }

                var block = int.Parse(match.Groups["block"].Value, CultureInfo.InvariantCulture);
                if (!presetBlocks.TryGetValue(block, out var rows))
                {
                    presetBlocks[block] = rows = [];
                }

                rows.Add(row);
            }
        }

        var layouts = new Dictionary<GameUiLayoutId, GameUiLayout>
        {
            [GameUiLayoutId.General] = new(GameUiLayoutId.General, general),
        };
        foreach (var (id, block) in GameUiLayoutBlocks.PresetBlockNumbers)
        {
            var rows = presetBlocks.TryGetValue(block, out var found) ? found : [];
            layouts[id] = new GameUiLayout(id, rows);
        }

        return new GameUiLayoutSet(layouts, malformed);
    }

    /// <summary>Reads and parses the file. Returns null when it is missing or unreadable (the error is logged).</summary>
    public static GameUiLayoutSet? Load(string path, AppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        // The full path is not logged: it contains the Windows user name and the account folder.
        using var scope = logger.BeginOperation("ui_layout.load", new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(path),
        });

        try
        {
            if (!File.Exists(path))
            {
                logger.Warn("ui_layout.missing", "gamevariable.xml was not found; the bars use the generic placement");
                return null;
            }

            var set = Parse(File.ReadAllText(path, Encoding.UTF8));
            foreach (var layout in set.Layouts.Values)
            {
                logger.Info("ui_layout.parsed", "UI layout read", new Dictionary<string, object?>
                {
                    ["layout"] = layout.Id.ToString(),
                    ["panels"] = layout.Panels.Count,
                    ["visible_panels"] = layout.Panels.Count(panel => panel.IsShow),
                });
            }

            if (set.MalformedRows > 0)
            {
                logger.Warn("ui_layout.malformed_rows", "Some UIData rows could not be read and were skipped", new Dictionary<string, object?>
                {
                    ["rows"] = set.MalformedRows,
                });
            }

            return set;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            return null;
        }
    }

    private static UiPanel? ReadRow(string line)
    {
        var attributes = ReadAttributes(line);
        if (!TryInt(attributes, "Index", out var index))
        {
            return null;
        }

        var anchor = attributes.TryGetValue("PendingType", out var anchorText)
            && Enum.TryParse<UiAnchor>(anchorText, out var parsedAnchor)
                ? parsedAnchor
                : (UiAnchor?)null;

        return new UiPanel(
            Index: index,
            WindowIndex: TryInt(attributes, "WindowIndex", out var windowIndex) ? windowIndex : null,
            IsShow: attributes.TryGetValue("IsShow", out var isShow) && isShow == "true",
            Anchor: anchor,
            PosX: TryDouble(attributes, "PosX"),
            PosY: TryDouble(attributes, "PosY"),
            SizeX: TryDouble(attributes, "SizeX"),
            SizeY: TryDouble(attributes, "SizeY"),
            RelativeX: TryDouble(attributes, "RelativePosX"),
            RelativeY: TryDouble(attributes, "RelativePosY"));
    }

    private static Dictionary<string, string> ReadAttributes(string line)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in AttributePattern().Matches(line))
        {
            attributes[match.Groups["name"].Value] = match.Groups["value"].Value;
        }

        return attributes;
    }

    private static bool TryInt(Dictionary<string, string> attributes, string name, out int value)
    {
        value = 0;
        return attributes.TryGetValue(name, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static double? TryDouble(Dictionary<string, string> attributes, string name) =>
        attributes.TryGetValue(name, out var text)
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    [GeneratedRegex("^\\s*<UIData\\s+Index=\"-?\\d+\"")]
    private static partial Regex GeneralRowPattern();

    [GeneratedRegex("^\\s*<UISettingPreset(?<block>\\d+)\\s+Index=\"-?\\d+\"")]
    private static partial Regex PresetRowPattern();

    [GeneratedRegex("(?<name>\\w+)=\"(?<value>[^\"]*)\"")]
    private static partial Regex AttributePattern();
}
