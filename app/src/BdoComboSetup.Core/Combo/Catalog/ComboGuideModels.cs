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

/// <summary>
/// What a chain is for. <see cref="ChainKind.Buff"/> and <see cref="ChainKind.Combo"/> chains are applied automatically
/// to the cooldown bar. <see cref="ChainKind.Mobility"/> and <see cref="ChainKind.Cancels"/> skills are placed by hand
/// from <see cref="ComboGuide.ManualPool"/>, never applied automatically. <see cref="ChainKind.Other"/> chains are
/// reference text.
/// </summary>
public enum ChainKind
{
    Other,
    Buff,
    Combo,
    Mobility,
    Cancels,
}

/// <summary>
/// A combo guide as the site shows it: chains of steps, tips and class add-ons.
/// <see cref="ManualPool"/> lists the skills of the mobility and cancels chains, once each, in guide order.
/// </summary>
/// <param name="Title">The site's title for the guide, usually the class and spec.</param>
/// <param name="Description">The short kicker, for example "PvE Combo Guide". Null when the site has none.</param>
/// <param name="Source">The guide page on bdoclasshub.com.</param>
/// <param name="ManualPool">The skills the user places by hand. Empty when the guide has no mobility or cancels chain.</param>
public sealed record ComboGuide(
    string Title,
    string? Description,
    string Source,
    IReadOnlyList<ComboChain> Chains,
    IReadOnlyList<ComboNote> Notes,
    IReadOnlyList<ComboAddon> Addons,
    IReadOnlyList<ComboSkill> ManualPool);

/// <summary>A titled sequence of steps, such as "Damage Combo". <see cref="Notes"/> are the prose lines of the chain.</summary>
/// <param name="Kind">What the chain is for. Defaults to <see cref="ChainKind.Other"/>, which is never applied automatically.</param>
public sealed record ComboChain(
    string Section,
    string Title,
    IReadOnlyList<string> Notes,
    IReadOnlyList<ComboStep> Steps,
    ChainKind Kind = ChainKind.Other);

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
/// <see cref="Tooltip"/> is the game's skill tooltip, or null when the bundle has none for the skill.
/// </summary>
public sealed record ComboSkill(
    int SkillNo,
    BilingualText Name,
    string? DescriptionEs,
    Func<Stream?> OpenIcon,
    SkillTooltip? Tooltip = null);

/// <summary>
/// The game's tooltip for a skill (bdoclasshub skill data), as plain text and numbers. The app formats the numbers.
/// </summary>
/// <param name="Name">The skill name, in <see cref="Language"/>.</param>
/// <param name="Language">The language of the text: "es-mx", "es" or "en", whichever the skill first has.</param>
/// <param name="Keys">Key hints the site lists for the skill.</param>
/// <param name="RequiredLevel">The level that unlocks the skill, or null.</param>
/// <param name="Costs">Costs such as MP. Empty when the skill has none.</param>
/// <param name="CooldownSeconds">The cooldown in seconds, or null when the skill has none listed.</param>
/// <param name="Damage">One row per damage line (an attack and its hits, or a total).</param>
/// <param name="PvpPercent">PvP damage as a percent of the PvE damage, or null.</param>
/// <param name="Protection">Protection lines such as super armor.</param>
/// <param name="Extras">Extra lines of the skill.</param>
/// <param name="Features">Feature lines such as targets or effects.</param>
public sealed record SkillTooltip(
    string Name,
    string Language,
    IReadOnlyList<string> Keys,
    int? RequiredLevel,
    IReadOnlyList<SkillCost> Costs,
    double? CooldownSeconds,
    IReadOnlyList<SkillDamage> Damage,
    double? PvpPercent,
    IReadOnlyList<string> Protection,
    IReadOnlyList<string> Extras,
    IReadOnlyList<string> Features);

/// <summary>A cost of a skill, for example type "MP", label "Cost", value "30".</summary>
public sealed record SkillCost(string Type, string Label, string Value);

/// <summary>
/// One damage line of a skill. <see cref="Percent"/> is the damage in percent of the attack, <see cref="Hits"/> the number of
/// hits and <see cref="MaxHits"/> the hit cap when the game gives one.
/// </summary>
public sealed record SkillDamage(string Label, double Percent, int Hits, int? MaxHits);
