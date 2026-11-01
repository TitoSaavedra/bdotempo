using System.Runtime.InteropServices;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Logging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Format = Vortice.DXGI.Format;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace BdoComboSetup.Capture;

/// <summary>
/// Captures one window with Windows.Graphics.Capture (capture only: the window is read, never written to). Frames are
/// copied to memory at most about five times a second, which is enough for a preview and keeps the GPU and the UI cheap.
/// Blocking calls are not made on the UI thread: frames arrive on the capture thread.
/// </summary>
public sealed partial class WindowsGraphicsCapture(AppLogger logger) : IGameFrameCapture
{
    private readonly object _gate = new();
    private CaptureSession? _session;

    public void Start(nint window, Action<GameScreenFrame> onFrame, Action<string> onEnded)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ArgumentNullException.ThrowIfNull(onEnded);

        lock (_gate)
        {
            StopLocked();

            var item = GraphicsCaptureItem.TryCreateFromWindowId(new Windows.UI.WindowId { Value = (ulong)window })
                ?? throw new InvalidOperationException("Windows.Graphics.Capture does not accept this window.");
            var session = CaptureSession.Create(logger, item, onFrame, onEnded);
            try
            {
                session.Start();
            }
            catch
            {
                session.Dispose();
                throw;
            }

            _session = session;
            logger.Info("capture.started", "Windows.Graphics.Capture started", new Dictionary<string, object?>
            {
                ["width"] = item.Size.Width,
                ["height"] = item.Size.Height,
            });
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    public void Dispose() => Stop();

    private void StopLocked()
    {
        var session = _session;
        _session = null;
        if (session is not null)
        {
            session.Dispose();
            logger.Info("capture.stopped", "Windows.Graphics.Capture stopped");
        }
    }

    /// <summary>One capture: the Direct3D device, the frame pool, the session and the staging texture for readback.</summary>
    private sealed class CaptureSession : IDisposable
    {
        /// <summary>At most one frame copied to memory per interval (about five per second).</summary>
        private const long FrameIntervalMs = 200;

        private static readonly Guid Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        private readonly AppLogger _logger;
        private readonly GraphicsCaptureItem _item;
        private readonly Action<GameScreenFrame> _onFrame;
        private readonly Action<string> _onEnded;
        private readonly object _frameGate = new();
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly IDirect3DDevice _winrtDevice;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _captureSession;
        private ID3D11Texture2D? _staging;
        private SizeInt32 _size;
        private long _lastCopyMs;
        private bool _firstFrameLogged;
        private bool _disposed;

        private CaptureSession(
            AppLogger logger,
            GraphicsCaptureItem item,
            Action<GameScreenFrame> onFrame,
            Action<string> onEnded,
            ID3D11Device device,
            ID3D11DeviceContext context,
            IDirect3DDevice winrtDevice)
        {
            _logger = logger;
            _item = item;
            _onFrame = onFrame;
            _onEnded = onEnded;
            _device = device;
            _context = context;
            _winrtDevice = winrtDevice;
            _size = item.Size;
        }

        public static CaptureSession Create(
            AppLogger logger,
            GraphicsCaptureItem item,
            Action<GameScreenFrame> onFrame,
            Action<string> onEnded)
        {
            var (device, context) = CreateD3DDevice();
            try
            {
                var winrtDevice = CreateWinRTDevice(device);
                return new CaptureSession(logger, item, onFrame, onEnded, device, context, winrtDevice);
            }
            catch
            {
                context.Dispose();
                device.Dispose();
                throw;
            }
        }

        public void Start()
        {
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
            _framePool.FrameArrived += OnFrameArrived;
            _item.Closed += OnItemClosed;
            _captureSession = _framePool.CreateCaptureSession(_item);
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                throw new PlatformNotSupportedException("Windows.Graphics.Capture needs Windows 10 version 2004 or later.");
            }

            _captureSession.IsCursorCaptureEnabled = false;
            TryHideCaptureBorder();
            _captureSession.StartCapture();
        }

        public void Dispose()
        {
            lock (_frameGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _item.Closed -= OnItemClosed;
            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
            }

            _captureSession?.Dispose();
            _framePool?.Dispose();
            _staging?.Dispose();
            _context.Dispose();
            _device.Dispose();
        }

        /// <summary>
        /// Hides the yellow border Windows draws around the captured window. It needs Windows 11 22H2 and the
        /// borderless-capture permission; without them the border stays, the capture keeps working and the reason is
        /// logged once.
        /// </summary>
        private void TryHideCaptureBorder()
        {
            if (_captureSession is null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                return;
            }

            try
            {
                var status = GraphicsCaptureAccess
                    .RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (!string.Equals(status.ToString(), "Allowed", StringComparison.Ordinal))
                {
                    _logger.Warn("capture.border_kept", "The yellow capture border cannot be hidden on this machine", new Dictionary<string, object?>
                    {
                        ["status"] = status.ToString(),
                    });
                    return;
                }

                _captureSession.IsBorderRequired = false;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.Warn("capture.border_kept", "The yellow capture border cannot be hidden on this machine", new Dictionary<string, object?>
                {
                    ["error"] = ex.GetType().Name,
                });
            }
        }

        private void OnItemClosed(GraphicsCaptureItem sender, object args) => EndWith("window_closed");
        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (_frameGate)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    using var frame = sender.TryGetNextFrame();
                    if (frame is null)
                    {
                        return;
                    }

                    var now = Environment.TickCount64;
                    if (now - _lastCopyMs < FrameIntervalMs)
                    {
                        return;
                    }

                    _lastCopyMs = now;
                    if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height)
                    {
                        _size = frame.ContentSize;
                        sender.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                        return;
                    }

