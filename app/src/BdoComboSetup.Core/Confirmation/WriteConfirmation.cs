using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Settings;

namespace BdoComboSetup.Core.Confirmation;

/// <summary>The user's answer to the reminder before a write. <see cref="DontShowAgain"/> only counts when Continue is true.</summary>
public sealed record WriteHintAnswer(bool Continue, bool DontShowAgain);

/// <summary>
/// Shows the reminder before a write to a character ("be on the character selection screen"). The WinUI app shows a
/// ContentDialog; tests use a fake. Returns the answer; never writes anything itself.
/// </summary>
public interface IConfirmationPrompt
{
    Task<WriteHintAnswer> ShowWriteHintAsync();
}

/// <summary>The "don't show again" flag of the reminder.</summary>
public interface IWriteHintSetting
{
    bool SkipWriteHint { get; }

    void SetSkipWriteHint(bool skip);
}

/// <summary>The flag kept in the app settings (settings.json).</summary>
public sealed class AppSettingsWriteHintSetting(AppSettingsStore store) : IWriteHintSetting
{
    public bool SkipWriteHint => store.Settings.SkipWriteHint;

    public void SetSkipWriteHint(bool skip) => store.Update(current => current with { SkipWriteHint = skip });
}

/// <summary>
/// The gate in front of a write to a character. With the flag set, nothing is shown and the write may go on. Otherwise
/// the prompt is shown: Cancel means nothing is written; Continue lets the write go on, and "don't show again" saves the flag.
/// </summary>
public sealed class WriteConfirmation(IWriteHintSetting setting, IConfirmationPrompt prompt, AppLogger logger)
{
    /// <returns>True when the write may go on.</returns>
    public async Task<bool> ConfirmAsync()
    {
        if (setting.SkipWriteHint)
        {
            logger.Info("write.hint_skipped", "Write reminder skipped (the user chose not to see it again)");
            return true;
        }

        var answer = await prompt.ShowWriteHintAsync();
        if (answer.Continue && answer.DontShowAgain)
        {
            setting.SetSkipWriteHint(true);
        }

        logger.Info("write.hint_answered", "Write reminder answered", new Dictionary<string, object?>
        {
            ["continue"] = answer.Continue,
            ["dont_show_again"] = answer.Continue && answer.DontShowAgain,
        });
        return answer.Continue;
    }
}
