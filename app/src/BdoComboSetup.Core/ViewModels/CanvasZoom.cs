namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// How the game screen is fitted to the viewport. Custom is what a manual zoom or pan leaves behind: no fit is selected then.
/// </summary>
public enum CanvasFitMode
{
    /// <summary>The whole game screen (gameWidth x gameHeight) fits the viewport, centred, keeping its aspect.</summary>
    Window,

    /// <summary>The frame of the slots fits the viewport (the default, and the fit button).</summary>
    Slots,

    /// <summary>The game at 1:1 (game pixels on screen pixels); the view scrolls and starts on the slots.</summary>
    Real,

    /// <summary>Zoomed or panned by hand: none of the fit modes is selected.</summary>
    Custom,
}

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

    /// <summary>The scale of the game screen at 1:1: one game pixel is one screen pixel.</summary>
    public const double RealScale = 1;

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

    /// <summary>The zoom (relative to the frame fit) that shows <paramref name="scale"/> on screen.</summary>
    public static double ZoomFor(double scale, double fitScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fitScale);
        return scale / fitScale;
    }

    /// <summary>
    /// The zoom range of the mouse wheel. The lowest zoom always reaches the whole game screen and the 1:1 size, even when
    /// those are smaller than <see cref="MinZoom"/> (a very small frame of slots); the highest keeps them reachable too.
    /// </summary>
    public static (double Min, double Max) WheelRange(double windowZoom, double realZoom) => (
        Math.Min(MinZoom, Math.Min(windowZoom, realZoom)),
        Math.Max(MaxZoom, Math.Max(windowZoom, realZoom)));

    public static double ClampZoom(double zoom) => ClampZoom(zoom, MinZoom, MaxZoom);

    public static double ClampZoom(double zoom, double minZoom, double maxZoom) =>
        double.IsNaN(zoom) ? minZoom : Math.Clamp(zoom, minZoom, maxZoom);

    /// <summary>The zoom after the given number of wheel notches (positive zooms in, negative zooms out).</summary>
    public static double Step(double zoom, int notches) => Step(zoom, notches, MinZoom, MaxZoom);

    /// <summary>The zoom after the given number of wheel notches, kept within <paramref name="minZoom"/> .. <paramref name="maxZoom"/>.</summary>
    public static double Step(double zoom, int notches, double minZoom, double maxZoom) =>
        ClampZoom(zoom * Math.Pow(StepFactor, notches), minZoom, maxZoom);

    /// <summary>Keeps to 0 .. extent - viewport, so the view never leaves the content.</summary>
    public static double ClampOffset(double offset, double extent, double viewport) =>
        Math.Clamp(offset, 0, Math.Max(0, extent - viewport));

    /// <summary>The scroll offset that puts the content point <paramref name="target"/> in the middle of the viewport.</summary>
    public static double CenteredOffset(double target, double extent, double viewport) =>
        ClampOffset(target - (viewport / 2), extent, viewport);

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