                    var pixels = ReadPixels(frame);
                    if (!_firstFrameLogged)
                    {
                        _firstFrameLogged = true;
                        _logger.Info("capture.first_frame", "The first captured frame was read", new Dictionary<string, object?>
                        {
                            ["width"] = _size.Width,
                            ["height"] = _size.Height,
                            ["mean_luminance"] = Math.Round(MeanLuminance(pixels, _size.Width, _size.Height), 2),
                        });
                    }

                    _onFrame(new GameScreenFrame(_size.Width, _size.Height, pixels));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Error("capture.frame_failed", "A captured frame could not be read; the capture ends", ex);
                    EndLocked("frame_failed");
                }
            }
        }

        private void EndWith(string reason)
        {
            lock (_frameGate)
            {
                EndLocked(reason);
            }
        }

        /// <summary>Stops the session and reports the end once. The caller holds the frame gate.</summary>
        private void EndLocked(string reason)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _item.Closed -= OnItemClosed;
            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
            }

            _captureSession?.Dispose();
            _framePool?.Dispose();
            _staging?.Dispose();
            _context.Dispose();
            _device.Dispose();
            _onEnded(reason);
        }

        /// <summary>Copies the frame to CPU memory as BGRA, top row first, stride width x 4.</summary>
        private byte[] ReadPixels(Direct3D11CaptureFrame frame)
        {
            // The projected surface is an IInspectable: the DXGI access is a COM query on its native object (CsWinRT
            // As<T>), not a managed cast. The texture comes back AddRef'd, so the Vortice wrapper owns and releases it.
            var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
            var textureIid = Texture2DIid;
            access.GetInterface(ref textureIid, out var texturePointer);
            using var texture = new ID3D11Texture2D(texturePointer);

            var width = (int)frame.ContentSize.Width;
            var height = (int)frame.ContentSize.Height;
            _staging ??= CreateStaging(width, height);
            if (_staging.Description.Width != width || _staging.Description.Height != height)
            {
                _staging.Dispose();
                _staging = CreateStaging(width, height);
            }

            _context.CopyResource(_staging, texture);
            var mapped = _context.Map(_staging, 0, MapMode.Read, MapFlags.None);
            try
            {
                var rowBytes = width * 4;
                var pixels = new byte[rowBytes * height];
                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(mapped.DataPointer + (row * (nint)mapped.RowPitch), pixels, row * rowBytes, rowBytes);
                }

                return pixels;
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }
        }

        private ID3D11Texture2D CreateStaging(int width, int height) => _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });

        private static (ID3D11Device Device, ID3D11DeviceContext Context) CreateD3DDevice()
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            var result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels,
                out var device,
                out _,
                out var context);
            if (result.Failure || device is null || context is null)
            {
                device?.Dispose();
                context?.Dispose();
                throw new InvalidOperationException($"No Direct3D 11 device could be created ({result.Code:X8}).");
            }

            return (device, context);
        }

        private static IDirect3DDevice CreateWinRTDevice(ID3D11Device device)
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable);
            if (hr < 0)
            {
                throw new InvalidOperationException($"The Direct3D device could not be wrapped for capture ({hr:X8}).");
            }

            return MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
        }
    }

    [LibraryImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    /// <summary>
    /// The DXGI access of a Windows.Graphics.DirectX surface: the texture behind a captured frame. Native signature:
    /// HRESULT GetInterface(REFIID iid, void** p). It derives from IUnknown, so it is declared as such (CsWinRT: invoking
    /// methods of an IInspectable-typed ComImport interface is not supported by the CLR).
    /// </summary>
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        void GetInterface(ref Guid iid, out nint pointer);
    }

    /// <summary>Mean luminance (0-255, Rec. 601) of a sample of the BGRA pixels. Near 0 means a black frame.</summary>
    private static double MeanLuminance(byte[] bgra, int width, int height)
    {
        const int step = 16;
        double sum = 0;
        long count = 0;
        for (var y = 0; y < height; y += step)
        {
            for (var x = 0; x < width; x += step)
            {
                var offset = ((y * width) + x) * 4;
                sum += (0.114 * bgra[offset]) + (0.587 * bgra[offset + 1]) + (0.299 * bgra[offset + 2]);
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }
}
