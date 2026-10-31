using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace BdoComboSetup.Views;

/// <summary>
/// The game screen on the Preview page: background, UIData overlay and the two bars, all in game pixels.
/// A click on a slot selects it. A bar whose position is not verified can be dragged; the position is saved per layout on release.
/// </summary>
public sealed partial class GameScreenCanvas : UserControl
{
    /// <summary>Movement in game pixels before a press becomes a drag.</summary>
    private const double DragThreshold = 4;

    private DragState? _drag;

    public GameScreenCanvas()
    {
        InitializeComponent();
        CooldownBarGrid.Tag = BarKind.Cooldown;
        QuickBarGrid.Tag = BarKind.Quick;
    }

    private void Bar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is not PreviewViewModel viewModel
            || sender is not UIElement { } element
            || element is not FrameworkElement { Tag: BarKind kind })
        {
            return;
        }

        if (!e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(ScreenGrid).Position;
        var bar = kind == BarKind.Cooldown ? viewModel.CooldownBar : viewModel.QuickBar;
        _drag = new DragState(
            kind,
            pointer,
            new Point(pointer.X - bar.Offset.Left, pointer.Y - bar.Offset.Top),
            e.OriginalSource as DependencyObject,
            Moved: false);

        if (bar.IsDraggable)
        {
            element.CapturePointer(e.Pointer);
        }
    }

    private void Bar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is null || DataContext is not PreviewViewModel viewModel)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(ScreenGrid).Position;
        if (!_drag.Moved && Distance(pointer, _drag.Pressed) < DragThreshold)
        {
            return;
        }

        _drag = _drag with { Moved = true };
        viewModel.MoveBarTo(_drag.Kind, pointer.X - _drag.GrabOffset.X, pointer.Y - _drag.GrabOffset.Y);
    }

    private void Bar_PointerReleased(object sender, PointerRoutedEventArgs e) => Finish(sender, select: true);

    private void Bar_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => Finish(sender, select: false);

    private void Finish(object sender, bool select)
    {
        var drag = _drag;
        _drag = null;
        if (drag is null || DataContext is not PreviewViewModel viewModel)
        {
            return;
        }

        if (sender is UIElement element)
        {
            element.ReleasePointerCaptures();
        }

        if (drag.Moved)
        {
            viewModel.CommitBarPosition(drag.Kind);
            return;
        }

        if (select)
        {
            SelectOwner(viewModel, drag.Source);
        }
    }

    /// <summary>Selects the cooldown slot or quick position under the pointer. Walks up from the pressed element.</summary>
    private static void SelectOwner(PreviewViewModel viewModel, DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Tag: SlotViewModel slot })
            {
                viewModel.SelectSlotCommand.Execute(slot);
                return;
            }

            if (current is FrameworkElement { Tag: QuickSlotViewModel quickSlot })
            {
                viewModel.SelectQuickSlotCommand.Execute(quickSlot);
                return;
            }
        }
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>State of one press: the bar, where it started, and the grab point inside the bar.</summary>
    private sealed record DragState(BarKind Kind, Point Pressed, Point GrabOffset, DependencyObject? Source, bool Moved);
}
