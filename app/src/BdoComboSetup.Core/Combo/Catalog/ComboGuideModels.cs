using System.Globalization;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// A name in both languages. The English text is always present; the Spanish one may be missing,
/// and then English is used.
/// </summary>
public sealed record BilingualText(string En, string? Es)
{
    public static BilingualText Same(string text) => new(text, text);

    /// <summary>The Spanish text for Spanish cultures when there is one, otherwise the English text.</summary>
    public string For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (culture.TwoLetterISOLanguageName == "es" && !string.IsNullOrWhiteSpace(Es))
        {
            return Es;
        }

        return En;
    }
}

/// <summary>One class of the catalog. <see cref="Specs"/> lists the specs that have a guide.</summary>
public sealed record ClassInfo(int ClassType, string Slug, BilingualText Name, IReadOnlyList<SpecInfo> Specs);

/// <summary>
/// One spec of a class. <see cref="GuideKey"/> names the guide file; several specs may share one guide
/// (the three Shai experts all use the Talent guide).
/// </summary>
public sealed record SpecInfo(string Id, BilingualText DisplayName, string GuideKey);

/// <summary>A combo guide as the site shows it: chains of steps, tips and class add-ons.</summary>
/// <param name="Title">The site's title for the guide, usually the class and spec.</param>
/// <param name="Description">The short kicker, for example "PvE Combo Guide". Null when the site has none.</param>
/// <param name="Source">The guide page on bdoclasshub.com.</param>
public sealed record ComboGuide(
    string Title,
    string? Description,
    string Source,
    IReadOnlyList<ComboChain> Chains,
    IReadOnlyList<ComboNote> Notes,
    IReadOnlyList<ComboAddon> Addons);

/// <summary>A titled sequence of steps, such as "Damage Combo". <see cref="Notes"/> are the prose lines of the chain.</summary>
public sealed record ComboChain(string Section, string Title, IReadOnlyList<string> Notes, IReadOnlyList<ComboStep> Steps);

/// <summary>One step: the keys to press, the skills it uses, an extra line (such as "opener") and an annotation.</summary>
public sealed record ComboStep(string? Keys, IReadOnlyList<ComboSkill> Skills, string? Extra, string? Note);

/// <summary>A tip from the guide's tips box, with the skills it names.</summary>
public sealed record ComboNote(string Section, string Text, IReadOnlyList<ComboSkill> Skills);

/// <summary>A skill add-on (the skill's add-on card) with its effects.</summary>
public sealed record ComboAddon(string Section, ComboSkill Skill, string? Description, IReadOnlyList<ComboAddonEffect> Effects);

public sealed record ComboAddonEffect(string Label, string Value);

/// <summary>
/// A skill in a guide. <see cref="DescriptionEs"/> is the Spanish game text, plain, with line breaks.
/// <see cref="OpenIcon"/> returns a new stream of the icon, or null when there is none; the caller disposes it.
/// </summary>
public sealed record ComboSkill(int SkillNo, BilingualText Name, string? DescriptionEs, Func<Stream?> OpenIcon);
