using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Search;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Views.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace BdoComboSetup.Views;

/// <summary>
/// The "Apply combo" page. The page only translates clicks and drags into view model calls; the rules (what a step
/// is, which slot it takes, what is written) live in the view models. The page also draws the two slot boards from the
/// working copy and keeps the drag in progress.
/// </summary>
public sealed partial class ApplyComboPage : Page
{
    /// <summary>The data-package property that tells a drop target which chain a dragged step came from.</summary>
    private const string SourceChainKey = "BdoComboSetup.SourceChain";

    /// <summary>The working copy the boards are drawn from. Null while no guide is open.</summary>
    private ComboEditorViewModel? _observedContent;

    /// <summary>
    /// The drag in progress: the step and the board it comes from (null when it is dragged from a chain card or the mobility
    /// pool). It is set when a drag starts and cleared when it ends; the drop targets read it.
    /// </summary>
    private BoardDrag? _drag;

    /// <summary>The board cell that shows the drop highlight while a drag hovers it, or null.</summary>
    private ApplyBoardCell? _dropCell;

    public ApplyComboPage()
    {
        ViewModel = App.Services.ApplyCombo;
        InitializeComponent();
        ClassList.SetFields(item => SearchFields.ForClass(((ClassOptionViewModel)item).Info));
        ComboSlotPicker.SetFields(item => [((ComboSlotOption)item).Label]);
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        ViewModel.CharacterChooser = ChooseCharacterAsync;

        // The handlers see the events even when the chain list already handled them (a drop on a card, a reorder).
        ChainsScroll.AddHandler(UIElement.DragOverEvent, new DragEventHandler(ChainsArea_DragOver), handledEventsToo: true);
        ChainsScroll.AddHandler(UIElement.DropEvent, new DragEventHandler(ChainsArea_Drop), handledEventsToo: true);
    }

    /// <summary>The texts a skill is searched by in the "Add skill" list: both names and the number.</summary>
    public static Func<object, IEnumerable<string?>> SkillSearchFields { get; } =
        item => SearchFields.ForSkill((SkillOption)item);

    public ApplyComboViewModel ViewModel { get; }

    /// <summary>The app's settings view model. The footer's reminder switch is the same setting as in the Settings page.</summary>
    public SettingsViewModel SettingsView => App.Services.SettingsView;

    /// <summary>The 20 cooldown slots of the working copy, in slot order.</summary>
    public ObservableCollection<ApplyBoardCell> CooldownCells { get; } = [];

    /// <summary>The quick positions of the working copy (1 to <see cref="QuickSlotKeys.ComboPositionCount"/>), in position order.</summary>
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

    /// <summary>The opacity of a character without game data: dimmed, still readable, and never selectable.</summary>
    public static double CharacterOpacityOf(bool hasGameData) => hasGameData ? 1 : UnreadyOpacity;

    private const double UnreadyOpacity = 0.55;

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
        _ = ViewModel.ChangeCharacterAsync();
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
        if (ViewModel.CharacterChooser == ChooseCharacterAsync)
        {
            ViewModel.CharacterChooser = null;
        }

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
    /// Draws the cooldown board (slots 1 to 20) and the quick board (every quick position) from the working copy: which step
    /// holds each one, and which step is selected.
    /// </summary>
    private void RebuildBoards()
    {
        var steps = _observedContent?.AllSteps().ToList() ?? [];
        var selected = _observedContent?.SelectedStep;
        var localizer = App.Services.Localizer;
        var culture = CultureInfo.InvariantCulture;

        CooldownCells.Clear();
        for (var slot = 1; slot <= SlotAssignment.CooldownSlotCount; slot++)
        {
            var step = steps.FirstOrDefault(item => item.CooldownSlot == slot);
            var number = slot.ToString(culture);
            var name = localizer.Format("Apply.CooldownCell", number);
            CooldownCells.Add(BoardCellFor(ApplyBoardKind.Cooldown, slot, string.Empty, name, step, selected));
        }

        QuickCells.Clear();
        for (var position = 1; position <= QuickSlotKeys.ComboPositionCount; position++)
        {
            var step = steps.FirstOrDefault(item => item.QuickPosition == position);
            var name = localizer.Format("Apply.QuickCell", position.ToString(culture));

            // Only the positions with a default key have an input label; the others show their number alone.
            var input = QuickSlotKeys.DefaultKey(position) is null ? string.Empty : QuickSlotKeys.ComboLabel(position);
            QuickCells.Add(BoardCellFor(ApplyBoardKind.Quick, position, input, name, step, selected));
        }
    }

