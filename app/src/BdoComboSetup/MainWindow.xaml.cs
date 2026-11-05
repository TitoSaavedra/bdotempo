using System.ComponentModel;
using BdoComboSetup.Core.Tour;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Services;
using BdoComboSetup.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace BdoComboSetup;

public sealed partial class MainWindow : Window
{
    /// <summary>The update state for the banner. A getter, because the x:Bind bindings are evaluated inside InitializeComponent.</summary>
    public UpdateViewModel Updates => App.Services.Updates;

    /// <summary>The tip of the guided tour step that is shown (a new one is built for every step, see ShowTourTip).</summary>
    private TeachingTip? _tourTip;

    /// <summary>True while a tab is selected from code after a navigation: its SelectionChanged is not a click, so it does nothing.</summary>
    private bool _syncingTabs;

    /// <summary>True when the setup on screen is the first-run setup: the guided tour starts when it is left.</summary>
    private bool _setupStartsTour;

    /// <summary>The "Next" action of the tour's tip: its availability follows the step (a waiting step disables it).</summary>
    private readonly TourCommand _tourNext;

    /// <summary>The side of the ghost tile of the drag cue, in device-independent pixels (see the XAML).</summary>
    private const double DragCueSize = 36;

    /// <summary>One loop of the drag cue: fade in, travel, hold, fade out, then a hidden pause.</summary>
    private static readonly TimeSpan DragCueCycle = TimeSpan.FromSeconds(2.5);

    /// <summary>The storyboard of the drag cue while it runs; null when the cue is hidden.</summary>
    private Storyboard? _dragCue;

    /// <summary>Where the running drag cue starts and ends (the ghost's top-left corner), to restart it only when they move.</summary>
    private DragCuePath? _dragCuePath;

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

