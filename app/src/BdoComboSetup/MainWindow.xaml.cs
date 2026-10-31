using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Services;
using BdoComboSetup.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace BdoComboSetup;

public sealed partial class MainWindow : Window
{
    /// <summary>The update state for the banner. A getter, because the x:Bind bindings are evaluated inside InitializeComponent.</summary>
    public UpdateViewModel Updates => App.Services.Updates;

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

        // Without a readable assets.bdoa the modules cannot show icons or combos: the window shows the explanation only.
        if (services.AssetPackLoad.ErrorCode is not null)
        {
            Nav.Visibility = Visibility.Collapsed;
            ContentFrame.Navigate(typeof(AssetPackErrorPage), services.AssetPackLoad);
            services.Logger.Warn("window.asset_pack_error", "Main window shows the asset pack error page", new Dictionary<string, object?>
            {
                ["code"] = services.AssetPackLoad.ErrorCode,
            });
        }
        else
        {
            Nav.SelectedItem = services.Startup.Page switch
            {
                StartupOptions.PreviewPage => PreviewItem,
                StartupOptions.SettingsPage => SettingsItem,
                _ => ApplyItem,
            };
        }

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

    /// <summary>Closing the banner (its X) is the same as "Later": hidden for this session.</summary>
    private void UpdateBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => Updates.DismissCommand.Execute(null);

    /// <summary>Shows the full release notes of the release in a dialog. Reads only; nothing is installed here.</summary>
    private async void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        var release = Updates.Release;
        if (release is null || Content?.XamlRoot is null)
        {
            return;
        }

        var notes = new TextBlock
        {
            Text = Updates.ReleaseNotes.Length > 0 ? Updates.ReleaseNotes : App.Services.Localizer.Get("Update.Notes.Empty"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Updates.BannerTitle,
            Content = new ScrollViewer { Content = notes, MaxHeight = 420 },
            CloseButtonText = App.Services.Localizer.Get("Update.Dialog.Close"),
            DefaultButton = ContentDialogButton.Close,
        };
        App.Services.Logger.Debug("update.notes.opened", "Release notes dialog opened");
        await dialog.ShowAsync();
    }
}
