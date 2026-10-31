using BdoComboSetup.Core.Characters;
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

            if (startup.DemoWriteHint)
            {
                // Demo only: asks to write, so the reminder before a write is shown. The demo applier writes nothing.
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

    private void CharacterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is CharacterOption option && !ReferenceEquals(option, ViewModel.SelectedCharacter))
        {
            ViewModel.SelectCharacterCommand.Execute(option);
        }
    }

    /// <summary>
    /// Opens the editor of one character: the name and the class, set once and remembered. The name box starts from the
    /// name the character has now; a name left unchanged is not saved, so it keeps following the game.
    /// </summary>
    private void EditCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CharacterOption option } button)
        {
            return;
        }

        var localizer = App.Services.Localizer;
        var entry = ViewModel.EntryOf(option);
        var nameBox = new TextBox
        {
            Header = localizer.Get("Characters.EditNameHeader"),
            PlaceholderText = localizer.Get("Characters.EditNamePlaceholder"),
            Text = entry?.Name ?? option.Name,
            MaxLength = CharacterOverrideStore.MaxNameLength,
        };
        var automatic = new CheckBox
        {
            Content = localizer.Get("Characters.EditAutomaticClass"),
            IsChecked = entry?.ClassSource != ClassSource.Custom,
        };
        var classList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = ViewModel.ClassChoices,
            ItemTemplate = (DataTemplate)Resources["ClassChoiceTemplate"],
            Height = 260,
            IsEnabled = automatic.IsChecked != true,
        };
        classList.SelectedItem = ViewModel.ClassChoices.FirstOrDefault(choice => choice.ClassType == entry?.ClassType);
        automatic.Checked += (_, _) => classList.IsEnabled = false;
        automatic.Unchecked += (_, _) => classList.IsEnabled = true;

        var save = new Button { Content = localizer.Get("Characters.EditSave") };
        var reset = new Button { Content = localizer.Get("Characters.EditReset") };
        var flyout = new Flyout();
        save.Click += async (_, _) =>
        {
            flyout.Hide();
            var typed = nameBox.Text.Trim();
            var keepAutomaticName = entry is not null && entry.NameSource != NameSource.Custom && typed == entry.Name;
            var name = keepAutomaticName ? null : typed;
            var classType = automatic.IsChecked == true ? null : (classList.SelectedItem as ClassChoice)?.ClassType;
            await ViewModel.SaveCharacterEditAsync(option, name, classType);
        };
        reset.Click += async (_, _) =>
        {
            flyout.Hide();
            await ViewModel.ResetCharacterEditAsync(option);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(save);
        buttons.Children.Add(reset);
        var panel = new StackPanel { Spacing = 10, Width = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = localizer.Get("Characters.EditTitle"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(automatic);
        panel.Children.Add(classList);
        panel.Children.Add(buttons);
        flyout.Content = panel;
        flyout.ShowAt(button);
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
