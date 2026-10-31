using System.IO;
using BdoComboSetup.Core.Assets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BdoComboSetup.Views.Controls;

/// <summary>Draws one slot art file (see <see cref="SlotArt"/>) from the asset provider. A failed decode is logged and draws nothing.</summary>
public sealed partial class SlotArtImage : UserControl
{
    public static readonly DependencyProperty ArtProperty = DependencyProperty.Register(
        nameof(Art),
        typeof(SlotArt),
        typeof(SlotArtImage),
        new PropertyMetadata(SlotArt.QuickSlotBackground, OnArtChanged));

    private int _loadVersion;

    public SlotArtImage()
    {
        InitializeComponent();
    }

    public SlotArt Art
    {
        get => (SlotArt)GetValue(ArtProperty);
        set => SetValue(ArtProperty, value);
    }

    private static void OnArtChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((SlotArtImage)sender).ReloadAsync();

    private async void ReloadAsync()
    {
        var version = ++_loadVersion;
        ArtImage.Source = null;
        try
        {
            using var stream = App.Services.Assets.OpenSlotArt(Art);
            if (stream is null)
            {
                return;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (version == _loadVersion)
            {
                ArtImage.Source = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            App.Services.Logger.Warn("slot_art.decode_failed", "A slot art file could not be decoded; nothing is drawn", new Dictionary<string, object?>
            {
                ["art"] = Art.ToString(),
                ["error"] = ex.GetType().Name,
            });
        }
    }
}
