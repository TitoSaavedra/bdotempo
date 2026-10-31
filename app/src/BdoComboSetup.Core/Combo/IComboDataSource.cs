namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Source of the Preview's cooldown layout. The characters come from the roster (<c>ICharacterSource</c>) and the classes
/// and guides from <see cref="ICombosCatalog"/>.
/// </summary>
public interface IComboDataSource
{
    ComboDefinition GetCombo(string className, ComboMode mode);
}
