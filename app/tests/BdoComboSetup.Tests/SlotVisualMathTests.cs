using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

/// <summary>The pure parts of the slot visuals: the sweep frame of a remaining fraction, the remaining time text and the frame of the slots.</summary>
public sealed class SlotVisualMathTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 50)]
    [InlineData(0.6, 59)]
    [InlineData(1.0, 99)]
    [InlineData(2.0, 99)]
    [InlineData(-1.0, 0)]
    [InlineData(double.NaN, 0)]
    public void The_sweep_frame_is_round_of_the_remaining_fraction_times_99(double remaining, int frame)
    {
        Assert.Equal(frame, CooldownSweepFrames.FrameFor(remaining));
    }

    [Fact]
    public void Frame_cells_are_read_row_by_row_in_a_10_by_10_sheet_of_52_pixel_cells()
    {
        Assert.Equal(520, CooldownSweepFrames.SheetSize);
        Assert.Equal((0, 0), CooldownSweepFrames.CellOrigin(0));
        Assert.Equal((468, 0), CooldownSweepFrames.CellOrigin(9));
        Assert.Equal((0, 52), CooldownSweepFrames.CellOrigin(10));
        Assert.Equal((468, 468), CooldownSweepFrames.CellOrigin(99));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(-1)]
    public void Frames_outside_the_sheet_are_refused(int frame)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownSweepFrames.CellOrigin(frame));
    }

    [Theory]
    [InlineData(0, "Preview.Cooldown.Seconds(0)")]
    [InlineData(12, "Preview.Cooldown.Seconds(12)")]
    [InlineData(60, "Preview.Cooldown.Seconds(60)")]
    [InlineData(61, "Preview.Cooldown.Minutes(1)")]
    [InlineData(3599, "Preview.Cooldown.Minutes(59)")]
    [InlineData(3600, "Preview.Cooldown.Minutes(60)")]
    [InlineData(3601, "Preview.Cooldown.Hours(1)")]
    [InlineData(7300, "Preview.Cooldown.Hours(2)")]
    [InlineData(-5, "Preview.Cooldown.Seconds(0)")]
    public void Remaining_time_is_seconds_then_minutes_then_hours_like_the_game(int seconds, string expected)
    {
        Assert.Equal(expected, CooldownTimeText.Format(seconds, new KeyLocalizer()));
    }

    [Fact]
    public void The_frame_of_the_slots_adds_the_margin_and_is_kept_on_the_screen()
    {
        var slots = new[]
        {
            new SlotFrame(100, 200, 50, 50),
            new SlotFrame(1000, 1300, 50, 65),
        };

        var frame = SlotFrame.Around(slots, margin: 12, screenWidth: 3440, screenHeight: 1440);

        Assert.Equal(new SlotFrame(88, 188, 974, 1189), frame);
    }

    [Fact]
    public void The_frame_is_clipped_to_the_screen_edges()
    {
        var frame = SlotFrame.Around([new SlotFrame(5, 5, 50, 50), new SlotFrame(3420, 1420, 50, 50)], 12, 3440, 1440);

        Assert.Equal(new SlotFrame(0, 0, 3440, 1440), frame);
    }

    [Fact]
    public void With_no_slots_the_frame_is_the_whole_screen()
    {
        Assert.Equal(new SlotFrame(0, 0, 3440, 1440), SlotFrame.Around([], 12, 3440, 1440));
    }
}
