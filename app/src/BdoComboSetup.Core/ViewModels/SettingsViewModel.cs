using System.Collections.ObjectModel;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;
using BdoComboSetup.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>An account of the data folder: its number and how many characters it has.</summary>
public sealed record AccountChoice(string Account, string Label);

/// <summary>An interface language: its code ("es", "en-US") and its name in the page language.</summary>
public sealed record LanguageChoice(string Code, string Name);

/// <summary>
/// The "Settings" page: the account, the Black Desert data folder (validated before it is saved) and the language.
/// Every change is saved at once. Some changes only take effect after a restart (the language, the data folder and the
/// account used by the Preview page); <see cref="RestartRequired"/> says so.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _settings;
    private readonly GameDataLocator _locator;
    private readonly ILocalizer _localizer;
    private readonly AppLogger _logger;
    private readonly string _runningLanguage;
    private string _runningDataRoot;
    private string? _runningAccount;
    private readonly System.Globalization.CultureInfo _systemCulture = System.Globalization.CultureInfo.CurrentUICulture;

    /// <param name="runningLanguage">The language the app started with (AppLanguages code).</param>
    public SettingsViewModel(
        AppSettingsStore settings,
        GameDataLocator locator,
        ILocalizer localizer,
        AppLogger logger,
        string runningLanguage)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(runningLanguage);

        _settings = settings;
        _locator = locator;
        _localizer = localizer;
        _logger = logger;
        _runningLanguage = runningLanguage;
        _runningDataRoot = settings.Settings.GameDataRoot;
        _runningAccount = settings.Settings.GameAccount;

        Languages =
        [
            new LanguageChoice(AppLanguages.Spanish, localizer.Get("Settings.LanguageSpanish")),
            new LanguageChoice(AppLanguages.English, localizer.Get("Settings.LanguageEnglish")),
        ];
        SelectedLanguage = Languages.First(choice => choice.Code == runningLanguage);
        DataRootText = settings.Settings.GameDataRoot;
        ShowWriteHint = !settings.Settings.SkipWriteHint;
        Theme = AppThemes.Normalize(settings.Settings.Theme);
        AccentColor = AccentColors.Normalize(settings.Settings.AccentColor);
        NavPaneOpen = settings.Settings.NavPaneOpen;
        Refresh();
    }

    public ObservableCollection<AccountChoice> Accounts { get; } = [];

    public IReadOnlyList<LanguageChoice> Languages { get; }

    /// <summary>The account chosen in the settings, or the one auto-detection picks. Null when there is none.</summary>
    [ObservableProperty]
    private AccountChoice? _selectedAccount;

    [ObservableProperty]
    private LanguageChoice? _selectedLanguage;

    /// <summary>The Black Desert data folder as typed or chosen. It is saved only when <see cref="ChooseDataRoot"/> accepts it.</summary>
    [ObservableProperty]
    private string _dataRootText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Information;

    [ObservableProperty]
    private bool _isStatusOpen;

    /// <summary>True when a changed setting only applies after the app restarts.</summary>
    [ObservableProperty]
    private bool _restartRequired;

    /// <summary>True when the reminder before writing to a character is shown (off after "Don't show again").</summary>
    [ObservableProperty]
    private bool _showWriteHint;

    /// <summary>The color theme ("system", "light" or "dark"). Changed with <see cref="ChooseTheme"/>.</summary>
    [ObservableProperty]
    private string _theme = AppThemes.FollowSystem;

    /// <summary>The accent color id. Changed with <see cref="ChooseAccentColor"/>.</summary>
    [ObservableProperty]
    private string _accentColor = AccentColors.Default;

    /// <summary>True when the left navigation menu is expanded, false when it is folded. Changed with <see cref="ChooseNavPaneOpen"/>.</summary>
    [ObservableProperty]
    private bool _navPaneOpen = true;

    /// <summary>Raised when the saved theme or accent color actually changes, so the window can apply it.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>Reads the accounts of the saved data folder and selects the active one.</summary>
    public void Refresh()
    {
        Accounts.Clear();
        var root = _settings.Settings.GameDataRoot;
        var state = GameDataLocator.CheckDataRoot(root);
        if (state == DataRootState.Ok)
        {
            try
            {
                foreach (var account in _locator.FindAccounts())
                {
                    Accounts.Add(new AccountChoice(account.Account, _localizer.Format("Settings.AccountItem", account.Account)));
                }
            }
            catch (OperationRefusedException ex)
            {
                _logger.Warn("settings.accounts_unavailable", ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Error("settings.accounts_failed", "The accounts of the data folder could not be read", ex);
            }
        }

        var chosen = _settings.Settings.GameAccount;
        SelectedAccount = chosen is null
            ? Accounts.FirstOrDefault(choice => choice.Account == _locator.TryResolveAccount())
            : Accounts.FirstOrDefault(choice => choice.Account == chosen);

        if (chosen is not null && SelectedAccount is null && Accounts.Count > 0)
        {
            _logger.Warn("settings.account_missing", "The saved account is not in the data folder; the choice is cleared");
            _settings.Update(current => current with { GameAccount = null });
            SelectedAccount = Accounts.FirstOrDefault(choice => choice.Account == _locator.TryResolveAccount());
        }

        ShowFolderStatus(state);
        UpdateRestartRequired();
    }

    /// <summary>
    /// Validates a data folder and saves it when it has a UserCache folder. Returns false (and shows why) otherwise.
    /// </summary>
    public bool ChooseDataRoot(string path)
    {
        var trimmed = path?.Trim() ?? string.Empty;
        var state = GameDataLocator.CheckDataRoot(trimmed);
        if (state != DataRootState.Ok)
        {
            _logger.Warn("settings.folder_rejected", "The chosen folder is not a Black Desert data folder", new Dictionary<string, object?>
            {
                ["state"] = state.ToString(),
            });
            DataRootText = trimmed;
            ShowFolderStatus(state);
            return false;
        }

        _settings.Update(current => current with { GameDataRoot = trimmed });
        DataRootText = trimmed;
        _logger.Info("settings.folder_saved", "Black Desert data folder saved");
        Refresh();
        return true;
    }

    /// <summary>Turns the reminder before a write on or off. Turning it on again undoes "Don't show again".</summary>
    public void ChooseShowWriteHint(bool show)
    {
        _settings.Update(current => current with { SkipWriteHint = !show });
        ShowWriteHint = show;
        _logger.Info("settings.write_hint_saved", "Write reminder setting saved", new Dictionary<string, object?> { ["show"] = show });
    }

    /// <summary>Saves the color theme at once. Unknown values are saved as "system". Raises <see cref="AppearanceChanged"/> on a change.</summary>
    public void ChooseTheme(string theme)
    {
        var normalized = AppThemes.Normalize(theme);
        var changed = normalized != Theme;
        _settings.Update(current => current with { Theme = normalized });
        Theme = normalized;
        _logger.Info("settings.theme_saved", "Theme saved", new Dictionary<string, object?> { ["theme"] = normalized });
        if (changed)
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Saves the accent color at once. Unknown values are saved as "violet". Raises <see cref="AppearanceChanged"/> on a change.</summary>
    public void ChooseAccentColor(string accentColor)
    {
        var normalized = AccentColors.Normalize(accentColor);
        var changed = normalized != AccentColor;
        _settings.Update(current => current with { AccentColor = normalized });
        AccentColor = normalized;
        _logger.Info("settings.accent_saved", "Accent color saved", new Dictionary<string, object?> { ["accent"] = normalized });
        if (changed)
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Saves whether the navigation menu is expanded (true) or folded (false) at once.</summary>
    public void ChooseNavPaneOpen(bool open)
    {
        _settings.Update(current => current with { NavPaneOpen = open });
        NavPaneOpen = open;
        _logger.Info("settings.nav_pane_saved", "Navigation menu state saved", new Dictionary<string, object?> { ["open"] = open });
    }

    /// <summary>Shows that the folder picker of the view failed (the reason is logged by the view).</summary>
    public void ReportPickerFailed() => ShowStatus(StatusSeverity.Error, "Settings.PickerFailed");

    /// <summary>Saves the account to use. Null clears the choice (auto-detection).</summary>
    public void ChooseAccount(string? account)
    {
        if (account is not null && Accounts.All(choice => choice.Account != account))
        {
            _logger.Warn("settings.account_unknown", "The chosen account is not in the data folder; nothing saved");
            return;
        }

        _settings.Update(current => current with { GameAccount = account });
        SelectedAccount = account is null ? null : Accounts.First(choice => choice.Account == account);
        _logger.Info("settings.account_saved", "Account saved", new Dictionary<string, object?>
        {
            ["auto"] = account is null,
        });
        UpdateRestartRequired();
    }

    /// <summary>Saves the interface language. It takes effect after a restart.</summary>
    public void ChooseLanguage(string code)
    {
        var language = AppLanguages.Normalize(code);
        if (language is null)
        {
            _logger.Warn("settings.language_unsupported", "Unsupported language; nothing saved", new Dictionary<string, object?> { ["code"] = code });
            return;
        }

        _settings.Update(current => current with { Language = language });
        SelectedLanguage = Languages.First(choice => choice.Code == language);
        _logger.Info("settings.language_saved", "Language saved", new Dictionary<string, object?> { ["language"] = language });
        UpdateRestartRequired();
    }

    /// <summary>Recomputes whether a restart is needed: language, data folder or account differ from the running values.</summary>
    public void UpdateRestartRequired()
    {
        var current = _settings.Settings;
        var language = AppLanguages.Resolve(current.Language, _systemCulture);
        RestartRequired = language != _runningLanguage
            || current.GameDataRoot != _runningDataRoot
            || current.GameAccount != _runningAccount;
    }

    /// <summary>
    /// Takes the current settings as the running ones: the initial setup just saved them, so asking for a restart would be
    /// wrong. The accounts and the folder status are read again.
    /// </summary>
    public void AdoptRunningConfiguration()
    {
        _runningDataRoot = _settings.Settings.GameDataRoot;
        _runningAccount = _settings.Settings.GameAccount;
        Refresh();
    }

    private void ShowFolderStatus(DataRootState state)
    {
        switch (state)
        {
            case DataRootState.Missing:
                ShowStatus(StatusSeverity.Warning, "Settings.FolderMissing");
                break;
            case DataRootState.NoUserCache:
                ShowStatus(StatusSeverity.Warning, "Settings.FolderNoUserCache");
                break;
            default:
                ShowStatus(Accounts.Count == 0 ? StatusSeverity.Warning : StatusSeverity.Success,
                    Accounts.Count == 0 ? "Settings.NoAccounts" : "Settings.FolderOk", Accounts.Count);
                break;
        }
    }

    private void ShowStatus(StatusSeverity severity, string key, params object[] args)
    {
        StatusMessage = args.Length == 0 ? _localizer.Get(key) : _localizer.Format(key, args);
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
