using BdoComboSetup.Core.Localization;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// The remaining time drawn on a cooldown slot: hours when it is over an hour, minutes when over a minute, else seconds
/// (the game's Time_Formatting_ShowTop rule). The unit letters are in the resources, so each language can change them.
/// </summary>
public static class CooldownTimeText
{
    private const int SecondsPerMinute = 60;
    private const int SecondsPerHour = 3600;

    public static string Format(int seconds, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var remaining = Math.Max(0, seconds);
        if (remaining > SecondsPerHour)
        {
            return localizer.Format("Preview.Cooldown.Hours", remaining / SecondsPerHour);
        }

        if (remaining > SecondsPerMinute)
        {
            return localizer.Format("Preview.Cooldown.Minutes", remaining / SecondsPerMinute);
        }

        return localizer.Format("Preview.Cooldown.Seconds", remaining);
    }
}
