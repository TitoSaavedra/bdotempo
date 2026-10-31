using System.Security.Cryptography;
using Microsoft.UI.Xaml.Media.Imaging;
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

    private static readonly Dictionary<string, BitmapImage> Bitmaps = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    public static async Task<BitmapImage> GetOrCreateAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var key = Convert.ToHexString(SHA256.HashData(bytes));
        if (Bitmaps.TryGetValue(key, out var cached))
        {
            return cached;
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

        Bitmaps[key] = bitmap;
        Order.Enqueue(key);
        while (Order.Count > Capacity)
        {
            Bitmaps.Remove(Order.Dequeue());
        }

        return bitmap;
    }
}
