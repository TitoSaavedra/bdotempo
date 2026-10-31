using System.Text.RegularExpressions;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.GameData;

public sealed record AccountInfo(string Account, int CharacterCount, DateTime NewestWriteUtc);

public sealed record CharacterInfo(string Pcid, string FilePath, DateTime LastWriteUtc);

/// <summary>
/// Finds the game's UserCache, the account folders and the character folders (port of the Python tool's config.py).
/// Layout: DataRoot\UserCache\&lt;account&gt;\11\&lt;pcid&gt;\gameVariable.xml. Only reads the folder tree.
/// </summary>
public sealed class GameDataLocator
{
    public const string CharacterFileName = "gameVariable.xml";

    private static readonly Regex DigitsRe = new("\\A[0-9]+\\z");

    private readonly AppLogger _logger;

    public GameDataLocator(string dataRoot, string? explicitAccount, AppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(logger);

        DataRoot = dataRoot;
        ExplicitAccount = string.IsNullOrWhiteSpace(explicitAccount) ? null : explicitAccount.Trim();
        _logger = logger;
    }

    public string DataRoot { get; }

    /// <summary>The account chosen in the settings, or null for auto-detection.</summary>
    public string? ExplicitAccount { get; }

    public string UserCacheDir => Path.Combine(DataRoot, "UserCache");

    public static bool IsDigits(string value) => DigitsRe.IsMatch(value);

    public string CharacterFilePath(string account, string pcid) =>
        Path.Combine(UserCacheDir, account, "11", pcid, CharacterFileName);

    /// <summary>Numeric account folders under UserCache that contain an "11" folder, with their character counts.</summary>
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
    /// modified most recently (logged as a warning).
    /// </summary>
    public string ResolveAccount()
    {
        if (ExplicitAccount is not null)
        {
            if (!IsDigits(ExplicitAccount))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, "account must be numeric");
            }

            if (!Directory.Exists(Path.Combine(UserCacheDir, ExplicitAccount, "11")))
            {
                throw new OperationRefusedException(
                    OperationProblem.AccountNotFound,
                    $"account {ExplicitAccount} has no '11' folder under UserCache");
            }

            _logger.Info("locator.account_explicit", "Account selected in the settings", new Dictionary<string, object?> { ["account"] = ExplicitAccount });
            return ExplicitAccount;
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
}
