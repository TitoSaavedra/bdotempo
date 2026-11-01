using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Search;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Views.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace BdoComboSetup.Views;

/// <summary>
/// The "Apply combo" page. The page only translates clicks and drags into view model calls; the rules (what a step
/// is, which slot it takes, what is written) live in the view models.
/// </summary>
public sealed partial class ApplyComboPage : Page
{
    /// <summary>The data-package property that tells a drop target which chain a dragged step came from.</summary>
    private const string SourceChainKey = "BdoComboSetup.SourceChain";

    public ApplyComboPage()
    {
        ViewModel = App.Services.ApplyCombo;
        InitializeComponent();
        ClassPicker.SetFields(item => SearchFields.ForClass(((ClassOptionViewModel)item).Info));
        CharacterPicker.SetFields(item => ViewModel.CharacterSearchTexts((CharacterOption)item));
        ComboSlotPicker.SetFields(item => [((ComboSlotOption)item).Label]);
    }

    /// <summary>The texts a skill is searched by in the "Add skill" list: both names and the number.</summary>
    public static Func<object, IEnumerable<string?>> SkillSearchFields { get; } =
        item => SearchFields.ForSkill((SkillOption)item);

    public ApplyComboViewModel ViewModel { get; }

    /// <summary>Maps the view model's severity to the InfoBar's enum (the view models do not reference WinUI).</summary>
    public InfoBarSeverity ToInfoBarSeverity(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Success => InfoBarSeverity.Success,
        StatusSeverity.Warning => InfoBarSeverity.Warning,
        StatusSeverity.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    /// <summary>The card of a step: gold outline when it is selected.</summary>
    public static Style CardStyle(bool selected) =>
        (Style)App.Current.Resources[selected ? "StepCardSelectedStyle" : "StepCardStyle"];

    /// <summary>A spec pill: gold when it is the selected spec.</summary>
    public static Style PillStyle(bool selected) =>
        (Style)App.Current.Resources[selected ? "PillSelectedStyle" : "PillStyle"];

    /// <summary>A class tile: gold when it is the selected class.</summary>
    public static Style ClassTileStyle(bool selected) =>
        (Style)App.Current.Resources[selected ? "ClassTileSelectedStyle" : "ClassTileStyle"];


    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var startup = App.Services.Startup;
        try
        {
            await ViewModel.InitializeAsync(startup.ApplyClass, startup.ApplySpec);
            if (startup.DemoReview)
            {
                // Demo only: opens the review at startup so it can be shown (the demo applier writes nothing).
                await ViewModel.ApplyCommand.ExecuteAsync(null);
            }

            if (startup.DemoCharacterPicker)
            {
                // Demo only: opens the character picker so it can be shown (the list, the search box).
                Loaded += DemoCharacterPicker_Loaded;
            }

            if (startup.DemoWriteHint)
            {
                // Demo only: asks to write, so the reminder before a write is shown. The demo applier writes nothing.
                // (The reminder's dialog needs the window, so it waits for the page: see DemoWriteHint_Loaded.)
                // Waits for the page to load: the reminder's dialog needs the window's XamlRoot, which exists only then.
                Loaded += DemoWriteHint_Loaded;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Services.Logger.Error("apply.page.initialize_failed", "The Apply page could not select its startup guide", ex);
        }
    }

    /// <summary>The class icon of a character. Null (the glyph is drawn) when the class is unknown.</summary>
    public static Func<Stream?> ClassIconOf(int? classType) =>
        () => classType is int value ? App.Services.Assets.OpenClassIcon(value) : null;

    /// <summary>The glyph drawn in place of the icon: a question mark for an unknown class.</summary>
    public static string ClassGlyphOf(int? classType) => classType is null ? "?" : string.Empty;

    /// <summary>Demo only: asks to write once the page is loaded, so the reminder has its window.</summary>
    private async void DemoWriteHint_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= DemoWriteHint_Loaded;
        await ViewModel.ConfirmWriteCommand.ExecuteAsync(null);
    }

    /// <summary>Demo only: opens the character picker once the page is loaded.</summary>
    private void DemoCharacterPicker_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= DemoCharacterPicker_Loaded;
        CharacterPicker.Open();
    }

    /// <summary>Chooses the class picked in the searchable class picker (the page then lists its guides).</summary>
    private void ClassPicker_Chosen(object? sender, object item)
    {
        if (item is ClassOptionViewModel option)
        {
            option.SelectCommand.Execute(null);
        }
    }

    /// <summary>Chooses the character picked in the searchable picker (the view model selects its class too).</summary>
    private void CharacterPicker_Chosen(object? sender, object item)
    {
        if (item is CharacterOption option)
        {
            ViewModel.SelectCharacterCommand.Execute(option);
        }
    }

    /// <summary>Chooses the custom combo slot the buttons work with.</summary>
    private void ComboSlotPicker_Chosen(object? sender, object item)
    {
        if (item is ComboSlotOption option)
        {
            ViewModel.SelectComboSlot(option);
        }
    }

    private void Step_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ComboStepViewModel step)
        {
            ViewModel.Editor?.SelectStep(step);
        }
    }

    /// <summary>
    /// "Apply to character": first asks which parts are written (the bar, the memos, the quick slots), then opens the review
    /// with that choice. Nothing is written from here; the review has the write button.
    /// </summary>
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmWriteOptionsAsync())
        {
            await ViewModel.ApplyCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Saves the parts to write in the view model. False when the user cancels or checks nothing.</summary>
    private async Task<bool> ConfirmWriteOptionsAsync()
    {
        var localizer = App.Services.Localizer;
        var bar = new CheckBox
        {
            Content = localizer.Get("Apply.IncludeCooldownBarCheck.Content"),
            IsChecked = ViewModel.IncludeCooldownBar,
        };
        var memos = new CheckBox
        {
            Content = localizer.Get("Apply.IncludeMemosCheck.Content"),
            IsChecked = ViewModel.IncludeMemos,
        };
        var quick = new CheckBox
        {
            Content = localizer.Get("Apply.IncludeQuickSlotsCheck.Content"),
            IsChecked = ViewModel.IncludeQuickSlots,
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

        ViewModel.IncludeCooldownBar = bar.IsChecked == true;
        ViewModel.IncludeMemos = memos.IsChecked == true;
        ViewModel.IncludeQuickSlots = quick.IsChecked == true;
        return true;
    }

    private void AddToSlot_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ComboStepViewModel step })
        {
            ViewModel.Editor?.AddToFreeSlot(step);
        }
    }

    /// <summary>Adds the picked skill to the chain the list was opened from (the list's Tag), then clears the search for the next one.</summary>
    private void AddSkill_ItemChosen(object? sender, object item)
    {
        if (sender is SearchableList { Tag: ComboChainViewModel chain } list && item is SkillOption option)
        {
            chain.AddSkillCommand.Execute(option);
            list.Reset();
        }
    }

    /// <summary>Remembers which chain a dragged step belongs to.</summary>
    private void Steps_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (sender is ListView { Tag: ComboChainViewModel chain })
        {
            e.Data.Properties[SourceChainKey] = chain.Index;
        }
    }

    /// <summary>
    /// A step can only be reordered inside its own chain. Its base step index is relative to that chain, so a step moved
    /// into another chain would point at the wrong guide step.
    /// </summary>
    private void Steps_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not ListView { Tag: ComboChainViewModel chain })
        {
            return;
        }

        if (!e.DataView.Properties.TryGetValue(SourceChainKey, out var source) || source is not int sourceIndex || sourceIndex != chain.Index)
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }
}
