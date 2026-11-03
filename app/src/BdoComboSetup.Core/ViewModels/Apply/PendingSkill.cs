namespace BdoComboSetup.Core.ViewModels;

/// <summary>A skill of the guide that no cooldown slot holds: the editor lists it so it can be dragged to a slot.</summary>
/// <param name="Step">The step that takes the slot when the skill is placed.</param>
/// <param name="Name">The skill name in the app language.</param>
/// <param name="Glyph">Two letters drawn when the skill has no icon.</param>
/// <param name="OpenIcon">Opens the icon, or null.</param>
public sealed record PendingSkill(ComboStepViewModel Step, string Name, string Glyph, Func<Stream?>? OpenIcon);

/// <summary>One group of pending skills: buffs, combo steps that did not fit, mobility or repositioning.</summary>
public sealed record PendingGroup(string Title, string CountText, IReadOnlyList<PendingSkill> Items);
