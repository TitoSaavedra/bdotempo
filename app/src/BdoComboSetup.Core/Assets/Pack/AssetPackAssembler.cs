using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>The result of <see cref="AssetPackAssembler.Assemble"/>.</summary>
/// <param name="Output">The full path of the complete pack written.</param>
/// <param name="Parts">The number of parts of the set.</param>
/// <param name="Entries">The number of entries in the complete pack.</param>
/// <param name="Bytes">The size of the complete pack.</param>
/// <param name="SetId">The set id shared by the parts.</param>
public sealed record AssemblyResult(string Output, int Parts, int Entries, long Bytes, uint SetId);

/// <summary>
/// Joins the parts of one asset set (app/assets/parts/assets.partNN.bdoa) into one complete pack (assets.bdoa).
/// Checks, in order: the folder exists; every part file name matches the part number in its header; all parts are
/// parts (not complete packs) of one set (same count and set id); every part from 1 to the count is present, once;
/// every entry of every part is authenticated and hash-checked. The complete pack copies the verified blobs (the
/// bytes are identical to what the Python builder writes for the same entries) and is written through a temporary
/// file that is renamed into place. The written file is reopened and verified before the result is returned.
/// Errors are <see cref="AssetPackException"/> with a code from <see cref="AssetPackErrorCodes"/>.
/// </summary>
public static class AssetPackAssembler
{
    private static readonly Regex PartFileName = new(@"^assets\.part(\d{2,3})\.bdoa$", RegexOptions.CultureInvariant);

    /// <summary>The file name of a part, for example assets.part01.bdoa.</summary>
    public static string PartFile(int number) => $"assets.part{number:00}.bdoa";

    /// <summary>Assembles the parts in <paramref name="partsDirectory"/> into <paramref name="outputPath"/>.</summary>
    public static AssemblyResult Assemble(string partsDirectory, string outputPath, AppLogger logger, AssetPackKeys? keys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(logger);

        using var scope = logger.BeginOperation("asset.pack.assemble", new Dictionary<string, object?>
        {
            ["parts_dir"] = partsDirectory,
            ["output"] = outputPath,
        });
        try
        {
            var parts = OpenParts(partsDirectory, keys ?? AssetPackFormat.DefaultKeys, logger);
            try
            {
                var (count, setId) = CheckSet(parts);
                var blobs = MergeEntries(parts);
                var bytes = BuildComplete(blobs, keys ?? AssetPackFormat.DefaultKeys);
                var fullOutput = Path.GetFullPath(outputPath);
                WriteAtomically(fullOutput, bytes);
                VerifyWritten(fullOutput, keys, logger);

                logger.Info("asset.pack.assembled", "Asset parts assembled into one pack", new Dictionary<string, object?>
                {
                    ["parts"] = count,
                    ["entries"] = blobs.Count,
                    ["bytes"] = bytes.Length,
                    ["set_id"] = setId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                });
                return new AssemblyResult(fullOutput, count, blobs.Count, bytes.Length, setId);
            }
            finally
            {
                foreach (var part in parts.Values)
                {
                    part.Dispose();
                }
            }
        }
        catch (AssetPackException ex)
        {
            scope.Fail(ex);
            throw;
        }
    }

