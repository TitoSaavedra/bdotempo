using BdoComboSetup.Capture;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Assets.Pack;
using BdoComboSetup.Core.Backup;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Confirmation;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Demo;
using BdoComboSetup.Core.Files;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;
using BdoComboSetup.Core.Updates;
using BdoComboSetup.Core.ViewModels;
using System.Globalization;

namespace BdoComboSetup.Services;

/// <summary>
/// Creates the logger, the localizer, the settings, the asset pack, the layouts and the view models.
/// In demo mode (--demo) nothing of the player is read or written: the settings, the characters and the saved combos go to
/// a temporary folder, which also holds a synthetic game data folder (accounts, characters, layouts); the display is
/// synthetic, the quick keys are the defaults, and the applier writes nothing.
/// </summary>
public sealed class AppServices : IDisposable
{
    /// <summary>Temporary folder of the demo mode. It is recreated on every demo start.</summary>
    private const string DemoFolderName = "BDOTempo-demo";

    /// <summary>The skill class table entry of the asset pack (written by <c>mise run build-assets</c>, packed by <c>mise run pack-assets</c>).</summary>
    private const string SkillClassesResource = "skill_classes.json";

    /// <summary>The per-class skill catalog of the asset pack: every class at its top rank, for the slot editor.</summary>
    private const string ClassSkillsResource = "class-skills.json";

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
        CultureInfo culture,
        AssetPackLoadResult assetPackLoad,
        UpdateViewModel updates,
        CharacterScanService characterScan,
        SetupViewModel setup,
        ThemeService theme,
        HttpClient httpClient)
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
        AssetPackLoad = assetPackLoad;
        Updates = updates;
        CharacterScan = characterScan;
        Setup = setup;
        Theme = theme;
        _httpClient = httpClient;
    }

    private readonly HttpClient _httpClient;

    public AppLogger Logger { get; }

    public ResourceLocalizer Localizer { get; }

    public AppSettingsStore Settings { get; }

    public StartupOptions Startup { get; }

    public ApplyComboViewModel ApplyCombo { get; }

    public PreviewViewModel Preview { get; }

    /// <summary>The "Settings" page: account, data folder and language.</summary>
    public SettingsViewModel SettingsView { get; }

    /// <summary>Classes and guides read from the asset pack.</summary>
    public ICombosCatalog Combos { get; }

    /// <summary>Skill and class icons read from the asset pack.</summary>
    public IAssetProvider Assets { get; }

    /// <summary>The culture of the running language (the language setting, or the Windows language).</summary>
    public CultureInfo Culture { get; }

    /// <summary>How assets.bdoa was opened. A non-null ErrorCode means the app shows the error page instead of the modules.</summary>
    public AssetPackLoadResult AssetPackLoad { get; }

    /// <summary>The update state: the Settings controls and the banner of the main window.</summary>
    public UpdateViewModel Updates { get; }

    /// <summary>The character name scan (OCR of the selection screen) and its green overlay.</summary>
    public CharacterScanService CharacterScan { get; }

    /// <summary>The initial setup (data folder and account), shown at startup until it is done.</summary>
    public SetupViewModel Setup { get; }

    /// <summary>Applies the saved color theme and accent color to the main window (and follows their changes).</summary>
    public ThemeService Theme { get; }

    /// <summary>
    /// True when the initial setup must be shown: it was never finished, or the saved data folder is no longer usable. The
    /// demo never asks (it has its own temporary folder).
    /// </summary>
    public bool NeedsSetup => !Startup.DemoData
        && (!Settings.Settings.SetupCompleted || GameDataLocator.CheckDataRoot(Settings.Settings.GameDataRoot) != DataRootState.Ok);

    /// <summary>
    /// Creates the services. <paramref name="exitApplication"/> closes the app after an installer was started; it is the only
    /// way the updater leaves the process (the installer replaces the files of the running copy).
    /// </summary>
    public static AppServices Create(StartupOptions startup, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(exitApplication);

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

        // The initial setup: the data folder found on this PC (or the saved one) and the account. Needed once.
        var setup = new SetupViewModel(settings, localizer, logger);

        // Every icon and JSON document comes from the complete pack next to the exe (assets.bdoa). There is no fallback.
        var assetPackLoad = AssetPackLoader.LoadFile(AssetPackLoader.DefaultPath, logger);
        var packResources = new AssetPackResources(assetPackLoad.Pack);
        var skillClasses = LoadSkillClasses(packResources, logger);
        var classSkillCatalog = LoadClassSkills(packResources, logger);
        var demoRoot = Path.Combine(demoFolder, "BlackDesert");
        if (startup.DemoData)
        {
            // The demo's own game data folder: synthetic accounts and characters, written only under the temporary folder.
            DemoGameFiles.Write(demoRoot, skillClasses, logger, startup.DemoCharacters);
            settings.Update(current => current with
            {
                GameDataRoot = demoRoot,
                GameAccount = DemoGameFiles.AccountMain,
            });
        }

        var display = startup.DemoData ? DemoGameLayouts.Display : GameOptionLoader.Load(GameOptionLoader.DefaultPath, logger);
        var assets = new BundledAssetProvider(packResources, logger);
        var combos = JsonCombosCatalog.Load(packResources, assets, logger);

        // The skills of every class at their top rank: the rows of the slot editor's picker, and what brings a skill the
        // user put in a slot back when the saved combo is opened again.
        var classSkills = new ClassSkillSource(classSkillCatalog, assets);

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
            logger,
            classSkills);

        // The character name scan: OCR of the selection screen with the green overlay. It reads the game, never writes it.
        var characterScan = new CharacterScanService(logger, roster, applyCombo.ClassChoices, localizer);

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
        var slots = new ComboPreviewSlotSource(applyCombo, boundKeysOf, classSkills);

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
            logger,
            slots);

        var settingsView = new SettingsViewModel(settings, locator, localizer, logger, language);

        // The updater reads the GitHub release of the app. The demo answers from memory (no network): "a newer version exists"
        // with --demo-update, else "you have the latest". The portable zip (marker file next to the exe) opens the release page.
        var httpClient = HttpClientUpdateTransport.CreateClient();
        var transport = new HttpClientUpdateTransport(httpClient);
        IReleaseFeed releases = startup.DemoData
            ? new DemoReleaseFeed(AppVersion.Current, startup.DemoUpdate)
            : new GitHubReleaseFeed(transport, logger, $"BDOTempo/{AppVersion.Current}");
        var installerLogPath = Path.Combine(AppLogger.DefaultLogDirectory, $"update-setup-{DateTime.UtcNow:yyyyMMddTHHmmss}.log");
        var updateState = new AppSettingsUpdateStateStore(settings);
        var updateService = new UpdateService(
            releases,
            new UpdatePackageDownloader(transport, logger, UpdatePackageDownloader.DefaultWorkDirectory),
            new ProcessInstallerLauncher(logger),
            updateState,
            TimeProvider.System,
            AppVersion.Current,
            logger,
            installerLogPath);
        var updates = new UpdateViewModel(
            updateService,
            updateState,
            InstallKindDetector.Detect(AppContext.BaseDirectory),
            new ShellUrlOpener(logger),
            localizer,
            logger,
            culture,
            AppVersion.Current,
            exitApplication);

        var theme = new ThemeService(settings, settingsView, logger);

        return new AppServices(
            logger,
            localizer,
            settings,
            startup,
            applyCombo,
            preview,
            settingsView,
            combos,
            assets,
            culture,
            assetPackLoad,
            updates,
            characterScan,
            setup,
            theme,
            httpClient);
    }

    public void Dispose()
    {
        CharacterScan.Dispose();
        Logger.Dispose();
        _httpClient.Dispose();
    }

    /// <summary>
    /// The skill class table of the asset pack. A missing or broken table is logged and the classes are unknown until the pack
    /// is rebuilt; the rest of the app still works.
    /// </summary>
    private static SkillClassTable LoadSkillClasses(IEmbeddedResources packResources, AppLogger logger)
    {
        using var stream = packResources.Open(SkillClassesResource);
        if (stream is null)
        {
            logger.Warn("skill_classes.missing", "The asset pack has no skill class table; run mise run build-assets and mise run pack-assets");
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

    /// <summary>
    /// The per-class skill catalog of the asset pack (every class at its top rank), the same way as the skill class table:
    /// a missing or broken catalog is logged and the slot editor's picker stays empty; the rest of the app still works.
    /// </summary>
    private static SkillCatalog LoadClassSkills(IEmbeddedResources packResources, AppLogger logger)
    {
        using var stream = packResources.Open(ClassSkillsResource);
        if (stream is null)
        {
            logger.Warn("class_skills.missing", "The asset pack has no class skill catalog; the slot editor cannot offer skills");
            return SkillCatalog.Empty;
        }

        try
        {
            using var reader = new StreamReader(stream);
            var catalog = SkillCatalog.Parse(reader.ReadToEnd());
            logger.Info("class_skills.loaded", "Class skill catalog loaded", new Dictionary<string, object?>
            {
                ["classes"] = catalog.ClassCount,
            });
            return catalog;
        }
        catch (FormatException ex)
        {
            logger.Error("class_skills.invalid", "The class skill catalog could not be read; the slot editor cannot offer skills", ex);
            return SkillCatalog.Empty;
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
