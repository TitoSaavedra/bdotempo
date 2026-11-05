using System.Globalization;
using BdoComboSetup.Core.Localization;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The text of a skill's hover tooltip: its name, its Spanish game description and the lines of its game tooltip (keys, level,
/// costs, cooldown, damage, PvP, protection, extras, features). The numbers are formatted here, in the app language.
/// </summary>
public sealed record SkillTooltipView(
    string Name,
    string? Description,
    string Glyph,
    Func<Stream?> OpenIcon,
    IReadOnlyList<string> Lines)
{
    /// <summary>True when the skill has a description to show.</summary>
    public bool HasDescription => Description is not null;

    /// <summary>True when the game tooltip has any line to show.</summary>
    public bool HasLines => Lines.Count > 0;
}

/// <summary>Builds <see cref="SkillTooltipView"/> from a skill of the bundle. Pure: no UI, no files.</summary>
public static class SkillTooltipText
{
    private const int GlyphLetters = 2;

    /// <summary>
    /// Builds the tooltip of a skill. The name is in the app language (the game tooltip's name when the guide has none), and each
    /// line is "label: value" or a plain text line.
    /// </summary>
    public static SkillTooltipView Build(ComboSkill skill, ILocalizer localizer, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);

        var game = skill.Tooltip;
        var name = skill.Name.For(culture);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = game?.Name ?? string.Empty;
        }

        var lines = new List<string>();
        if (game is not null)
        {
            AddGameLines(game, localizer, culture, lines);
        }

        return new SkillTooltipView(
            Name: name,
            Description: string.IsNullOrWhiteSpace(skill.DescriptionEs) ? null : skill.DescriptionEs.Trim(),
            Glyph: GlyphOf(name),
            OpenIcon: skill.OpenIcon,
            Lines: lines);
    }

    private static void AddGameLines(SkillTooltip game, ILocalizer localizer, CultureInfo culture, List<string> lines)
    {
        if (game.Keys.Count > 0)
        {
            lines.Add(Line(localizer.Get("SkillTooltip.Keys"), string.Join(", ", game.Keys)));
        }

        if (game.RequiredLevel is { } level)
        {
            lines.Add(Line(localizer.Get("SkillTooltip.Level"), level.ToString(culture)));
        }

        foreach (var cost in game.Costs)
        {
            var label = string.IsNullOrWhiteSpace(cost.Label) ? localizer.Get("SkillTooltip.Cost") : cost.Label;
            lines.Add(Line(label, $"{cost.Value} {cost.Type}".Trim()));
        }

        if (game.CooldownSeconds is { } seconds)
        {
            lines.Add(Line(localizer.Get("SkillTooltip.Cooldown"), localizer.Format("SkillTooltip.CooldownValue", Number(seconds, culture))));
        }

        foreach (var row in game.Damage)
        {
            var label = string.IsNullOrWhiteSpace(row.Label) ? localizer.Get("SkillTooltip.Damage") : row.Label;
            var value = $"{Number(row.Percent, culture)}%";
            if (row.Hits > 1)
            {
                value = $"{value} · {localizer.Format("SkillTooltip.Hits", row.Hits)}";
            }

            lines.Add(Line(label, value));
        }

        if (game.PvpPercent is { } pvp)
        {
            lines.Add(Line(localizer.Get("SkillTooltip.Pvp"), $"{Number(pvp, culture)}%"));
        }

        lines.AddRange(game.Protection.Where(text => !string.IsNullOrWhiteSpace(text)));
        lines.AddRange(game.Extras.Where(text => !string.IsNullOrWhiteSpace(text)));
        lines.AddRange(game.Features.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>The two first letters of the name, in capitals: the glyph drawn when the skill has no icon.</summary>
    private static string GlyphOf(string name) =>
        string.Concat(name.Where(char.IsLetter).Take(GlyphLetters).Select(char.ToUpperInvariant));

    private static string Number(double value, CultureInfo culture) => value.ToString("0.##", culture);

    private static string Line(string label, string value) => $"{label}: {value}";
}
