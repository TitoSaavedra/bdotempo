using BdoComboSetup.Core.GameData;

namespace BdoComboSetup.Core.Setup;

/// <summary>
/// Looks for the Black Desert data folder (the one that holds UserCache) in the places Windows and OneDrive usually put it:
/// the Documents folder (also when OneDrive redirects it, in Spanish or English), the OneDrive roots of the machine and any
/// other "OneDrive*" folder under the user profile. Only existing folders with a UserCache folder are returned.
/// </summary>
public static class DataFolderFinder
{
    /// <summary>The folder name Black Desert uses inside Documents.</summary>
    public const string GameFolderName = "Black Desert";

    /// <summary>Every data folder found on this PC, in preference order, without duplicates.</summary>
    public static IReadOnlyList<string> Find()
    {
        var candidates = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var full = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Length > 0 && !candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(full);
            }
        }

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Add(Path.Combine(documents, GameFolderName));
        Add(Path.Combine(user, "Documents", GameFolderName));
        Add(Path.Combine(user, "Documentos", GameFolderName));
        Add(Path.Combine(user, "OneDrive", "Documents", GameFolderName));
        Add(Path.Combine(user, "OneDrive", "Documentos", GameFolderName));

        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            Add(Path.Combine(root, "Documents", GameFolderName));
            Add(Path.Combine(root, "Documentos", GameFolderName));
        }

        // Other OneDrive folders of the profile ("OneDrive - Contoso"): the game folder may live in any of them.
        try
        {
            if (Directory.Exists(user))
            {
                foreach (var root in Directory.EnumerateDirectories(user, "OneDrive*"))
                {
                    Add(Path.Combine(root, "Documents", GameFolderName));
                    Add(Path.Combine(root, "Documentos", GameFolderName));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed is simply not a candidate.
        }

        return candidates
            .Where(path => GameDataLocator.CheckDataRoot(path) == DataRootState.Ok)
            .ToList();
    }
}
