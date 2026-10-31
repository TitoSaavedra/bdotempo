namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>Stable error codes of the asset pack. They appear in the logs as the "code" field and set the exit codes of the assembler.</summary>
public static class AssetPackErrorCodes
{
    // Reading one pack file.
    public const string Missing = "missing";
    public const string ReadFailed = "read_failed";
    public const string BadMagic = "bad_magic";
    public const string UnsupportedVersion = "unsupported_version";
    public const string BadHeader = "bad_header";
    public const string Truncated = "truncated";
    public const string IndexAuthentication = "index_authentication";
    public const string BadIndex = "bad_index";
    public const string EntryRead = "entry_read";
    public const string EntryAuthentication = "entry_authentication";
    public const string EntryDecompression = "entry_decompression";
    public const string EntryLength = "entry_length";
    public const string EntryHash = "entry_hash";

    // A part file used where a complete pack is required, or a complete pack used where parts are required.
    public const string IsPart = "is_part";
    public const string NotAPart = "not_a_part";

    // Assembling a set of parts.
    public const string PartsDirMissing = "parts_dir_missing";
    public const string PartMissing = "part_missing";
    public const string PartMismatch = "part_mismatch";
    public const string DuplicateEntry = "duplicate_entry";
    public const string WriteFailed = "write_failed";
}

/// <summary>
/// A pack cannot be opened, or an entry fails a check, or a set of parts cannot be assembled.
/// <see cref="Code"/> names the check (see <see cref="AssetPackErrorCodes"/>).
/// </summary>
public sealed class AssetPackException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}
