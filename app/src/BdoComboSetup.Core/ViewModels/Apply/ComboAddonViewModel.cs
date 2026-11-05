using System.Globalization;
using BdoComboSetup.Core.Combo;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// One class add-on of the guide, shown read-only in "Aplicar combo": the skill it belongs to, the guide's description of
/// it and its effects as label and value pairs. It is never applied: add-ons are information for the player.
/// </summary>
public sealed class ComboAddonViewModel
{
    public ComboAddonViewModel(ComboAddon addon, CultureInfo culture, GuideTranslation translation)
    {
        ArgumentNullException.ThrowIfNull(addon);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(translation);

        Name = addon.Skill.Name.For(culture);
        OpenIcon = addon.Skill.OpenIcon;
        Description = string.IsNullOrWhiteSpace(addon.Description) ? null : translation.Text(addon.Description.Trim());
        Effects = addon.Effects
            .Where(effect => !string.IsNullOrWhiteSpace(effect.Label) || !string.IsNullOrWhiteSpace(effect.Value))
            .Select(effect => new ComboAddonEffectViewModel(
                translation.Text(effect.Label?.Trim() ?? string.Empty),
                translation.Text(effect.Value?.Trim() ?? string.Empty)))
            .ToList();
    }

    /// <summary>The skill name in the page language.</summary>
    public string Name { get; }

    /// <summary>Opens the skill icon; null from the function means the icon is missing.</summary>
    public Func<Stream?> OpenIcon { get; }

    /// <summary>The guide's description of the add-on in the page language, or null when it has none.</summary>
    public string? Description { get; }

    public bool HasDescription => Description is not null;

    /// <summary>Two letters of the name, drawn when the icon is missing.</summary>
    public string Glyph => Name.Where(char.IsLetter).Take(2).Aggregate(string.Empty, (text, letter) => text + char.ToUpperInvariant(letter));

    /// <summary>The effects, in guide order: one row with a label and its value.</summary>
    public IReadOnlyList<ComboAddonEffectViewModel> Effects { get; }

    public bool HasEffects => Effects.Count > 0;
}

/// <summary>One effect of a class add-on: its label (for example "All Damage") and its value (for example "+10%").</summary>
public sealed record ComboAddonEffectViewModel(string Label, string Value);
