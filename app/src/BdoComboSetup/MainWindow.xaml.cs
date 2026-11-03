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

    /// <summary>False until the constructor has restored the menu state: the pane events before that do not save anything.</summary>
    private bool _navPaneReady;

    public MainWindow()
    {
        InitializeComponent();

        var services = App.Services;
        Title = services.Localizer.Get("AppTitle");
        TitleText.Text = Title;
        VersionText.Text = services.Localizer.Format("StatusBar.Version", AppVersion.Current.ToString());

        // The window and its title bar are drawn by the app; the TitleBar control hosts the drag region.
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1440, 900));
        WindowIcon.Apply(AppWindow, AppTitleBar, services.Logger);

        // The logo chip of the title bar replaces the icon image: the taskbar keeps the icon (AppWindow.SetIcon).
        AppTitleBar.IconSource = null;

        // The color theme and the accent are applied before the first page is shown.
        services.Theme.Attach(RootGrid);

        // The menu is folded or expanded as the user left it. Nothing is saved until the constructor has finished.
        Nav.IsPaneOpen = services.Settings.Settings.NavPaneOpen;

        // Without a readable assets.bdoa the modules cannot show icons or combos: the window shows the explanation only.
        if (services.AssetPackLoad.ErrorCode is not null)
        {
            // Only the pane is hidden: collapsing the whole NavigationView would hide the Frame inside it.
            Nav.IsPaneVisible = false;
            Nav.IsPaneToggleButtonVisible = false;
            ContentFrame.Navigate(typeof(AssetPackErrorPage), services.AssetPackLoad);
            services.Logger.Warn("window.asset_pack_error", "Main window shows the asset pack error page", new Dictionary<string, object?>
            {
                ["code"] = services.AssetPackLoad.ErrorCode,
            });
        }
        else if (services.NeedsSetup)
        {
            services.Logger.Info("window.setup", "Main window shows the initial setup");
            ShowSetup(firstRun: true);
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

        _navPaneReady = true;
        services.Logger.Info("window.opened", "Main window shown", new Dictionary<string, object?>
        {
            ["page"] = services.Startup.Page,
        });
    }

    /// <summary>The user expanded the menu: the state is saved (not while the pane is hidden by setup or the error page).</summary>
    private void Nav_PaneOpened(NavigationView sender, object args) => SaveNavPaneState(open: true);

    /// <summary>The user folded the menu: the state is saved (not while the pane is hidden by setup or the error page).</summary>
    private void Nav_PaneClosed(NavigationView sender, object args) => SaveNavPaneState(open: false);

    private void SaveNavPaneState(bool open)
    {
        if (!_navPaneReady || !Nav.IsPaneVisible)
        {
            return;
        }

        App.Services.SettingsView.ChooseNavPaneOpen(open);
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

    /// <summary>
    /// Shows the initial setup page: first run (the navigation is hidden) or from Settings (the navigation stays, and
    /// finishing returns there).
    /// </summary>
    public void ShowSetup(bool firstRun)
    {
        // The pane is hidden, never the whole NavigationView: the Frame that holds the page lives inside it.
        Nav.IsPaneVisible = !firstRun;
        Nav.IsPaneToggleButtonVisible = !firstRun;
        if (firstRun)
        {
            Nav.SelectedItem = null;
        }

        ContentFrame.Navigate(typeof(SetupPage), firstRun);
    }

    /// <summary>Opens the Editor page (the nav item, or the button of the Combos page).</summary>
    public void OpenEditor()
    {
        if (ReferenceEquals(Nav.SelectedItem, PreviewItem))
        {
            if (ContentFrame.CurrentSourcePageType != typeof(PreviewPage))
            {
                ContentFrame.Navigate(typeof(PreviewPage));
            }

            return;
        }

        Nav.SelectedItem = PreviewItem;
    }

    /// <summary>
    /// Leaves the initial setup: the navigation is shown again and the page of the app opens. The settings just saved become
    /// the running ones, so the page does not ask for a restart.
    /// </summary>
    public void LeaveSetup(bool goToSettings)
    {
        App.Services.SettingsView.AdoptRunningConfiguration();
        Nav.IsPaneVisible = true;
        Nav.IsPaneToggleButtonVisible = true;
        var target = goToSettings ? SettingsItem : ApplyItem;
        if (ReferenceEquals(Nav.SelectedItem, target))
        {
            ContentFrame.Navigate(goToSettings ? typeof(SettingsPage) : typeof(ApplyComboPage));
        }
        else
        {
            Nav.SelectedItem = target;
        }
    }

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
