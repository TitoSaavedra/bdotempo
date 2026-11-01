using System.Collections.ObjectModel;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// The Preview page: only the 20 cooldown slots and the 20 quick slots, each drawn at its saved position (UIData rows of the
/// selected layout), over the live capture of the game window (black when the game is not visible). The skills come from
/// <see cref="IPreviewSlotSource"/>, which follows the combo edited on the Apply page. The page also edits that combo: a click
/// on a cooldown slot opens the class skill catalog and the picked skill replaces the one of that slot (see
/// <see cref="ISlotSkillEditor"/>).
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject, IDisposable
{
    /// <summary>Game size used when GameOption.txt cannot be read.</summary>
    public const double FallbackGameWidth = 1920;

    /// <summary>Game size used when GameOption.txt cannot be read.</summary>
    public const double FallbackGameHeight = 1080;

    /// <summary>Debug cooldown: the remaining part of the sweep is this fraction of the cooldown.</summary>
    public const double DemoCooldownRatio = 0.6;

    /// <summary>Debug cooldown: seconds left (60% of a 20 second cooldown), drawn as text on the slot.</summary>
    public const int DemoCooldownSeconds = 12;

    /// <summary>Game pixels of empty space kept around the slots in the frame the preview opens on.</summary>
    public const double FrameMargin = 12;

    private readonly IPreviewSlotSource _slots;
    private readonly ISlotSkillEditor? _slotEditor;
    private readonly ILocalizer _localizer;
    private readonly AppLogger _logger;
    private readonly GameUiLayoutSet? _uiLayouts;
    private readonly AppSettingsStore _settings;
    private GameUiLayoutId _layoutId;
    private int? _skillClassType;
    private int _missingPanelCount;
    private double _viewportWidth;
    private double _viewportHeight;
    private bool _disposed;

    /// <param name="slotEditor">
    /// The write side of the page: what a pick in a cooldown slot changes. Null (the preview alone) leaves the slots read-only.
    /// </param>
    public PreviewViewModel(
        IPreviewSlotSource slots,
        GameDisplaySettings? displaySettings,
        IGameScreenSource gameScreen,
        GameUiLayoutSet? uiLayouts,
        AppSettingsStore settings,
        PreviewStartup startup,
        ILocalizer localizer,
        AppLogger logger,
        ISlotSkillEditor? slotEditor = null)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(gameScreen);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _slots = slots;
        _slotEditor = slotEditor;
        GameScreen = gameScreen;
        _localizer = localizer;
        _logger = logger;
        _uiLayouts = uiLayouts;
        _settings = settings;
        GameWidth = displaySettings?.Width ?? FallbackGameWidth;
        GameHeight = displaySettings?.Height ?? FallbackGameHeight;
        UiScale = displaySettings?.UiScale ?? 1.0;

        _layoutId = startup.Layout ?? ParseLayout(settings.Settings.SelectedLayout) ?? GameUiLayoutId.General;
        _showHiddenSlots = startup.ShowHiddenSlots;
        _cooldownDemo = startup.CooldownDemo;
        _zoom = CanvasZoom.ClampZoom(startup.Zoom ?? CanvasZoom.FitZoom);

        CooldownSlots = new ObservableCollection<SlotViewModel>(
            Enumerable.Range(1, SlotAssignment.CooldownSlotCount)
                .Select(number => new SlotViewModel(number, slots.Cooldown(number), localizer)));
        QuickSlots = new ObservableCollection<QuickSlotViewModel>(
            Enumerable.Range(1, QuickSlotKeys.SlotCount)
                .Select(position => new QuickSlotViewModel(position, slots.Quick(position), slots.QuickKey(position), localizer)));
        SkillOptions = new ObservableCollection<SkillOption>();
        LayoutOptions =
        [
            new LayoutOption(GameUiLayoutId.General, localizer.Get("Preview.Layout.General")),
            new LayoutOption(GameUiLayoutId.Preset1, localizer.Get("Preview.Layout.Preset1")),
            new LayoutOption(GameUiLayoutId.Preset2, localizer.Get("Preview.Layout.Preset2")),
            new LayoutOption(GameUiLayoutId.Preset3, localizer.Get("Preview.Layout.Preset3")),
        ];

        RefreshSkillOptions();
        Recompute();
        slots.Changed += OnSourceChanged;
    }

    /// <summary>Cooldown slots 1 to 20.</summary>
    public ObservableCollection<SlotViewModel> CooldownSlots { get; }

    /// <summary>Quick slots 1 to 20 (1-9 hold the combo's quick positions; slot 10 is the key 0).</summary>
    public ObservableCollection<QuickSlotViewModel> QuickSlots { get; }

    /// <summary>The four layouts of the layout selector, General first.</summary>
    public IReadOnlyList<LayoutOption> LayoutOptions { get; }

    /// <summary>
    /// The skills a cooldown slot can take: the class of the combo being edited at its top rank, from the class skill
    /// catalog. Refilled when the Apply page opens another class; empty when there is no catalog for the class.
    /// </summary>
    public ObservableCollection<SkillOption> SkillOptions { get; }

    /// <summary>True when clicking a cooldown slot can change its skill: a combo is open and its class has skills to pick.</summary>
    public bool CanPickSkill => _slotEditor is not null && SkillOptions.Count > 0;

    /// <summary>
    /// The line the slot picker leaves behind: empty normally, otherwise why the last pick did nothing (there is no combo
    /// open, or the slot has no step in the combo). The view shows it under the slots.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlotEditMessage))]
    private string _slotEditMessage = string.Empty;

    public bool HasSlotEditMessage => SlotEditMessage.Length > 0;

    /// <summary>Clears the line of the last pick (the view calls it when another slot is clicked).</summary>
    public void ClearSlotEditMessage() => SlotEditMessage = string.Empty;

    /// <summary>
    /// Puts a skill of the class catalog in a cooldown slot of the combo being edited: the one edit this page makes. The guide
    /// is never touched; the change is an edit of the working copy, so the Apply page shows it as unsaved and "Save my combo"
    /// keeps it. False when nothing was changed (see <see cref="SlotEditMessage"/>).
    /// </summary>
    public bool TryAssignSkill(int cooldownNumber, SkillOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        SlotEditMessage = string.Empty;

        if (_slotEditor is not { } editor)
        {
            return false;
        }

        if (editor.ClassType is null)
        {
            SlotEditMessage = _localizer.Get("Preview.SlotEdit.NoCombo");
            return false;
        }

        if (!editor.ReplaceCooldownSkill(cooldownNumber, option.SkillNo))
        {
            SlotEditMessage = _localizer.Format("Preview.SlotEdit.NoStep", cooldownNumber);
            return false;
        }

        _logger.Info("preview.slot_skill.replaced", "A skill was put in a cooldown slot from the slot editor", new Dictionary<string, object?>
        {
            ["slot"] = cooldownNumber,
            ["skill_no"] = option.SkillNo,
            ["class_type"] = editor.ClassType,
        });
        return true;
    }

    /// <summary>The background: the live capture of the game window, black when it is not visible.</summary>
    public IGameScreenSource GameScreen { get; }

    /// <summary>Game screen size in game pixels. The canvas is drawn at this size and scaled.</summary>
    public double GameWidth { get; }

    /// <summary>Game screen size in game pixels.</summary>
    public double GameHeight { get; }

    /// <summary>UI scale of the game. Slot sizes are multiplied by it; positions are not.</summary>
    public double UiScale { get; }

    public GameUiLayoutId SelectedLayout => _layoutId;

    /// <summary>The layout chosen in the selector. Setting it selects and remembers the layout.</summary>
    public LayoutOption SelectedLayoutOption
    {
        get => LayoutOptions.First(option => option.Id == _layoutId);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SelectLayout(value.Id);
        }
    }

    /// <summary>
    /// Set by the mouse wheel and the fit button. 1 = fit: the frame of the slots fills the viewport; below 1 the whole
    /// game screen comes into view.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenScale), nameof(ZoomText))]
    private double _zoom = CanvasZoom.FitZoom;

    /// <summary>Scale at which the frame of the slots fits the viewport. Recomputed when the viewport or the frame changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenScale), nameof(ZoomText))]
    private double _fitScale = 1;

    /// <summary>
    /// The area of the game screen that holds the drawn slots, with a margin, in game pixels. The preview opens on it, so
    /// the slots are as large as the viewport allows. Empty layouts frame the whole screen.
    /// </summary>
    public SlotFrame Frame { get; private set; }

    /// <summary>True once the view has reported a viewport with a size (until then the fit is not known).</summary>
    public bool ViewportKnown => _viewportWidth > 0 && _viewportHeight > 0;

    /// <summary>Raised when the view should move the scroll position to <see cref="Frame"/> (the fit button, a new layout).</summary>
    public event EventHandler? FrameRequested;

    /// <summary>The scale of the canvas on screen: fit times zoom. The view applies it as a render transform.</summary>
    public double ScreenScale => FitScale * Zoom;

    /// <summary>Zoom as a percentage of the real game size (100 = the game's own pixels).</summary>
    public string ZoomText => _localizer.Format("Preview.Zoom", (int)Math.Round(ScreenScale * 100, MidpointRounding.AwayFromZero));

    /// <summary>Draws the hidden game slots dimmed with a dashed outline. Off by default.</summary>
    [ObservableProperty]
    private bool _showHiddenSlots;

    /// <summary>
    /// Draws the live game behind the slots. Turned off, the capture is stopped: nothing of the game window is read.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaptureStatusText))]
    private bool _captureGame = true;

    /// <summary>Debug: a 60% cooldown on the first cooldown slot with a skill. Off by default; never saved.</summary>
    [ObservableProperty]
    private bool _cooldownDemo;

    /// <summary>Empty when the layout is complete; otherwise the one line that says what is missing.</summary>
    public string LayoutStatusText => _uiLayouts is null
        ? _localizer.Get("Preview.Layout.FileMissing")
        : !_uiLayouts.Get(_layoutId).HasPanels
            ? _localizer.Get("Preview.Layout.NoRows")
            : _missingPanelCount > 0
                ? _localizer.Format("Preview.Layout.MissingPanels", _missingPanelCount)
                : string.Empty;

    /// <summary>
    /// The one line about the background: empty while the game is captured, otherwise why the background is black.
    /// The view calls <see cref="RefreshCaptureStatus"/> when it polls the source.
    /// </summary>
    public string CaptureStatusText => !CaptureGame
        ? string.Empty
        : GameScreen.State switch
        {
            GameScreenState.Capturing => string.Empty,
            GameScreenState.Minimized => _localizer.Get("Preview.Capture.Minimized"),
            GameScreenState.Failed => _localizer.Get("Preview.Capture.Failed"),
            _ => _localizer.Get("Preview.Capture.NotRunning"),
        };

    /// <summary>Raises the property that shows the capture state (called on the UI thread after a poll).</summary>
    public void RefreshCaptureStatus() => OnPropertyChanged(nameof(CaptureStatusText));

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
        FrameRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The view reports its viewport size; the fit scale follows it.</summary>
    public void SetViewport(double width, double height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        UpdateFitScale();
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

    /// <summary>Back to fit: the slots fill the window (the frame of the slots, not the whole game screen).</summary>
    public void FitToView()
    {
        Zoom = CanvasZoom.FitZoom;
        _logger.Debug("preview.zoom.fit", "Fit to the slots");
        FrameRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _slots.Changed -= OnSourceChanged;
        }
    }

    private static GameUiLayoutId? ParseLayout(string? text) =>
        Enum.TryParse<GameUiLayoutId>(text, out var id) ? id : null;

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        foreach (var slot in CooldownSlots)
        {
            slot.Data = _slots.Cooldown(slot.Number);
        }

        foreach (var quick in QuickSlots)
        {
            quick.Data = _slots.Quick(quick.Position);
            quick.Key = _slots.QuickKey(quick.Position);
        }

        // Another guide can be a class with other skills: the picker follows the class of the combo being edited.
        RefreshSkillOptions();
        ApplyCooldownDemo();
        _logger.Debug("preview.source.refreshed", "Preview slots refreshed from the combo", new Dictionary<string, object?>
        {
            ["cooldown_filled"] = CooldownSlots.Count(slot => slot.HasSkill),
            ["quick_filled"] = QuickSlots.Count(quick => quick.HasSkill),
        });
    }

    /// <summary>
    /// Fills <see cref="SkillOptions"/> from the class of the combo being edited. It only rebuilds when the class changed:
    /// the options are the same SkillOption rows the editor resolves a saved skill with, so replacing one twice is cheap.
    /// </summary>
    private void RefreshSkillOptions()
    {
        var classType = _slotEditor?.ClassType;
        if (classType == _skillClassType)
        {
            return;
        }

        _skillClassType = classType;
        SkillOptions.Clear();
        if (classType is not null && _slotEditor is { } editor)
        {
            foreach (var option in editor.Skills)
            {
                SkillOptions.Add(option);
            }
        }

        OnPropertyChanged(nameof(CanPickSkill));
        _logger.Info("preview.skills.loaded", "Skills offered by the slot editor", new Dictionary<string, object?>
        {
            ["class_type"] = classType,
            ["skills"] = SkillOptions.Count,
        });
    }

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
        UpdateFrame();
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

        CooldownSlots.FirstOrDefault(slot => slot.HasSkill)?.SetCooldownDemo(
            DemoCooldownRatio,
            CooldownTimeText.Format(DemoCooldownSeconds, _localizer));
    }

    /// <summary>Recomputes the frame of the slots from the panels that are drawn, and the fit that goes with it.</summary>
    private void UpdateFrame()
    {
        var drawn = AllPanels()
            .Where(panel => panel.IsDrawn)
            .Select(panel => new SlotFrame(panel.Offset.Left, panel.Offset.Top, panel.Width, panel.Height));
        Frame = SlotFrame.Around(drawn, FrameMargin, GameWidth, GameHeight);
        UpdateFitScale();
        OnPropertyChanged(nameof(Frame));
    }

    private void UpdateFitScale() => FitScale = CanvasZoom.FitScale(_viewportWidth, _viewportHeight, Frame.Width, Frame.Height);

    private IEnumerable<PanelViewModel> AllPanels() => CooldownSlots.Cast<PanelViewModel>().Concat(QuickSlots);

    private PanelViewModel PanelFor(GameSlotPlacement placement) => placement.Kind == SlotKind.Cooldown
        ? CooldownSlots[placement.Number - 1]
        : QuickSlots[placement.Number - 1];

    partial void OnShowHiddenSlotsChanged(bool value)
    {
        foreach (var panel in AllPanels())
        {
            panel.UpdateVisibility(value);
        }

        UpdateFrame();

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
}
