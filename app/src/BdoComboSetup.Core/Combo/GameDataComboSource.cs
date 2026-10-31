using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Characters are discovered from the game's UserCache folders (the account is auto-detected unless set in the settings).
/// Classes and combos still come from <paramref name="fallback"/> until the ComboGuide to slot mapping exists.
/// </summary>
public sealed class GameDataComboSource(GameDataLocator locator, IComboDataSource fallback, AppLogger logger) : IComboDataSource
{
    public IReadOnlyList<CharacterOption> GetCharacters()
    {
        try
        {
            var account = locator.ResolveAccount();
            return locator.ListCharacters(account)
                .Select(character => new CharacterOption(character.Pcid, character.Pcid))
                .ToList();
        }
        catch (OperationRefusedException ex)
        {
            logger.Warn("characters.unavailable", ex.Message);
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error("characters.read_failed", "Could not read the character folders", ex);
            return [];
        }
    }

    public IReadOnlyList<string> GetClassNames() => fallback.GetClassNames();

    public ComboDefinition GetCombo(string className, ComboMode mode) => fallback.GetCombo(className, mode);
}
