namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// Zoom and pan math of the game screen canvas. The fit scale makes the frame of the slots fit the viewport;
/// the zoom factor is relative to that fit (1 = fit, 2 = twice as big on screen, 0.1 = the whole screen is far smaller).
/// </summary>
public static class CanvasZoom
{
    /// <summary>The default zoom: the slots fill the viewport (the frame of the slots, see <c>PreviewViewModel.Frame</c>).</summary>
    public const double FitZoom = 1;

    /// <summary>Zooming out below the frame of the slots reaches the whole game screen; this is the lowest zoom.</summary>
    public const double MinZoom = 0.1;

    public const double MaxZoom = 8;

    /// <summary>Zoom change per mouse-wheel notch.</summary>
    public const double StepFactor = 1.1;

    /// <summary>The scale at which a game screen of gameWidth x gameHeight fits the viewport.</summary>
    public static double FitScale(double viewportWidth, double viewportHeight, double gameWidth, double gameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameHeight);
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return 1;
        }

        return Math.Min(viewportWidth / gameWidth, viewportHeight / gameHeight);
    }

    public static double ClampZoom(double zoom) => double.IsNaN(zoom) ? MinZoom : Math.Clamp(zoom, MinZoom, MaxZoom);

    /// <summary>The zoom after the given number of wheel notches (positive zooms in, negative zooms out).</summary>
    public static double Step(double zoom, int notches) => ClampZoom(zoom * Math.Pow(StepFactor, notches));

    /// <summary>Keeps to 0 .. extent - viewport, so the view never leaves the content.</summary>
    public static double ClampOffset(double offset, double extent, double viewport) =>
        Math.Clamp(offset, 0, Math.Max(0, extent - viewport));

    /// <summary>
    /// The scroll offset that keeps the content under the pointer still when the scale changes:
    /// the pointer is at (offset + pointer) in content pixels before the change.
    /// </summary>
    public static double AnchoredOffset(double offset, double pointer, double oldScale, double newScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(oldScale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newScale);
        return ((offset + pointer) * newScale / oldScale) - pointer;
    }
}
