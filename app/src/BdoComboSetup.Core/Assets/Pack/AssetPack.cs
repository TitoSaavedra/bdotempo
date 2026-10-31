using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>Options of <see cref="AssetPack.Open"/>.</summary>
/// <param name="CacheBudgetBytes">The bytes of decoded entries kept in memory (least recently used go first).</param>
/// <param name="Keys">The key set. Null uses <see cref="AssetPackFormat.DefaultKeys"/>; tests pass another one for a wrong-key check.</param>
/// <param name="AllowParts">False (the app): a part file is refused with code <c>is_part</c>. True (the assembler): parts are opened.</param>
public sealed record AssetPackOptions(long CacheBudgetBytes = AssetPack.DefaultCacheBudgetBytes, AssetPackKeys? Keys = null, bool AllowParts = false);

/// <summary>An entry as stored: its key, compression, lengths, SHA-256 and the encrypted blob exactly as in the file.</summary>
internal sealed record PackedBlob(string Key, byte Compression, int OriginalLength, byte[] Sha256, byte[] Blob);

/// <summary>
/// Reads a .bdoa pack (format: app/docs/asset-pack-format.md).
/// <see cref="Open"/> reads the header and the index once and checks the index MAC, so a wrong key or a damaged
/// index is refused before any entry is read. Each entry is read, decrypted, decompressed and hash-checked on its
/// first use, and kept in a bounded LRU cache of decoded bytes.
/// Thread-safe: the index is immutable, stream reads are serialized, and the cache has its own lock.
/// The pack owns the stream it was opened from and disposes it.
/// </summary>
public sealed class AssetPack : IAssetPack, IDisposable
{
    public const long DefaultCacheBudgetBytes = 16 * 1024 * 1024;

    private readonly object _streamGate = new();
    private readonly Stream _source;
    private readonly long _baseOffset;
    private readonly AssetPackKeys _keys;
    private readonly Dictionary<string, IndexEntry> _entries;
    private readonly string[] _keyList;
    private readonly ByteLruCache _cache;
    private readonly AppLogger _logger;
    private int _disposed;

    private AssetPack(
        Stream source,
        long baseOffset,
        AssetPackKeys keys,
        Dictionary<string, IndexEntry> entries,
        ByteLruCache cache,
        AppLogger logger,
        PartInfo? part)
    {
        _source = source;
        _baseOffset = baseOffset;
        _keys = keys;
        _entries = entries;
        _keyList = entries.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        _cache = cache;
        _logger = logger;
        Part = part;
    }

    /// <summary>The part fields when the file is one part of a set; null for a complete pack.</summary>
    public PartInfo? Part { get; }

    public IReadOnlyList<string> Keys
    {
        get
        {
            ThrowIfDisposed();
            return _keyList;
        }
    }

    /// <summary>Opens a pack. A non-seekable stream is copied into memory first. Throws <see cref="AssetPackException"/> on any failed check.</summary>
    public static AssetPack Open(Stream source, AppLogger logger, AssetPackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(logger);
        options ??= new AssetPackOptions();

        using var scope = logger.BeginOperation("asset.pack.open");
        var stream = source;
        var opened = false;
        try
        {
            if (!stream.CanSeek)
            {
                var memory = new MemoryStream();
                source.CopyTo(memory);
                memory.Position = 0;
                source.Dispose();
                stream = memory;
            }

            var pack = Read(stream, options, logger);
            opened = true;
            return pack;
        }
        catch (IOException ex)
        {
            scope.Fail(ex);
            throw new AssetPackException(AssetPackErrorCodes.ReadFailed, "The asset pack could not be read.", ex);
        }
        catch (AssetPackException ex)
        {
            scope.Fail(ex);
            throw;
        }
        finally
        {
            if (!opened)
            {
                stream.Dispose();
            }
        }
    }

    public bool Contains(string key)
    {
        ThrowIfDisposed();
        return key is not null && _entries.ContainsKey(key);
    }

    /// <summary>Decodes one entry (or returns it from the cache). A failed entry is logged and reported as false.</summary>
    public bool TryGet(string key, [NotNullWhen(true)] out byte[]? bytes)
    {
        ArgumentNullException.ThrowIfNull(key);
        ThrowIfDisposed();

        if (!_entries.TryGetValue(key, out var entry))
        {
            bytes = null;
            _logger.Debug("asset.pack.entry_missing", "The pack has no entry with this key", new Dictionary<string, object?>
            {
                ["key"] = key,
            });
            return false;
        }

        if (_cache.TryGet(key, out var cached))
        {
            bytes = cached;
            return true;
        }

        try
        {
            var decoded = DecodeBlob(entry, ReadBlob(entry));
            _cache.Add(key, decoded);
            bytes = decoded;
            return true;
        }
        catch (AssetPackException ex)
        {
            _logger.Error("asset.pack.entry_invalid", "A pack entry failed its check and is not returned", ex, new Dictionary<string, object?>
            {
                ["key"] = key,
                ["code"] = ex.Code,
            });
            bytes = null;
            return false;
        }
    }