    private static Dictionary<int, AssetPack> OpenParts(string partsDirectory, AssetPackKeys keys, AppLogger logger)
    {
        if (!Directory.Exists(partsDirectory))
        {
            throw new AssetPackException(AssetPackErrorCodes.PartsDirMissing, $"The parts folder does not exist: {partsDirectory}");
        }

        var names = Directory.EnumerateFiles(partsDirectory)
            .Select(path => Path.GetFileName(path))
            .Where(name => name is not null && PartFileName.IsMatch(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (names.Count == 0)
        {
            throw new AssetPackException(AssetPackErrorCodes.PartMissing, $"No part files (assets.partNN.bdoa) in {partsDirectory}.");
        }

        var parts = new Dictionary<int, AssetPack>();
        try
        {
            foreach (var name in names)
            {
                var fileNumber = int.Parse(PartFileName.Match(name!).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var pack = OpenPart(Path.Combine(partsDirectory, name!), keys, logger);
                if (pack.Part is null)
                {
                    pack.Dispose();
                    throw new AssetPackException(AssetPackErrorCodes.NotAPart, $"{name} is not a part: it is a complete pack.");
                }

                if (pack.Part.Number != fileNumber)
                {
                    pack.Dispose();
                    throw new AssetPackException(
                        AssetPackErrorCodes.PartMismatch,
                        $"{name} holds part {pack.Part.Number}: it is the wrong part for this file name.");
                }

                if (!parts.TryAdd(pack.Part.Number, pack))
                {
                    pack.Dispose();
                    throw new AssetPackException(AssetPackErrorCodes.PartMismatch, $"Part {pack.Part.Number} is given twice.");
                }
            }
        }
        catch
        {
            foreach (var part in parts.Values)
            {
                part.Dispose();
            }

            throw;
        }

        return parts;
    }

    private static AssetPack OpenPart(string path, AssetPackKeys keys, AppLogger logger)
    {
        FileStream stream;
        try
        {
            stream = File.OpenRead(path);
        }
        catch (IOException ex)
        {
            throw new AssetPackException(AssetPackErrorCodes.ReadFailed, $"{Path.GetFileName(path)} could not be opened.", ex);
        }

        return AssetPack.Open(stream, logger, new AssetPackOptions(Keys: keys, AllowParts: true));
    }

    private static (int Count, uint SetId) CheckSet(IReadOnlyDictionary<int, AssetPack> parts)
    {
        var first = parts.Values.First().Part!;
        foreach (var part in parts.Values)
        {
            if (part.Part!.Count != first.Count || part.Part.SetId != first.SetId)
            {
                throw new AssetPackException(
                    AssetPackErrorCodes.PartMismatch,
                    $"Part {part.Part.Number} belongs to another set (count {part.Part.Count}, set id {part.Part.SetId:X8}).");
            }
        }

        var missing = Enumerable.Range(1, first.Count).Where(number => !parts.ContainsKey(number)).ToList();
        if (missing.Count > 0)
        {
            throw new AssetPackException(
                AssetPackErrorCodes.PartMissing,
                $"Missing part(s) {string.Join(", ", missing)} of {first.Count}.");
        }

        return (first.Count, first.SetId);
    }

    private static List<PackedBlob> MergeEntries(IReadOnlyDictionary<int, AssetPack> parts)
    {
        var merged = new List<PackedBlob>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var number in parts.Keys.OrderBy(number => number))
        {
            foreach (var blob in parts[number].ReadVerifiedBlobs())
            {
                if (!seen.Add(blob.Key))
                {
                    throw new AssetPackException(AssetPackErrorCodes.DuplicateEntry, $"Entry {blob.Key} is in more than one part.");
                }

                merged.Add(blob);
            }
        }

        merged.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        return merged;
    }

    /// <summary>Writes the complete pack bytes: header (flags 0, no part fields), blobs in key order, index, index MAC.</summary>
    private static byte[] BuildComplete(IReadOnlyList<PackedBlob> blobs, AssetPackKeys keys)
    {
        var blobArea = new MemoryStream();
        var index = new MemoryStream();
        long offset = AssetPackFormat.HeaderSize;
        foreach (var blob in blobs)
        {
            var keyBytes = Encoding.ASCII.GetBytes(blob.Key);
            var lengthBytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(lengthBytes, (ushort)keyBytes.Length);
            index.Write(lengthBytes);
            index.Write(keyBytes);

            var fixedFields = new byte[AssetPackFormat.EntryFixedSize];
            fixedFields[0] = blob.Compression;
            fixedFields[1] = AssetPackFormat.FlagEncrypted;
            BinaryPrimitives.WriteUInt64LittleEndian(fixedFields.AsSpan(4), (ulong)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(fixedFields.AsSpan(12), (uint)blob.Blob.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(fixedFields.AsSpan(16), (uint)blob.OriginalLength);
            blob.Sha256.AsSpan().CopyTo(fixedFields.AsSpan(20));
            index.Write(fixedFields);

            blobArea.Write(blob.Blob);
            offset += blob.Blob.Length;
        }

        var indexBytes = index.ToArray();
        var blobBytes = blobArea.ToArray();
        var indexOffset = (ulong)(AssetPackFormat.HeaderSize + blobBytes.Length);
        var fileLength = indexOffset + (ulong)indexBytes.Length;

        var output = new byte[AssetPackFormat.HeaderSize + blobBytes.Length + indexBytes.Length];
        var header = output.AsSpan(0, AssetPackFormat.HeaderFieldsSize);
        AssetPackFormat.Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], AssetPackFormat.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)AssetPackFormat.HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)blobs.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(header[16..], indexOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header[24..], (ulong)indexBytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header[32..], fileLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header[40..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header[42..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], 0);

        using (var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, keys.IndexMacKey))
        {
            mac.AppendData(header);
            mac.AppendData(indexBytes);
            mac.GetHashAndReset().AsSpan().CopyTo(output.AsSpan(AssetPackFormat.HeaderFieldsSize, AssetPackFormat.MacSize));
        }

        blobBytes.CopyTo(output, AssetPackFormat.HeaderSize);
        indexBytes.CopyTo(output, AssetPackFormat.HeaderSize + blobBytes.Length);
        return output;
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new AssetPackException(AssetPackErrorCodes.WriteFailed, $"Invalid output path: {path}");
        var temporary = Path.Combine(directory, Path.GetFileName(path) + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new AssetPackException(AssetPackErrorCodes.WriteFailed, $"The pack could not be written to {path}.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new AssetPackException(AssetPackErrorCodes.WriteFailed, $"The pack could not be written to {path}.", ex);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void VerifyWritten(string path, AssetPackKeys? keys, AppLogger logger)
    {
        using var pack = AssetPack.Open(File.OpenRead(path), logger, new AssetPackOptions(Keys: keys));
        var verification = pack.Verify();
        if (!verification.IsValid)
        {
            throw new AssetPackException(
                AssetPackErrorCodes.WriteFailed,
                $"The written pack failed verification ({verification.Failures.Count} entries).");
        }
    }
}
