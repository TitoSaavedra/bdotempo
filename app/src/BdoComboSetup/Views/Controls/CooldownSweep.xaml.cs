using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// The cooldown sweep of a skill icon: <see cref="Ratio"/> is the remaining fraction (0 to 1), drawn as a pie that starts at
/// 12 o'clock and goes clockwise. <see cref="Label"/> is the number drawn in the middle. The geometry is in the nominal
/// 42 x 42 icon space; the slot scales the whole control with its layout transform.
/// </summary>
public sealed partial class CooldownSweep : UserControl
{
    /// <summary>Icon area in game pixels at UI scale 1.</summary>
    private const double IconSize = 42;

    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio),
        typeof(double),
        typeof(CooldownSweep),
        new PropertyMetadata(0.0, OnRatioChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(CooldownSweep),
        new PropertyMetadata(string.Empty, OnLabelChanged));

    public CooldownSweep()
    {
        InitializeComponent();
        Width = IconSize;
        Height = IconSize;
    }

    /// <summary>Remaining fraction of the cooldown, 0 (nothing drawn) to 1 (whole icon dark).</summary>
    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    /// <summary>Number drawn in the middle of the sweep.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private static void OnRatioChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((CooldownSweep)sender).Redraw();

    private static void OnLabelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((CooldownSweep)sender).NumberText.Text = e.NewValue as string ?? string.Empty;

    private void Redraw()
    {
        var ratio = Math.Clamp(Ratio, 0, 1);
        if (ratio <= 0)
        {
            SweepPath.Data = null;
            return;
        }

        const double centre = IconSize / 2;
        const double radius = IconSize / 2;
        if (ratio >= 0.9999)
        {
            SweepPath.Data = new EllipseGeometry
            {
                Center = new Point(centre, centre),
                RadiusX = radius,
                RadiusY = radius,
            };
            return;
        }

        // Angles in radians, measured from 3 o'clock; the sweep starts at 12 o'clock (-90 degrees) and goes clockwise on screen.
        var angle = (-Math.PI / 2) + (2 * Math.PI * ratio);
        var figure = new PathFigure
        {
            StartPoint = new Point(centre, centre),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment { Point = new Point(centre, centre - radius) });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(centre + (radius * Math.Cos(angle)), centre + (radius * Math.Sin(angle))),
            Size = new Size(radius, radius),
            IsLargeArc = ratio > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });

        SweepPath.Data = new PathGeometry { Figures = { figure } };
    }
}
