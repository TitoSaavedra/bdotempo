using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The write side of the "Slot editor" page (the preview over the game). The page shows the combo that the Apply page is
/// editing; here the user can put another skill in one of its cooldown slots. The change is a customization of that working
/// copy only: the bdoclasshub guide is never touched, and "Save my combo" stores it like any other edit.
/// </summary>
public interface ISlotSkillEditor
{
    /// <summary>The class of the combo being edited, or null when the Apply page has no guide open yet.</summary>
    int? ClassType { get; }

    /// <summary>The skills of that class at their top rank, sorted by name. Empty without a combo or without a catalog.</summary>
    IReadOnlyList<SkillOption> Skills { get; }

    /// <summary>
    /// Puts the skill <paramref name="skillNo"/> of the class in cooldown slot 1-20 of the combo being edited. Returns false
    /// when the slot has no step in the combo (there is nothing to replace) or when the class catalog has no such skill.
    /// </summary>
    bool ReplaceCooldownSkill(int number, int skillNo);
}