    /// <summary>Decodes every entry without the cache and reports the failures. Logs one summary line.</summary>
    public AssetPackVerification Verify()
    {
        ThrowIfDisposed();
        var failures = new List<AssetPackFailure>();
        foreach (var key in _keyList)
        {
            try
            {
                var entry = _entries[key];
                _ = DecodeBlob(entry, ReadBlob(entry));
            }
            catch (AssetPackException ex)
            {
                failures.Add(new AssetPackFailure(key, ex.Code));
                _logger.Error("asset.pack.entry_invalid", "A pack entry failed verification", ex, new Dictionary<string, object?>
                {
                    ["key"] = key,
                    ["code"] = ex.Code,
                });
            }
        }

        _logger.Info("asset.pack.verified", "Asset pack verified", new Dictionary<string, object?>
        {
            ["entries"] = _keyList.Length,
            ["failures"] = failures.Count,
        });
        return new AssetPackVerification(_keyList.Length, failures);
    }

    /// <summary>
    /// Every entry, verified (decrypted, decompressed, hash-checked), with its blob exactly as stored, in key order.
    /// Used by the assembler to copy the blobs into a complete pack without decrypting them to disk.
    /// Throws <see cref="AssetPackException"/> on the first failed entry.
    /// </summary>
    internal IReadOnlyList<PackedBlob> ReadVerifiedBlobs()
    {
        ThrowIfDisposed();
        var result = new List<PackedBlob>(_keyList.Length);
        foreach (var key in _keyList)
        {
            var entry = _entries[key];
            var blob = ReadBlob(entry);
            _ = DecodeBlob(entry, blob);
            result.Add(new PackedBlob(key, entry.Compression, entry.OriginalLength, entry.Sha256, blob));
        }

        return result;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_streamGate)
        {
            _source.Dispose();
        }

