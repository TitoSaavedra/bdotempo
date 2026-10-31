using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.ViewModels;
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
    }

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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Services.Logger.Error("apply.page.initialize_failed", "The Apply page could not select its startup guide", ex);
        }
    }

    private void CharacterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is CharacterOption option && !ReferenceEquals(option, ViewModel.SelectedCharacter))
        {
            ViewModel.SelectCharacterCommand.Execute(option);
        }
    }

    private void Step_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ComboStepViewModel step)
        {
            ViewModel.Editor?.SelectStep(step);
        }
    }

    private void AddToSlot_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ComboStepViewModel step })
        {
            ViewModel.Editor?.AddToFreeSlot(step);
        }
    }

    /// <summary>Adds the clicked skill to the chain the picker was opened from (the list's Tag).</summary>
    private void AddSkill_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ComboChainViewModel chain } && e.ClickedItem is SkillOption option)
        {
            chain.AddSkillCommand.Execute(option);
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
