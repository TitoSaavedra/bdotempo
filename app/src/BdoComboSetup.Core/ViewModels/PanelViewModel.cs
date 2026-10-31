using BdoComboSetup.Core.Game;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// One slot panel of the game screen: its placement in game pixels and whether it is drawn.
/// Width and Height are the nominal art size times the UI scale; Offset is the top-left corner that centres the
/// panel on the saved centre. The view draws the art at its nominal size and scales it by <see cref="Scale"/>.
/// </summary>
public abstract partial class PanelViewModel : ObservableObject
{
    /// <summary>Opacity of a slot the game hides. It is only drawn when "show hidden slots" is on.</summary>
    public const double HiddenOpacity = 0.45;

    private bool _showHiddenSlots;

    /// <summary>The placement read from the layout. Null until the layout is resolved.</summary>
    public GameSlotPlacement? Placement { get; private set; }

    [ObservableProperty]
    private PixelOffset _offset;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    /// <summary>The game's UI scale. The view scales the nominal art by it.</summary>
    [ObservableProperty]
    private double _scale = 1;

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
        _showHiddenSlots = showHiddenSlots;
        Scale = uiScale;
        ApplyPlacement(uiScale);
    }

    /// <summary>Changes whether hidden panels are drawn, keeping the placement.</summary>
    public void UpdateVisibility(bool showHiddenSlots)
    {
        _showHiddenSlots = showHiddenSlots;
        if (Placement is not null)
        {
            ApplyPlacement(Scale);
        }
    }

    /// <summary>The two letters of a skill name, or the slot number when the slot has no skill name.</summary>
    protected static string GlyphFor(string? skillName, int number)
    {
        if (skillName is null)
        {
            return number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        return new string(skillName.Where(char.IsLetter).Take(2).ToArray()).ToUpperInvariant();
    }

    private void ApplyPlacement(double uiScale)
    {
        var placement = Placement!;
        var (nominalWidth, nominalHeight) = GameSlotLayoutResolver.NominalSize(placement.Kind);
        Width = nominalWidth * uiScale;
        Height = nominalHeight * uiScale;
        Offset = new PixelOffset(placement.CentreX - (Width / 2), placement.CentreY - (Height / 2));

        IsPresent = placement.Present;
        IsHiddenPanel = placement.Present && !placement.IsShow;
        IsDrawn = placement.Present && (placement.IsShow || _showHiddenSlots);
        ShowsDashedOutline = IsHiddenPanel && _showHiddenSlots;
        Opacity = IsHiddenPanel ? HiddenOpacity : 1;
    }
}
