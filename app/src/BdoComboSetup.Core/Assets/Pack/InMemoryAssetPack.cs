using System.Diagnostics.CodeAnalysis;

namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>
/// An <see cref="IAssetPack"/> held in memory: the fake for tests and the empty pack the app uses when the real one
/// is missing or invalid (the UI then draws glyphs). It has no encryption and no integrity check of its own.
/// </summary>
public sealed class InMemoryAssetPack(IReadOnlyDictionary<string, byte[]> entries) : IAssetPack
{
    private readonly IReadOnlyDictionary<string, byte[]> _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    /// <summary>A pack with no entries.</summary>
    public static InMemoryAssetPack Empty { get; } = new(new Dictionary<string, byte[]>(StringComparer.Ordinal));

    public IReadOnlyList<string> Keys => _entries.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();

    public bool Contains(string key) => key is not null && _entries.ContainsKey(key);

    public bool TryGet(string key, [NotNullWhen(true)] out byte[]? bytes)
    {
        if (key is not null && _entries.TryGetValue(key, out var value))
        {
            bytes = value;
            return true;
        }

        bytes = null;
        return false;
    }

    public AssetPackVerification Verify() => new(_entries.Count, []);
}
