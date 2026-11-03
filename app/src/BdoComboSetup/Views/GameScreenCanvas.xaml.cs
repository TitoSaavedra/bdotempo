using Windows.ApplicationModel.DataTransfer;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Search;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Views.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VirtualKey = Windows.System.VirtualKey;

namespace BdoComboSetup.Views;

/// <summary>
/// The game screen on the Preview page: the live capture behind the slots. It polls the capture source once a second while
/// the page is visible (the poll is cheap: it only looks at the window) and stops the capture when the page is left.
/// Frames arrive on the capture thread and are drawn on the UI thread. The view model holds the zoom; this code applies it.
/// </summary>
public sealed partial class GameScreenCanvas : UserControl
{
    /// <summary>How often the window is looked at while the page is visible.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private PreviewViewModel? _viewModel;
    private DispatcherQueueTimer? _pollTimer;
    private WriteableBitmap? _bitmap;
    private long _shownVersion = -1;

    /// <summary>Scroll offsets to restore after the next layout pass, so a wheel zoom keeps the pointer's point still.</summary>
    private (double Horizontal, double Vertical)? _pendingOffset;

    /// <summary>True when the scroll position should move to the view model's scroll target once the new size is laid out.</summary>
    private bool _scrollRequested;

    /// <summary>True while the user presses on the view: a scroll during the press is a pan by hand.</summary>
    private bool _pointerDown;

    /// <summary>True while a pan key (arrows, page, home, end) is held on the view: a scroll then is a pan by hand.</summary>
    private bool _panKeyDown;

    /// <summary>True while the game capture is stopped because the capture switch is off.</summary>
    private bool _capturePaused;

    public GameScreenCanvas()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ScreenHost.LayoutUpdated += OnScreenLayoutUpdated;