        // The guided tour: the tip follows the page, the state of the tour and the loaded combo.
        _tourNext = new TourCommand(TourNext, () => App.Services.Tour.CanAdvance);
        ContentFrame.Navigated += (_, _) => RefreshTourTip();
        services.Tour.Changed += (_, _) => RefreshTourTip();
        // A resize moves the cue's targets, but not the tip: only the cue is placed again, so the tip is not rebuilt on every resize.
        RootGrid.SizeChanged += (_, _) => RefreshTourDragCue();
        services.ApplyCombo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ApplyComboViewModel.HasEditor))
            {
                RefreshTourTip();
            }
        };

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
        else if (services.Startup.Tour)
        {
            // --tour: the guided tour opens on Combos, for testing it by hand.
            StartTour();
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
    public void ShowSetup(bool firstRun)
    {
        _setupStartsTour = firstRun;
        Navigate(typeof(SetupPage), firstRun);
    }

    /// <summary>
    /// Leaves the initial setup: the page of the app opens. The settings just saved become the running ones, so the page does
    /// not ask for a restart. After the first-run setup the guided tour starts on Combos; a setup reopened from Settings does not.
    /// </summary>
    public void LeaveSetup(bool goToSettings)
    {
        var startTour = _setupStartsTour && !goToSettings;
        _setupStartsTour = false;
        App.Services.SettingsView.AdoptRunningConfiguration();
        if (startTour)
        {
            StartTour();
            return;
        }

        ShowPage(goToSettings ? typeof(SettingsPage) : typeof(ApplyComboPage));
    }

    /// <summary>
    /// Starts the guided tour on the Combos page. Used after the first-run setup, by "--tour" and by Settings ("Repeat tour").
    /// A write review left open is closed first: the tour never writes, and the review's write button would still work.
    /// </summary>
    public void StartTour()
    {
        var services = App.Services;
        if (services.ApplyCombo.IsReviewOpen && services.ApplyCombo.CancelReviewCommand.CanExecute(null))
        {
            services.ApplyCombo.CancelReviewCommand.Execute(null);
        }

        services.Tour.Start(services.ApplyCombo.HasEditor);
        ShowTourStep();
    }

    /// <summary>
    /// The "Next" of the tip: the tour moves on (or finishes), and the page of the new step is shown. A refused Next does nothing.
    /// </summary>
    private void TourNext()
    {
        if (App.Services.Tour.Next())
        {
            ShowTourStep();
        }
    }

    /// <summary>Shows the page the current step needs (Combos or Editor, when the step has one) and places the tip on it.</summary>
    private void ShowTourStep()
    {
        var page = App.Services.Tour.Current?.Id switch
        {
            "character" or "drag" => typeof(ApplyComboPage),
            "apply" => typeof(PreviewPage),
            _ => null,
        };
        if (page is not null)
        {
            ShowPage(page);
        }

        RefreshTourTip();
    }

    /// <summary>
    /// Places the tip again after a change. It is deferred to the dispatcher, so the page of the step has laid out its controls.
    /// </summary>
    private void RefreshTourTip() => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, PositionTourTip);

    /// <summary>Places the drag cue again on the dispatcher, after a resize. The tip is not touched.</summary>
    private void RefreshTourDragCue() => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, UpdateTourDragCue);

    /// <summary>
    /// Runs the drag cue while the drag step waits for the user's drop: a ghost tile travels from the first skill card of the chain
    /// list to the first empty cooldown cell. It is stopped and hidden on any other step, once the drop is done, and when the Combos
    /// page is not shown. A target that is not laid out yet skips the cue quietly; the next refresh tries again.
    /// </summary>
    private void UpdateTourDragCue()
    {
        var services = App.Services;
        var tour = services.Tour;
        var page = ContentFrame.Content as ApplyComboPage;
        if (tour.Current?.Id != "drag" || !tour.IsWaitingForAction || page is null)
        {
            StopTourDragCue();
            return;
        }

        try
        {
            var start = CenterInRoot(page.TourDragSourceTarget);
            var end = CenterInRoot(page.TourDropCellTarget);
            if (start is null || end is null)
            {
                services.Logger.Debug("tour.drag_cue.skipped", "Drag cue skipped: a target is not laid out yet", new Dictionary<string, object?>
                {
                    ["source_ready"] = start is not null,
                    ["target_ready"] = end is not null,
                });
                StopTourDragCue();
                return;
            }

            // The ghost's top-left corner travels, so its center lands on the center of each target.
            var half = DragCueSize / 2;
            var path = new DragCuePath(start.Value.X - half, start.Value.Y - half, end.Value.X - half, end.Value.Y - half);
            if (_dragCue is null || _dragCuePath != path)
            {
                StartTourDragCue(path);
            }
        }
        catch (Exception ex)
        {
            services.Logger.Error("tour.drag_cue.failed", "The drag cue could not be placed", ex);
            StopTourDragCue();
        }
    }

    /// <summary>The center of an element in the coordinates of RootGrid; null when the element is not laid out yet.</summary>
    private (double X, double Y)? CenterInRoot(FrameworkElement? element)
    {
        if (element is not { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 })
        {
            return null;
        }

        var center = element.TransformToVisual(RootGrid).TransformPoint(new Windows.Foundation.Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return (center.X, center.Y);
    }

    /// <summary>
    /// Starts the drag cue's loop (2.5 s per cycle): the tile fades in on the first card (0.2 s), travels to the empty cell in 1.2 s
    /// (cubic ease-in-out), stays 0.3 s, fades out (0.3 s), and waits 0.7 s hidden before it repeats.
    /// </summary>
    private void StartTourDragCue(DragCuePath path)
    {
        _dragCue?.Stop();
        var travel = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var fade = new CubicEase { EasingMode = EasingMode.EaseIn };
        var board = new Storyboard { RepeatBehavior = new RepeatBehavior { Type = RepeatBehaviorType.Forever } };
        board.Children.Add(KeyFramed(DragCueMove, "X", (0, path.StartX, null), (1.2, path.EndX, travel), (2.5, path.EndX, null)));
        board.Children.Add(KeyFramed(DragCueMove, "Y", (0, path.StartY, null), (1.2, path.EndY, travel), (2.5, path.EndY, null)));
        board.Children.Add(KeyFramed(DragCueGhost, "Opacity", (0, 0, null), (0.2, 1, null), (1.5, 1, null), (1.8, 0, fade), (2.5, 0, null)));
        DragCueLayer.Visibility = Visibility.Visible;
        board.Begin();
        _dragCue = board;
        _dragCuePath = path;
        App.Services.Logger.Debug("tour.drag_cue.started", "Drag cue started");
    }

    /// <summary>One animated property over the cycle: each key is (seconds, value, the easing into it, null for a straight line).</summary>
    private static Timeline KeyFramed(DependencyObject target, string property, params (double Seconds, double Value, EasingFunctionBase? Ease)[] keys)
    {
        var track = new DoubleAnimationUsingKeyFrames { Duration = new Duration(DragCueCycle) };
        foreach (var key in keys)
        {
            var time = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(key.Seconds));
            if (key.Ease is null)
            {
                track.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = time, Value = key.Value });
            }
            else
            {
                track.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = time, Value = key.Value, EasingFunction = key.Ease });
            }
        }

        Storyboard.SetTarget(track, target);
        Storyboard.SetTargetProperty(track, property);
        return track;
    }

    /// <summary>Stops and hides the drag cue. Does nothing more when it is not running.</summary>
    private void StopTourDragCue()
    {
        if (_dragCue is not null)
        {
            _dragCue.Stop();
            _dragCue = null;
            _dragCuePath = null;
            App.Services.Logger.Debug("tour.drag_cue.stopped", "Drag cue stopped");
        }

        DragCueLayer.Visibility = Visibility.Collapsed;
    }

    /// <summary>Where the drag cue's ghost starts and ends: the top-left corners, in RootGrid coordinates.</summary>
    private readonly record struct DragCuePath(double StartX, double StartY, double EndX, double EndY);

    /// <summary>
    /// Moves the tip to the target of the current step. When the tour is not active, or the page of the step is not the one
    /// shown (the user went elsewhere), the tip is closed and the tour keeps its step: the tip returns when the page does.
    /// The drag cue is placed after the tip, so it follows the same refreshes.
    /// </summary>
    private void PositionTourTip()
    {
        PlaceTourTip();
        UpdateTourDragCue();
    }

    /// <summary>Opens the tip at the current step's target, or closes it when the step has no target on the page shown.</summary>
    private void PlaceTourTip()
    {
        var services = App.Services;
        services.Tour.UpdateCombo(services.ApplyCombo.HasEditor);
        var step = services.Tour.Current;
        if (step is null)
        {
            CloseTourTip();
            return;
        }

        var target = TourTargetOf(step.Id);
        if (target is null)
        {
            CloseTourTip();
            return;
        }

        if (target is FrameworkElement { IsLoaded: false } pending)
        {
            // The page of the step is still loading: the tip is placed once its target is loaded.
            WaitForLoaded(pending);
            CloseTourTip();
            return;
        }

        ShowTourTip(step, target);
    }

    /// <summary>The element a step points at. Null when the page of the step is not the one shown.</summary>
    private UIElement? TourTargetOf(string stepId) => stepId switch
    {
        "character" => (ContentFrame.Content as ApplyComboPage)?.TourCharacterTarget,
        "drag" => (ContentFrame.Content as ApplyComboPage)?.TourChainsTarget,
        "editor" => EditorTab,
        "apply" => (ContentFrame.Content as PreviewPage)?.TourApplyTarget,
        "settings" => SettingsButton,
        _ => null,
    };

    /// <summary>Fills the tip with the step (title, text, the "n / 5" caption, the buttons) and opens it at its target.</summary>
    private void ShowTourTip(TourStep step, UIElement target)
    {
        var services = App.Services;
        var tour = services.Tour;
        var localizer = services.Localizer;

        // A new tip for every step: a tip that was open keeps the position it had (and reopening it while it is still closing
        // is ignored), so its placement could not be changed between steps.
        DiscardTourTip();

        var tip = new TeachingTip
        {
            IsLightDismissEnabled = false,
            // Placed so it never covers what the step is about: beside the tall left card, under the slot board (the skills to
            // drag stay visible), under the title bar items.
            PreferredPlacement = step.Id switch
            {
                "character" => TeachingTipPlacementMode.RightTop,
                "apply" => TeachingTipPlacementMode.Top,
                _ => TeachingTipPlacementMode.Bottom,
            },
            Title = localizer.Get(step.TitleKey),
            Subtitle = localizer.Get(step.TextKey),
            Content = new TextBlock
            {
                Text = localizer.Format("Tour.Progress", tour.StepNumber, tour.StepCount),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            },
            CloseButtonContent = localizer.Get("Tour.Skip"),
            ActionButtonContent = tour.IsWaitingForAction
                ? localizer.Get("Tour.DragToContinue")
                : localizer.Get(tour.IsLastStep ? "Tour.Finish" : "Tour.Next"),
            ActionButtonCommand = _tourNext,
        };
        Grid.SetRowSpan(tip, 4);
        tip.Closed += TourTip_Closed;
        RootGrid.Children.Add(tip);
        _tourTip = tip;
        _tourNext.RaiseCanExecuteChanged();
        tip.Target = target as FrameworkElement;
        tip.IsOpen = true;
    }

    private void CloseTourTip() => DiscardTourTip();

    /// <summary>Closes and removes the tip of the step that is shown, if any.</summary>
    private void DiscardTourTip()
    {
        if (_tourTip is not { } tip)
        {
            return;
        }

        _tourTip = null;
        tip.Closed -= TourTip_Closed;
        tip.IsOpen = false;
        tip.Target = null;
        RootGrid.Children.Remove(tip);
    }

    /// <summary>Calls <see cref="RefreshTourTip"/> once the element has loaded.</summary>
    private void WaitForLoaded(FrameworkElement element)
    {
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            element.Loaded -= OnLoaded;
            RefreshTourTip();
        }

        element.Loaded += OnLoaded;
    }

    /// <summary>
    /// The tip's close button (or Escape) skips the tour. A close made from the code is not a skip: it is the end of a step
    /// (the tip is closed and opened again at the next target).
    /// </summary>
    private void TourTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        if (args.Reason != TeachingTipCloseReason.Programmatic && App.Services.Tour.IsActive)
        {
            App.Services.Tour.Skip();
        }
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
