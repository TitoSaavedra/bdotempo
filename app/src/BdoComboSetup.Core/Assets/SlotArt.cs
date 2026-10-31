namespace BdoComboSetup.Core.Assets;

/// <summary>
/// Art of the game's slot UI that the app draws. The files come from the client export and are kept in the asset bundle
/// (git-ignored, copyrighted game art); the names of the files are in <see cref="BundleResourceNames"/>.
/// </summary>
public enum SlotArt
{
    /// <summary>The 44 x 44 semi-transparent background of a quick slot (the game's SlotBG).</summary>
    QuickSlotBackground,

    /// <summary>The 520 x 520 sheet of 100 radial sweep frames (10 x 10 cells of 52 x 52) of a cooldown slot.</summary>
    CooldownSweep,
}
