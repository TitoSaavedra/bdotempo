using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace BdoComboSetup.Views;

public sealed partial class PreviewPage : Microsoft.UI.Xaml.Controls.Page
{
    public PreviewPage()
    {
        ViewModel = App.Services.Preview;
        InitializeComponent();

        var localizer = App.Services.Localizer;
        ToolTipService.SetToolTip(LayoutBox, localizer.Get("Preview_LayoutTooltip"));
        ToolTipService.SetToolTip(PanelsToggle, localizer.Get("Preview_ShowPanelsTooltip"));
        ToolTipService.SetToolTip(LiveToggle, localizer.Get("Preview_LiveTooltip"));
    }

    public PreviewViewModel ViewModel { get; }

    /// <summary>Lets the user choose a PNG or JPEG screenshot as the background. The file is read in place, never copied.</summary>
    private async void ChangeBackground_Click(object sender, RoutedEventArgs e)
    {
        var window = App.MainWindow;
        if (window is null)
        {
            App.Services.Logger.Warn("preview.background.no_window", "The main window is not available for the file picker");
            return;
        }

        try
        {
            var picker = new FileOpenPicker(window.AppWindow.Id)
            {
                Title = App.Services.Localizer.Get("Preview_PickBackgroundTitle"),
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail,
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                App.Services.Logger.Debug("preview.background.cancelled", "Background picker closed without a file");
                return;
            }

            ViewModel.SetBackgroundPath(file.Path);
        }
        catch (Exception ex)
        {
            App.Services.Logger.Error("preview.background.pick_failed", "The background file picker failed", ex);
        }
    }

    private void DefaultBackground_Click(object sender, RoutedEventArgs e) => ViewModel.ResetBackground();

    private void ResetBars_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetBarPosition(BarKind.Cooldown);
        ViewModel.ResetBarPosition(BarKind.Quick);
    }
}
