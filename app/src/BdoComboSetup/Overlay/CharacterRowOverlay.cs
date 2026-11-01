using System.Runtime.InteropServices;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Overlay;

/// <summary>
/// The green feedback over the character selection screen: one rounded rectangle per row the scan just read, plus the
/// running count ("12 de 35"). It is a borderless, topmost, click-through window placed over the game's client area and it
/// is never activated, so the player keeps scrolling the game normally. Drawing is layered (per-pixel alpha): the window
/// shows only the boxes and the count, never a background.
/// <para>
/// It reads the game window rectangle and draws over it. It never sends input to the game and never hooks it.
/// </para>
/// </summary>
internal sealed partial class CharacterRowOverlay : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShowNoActivate = 4;
    private const int SwHide = 0;
    private const int NullBrushStockObject = 5;
    private const int PenSolid = 0;
    private const int BkTransparent = 1;
    private const uint DtCenter = 0x0001;
    private const uint DtVCenter = 0x0004;
    private const uint DtSingleLine = 0x0020;
    private const uint UlwAlpha = 0x0002;
    private static readonly nint Topmost = new(-1);

    private readonly AppLogger _logger;
    private nint _hwnd;
    private nint _dib;
    private nint _bits;
    private int _dibWidth;
    private int _dibHeight;
    private int _originX;
    private int _originY;
    private bool _disposed;

    public CharacterRowOverlay(AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Creates the window (once) and places it over the game's client area.</summary>
    public bool Show(nint gameWindow)
    {
        if (_disposed || !ClientRectOf(gameWindow, out var origin, out var width, out var height))
        {
            return false;
        }

        if (_hwnd == nint.Zero)
        {
            _hwnd = CreateWindowExW(
                WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow | WsExTopmost,
                "STATIC",
                string.Empty,
                WsPopup,
                origin.X,
                origin.Y,
                width,
                height,
                nint.Zero,
                nint.Zero,
                GetModuleHandleW(nint.Zero),
                nint.Zero);
            if (_hwnd == nint.Zero)
            {
                _logger.Warn("character_scan.overlay_failed", "The overlay window could not be created");
                return false;
            }

            _logger.Info("character_scan.overlay", "The character scan overlay was created");
        }

        _originX = origin.X;
        _originY = origin.Y;
        SetWindowPos(_hwnd, Topmost, origin.X, origin.Y, width, height, SwpNoActivate | SwpShowWindow);
        ShowWindow(_hwnd, SwShowNoActivate);
        return EnsureBitmap(width, height);
    }

    /// <summary>Draws the boxes of the rows just read and the count, over the game window.</summary>
    /// <param name="frameWidth">Width of the captured frame; the boxes come in its pixels.</param>
    /// <param name="frameHeight">Height of the captured frame.</param>
    public void Draw(
        nint gameWindow,
        IReadOnlyList<CharacterRowCandidate> rows,
        string caption,
        int frameWidth,
        int frameHeight)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (frameWidth <= 0 || frameHeight <= 0 || !Show(gameWindow))
        {
            return;
        }

        var width = _dibWidth;
        var height = _dibHeight;
        unsafe
        {
            new Span<uint>((void*)_bits, width * height).Clear();
        }

        var screen = GetDC(nint.Zero);
        if (screen == nint.Zero)
        {
            return;
        }

        var memory = CreateCompatibleDC(screen);
        if (memory == nint.Zero)
        {
            ReleaseDC(nint.Zero, screen);
            return;
        }

        var oldBitmap = SelectObject(memory, _dib);
        var border = CreatePen(PenSolid, 3, Rgb(0, 240, 80));
        var label = CreateSolidBrush(Rgb(8, 22, 12));
        var font = CreateFontW(-18, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        try
        {
            var oldPen = SelectObject(memory, border);
            var oldFont = SelectObject(memory, font);
            SetBkMode(memory, BkTransparent);
            SetTextColor(memory, Rgb(230, 255, 235));

            var scaleX = width / (double)frameWidth;
            var scaleY = height / (double)frameHeight;
            foreach (var row in rows)
            {
                var left = (int)Math.Round(row.Left * scaleX);
                var top = (int)Math.Round(row.Top * scaleY);
                var right = (int)Math.Round(row.Right * scaleX);
                var bottom = (int)Math.Round(row.Bottom * scaleY);
                SelectObject(memory, GetStockObject(NullBrushStockObject));
                RoundRect(memory, left, top, right, bottom, 12, 12);
            }

            // The running count sits in its own small box at the top of the window.
            var boxLeft = 12;
            var boxTop = 12;
            var boxRight = Math.Min(width - 12, 212);
            var boxBottom = boxTop + 36;
            if (boxRight > boxLeft)
            {
                SelectObject(memory, label);
                RoundRect(memory, boxLeft, boxTop, boxRight, boxBottom, 10, 10);
                var text = new RectInt { Left = boxLeft, Top = boxTop, Right = boxRight, Bottom = boxBottom };
                DrawTextW(memory, caption, -1, ref text, DtCenter | DtVCenter | DtSingleLine);
            }

            SelectObject(memory, oldFont);
            SelectObject(memory, oldPen);
            FixAlpha(width, height);

            var size = new SizeInt { Width = width, Height = height };
            var source = new PointInt { X = 0, Y = 0 };
            var destination = new PointInt { X = _originX, Y = _originY };
            var blend = new BlendFunction
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1,
            };
            UpdateLayeredWindow(_hwnd, screen, ref destination, ref size, memory, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memory, oldBitmap);
            DeleteObject(border);
            DeleteObject(label);
            DeleteObject(font);
            DeleteDC(memory);
            ReleaseDC(nint.Zero, screen);
        }
    }

    public void Hide()
    {
        if (_hwnd != nint.Zero)
        {
            ShowWindow(_hwnd, SwHide);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != nint.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

        if (_dib != nint.Zero)
        {
            DeleteObject(_dib);
            _dib = nint.Zero;
            _bits = nint.Zero;
        }
    }

    /// <summary>Creates the 32-bit top-down bitmap the boxes are drawn on, when the size changed.</summary>
    private bool EnsureBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (_dib != nint.Zero && _dibWidth == width && _dibHeight == height)
        {
            return true;
        }

        if (_dib != nint.Zero)
        {
            DeleteObject(_dib);
            _dib = nint.Zero;
            _bits = nint.Zero;
        }

        var screen = GetDC(nint.Zero);
        if (screen == nint.Zero)
        {
            return false;
        }

        try
        {
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
            };
            var info = new BitmapInfo { Header = header };
            _dib = CreateDIBSection(screen, ref info, 0, out _bits, nint.Zero, 0);
            if (_dib == nint.Zero || _bits == nint.Zero)
            {
                _logger.Warn("character_scan.overlay_bitmap_failed", "The overlay bitmap could not be created");
                return false;
            }

            _dibWidth = width;
            _dibHeight = height;
            return true;
        }
        finally
        {
            ReleaseDC(nint.Zero, screen);
        }
    }

    /// <summary>GDI leaves the alpha byte at zero, so every drawn pixel is made opaque. The rest stays transparent.</summary>
    private unsafe void FixAlpha(int width, int height)
    {
        var pixels = (uint*)_bits;
        var count = width * height;
        for (var index = 0; index < count; index++)
        {
            if ((pixels[index] & 0x00FFFFFFu) != 0)
            {
                pixels[index] |= 0xFF000000u;
            }
        }
    }

    /// <summary>The client area of the game window in screen pixels: its origin and its size.</summary>
    private static bool ClientRectOf(nint window, out PointInt origin, out int width, out int height)
    {
        origin = default;
        width = 0;
        height = 0;
        if (window == nint.Zero || !GetClientRect(window, out var rect))
        {
            return false;
        }

        var point = new PointInt { X = 0, Y = 0 };
        if (!ClientToScreen(window, ref point))
        {
            return false;
        }

        origin = point;
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return width > 0 && height > 0;
    }

    private static uint Rgb(byte red, byte green, byte blue) => (uint)(red | (green << 8) | (blue << 16));

    [StructLayout(LayoutKind.Sequential)]
    private struct PointInt
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeInt
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectInt
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        int extendedStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out RectInt rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint window, ref PointInt point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(
        nint window,
        nint destinationDc,
        ref PointInt destination,
        ref SizeInt size,
        nint sourceDc,
        ref PointInt source,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial nint GetModuleHandleW(nint name);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint graphicsObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint graphicsObject);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreatePen(int style, int width, uint color);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    private static partial nint GetStockObject(int index);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RoundRect(nint dc, int left, int top, int right, int bottom, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial int SetBkMode(nint dc, int mode);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextColor(nint dc, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint charSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string face);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DrawTextW(nint dc, string text, int length, ref RectInt rect, uint format);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(
        nint dc,
        ref BitmapInfo info,
        uint usage,
        out nint bits,
        nint section,
        uint offset);
}
