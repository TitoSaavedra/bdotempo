namespace BdoComboSetup.Core.ViewModels;

/// <summary>What the view needs to draw a skill icon: the skill number and the class slug (for the bdoclasshub fallback).</summary>
public sealed record SkillIconRequest(int SkillNo, string ClassSlug);
