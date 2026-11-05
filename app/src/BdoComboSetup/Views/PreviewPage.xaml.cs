using Windows.ApplicationModel.DataTransfer;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Search;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BdoComboSetup.Views;

/// <summary>
/// The Editor: the 20 cooldown slots and the 20 quick slots of the combo being edited, over the live game capture. A click on
/// a cooldown slot changes its skill in that combo. The combo is applied from here: "Apply to character" asks which parts
/// are written, runs the review and offers the write. The guide is never touched.
/// </summary>
public sealed partial class PreviewPage : Page
{
    public PreviewPage()
    {
        ViewModel = App.Services.Preview;
        Apply = App.Services.ApplyCombo;
        InitializeComponent();

        var localizer = App.Services.Localizer;
        ToolTipService.SetToolTip(LayoutBox, localizer.Get("Preview.Layout.Tooltip"));
        ToolTipService.SetToolTip(HiddenToggle, localizer.Get("Preview.Hidden.Tooltip"));
    }

    public PreviewViewModel ViewModel { get; }

    /// <summary>The write side: the combo of the Combos page, its review and the write.</summary>
    public ApplyComboViewModel Apply { get; }

    /// <summary>The guided tour: its state drives whether the Apply button is enabled.</summary>
    public TourService Tour => App.Services.Tour;

    /// <summary>The guided tour's Apply step target: the Apply button of the side panel.</summary>
    public Button TourApplyTarget => ApplyButton;

    /// <summary>The guided tour's Settings step target: the Ajustes tab of the side panel.</summary>
    public SelectorBarItem TourSettingsTarget => TabSettingsItem;

    /// <summary>The Apply button is enabled when the combo can be applied and no guided tour is running.</summary>
    public static bool ApplyEnabled(bool canRun, bool tourActive) => canRun && !tourActive;

