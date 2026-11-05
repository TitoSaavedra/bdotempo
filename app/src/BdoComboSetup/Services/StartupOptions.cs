using System.Globalization;
using BdoComboSetup.Core.Demo;
using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Services;

/// <summary>
/// Command-line options: "--page apply|preview", "--layout general|preset1|preset2|preset3", "--no-capture" (do not capture the game window), "--tour" (starts the guided tour on Combos),"--hidden-slots" (draw the slots
/// the game hides, dimmed), "--cooldown-demo" (a 60% cooldown on the first cooldown slot, debug), "--zoom &lt;n&gt;" (zoom
/// relative to fit). For the Apply page "--class &lt;slug&gt;" and "--spec &lt;id&gt;" (for example --class tamer --spec awakening),
/// "--demo" (synthetic characters, synthetic layouts and display, a demo applier; used for screenshots, never writes, never
/// reads the game account), "--demo-review" (the same, and opens the review of the combo at startup) and
/// "--demo-write-hint" (the same, and asks to write, so the reminder before a write is shown), "--demo-update" (the same, and
/// the update feed answers "a newer version exists", so the update banner and the Settings controls can be shown; the check
/// runs at startup in this mode only).
/// Options given on the command line apply to this run only; they are not saved in the settings.
/// </summary>
public sealed record StartupOptions(
    string Page,
    GameUiLayoutId? Layout,
    bool ShowHiddenSlots,
    string? ApplyClass = null,
    string? ApplySpec = null,
    bool DemoData = false,
    bool DemoReview = false,
    bool CooldownDemo = false,
    double? Zoom = null,
    bool DemoWriteHint = false,
    int DemoCharacters = 0,
    bool DemoCharacterPicker = false,
    bool DemoUpdate = false,
    bool NoCapture = false,
    bool Tour = false)
{
    public const string ApplyPage = "apply";
    public const string PreviewPage = "preview";
    public const string SettingsPage = "settings";

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var page = ApplyPage;
        GameUiLayoutId? layout = null;
        var showHiddenSlots = false;
        string? applyClass = null;
        string? applySpec = null;
        var demoData = false;
        var demoReview = false;
        var cooldownDemo = false;
        var demoWriteHint = false;
        var demoUpdate = false;
        double? zoom = null;
        var demoCharacters = 0;
        var demoCharacterPicker = false;
        var noCapture = false;
        var tour = false;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--page" when i + 1 < args.Count:
                    page = ParsePage(args[i + 1]);
                    i++;
                    break;
                case "--layout" when i + 1 < args.Count:
                    layout = ParseLayout(args[i + 1]);
                    i++;
                    break;
                case "--no-capture":
                    noCapture = true;
                    break;
                case "--tour":
                    tour = true;
                    break;
                case "--hidden-slots":
                    showHiddenSlots = true;
                    break;
                case "--cooldown-demo":
                    cooldownDemo = true;
                    break;
                case "--zoom" when i + 1 < args.Count:
                    zoom = double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                        ? parsed
                        : null;
                    i++;
                    break;
                case "--class" when i + 1 < args.Count:
                    applyClass = args[i + 1];
                    i++;
                    break;
                case "--spec" when i + 1 < args.Count:
                    applySpec = args[i + 1];
                    i++;
                    break;
                case "--demo":
                    demoData = true;
                    break;
                case "--demo-review":
                    demoData = true;
                    demoReview = true;
                    break;
                case "--demo-write-hint":
                    demoData = true;
                    demoReview = true;
                    demoWriteHint = true;
                    break;
                case "--demo-characters" when i + 1 < args.Count:
                    demoData = true;
                    demoCharacters = int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                        && count is >= 0 and <= DemoGameFiles.MaxCrowd
                        ? count
                        : 0;
                    i++;
                    break;
                case "--demo-character-picker":
                    demoData = true;
                    demoCharacterPicker = true;
                    break;
                case "--demo-update":
                    demoData = true;
                    demoUpdate = true;
                    break;
            }
        }

        return new StartupOptions(
            page,
            layout,
            showHiddenSlots,
            applyClass,
            applySpec,
            demoData,
            demoReview,
            cooldownDemo,
            zoom,
            demoWriteHint,
            demoCharacters,
            demoCharacterPicker,
            demoUpdate,
            noCapture,
            tour);
    }

    private static string ParsePage(string text) => text.ToLowerInvariant() switch
    {
        PreviewPage => PreviewPage,
        SettingsPage => SettingsPage,
        _ => ApplyPage,
    };

    private static GameUiLayoutId? ParseLayout(string text) => text.ToLowerInvariant() switch
    {
        "general" => GameUiLayoutId.General,
        "preset1" => GameUiLayoutId.Preset1,
        "preset2" => GameUiLayoutId.Preset2,
        "preset3" => GameUiLayoutId.Preset3,
        _ => null,
    };
}
