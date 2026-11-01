using System.Collections.ObjectModel;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.Setup;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>A data folder offered by the initial setup.</summary>
public sealed record DataFolderChoice(string Path);

/// <summary>
/// The initial setup of the app, like Garmoth's: the Black Desert data folder and the account. The folder is looked for in
/// the usual places (<see cref="DataFolderFinder"/>); when it is not found, the page asks for it with the folder picker.
/// Finishing saves both settings and the "setup done" mark; skipping only marks it, so the app can still be used and the
/// setup can be reopened from Settings.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly AppSettingsStore _settings;
    private readonly ILocalizer _localizer;
    private readonly AppLogger _logger;
    private bool _adopting;

    public SetupViewModel(AppSettingsStore settings, ILocalizer localizer, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _localizer = localizer;
        _logger = logger;
        Refresh();
    }

    /// <summary>The data folders found on this PC; empty when the app could not find one.</summary>
    public ObservableCollection<DataFolderChoice> Candidates { get; } = [];

    /// <summary>The accounts with characters of the selected folder.</summary>
    public ObservableCollection<AccountChoice> Accounts { get; } = [];

    [ObservableProperty]
    private DataFolderChoice? _selectedCandidate;

    /// <summary>The folder that was selected or browsed. Empty while none is valid.</summary>
    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    private bool _isFolderValid;

    /// <summary>What the page says about the folder: found, chosen, or why it is not valid.</summary>
    [ObservableProperty]
    private string _folderStatus = string.Empty;

    [ObservableProperty]
    private AccountChoice? _selectedAccount;

    /// <summary>True when there is a folder to offer (detected or browsed).</summary>
    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>The setup can be finished with a valid folder; the account falls back to auto-detection.</summary>
    public bool CanFinish => IsFolderValid;

    /// <summary>Re-reads the candidates and the current settings. Called every time the setup page opens.</summary>
    public void Refresh()
    {
        _adopting = true;
        try
        {
            Candidates.Clear();
            foreach (var path in DataFolderFinder.Find())
            {
                Candidates.Add(new DataFolderChoice(path));
            }

            OnPropertyChanged(nameof(HasCandidates));

            var configured = _settings.Settings.GameDataRoot;
            var selected = Candidates.FirstOrDefault(choice => SamePath(choice.Path, configured))
                ?? Candidates.FirstOrDefault();
            if (selected is not null)
            {
                SelectedCandidate = selected;
                Select(selected);
            }
            else if (GameDataLocator.CheckDataRoot(configured) == DataRootState.Ok)
            {
                AddAndSelect(configured);
            }
            else
            {
                FolderPath = string.Empty;
                IsFolderValid = false;
                FolderStatus = _localizer.Get("Setup.FolderMissing");
                Accounts.Clear();
                SelectedAccount = null;
            }

            _logger.Info("setup.refreshed", "Initial setup refreshed", new Dictionary<string, object?>
            {
                ["candidates"] = Candidates.Count,
                ["folder_valid"] = IsFolderValid,
                ["accounts"] = Accounts.Count,
            });
        }
        finally
        {
            _adopting = false;
        }
    }

    /// <summary>
    /// Checks a folder chosen with the folder picker. False (with the reason on the page) when it is not a Black Desert
    /// data folder. A valid one is added to the list and selected.
    /// </summary>
    public bool ChooseFolder(string path)
    {
        var trimmed = path?.Trim() ?? string.Empty;
        var state = GameDataLocator.CheckDataRoot(trimmed);
        if (state != DataRootState.Ok)
        {
            FolderStatus = _localizer.Get(state == DataRootState.Missing ? "Setup.FolderNotFound" : "Setup.FolderInvalid");
            _logger.Warn("setup.folder_rejected", "The chosen folder is not a Black Desert data folder", new Dictionary<string, object?>
            {
                ["state"] = state.ToString(),
            });
            return false;
        }

        AddAndSelect(trimmed);
        _logger.Info("setup.folder_chosen", "A data folder was chosen in the initial setup", new Dictionary<string, object?>
        {
            ["folder"] = Path.GetFileName(Path.GetDirectoryName(trimmed)),
        });
        return true;
    }

    /// <summary>Saves the folder and the account (or the automatic account when none is picked) and marks the setup done.</summary>
    public bool Finish()
    {
        if (!IsFolderValid)
        {
            return false;
        }

        var account = SelectedAccount?.Account;
        _settings.Update(current => current with
        {
            GameDataRoot = FolderPath,
            GameAccount = account,
            SetupCompleted = true,
        });
        _logger.Info("setup.finished", "Initial setup saved", new Dictionary<string, object?>
        {
            ["account"] = account,
            ["account_auto"] = account is null,
        });
        return true;
    }

    /// <summary>Marks the setup done without changing anything: the app opens and Settings can configure it later.</summary>
    public void Skip()
    {
        _settings.Update(current => current with { SetupCompleted = true });
        _logger.Info("setup.skipped", "Initial setup skipped");
    }

    partial void OnSelectedCandidateChanged(DataFolderChoice? value)
    {
        if (!_adopting && value is not null)
        {
            Select(value);
        }
    }

    private void AddAndSelect(string path)
    {
        var choice = Candidates.FirstOrDefault(item => SamePath(item.Path, path));
        if (choice is null)
        {
            choice = new DataFolderChoice(path);
            Candidates.Add(choice);
            OnPropertyChanged(nameof(HasCandidates));
        }

        _adopting = true;
        try
        {
            SelectedCandidate = choice;
        }
        finally
        {
            _adopting = false;
        }

        Select(choice);
    }

    private void Select(DataFolderChoice choice)
    {
        FolderPath = choice.Path;
        IsFolderValid = GameDataLocator.CheckDataRoot(choice.Path) == DataRootState.Ok;
        FolderStatus = _localizer.Format("Setup.FolderReady", choice.Path);
        LoadAccounts();
    }

    /// <summary>Lists the accounts of the folder and preselects the configured one, else the one with most characters.</summary>
    private void LoadAccounts()
    {
        Accounts.Clear();
        SelectedAccount = null;
        if (!IsFolderValid)
        {
            return;
        }

        try
        {
            var locator = new GameDataLocator(FolderPath, null, _logger);
            foreach (var account in locator.FindAccounts().OrderByDescending(item => item.CharacterCount))
            {
                Accounts.Add(new AccountChoice(account.Account, _localizer.Format("Settings.AccountItem", account.Account)));
            }
        }
        catch (OperationRefusedException ex)
        {
            _logger.Warn("setup.accounts_unavailable", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("setup.accounts_failed", "The accounts of the chosen folder could not be read", ex);
        }

        var configured = _settings.Settings.GameAccount;
        SelectedAccount = Accounts.FirstOrDefault(choice => string.Equals(choice.Account, configured, StringComparison.Ordinal))
            ?? Accounts.FirstOrDefault();
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
