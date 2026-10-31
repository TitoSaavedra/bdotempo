using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Files;

/// <summary>File access used by the writers. An interface so that tests can inject faults (failed copies, bad writes).</summary>
public interface IFileStore
{
    bool Exists(string path);

    byte[] ReadAllBytes(string path);

    /// <summary>Creates or overwrites the file, creating its folder when needed.</summary>
    void WriteAllBytes(string path, byte[] data);

    /// <summary>Writes through a temporary file in the same folder, then moves it over the target.</summary>
    void AtomicReplace(string path, byte[] data);

    DateTime GetLastWriteUtc(string path);
}

public sealed class LocalFileStore(AppLogger logger) : IFileStore
{
    public const string TemporarySuffix = ".bdo-combo.tmp";

    public bool Exists(string path) => File.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, data);
    }

    public void AtomicReplace(string path, byte[] data)
    {
        var temporary = path + TemporarySuffix;
        try
        {
            WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.Error("file.atomic_replace_failed", "Atomic replace failed", ex, new Dictionary<string, object?>
            {
                ["file"] = Path.GetFileName(path),
            });
            DeleteTemporary(temporary);
            throw;
        }
    }

    public DateTime GetLastWriteUtc(string path) => File.GetLastWriteTimeUtc(path);

    private void DeleteTemporary(string temporary)
    {
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error("file.temporary_cleanup_failed", "Could not delete the temporary file", ex, new Dictionary<string, object?>
            {
                ["file"] = Path.GetFileName(temporary),
            });
        }
    }
}
