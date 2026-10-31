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
}
