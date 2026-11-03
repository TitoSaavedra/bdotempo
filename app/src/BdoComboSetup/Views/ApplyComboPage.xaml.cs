using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
/// is, which slot it takes, what is written) live in the view models. The page also draws the two slot boards from the
/// working copy: it reads the steps and never changes a slot itself.
/// </summary>
public sealed partial class ApplyComboPage : Page
{
    /// <summary>The data-package property that tells a drop target which chain a dragged step came from.</summary>
    private const string SourceChainKey = "BdoComboSetup.SourceChain";

    /// <summary>The working copy the boards are drawn from. Null while no guide is open.</summary>
    private ComboEditorViewModel? _observedContent;

    public ApplyComboPage()
    {
        ViewModel = App.Services.ApplyCombo;
        InitializeComponent();
        ClassList.SetFields(item => SearchFields.ForClass(((ClassOptionViewModel)item).Info));
        ComboSlotPicker.SetFields(item => [((ComboSlotOption)item).Label]);
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    /// <summary>The texts a skill is searched by in the "Add skill" list: both names and the number.</summary>
    public static Func<object, IEnumerable<string?>> SkillSearchFields { get; } =
        item => SearchFields.ForSkill((SkillOption)item);

    public ApplyComboViewModel ViewModel { get; }

    /// <summary>The app's settings view model. The footer's reminder switch is the same setting as in the Settings page.</summary>
    public SettingsViewModel SettingsView => App.Services.SettingsView;

    /// <summary>The 20 cooldown slots of the working copy, in slot order.</summary>
    public ObservableCollection<ApplyBoardCell> CooldownCells { get; } = [];

    /// <summary>The nine quick positions of the working copy, in position order.</summary>
    public ObservableCollection<ApplyBoardCell> QuickCells { get; } = [];

    /// <summary>Maps the view model's severity to the InfoBar's enum (the view models do not reference WinUI).</summary>
    public InfoBarSeverity ToInfoBarSeverity(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Success => InfoBarSeverity.Success,
        StatusSeverity.Warning => InfoBarSeverity.Warning,
        StatusSeverity.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    /// <summary>The card of a step: accent outline when it is selected.</summary>
    public static Style CardStyle(bool selected) =>
        (Style)App.Current.Resources[selected ? "StepCardSelectedStyle" : "StepCardStyle"];

    /// <summary>A spec pill: accent when it is the selected spec.</summary>
    public static Style PillStyle(bool selected) =>
        (Style)App.Current.Resources[selected ? "PillSelectedStyle" : "PillStyle"];

    /// <summary>A class tile: accent when it is the selected class.</summary>
    public static Style ClassTileStyle(bool selected) =>
        (Style)App.Current.Resources[selected ? "ClassTileSelectedStyle" : "ClassTileStyle"];

    /// <summary>
    /// The line under the cooldown board: what the selected step holds ("#3 · name · key"), a hint to select a step, or a
    /// note that the selected step has no cooldown slot.
    /// </summary>
    public static string SlotHintText(string? cooldownLabel, string? stepName, string? keys)
    {
        if (string.IsNullOrEmpty(stepName))
        {
            return App.Services.Localizer.Get("Apply.SlotHint.Pick");
        }

        if (string.IsNullOrEmpty(cooldownLabel))
        {
            return App.Services.Localizer.Get("Apply.SlotHint.NoSlot");
        }

        return string.Join(" · ", new[] { cooldownLabel, stepName, keys }.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>The line under the guide title: class, source host and which copy is on screen, joined; empty parts are left out.</summary>
    public static string GuideMetaText(string? className, string? source, string? state) =>
        string.Join(" · ", new[] { className, source, state }.Where(text => !string.IsNullOrWhiteSpace(text)));

    /// <summary>A step shows its memo on its own line, except a hotkey step, whose key legend is the memo already.</summary>
    public static Visibility MemoLineVisibility(bool hasMemo, bool isHotkey) =>
        hasMemo && !isHotkey ? Visibility.Visible : Visibility.Collapsed;

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
        (App.MainWindow as MainWindow)?.OpenCharacterPicker();
    }

    /// <summary>Follows the working copy while the page is shown: the boards are drawn again when it changes.</summary>
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        AttachContent(ViewModel.Editor);
    }

    /// <summary>Stops following the working copy, so a page that is not shown is not kept alive by the view model.</summary>
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        AttachContent(null);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplyComboViewModel.Editor))
        {
            AttachContent(ViewModel.Editor);
        }
    }

    /// <summary>Moves the subscriptions to the new working copy and draws its boards.</summary>
    private void AttachContent(ComboEditorViewModel? editor)
    {
        if (_observedContent is not null)
        {
            _observedContent.ContentChanged -= Editor_ContentChanged;
            _observedContent.PropertyChanged -= Editor_PropertyChanged;
        }

        _observedContent = editor;
        if (editor is not null)
        {
            editor.ContentChanged += Editor_ContentChanged;
            editor.PropertyChanged += Editor_PropertyChanged;
        }

        RebuildBoards();
    }

    /// <summary>Every edit of a step (name, memo, slots, order) redraws the boards.</summary>
    private void Editor_ContentChanged(object? sender, EventArgs e) => RebuildBoards();

    /// <summary>A new selected step moves the highlight on the boards.</summary>
    private void Editor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComboEditorViewModel.SelectedStep))
        {
            RebuildBoards();
        }
    }

    /// <summary>
    /// Draws the cooldown board (slots 1 to 20) and the quick board (the nine positions) from the working copy: which step
    /// holds each one, and which step is selected.
    /// </summary>
    private void RebuildBoards()
    {
        var steps = _observedContent?.AllSteps().ToList() ?? [];
        var selected = _observedContent?.SelectedStep;
        var culture = CultureInfo.InvariantCulture;

        CooldownCells.Clear();
        for (var slot = 1; slot <= SlotAssignment.CooldownSlotCount; slot++)
        {
            var step = steps.FirstOrDefault(item => item.CooldownSlot == slot);
            var name = App.Services.Localizer.Format("Apply.CooldownCell", slot.ToString(culture));
            CooldownCells.Add(BoardCellFor(slot.ToString(culture), name, step, selected));
        }

        QuickCells.Clear();
        for (var position = 1; position <= QuickSlotMap.Count; position++)
        {
            var label = QuickSlotMap.InputFor(position).MouseButton ?? position.ToString(culture);
            var step = steps.FirstOrDefault(item => item.QuickPosition == position);
            QuickCells.Add(BoardCellFor(label, label, step, selected));
        }
    }

    /// <summary>One board cell: its caption names the slot and, when it holds a step, the step's name.</summary>
    private static ApplyBoardCell BoardCellFor(string label, string name, ComboStepViewModel? step, ComboStepViewModel? selected)
    {
        var localizer = App.Services.Localizer;
        var caption = step is null
            ? localizer.Format("Apply.BoardCell.Empty", name)
            : localizer.Format("Apply.BoardCell.Filled", name, step.DisplayName);
        return new ApplyBoardCell(label, caption, step, step is not null && ReferenceEquals(step, selected));
    }

    /// <summary>A board cell is clicked: its step becomes the selected step, where the details are edited.</summary>
    private void BoardCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ApplyBoardCell { Step: { } step } })
        {
            ViewModel.Editor?.SelectStep(step);
        }
    }

    /// <summary>Combos are written from Preview: this opens it, with the same character and combo.</summary>
    private void GoToPreview_Click(object sender, RoutedEventArgs e) => (App.MainWindow as MainWindow)?.ShowPreview();

    /// <summary>Chooses the class picked in the searchable class list (the page then lists its guides).</summary>
    private void ClassList_ItemChosen(object? sender, object item)
    {
        if (item is ClassOptionViewModel option)
        {
            option.SelectCommand.Execute(null);
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
    /// "Open the editor": the combo is not applied from here. The Editor shows the slots of this combo, lets the skills be
    /// changed there, and applies from its own button.
    /// </summary>
    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow window)
        {
            window.OpenEditor();
        }
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

/// <summary>
/// One cell of a slot board: a cooldown slot or a quick position, its label, the step in it (null when empty), its caption
/// for screen readers and tooltips, and whether that step is the selected one.
/// </summary>
public sealed class ApplyBoardCell(string label, string caption, ComboStepViewModel? step, bool isSelected)
{
    public string Label { get; } = label;

    public string Caption { get; } = caption;

    public ComboStepViewModel? Step { get; } = step;

    public bool HasStep => Step is not null;

    public bool IsSelected { get; } = isSelected;
}