        // Handled events too: the scroll bars and the slots handle their own presses, and a pan must still be seen.
        Viewport.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Viewport_PointerDown), handledEventsToo: true);
        Viewport.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Viewport_PointerUp), handledEventsToo: true);
        Viewport.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Viewport_PointerUp), handledEventsToo: true);
        Viewport.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Viewport_KeyDown), handledEventsToo: true);
        Viewport.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(Viewport_KeyUp), handledEventsToo: true);
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        if (_viewModel is not null)
        {
            _viewModel.FrameRequested -= OnFrameRequested;
        }

        _viewModel = args.NewValue as PreviewViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            _viewModel.FrameRequested += OnFrameRequested;
            _scrollRequested = true;
            ApplyScale();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.GameScreen.FrameArrived += OnFrameArrived;
        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = PollInterval;
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
        Poll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_pollTimer is not null)
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= OnPollTick;
            _pollTimer = null;
        }

        if (_viewModel is not null)
        {
            _viewModel.GameScreen.FrameArrived -= OnFrameArrived;
            _viewModel.GameScreen.Stop();
        }

        ShowFrame();
    }

    private void OnPollTick(DispatcherQueueTimer sender, object args) => Poll();

    /// <summary>Looks at the game window (start, keep or stop the capture) and shows the state. Runs on the UI thread.</summary>
    private void Poll()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!_viewModel.CaptureGame)
        {
            // The switch is off: the capture is stopped once and nothing of the game window is read until it is on again.
            if (!_capturePaused)
            {
                _capturePaused = true;
                _viewModel.GameScreen.Stop();
                ShowFrame();
                _viewModel.RefreshCaptureStatus();
            }

            return;
        }

        _capturePaused = false;
        _viewModel.GameScreen.Poll();
        _viewModel.RefreshCaptureStatus();
        ShowFrame();
    }

    /// <summary>A frame arrived on the capture thread: draw it on the UI thread.</summary>
    private void OnFrameArrived(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(ShowFrame);

    /// <summary>
    /// Draws the newest frame, or clears the picture (black) when the source has none. A frame that is already drawn
    /// is not copied again.
    /// </summary>
    private void ShowFrame()
    {
        var source = _viewModel?.GameScreen;
        var frame = source?.LatestFrame;
        if (source is null || frame is null)
        {
            if (LiveImage.Source is not null)
            {
                LiveImage.Source = null;
                _bitmap = null;
                _shownVersion = -1;
            }

            return;
        }

        var version = source.FrameVersion;
        if (version == _shownVersion && _bitmap is not null)
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height);
            LiveImage.Source = _bitmap;
        }

        using (var stream = _bitmap.PixelBuffer.AsStream())
        {
            stream.Position = 0;
            stream.Write(frame.Bgra, 0, frame.Bgra.Length);
        }

        _bitmap.Invalidate();
        _shownVersion = version;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewViewModel.ScreenScale))
        {
            ApplyScale();
        }

        if (e.PropertyName == nameof(PreviewViewModel.CaptureGame))
        {
            Poll();
        }
    }

    /// <summary>Highlights the slot under the pointer, so it is obvious that a cooldown slot can be clicked.</summary>
    private void CooldownSlot_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)Application.Current.Resources["PreviewSlotHoverFillBrush"];
            border.BorderBrush = (Brush)Application.Current.Resources["PreviewSlotHoverBorderBrush"];
            border.BorderThickness = new Thickness(2);
        }
    }

    private void CooldownSlot_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)Resources["SlotIdleFill"];
            border.BorderBrush = (Brush)Application.Current.Resources["PreviewSlotFrameBrush"];
            border.BorderThickness = new Thickness(1);
        }
    }

    /// <summary>
    /// A click on a cooldown slot opens the skill picker of that slot: the searchable list of the class skills at their top
    /// rank (icon and name). Picking one replaces the skill of that slot in the combo the Apply page is editing; the guide is
    /// never touched. Quick slots are not edited here: they follow the quick positions of the combo.
    /// </summary>
    private void CooldownSlot_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_viewModel is not { CanPickSkill: true } viewModel || sender is not FrameworkElement { DataContext: SlotViewModel slot } anchor)
        {
            return;
        }

        e.Handled = true;
        viewModel.ClearSlotEditMessage();
        ShowSkillPicker(anchor, slot, viewModel);
    }

    /// <summary>A slot with a skill starts to be dragged: dropped on another slot the two swap, dropped on the pending list it is emptied.</summary>
    private void CooldownSlot_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var step = sender is FrameworkElement { DataContext: SlotViewModel slot }
            ? App.Services.ApplyCombo.Editor?.StepAtSlot(slot.Number)
            : null;
        if (step is null)
        {
            args.Cancel = true;
            return;
        }

        SlotDrag.Step = step;
        SlotDrag.FromSlot = ((SlotViewModel)((FrameworkElement)sender).DataContext).Number;
        args.Data.SetText(step.SkillName);
    }

    private void CooldownSlot_DragOver(object sender, DragEventArgs e) =>
        e.AcceptedOperation = SlotDrag.Step is not null ? DataPackageOperation.Move : DataPackageOperation.None;

    /// <summary>
    /// A skill dropped on a slot takes it (from the pending list or from another slot). The skill that was in the slot goes to
    /// the slot the dragged one left, or back to the pending list.
    /// </summary>
    private void CooldownSlot_Drop(object sender, DragEventArgs e)
    {
        if (SlotDrag.Step is { } step && sender is FrameworkElement { DataContext: SlotViewModel slot })
        {
            App.Services.ApplyCombo.Editor?.AssignCooldown(step, slot.Number);
        }

        SlotDrag.Clear();
        e.Handled = true;
    }

    private void CooldownSlot_DropCompleted(UIElement sender, DropCompletedEventArgs args) => SlotDrag.Clear();

    /// <summary>Opens the picker of one slot under the slot itself. It is built here because every slot opens its own.</summary>
    private void ShowSkillPicker(FrameworkElement anchor, SlotViewModel slot, PreviewViewModel viewModel)
    {
        var list = new SearchableList
        {
            Width = 320,
            ItemsSource = viewModel.SkillOptions,
            ItemTemplate = (DataTemplate)Resources["SkillPickTemplate"],
        };
        list.SetFields(item => SearchFields.ForSkill((SkillOption)item));

        var flyout = new Flyout { Content = list };
        list.ItemChosen += (_, item) =>
        {
            if (item is SkillOption option)
            {
                viewModel.TryAssignSkill(slot.Number, option);
            }

            flyout.Hide();
            list.Reset();
        };

        flyout.ShowAt(anchor);
    }

    private void ApplyScale()
    {
        if (_viewModel is null)
        {
            return;
        }

        // The render transform scales the game-size grid; the host takes the scaled size so the scroll viewer knows the extent.
        var scale = _viewModel.ScreenScale;
        ScreenTransform.ScaleX = scale;
        ScreenTransform.ScaleY = scale;
        ScreenHost.Width = _viewModel.GameWidth * scale;
        ScreenHost.Height = _viewModel.GameHeight * scale;
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) =>
        _viewModel?.SetViewport(Viewport.ActualWidth, Viewport.ActualHeight);

    /// <summary>
    /// The scroll position changed. When the user is panning (a press on the view, a scroll bar drag, a touch drag or a pan
    /// key), the view is no longer one of the fit modes. Changes the code makes itself (a fit, a resize, a wheel zoom) happen
    /// without a press or a key, so they do not count.
    /// </summary>
    private void Viewport_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_pointerDown || _panKeyDown)
        {
            _viewModel?.EnterCustomView();
        }
    }

    private void Viewport_PointerDown(object sender, PointerRoutedEventArgs e) => _pointerDown = true;

    private void Viewport_PointerUp(object sender, PointerRoutedEventArgs e) => _pointerDown = false;

    private void Viewport_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsPanKey(e.Key))
        {
            _panKeyDown = true;
        }
    }

    private void Viewport_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (IsPanKey(e.Key))
        {
            _panKeyDown = false;
        }
    }

    private static bool IsPanKey(VirtualKey key) =>
        key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
            or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Home or VirtualKey.End;

    /// <summary>Mouse wheel: zooms by notches, keeping the content under the pointer where it is.</summary>
    private void Viewport_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        e.Handled = true;
        var notches = e.GetCurrentPoint(Viewport).Properties.MouseWheelDelta / 120;
        if (notches == 0)
        {
            return;
        }

        var pointer = e.GetCurrentPoint(Viewport).Position;
        var oldScale = _viewModel.ScreenScale;
        var horizontal = Viewport.HorizontalOffset;
        var vertical = Viewport.VerticalOffset;

        _viewModel.ZoomBy(notches);
        var newScale = _viewModel.ScreenScale;
        if (Math.Abs(newScale - oldScale) < 1e-9)
        {
            return;
        }

        _pendingOffset = (
            CanvasZoom.AnchoredOffset(horizontal, pointer.X, oldScale, newScale),
            CanvasZoom.AnchoredOffset(vertical, pointer.Y, oldScale, newScale));
    }

    /// <summary>The view model asks for a fit, a new layout or a new size: apply the scale and move to the scroll target now, or once laid out.</summary>
    private void OnFrameRequested(object? sender, EventArgs e)
    {
        _scrollRequested = true;
        ApplyScale();
        ScreenHost.UpdateLayout();
        ApplyScroll();
    }

    /// <summary>Scrolls to the view model's scroll target for the active fit. Waits until the viewport has a size.</summary>
    private void ApplyScroll()
    {
        if (_viewModel is not { ViewportKnown: true } viewModel)
        {
            return;
        }

        _scrollRequested = false;
        var (horizontal, vertical) = viewModel.ScrollTarget;
        Viewport.ChangeView(horizontal, vertical, null, disableAnimation: true);
    }

    /// <summary>Applies the scroll offset that a wheel zoom asked for, or the frame, once the new size is laid out.</summary>
    private void OnScreenLayoutUpdated(object? sender, object e)
    {
        if (_pendingOffset is { } offset)
        {
            _pendingOffset = null;
            Viewport.ChangeView(offset.Horizontal, offset.Vertical, null, disableAnimation: true);
            return;
        }

        if (_scrollRequested)
        {
            ApplyScroll();
        }
    }
}
