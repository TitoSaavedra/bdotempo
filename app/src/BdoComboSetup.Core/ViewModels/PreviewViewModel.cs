using System.Collections.ObjectModel;
using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
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
/// The Preview page: the game screen with its 20 cooldown slots and 20 quick slots, each drawn at its saved position
/// (UIData rows of the selected layout). Positions are exact; nothing is dragged. The quick slot assignment of the
/// selected cooldown slot can be changed in memory.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    /// <summary>Background used when the user has not picked one.</summary>
    public const string DefaultBackgroundFileName = "2026-02-15_13735792.PNG";

    /// <summary>Game size used when GameOption.txt cannot be read.</summary>
    public const double FallbackGameWidth = 1920;

    /// <summary>Game size used when GameOption.txt cannot be read.</summary>
    public const double FallbackGameHeight = 1080;

    /// <summary>Demo cooldown: the dark sweep covers this fraction of the slot.</summary>
    public const double DemoCooldownRatio = 0.6;

    /// <summary>Number drawn on the demo cooldown.</summary>
    public const string DemoCooldownText = "12";

    /// <summary>The quick slots that the slot source provides (1 to 10). Quick slots 11-20 have no source yet.</summary>
    public const int SourcedQuickSlotCount = 10;

    private readonly ILocalizer _localizer;
    private readonly AppLogger _logger;
    private readonly GameUiLayoutSet? _uiLayouts;
    private readonly AppSettingsStore _settings;
    private readonly string _classSlug;
    private GameUiLayoutId _layoutId;
    private int _missingPanelCount;

    public PreviewViewModel(
        IPreviewSlotSource slots,
        string classSlug,
        GameDisplaySettings? displaySettings,
        IGameWindowSource gameWindowSource,
        GameUiLayoutSet? uiLayouts,
        IReadOnlyDictionary<int, int>? boundQuickKeys,
        AppSettingsStore settings,
        PreviewStartup startup,
        ILocalizer localizer,
        AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentException.ThrowIfNullOrWhiteSpace(classSlug);
        ArgumentNullException.ThrowIfNull(gameWindowSource);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _localizer = localizer;
        _logger = logger;
        _uiLayouts = uiLayouts;
        _settings = settings;
        _classSlug = classSlug;
        DisplaySettings = displaySettings;
        WindowStatus = gameWindowSource.GetStatus();
        GameWidth = displaySettings?.Width ?? FallbackGameWidth;
        GameHeight = displaySettings?.Height ?? FallbackGameHeight;
        UiScale = displaySettings?.UiScale ?? 1.0;

        _layoutId = startup.Layout ?? ParseLayout(settings.Settings.SelectedLayout) ?? GameUiLayoutId.General;
        _showHiddenSlots = startup.ShowHiddenSlots;
        _cooldownDemo = startup.CooldownDemo;
        _zoom = CanvasZoom.ClampZoom(startup.Zoom ?? CanvasZoom.MinZoom);

        CooldownSlots = new ObservableCollection<SlotViewModel>(
            Enumerable.Range(1, GameSlotIndices.CooldownSlotCount)
                .Select(number => new SlotViewModel(number, slots.Cooldown(number), classSlug, localizer)));
        QuickSlots = new ObservableCollection<QuickSlotViewModel>(
            Enumerable.Range(1, GameSlotIndices.QuickSlotCount)
                .Select(position => new QuickSlotViewModel(
                    position,
                    position <= SourcedQuickSlotCount ? slots.Quick(position) : null,
                    classSlug,
                    QuickSlotLegend.Resolve(position, boundQuickKeys),
                    localizer)));
        QuickPositionOptions = BuildQuickPositionOptions(localizer, QuickSlots);
        RefreshQuickLinks();

        ResolveBackground();
        Recompute();
        SelectedSlot = CooldownSlots.FirstOrDefault(slot => slot.HasSkill);
    }

    /// <summary>Cooldown slots 1 to 20.</summary>
    public ObservableCollection<SlotViewModel> CooldownSlots { get; }

    /// <summary>Quick slots 1 to 20 (1-10 are the main set; slot 10 is the key 0).</summary>
    public ObservableCollection<QuickSlotViewModel> QuickSlots { get; }

    /// <summary>Entries for the quick slot editor: "not on the quick bar", then quick slots 1 to 10.</summary>
    public IReadOnlyList<QuickPositionOption> QuickPositionOptions { get; }

    public GameDisplaySettings? DisplaySettings { get; }

    public GameWindowStatus WindowStatus { get; }

    /// <summary>Game screen size in game pixels. The canvas is drawn at this size and scaled.</summary>
    public double GameWidth { get; }

    /// <summary>Game screen size in game pixels.</summary>
    public double GameHeight { get; }

    /// <summary>UI scale of the game. Slot sizes are multiplied by it; positions are not.</summary>
    public double UiScale { get; }

    public GameUiLayoutId SelectedLayout => _layoutId;

    /// <summary>Set by the mouse wheel and the zoom keys. 1 = fit to the window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenScale), nameof(ZoomText))]
    private double _zoom = CanvasZoom.MinZoom;

    /// <summary>Scale at which the whole game screen fits the viewport. Set by the view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenScale), nameof(ZoomText))]
    private double _fitScale = 1;

    /// <summary>The scale of the canvas on screen: fit times zoom. The view applies it as a layout transform.</summary>
    public double ScreenScale => FitScale * Zoom;

    /// <summary>Zoom as a percentage of the real game size (100 = the game's own pixels).</summary>
    public string ZoomText => _localizer.Format("Preview.ZoomText", (int)Math.Round(ScreenScale * 100, MidpointRounding.AwayFromZero));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundStatusText))]
    private string? _backgroundPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundStatusText))]
    private BackgroundKind _backgroundKind = BackgroundKind.None;

    /// <summary>Draws the hidden game slots dimmed with a dashed outline. Off by default.</summary>
    [ObservableProperty]
    private bool _showHiddenSlots;

    /// <summary>Debug: a 60% cooldown on the first cooldown slot with a skill. Off by default.</summary>
    [ObservableProperty]
    private bool _cooldownDemo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(DetailTitle), nameof(DetailSubtitle), nameof(DetailCustomName), nameof(DetailMemo), nameof(IsEditorVisible), nameof(CanEdit), nameof(HasPendingChanges))]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private SlotViewModel? _selectedSlot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(DetailTitle), nameof(DetailSubtitle), nameof(DetailCustomName), nameof(DetailMemo))]
    private QuickSlotViewModel? _selectedQuickSlot;

    /// <summary>The quick position chosen in the editor (0 = not on the quick bar). Saved only by SaveChanges.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChanges))]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    private int _draftQuickPosition;

    public string BackgroundStatusText => _localizer.Get(BackgroundKind switch
    {
        BackgroundKind.Custom => "Preview.Background.Custom",
        BackgroundKind.Default => "Preview.Background.Default",
        _ => "Preview.Background.None",
    });

    public string LayoutStatusText => _uiLayouts is null
        ? _localizer.Get("Preview.Layout.FileMissing")
        : !_uiLayouts.Get(_layoutId).HasPanels
            ? _localizer.Get("Preview.Layout.NoRows")
            : _missingPanelCount > 0
                ? _localizer.Format("Preview.Layout.MissingPanels", _missingPanelCount)
                : string.Empty;

    public string ResolutionText => DisplaySettings is null
        ? _localizer.Get("Preview.Resolution.Unknown")
        : _localizer.Format(
            "Preview.Resolution",
            DisplaySettings.Width,
            DisplaySettings.Height,
            UiScale.ToString("0.##", CultureInfo.InvariantCulture));

    /// <summary>Status of the live game window (always "not connected" until the capture exists).</summary>
    public string GameStatusText => _localizer.Get(WindowStatus switch
    {
        GameWindowStatus.Running => "Preview.GameWindow.Running",
        GameWindowStatus.NotRunning => "Preview.GameWindow.NotRunning",
        _ => "Preview.GameWindow.NotConnected",
    });

    public bool HasSelection => SelectedSlot is not null || SelectedQuickSlot is not null;

    public string DetailTitle => SelectedSlot?.Title ?? SelectedQuickSlot?.Title ?? string.Empty;

    public string DetailSubtitle => SelectedSlot is { } slot
        ? _localizer.Format("Preview.Detail.CooldownSubtitle", slot.Number)
        : SelectedQuickSlot is { } quick
            ? _localizer.Format("Preview.Detail.QuickSubtitle", quick.Position, quick.KeyLabelOrNone)
            : string.Empty;

    public string DetailCustomName => SelectedSlot?.CustomName ?? SelectedQuickSlot?.CustomName ?? string.Empty;

    public string DetailMemo => SelectedSlot?.Memo ?? SelectedQuickSlot?.Memo ?? string.Empty;

    /// <summary>The quick position editor belongs to cooldown slots only.</summary>
    public bool IsEditorVisible => SelectedSlot is not null;

    public bool CanEdit => SelectedSlot is { HasSkill: true };

    public bool HasPendingChanges => SelectedSlot is not null && DraftQuickPosition != SelectedSlot.QuickPosition;

    partial void OnShowHiddenSlotsChanged(bool value)
    {
        foreach (var panel in AllPanels())
        {
            panel.UpdateVisibility(value);
        }

        _logger.Info("preview.show_hidden.changed", "Hidden slots shown or hidden", new Dictionary<string, object?>
        {
            ["enabled"] = value,
        });
    }

    partial void OnCooldownDemoChanged(bool value)
    {
        ApplyCooldownDemo();
        _logger.Info("preview.cooldown_demo.changed", "Cooldown demo switched", new Dictionary<string, object?>
        {
            ["enabled"] = value,
        });
    }

    partial void OnSelectedSlotChanged(SlotViewModel? value)
    {
        foreach (var slot in CooldownSlots)
        {
            slot.IsSelected = ReferenceEquals(slot, value);
        }

        DraftQuickPosition = value?.QuickPosition ?? 0;
        RefreshDetail();
    }

    partial void OnSelectedQuickSlotChanged(QuickSlotViewModel? value)
    {
        foreach (var quick in QuickSlots)
        {
            quick.IsSelected = ReferenceEquals(quick, value);
        }

        RefreshDetail();
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
    }

    /// <summary>The view reports its viewport size; the fit scale follows it.</summary>
    public void SetViewport(double width, double height)
    {
        FitScale = CanvasZoom.FitScale(width, height, GameWidth, GameHeight);
    }

    /// <summary>Zooms by mouse-wheel notches (positive zooms in). Never below fit.</summary>
    public void ZoomBy(int notches)
    {
        var before = Zoom;
        Zoom = CanvasZoom.Step(Zoom, notches);
        if (Zoom != before)
        {
            _logger.Debug("preview.zoom.changed", "Zoom changed", new Dictionary<string, object?>
            {
                ["zoom"] = Math.Round(Zoom, 3),
            });
        }
    }

    /// <summary>Back to fit: the whole game screen in the window.</summary>
    public void FitToView()
    {
        Zoom = CanvasZoom.MinZoom;
        _logger.Debug("preview.zoom.fit", "Fit to the window");
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

        SelectedQuickSlot = null;
        SelectedSlot = slot;
        _logger.Debug("preview.slot.selected", "Cooldown slot selected", new Dictionary<string, object?>
        {
            ["slot_number"] = slot.Number,
        });
    }

    [RelayCommand]
    private void SelectQuickSlot(QuickSlotViewModel? quickSlot)
    {
        if (quickSlot is null)
        {
            return;
        }

        SelectedSlot = null;
        SelectedQuickSlot = quickSlot;
        _logger.Debug("preview.quick_slot.selected", "Quick slot selected", new Dictionary<string, object?>
        {
            ["slot_number"] = quickSlot.Position,
        });
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
        if (previous == target)
        {
            return;
        }

        // A quick slot holds one skill. The skill that holds the target moves to this slot's old position, or leaves the bar.
        var displaced = target > 0 ? CooldownOnQuick(target) : null;
        if (ReferenceEquals(displaced, slot))
        {
            displaced = null;
        }

        if (previous > 0)
        {
            SetQuick(previous, null);
        }

        if (displaced is not null)
        {
            displaced.QuickPosition = previous;
            if (previous > 0)
            {
                SetQuick(previous, QuickDataOf(displaced));
            }
        }

        if (target > 0)
        {
            SetQuick(target, QuickDataOf(slot));
        }

        slot.QuickPosition = target;
        NotifyPendingChangesChanged();

        _logger.Info("preview.quick_slot.saved", "Quick slot position saved in memory", new Dictionary<string, object?>
        {
            ["slot_number"] = slot.Number,
            ["previous_position"] = previous,
            ["position"] = target,
            ["displaced_slot_number"] = displaced?.Number,
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

    /// <summary>Places every slot panel of the selected layout. A panel without a row is skipped and counted in the warning.</summary>
    private void Recompute()
    {
        var layout = _uiLayouts?.Get(_layoutId);
        var placements = GameSlotLayoutResolver.Resolve(layout, GameWidth, GameHeight);

        var missing = new List<int>();
        foreach (var placement in placements)
        {
            PanelFor(placement).Place(placement, UiScale, ShowHiddenSlots);
            if (!placement.Present)
            {
                missing.Add(placement.PanelIndex);
            }
        }

        _missingPanelCount = missing.Count;
        if (missing.Count > 0)
        {
            _logger.Warn("preview.layout.panels_missing", "Some slot panels have no row in this layout and are not drawn", new Dictionary<string, object?>
            {
                ["layout"] = _layoutId.ToString(),
                ["count"] = missing.Count,
                ["panel_indices"] = missing.ToArray(),
            });
        }

        ApplyCooldownDemo();
        _logger.Info("preview.layout.resolved", "Slot panels placed for the layout", new Dictionary<string, object?>
        {
            ["layout"] = _layoutId.ToString(),
            ["layout_read"] = layout is not null,
            ["panels_missing"] = missing.Count,
            ["game_width"] = GameWidth,
            ["game_height"] = GameHeight,
            ["ui_scale"] = UiScale,
        });

        OnPropertyChanged(nameof(LayoutStatusText));
    }

    private void ApplyCooldownDemo()
    {
        foreach (var slot in CooldownSlots)
        {
            slot.SetCooldownDemo(0, string.Empty);
        }

        if (!CooldownDemo)
        {
            return;
        }

        CooldownSlots.FirstOrDefault(slot => slot.HasSkill)?.SetCooldownDemo(DemoCooldownRatio, DemoCooldownText);
    }

    private IEnumerable<PanelViewModel> AllPanels() => CooldownSlots.Cast<PanelViewModel>().Concat(QuickSlots);

    private PanelViewModel PanelFor(GameSlotPlacement placement) => placement.Kind == SlotKind.Cooldown
        ? CooldownSlots[placement.Number - 1]
        : QuickSlots[placement.Number - 1];

    /// <summary>The cooldown slot whose skill sits on a quick slot, when that link is known.</summary>
    private SlotViewModel? CooldownOnQuick(int position)
    {
        var number = QuickSlots[position - 1].CooldownNumber;
        if (number is not (>= 1 and <= GameSlotIndices.CooldownSlotCount))
        {
            return null;
        }

        var slot = CooldownSlots[number.Value - 1];
        return slot.HasSkill ? slot : null;
    }

    private void SetQuick(int position, PreviewSlot? data) => QuickSlots[position - 1].SetSkill(data);

    private static PreviewSlot QuickDataOf(SlotViewModel slot) =>
        slot.Data! with { CooldownNumber = slot.Number };

    /// <summary>Derives the cooldown QuickPosition of every slot from the quick slots that hold its skill.</summary>
    private void RefreshQuickLinks()
    {
        foreach (var slot in CooldownSlots)
        {
            slot.QuickPosition = 0;
        }

        foreach (var quick in QuickSlots.Where(quick => quick.HasSkill && quick.CooldownNumber is >= 1 and <= GameSlotIndices.CooldownSlotCount))
        {
            CooldownSlots[quick.CooldownNumber!.Value - 1].QuickPosition = quick.Position;
        }
    }

    private void RefreshDetail()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailSubtitle));
        OnPropertyChanged(nameof(DetailCustomName));
        OnPropertyChanged(nameof(DetailMemo));
        OnPropertyChanged(nameof(IsEditorVisible));
        OnPropertyChanged(nameof(CanEdit));
        NotifyPendingChangesChanged();
    }

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

    private static IReadOnlyList<QuickPositionOption> BuildQuickPositionOptions(
        ILocalizer localizer,
        IEnumerable<QuickSlotViewModel> quickSlots)
    {
        var options = new List<QuickPositionOption> { new(0, localizer.Get("Preview.QuickPosition.None")) };
        options.AddRange(quickSlots
            .Where(quick => quick.Position <= SourcedQuickSlotCount)
            .Select(quick => new QuickPositionOption(
                quick.Position,
                localizer.Format("Preview.QuickPosition.Option", quick.Position, quick.KeyLabelOrNone))));
        return options;
    }

    private void NotifyPendingChangesChanged()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        SaveChangesCommand.NotifyCanExecuteChanged();
    }
}
