using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Assets;

/// <summary>
/// The default <see cref="IAssetProvider"/>: skill and class icons read from the asset pack (through the
/// <see cref="IEmbeddedResources"/> view of it, see <c>AssetPackResources</c>). No loose files are read: a missing entry
/// gives null, and the caller draws a glyph. The result of each lookup is logged at debug level.
/// </summary>
public sealed class BundledAssetProvider(IEmbeddedResources resources, AppLogger logger) : IAssetProvider
{
    private readonly IEmbeddedResources _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <param name="className">Not used: every skill icon is in the pack under its skill number.</param>
    public Stream? OpenSkillIcon(int skillNo, string? className = null)
    {
        var stream = _resources.Open(BundleResourceNames.SkillIcon(skillNo));
        LogSource("skill", skillNo, stream);
        return stream;
    }

    public Stream? OpenClassIcon(int classType)
    {
        var stream = _resources.Open(BundleResourceNames.ClassIcon(classType));
        LogSource("class", classType, stream);
        return stream;
    }

    private void LogSource(string kind, int number, Stream? stream) =>
        _logger.Debug("asset.icon.resolved", "Icon resolved", new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["number"] = number,
            ["found"] = stream is not null,
        });
}
