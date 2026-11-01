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
        EditorCharacterPicker.SetFields(item => Apply.CharacterSearchTexts((CharacterOption)item));
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

    private void Fit_Click(object sender, RoutedEventArgs e) => ViewModel.FitToView();

    /// <summary>Chooses the character whose slots are edited and written.</summary>
    private void EditorCharacterPicker_Chosen(object? sender, object item)
    {
        if (item is CharacterOption option)
        {
            Apply.SelectCharacterCommand.Execute(option);
        }
    }

    /// <summary>
    /// "Apply to character": asks which parts are written (the bar, the memos, the quick slots) and opens the review with
    /// that choice. Nothing is written here; the review card has the write button.
    /// </summary>
    private async void EditorApply_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmWriteOptionsAsync())
        {
            await Apply.ApplyCommand.ExecuteAsync(null);
        }
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
