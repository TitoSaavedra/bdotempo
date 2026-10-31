using BdoComboSetup.Capture;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Backup;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Confirmation;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Demo;
using BdoComboSetup.Core.Files;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Icons;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.ViewModels;
using System.Globalization;

namespace BdoComboSetup.Services;

/// <summary>
/// Creates the logger, the localizer, the settings, the icon catalog, the layouts and the view models.
/// In demo mode (--demo) nothing of the player is read or written: the settings, the characters and the saved combos go to
/// a temporary folder, which also holds a synthetic game data folder (accounts, characters, layouts); the display is
/// synthetic, the quick keys are the defaults, and the applier writes nothing.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <summary>Temporary folder of the demo mode. It is recreated on every demo start.</summary>
    private const string DemoFolderName = "BdoComboSetup-demo";

    /// <summary>The skill class table in the asset bundle (written by <c>mise run build-assets</c>).</summary>
    private const string SkillClassesResource = "skill_classes.json";

    private AppServices(
        AppLogger logger,
        ResourceLocalizer localizer,
        AppSettingsStore settings,
        StartupOptions startup,
        ApplyComboViewModel applyCombo,
        PreviewViewModel preview,
        SettingsViewModel settingsView,
        ICombosCatalog combos,
        IAssetProvider assets,
        CultureInfo culture)
    {
        Logger = logger;
        Localizer = localizer;
        Settings = settings;
        Startup = startup;
        ApplyCombo = applyCombo;
        Preview = preview;
        SettingsView = settingsView;
        Combos = combos;
        Assets = assets;
        Culture = culture;
    }

    public AppLogger Logger { get; }

    public ResourceLocalizer Localizer { get; }

    public AppSettingsStore Settings { get; }

    public StartupOptions Startup { get; }

    public ApplyComboViewModel ApplyCombo { get; }

    public PreviewViewModel Preview { get; }

    /// <summary>The "Settings" page: account, data folder and language.</summary>
    public SettingsViewModel SettingsView { get; }

    /// <summary>Classes and guides read from the embedded asset bundle.</summary>
    public ICombosCatalog Combos { get; }

    /// <summary>Skill and class icons: embedded bundle, then the file catalogs.</summary>
    public IAssetProvider Assets { get; }

    /// <summary>The culture of the running language (the language setting, or the Windows language).</summary>
    public CultureInfo Culture { get; }

    public static AppServices Create(StartupOptions startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        var logger = AppLogger.CreateDefault("app.session");
        var demoFolder = Path.Combine(Path.GetTempPath(), DemoFolderName);
        if (startup.DemoData)
        {
            ResetDemoFolder(demoFolder, logger);
        }

        var settings = new AppSettingsStore(
            startup.DemoData ? Path.Combine(demoFolder, "settings.json") : AppSettingsStore.DefaultPath,
            logger);

        // The language must be set before the first resource is loaded.
        var language = LanguageSetup.Apply(settings.Settings.Language, logger);
        var culture = new CultureInfo(language);
        var localizer = new ResourceLocalizer(logger);

        var embedded = new AssemblyEmbeddedResources(typeof(AppServices).Assembly);
        var skillClasses = LoadSkillClasses(embedded, logger);
        var demoRoot = Path.Combine(demoFolder, "BlackDesert");
        if (startup.DemoData)
        {
            // The demo's own game data folder: synthetic accounts and characters, written only under the temporary folder.
            DemoGameFiles.Write(demoRoot, skillClasses, logger);
            settings.Update(current => current with
            {
                GameDataRoot = demoRoot,
                GameAccount = DemoGameFiles.AccountMain,
            });
        }

        var display = startup.DemoData ? DemoGameLayouts.Display : GameOptionLoader.Load(GameOptionLoader.DefaultPath, logger);
        var icons = IconCatalog.Load(settings.Settings.SkillIconRoot, settings.Settings.ClassHubRoot, logger);
        var assets = new BundledAssetProvider(embedded, icons, logger);
        var combos = JsonCombosCatalog.Load(embedded, assets, logger);

        // The data folder and the account are read live: a change in the settings applies to the Apply page at once.
        var locator = new GameDataLocator(() => settings.Settings.GameDataRoot, () => settings.Settings.GameAccount, logger);

        var files = new LocalFileStore(logger);
        var guard = new GameProcessGuard(logger);
        Func<string> backupsRoot = () => settings.Settings.BackupsRoot ?? Path.Combine(settings.Settings.GameDataRoot, "_backups");
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
        var profiles = new ComboProfileStore(
            startup.DemoData ? Path.Combine(demoFolder, "profiles") : ComboProfileStore.DefaultDirectory,
            logger);

        var overrides = new CharacterOverrideStore(
            startup.DemoData ? Path.Combine(demoFolder, "characters.json") : CharacterOverrideStore.DefaultPath,
            logger);
        if (startup.DemoData)
        {
            overrides.Set(DemoGameFiles.AccountMain, DemoGameFiles.PcidShai, new CharacterOverride("Demo Shai (main)", null));
            overrides.Set(DemoGameFiles.AccountMain, DemoGameFiles.PcidUnnamed, new CharacterOverride(null, DemoGameFiles.TamerClass));
        }

        var roster = new CharacterRoster(locator, overrides, new GameFileReader(logger), skillClasses, combos, localizer, culture, logger);
        var writeConfirmation = new WriteConfirmation(
            new AppSettingsWriteHintSetting(settings),
            new WriteHintDialog(logger, localizer),
            logger);
        var applyCombo = new ApplyComboViewModel(
            combos,
            assets,
            roster,
            profiles,
            applier,
            writeConfirmation,
            localizer,
            culture,
            logger);

        // The Preview page reads its layouts and the quick keys of the active account once, at startup (restart applies a change).
        var activeAccount = startup.DemoData ? DemoGameFiles.AccountMain : locator.TryResolveAccount();
        var layouts = startup.DemoData
            ? DemoGameLayouts.Create()
            : activeAccount is null
                ? null
                : GameUiLayoutReader.Load(GameUiLayoutReader.DefaultPath(activeAccount), logger);

        // The quick keys of the character chosen on the Apply page. The demo uses the defaults and never opens a character file.
        Func<CharacterOption?, IReadOnlyDictionary<int, int>?> boundKeysOf = startup.DemoData
            ? _ => null
            : character => character is null || activeAccount is null
                ? null
                : QuickSlotKeyReader.Load(locator.CharacterFilePath(activeAccount, character.Pcid), logger);
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

        var settingsView = new SettingsViewModel(settings, locator, localizer, logger, language);

        return new AppServices(logger, localizer, settings, startup, applyCombo, preview, settingsView, combos, assets, culture);
    }

    public void Dispose() => Logger.Dispose();

    /// <summary>
    /// The skill class table of the bundle. A missing or broken table is logged and the classes are unknown until the bundle
    /// is rebuilt; the rest of the app still works.
    /// </summary>
    private static SkillClassTable LoadSkillClasses(IEmbeddedResources embedded, AppLogger logger)
    {
        using var stream = embedded.Open(SkillClassesResource);
        if (stream is null)
        {
            logger.Warn("skill_classes.missing", "The asset bundle has no skill class table; run mise run build-assets");
            return SkillClassTable.Empty;
        }

        try
        {
            using var reader = new StreamReader(stream);
            var table = SkillClassTable.Parse(reader.ReadToEnd());
            logger.Info("skill_classes.loaded", "Skill class table loaded", new Dictionary<string, object?> { ["skills"] = table.Count });
            return table;
        }
        catch (FormatException ex)
        {
            logger.Error("skill_classes.invalid", "The skill class table could not be read; classes are unknown", ex);
            return SkillClassTable.Empty;
        }
    }

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
