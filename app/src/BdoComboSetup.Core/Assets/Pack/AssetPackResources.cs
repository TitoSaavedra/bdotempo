namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>
/// Exposes an asset pack through <see cref="IEmbeddedResources"/>, so the catalog and the icon provider read the pack
/// by entry name with the same abstraction they used for the loose bundle. Each call returns a new read-only stream
/// over the decoded bytes (the caller disposes it).
/// </summary>
public sealed class AssetPackResources(IAssetPack pack) : IEmbeddedResources
{
    private readonly IAssetPack _pack = pack ?? throw new ArgumentNullException(nameof(pack));

    public Stream? Open(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _pack.TryGet(name, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
    }
}
