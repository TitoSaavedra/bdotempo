namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Source of characters, classes and combos. The real core replaces <see cref="MockComboDataSource"/>.
/// </summary>
public interface IComboDataSource
{
    IReadOnlyList<CharacterOption> GetCharacters();

    IReadOnlyList<string> GetClassNames();

    ComboDefinition GetCombo(string className, ComboMode mode);
}
