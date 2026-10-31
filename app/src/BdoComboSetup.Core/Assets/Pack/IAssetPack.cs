using System.Diagnostics.CodeAnalysis;

namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>Read access to the entries of an asset pack (or to an in-memory fake of one).</summary>
public interface IAssetPack
{
    /// <summary>All keys, sorted ordinal.</summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>True when the pack has an entry with this key. It does not decode the entry.</summary>
    bool Contains(string key);

    /// <summary>
    /// The decoded bytes of an entry. False when there is no such entry, or when the entry fails its check (the
    /// failure is logged). The array may be shared with a cache: callers must not modify it.
    /// </summary>
    bool TryGet(string key, [NotNullWhen(true)] out byte[]? bytes);

    /// <summary>Decodes and checks every entry and reports the ones that fail.</summary>
    AssetPackVerification Verify();
}

/// <summary>The result of <see cref="IAssetPack.Verify"/>.</summary>
public sealed record AssetPackVerification(int Entries, IReadOnlyList<AssetPackFailure> Failures)
{
    public bool IsValid => Failures.Count == 0;
}

/// <summary>An entry that failed its check, with the error code.</summary>
public sealed record AssetPackFailure(string Key, string Code);
