using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The class skill catalog of the asset pack (<c>class-skills.json</c>) as the combo editor needs it: every skill of a class
/// at its top rank as a <see cref="SkillOption"/> (the Spanish name of the game, the icon by skill number), and the lookup
/// by number that brings a skill back when a saved combo is opened again. Read-only; with no catalog it has no rows.
/// </summary>
public sealed class ClassSkillSource
{
    private readonly SkillCatalog _catalog;
    private readonly IAssetProvider? _icons;
    private readonly Dictionary<int, IReadOnlyList<SkillOption>> _byClass = [];

    /// <param name="catalog">The parsed class skill catalog, or null when the asset pack has none.</param>
    /// <param name="icons">The asset provider the skill icons come from, or null (the rows show no icon).</param>
    public ClassSkillSource(SkillCatalog? catalog, IAssetProvider? icons)
    {
        _catalog = catalog ?? SkillCatalog.Empty;
        _icons = icons;
    }

    /// <summary>The skills of a class at their top rank, sorted by name; empty when the catalog does not know the class.</summary>
    public IReadOnlyList<SkillOption> OptionsOf(int classType)
    {
        if (_byClass.TryGetValue(classType, out var cached))
        {
            return cached;
        }

        var options = _catalog.SkillsOf(classType).Select(ToOption).ToList();
        _byClass[classType] = options;
        return options;
    }

    /// <summary>The skill of a class with that number, or null when the catalog does not list it for the class.</summary>
    public ComboSkill? Find(int classType, int skillNo) =>
        OptionsOf(classType).FirstOrDefault(option => option.SkillNo == skillNo)?.Skill;

    /// <summary>
    /// A catalog entry as the editor uses it. The name is the game's (Spanish) in both languages because the catalog has no
    /// English one, and it has no description: the catalog only carries the number and the name of the top rank.
    /// </summary>
    private SkillOption ToOption(SkillCatalogEntry entry)
    {
        var skill = new ComboSkill(
            entry.SkillNo,
            BilingualText.Same(entry.Name),
            DescriptionEs: null,
            OpenIcon: () => _icons?.OpenSkillIcon(entry.SkillNo));
        return new SkillOption(skill, entry.Name);
    }
}
