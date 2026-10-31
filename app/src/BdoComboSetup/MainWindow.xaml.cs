using BdoComboSetup.Services;
using BdoComboSetup.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace BdoComboSetup;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var services = App.Services;
        Title = services.Localizer.Get("AppTitle");

        // The window and its title bar are drawn by the app; the TitleBar control hosts the drag region.
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1440, 900));

        Nav.SelectedItem = services.Startup.Page switch
        {
            StartupOptions.PreviewPage => PreviewItem,
            StartupOptions.SettingsPage => SettingsItem,
            _ => ApplyItem,
        };
        services.Logger.Info("window.opened", "Main window shown", new Dictionary<string, object?>
        {
            ["page"] = services.Startup.Page,
        });
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag as string;
        Type target = tag switch
        {
            StartupOptions.PreviewPage => typeof(PreviewPage),
            StartupOptions.SettingsPage => typeof(SettingsPage),
            _ => typeof(ApplyComboPage),
        };

        if (ContentFrame.CurrentSourcePageType == target)
        {
            return;
        }

        App.Services.Logger.Debug("navigation.page", "Navigating to page", new Dictionary<string, object?>
        {
            ["page"] = target.Name,
        });
        ContentFrame.Navigate(target);
    }
}
