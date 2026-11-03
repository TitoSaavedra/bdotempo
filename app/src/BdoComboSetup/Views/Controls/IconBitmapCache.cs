using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// Turns the bytes of an icon into a <see cref="BitmapImage"/> and keeps the most recent ones, keyed by the SHA-256 of
/// the content (the same icon read twice gives the same bitmap). Bounded: the oldest bitmap is dropped first.
/// Must be used on the UI thread: a BitmapImage is a UI object and <c>SetSourceAsync</c> needs the dispatcher.
/// </summary>
internal static class IconBitmapCache
{
    private const int Capacity = 512;

    private static readonly Dictionary<string, ImageSource> Bitmaps = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    public static async Task<BitmapImage> GetOrCreateAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var key = Convert.ToHexString(SHA256.HashData(bytes));
        if (Bitmaps.TryGetValue(key, out var cached))
        {
            return (BitmapImage)cached;
        }

        var bitmap = new BitmapImage();
        using var memory = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(memory.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        memory.Seek(0);
        await bitmap.SetSourceAsync(memory);

        Remember(key, bitmap);
        return bitmap;
    }

    /// <summary>
    /// The icon painted in one flat color (black or white), keeping its transparency. Used for the class icons, which follow
    /// the theme: black on the light theme, white on the dark one.
    /// </summary>
    public static async Task<ImageSource> GetOrCreateMonochromeAsync(byte[] bytes, bool black)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var key = Convert.ToHexString(SHA256.HashData(bytes)) + (black ? ":black" : ":white");
        if (Bitmaps.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var memory = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(memory.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        memory.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(memory);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var data = pixels.DetachPixelData();

        // A WriteableBitmap holds premultiplied pixels: the color channels are the flat color scaled by the alpha.
        var level = black ? 0 : 255;
        for (var i = 0; i + 3 < data.Length; i += 4)
        {
            var value = (byte)(level * data[i + 3] / 255);
            data[i] = value;
            data[i + 1] = value;
            data[i + 2] = value;
        }

        var bitmap = new WriteableBitmap((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        using (var buffer = bitmap.PixelBuffer.AsStream())
        {
            buffer.Write(data, 0, data.Length);
        }

        bitmap.Invalidate();
        Remember(key, bitmap);
        return bitmap;
    }

    private static void Remember(string key, ImageSource bitmap)
    {
        Bitmaps[key] = bitmap;
        Order.Enqueue(key);
        while (Order.Count > Capacity)
        {
            Bitmaps.Remove(Order.Dequeue());
        }
    }
}
