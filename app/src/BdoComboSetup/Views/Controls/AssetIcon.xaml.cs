using System.IO;
using BdoComboSetup.Core.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BdoComboSetup.Views.Controls;

/// <summary>
/// Draws one icon. <see cref="IconSource"/> is a <c>Func&lt;Stream?&gt;</c> from the asset provider (a new stream each call);
/// when it gives no stream, <see cref="Glyph"/> is shown instead. A failed decode is logged and falls back to the glyph.
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

    private int _loadVersion;

    public AssetIcon()
    {
        InitializeComponent();
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
            using var stream = open();
            if (stream is null)
            {
                return;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (version != _loadVersion)
            {
                return;
            }

            IconImage.Source = bitmap;
            IconImage.Visibility = Visibility.Visible;
            GlyphBorder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            App.Services.Logger.Warn("icon.decode_failed", "An icon could not be decoded; the glyph is shown", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name,
            });
        }
    }
}
