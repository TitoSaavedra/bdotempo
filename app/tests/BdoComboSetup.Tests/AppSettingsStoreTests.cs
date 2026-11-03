using BdoComboSetup.Core.Settings;

namespace BdoComboSetup.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    private string SettingsPath => Path.Combine(_log.Directory, "settings.json");

    [Fact]
    public void Missing_file_gives_the_defaults()
    {
        var store = new AppSettingsStore(SettingsPath, _log.Logger);

        Assert.Equal(AppSettings.DefaultGameDataRoot, store.Settings.GameDataRoot);
        Assert.Null(store.Settings.GameAccount);
        Assert.Null(store.Settings.Language);
        Assert.Null(store.Settings.SelectedLayout);
    }

    [Fact]
    public void Changes_are_written_and_read_back()
    {
        var store = new AppSettingsStore(SettingsPath, _log.Logger);

        store.Update(settings => settings with
        {
            SelectedLayout = "Preset1",
        });

        var reloaded = new AppSettingsStore(SettingsPath, _log.Logger).Settings;
        Assert.Equal("Preset1", reloaded.SelectedLayout);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Invalid_json_is_logged_and_the_defaults_are_used()
    {
        File.WriteAllText(SettingsPath, "{ broken");

        var store = new AppSettingsStore(SettingsPath, _log.Logger);

        Assert.Equal(AppSettings.DefaultGameDataRoot, store.Settings.GameDataRoot);
    }

    [Fact]
    public void Appearance_and_menu_settings_default_to_system_violet_and_an_auto_hiding_menu()
    {
        var store = new AppSettingsStore(SettingsPath, _log.Logger);

        Assert.Equal("system", store.Settings.Theme);
        Assert.Equal("violet", store.Settings.AccentColor);
        Assert.True(store.Settings.NavAutoHide);
    }

    [Fact]
    public void Appearance_and_menu_settings_are_written_and_read_back()
    {
        var store = new AppSettingsStore(SettingsPath, _log.Logger);

        store.Update(settings => settings with
        {
            Theme = "dark",
            AccentColor = "mint",
            NavAutoHide = false,
        });

        var reloaded = new AppSettingsStore(SettingsPath, _log.Logger).Settings;
        Assert.Equal("dark", reloaded.Theme);
        Assert.Equal("mint", reloaded.AccentColor);
        Assert.False(reloaded.NavAutoHide);
    }

    [Fact]
    public void An_old_settings_file_without_the_new_fields_loads_with_their_defaults()
    {
        File.WriteAllText(SettingsPath, "{ \"GameDataRoot\": \"D:\\\\Old\", \"Language\": \"en-US\", \"SkipWriteHint\": true, \"NavPaneOpen\": false }");

        var store = new AppSettingsStore(SettingsPath, _log.Logger);

        Assert.Equal("D:\\Old", store.Settings.GameDataRoot);
        Assert.Equal("en-US", store.Settings.Language);
        Assert.Equal("system", store.Settings.Theme);
        Assert.Equal("violet", store.Settings.AccentColor);
        Assert.True(store.Settings.NavAutoHide);
    }

    [Fact]
    public void Unknown_theme_and_accent_values_normalize_to_the_defaults()
    {
        Assert.Equal("system", AppThemes.Normalize("neon"));
        Assert.Equal("system", AppThemes.Normalize(null));
        Assert.Equal("dark", AppThemes.Normalize(" DARK "));
        Assert.Equal("light", AppThemes.Normalize("light"));
        Assert.Equal("violet", AccentColors.Normalize("pink"));
        Assert.Equal("violet", AccentColors.Normalize(""));
        Assert.Equal("blue", AccentColors.Normalize("Blue"));
        Assert.Equal(new[] { "violet", "amber", "mint", "rose", "blue" }, AccentColors.Supported);
    }
}
