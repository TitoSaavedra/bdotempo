using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Icons;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;
using System.Globalization;

namespace BdoComboSetup.Services;

/// <summary>
/// Creates the logger, the localizer, the settings, the icon catalog, the layouts and the view models.
/// Stubs stand in for the core and the game capture until those exist.
/// </summary>
public sealed class AppServices : IDisposable
{
    private AppServices(
        AppLogger logger,
        ResourceLocalizer localizer,
        AppSettingsStore settings,
        StartupOptions startup,
        ApplyComboViewModel applyCombo,
        PreviewViewModel preview,
        ICombosCatalog combos,
        IAssetProvider assets)
    {
        Logger = logger;
        Localizer = localizer;
        Settings = settings;
        Startup = startup;
        ApplyCombo = applyCombo;
        Preview = preview;
        Combos = combos;
        Assets = assets;
    }

    public AppLogger Logger { get; }

    public ResourceLocalizer Localizer { get; }

    public AppSettingsStore Settings { get; }

    public StartupOptions Startup { get; }

    public ApplyComboViewModel ApplyCombo { get; }

    public PreviewViewModel Preview { get; }

    /// <summary>Classes and guides read from the embedded asset bundle.</summary>
    public ICombosCatalog Combos { get; }

    /// <summary>Skill and class icons: embedded bundle, then the file catalogs.</summary>
    public IAssetProvider Assets { get; }

    public static AppServices Create(StartupOptions startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        var logger = AppLogger.CreateDefault("app.session");
        var localizer = new ResourceLocalizer(logger);
        var settings = new AppSettingsStore(AppSettingsStore.DefaultPath, logger);

        var dataSource = new MockComboDataSource();
        var combo = dataSource.GetCombo("Tamer", ComboMode.Awakening);
        var display = GameOptionLoader.Load(GameOptionLoader.DefaultPath, logger);
        var layouts = GameUiLayoutReader.Load(GameUiLayoutReader.DefaultPath(settings.Settings.GameAccountFolder), logger);
        var icons = IconCatalog.Load(settings.Settings.SkillIconRoot, settings.Settings.ClassHubRoot, logger);
        var embedded = new AssemblyEmbeddedResources(typeof(AppServices).Assembly);
        var assets = new BundledAssetProvider(embedded, icons, logger);
        var combos = JsonCombosCatalog.Load(embedded, assets, logger);

        var profiles = new ComboProfileStore(ComboProfileStore.DefaultDirectory, logger);
        var applyCombo = new ApplyComboViewModel(
            combos,
            assets,
            dataSource,
            profiles,
            new StubComboApplier(logger),
            localizer,
            CultureInfo.CurrentUICulture,
            logger);

        var preview = new PreviewViewModel(
            combo,
            GameLayoutIndices.Default,
            display,
            new StubGameWindowSource(),
            layouts,
            icons,
            settings,
            startup.Layout,
            startup.ShowPanels,
            localizer,
            logger);

        return new AppServices(logger, localizer, settings, startup, applyCombo, preview, combos, assets);
    }

    public void Dispose() => Logger.Dispose();
}
