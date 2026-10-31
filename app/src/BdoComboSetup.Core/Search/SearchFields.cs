using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Core.Search;

/// <summary>
/// The texts each selector of the app searches in. Every selector uses <see cref="SearchText"/> and these fields, so the rule
/// is the same everywhere: a character by name and by class (Spanish and English), a class by its two names, a skill by its
/// two names and its number, an account by its number. A pcid is never searched and never shown.
/// </summary>
public static class SearchFields
{
    /// <summary>The character's name and, when known, the class names in both languages (any UI language finds it).</summary>
    public static IEnumerable<string?> ForCharacter(CharacterOption character, ClassInfo? classInfo) =>
        [character.Name, classInfo?.Name.En, classInfo?.Name.Es];

    public static IEnumerable<string?> ForClass(ClassInfo info) => ForNames(info.Name);

    public static IEnumerable<string?> ForNames(BilingualText names) => [names.En, names.Es];

    /// <summary>The skill's English and Spanish names and its number ("1926").</summary>
    public static IEnumerable<string?> ForSkill(SkillOption option) =>
        [option.Skill.Name.En, option.Skill.Name.Es, option.SkillNo.ToString(CultureInfo.InvariantCulture)];

    /// <summary>An account is its number (the name of its folder under UserCache).</summary>
    public static IEnumerable<string?> ForAccount(string account) => [account];
}
