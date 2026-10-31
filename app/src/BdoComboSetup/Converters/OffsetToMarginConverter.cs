using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BdoComboSetup.Converters;

/// <summary>Turns a game-pixel offset into a left and top margin, so an element sits at that point of its parent grid.</summary>
public sealed class OffsetToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is PixelOffset offset
            ? new Thickness(offset.Left, offset.Top, 0, 0)
            : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("OffsetToMarginConverter is one-way.");
}
