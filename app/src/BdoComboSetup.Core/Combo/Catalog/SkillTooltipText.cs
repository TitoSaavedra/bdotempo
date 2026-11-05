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
    IReadOnlyList<SkillTooltipLine> Lines)
{
    /// <summary>True when the skill has a description to show.</summary>
    public bool HasDescription => Description is not null;

    /// <summary>True when the game tooltip has any line to show.</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>True when there is something to show besides the icon: a name, a description or a line.</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Name) || HasDescription || HasLines;
}

/// <summary>One line of a skill tooltip: an optional label (with its separator, such as "Cooldown: ") and the value.</summary>
/// <param name="Label">The label and its separator, or an empty string for a plain text line.</param>
/// <param name="Value">The text after the label.</param>
public sealed record SkillTooltipLine(string Label, string Value);

/// <summary>Builds <see cref="SkillTooltipView"/> from a skill of the bundle. Pure: no UI, no files.</summary>
public static class SkillTooltipText
{
    private const int GlyphLetters = 2;

    /// <summary>
    /// Builds the tooltip of a skill. The name is in the app language (the game tooltip's name when the guide has none). Labeled
    /// lines are "label: value"; the rest are plain text lines. Blank fields are left out, so no empty line is ever built.
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

        var lines = new List<SkillTooltipLine>();
        if (game is not null)
        {
            AddGameLines(game, localizer, culture, lines);
        }

        return new SkillTooltipView(
            Name: name.Trim(),
            Description: string.IsNullOrWhiteSpace(skill.DescriptionEs) ? null : skill.DescriptionEs.Trim(),
            Glyph: GlyphOf(name),
            OpenIcon: skill.OpenIcon,
            Lines: lines);
    }

    private static void AddGameLines(SkillTooltip game, ILocalizer localizer, CultureInfo culture, List<SkillTooltipLine> lines)
    {
        var keys = game.Keys.Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
        if (keys.Count > 0)
        {
            lines.Add(Labeled(localizer.Get("SkillTooltip.Keys"), string.Join(", ", keys)));
        }

        if (game.RequiredLevel is { } level)
        {
            lines.Add(Labeled(localizer.Get("SkillTooltip.Level"), level.ToString(culture)));
        }

        foreach (var cost in game.Costs)
        {
            var value = $"{cost.Value} {cost.Type}".Trim();
            if (value.Length == 0)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(cost.Label) ? localizer.Get("SkillTooltip.Cost") : cost.Label;
            lines.Add(Labeled(label, value));
        }

        if (game.CooldownSeconds is { } seconds and > 0d)
        {
            lines.Add(Labeled(localizer.Get("SkillTooltip.Cooldown"), localizer.Format("SkillTooltip.CooldownValue", Number(seconds, culture))));
        }

        foreach (var row in game.Damage)
        {
            var label = string.IsNullOrWhiteSpace(row.Label) ? localizer.Get("SkillTooltip.Damage") : row.Label;
            var value = $"{Number(row.Percent, culture)}%";
            if (row.Hits > 1)
            {
                value = $"{value} · {localizer.Format("SkillTooltip.Hits", row.Hits)}";
            }

            lines.Add(Labeled(label, value));
        }

        if (game.PvpPercent is { } pvp)
        {
            lines.Add(Labeled(localizer.Get("SkillTooltip.Pvp"), $"{Number(pvp, culture)}%"));
        }

        lines.AddRange(PlainLines(game.Protection));
        lines.AddRange(PlainLines(game.Extras));
        lines.AddRange(PlainLines(game.Features));
    }

    private static IEnumerable<SkillTooltipLine> PlainLines(IEnumerable<string> texts) =>
        texts.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => new SkillTooltipLine(string.Empty, text.Trim()));

    /// <summary>The two first letters of the name, in capitals: the glyph drawn when the skill has no icon.</summary>
    private static string GlyphOf(string name) =>
        string.Concat(name.Where(char.IsLetter).Take(GlyphLetters).Select(char.ToUpperInvariant));

    private static string Number(double value, CultureInfo culture) => value.ToString("0.##", culture);

    private static SkillTooltipLine Labeled(string label, string value) => new($"{label}: ", value);
}
