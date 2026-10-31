using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// One bar on the game screen (cooldown or quick). Its slots are positioned relative to <see cref="Offset"/>.
/// The position comes from <see cref="GameScreenLayoutResolver"/>; a bar with an unverified source can be dragged.
/// </summary>
public sealed partial class GameBarViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;

    public GameBarViewModel(BarKind kind, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        Kind = kind;
        Title = localizer.Get(kind == BarKind.Cooldown ? "Preview_Bar_Cooldown" : "Preview_Bar_Quick");
    }

    public BarKind Kind { get; }

    public string Title { get; }

    [ObservableProperty]
    private PixelOffset _offset;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraggable), nameof(IsVerified))]
    private BarSource _source = BarSource.Default;

    [ObservableProperty]
    private int? _indexUsed;

    [ObservableProperty]
    private string _note = string.Empty;

    public double SlotSize { get; private set; }

    public double PitchX { get; private set; }

    public double PitchY { get; private set; }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    /// <summary>Only a verified UIData position is exact; the others can be dragged by the user.</summary>
    public bool IsDraggable => Source != BarSource.UiData;

    public bool IsVerified => Source == BarSource.UiData;

    /// <summary>Copies a resolved placement into the bar and writes the note the user reads under the bar name.</summary>
    public void Apply(BarPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.Kind != Kind)
        {
            throw new ArgumentException($"A {placement.Kind} placement cannot be applied to the {Kind} bar.", nameof(placement));
        }

        SlotSize = placement.SlotSize;
        PitchX = placement.PitchX;
        PitchY = placement.PitchY;
        Columns = placement.Columns;
        Rows = placement.Rows;
        Width = placement.Width;
        Height = placement.Height;
        IndexUsed = placement.IndexUsed;
        Source = placement.Source;
        Offset = new PixelOffset(placement.Left, placement.Top);
        Note = placement.Source switch
        {
            BarSource.UiData => _localizer.Format("Preview_Bar_NoteUiData", placement.IndexUsed ?? 0),
            BarSource.Candidates => _localizer.Get("Preview_Bar_NoteCandidates"),
            BarSource.Manual => _localizer.Get("Preview_Bar_NoteManual"),
            _ => _localizer.Get("Preview_Bar_NoteDefault"),
        };
    }
}
