using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Tests;

public sealed class GameOptionParserTests
{
    [Fact]
    public void Reads_resolution_mode_and_ui_scale_from_crlf_text()
    {
        const string text = "windowed = 1\r\nwidth = 3440\r\nheight = 1440\r\nuiScale =  1.00\r\n";

        var parsed = GameOptionParser.TryParse(text, out var settings);

        Assert.True(parsed);
        Assert.NotNull(settings);
        Assert.Equal(3440, settings!.Width);
        Assert.Equal(1440, settings.Height);
        Assert.True(settings.Windowed);
        Assert.Equal(1.0, settings.UiScale);
        Assert.Equal(3440.0 / 1440.0, settings.AspectRatio, precision: 9);
    }

    [Fact]
    public void Missing_width_is_not_usable()
    {
        var parsed = GameOptionParser.TryParse("height = 1080\r\n", out var settings);

        Assert.False(parsed);
        Assert.Null(settings);
    }

    [Fact]
    public void Non_positive_height_is_not_usable()
    {
        var parsed = GameOptionParser.TryParse("width = 1920\r\nheight = 0\r\n", out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Missing_ui_scale_defaults_to_one_and_windowed_defaults_to_false()
    {
        var parsed = GameOptionParser.TryParse("width = 1920\nheight = 1080\n", out var settings);

        Assert.True(parsed);
        Assert.Equal(1.0, settings!.UiScale);
        Assert.False(settings.Windowed);
    }
}
