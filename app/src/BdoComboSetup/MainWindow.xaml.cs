using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Services;
using BdoComboSetup.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace BdoComboSetup;

public sealed partial class MainWindow : Window
{
    /// <summary>The update state for the banner. A getter, because the x:Bind bindings are evaluated inside InitializeComponent.</summary>
    public UpdateViewModel Updates => App.Services.Updates;

    /// <summary>How long the menu stays open after the pointer and the keyboard focus left it (the prototype's 0.3 s).</summary>
    private static readonly TimeSpan MenuCloseDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Closes the menu after <see cref="MenuCloseDelay"/> when nothing keeps it open. Cancelled when the pointer comes back.</summary>
    private readonly DispatcherQueueTimer _menuCloseTimer;

    /// <summary>True when the menu hides itself (auto-hide mode). False when it is always visible.</summary>
    private bool _autoHide;

    /// <summary>The pointer is over the edge handle.</summary>
    private bool _pointerOnEdge;

    /// <summary>The pointer is over the open menu (within its width).</summary>
    private bool _pointerOnMenu;

    public MainWindow()
    {
        Apply = App.Services.ApplyCombo;
        InitializeComponent();
        CharacterPicker.SetFields(item => Apply.CharacterSearchTexts((CharacterOption)item));

        var services = App.Services;
        _menuCloseTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _menuCloseTimer.Interval = MenuCloseDelay;
        _menuCloseTimer.IsRepeating = false;
        _menuCloseTimer.Tick += (_, _) => CloseMenuIfIdle();
        Title = services.Localizer.Get("AppTitle");
        TitleText.Text = Title;
        VersionText.Text = services.Localizer.Format("StatusBar.Version", AppVersion.Current.ToString());

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
        Closed += (_, _) =>
        {
            gameTimer.Stop();
            _menuCloseTimer.Stop();
        };

        // The window and its title bar are drawn by the app; the TitleBar control hosts the drag region.
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1440, 900));
        WindowIcon.Apply(AppWindow, AppTitleBar, services.Logger);

        // The logo chip of the title bar replaces the icon image: the taskbar keeps the icon (AppWindow.SetIcon).
        AppTitleBar.IconSource = null;

        // The color theme and the accent are applied before the first page is shown.
        services.Theme.Attach(RootGrid, AppWindow);

        // The pointer and the keyboard focus open and close the menu (see the handlers below). Their events are taken even when a
        // child already handled them, so the menu also sees the pointer over the page content.
        Nav.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(Menu_PointerEntered), true);
        Nav.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Menu_PointerMoved), true);
        Nav.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(Menu_PointerExited), true);

        // The menu hides itself unless the user chose "always visible" in Settings, which applies live.
        ApplyMenuMode(services.Settings.Settings.NavAutoHide);
        services.SettingsView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.NavAutoHide) && _autoHide != services.SettingsView.NavAutoHide)
            {
                ApplyMenuMode(services.SettingsView.NavAutoHide);
            }
        };

        // Without a readable assets.bdoa the modules cannot show icons or combos: the window shows the explanation only.
        if (services.AssetPackLoad.ErrorCode is not null)
        {
            // Only the pane is hidden: collapsing the whole NavigationView would hide the Frame inside it.
            Nav.IsPaneVisible = false;
            UpdateEdgeHandle();
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

    /// <summary>
    /// Applies the side menu mode. Auto-hide: the menu is closed (the content has the full width), the edge handle opens it as an
    /// overlay. Always visible: the menu is open and has no handle. The toggle button is never shown.
    /// </summary>
    private void ApplyMenuMode(bool autoHide)
    {
        _autoHide = autoHide;
        _pointerOnEdge = false;
        _pointerOnMenu = false;
        _menuCloseTimer.Stop();
        Nav.PaneDisplayMode = autoHide ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.Left;
        Nav.IsPaneOpen = !autoHide;
        UpdateEdgeHandle();
        App.Services.Logger.Info("window.menu_mode", "Side menu mode applied", new Dictionary<string, object?>
        {
            ["auto_hide"] = autoHide,
        });
    }

    /// <summary>The edge handle exists only in auto-hide mode, and only while the menu is part of the window (not on setup or the error page).</summary>
    private void UpdateEdgeHandle() => EdgeHandle.Visibility = _autoHide && Nav.IsPaneVisible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Opens the menu (auto-hide only) and cancels a pending close.</summary>
    private void OpenMenu()
    {
        _menuCloseTimer.Stop();
        if (_autoHide && Nav.IsPaneVisible && !Nav.IsPaneOpen)
        {
            Nav.IsPaneOpen = true;
            App.Services.Logger.Debug("navigation.menu_opened", "Side menu opened");
        }
    }

    /// <summary>Starts the close delay, unless it is already running or the menu is not open.</summary>
    private void ScheduleMenuClose()
    {
        if (_autoHide && Nav.IsPaneOpen && !_menuCloseTimer.IsRunning)
        {
            _menuCloseTimer.Start();
        }
    }

    /// <summary>Closes the menu when the pointer and the keyboard focus are both away from it.</summary>
    private void CloseMenuIfIdle()
    {
        if (!_autoHide || !Nav.IsPaneOpen || _pointerOnEdge || _pointerOnMenu || FocusIsOnMenu())
        {
            return;
        }

        CloseMenu();
    }

    private void CloseMenu()
    {
        _menuCloseTimer.Stop();
        _pointerOnMenu = false;
        Nav.IsPaneOpen = false;
        App.Services.Logger.Debug("navigation.menu_closed", "Side menu closed");
    }

    /// <summary>The keyboard focus is on the edge handle or on a menu item (so the menu stays open for keyboard use).</summary>
    private bool FocusIsOnMenu()
    {
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        return focused is NavigationViewItem || (ReferenceEquals(focused, EdgeHandle) && EdgeHandle.FocusState == FocusState.Keyboard);
    }

    /// <summary>The pointer is over the open menu when it is within the menu width (the content is to the right of it).</summary>
    private void UpdateMenuPointer(PointerRoutedEventArgs e) =>
        _pointerOnMenu = _autoHide && Nav.IsPaneOpen && e.GetCurrentPoint(Nav).Position.X <= Nav.OpenPaneLength;

    private void Menu_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        UpdateMenuPointer(e);
        if (_pointerOnMenu)
        {
            _menuCloseTimer.Stop();
        }
    }

    private void Menu_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        UpdateMenuPointer(e);
        if (_pointerOnMenu)
        {
            _menuCloseTimer.Stop();
        }
        else
        {
            ScheduleMenuClose();
        }
    }

    private void Menu_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnMenu = false;
        ScheduleMenuClose();
    }

    /// <summary>Focus left a menu item: the menu closes unless the focus moved to the edge handle or another item.</summary>
    private void MenuItem_LostFocus(object sender, RoutedEventArgs e) => ScheduleMenuClose();

    private void EdgeHandle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnEdge = true;
        OpenMenu();
    }

    private void EdgeHandle_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnEdge = false;
        ScheduleMenuClose();
    }

    /// <summary>
    /// Keyboard focus on the handle opens the menu, as the pointer does. Focus that the framework gives back to the handle when the
    /// menu closes is programmatic and must not reopen it (that made the menu open and close in a loop).
    /// </summary>
    private void EdgeHandle_GotFocus(object sender, RoutedEventArgs e)
    {
        if (EdgeHandle.FocusState == FocusState.Keyboard)
        {
            OpenMenu();
        }
    }

    private void EdgeHandle_LostFocus(object sender, RoutedEventArgs e) => ScheduleMenuClose();

    private void EdgeHandle_Click(object sender, RoutedEventArgs e) => OpenMenu();

    /// <summary>The view model that holds the character of the whole app.</summary>
    public ApplyComboViewModel Apply { get; }

    /// <summary>Shows the Preview page (the combos are written from there).</summary>
    public void ShowPreview() => Nav.SelectedItem = PreviewItem;

    /// <summary>Opens the character picker of the status bar (used by the demo mode).</summary>
    public void OpenCharacterPicker() => CharacterPicker.Open();

    /// <summary>Chooses the character picked in the status bar (the view model selects its class too).</summary>
    private void CharacterPicker_Chosen(object? sender, object item)
    {
        if (item is CharacterOption option)
        {
            Apply.SelectCharacterCommand.Execute(option);
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Choosing an item closes the auto-hide menu (the always visible menu stays open).
        if (_autoHide && Nav.IsPaneOpen)
        {
            CloseMenu();
        }

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
        UpdateEdgeHandle();
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
        UpdateEdgeHandle();
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
