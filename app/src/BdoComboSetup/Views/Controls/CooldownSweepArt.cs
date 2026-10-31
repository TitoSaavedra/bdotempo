using System.IO;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Logging;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// The 100 frames of the game's radial cooldown sweep. The sheet is decoded once (premultiplied BGRA, as the WriteableBitmap
/// expects), and each frame is cut out of it the first time it is shown. Used on the UI thread only.
/// </summary>
internal static class CooldownSweepArt
{
    private static Task<byte[]?>? _sheet;
    private static readonly WriteableBitmap?[] Frames = new WriteableBitmap?[CooldownSweepFrames.FrameCount];

    /// <summary>The decoded sheet, or null when the bundle has no sheet (the caller then draws the procedural sweep).</summary>
    public static Task<byte[]?> SheetAsync(IAssetProvider assets, AppLogger logger) => _sheet ??= DecodeAsync(assets, logger);

    /// <summary>One frame as an image of 52 x 52 pixels. Cached after the first call.</summary>
    public static WriteableBitmap Frame(byte[] sheet, int frame)
    {
        var cached = Frames[frame];
        if (cached is not null)
        {
            return cached;
        }

        var (x0, y0) = CooldownSweepFrames.CellOrigin(frame);
        const int cell = CooldownSweepFrames.CellSize;
        const int sheetRowBytes = CooldownSweepFrames.SheetSize * 4;
        const int cellRowBytes = cell * 4;

        var bitmap = new WriteableBitmap(cell, cell);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            for (var y = 0; y < cell; y++)
            {
                stream.Write(sheet, ((y0 + y) * sheetRowBytes) + (x0 * 4), cellRowBytes);
            }
        }

        bitmap.Invalidate();
        Frames[frame] = bitmap;
        return bitmap;
    }

    private static async Task<byte[]?> DecodeAsync(IAssetProvider assets, AppLogger logger)
    {
        using var stream = assets.OpenSlotArt(SlotArt.CooldownSweep);
        if (stream is null)
        {
            logger.Info("slot_art.sweep_missing", "The cooldown sweep art is not in the bundle; the procedural sweep is drawn");
            return null;
        }

        try
        {
            var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            if (decoder.PixelWidth != CooldownSweepFrames.SheetSize || decoder.PixelHeight != CooldownSweepFrames.SheetSize)
            {
                logger.Warn("slot_art.sweep_size", "The cooldown sweep art has an unexpected size; the procedural sweep is drawn", new Dictionary<string, object?>
                {
                    ["width"] = decoder.PixelWidth,
                    ["height"] = decoder.PixelHeight,
                });
                return null;
            }

            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            return pixels.DetachPixelData();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            logger.Warn("slot_art.sweep_decode_failed", "The cooldown sweep art could not be decoded; the procedural sweep is drawn", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name,
            });
            return null;
        }
    }
}
