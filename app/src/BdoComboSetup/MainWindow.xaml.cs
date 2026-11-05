using System.ComponentModel;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Services;
using BdoComboSetup.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace BdoComboSetup;

public sealed partial class MainWindow : Window
{
    /// <summary>The update state for the banner. A getter, because the x:Bind bindings are evaluated inside InitializeComponent.</summary>
    public UpdateViewModel Updates => App.Services.Updates;

    /// <summary>True while a tab is selected from code after a navigation: its SelectionChanged is not a click, so it does nothing.</summary>
    private bool _syncingTabs;

    public MainWindow()
    {
        InitializeComponent();

        var services = App.Services;
        Title = services.Localizer.Get("AppTitle");

        // The Settings button takes its accessible name and tooltip from the Settings resource (the same text as before).
        var settingsLabel = services.Localizer.Get("NavSettingsItem.Content");
        ToolTipService.SetToolTip(SettingsButton, settingsLabel);
        AutomationProperties.SetName(SettingsButton, settingsLabel);

        // The game state of the status bar: a process lookup every few seconds on the UI thread, never a capture.
        GameDetectedText.Text = services.Localizer.Get("StatusBar.GameDetected");
        GameNotDetectedText.Text = services.Localizer.Get("StatusBar.GameNotDetected");
        services.GameDetection.DetectionChanged += (_, _) => ShowGameState();
        services.GameDetection.Poll();
        ShowGameState();
        var gameTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        gameTimer.Interval = TimeSpan.FromSeconds(3);
        gameTimer.Tick += (_, _) => services.GameDetection.Poll();
        gameTimer.Start();
        Closed += (_, _) => gameTimer.Stop();

        // The window and its title bar are drawn by the app; the TitleBar control hosts the drag region.
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        // One bar: the tall title bar makes the caption buttons, the gear and the tabs share the same height and center line.
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1440, 900));
        WindowIcon.Apply(AppWindow, AppTitleBar, services.Logger);

        // The logo chip of the title bar replaces the icon image: the taskbar keeps the icon (AppWindow.SetIcon).
        AppTitleBar.IconSource = null;

        // The color theme and the accent are applied before the first page is shown.
        services.Theme.Attach(RootGrid, AppWindow);

        // Without a readable assets.bdoa the modules cannot show icons or combos: the window shows the explanation only.
        if (services.AssetPackLoad.ErrorCode is not null)
        {
            Navigate(typeof(AssetPackErrorPage), services.AssetPackLoad);
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
            ShowPage(services.Startup.Page switch
            {
                StartupOptions.PreviewPage => typeof(PreviewPage),
                StartupOptions.SettingsPage => typeof(SettingsPage),
                _ => typeof(ApplyComboPage),
            });
        }

        services.Logger.Info("window.opened", "Main window shown", new Dictionary<string, object?>
        {
            ["page"] = services.Startup.Page,
        });
    }

    /// <summary>Shows the game state of the status bar: the green dot when the game process exists, the muted one otherwise.</summary>
    private void ShowGameState()
    {
        var detected = App.Services.GameDetection.IsDetected == true;
        GameDetectedGroup.Visibility = detected ? Visibility.Visible : Visibility.Collapsed;
        GameNotDetectedGroup.Visibility = detected ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows the Editor page (the Preview page): the combos are written from there.</summary>
    public void ShowPreview() => ShowPage(typeof(PreviewPage));

    /// <summary>Shows the Combos page (the character is chosen there).</summary>
    public void ShowCombos() => ShowPage(typeof(ApplyComboPage));

    /// <summary>
    /// A tab of the title bar is clicked: Combos or Editor. A tab that is already shown does nothing (ShowPage returns early); the
    /// tab selected from code after a navigation is ignored.
    /// </summary>
    private void MainTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncingTabs)
        {
            return;
        }

        if (ReferenceEquals(sender.SelectedItem, CombosTab))
        {
            ShowCombos();
        }
        else if (ReferenceEquals(sender.SelectedItem, EditorTab))
        {
            ShowPreview();
        }
    }

    /// <summary>
    /// The Settings gear of the title bar: opens Settings. Settings is a third destination, so while it is shown the click does nothing.
    /// </summary>
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowPage(typeof(SettingsPage));

    /// <summary>Closing the banner (its X) is the same as "Later": hidden for this session.</summary>
    private void UpdateBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => Updates.DismissCommand.Execute(null);

    /// <summary>
    /// Shows the initial setup page: first run, or from Settings (finishing returns there). The setup has no tabs and no Settings gear.
    /// </summary>
    public void ShowSetup(bool firstRun) => Navigate(typeof(SetupPage), firstRun);

    /// <summary>
    /// Leaves the initial setup: the page of the app opens. The settings just saved become the running ones, so the page does
    /// not ask for a restart.
    /// </summary>
    public void LeaveSetup(bool goToSettings)
    {
        App.Services.SettingsView.AdoptRunningConfiguration();
        ShowPage(goToSettings ? typeof(SettingsPage) : typeof(ApplyComboPage));
    }

    /// <summary>Navigates to a page, unless the frame already shows it.</summary>
    private void ShowPage(Type page)
    {
        if (ContentFrame.CurrentSourcePageType == page)
        {
            return;
        }

        App.Services.Logger.Debug("navigation.page", "Navigating to page", new Dictionary<string, object?>
        {
            ["page"] = page.Name,
        });
        Navigate(page);
    }

    /// <summary>Navigates the content frame, then brings the title bar in line with the page that is now shown.</summary>
    private void Navigate(Type page, object? parameter = null)
    {
        if (parameter is null)
        {
            ContentFrame.Navigate(page);
        }
        else
        {
            ContentFrame.Navigate(page, parameter);
        }

        ShowNavigationState();
    }

    /// <summary>
    /// Brings the title bar in line with the shown page. The tabs and the gear are hidden on the setup and the asset pack error page;
    /// otherwise the tab of Combos or Editor is selected, or the gear while Settings is shown. The context and the Apply button belong to
    /// Combos and Editor, and the combo state is watched only while one of those two is shown.
    /// </summary>
    private void ShowNavigationState()
    {
        var page = ContentFrame.CurrentSourcePageType;
        var hasNavigation = page != typeof(SetupPage) && page != typeof(AssetPackErrorPage);
        MainTabs.Visibility = hasNavigation ? Visibility.Visible : Visibility.Collapsed;
        SettingsSlot.Visibility = hasNavigation ? Visibility.Visible : Visibility.Collapsed;

        _syncingTabs = true;
        try
        {
            MainTabs.SelectedItem = page == typeof(ApplyComboPage) ? CombosTab
                : page == typeof(PreviewPage) ? EditorTab
                : null;
        }
        finally
        {
            _syncingTabs = false;
        }

        // The gear is marked while Settings is shown: accent glyph and the small accent underline.
        var settingsShown = page == typeof(SettingsPage);
        SettingsUnderline.Visibility = settingsShown ? Visibility.Visible : Visibility.Collapsed;
        if (settingsShown)
        {
            SettingsIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
        }
        else
        {
            SettingsIcon.ClearValue(Microsoft.UI.Xaml.Controls.IconElement.ForegroundProperty);
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
