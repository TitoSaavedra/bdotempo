using System.Globalization;
using System.Runtime.InteropServices;
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

        // The PRI stores a resw name such as "Apply.NoSkill" as the hierarchy "Apply/NoSkill", so the lookup uses that path.
        var value = Lookup(key.Replace('.', '/'));
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

    /// <summary>
    /// The loader throws a COMException for a name that is not in the PRI instead of returning empty. That is a miss, and it is
    /// logged here so the caller gets the key back.
    /// </summary>
    private string? Lookup(string path)
    {
        try
        {
            return _loader.GetString(path);
        }
        catch (COMException ex)
        {
            logger.Debug("localization.lookup_miss", "Resource name is not in the PRI", new Dictionary<string, object?>
            {
                ["name"] = path,
                ["hresult"] = ex.HResult,
            });
            return null;
        }
    }

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
