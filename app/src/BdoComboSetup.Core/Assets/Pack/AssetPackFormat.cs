using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>
/// The constants and key derivation of the .bdoa asset pack, format version 1. The specification is
/// app/docs/asset-pack-format.md and the Python builder is src/bdo_combo/assetpack/format.py: both must match.
/// Obfuscation, not DRM: the key is in the app. The two key halves are split so that no single constant in this
/// file is the whole key (PART_B in plain form, PART_A XOR-masked with 0x5A; the builder holds the mirror image).
/// </summary>
public static class AssetPackFormat
{
    public const ushort Version = 1;

    /// <summary>Header size: 48 bytes of fields, then the 32-byte index MAC.</summary>
    public const int HeaderSize = 80;
    public const int HeaderFieldsSize = 48;
    public const int MacSize = 32;
    public const int EntryFixedSize = 52;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int MaxKeyBytes = 128;
    public const int MaxEntryBytes = 64 * 1024 * 1024;
    public const int MaxEntryCount = 1_000_000;
    public const int MaxIndexBytes = 16 * 1024 * 1024;
    public const int MaxPartCount = 999;

    public const byte CompressionNone = 0;
    public const byte CompressionDeflate = 1;
    public const byte CompressionBrotli = 2;

    /// <summary>Entry flag: the blob is encrypted (always set in version 1).</summary>
    public const byte FlagEncrypted = 0x01;

    /// <summary>Header flag: this file is one part of a set; the part number, count and set id are set.</summary>
    public const ushort FlagPart = 0x0001;

    /// <summary>The set id the Python builder uses by default. Parts of one set share it.</summary>
    public const uint DefaultSetId = 0x0A55E701;

    private const byte MaskByte = 0x5A;

    private static readonly Lazy<AssetPackKeys> DefaultLazy = new(() => DeriveKeys(DefaultSecret()));

    /// <summary>The "BDOA" magic at offset 0.</summary>
    public static ReadOnlySpan<byte> Magic => "BDOA"u8;

    /// <summary>The HKDF salt of the key derivation.</summary>
    public static ReadOnlySpan<byte> Salt => "BDOA/v1"u8;

    /// <summary>The keys of the built-in secret, derived once.</summary>
    public static AssetPackKeys DefaultKeys => DefaultLazy.Value;

    /// <summary>
    /// Derives the three subkeys from a 32-byte secret, exactly as the Python builder does: an HMAC-SHA-256 extract
    /// with the salt, then one HMAC-SHA-256 expansion per label (info || 0x01).
    /// </summary>
    public static AssetPackKeys DeriveKeys(ReadOnlySpan<byte> secret)
    {
        if (secret.Length != 32)
        {
            throw new ArgumentException("The key secret must be 32 bytes.", nameof(secret));
        }

        var prk = HMACSHA256.HashData(Salt, secret);
        return new AssetPackKeys(
            EncryptionKey: Expand(prk, "bdoa/v1/aes-256-gcm"u8),
            NonceKey: Expand(prk, "bdoa/v1/synthetic-nonce"u8),
            IndexMacKey: Expand(prk, "bdoa/v1/index-mac"u8));
    }

    /// <summary>The associated data of an entry: key bytes, compression id, original length (uint32 LE).</summary>
    public static byte[] AssociatedData(ReadOnlySpan<byte> keyBytes, byte compression, uint originalLength)
    {
        var data = new byte[keyBytes.Length + 1 + 4];
        keyBytes.CopyTo(data);
        data[keyBytes.Length] = compression;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(keyBytes.Length + 1), originalLength);
        return data;
    }

    /// <summary>True for a key the format allows: 1 to 128 bytes, first [a-z0-9], then [a-z0-9._-].</summary>
    public static bool IsValidKey(ReadOnlySpan<byte> key)
    {
        if (key.Length is 0 or > MaxKeyBytes)
        {
            return false;
        }

        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            var lowerOrDigit = c is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9';
            var separator = c is (byte)'.' or (byte)'_' or (byte)'-';
            if (!(lowerOrDigit || (i > 0 && separator)))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] DefaultSecret()
    {
        // PART_A is held here XOR-masked; PART_B is held in plain form.
        var partAMasked = Convert.FromHexString("16690b6622665659eafc6fce30904eb5");
        var partB = Convert.FromHexString("534b39fcf83eed3a9ebff68ac8b3cc01");
        var secret = new byte[32];
        for (var i = 0; i < 16; i++)
        {
            secret[i] = (byte)(partAMasked[i] ^ MaskByte);
        }

        partB.CopyTo(secret, 16);
        return secret;
    }

    private static byte[] Expand(byte[] prk, ReadOnlySpan<byte> info)
    {
        var input = new byte[info.Length + 1];
        info.CopyTo(input);
        input[^1] = 0x01;
        return HMACSHA256.HashData(prk, input);
    }
}

/// <summary>The three 32-byte subkeys of one pack: entry encryption, synthetic nonces and the index MAC.</summary>
public sealed record AssetPackKeys(byte[] EncryptionKey, byte[] NonceKey, byte[] IndexMacKey);

/// <summary>The part fields of a part file: 1-based number, the set's part count, and the set id.</summary>
public sealed record PartInfo(int Number, int Count, uint SetId);
