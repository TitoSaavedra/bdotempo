using System.Globalization;
using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Services;

/// <summary>
/// Command-line options: "--page apply|preview", "--layout general|preset1|preset2|preset3", "--hidden-slots" (draw the slots
/// the game hides, dimmed), "--cooldown-demo" (a 60% cooldown on the first cooldown slot, debug), "--zoom &lt;n&gt;" (zoom
/// relative to fit). For the Apply page "--class &lt;slug&gt;" and "--spec &lt;id&gt;" (for example --class tamer --spec awakening),
/// "--demo" (synthetic characters, synthetic layouts and display, a demo applier; used for screenshots, never writes, never
/// reads the game account) and "--demo-review" (the same, and opens the review of the combo at startup).
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
    double? Zoom = null)
{
    public const string ApplyPage = "apply";
    public const string PreviewPage = "preview";

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
        double? zoom = null;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--page" when i + 1 < args.Count:
                    page = string.Equals(args[i + 1], PreviewPage, StringComparison.OrdinalIgnoreCase) ? PreviewPage : ApplyPage;
                    i++;
                    break;
                case "--layout" when i + 1 < args.Count:
                    layout = ParseLayout(args[i + 1]);
                    i++;
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
            }
        }

        return new StartupOptions(page, layout, showHiddenSlots, applyClass, applySpec, demoData, demoReview, cooldownDemo, zoom);
    }

    private static GameUiLayoutId? ParseLayout(string text) => text.ToLowerInvariant() switch
    {
        "general" => GameUiLayoutId.General,
        "preset1" => GameUiLayoutId.Preset1,
        "preset2" => GameUiLayoutId.Preset2,
        "preset3" => GameUiLayoutId.Preset3,
        _ => null,
    };
}