    /// <summary>The status line under the game takes room only when at least one of its texts has something to say.</summary>
    public static Visibility AnyTextVisibility(string? first, string? second, string? third) =>
        string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second) && string.IsNullOrWhiteSpace(third)
            ? Visibility.Collapsed
            : Visibility.Visible;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var startup = App.Services.Startup;
        try
        {
            await Apply.InitializeAsync(startup.ApplyClass, startup.ApplySpec);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Services.Logger.Error("preview.page.initialize_failed", "The Preview page could not load the combo of the Apply page", ex);
        }
    }

    /// <summary>The fit button: the slots fill the window (the same as the "fit to slots" pill).</summary>
    private void FitWindow_Click(object sender, RoutedEventArgs e) => ViewModel.SetFitMode(CanvasFitMode.Window);

    private void FitSlots_Click(object sender, RoutedEventArgs e) => ViewModel.SetFitMode(CanvasFitMode.Slots);

    private void FitReal_Click(object sender, RoutedEventArgs e) => ViewModel.SetFitMode(CanvasFitMode.Real);

    /// <summary>The look of a fit pill: the accent gradient while its fit is the active one, the glass pill otherwise.</summary>
    public Style PillStyleFor(bool selected) =>
        (Style)Application.Current.Resources[selected ? "PillSelectedStyle" : "PillStyle"];

    /// <summary>Shows the content of the chosen tab and hides the others. Slots is the first tab.</summary>
    private void EditorTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ShowTab(sender.SelectedItem?.Tag as string ?? "slots");

    /// <summary>
    /// Selects the Ajustes tab (true) or the Casillas tab (false) from code, for the guided tour. Setting the selection raises
    /// SelectionChanged, and the tab's content is shown the same way the click shows it.
    /// </summary>
    public void ShowSettingsTab(bool show)
    {
        EditorTabs.SelectedItem = show ? TabSettingsItem : TabSlotsItem;
        ShowTab(show ? "settings" : "slots");
    }

    private void ShowTab(string tag)
    {
        TabSlots.Visibility = tag == "slots" ? Visibility.Visible : Visibility.Collapsed;
        TabSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Asks which parts are written (the bar, the memos, the quick slots) and opens the review with that choice. Nothing is written
    /// here; the review card has the write button. The title bar's "Apply" button calls it.
    /// </summary>
    public async Task RequestApplyAsync()
    {
        // The guided tour never writes: while it runs, Apply does nothing (the button is disabled too).
        if (App.Services.Tour.IsApplyBlocked)
        {
            App.Services.Logger.Info("tour.apply_blocked", "Apply ignored: the guided tour is active");
            return;
        }

        if (await ConfirmWriteOptionsAsync())
        {
            await Apply.ApplyCommand.ExecuteAsync(null);
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e) => await RequestApplyAsync();

    /// <summary>The character is chosen in Combos only: the "Change in Combos" link shows the Combos page.</summary>
    private void ChangeCharacter_Click(object sender, RoutedEventArgs e) => (App.MainWindow as MainWindow)?.ShowCombos();

    /// <summary>A pending skill starts to be dragged: its step takes the slot it is dropped on.</summary>
    private void PendingSkill_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: PendingSkill pending })
        {
            SlotDrag.Step = pending.Step;
            SlotDrag.FromSlot = null;
            args.Data.SetText(pending.Name);
        }
        else
        {
            args.Cancel = true;
        }
    }

    private void PendingSkill_DropCompleted(UIElement sender, DropCompletedEventArgs args) => SlotDrag.Clear();

    /// <summary>"+": the skill takes the first free cooldown slot.</summary>
    private void PendingAdd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PendingSkill pending })
        {
            Apply.Editor?.AddToFreeSlot(pending.Step);
        }
    }

    /// <summary>The list accepts a skill dragged out of a slot.</summary>
    private void PendingCard_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = SlotDrag.FromSlot is not null ? DataPackageOperation.Move : DataPackageOperation.None;
    }

    /// <summary>A skill dropped on the list leaves its slot, so it is pending again.</summary>
    private void PendingCard_Drop(object sender, DragEventArgs e)
    {
        if (SlotDrag.FromSlot is not null && SlotDrag.Step is { } step)
        {
            Apply.Editor?.AssignCooldown(step, null);
        }

        SlotDrag.Clear();
    }

    /// <summary>Saves the parts to write in the view model. False when the user cancels or checks nothing.</summary>
    private async Task<bool> ConfirmWriteOptionsAsync()
    {
        var localizer = App.Services.Localizer;
        var bar = new CheckBox
        {
            Content = localizer.Get("Apply.IncludeCooldownBarCheck.Content"),
            IsChecked = Apply.IncludeCooldownBar,
        };
        var memos = new CheckBox
        {
            Content = localizer.Get("Apply.IncludeMemosCheck.Content"),
            IsChecked = Apply.IncludeMemos,
        };
        var quick = new CheckBox
        {
            Content = localizer.Get("Apply.IncludeQuickSlotsCheck.Content"),
            IsChecked = Apply.IncludeQuickSlots,
        };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = localizer.Get("Apply.WriteDialogHint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        });
        panel.Children.Add(bar);
        panel.Children.Add(memos);
        panel.Children.Add(quick);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = localizer.Get("Apply.WriteDialogTitle"),
            Content = panel,
            PrimaryButtonText = localizer.Get("Apply.WriteDialogContinue"),
            CloseButtonText = localizer.Get("Apply.CancelReviewButton.Content"),
            DefaultButton = ContentDialogButton.Primary,
        };

        void SyncPrimary() =>
            dialog.IsPrimaryButtonEnabled = bar.IsChecked == true || memos.IsChecked == true || quick.IsChecked == true;

        bar.Checked += (_, _) => SyncPrimary();
        bar.Unchecked += (_, _) => SyncPrimary();
        memos.Checked += (_, _) => SyncPrimary();
        memos.Unchecked += (_, _) => SyncPrimary();
        quick.Checked += (_, _) => SyncPrimary();
        quick.Unchecked += (_, _) => SyncPrimary();
        SyncPrimary();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        Apply.IncludeCooldownBar = bar.IsChecked == true;
        Apply.IncludeMemos = memos.IsChecked == true;
        Apply.IncludeQuickSlots = quick.IsChecked == true;
        return true;
    }
}
