namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Source of characters and of the Preview's cooldown layout. The real core replaces <see cref="MockComboDataSource"/>.
/// The Apply page takes its classes and guides from <see cref="ICombosCatalog"/> instead.
/// </summary>
public interface IComboDataSource
{
    IReadOnlyList<CharacterOption> GetCharacters();

    ComboDefinition GetCombo(string className, ComboMode mode);
}
