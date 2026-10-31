using System.Globalization;
using System.Text.Json;

namespace BdoComboSetup.Core.Updates;

/// <summary>Reads the JSON of the GitHub "latest release" endpoint and picks the release assets the updater uses.</summary>
public static class ReleaseParser
{
    /// <summary>Parses the release. Throws <see cref="UpdateException"/> (InvalidRelease) when a required field is wrong.</summary>
    public static ReleaseInfo Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new UpdateException(UpdateErrorCode.InvalidRelease, "The release answer is not JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("The release answer is not an object.");
            }

            var tag = ReadString(root, "tag_name") ?? throw Invalid("The release has no tag_name.");
            if (!SemanticVersion.TryParse(tag, out var version) || version is null)
            {
                throw Invalid("The release tag is not a SemVer version.");
            }

            var pageUrl = ReadHttps(root, "html_url") ?? UpdateRepository.ReleasesPage;
            var title = ReadString(root, "name") ?? tag;
            var notes = ReadString(root, "body") ?? string.Empty;
            var published = ReadDate(root, "published_at");

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var assetList) && assetList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in assetList.EnumerateArray())
                {
                    var name = ReadString(item, "name");
                    var url = ReadHttps(item, "browser_download_url");
                    if (name is null || url is null)
                    {
                        continue;
                    }

                    var size = item.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var parsedSize)
                        ? parsedSize
                        : 0L;
                    assets.Add(new ReleaseAsset(name, url, size));
                }
            }

            return new ReleaseInfo(tag, version, title, notes, pageUrl, published, assets);
        }
    }

    /// <summary>
    /// The installer of this release: <c>BdoComboSetup-Setup-&lt;version&gt;.exe</c> with the release's own version. Null when the
    /// release has none (the check then reports the asset as missing).
    /// </summary>
    public static ReleaseAsset? FindInstaller(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var expected = UpdateRepository.InstallerPrefix + release.Version + UpdateRepository.InstallerSuffix;
        return release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, expected, StringComparison.Ordinal));
    }

    /// <summary>The SHA256SUMS.txt asset of the release, or null.</summary>
    public static ReleaseAsset? FindChecksums(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, UpdateRepository.ChecksumsAssetName, StringComparison.Ordinal));
    }

    private static UpdateException Invalid(string message) => new(UpdateErrorCode.InvalidRelease, message);

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Only https URLs are accepted: the updater downloads from them.</summary>
    private static Uri? ReadHttps(JsonElement element, string property)
    {
        var text = ReadString(element, property);
        return text is not null
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : null;
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string property)
    {
        var text = ReadString(element, property);
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>Reads a <c>SHA256SUMS.txt</c> file (<c>sha256sum</c> format: "hash  name" or "hash *name").</summary>
public sealed class ChecksumFile
{
    private readonly Dictionary<string, string> _hashes;

    private ChecksumFile(Dictionary<string, string> hashes)
    {
        _hashes = hashes;
    }

    public int Count => _hashes.Count;

    /// <summary>
    /// Parses the file. Blank lines and comments are skipped; a malformed line is skipped too (the file may hold other
    /// tools' lines). The same file name with two different hashes is an error: the file cannot be trusted.
    /// </summary>
    public static ChecksumFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // sha256sum: 64 hex digits, a space, then the mode mark: a second space (text) or "*" (binary), then the file name.
            // A line with a single space and no mark is accepted too.
            if (line.Length < 66 || !IsHex64(line.AsSpan(0, 64)) || line[64] != ' ')
            {
                continue;
            }

            var nameStart = line[65] is ' ' or '*' ? 66 : 65;
            if (nameStart >= line.Length)
            {
                continue;
            }

            var name = line[nameStart..];
            var hash = line[..64].ToLowerInvariant();
            if (hashes.TryGetValue(name, out var existing) && existing != hash)
            {
                throw new UpdateException(UpdateErrorCode.ChecksumMissing, $"The checksum file lists '{name}' twice with different hashes.");
            }

            hashes[name] = hash;
        }

        return new ChecksumFile(hashes);
    }

    /// <summary>The SHA-256 (lowercase hex) published for a file name, or null when the file lists none.</summary>
    public string? Find(string fileName) => _hashes.TryGetValue(fileName, out var hash) ? hash : null;

    private static bool IsHex64(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
