using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BdoComboSetup.Views;

/// <summary>
/// The Preview page: translates clicks into view model calls. It shows the combo that the Apply page is editing, so it
/// makes sure that page has its guide loaded (the call is idempotent). The live capture runs only while the page is shown.
/// </summary>
public sealed partial class PreviewPage : Page
{
    public PreviewPage()
    {
        ViewModel = App.Services.Preview;
        InitializeComponent();

        var localizer = App.Services.Localizer;
        ToolTipService.SetToolTip(LayoutBox, localizer.Get("Preview.Layout.Tooltip"));
        ToolTipService.SetToolTip(HiddenToggle, localizer.Get("Preview.Hidden.Tooltip"));
    }

    public PreviewViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var startup = App.Services.Startup;
        try
        {
            await App.Services.ApplyCombo.InitializeAsync(startup.ApplyClass, startup.ApplySpec);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Services.Logger.Error("preview.page.initialize_failed", "The Preview page could not load the combo of the Apply page", ex);
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => ViewModel.FitToView();
}
