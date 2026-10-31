using BdoComboSetup.Core.Assets.Pack;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BdoComboSetup.Views;

/// <summary>
/// Explains that assets.bdoa could not be used and what to do. Shown by the main window in place of the modules.
/// The message depends on the error code: a missing file or a damaged one.
/// </summary>
public sealed partial class AssetPackErrorPage : Page
{
    public AssetPackErrorPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not AssetPackLoadResult load || load.ErrorCode is null)
        {
            return;
        }

        var localizer = App.Services.Localizer;
        TitleText.Text = localizer.Get("AssetPack.Error.Title");
        BodyText.Text = localizer.Get(load.ErrorCode == AssetPackErrorCodes.Missing ? "AssetPack.Error.Missing" : "AssetPack.Error.Invalid");
        DetailText.Text = localizer.Format("AssetPack.Error.Detail", load.ErrorCode, load.Path);
    }
}
