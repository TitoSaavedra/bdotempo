using BdoComboSetup.Capture;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Assets.Pack;
using BdoComboSetup.Core.Backup;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Files;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;
using System.Globalization;

namespace BdoComboSetup.Services;

/// <summary>
/// Creates the logger, the localizer, the settings, the icon catalog, the layouts and the view models.
/// In demo mode (--demo) nothing of the player is read or written: the settings and the saved combos go to a temporary
/// folder, the layouts and the display are synthetic, the quick keys are the defaults, and the applier writes nothing.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <summary>Temporary folder of the demo mode. It is recreated on every demo start.</summary>
    private const string DemoFolderName = "BdoComboSetup-demo";

    private AppServices(
        AppLogger logger,
        ResourceLocalizer localizer,
        AppSettingsStore settings,
        StartupOptions startup,
        ApplyComboViewModel applyCombo,
        PreviewViewModel preview,
        ICombosCatalog combos,
        IAssetProvider assets,
        AssetPackLoadResult assetPackLoad)
    {
        Logger = logger;
        Localizer = localizer;
        Settings = settings;
        Startup = startup;
        ApplyCombo = applyCombo;
        Preview = preview;
        Combos = combos;
        Assets = assets;
        AssetPackLoad = assetPackLoad;
    }

    public AppLogger Logger { get; }

    public ResourceLocalizer Localizer { get; }

    public AppSettingsStore Settings { get; }

    public StartupOptions Startup { get; }

    public ApplyComboViewModel ApplyCombo { get; }

    public PreviewViewModel Preview { get; }

    /// <summary>Classes and guides read from the asset pack.</summary>
    public ICombosCatalog Combos { get; }

    /// <summary>Skill and class icons read from the asset pack.</summary>
    public IAssetProvider Assets { get; }

    /// <summary>How assets.bdoa was opened. A non-null ErrorCode means the app shows the error page instead of the modules.</summary>
    public AssetPackLoadResult AssetPackLoad { get; }

    public static AppServices Create(StartupOptions startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        var logger = AppLogger.CreateDefault("app.session");
        var localizer = new ResourceLocalizer(logger);
        var demoFolder = Path.Combine(Path.GetTempPath(), DemoFolderName);
        if (startup.DemoData)
        {
            ResetDemoFolder(demoFolder, logger);
        }

        var settings = new AppSettingsStore(
            startup.DemoData ? Path.Combine(demoFolder, "settings.json") : AppSettingsStore.DefaultPath,
            logger);

        var mockData = new MockComboDataSource();
        var display = startup.DemoData ? DemoGameLayouts.Display : GameOptionLoader.Load(GameOptionLoader.DefaultPath, logger);
        var layouts = startup.DemoData
            ? DemoGameLayouts.Create()
            : GameUiLayoutReader.Load(GameUiLayoutReader.DefaultPath(settings.Settings.GameAccountFolder), logger);
        // Every icon and JSON document comes from the complete pack next to the exe (assets.bdoa). There is no fallback.
        var assetPackLoad = AssetPackLoader.LoadFile(AssetPackLoader.DefaultPath, logger);
        var packResources = new AssetPackResources(assetPackLoad.Pack);
        var assets = new BundledAssetProvider(packResources, logger);
        var combos = JsonCombosCatalog.Load(packResources, assets, logger);

        var files = new LocalFileStore(logger);
        var guard = new GameProcessGuard(logger);
        var locator = new GameDataLocator(settings.Settings.GameDataRoot, settings.Settings.GameAccount, logger);
        var backupsRoot = settings.Settings.BackupsRoot ?? Path.Combine(settings.Settings.GameDataRoot, "_backups");
        var backups = new BackupService(backupsRoot, locator, files, guard, logger);
        // --demo never reads or writes the game files: its applier only describes what a write would do.
        IComboApplier applier = startup.DemoData
            ? new DemoComboApplier(logger)
            : new ComboApplier(
                locator,
                backups,
                guard,
                files,
                () => SkillTable.Load(settings.Settings.SkillsJsonPath, logger),
                logger);
        IComboDataSource dataSource = startup.DemoData ? mockData : new GameDataComboSource(locator, mockData, logger);
        var profiles = new ComboProfileStore(
            startup.DemoData ? Path.Combine(demoFolder, "profiles") : ComboProfileStore.DefaultDirectory,
            logger);

        var applyCombo = new ApplyComboViewModel(
            combos,
            assets,
            dataSource,
            profiles,
            applier,
            localizer,
            CultureInfo.CurrentUICulture,
            logger);

        // The quick keys of the character chosen on the Apply page. The demo uses the defaults and never opens a character file.
        Func<CharacterOption?, IReadOnlyDictionary<int, int>?> boundKeysOf = startup.DemoData
            ? _ => null
            : character => character is null
                ? null
                : QuickSlotKeyReader.Load(locator.CharacterFilePath(settings.Settings.GameAccountFolder, character.Pcid), logger);
        var slots = new ComboPreviewSlotSource(applyCombo, boundKeysOf);

        // The background is the live capture of the game window. The demo never captures: its images stay black.
        IGameScreenSource gameScreen = startup.DemoData
            ? new BlackGameScreenSource()
            : new GameScreenSource(new Win32GameWindowLocator(logger), new WindowsGraphicsCapture(logger), logger);

        var preview = new PreviewViewModel(
            slots,
            display,
            gameScreen,
            layouts,
            settings,
            new PreviewStartup(startup.Layout, startup.ShowHiddenSlots, startup.CooldownDemo, startup.Zoom),
            localizer,
            logger);

        return new AppServices(logger, localizer, settings, startup, applyCombo, preview, combos, assets, assetPackLoad);
    }

    public void Dispose() => Logger.Dispose();

    /// <summary>Deletes the demo folder of an earlier run so every demo starts from the same empty state. Only that folder is touched.</summary>
    private static void ResetDemoFolder(string folder, AppLogger logger)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warn("demo.reset_failed", "The demo folder of an earlier run could not be cleared; its files are used", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name,
            });
        }
    }
}
