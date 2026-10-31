namespace BdoComboSetup.Core.Game;

/// <summary>The four UI layouts the game keeps in gamevariable.xml.</summary>
public enum GameUiLayoutId
{
    General,
    Preset1,
    Preset2,
    Preset3,
}

/// <summary>Corner that PosX and PosY are measured from in a UIData row.</summary>
public enum UiAnchor
{
    LeftTop,
    LeftBottom,
    RightTop,
    RightBottom,
}

/// <summary>Point in game pixels, origin at the top-left corner of the screen.</summary>
public readonly record struct UiPoint(double X, double Y);

/// <summary>Rectangle in game pixels.</summary>
public readonly record struct UiRect(double Left, double Top, double Width, double Height);

/// <summary>
/// One UIData row (a UI panel). Position is as stored: offsets from <see cref="Anchor"/>, not screen coordinates.
/// RelativeX/RelativeY are the panel centre divided by the screen size (stored next to PosX/PosY).
/// Use <see cref="UiPanelGeometry"/> to turn it into game pixels.
/// </summary>
public sealed record UiPanel(
    int Index,
    int? WindowIndex,
    bool IsShow,
    UiAnchor? Anchor,
    double? PosX,
    double? PosY,
    double? SizeX,
    double? SizeY,
    double? RelativeX = null,
    double? RelativeY = null)
{
    public bool HasPosition => Anchor is not null && PosX is not null && PosY is not null;

    public bool HasSize => SizeX is > 0 && SizeY is > 0;

    /// <summary>True when the stored centre is usable. A (0, 0) centre is what the game writes for some stale rows.</summary>
    public bool HasRelativeCentre => RelativeX is not null && RelativeY is not null && (RelativeX != 0 || RelativeY != 0);
}

/// <summary>Converts the stored anchor offsets to game pixels for a screen of the given size.</summary>
public static class UiPanelGeometry
{
    /// <summary>
    /// Top-left (left and top edge) of the panel in game pixels, or null when the row has no position.
    /// LeftTop: (PosX, PosY). LeftBottom: (PosX, H - PosY). RightTop: (W - PosX, PosY). RightBottom: (W - PosX, H - PosY).
    /// </summary>
    public static UiPoint? TopLeft(UiPanel panel, double gameWidth, double gameHeight)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (!panel.HasPosition)
        {
            return null;
        }

        var x = panel.PosX!.Value;
        var y = panel.PosY!.Value;
        return panel.Anchor switch
        {
            UiAnchor.LeftTop => new UiPoint(x, y),
            UiAnchor.LeftBottom => new UiPoint(x, gameHeight - y),
            UiAnchor.RightTop => new UiPoint(gameWidth - x, y),
            UiAnchor.RightBottom => new UiPoint(gameWidth - x, gameHeight - y),
            _ => null,
        };
    }
}

/// <summary>Rows of one layout. Find returns the screen panel of an index; chat windows (WindowIndex) are only used when nothing else matches.</summary>
public sealed class GameUiLayout(GameUiLayoutId id, IReadOnlyList<UiPanel> panels)
{
    public GameUiLayoutId Id { get; } = id;

    public IReadOnlyList<UiPanel> Panels { get; } = panels;

    public bool HasPanels => Panels.Count > 0;

    public UiPanel? Find(int index) =>
        Panels.FirstOrDefault(panel => panel.Index == index && panel.WindowIndex is null)
        ?? Panels.FirstOrDefault(panel => panel.Index == index);
}

/// <summary>The four layouts read from one gamevariable.xml. Every id is present; a layout without rows has no panels.</summary>
public sealed class GameUiLayoutSet(IReadOnlyDictionary<GameUiLayoutId, GameUiLayout> layouts, int malformedRows)
{
    public IReadOnlyDictionary<GameUiLayoutId, GameUiLayout> Layouts { get; } = layouts;

    /// <summary>Rows that had an Index but could not be read (logged at load time).</summary>
    public int MalformedRows { get; } = malformedRows;

    public GameUiLayout Get(GameUiLayoutId id) => Layouts[id];
}

/// <summary>
/// Maps the game's Preset 1-3 to the UISettingPreset blocks of gamevariable.xml.
/// ASSUMPTION: Preset N = UISettingPreset(N-1). This is the only place that decides it; there is no Preset3 block name in the file.
/// </summary>
public static class GameUiLayoutBlocks
{
    public static IReadOnlyDictionary<GameUiLayoutId, int> PresetBlockNumbers { get; } = new Dictionary<GameUiLayoutId, int>
    {
        [GameUiLayoutId.Preset1] = 0,
        [GameUiLayoutId.Preset2] = 1,
        [GameUiLayoutId.Preset3] = 2,
    };
}
