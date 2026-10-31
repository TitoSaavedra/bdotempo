using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Services;

/// <summary>
/// Command-line options: "--page apply|preview", "--layout general|preset1|preset2|preset3" and "--panels" (show the UI panel overlay).
/// Options given on the command line apply to this run only; they are not saved in the settings.
/// </summary>
public sealed record StartupOptions(string Page, GameUiLayoutId? Layout, bool ShowPanels)
{
    public const string ApplyPage = "apply";
    public const string PreviewPage = "preview";

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var page = ApplyPage;
        GameUiLayoutId? layout = null;
        var showPanels = false;
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
                case "--panels":
                    showPanels = true;
                    break;
            }
        }

        return new StartupOptions(page, layout, showPanels);
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
