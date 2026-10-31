namespace BdoComboSetup.Core.Assets;

/// <summary>
/// Gives the image bytes for skill and class icons. Order: the embedded bundle, then the external catalog
/// (Scrappers export, bdoclasshub folder), then null. A null answer means the caller draws a glyph.
/// </summary>
public interface IAssetProvider
{
    /// <summary>A new stream of the skill's icon, or null. The caller disposes it.</summary>
    /// <param name="className">The class slug, used to find icons in the bdoclasshub folder. Optional.</param>
    Stream? OpenSkillIcon(int skillNo, string? className = null);

    /// <summary>A new stream of the class icon, or null. The caller disposes it.</summary>
    Stream? OpenClassIcon(int classType);
}
