using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

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

    public GameScreenCanvas()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ScreenHost.LayoutUpdated += OnScreenLayoutUpdated;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = args.NewValue as PreviewViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
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

    /// <summary>Applies the scroll offset that a wheel zoom asked for, once the new size is laid out.</summary>
    private void OnScreenLayoutUpdated(object? sender, object e)
    {
        if (_pendingOffset is not { } offset)
        {
            return;
        }

        _pendingOffset = null;
        Viewport.ChangeView(offset.Horizontal, offset.Vertical, null, disableAnimation: true);
    }
}
