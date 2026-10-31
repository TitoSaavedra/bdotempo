using BdoComboSetup.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// One slot panel of the game screen: its placement in game pixels and whether it is drawn.
/// The view centres the slot art on the placement; sizes are the nominal art size times the UI scale.
/// </summary>
public abstract partial class PanelViewModel : ObservableObject
{
    /// <summary>Opacity of a slot the game hides. It is only drawn when "show hidden slots" is on.</summary>
    public const double HiddenOpacity = 0.45;

    private double _uiScale = 1;
    private bool _showHiddenSlots;

    /// <summary>The placement read from the layout. Null until the layout is resolved.</summary>
    public GameSlotPlacement? Placement { get; private set; }

    [ObservableProperty]
    private PixelOffset _offset;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    /// <summary>False when the layout has no row for this panel: the panel is skipped and logged.</summary>
    [ObservableProperty]
    private bool _isPresent;

    /// <summary>True when the game hides this panel (IsShow=false) but its row keeps a saved position.</summary>
    [ObservableProperty]
    private bool _isHiddenPanel;

    [ObservableProperty]
    private bool _isDrawn;

    [ObservableProperty]
    private bool _showsDashedOutline;

    [ObservableProperty]
    private double _opacity = 1;

    /// <summary>Stores the placement and applies it.</summary>
    public void Place(GameSlotPlacement placement, double uiScale, bool showHiddenSlots)
    {
        ArgumentNullException.ThrowIfNull(placement);
        Placement = placement;
        _uiScale = uiScale;
        _showHiddenSlots = showHiddenSlots;
        ApplyPlacement();
    }

    /// <summary>Changes whether hidden panels are drawn, keeping the placement.</summary>
    public void UpdateVisibility(bool showHiddenSlots)
    {
        _showHiddenSlots = showHiddenSlots;
        if (Placement is not null)
        {
            ApplyPlacement();
        }
    }

    /// <summary>The one-letter-pair glyph of a skill, or the slot number when the skill has no name.</summary>
    protected static string GlyphFor(string? skillName, int number)
    {
        if (skillName is null)
        {
            return number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        return new string(skillName.Where(char.IsLetter).Take(2).ToArray()).ToUpperInvariant();
    }

    private void ApplyPlacement()
    {
        var placement = Placement!;
        var (nominalWidth, nominalHeight) = GameSlotLayoutResolver.NominalSize(placement.Kind);
        Width = nominalWidth * _uiScale;
        Height = nominalHeight * _uiScale;
        Offset = new PixelOffset(placement.CentreX - (Width / 2), placement.CentreY - (Height / 2));

        IsPresent = placement.Present;
        IsHiddenPanel = placement.Present && !placement.IsShow;
        IsDrawn = placement.Present && (placement.IsShow || _showHiddenSlots);
        ShowsDashedOutline = IsHiddenPanel && _showHiddenSlots;
        Opacity = IsHiddenPanel ? HiddenOpacity : 1;
    }
}
