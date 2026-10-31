using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Icons;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>Where the game screen background comes from.</summary>
public enum BackgroundKind
{
    /// <summary>A file the user picked (kept in settings).</summary>
    Custom,

    /// <summary>The default clean screenshot in Documents\Black Desert\ScreenShot.</summary>
    Default,

    /// <summary>No image: the dark gradient is shown.</summary>
    None,
}

/// <summary>
/// The Preview page: the game screen (background, cooldown bar, quick bar, optional UIData overlay) in game pixels,
/// and the detail of the selected cooldown slot. Only the quick slot position is editable; it is kept in memory.
/// Bar positions come from the layout file when verified, otherwise from the estimate or from the position the user dragged.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    /// <summary>Background used when the user has not picked one.</summary>
    public const string DefaultBackgroundFileName = "2026-02-15_13735792.PNG";

    /// <summary>Game size used when GameOption.txt cannot be read.</summary>
    public const double FallbackGameWidth = 1920;

    /// <summary>Game size used when GameOption.txt cannot be read.</summary>
    public const double FallbackGameHeight = 1080;

    private readonly ILocalizer _localizer;
    private readonly AppLogger _logger;
    private readonly GameUiLayoutSet? _uiLayouts;
    private readonly GameLayoutIndices _indices;
    private readonly AppSettingsStore _settings;
    private readonly IconCatalog _icons;
    private GameUiLayoutId _layoutId;

    public PreviewViewModel(
        ComboDefinition combo,
        GameLayoutIndices layoutIndices,
        GameDisplaySettings? displaySettings,
        IGameWindowSource gameWindowSource,
        GameUiLayoutSet? uiLayouts,
        IconCatalog icons,
        AppSettingsStore settings,
        GameUiLayoutId? initialLayout,
        bool initialShowUiPanels,
        ILocalizer localizer,
        AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(combo);
        ArgumentNullException.ThrowIfNull(layoutIndices);
        ArgumentNullException.ThrowIfNull(gameWindowSource);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _localizer = localizer;
        _logger = logger;
        _indices = layoutIndices;
        _uiLayouts = uiLayouts;
        _icons = icons;
        _settings = settings;
        DisplaySettings = displaySettings;
        WindowStatus = gameWindowSource.GetStatus();
        GameWidth = displaySettings?.Width ?? FallbackGameWidth;
        GameHeight = displaySettings?.Height ?? FallbackGameHeight;
        UiScale = displaySettings?.UiScale ?? 1.0;

        _layoutId = initialLayout ?? ParseLayout(settings.Settings.SelectedLayout) ?? GameUiLayoutId.General;
        LayoutOptions = Enum.GetValues<GameUiLayoutId>()
            .Select(id => new LayoutOption(id, localizer.Get(LayoutLabelKey(id))))
            .ToList();

        CooldownBar = new GameBarViewModel(BarKind.Cooldown, localizer);
        QuickBar = new GameBarViewModel(BarKind.Quick, localizer);
        CooldownBar.PropertyChanged += OnBarPropertyChanged;
        QuickBar.PropertyChanged += OnBarPropertyChanged;

        CooldownSlots = new ObservableCollection<SlotViewModel>(
            combo.Slots.OrderBy(slot => slot.Index).Select(slot => new SlotViewModel(slot, localizer)
            {
                IconPath = ResolveIconPath(slot, combo.ClassName),
            }));
        QuickSlots = new ObservableCollection<QuickSlotViewModel>(
            QuickSlotMap.Inputs.Select(input => new QuickSlotViewModel(input, localizer)));
        QuickPositionOptions = BuildQuickPositionOptions(localizer, QuickSlots);
        UiPanels = new ObservableCollection<UiPanelOverlayViewModel>();
        _showUiPanels = initialShowUiPanels;

        ResolveBackground();
        RefreshQuickSlots();
        SelectedSlot = CooldownSlots.FirstOrDefault(slot => !slot.IsEmpty);
        Recompute();
    }

    /// <summary>Entries for the quick position editor: "not assigned" first, then positions 1 to 9.</summary>
    public IReadOnlyList<QuickPositionOption> QuickPositionOptions { get; }

    /// <summary>Cooldown slots in index order (20 for the tamer combo).</summary>
    public ObservableCollection<SlotViewModel> CooldownSlots { get; }

    /// <summary>The nine quick bar positions, in position order.</summary>
    public ObservableCollection<QuickSlotViewModel> QuickSlots { get; }

    /// <summary>The layouts offered by the selector: General, Preset 1, Preset 2, Preset 3.</summary>
    public IReadOnlyList<LayoutOption> LayoutOptions { get; }

    public GameBarViewModel CooldownBar { get; }

    public GameBarViewModel QuickBar { get; }

    /// <summary>Visible UIData panels of the selected layout, drawn when <see cref="ShowUiPanels"/> is on.</summary>
    public ObservableCollection<UiPanelOverlayViewModel> UiPanels { get; }

    public GameDisplaySettings? DisplaySettings { get; }

    public GameWindowStatus WindowStatus { get; }

    /// <summary>Game screen size in game pixels. The canvas is drawn at this size and scaled by a Viewbox.</summary>
    public double GameWidth { get; }

    /// <summary>Game screen size in game pixels.</summary>
    public double GameHeight { get; }

    /// <summary>UI scale of the game. Slot sizes are multiplied by it; positions are not.</summary>
    public double UiScale { get; }

    public double CanvasAspectRatio => GameWidth / GameHeight;

    public GameUiLayoutId SelectedLayout => _layoutId;

    /// <summary>Two-way selection of the layout selector.</summary>
    public LayoutOption SelectedLayoutOption
    {
        get => LayoutOptions.First(option => option.Id == _layoutId);
        set
        {
            if (value is not null && value.Id != _layoutId)
            {
                SelectLayout(value.Id);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundStatusText))]
    private string? _backgroundPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundStatusText))]
    private BackgroundKind _backgroundKind = BackgroundKind.None;

    [ObservableProperty]
    private bool _showUiPanels;

    public string BackgroundStatusText => _localizer.Get(BackgroundKind switch
    {
        BackgroundKind.Custom => "Preview_Background_Custom",
        BackgroundKind.Default => "Preview_Background_Default",
        _ => "Preview_Background_None",
    });

    public bool IsLayoutVerified => _indices.IsVerified && CooldownBar.IsVerified && QuickBar.IsVerified;

    public string LayoutStatusText => _uiLayouts is null
        ? _localizer.Get("Preview_Layout_FileMissing")
        : !_uiLayouts.Get(_layoutId).HasPanels
            ? _localizer.Get("Preview_Layout_NoRows")
            : IsLayoutVerified
                ? string.Empty
                : _localizer.Get("Preview_LayoutUnknown");

    public string ResolutionText => DisplaySettings is null
        ? _localizer.Get("Preview_Resolution_Unknown")
        : _localizer.Format("Preview_Resolution", DisplaySettings.Width, DisplaySettings.Height);

    public string GameStatusText => _localizer.Get(WindowStatus switch
    {
        GameWindowStatus.Running => "Preview_GameWindow_Running",
        GameWindowStatus.NotRunning => "Preview_GameWindow_NotRunning",
        _ => "Preview_GameWindow_NotConnected",
    });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanEdit), nameof(HasPendingChanges))]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private SlotViewModel? _selectedSlot;

    /// <summary>The quick position chosen in the editor (0 = not on the quick bar). Saved only by SaveChanges.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChanges))]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private int _draftQuickPosition;

    public bool HasSelection => SelectedSlot is not null;

    public bool CanEdit => SelectedSlot is { IsEmpty: false };

    public bool HasPendingChanges => SelectedSlot is not null && DraftQuickPosition != SelectedSlot.QuickPosition;

    partial void OnSelectedSlotChanged(SlotViewModel? value)
    {
        foreach (var slot in CooldownSlots)
        {
            slot.IsSelected = ReferenceEquals(slot, value);
        }

        DraftQuickPosition = value?.QuickPosition ?? 0;
    }

    /// <summary>Picks another layout and remembers it in the settings.</summary>
    public void SelectLayout(GameUiLayoutId layout)
    {
        if (layout == _layoutId)
        {
            return;
        }

        _layoutId = layout;
        _settings.Update(settings => settings with { SelectedLayout = layout.ToString() });
        _logger.Info("preview.layout.selected", "Layout selected", new Dictionary<string, object?>
        {
            ["layout"] = layout.ToString(),
        });

        Recompute();
        OnPropertyChanged(nameof(SelectedLayout));
        OnPropertyChanged(nameof(SelectedLayoutOption));
    }

    /// <summary>Moves a bar that has no verified position. The move is shown at once and saved by <see cref="CommitBarPosition"/>.</summary>
    public void MoveBarTo(BarKind kind, double left, double top)
    {
        var bar = BarOf(kind);
        if (!bar.IsDraggable)
        {
            return;
        }

        var maxLeft = Math.Max(0, GameWidth - bar.Width);
        var maxTop = Math.Max(0, GameHeight - bar.Height);
        bar.Offset = new PixelOffset(Math.Clamp(left, 0, maxLeft), Math.Clamp(top, 0, maxTop));
    }

    /// <summary>Saves the current position of a dragged bar for the selected layout.</summary>
    public void CommitBarPosition(BarKind kind)
    {
        var bar = BarOf(kind);
        if (!bar.IsDraggable)
        {
            return;
        }

        var normalized = GameScreenLayoutResolver.Normalize(bar.Offset.Left, bar.Offset.Top, GameWidth, GameHeight);
        var key = AppSettings.ManualBarKey(_layoutId, kind);
        _settings.Update(settings =>
        {
            var positions = new Dictionary<string, NormalizedPosition>(settings.ManualBarPositions, StringComparer.Ordinal)
            {
                [key] = normalized,
            };
            return settings with { ManualBarPositions = positions };
        });

        _logger.Info("preview.bar.position_saved", "Bar position saved for the layout", new Dictionary<string, object?>
        {
            ["layout"] = _layoutId.ToString(),
            ["bar"] = kind.ToString(),
            ["x"] = Math.Round(normalized.X, 4),
            ["y"] = Math.Round(normalized.Y, 4),
        });
        Recompute();
    }

    /// <summary>Forgets the saved position of a bar for the selected layout.</summary>
    public void ResetBarPosition(BarKind kind)
    {
        var key = AppSettings.ManualBarKey(_layoutId, kind);
        _settings.Update(settings =>
        {
            var positions = new Dictionary<string, NormalizedPosition>(settings.ManualBarPositions, StringComparer.Ordinal);
            positions.Remove(key);
            return settings with { ManualBarPositions = positions };
        });

        _logger.Info("preview.bar.position_reset", "Saved bar position removed", new Dictionary<string, object?>
        {
            ["layout"] = _layoutId.ToString(),
            ["bar"] = kind.ToString(),
        });
        Recompute();
    }

    /// <summary>Uses a picture the user chose as the background. It must be an existing file.</summary>
    public bool SetBackgroundPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            _logger.Warn("preview.background.not_found", "The chosen background file does not exist");
            return false;
        }

        _settings.Update(settings => settings with { BackgroundImagePath = path });
        _logger.Info("preview.background.selected", "Background image chosen");
        ResolveBackground();
        return true;
    }

    /// <summary>Goes back to the default screenshot (or to the gradient when it is missing).</summary>
    public void ResetBackground()
    {
        _settings.Update(settings => settings with { BackgroundImagePath = null });
        _logger.Info("preview.background.reset", "Background reset to the default");
        ResolveBackground();
    }

    [RelayCommand]
    private void SelectSlot(SlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        SelectedSlot = slot;
        _logger.Debug("preview.slot.selected", "Cooldown slot selected", new Dictionary<string, object?>
        {
            ["slot_index"] = slot.Index,
            ["group"] = slot.Group.ToString(),
        });
    }

    [RelayCommand]
    private void SelectQuickSlot(QuickSlotViewModel? quickSlot)
    {
        if (quickSlot?.Slot is { } slot)
        {
            SelectSlot(slot);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private void SaveChanges()
    {
        var slot = SelectedSlot;
        if (slot is null || !CanEdit)
        {
            return;
        }

        var previous = slot.QuickPosition;
        var target = DraftQuickPosition;

        // A position holds one slot. If another slot has the target, it takes this slot's previous position.
        SlotViewModel? swappedWith = null;
        if (target > 0)
        {
            swappedWith = CooldownSlots.FirstOrDefault(other => !ReferenceEquals(other, slot) && other.QuickPosition == target);
            if (swappedWith is not null)
            {
                swappedWith.QuickPosition = previous;
            }
        }

        slot.QuickPosition = target;
        RefreshQuickSlots();
        NotifyPendingChangesChanged();

        _logger.Info("preview.quick_slot.saved", "Quick slot position saved in memory", new Dictionary<string, object?>
        {
            ["slot_index"] = slot.Index,
            ["previous_position"] = previous,
            ["position"] = target,
            ["swapped_slot_index"] = swappedWith?.Index,
        });
    }

    private bool CanSaveChanges() => HasPendingChanges && CanEdit;

    [RelayCommand]
    private void Reset()
    {
        DraftQuickPosition = SelectedSlot?.QuickPosition ?? 0;
    }

    private static GameUiLayoutId? ParseLayout(string? text) =>
        Enum.TryParse<GameUiLayoutId>(text, out var id) ? id : null;

    private static string LayoutLabelKey(GameUiLayoutId id) => id switch
    {
        GameUiLayoutId.Preset1 => "Preview_Layout_Preset1",
        GameUiLayoutId.Preset2 => "Preview_Layout_Preset2",
        GameUiLayoutId.Preset3 => "Preview_Layout_Preset3",
        _ => "Preview_Layout_General",
    };

    /// <summary>Resolves both bars, the slot positions and the overlay for the selected layout.</summary>
    private void Recompute()
    {
        var layout = _uiLayouts?.Get(_layoutId);
        var rows = Math.Clamp(_settings.Settings.CooldownRows, 1, 10);
        var columns = Math.Clamp(_settings.Settings.CooldownColumns, 1, 20);
        var input = new BarLayoutInput(
            GameWidth,
            GameHeight,
            UiScale,
            layout,
            _indices,
            rows,
            columns,
            ManualPosition(BarKind.Cooldown),
            ManualPosition(BarKind.Quick));

        var cooldown = GameScreenLayoutResolver.ResolveCooldown(input);
        var quick = GameScreenLayoutResolver.ResolveQuick(input);
        CooldownBar.Apply(cooldown);
        QuickBar.Apply(quick);
        RepositionSlots();

        UiPanels.Clear();
        foreach (var outline in GameScreenLayoutResolver.BuildOutlines(input))
        {
            var label = outline.WindowIndex is null
                ? outline.Index.ToString(CultureInfo.InvariantCulture)
                : $"{outline.Index}.{outline.WindowIndex}";
            UiPanels.Add(new UiPanelOverlayViewModel(
                new PixelOffset(outline.Left, outline.Top),
                outline.Width,
                outline.Height,
                label,
                outline.IsCandidate));
        }

        if (_indices.CooldownBarIndex is { } cooldownIndex && cooldown.Source != BarSource.UiData)
        {
            _logger.Warn("preview.bar.index_not_found", "The cooldown bar index is not a visible panel in this layout", new Dictionary<string, object?>
            {
                ["index"] = cooldownIndex,
                ["layout"] = _layoutId.ToString(),
            });
        }

        _logger.Info("preview.layout.resolved", "Bars placed for the layout", new Dictionary<string, object?>
        {
            ["layout"] = _layoutId.ToString(),
            ["layout_read"] = layout is not null,
            ["cooldown_source"] = cooldown.Source.ToString(),
            ["quick_source"] = quick.Source.ToString(),
            ["outlines"] = UiPanels.Count,
            ["game_width"] = GameWidth,
            ["game_height"] = GameHeight,
        });

        OnPropertyChanged(nameof(IsLayoutVerified));
        OnPropertyChanged(nameof(LayoutStatusText));
    }

    private NormalizedPosition? ManualPosition(BarKind kind) =>
        _settings.Settings.ManualBarPositions.TryGetValue(AppSettings.ManualBarKey(_layoutId, kind), out var position)
            ? position
            : null;

    private GameBarViewModel BarOf(BarKind kind) => kind == BarKind.Cooldown ? CooldownBar : QuickBar;

    private void OnBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameBarViewModel.Offset))
        {
            RepositionSlots();
        }
    }

    /// <summary>
    /// Places every slot relative to its bar (the view puts the bar at its own offset). Cooldown slots fill the rows left to right.
    /// </summary>
    private void RepositionSlots()
    {
        var cooldown = CooldownBar;
        for (var i = 0; i < CooldownSlots.Count; i++)
        {
            var slot = CooldownSlots[i];
            var column = cooldown.Columns > 0 ? i % cooldown.Columns : 0;
            var row = cooldown.Columns > 0 ? i / cooldown.Columns : 0;
            slot.IsPlaced = cooldown.Columns > 0 && row < cooldown.Rows;
            slot.Size = cooldown.SlotSize;
            slot.Offset = new PixelOffset(column * cooldown.PitchX, row * cooldown.PitchY);
        }

        var quick = QuickBar;
        for (var i = 0; i < QuickSlots.Count; i++)
        {
            var quickSlot = QuickSlots[i];
            quickSlot.Size = quick.SlotSize;
            quickSlot.Offset = new PixelOffset(i * quick.PitchX, 0);
        }
    }

    /// <summary>Picks the background: the user's file, else the default screenshot, else none (gradient).</summary>
    private void ResolveBackground()
    {
        var chosen = _settings.Settings.BackgroundImagePath;
        if (!string.IsNullOrWhiteSpace(chosen) && File.Exists(chosen))
        {
            BackgroundKind = BackgroundKind.Custom;
            BackgroundPath = chosen;
            return;
        }

        if (!string.IsNullOrWhiteSpace(chosen))
        {
            _logger.Warn("preview.background.missing", "The chosen background file is missing; the default is used");
        }

        var defaultPath = DefaultBackgroundPath;
        if (File.Exists(defaultPath))
        {
            BackgroundKind = BackgroundKind.Default;
            BackgroundPath = defaultPath;
            return;
        }

        BackgroundKind = BackgroundKind.None;
        BackgroundPath = null;
        _logger.Warn("preview.background.none", "No background screenshot found; the gradient is shown");
    }

    /// <summary>Default screenshot: Documents\Black Desert\ScreenShot\2026-02-15_13735792.PNG (read in place, never copied).</summary>
    public static string DefaultBackgroundPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Black Desert",
        "ScreenShot",
        DefaultBackgroundFileName);

    /// <summary>Icon of the skill, or null when the skill has no number or no icon exists.</summary>
    private string? ResolveIconPath(SlotData slot, string className) =>
        slot.SkillNo is { } skillNo ? _icons.GetSkillIcon(skillNo, className) : null;

    private void RefreshQuickSlots()
    {
        foreach (var quick in QuickSlots)
        {
            quick.Slot = CooldownSlots.FirstOrDefault(slot => slot.QuickPosition == quick.Position);
        }
    }

    private static IReadOnlyList<QuickPositionOption> BuildQuickPositionOptions(
        ILocalizer localizer,
        IEnumerable<QuickSlotViewModel> quickSlots)
    {
        var options = new List<QuickPositionOption> { new(0, localizer.Get("Preview_QuickNone")) };
        options.AddRange(quickSlots.Select(quick => new QuickPositionOption(
            quick.Position,
            localizer.Format("Preview_QuickPositionOption", quick.Position, quick.Label))));
        return options;
    }

    private void NotifyPendingChangesChanged()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        SaveChangesCommand.NotifyCanExecuteChanged();
    }
}
