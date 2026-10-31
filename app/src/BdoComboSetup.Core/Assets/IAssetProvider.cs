namespace BdoComboSetup.Core.Assets;

/// <summary>
/// Gives the image bytes for skill and class icons, and for the slot art, all read from the asset pack. A null answer
/// (no entry) means the caller draws a glyph (icons) or draws nothing (slot art).
/// </summary>
public interface IAssetProvider
{
    /// <summary>A new stream of the skill's icon, or null. The caller disposes it.</summary>
    /// <param name="className">The class slug, used to find icons in the bdoclasshub folder. Optional.</param>
    Stream? OpenSkillIcon(int skillNo, string? className = null);

    /// <summary>A new stream of the class icon, or null. The caller disposes it.</summary>
    Stream? OpenClassIcon(int classType);

    /// <summary>A new stream of the slot art (PNG), or null when the bundle has no such file. The caller disposes it.</summary>
    Stream? OpenSlotArt(SlotArt art);
}
