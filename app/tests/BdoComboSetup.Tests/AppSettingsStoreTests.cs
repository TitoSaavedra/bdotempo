using BdoComboSetup.Core.Game;
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

        Assert.Null(store.Settings.BackgroundImagePath);
        Assert.Equal(AppSettings.DefaultSkillIconRoot, store.Settings.SkillIconRoot);
        Assert.Equal(2, store.Settings.CooldownRows);
        Assert.Equal(10, store.Settings.CooldownColumns);
        Assert.Equal("330", store.Settings.GameAccountFolder);
    }

    [Fact]
    public void Changes_are_written_and_read_back()
    {
        var store = new AppSettingsStore(SettingsPath, _log.Logger);

        store.Update(settings => settings with
        {
            BackgroundImagePath = @"C:\shots\bg.png",
            SelectedLayout = "Preset1",
            ManualBarPositions = new Dictionary<string, NormalizedPosition>(StringComparer.Ordinal)
            {
                [AppSettings.ManualBarKey(GameUiLayoutId.General, BarKind.Quick)] = new(0.25, 0.75),
            },
        });

        var reloaded = new AppSettingsStore(SettingsPath, _log.Logger).Settings;
        Assert.Equal(@"C:\shots\bg.png", reloaded.BackgroundImagePath);
        Assert.Equal("Preset1", reloaded.SelectedLayout);
        Assert.Equal(new NormalizedPosition(0.25, 0.75), reloaded.ManualBarPositions["General.Quick"]);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Invalid_json_is_logged_and_the_defaults_are_used()
    {
        File.WriteAllText(SettingsPath, "{ broken");

        var store = new AppSettingsStore(SettingsPath, _log.Logger);

        Assert.Equal(AppSettings.DefaultGameAccountFolder, store.Settings.GameAccountFolder);
    }

    [Fact]
    public void Manual_bar_key_names_the_layout_and_the_bar()
    {
        Assert.Equal("Preset2.Cooldown", AppSettings.ManualBarKey(GameUiLayoutId.Preset2, BarKind.Cooldown));
        Assert.Equal("General.Quick", AppSettings.ManualBarKey(GameUiLayoutId.General, BarKind.Quick));
    }
}
