using BdoComboSetup.Core.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BdoComboSetup.Converters;

/// <summary>
/// Turns a file path (background picture or skill icon) into an image source. Null or a missing file gives null,
/// so the caller shows its fallback. A file the decoder cannot read is logged, not thrown.
/// </summary>
public sealed class FilePathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage(new Uri(path));
        image.ImageFailed += (_, args) => App.Services.Logger.Warn("image.decode_failed", "An image could not be decoded", new Dictionary<string, object?>
        {
            ["extension"] = Path.GetExtension(path),
            ["error"] = args.ErrorMessage,
        });
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("FilePathToImageConverter is one-way.");
}
