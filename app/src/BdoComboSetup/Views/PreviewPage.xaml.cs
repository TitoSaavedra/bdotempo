using Windows.ApplicationModel.DataTransfer;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Search;
using BdoComboSetup.Core.ViewModels;
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
    private void Fit_Click(object sender, RoutedEventArgs e) => ViewModel.FitToView();

    private void FitWindow_Click(object sender, RoutedEventArgs e) => ViewModel.SetFitMode(CanvasFitMode.Window);

    private void FitSlots_Click(object sender, RoutedEventArgs e) => ViewModel.SetFitMode(CanvasFitMode.Slots);

    private void FitReal_Click(object sender, RoutedEventArgs e) => ViewModel.SetFitMode(CanvasFitMode.Real);

    /// <summary>The look of a fit pill: the accent gradient while its fit is the active one, the glass pill otherwise.</summary>
    public Style PillStyleFor(bool selected) =>
        (Style)Application.Current.Resources[selected ? "PillSelectedStyle" : "PillStyle"];

    /// <summary>The header's "Apply" button: the combo is applied to the character from here.</summary>
    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        await RequestApplyAsync();
    }

    /// <summary>
    /// Asks which parts are written (the bar, the memos, the quick slots) and opens the review with that choice. Nothing is written
    /// here; the review card has the write button.
    /// </summary>
    public async Task RequestApplyAsync()
    {
        if (await ConfirmWriteOptionsAsync())
        {
            await Apply.ApplyCommand.ExecuteAsync(null);
        }
    }

    /// <summary>The character is chosen in Combos only, and the "Back" button goes there too: this shows the Combos page.</summary>
    private void GoToCombos_Click(object sender, RoutedEventArgs e) => (App.MainWindow as MainWindow)?.ShowCombos();

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