    /// <summary>One board cell: its caption names the slot and, when it holds a step, the step's name.</summary>
    private static ApplyBoardCell BoardCellFor(ApplyBoardKind kind, int number, string input, string name, ComboStepViewModel? step, ComboStepViewModel? selected)
    {
        var localizer = App.Services.Localizer;
        var caption = step is null
            ? localizer.Format("Apply.BoardCell.Empty", name)
            : localizer.Format("Apply.BoardCell.Filled", name, step.DisplayName);
        return new ApplyBoardCell(kind, number, input, name, caption, step, step is not null && ReferenceEquals(step, selected));
    }

    /// <summary>A board cell is tapped (a press without a drag): its step becomes the selected step, where the details are edited.</summary>
    private void BoardCell_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ApplyBoardCell { Step: { } step } })
        {
            ViewModel.Editor?.SelectStep(step);
        }
    }

    /// <summary>A filled board cell starts to be dragged: its step can take another cell, or be taken out of this one.</summary>
    private void BoardCell_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ApplyBoardCell { Step: { } step } cell })
        {
            StartDrag(step, cell.Kind, args);
        }
        else
        {
            args.Cancel = true;
        }
    }

    /// <summary>
    /// Every board cell, empty or filled, accepts the drag in progress (a chain step, a pending skill, or a cell of either board)
    /// while a drag is under way. It shows the target cell's highlight. The caption and the operation say what the drop does:
    /// "Copy to ..." when it makes a duplicate, "Move to ..." otherwise (the editor's swap rule moves the step that was in the cell).
    /// </summary>
    private void BoardCell_DragOver(object sender, DragEventArgs e)
    {
        DragLog("cell.drag_over", $"drag_set={_drag is not null} editor={ViewModel.Editor is not null} dc={(sender as FrameworkElement)?.DataContext?.GetType().Name}");
        if (_drag is null || ViewModel.Editor is not { } editor || sender is not FrameworkElement { DataContext: ApplyBoardCell cell })
        {
            e.AcceptedOperation = DataPackageOperation.None;
            SetDropCell(null);
            return;
        }

        var duplicates = editor.DropMakesDuplicate(_drag.Step, BoardOf(cell.Kind), cell.Number, HowOf(_drag, e.Modifiers));
        e.AcceptedOperation = duplicates ? DataPackageOperation.Copy : DataPackageOperation.Move;
        if (e.DragUIOverride is { } visual)
        {
            visual.Caption = App.Services.Localizer.Format(duplicates ? "Apply.DropCopyOnCell" : "Apply.DropOnCell", cell.Name);
            visual.IsCaptionVisible = true;
            visual.IsGlyphVisible = true;
        }

        SetDropCell(cell);
        e.Handled = true;
    }

    /// <summary>The pointer left a cell: its highlight goes off (the next cell it enters lights up on its own DragOver).</summary>
    private void BoardCell_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ApplyBoardCell cell } && ReferenceEquals(cell, _dropCell))
        {
            SetDropCell(null);
        }
    }

    private void BoardCell_Drop(object sender, DragEventArgs e)
    {
        DragLog("cell.drop", $"drag_set={_drag is not null}");
        SetDropCell(null);
        if (_drag is { } drag && sender is FrameworkElement { DataContext: ApplyBoardCell cell })
        {
            PlaceDrag(drag, cell.Kind, cell.Number, HowOf(drag, e.Modifiers));
        }

        _drag = null;
        e.Handled = true;
    }

    /// <summary>
    /// The chain list accepts a board cell's step: dropping it on the chains (or on its own card) takes it out of that cell.
    /// A step dragged from a chain card is left to the list's own reorder.
    /// </summary>
    private void ChainsArea_DragOver(object sender, DragEventArgs e)
    {
        SetDropCell(null);
        if (_drag?.Origin is not null)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private void ChainsArea_Drop(object sender, DragEventArgs e)
    {
        SetDropCell(null);
        if (_drag is { Origin: { } origin } drag)
        {
            PlaceDrag(drag, origin, null, BoardDrop.Move);
            _drag = null;
            e.Handled = true;
        }
    }

    /// <summary>A drag ended (dropped or cancelled): nothing is dragged any more, and no cell is highlighted.</summary>
    private void Drag_Completed(UIElement sender, DropCompletedEventArgs args)
    {
        _drag = null;
        SetDropCell(null);
    }

    /// <summary>Shows the drop highlight on one cell and removes it from the cell that had it.</summary>
    private void SetDropCell(ApplyBoardCell? cell)
    {
        if (ReferenceEquals(_dropCell, cell))
        {
            return;
        }

        if (_dropCell is not null)
        {
            _dropCell.IsDropTarget = false;
        }

        _dropCell = cell;
        if (cell is not null)
        {
            cell.IsDropTarget = true;
        }
    }

    /// <summary>
    /// A drop puts the dragged step where it was dropped, as the editor's drop rules say (see <see cref="ComboEditorViewModel.DropOnBoard"/>);
    /// a null position takes it out of its board. The step that now holds the position is selected, so the details and the
    /// highlight follow it.
    /// </summary>
    private void PlaceDrag(BoardDrag drag, ApplyBoardKind kind, int? position, BoardDrop how)
    {
        var editor = ViewModel.Editor;
        if (editor is null)
        {
            return;
        }

        var placed = drag.Step;
        if (position is int target)
        {
            placed = editor.DropOnBoard(drag.Step, BoardOf(kind), target, how);
        }
        else
        {
            SetBoardPosition(drag.Step, kind, null);
        }

        editor.SelectStep(placed);
        App.Services.Logger.Info("apply.board.dropped", "A step was dropped on a board", new Dictionary<string, object?>
        {
            ["target_board"] = kind.ToString(),
            ["position"] = position,
            ["origin_board"] = drag.Origin?.ToString(),
            ["how"] = how.ToString(),
            ["duplicated"] = !ReferenceEquals(placed, drag.Step),
        });
    }

    /// <summary>Takes a step out of a board position (the only use of a null position: a drop on the chains).</summary>
    private void SetBoardPosition(ComboStepViewModel step, ApplyBoardKind kind, int? position)
    {
        var editor = ViewModel.Editor;
        if (editor is null)
        {
            return;
        }

        switch (kind)
        {
            case ApplyBoardKind.Cooldown:
                editor.AssignCooldown(step, position);
                break;
            case ApplyBoardKind.Quick:
                editor.AssignQuick(step, position);
                break;
        }
    }

    private static ComboBoard BoardOf(ApplyBoardKind kind) => kind == ApplyBoardKind.Cooldown ? ComboBoard.Cooldown : ComboBoard.Quick;

    /// <summary>
    /// How a drag lands on a board: a chain card or the mobility pool places its step; a board cell moves it, or copies it while
    /// Ctrl is held.
    /// </summary>
    private static BoardDrop HowOf(BoardDrag drag, Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers modifiers) =>
        drag.Origin is null ? BoardDrop.Place : modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control) ? BoardDrop.Copy : BoardDrop.Move;

    /// <summary>
    /// Starts a drag of a step: its name is the drag data. The source allows Move and Copy; the target picks Copy when the drop
    /// makes a duplicate (a Ctrl drag of a cell, or a card of a step that already holds a slot on the board) and Move otherwise.
    /// </summary>
    private void StartDrag(ComboStepViewModel step, ApplyBoardKind? origin, DragStartingEventArgs args)
    {
        _drag = new BoardDrag(step, origin);
        args.AllowedOperations = DataPackageOperation.Move | DataPackageOperation.Copy;
        // Both: a drop that makes a duplicate answers Copy, any other drop answers Move, and an answer outside the requested
        // operations is refused by the system (the pointer shows the forbidden sign).
        args.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
        args.Data.SetText(step.DisplayName);
    }

    /// <summary>Combos are written from Preview: this opens it, with the same character and combo.</summary>
    private void GoToPreview_Click(object sender, RoutedEventArgs e) => (App.MainWindow as MainWindow)?.ShowPreview();

    /// <summary>"Change": asks again which character to use.</summary>
    private async void ChangeCharacter_Click(object sender, RoutedEventArgs e) => await ViewModel.ChangeCharacterAsync();

    /// <summary>
    /// The dialog that asks which character to apply to: shown when the chosen class has several characters in the account, and
    /// by "Change". Returns the character the user taps, or null when the dialog is closed without a choice. The characters
    /// without game data of the same class are listed dimmed after the choosable ones; tapping them does nothing.
    /// </summary>
    private async Task<CharacterOption?> ChooseCharacterAsync(IReadOnlyList<CharacterOption> candidates, ClassOptionViewModel? option)
    {
        var localizer = App.Services.Localizer;
        var unready = ViewModel.UnreadyCharacters.Where(item => option is null || item.ClassType == option.ClassType);
        var list = new ListView
        {
            ItemsSource = candidates.Concat(unready).ToList(),
            ItemTemplate = (DataTemplate)Resources["CharacterTemplate"],
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            MaxHeight = 320,
        };
        var body = option is null
            ? localizer.Get("Apply.CharacterDialog.BodyAny")
            : localizer.Format("Apply.CharacterDialog.Body", candidates.Count, option.Name);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(list);

        CharacterOption? chosen = null;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = localizer.Get("Apply.CharacterDialog.Title"),
            Content = panel,
            CloseButtonText = localizer.Get("Apply.CharacterDialog.Cancel"),
        };
        list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not CharacterOption { HasGameData: true } character)
            {
                return;
            }

            chosen = character;
            dialog.Hide();
        };

        await dialog.ShowAsync();
        return chosen;
    }

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

    /// <summary>
    /// Remembers which chain a dragged step belongs to, and that the step can be dropped on a board cell (from its card it has no
    /// cell of origin).
    /// </summary>
    private static void DragLog(string what, string detail = "") =>
        App.Services.Logger.Info("drag.trace", what, new Dictionary<string, object?> { ["detail"] = detail });

    private void Steps_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        DragLog("steps.drag_items_starting", $"items={e.Items.Count}");
        if (sender is ListView { Tag: ComboChainViewModel chain })
        {
            e.Data.Properties[SourceChainKey] = chain.Index;
        }

        if (e.Items.FirstOrDefault() is ComboStepViewModel step)
        {
            _drag = new BoardDrag(step, null);
            e.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
            e.Data.SetText(step.SkillName);
        }
    }

    private void Steps_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => _drag = null;

    /// <summary>A mobility step can be dragged to a board cell, as the pending skills of the Editor can.</summary>
    private void ManualStep_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: ComboStepViewModel step })
        {
            StartDrag(step, null, args);
        }
        else
        {
            args.Cancel = true;
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

/// <summary>The two boards of the page: the cooldown slots and the quick positions.</summary>
public enum ApplyBoardKind
{
    Cooldown,
    Quick,
}

/// <summary>
/// One cell of a slot board: its board and number (a cooldown slot or a quick position), its input label (empty when it has
/// none), its name ("Slot 5"), the step in it (null when empty), its caption for screen readers and tooltips, whether that step
/// is the selected one, and whether a drag is hovering it (the drop highlight, the only property that changes).
/// </summary>
public sealed class ApplyBoardCell(ApplyBoardKind kind, int number, string input, string name, string caption, ComboStepViewModel? step, bool isSelected)
    : INotifyPropertyChanged
{
    private bool _isDropTarget;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ApplyBoardKind Kind { get; } = kind;

    public int Number { get; } = number;

    public string Label { get; } = number.ToString(CultureInfo.InvariantCulture);

    public string Input { get; } = input;

    public bool HasInput => Input.Length > 0;

    /// <summary>The cell's name without its step, such as "Slot 5"; the drop caption names it.</summary>
    public string Name { get; } = name;

    public string Caption { get; } = caption;

    public ComboStepViewModel? Step { get; } = step;

    public bool HasStep => Step is not null;

    public bool IsSelected { get; } = isSelected;

    /// <summary>True while a drag hovers this cell: the cell is drawn with the drop highlight.</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
            {
                return;
            }

            _isDropTarget = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDropTarget)));
        }
    }
}

/// <summary>What is being dragged on the Combos page: a step, and the board cell it comes from (null from a chain card or the mobility pool).</summary>
internal sealed record BoardDrag(ComboStepViewModel Step, ApplyBoardKind? Origin);
