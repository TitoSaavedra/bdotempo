using System.Globalization;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using Microsoft.Windows.ApplicationModel.Resources;

namespace BdoComboSetup.Services;

/// <summary>
/// Reads strings from the app's resources.pri, which is built from Strings\&lt;lang&gt;\Resources.resw.
/// The unpackaged loader finds the PRI next to the executable. A missing key is logged and returned as the key itself.
/// </summary>
public sealed class ResourceLocalizer(AppLogger logger) : ILocalizer
{
    private readonly ResourceLoader _loader = new();

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var value = _loader.GetString(key);
        if (string.IsNullOrEmpty(value))
        {
            logger.Warn("localization.missing_key", "Resource key not found", new Dictionary<string, object?>
            {
                ["key"] = key,
            });
            return key;
        }

        return value;
    }

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
