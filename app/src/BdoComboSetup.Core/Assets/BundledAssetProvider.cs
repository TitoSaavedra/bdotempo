using BdoComboSetup.Core.Icons;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Assets;

/// <summary>
/// The default <see cref="IAssetProvider"/>: the embedded bundle first, then the file catalog (Scrappers export and the
/// bdoclasshub folder through <see cref="IconCatalog"/>), then null. The source that answered is logged at debug level.
/// </summary>
public sealed class BundledAssetProvider(IEmbeddedResources resources, IconCatalog? fallback, AppLogger logger) : IAssetProvider
{
    private readonly IEmbeddedResources _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    private readonly IconCatalog? _fallback = fallback;
    private readonly AppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Stream? OpenSkillIcon(int skillNo, string? className = null)
    {
        var embedded = _resources.Open(BundleResourceNames.SkillIcon(skillNo));
        if (embedded is not null)
        {
            LogSource("skill", skillNo, "embedded");
            return embedded;
        }

        var path = _fallback?.GetSkillIcon(skillNo, className);
        if (path is not null)
        {
            LogSource("skill", skillNo, "file");
            return File.OpenRead(path);
        }

        LogSource("skill", skillNo, "none");
        return null;
    }

    public Stream? OpenClassIcon(int classType)
    {
        var embedded = _resources.Open(BundleResourceNames.ClassIcon(classType));
        if (embedded is not null)
        {
            LogSource("class", classType, "embedded");
            return embedded;
        }

        var path = _fallback?.GetClassIcon(classType);
        if (path is not null)
        {
            LogSource("class", classType, "file");
            return File.OpenRead(path);
        }

        LogSource("class", classType, "none");
        return null;
    }

    private void LogSource(string kind, int number, string source) =>
        _logger.Debug("asset.icon.resolved", "Icon resolved", new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["number"] = number,
            ["source"] = source,
        });
}
