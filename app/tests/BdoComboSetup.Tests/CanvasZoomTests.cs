using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

public sealed class CanvasZoomTests
{
    [Fact]
    public void Fit_scale_fits_the_game_screen_inside_the_viewport_keeping_the_aspect_ratio()
    {
        // 3440 x 1440 in a 1720 x 1000 viewport: width limits (0.5), height would allow 0.694.
        Assert.Equal(0.5, CanvasZoom.FitScale(1720, 1000, 3440, 1440), precision: 9);

        // Tall viewport: height limits.
        Assert.Equal(1000.0 / 1440.0, CanvasZoom.FitScale(3440, 1000, 3440, 1440), precision: 9);
    }

    [Fact]
    public void Fit_scale_is_one_before_the_viewport_has_a_size()
    {
        Assert.Equal(1, CanvasZoom.FitScale(0, 0, 3440, 1440));
    }

    [Fact]
    public void Zoom_never_goes_below_fit_or_above_the_maximum()
    {
        Assert.Equal(CanvasZoom.MinZoom, CanvasZoom.ClampZoom(0.2));
        Assert.Equal(CanvasZoom.MaxZoom, CanvasZoom.ClampZoom(100));
        Assert.Equal(CanvasZoom.MinZoom, CanvasZoom.ClampZoom(double.NaN));
        Assert.Equal(CanvasZoom.MinZoom, CanvasZoom.Step(CanvasZoom.MinZoom, -5));
    }

    [Fact]
    public void Each_wheel_notch_multiplies_the_zoom_by_the_step_factor()
    {
        Assert.Equal(CanvasZoom.StepFactor, CanvasZoom.Step(1, 1), precision: 9);
        Assert.Equal(2 / CanvasZoom.StepFactor, CanvasZoom.Step(2, -1), precision: 9);
        Assert.Equal(CanvasZoom.StepFactor * CanvasZoom.StepFactor, CanvasZoom.Step(1, 2), precision: 9);
    }

    [Fact]
    public void Offset_is_clamped_to_the_scrollable_range()
    {
        Assert.Equal(0, CanvasZoom.ClampOffset(-20, extent: 1000, viewport: 400));
        Assert.Equal(600, CanvasZoom.ClampOffset(900, extent: 1000, viewport: 400));
        Assert.Equal(0, CanvasZoom.ClampOffset(50, extent: 300, viewport: 400));
    }

    [Fact]
    public void Zooming_keeps_the_content_under_the_pointer_still()
    {
        // Content point under the pointer: (offset + pointer) / oldScale. It must stay the same after the zoom.
        const double offset = 120;
        const double pointer = 300;
        const double oldScale = 0.5;
        const double newScale = 1.0;

        var newOffset = CanvasZoom.AnchoredOffset(offset, pointer, oldScale, newScale);

        Assert.Equal((offset + pointer) / oldScale, (newOffset + pointer) / newScale, precision: 9);
    }
}