        _cache.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(AssetPack));
        }
    }

    private static AssetPack Read(Stream stream, AssetPackOptions options, AppLogger logger)
    {
        var baseOffset = stream.Position;
        var available = stream.Length - baseOffset;
        if (available < AssetPackFormat.HeaderSize)
        {
            throw new AssetPackException(AssetPackErrorCodes.Truncated, "The file is shorter than the pack header.");
        }

        var header = ReadAt(stream, baseOffset, AssetPackFormat.HeaderSize);
        if (!header.AsSpan(0, 4).SequenceEqual(AssetPackFormat.Magic))
        {
            throw new AssetPackException(AssetPackErrorCodes.BadMagic, "The file is not a BDOA asset pack.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
        if (version != AssetPackFormat.Version)
        {
            throw new AssetPackException(AssetPackErrorCodes.UnsupportedVersion, $"Unsupported asset pack version {version}.");
        }

        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        var indexOffset = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(16));
        var indexLength = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24));
        var fileLength = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32));
        var partNumber = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(40));
        var partCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(42));
        var setId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(44));

        if (headerSize != AssetPackFormat.HeaderSize || (flags & ~(uint)AssetPackFormat.FlagPart) != 0)
        {
            throw new AssetPackException(AssetPackErrorCodes.BadHeader, "The pack header has an unexpected size or unknown flags.");
        }

        var part = ReadPartFields(flags, partNumber, partCount, setId);
        if (part is not null && !options.AllowParts)
        {
            throw new AssetPackException(
                AssetPackErrorCodes.IsPart,
                $"This file is part {part.Number} of {part.Count} of an asset set; the complete assets.bdoa is required.");
        }

        if (fileLength != (ulong)available)
        {
            throw new AssetPackException(AssetPackErrorCodes.Truncated, "The file length does not match the header (truncated or extended).");
        }

        if (indexOffset < AssetPackFormat.HeaderSize
            || indexOffset > fileLength
            || indexLength > fileLength - indexOffset
            || indexOffset + indexLength != fileLength
            || indexLength > AssetPackFormat.MaxIndexBytes
            || entryCount > AssetPackFormat.MaxEntryCount)
        {
            throw new AssetPackException(AssetPackErrorCodes.BadIndex, "The pack index bounds are inconsistent.");
        }

        var index = ReadAt(stream, baseOffset + (long)indexOffset, (int)indexLength);
        var keys = options.Keys ?? AssetPackFormat.DefaultKeys;
        using (var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, keys.IndexMacKey))
        {
            mac.AppendData(header.AsSpan(0, AssetPackFormat.HeaderFieldsSize));
            mac.AppendData(index);
            var expected = mac.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(expected, header.AsSpan(AssetPackFormat.HeaderFieldsSize, AssetPackFormat.MacSize)))
            {
                throw new AssetPackException(
                    AssetPackErrorCodes.IndexAuthentication,
                    "The pack index failed authentication (wrong key or damaged pack).");
            }
        }

        var entries = ParseIndex(index, entryCount, indexOffset);
        var cache = new ByteLruCache(options.CacheBudgetBytes);
        logger.Info("asset.pack.index_read", "Asset pack index read", new Dictionary<string, object?>
        {
            ["entries"] = entries.Count,
            ["file_bytes"] = (long)fileLength,
            ["part"] = part is null ? null : $"{part.Number}/{part.Count}",
        });
        return new AssetPack(stream, baseOffset, keys, entries, cache, logger, part);
    }

    private static PartInfo? ReadPartFields(uint flags, ushort partNumber, ushort partCount, uint setId)
    {
        if ((flags & AssetPackFormat.FlagPart) == 0)
        {
            if (partNumber != 0 || partCount != 0 || setId != 0)
            {
                throw new AssetPackException(AssetPackErrorCodes.BadHeader, "A complete pack must not carry part fields.");
            }

            return null;
        }

        if (partNumber < 1 || partNumber > partCount || partCount > AssetPackFormat.MaxPartCount)
        {
            throw new AssetPackException(AssetPackErrorCodes.BadHeader, $"The part fields are invalid: part {partNumber} of {partCount}.");
        }

        return new PartInfo(partNumber, partCount, setId);
    }

    private static Dictionary<string, IndexEntry> ParseIndex(byte[] index, uint entryCount, ulong indexOffset)
    {
        var entries = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        var position = 0;
        string? previous = null;
        for (var i = 0; i < entryCount; i++)
        {
            if (index.Length - position < 2)
            {
                throw BadIndex("the index ends inside an entry key length");
            }

            var keyLength = BinaryPrimitives.ReadUInt16LittleEndian(index.AsSpan(position));
            position += 2;
            if (keyLength == 0 || keyLength > AssetPackFormat.MaxKeyBytes || index.Length - position < keyLength + AssetPackFormat.EntryFixedSize)
            {
                throw BadIndex("an entry key length is out of range or the index ends inside an entry");
            }

            var keyBytes = index.AsSpan(position, keyLength);
            if (!AssetPackFormat.IsValidKey(keyBytes))
            {
                throw BadIndex("an entry key has characters the format does not allow");
            }

            var key = Encoding.ASCII.GetString(keyBytes);
            var keyByteArray = keyBytes.ToArray();
            position += keyLength;

            var fixedFields = index.AsSpan(position, AssetPackFormat.EntryFixedSize);
            position += AssetPackFormat.EntryFixedSize;
            var compression = fixedFields[0];
            var entryFlags = fixedFields[1];
            var reserved = BinaryPrimitives.ReadUInt16LittleEndian(fixedFields[2..]);
            var storedOffset = BinaryPrimitives.ReadUInt64LittleEndian(fixedFields[4..]);
            var storedLength = BinaryPrimitives.ReadUInt32LittleEndian(fixedFields[12..]);
            var originalLength = BinaryPrimitives.ReadUInt32LittleEndian(fixedFields[16..]);
            var sha256 = fixedFields[20..AssetPackFormat.EntryFixedSize].ToArray();

            if (previous is not null && string.CompareOrdinal(key, previous) <= 0)
            {
                throw BadIndex("the index keys are not unique and sorted");
            }

            previous = key;
            if (compression is not (AssetPackFormat.CompressionNone or AssetPackFormat.CompressionDeflate or AssetPackFormat.CompressionBrotli))
            {
                throw BadIndex($"entry {key} uses an unknown compression id {compression}");
            }

            if ((entryFlags & ~AssetPackFormat.FlagEncrypted) != 0 || (entryFlags & AssetPackFormat.FlagEncrypted) == 0 || reserved != 0)
            {
                throw BadIndex($"entry {key} has unexpected flags");
            }

            if (originalLength > AssetPackFormat.MaxEntryBytes
                || storedLength < AssetPackFormat.NonceSize + AssetPackFormat.TagSize
                || storedLength > AssetPackFormat.MaxEntryBytes + AssetPackFormat.NonceSize + AssetPackFormat.TagSize)
            {
                throw BadIndex($"entry {key} has an invalid size");
            }

            if (storedOffset < AssetPackFormat.HeaderSize || storedOffset > indexOffset || storedLength > indexOffset - storedOffset)
            {
                throw BadIndex($"entry {key} points outside the blob area");
            }

            entries.Add(key, new IndexEntry(key, keyByteArray, compression, (long)storedOffset, (int)storedLength, (int)originalLength, sha256));
        }

        if (position != index.Length)
        {
            throw BadIndex("the index has bytes after the last entry");
        }

        return entries;
    }

    private static AssetPackException BadIndex(string detail) =>
        new(AssetPackErrorCodes.BadIndex, $"The pack index is invalid: {detail}.");

    private static byte[] ReadAt(Stream stream, long offset, int length)
    {
        var buffer = new byte[length];
        try
        {
            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new AssetPackException(AssetPackErrorCodes.Truncated, "The pack ends before the expected data.", ex);
        }

        return buffer;
    }

    private byte[] ReadBlob(IndexEntry entry)
    {
        var blob = new byte[entry.StoredLength];
        try
        {
            lock (_streamGate)
            {
                _source.Seek(_baseOffset + entry.StoredOffset, SeekOrigin.Begin);
                _source.ReadExactly(blob);
            }
        }
        catch (IOException ex)
        {
            throw new AssetPackException(AssetPackErrorCodes.EntryRead, $"Entry {entry.Key} could not be read.", ex);
        }

        return blob;
    }

    private byte[] DecodeBlob(IndexEntry entry, byte[] blob)
    {
        var nonce = blob.AsSpan(0, AssetPackFormat.NonceSize);
        var ciphertext = blob.AsSpan(AssetPackFormat.NonceSize, blob.Length - AssetPackFormat.NonceSize - AssetPackFormat.TagSize);
        var tag = blob.AsSpan(blob.Length - AssetPackFormat.TagSize, AssetPackFormat.TagSize);
        var payload = new byte[ciphertext.Length];
        var associated = AssetPackFormat.AssociatedData(entry.KeyBytes, entry.Compression, (uint)entry.OriginalLength);

        try
        {
            using var aes = new AesGcm(_keys.EncryptionKey, AssetPackFormat.TagSize);
            aes.Decrypt(nonce, ciphertext, tag, payload, associated);
        }
        catch (CryptographicException ex)
        {
            throw new AssetPackException(
                AssetPackErrorCodes.EntryAuthentication,
                $"Entry {entry.Key} failed authentication (wrong key or tampered data).",
                ex);
        }

        var data = Decompress(payload, entry);
        if (!SHA256.HashData(data).AsSpan().SequenceEqual(entry.Sha256))
        {
            throw new AssetPackException(AssetPackErrorCodes.EntryHash, $"Entry {entry.Key} failed its SHA-256 check.");
        }

        return data;
    }

    private static byte[] Decompress(byte[] payload, IndexEntry entry)
    {
        if (entry.Compression == AssetPackFormat.CompressionNone)
        {
            if (payload.Length != entry.OriginalLength)
            {
                throw new AssetPackException(AssetPackErrorCodes.EntryLength, $"Entry {entry.Key} has the wrong length.");
            }

            return payload;
        }

        var output = new byte[entry.OriginalLength];
        try
        {
            using var input = new MemoryStream(payload, writable: false);
            if (entry.Compression == AssetPackFormat.CompressionBrotli)
            {
                using var brotli = new BrotliStream(input, CompressionMode.Decompress);
                ReadExactly(brotli, output, entry.Key);
            }
            else
            {
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                ReadExactly(deflate, output, entry.Key);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw new AssetPackException(AssetPackErrorCodes.EntryDecompression, $"Entry {entry.Key} does not decompress.", ex);
        }

        return output;
    }

    private static void ReadExactly(Stream decoder, byte[] output, string key)
    {
        var read = decoder.ReadAtLeast(output, output.Length, throwOnEndOfStream: false);
        if (read != output.Length || decoder.ReadByte() != -1)
        {
            throw new AssetPackException(AssetPackErrorCodes.EntryLength, $"Entry {key} does not have its original length.");
        }
    }

    private sealed record IndexEntry(
        string Key,
        byte[] KeyBytes,
        byte Compression,
        long StoredOffset,
        int StoredLength,
        int OriginalLength,
        byte[] Sha256);
}
