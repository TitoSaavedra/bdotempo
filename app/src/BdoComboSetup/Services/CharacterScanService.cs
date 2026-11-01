using BdoComboSetup.Capture;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Overlay;
using Microsoft.UI.Dispatching;

namespace BdoComboSetup.Services;

/// <summary>
/// Runs the character name scan: it captures the game window (read only), runs the OCR about once a second, feeds the rows
/// to <see cref="ViewModel"/> and keeps the green overlay over the game up to date. The player scrolls the list; the scan
/// only reads. Everything happens on the UI thread, except the capture itself (its frames arrive on the capture thread).
/// <para>
/// Nothing here touches the game: no input is sent, no window is moved and no game file is written. Saving the reviewed
/// names is <see cref="CharacterScanViewModel.Save"/>, which writes only the app's own characters.json.
/// </para>
/// </summary>
public sealed class CharacterScanService : IDisposable
{
    /// <summary>How often the game window is looked at and the capture is kept alive.</summary>
    private const int PollIntervalMs = 500;

    /// <summary>At most one OCR pass per interval: recognizing a 3440x1440 frame is not free.</summary>
    private const int OcrIntervalMs = 1100;

    private readonly AppLogger _logger;
    private readonly IReadOnlyList<ClassChoice> _classes;
    private readonly GameScreenSource _screen;
    private readonly IGameWindowLocator _locator;
    private readonly CharacterRowOverlay _overlay;
    private readonly DispatcherQueue _dispatcher;
    private CharacterNameOcr? _ocr;
    private ClassIconMatcher? _classIcons;
    private DispatcherQueueTimer? _timer;
    private nint _window;
    private long _lastFrameVersion;
    private long _lastOcrMs;
    private bool _busy;
    private bool _selectionChecked;
    private bool _disposed;

    public CharacterScanService(
        AppLogger logger,
        ICharacterSource characters,
        IReadOnlyList<ClassChoice> classes,
        ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(localizer);

        _logger = logger;
        _classes = classes;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The character scan must be created on the UI thread.");
        _locator = new Win32GameWindowLocator(logger);
        _screen = new GameScreenSource(_locator, new WindowsGraphicsCapture(logger), logger);
        _overlay = new CharacterRowOverlay(logger);
        ViewModel = new CharacterScanViewModel(characters, classes, localizer, logger);
    }

    /// <summary>The scan state the Settings page shows: the count, the messages and the review table.</summary>
    public CharacterScanViewModel ViewModel { get; }

    public bool IsRunning => ViewModel.IsScanning;

    /// <summary>
    /// Prepares the roster, checks the game window and the OCR engine, then starts reading. False (with the reason on the
    /// page) when there is no account, the game is not at a usable window, or Windows has no OCR language.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        if (_disposed)
        {
            return false;
        }

        if (ViewModel.IsScanning)
        {
            return true;
        }

        if (!ViewModel.Prepare())
        {
            return false;
        }

        var window = _locator.Find();
        if (window is null)
        {
            _logger.Info("character_scan.no_window", "The scan found no game window");
            ViewModel.ReportFailure("Characters.ScanGameMissing");
            return false;
        }

        if (window.IsMinimized)
        {
            _logger.Info("character_scan.minimized", "The scan found the game window minimized");
            ViewModel.ReportFailure("Characters.ScanGameMinimized");
            return false;
        }

        _ocr ??= CharacterNameOcr.TryCreate(_logger);
        if (_ocr is null)
        {
            ViewModel.ReportFailure("Characters.ScanOcrMissing");
            return false;
        }

        try
        {
            _classIcons ??= await ClassIconMatcher.CreateAsync(_classes, _logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("character_scan.class_icons_failed", "The class icons could not be prepared; classes stay unknown", ex);
            _classIcons = null;
        }

        _window = window.Handle;
        if (!_overlay.Show(_window))
        {
            ViewModel.ReportFailure("Characters.ScanOverlayFailed");
            return false;
        }

        ViewModel.BeginScan();
        _lastFrameVersion = 0;
        _lastOcrMs = 0;
        _selectionChecked = false;
        _screen.Poll();
        EnsureTimer().Start();
        _logger.Info("character_scan.started", "Character scan capture started", new Dictionary<string, object?>
        {
            ["frame"] = _screen.State.ToString(),
        });
        return true;
    }

    /// <summary>The player is done: stops the capture and builds the review table (or reports the count mismatch).</summary>
    public void Finish()
    {
        StopCapture("finished");
        ViewModel.Finish();
    }

    /// <summary>Drops the scan: stops the capture and forgets every row.</summary>
    public void Cancel()
    {
        StopCapture("cancelled");
        ViewModel.Discard();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCapture("disposed");
        _overlay.Dispose();
        _screen.Dispose();
    }

    private DispatcherQueueTimer EnsureTimer()
    {
        if (_timer is not null)
        {
            return _timer;
        }

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(PollIntervalMs);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        return _timer;
    }

    private async void OnTick(DispatcherQueueTimer sender, object args) => await PumpAsync();

    /// <summary>One poll: keeps the capture alive and runs an OCR pass when a new frame is ready.</summary>
    private async Task PumpAsync()
    {
        if (_disposed || !ViewModel.IsScanning)
        {
            return;
        }

        try
        {
            _screen.Poll();
            if (_busy)
            {
                return;
            }

            var window = _locator.Find();
            if (window is null)
            {
                StopCapture("game_closed");
                ViewModel.ReportFailure("Characters.ScanGameMissing");
                return;
            }

            if (window.IsMinimized)
            {
                _overlay.Hide();
                return;
            }

            _window = window.Handle;
            if (_screen.State == GameScreenState.Failed)
            {
                StopCapture("capture_failed");
                ViewModel.ReportFailure("Characters.ScanCaptureFailed");
                return;
            }

            var frame = _screen.LatestFrame;
            var version = _screen.FrameVersion;
            var now = Environment.TickCount64;
            if (frame is null
                || version == _lastFrameVersion
                || now - _lastOcrMs < OcrIntervalMs
                || _ocr is null)
            {
                return;
            }

            _busy = true;
            _lastFrameVersion = version;
            _lastOcrMs = now;
            try
            {
                var page = await _ocr.ReadAsync(frame, _classIcons);
                if (!ViewModel.IsScanning)
                {
                    return;
                }

                if (!_selectionChecked)
                {
                    _selectionChecked = true;
                    if (!page.IsSelectionScreen)
                    {
                        StopCapture("not_selection");
                        ViewModel.ReportFailure("Characters.ScanNotSelectionScreen");
                        return;
                    }
                }

                ViewModel.AddPage(page.Rows, page.ScrollFraction);
                _overlay.Draw(
                    _window,
                    page.Rows.Select(reading => reading.Row).ToList(),
                    ViewModel.CountText,
                    frame.Width,
                    frame.Height);
            }
            finally
            {
                _busy = false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("character_scan.failed", "The character scan stopped because of an error", ex);
            StopCapture("failed");
            ViewModel.ReportFailure("Characters.ScanFailed");
        }
    }

    private void StopCapture(string reason)
    {
        _timer?.Stop();
        try
        {
            _screen.Stop();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn("character_scan.stop_failed", "The character scan capture could not be stopped", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name,
            });
        }

        _overlay.Hide();
        if (ViewModel.IsScanning)
        {
            ViewModel.Stop();
            _logger.Info("character_scan.stopped", "Character scan capture stopped", new Dictionary<string, object?>
            {
                ["reason"] = reason,
            });
        }
    }
}
