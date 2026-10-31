using System.Globalization;
using System.Runtime.InteropServices;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;

namespace BdoComboSetup.Services;

/// <summary>
/// Sets the language the resources (.resw) and the XAML x:Uid strings are loaded in. Windows App SDK reads the override
/// when it loads a resource, so this runs before the first resource is loaded. The override is not persisted for an
/// unpackaged app: it is set on every start from the settings.
/// </summary>
public static class LanguageSetup
{
    /// <summary>Applies the language of the setting (or the Windows language) and returns its code.</summary>
    public static string Apply(string? configured, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var language = AppLanguages.Resolve(configured, CultureInfo.CurrentUICulture);
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or COMException)
        {
            logger.Error("app.language_override_failed", "The language override could not be set; the Windows language is used", ex, new Dictionary<string, object?>
            {
                ["language"] = language,
            });
            return language;
        }

        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(language);
        CultureInfo.CurrentUICulture = new CultureInfo(language);
        logger.Info("app.language", "Interface language set", new Dictionary<string, object?> { ["language"] = language });
        return language;
    }
}
