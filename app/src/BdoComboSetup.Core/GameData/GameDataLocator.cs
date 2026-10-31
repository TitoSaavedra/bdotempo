using System.Text.RegularExpressions;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.GameData;

public sealed record AccountInfo(string Account, int CharacterCount, DateTime NewestWriteUtc);

public sealed record CharacterInfo(string Pcid, string FilePath, DateTime LastWriteUtc);

/// <summary>What a Black Desert data folder looks like. Used by the settings to validate a folder before it is saved.</summary>
public enum DataRootState
{
    /// <summary>The folder does not exist.</summary>
    Missing,

    /// <summary>The folder exists, but it has no UserCache folder (not a Black Desert data folder).</summary>
    NoUserCache,

    /// <summary>The folder has a UserCache folder.</summary>
    Ok,
}

/// <summary>
/// Finds the game's UserCache, the account folders and the character folders (port of the Python tool's config.py).
/// Layout: DataRoot\UserCache\&lt;account&gt;\11\&lt;pcid&gt;\gameVariable.xml. Only reads the folder tree.
/// The data folder and the chosen account are read on every call, so a change in the settings applies at once.
/// </summary>
public sealed class GameDataLocator
{
    public const string CharacterFileName = "gameVariable.xml";

    private static readonly Regex DigitsRe = new("\\A[0-9]+\\z");

    private readonly Func<string> _dataRoot;
    private readonly Func<string?> _explicitAccount;
    private readonly AppLogger _logger;

    public GameDataLocator(string dataRoot, string? explicitAccount, AppLogger logger)
        : this(() => dataRoot, () => explicitAccount, logger)
    {
    }

    public GameDataLocator(Func<string> dataRoot, Func<string?> explicitAccount, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        ArgumentNullException.ThrowIfNull(explicitAccount);
        ArgumentNullException.ThrowIfNull(logger);

        _dataRoot = dataRoot;
        _explicitAccount = explicitAccount;
        _logger = logger;
    }

    /// <summary>The Black Desert data folder (the one that holds UserCache and _backups).</summary>
    public string DataRoot => _dataRoot();

    /// <summary>The account chosen in the settings, or null for auto-detection.</summary>
    public string? ExplicitAccount
    {
        get
        {
            var value = _explicitAccount();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public string UserCacheDir => Path.Combine(DataRoot, "UserCache");

    public static bool IsDigits(string value) => DigitsRe.IsMatch(value);

    public string CharacterFilePath(string account, string pcid) =>
        Path.Combine(UserCacheDir, account, "11", pcid, CharacterFileName);

    /// <summary>Checks a candidate data folder. Reads the folder tree only.</summary>
    public static DataRootState CheckDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return DataRootState.Missing;
        }

        return Directory.Exists(Path.Combine(path, "UserCache")) ? DataRootState.Ok : DataRootState.NoUserCache;
    }

    /// <summary>
    /// Numeric account folders under UserCache that contain an "11" folder, with their character counts. The "-1" folder
    /// and any non-numeric folder are ignored.
    /// </summary>
    public IReadOnlyList<AccountInfo> FindAccounts()
    {
        if (!Directory.Exists(UserCacheDir))
        {
            throw new OperationRefusedException(OperationProblem.AccountNotFound, "UserCache folder not found under the data directory");
        }

        var accounts = new List<AccountInfo>();
        foreach (var directory in Directory.EnumerateDirectories(UserCacheDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);
            if (!IsDigits(name) || !Directory.Exists(Path.Combine(directory, "11")))
            {
                continue;
            }

            var characters = ListCharacters(name);
            var newest = characters.Count == 0 ? DateTime.MinValue : characters.Max(c => c.LastWriteUtc);
            accounts.Add(new AccountInfo(name, characters.Count, newest));
        }

        return accounts;
    }

    /// <summary>Characters (digit folders with a gameVariable.xml) of an account, sorted by PCID.</summary>
    public IReadOnlyList<CharacterInfo> ListCharacters(string account)
    {
        var root = Path.Combine(UserCacheDir, account, "11");
        if (!Directory.Exists(root))
        {
            return [];
        }

        var characters = new List<CharacterInfo>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var pcid = Path.GetFileName(directory);
            if (!IsDigits(pcid))
            {
                continue;
            }

            var file = Path.Combine(directory, CharacterFileName);
            if (File.Exists(file))
            {
                characters.Add(new CharacterInfo(pcid, file, File.GetLastWriteTimeUtc(file)));
            }
            else
            {
                _logger.Debug("locator.folder_skipped", "Skipped a character folder without gameVariable.xml");
            }
        }

        return characters.OrderBy(c => c.Pcid, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The account to use: the explicit one when set, else the only account, else the one whose characters were
    /// modified most recently (logged as a warning). Throws <see cref="OperationRefusedException"/> when there is none.
    /// </summary>
    public string ResolveAccount()
    {
        var explicitAccount = ExplicitAccount;
        if (explicitAccount is not null)
        {
            if (!IsDigits(explicitAccount))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, "account must be numeric");
            }

            if (!Directory.Exists(Path.Combine(UserCacheDir, explicitAccount, "11")))
            {
                throw new OperationRefusedException(
                    OperationProblem.AccountNotFound,
                    $"account {explicitAccount} has no '11' folder under UserCache");
            }

            _logger.Info("locator.account_explicit", "Account selected in the settings", new Dictionary<string, object?> { ["account"] = explicitAccount });
            return explicitAccount;
        }

        var accounts = FindAccounts();
        if (accounts.Count == 0)
        {
            throw new OperationRefusedException(
                OperationProblem.AccountNotFound,
                "no account with a character folder found; set the account in the settings");
        }

        if (accounts.Count == 1)
        {
            _logger.Info("locator.account_auto", "Account auto-detected", new Dictionary<string, object?> { ["account"] = accounts[0].Account });
            return accounts[0].Account;
        }

        var chosen = accounts.OrderByDescending(a => a.NewestWriteUtc).First();
        _logger.Warn(
            "locator.account_ambiguous",
            "Several accounts found; using the most recently played one (set the account in the settings to choose)",
            new Dictionary<string, object?>
            {
                ["candidates"] = accounts.Select(a => a.Account).ToList(),
                ["chosen"] = chosen.Account,
            });
        return chosen.Account;
    }

    /// <summary>
    /// The active account, or null when none can be found (no UserCache, no account folder, or the chosen one is missing).
    /// The reason is logged; nothing is thrown.
    /// </summary>
    public string? TryResolveAccount()
    {
        try
        {
            return ResolveAccount();
        }
        catch (OperationRefusedException ex)
        {
            _logger.Warn("locator.no_account", ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("locator.read_failed", "The game folders could not be read", ex);
            return null;
        }
    }
}
