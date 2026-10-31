using System.Globalization;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Demo;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

/// <summary>The demo game folder runs the real roster and account code on synthetic files only.</summary>
public sealed class DemoGameFilesTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    /// <summary>A synthetic table: eight skills only for each of the demo classes 16, 0 and 17.</summary>
    private static SkillClassTable DemoTable()
    {
        var entries = new List<string>();
        var skill = 100;
        foreach (var classType in new[] { 16, 0, 17 })
        {
            for (var i = 0; i < 8; i++)
            {
                entries.Add($"\"{skill++}\": [{classType}]");
            }
        }

        return SkillClassTable.Parse("{\"skills\": {" + string.Join(", ", entries) + "}}");
    }

    [Fact]
    public void The_demo_account_lists_its_live_characters_with_the_labels_and_inferred_classes()
    {
        var root = Path.Combine(_log.Directory, "BlackDesert");
        var table = DemoTable();
        DemoGameFiles.Write(root, table, _log.Logger);
        var locator = new GameDataLocator(root, DemoGameFiles.AccountMain, _log.Logger);
        var roster = new CharacterRoster(
            locator,
            new CharacterOverrideStore(Path.Combine(_log.Directory, "characters.json"), _log.Logger),
            new GameFileReader(_log.Logger, retryDelay: TimeSpan.Zero),
            table,
            FakeCombosCatalog.Sample(),
            new KeyLocalizer(),
            CultureInfo.InvariantCulture,
            _log.Logger);

        var entries = roster.Load(DemoGameFiles.AccountMain);

        // The stale order entry (no folder) and the sidecar file are not live.
        Assert.Equal(4, entries.Count);
        Assert.Equal("Demo Tamer", entries[0].Name);
        Assert.Equal(NameSource.Label, entries[0].NameSource);
        Assert.Equal(DemoGameFiles.TamerClass, entries[0].ClassType);
        Assert.Equal(ClassSource.Inferred, entries[0].ClassSource);
        Assert.Equal(DemoGameFiles.WarriorClass, entries[1].ClassType);
        Assert.Equal(DemoGameFiles.PcidUnnamed, entries[3].Pcid);
    }

    [Fact]
    public void The_demo_lists_only_real_account_folders()
    {
        var root = Path.Combine(_log.Directory, "BlackDesert");
        DemoGameFiles.Write(root, DemoTable(), _log.Logger);
        var locator = new GameDataLocator(root, null, _log.Logger);

        var accounts = locator.FindAccounts().Select(account => account.Account).ToList();

        Assert.Equal(new[] { DemoGameFiles.AccountMain, DemoGameFiles.AccountAlt }, accounts);
    }
}

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private string Root => Path.Combine(_log.Directory, "Black Desert");

    /// <summary>Settings in a temporary folder whose data folder does not exist: the real game folder is never read.</summary>
    private AppSettingsStore Store()
    {
        var store = new AppSettingsStore(Path.Combine(_log.Directory, "settings.json"), _log.Logger);
        store.Update(current => current with { GameDataRoot = Path.Combine(_log.Directory, "none") });
        return store;
    }

    private static SettingsViewModel Create(AppSettingsStore store, AppLogger logger, string language = "es")
    {
        var locator = new GameDataLocator(() => store.Settings.GameDataRoot, () => store.Settings.GameAccount, logger);
        return new SettingsViewModel(store, locator, new KeyLocalizer(), logger, language);
    }

    private void CreateGameFolder(string account)
    {
        Directory.CreateDirectory(Path.Combine(Root, "UserCache", account, "11"));
    }

    [Fact]
    public void A_missing_folder_is_reported_and_lists_no_accounts()
    {
        var store = Store();
        store.Update(current => current with { GameDataRoot = Path.Combine(_log.Directory, "absent") });

        var vm = Create(store, _log.Logger);

        Assert.Equal("Settings.FolderMissing", vm.StatusMessage);
        Assert.Empty(vm.Accounts);
    }

    [Fact]
    public void A_folder_without_usercache_is_refused_and_not_saved()
    {
        Directory.CreateDirectory(Root);
        var store = Store();
        var vm = Create(store, _log.Logger);

        var saved = vm.ChooseDataRoot(Root);

        Assert.False(saved);
        Assert.Equal("Settings.FolderNoUserCache", vm.StatusMessage);
        Assert.NotEqual(Root, store.Settings.GameDataRoot);
    }

    [Fact]
    public void A_valid_folder_is_saved_and_its_accounts_are_listed()
    {
        CreateGameFolder("100000001");
        CreateGameFolder("100000002");
        var store = Store();
        var vm = Create(store, _log.Logger);

        var saved = vm.ChooseDataRoot(Root);

        Assert.True(saved);
        Assert.Equal(Root, store.Settings.GameDataRoot);
        Assert.Equal(new[] { "100000001", "100000002" }, vm.Accounts.Select(account => account.Account));
        Assert.True(vm.RestartRequired);
    }

    [Fact]
    public void An_account_must_be_one_of_the_folder_accounts_and_is_remembered()
    {
        CreateGameFolder("100000001");
        var store = Store();
        var vm = Create(store, _log.Logger);
        vm.ChooseDataRoot(Root);

        vm.ChooseAccount("100000009");
        Assert.Null(store.Settings.GameAccount);

        vm.ChooseAccount("100000001");
        Assert.Equal("100000001", store.Settings.GameAccount);
        Assert.Equal("100000001", vm.SelectedAccount!.Account);

        vm.ChooseAccount(null);
        Assert.Null(store.Settings.GameAccount);
    }

    [Fact]
    public void Only_supported_languages_are_saved_and_a_change_needs_a_restart()
    {
        var store = Store();
        var vm = Create(store, _log.Logger, language: "es");
        Assert.False(vm.RestartRequired);

        vm.ChooseLanguage("fr");
        Assert.Null(store.Settings.Language);

        vm.ChooseLanguage("en-us");
        Assert.Equal(AppLanguages.English, store.Settings.Language);
        Assert.True(vm.RestartRequired);
    }

    [Fact]
    public void The_write_reminder_can_be_turned_back_on_after_dont_show_again()
    {
        var store = Store();
        store.Update(current => current with { SkipWriteHint = true });
        var vm = Create(store, _log.Logger);
        Assert.False(vm.ShowWriteHint);

        vm.ChooseShowWriteHint(true);

        Assert.True(vm.ShowWriteHint);
        Assert.False(store.Settings.SkipWriteHint);
    }

    [Fact]
    public void Languages_resolve_from_the_setting_then_the_windows_language()
    {
        Assert.Equal(AppLanguages.Spanish, AppLanguages.Resolve(null, new CultureInfo("es-MX")));
        Assert.Equal(AppLanguages.English, AppLanguages.Resolve(null, new CultureInfo("fr-FR")));
        Assert.Equal(AppLanguages.English, AppLanguages.Resolve("EN-US", new CultureInfo("es-MX")));
        Assert.Equal(AppLanguages.Spanish, AppLanguages.Resolve("xx", new CultureInfo("es-MX")));
    }
}
