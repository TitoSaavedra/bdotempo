using System.Globalization;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Game;

/// <summary>Resolution and window mode read from the game's GameOption.txt.</summary>
public sealed record GameDisplaySettings(int Width, int Height, bool Windowed, double UiScale)
{
    /// <summary>Width divided by height. The game canvas keeps this ratio.</summary>
    public double AspectRatio => (double)Width / Height;
}

/// <summary>Parses GameOption.txt, a list of "key = value" lines (CRLF).</summary>
public static class GameOptionParser
{
    public static bool TryParse(string text, out GameDisplaySettings? settings)
    {
        ArgumentNullException.ThrowIfNull(text);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        settings = null;
        if (!TryReadPositiveInt(values, "width", out var width) || !TryReadPositiveInt(values, "height", out var height))
        {
            return false;
        }

        var windowed = values.TryGetValue("windowed", out var windowedText) && windowedText == "1";
        var uiScale = values.TryGetValue("uiScale", out var uiScaleText)
            && double.TryParse(uiScaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale)
            && parsedScale > 0
                ? parsedScale
                : 1.0;

        settings = new GameDisplaySettings(width, height, windowed, uiScale);
        return true;
    }

    private static bool TryReadPositiveInt(IReadOnlyDictionary<string, string> values, string key, out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }
}

/// <summary>Loads GameOption.txt, which the game keeps directly under its data folder (next to UserCache).</summary>
public static class GameOptionLoader
{
    public const string FileName = "GameOption.txt";

    /// <summary>GameOption.txt of a Black Desert data folder: DataRoot\GameOption.txt.</summary>
    public static string PathIn(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return Path.Combine(dataRoot, FileName);
    }

    /// <summary>Returns the settings, or null when the file is missing or has no usable width and height.</summary>
    public static GameDisplaySettings? Load(string path, AppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        // The full path is not logged: it contains the Windows user name.
        using var scope = logger.BeginOperation("game_option.load", new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(path),
        });

        try
        {
            if (!File.Exists(path))
            {
                logger.Warn("game_option.missing", "GameOption.txt was not found; the canvas falls back to 16:9");
                return null;
            }

            if (!GameOptionParser.TryParse(File.ReadAllText(path), out var settings))
            {
                logger.Warn("game_option.invalid", "GameOption.txt has no usable width and height; the canvas falls back to 16:9");
                return null;
            }

            logger.Info("game_option.resolved", "Game display settings read", new Dictionary<string, object?>
            {
                ["width"] = settings!.Width,
                ["height"] = settings.Height,
                ["windowed"] = settings.Windowed,
                ["ui_scale"] = settings.UiScale,
            });
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.Fail(ex);
            return null;
        }
    }
}
