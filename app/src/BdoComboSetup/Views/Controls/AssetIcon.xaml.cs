using System.IO;
using System.Runtime.InteropServices;
using BdoComboSetup.Core.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// Draws one icon. <see cref="IconSource"/> is a <c>Func&lt;Stream?&gt;</c> from the asset provider (a new stream each call);
/// when it gives no stream, <see cref="Glyph"/> is shown instead. The stream is read off the UI thread, then the bytes
/// become a <see cref="BitmapImage"/> through an <c>InMemoryRandomAccessStream</c>, cached by content. A failed decode is
/// logged and falls back to the glyph.
/// </summary>
public sealed partial class AssetIcon : UserControl
{
    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(
        nameof(IconSource),
        typeof(object),
        typeof(AssetIcon),
        new PropertyMetadata(null, OnIconSourceChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(AssetIcon),
        new PropertyMetadata(string.Empty, OnGlyphChanged));

    public static readonly DependencyProperty MonochromeProperty = DependencyProperty.Register(
        nameof(Monochrome),
        typeof(bool),
        typeof(AssetIcon),
        new PropertyMetadata(false, OnIconSourceChanged));

    private int _loadVersion;

    public AssetIcon()
    {
        InitializeComponent();

        // A monochrome icon is black on the light theme and white on the dark one, so it is drawn again when the theme changes.
        ActualThemeChanged += (_, _) =>
        {
            if (Monochrome)
            {
                ReloadAsync();
            }
        };
    }

    /// <summary>Paints the icon in one flat color that follows the theme (black on light, white on dark). Used for the class icons.</summary>
    public bool Monochrome
    {
        get => (bool)GetValue(MonochromeProperty);
        set => SetValue(MonochromeProperty, value);
    }

    /// <summary>A <c>Func&lt;Stream?&gt;</c> that opens the icon. Null or a function that returns null shows the glyph.</summary>
    public object? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    private static void OnIconSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((AssetIcon)sender).ReloadAsync();

    private static void OnGlyphChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((AssetIcon)sender).GlyphText.Text = e.NewValue as string ?? string.Empty;

    private async void ReloadAsync()
    {
        var version = ++_loadVersion;
        GlyphText.Text = Glyph ?? string.Empty;
        IconImage.Source = null;
        IconImage.Visibility = Visibility.Collapsed;
        GlyphBorder.Visibility = Visibility.Visible;

        if (IconSource is not Func<Stream?> open)
        {
            return;
        }

        try
        {
            // Reading the pack entry (decryption, decompression) runs off the UI thread; the bitmap is created on it.
            var bytes = await Task.Run(() => ReadAllBytes(open));
            if (bytes is null || version != _loadVersion)
            {
                return;
            }

            ImageSource bitmap = Monochrome
                ? await IconBitmapCache.GetOrCreateMonochromeAsync(bytes, black: ActualTheme == ElementTheme.Light)
                : await IconBitmapCache.GetOrCreateAsync(bytes);
            if (version != _loadVersion)
            {
                return;
            }

            IconImage.Source = bitmap;
            IconImage.Visibility = Visibility.Visible;
            GlyphBorder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            App.Services.Logger.Warn("icon.decode_failed", "An icon could not be decoded; the glyph is shown", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name,
            });
        }
    }

    private static byte[]? ReadAllBytes(Func<Stream?> open)
    {
        using var stream = open();
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
