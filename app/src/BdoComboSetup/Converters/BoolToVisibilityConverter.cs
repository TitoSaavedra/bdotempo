using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BdoComboSetup;

/// <summary>Maps true to Visible. ConverterParameter "Invert" maps true to Collapsed instead.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is true;
        if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("BoolToVisibilityConverter is one-way.");
}
