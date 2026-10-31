using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.GameData;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>Which parts of the combo are written to the character. Each one maps to a write flag of the applier.</summary>
/// <param name="CooldownBar">The 20 cooldown slots (skills by slot).</param>
/// <param name="Memos">The memo text shown for each skill on the cooldown bar.</param>
/// <param name="QuickSlots">The quick bar positions with a skill.</param>
public sealed record ComboWriteOptions(bool CooldownBar = true, bool Memos = true, bool QuickSlots = true);

/// <summary>
/// Turns the working copy of a guide into the request the real applier writes. Only steps with a skill take part:
/// a step with a cooldown slot goes to the bar (slot N is written as N-1, the file's 0-based index), and a step with a
/// quick position goes to the quick bar. Steps without a slot are not written and stay as they are in the game.
/// </summary>
public static class ComboApplyRequestBuilder
{
    public static ComboApplyRequest Build(ComboEditorViewModel editor, string pcid, ComboWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentException.ThrowIfNullOrWhiteSpace(pcid);
        ArgumentNullException.ThrowIfNull(options);

        var steps = editor.AllSteps()
            .Where(step => step.PrimarySkill is not null)
            .ToList();

        var cooldown = steps
            .Where(step => step.CooldownSlot is not null)
            .Select(step => new CooldownSlot(
                step.CooldownSlot!.Value - 1,
                step.PrimarySkill!.SkillNo,
                GameSkillName(step.PrimarySkill),
                options.Memos ? NullIfBlank(step.Memo) : null))
            .ToList();

        var quick = options.QuickSlots
            ? steps
                .Where(step => step.QuickPosition is not null)
                .Select(step => new QuickSlotAssignment(step.QuickPosition!.Value, step.PrimarySkill!.SkillNo))
                .ToList()
            : new List<QuickSlotAssignment>();

        return new ComboApplyRequest(
            pcid,
            editor.ClassType,
            cooldown,
            quick,
            options.CooldownBar,
            options.Memos,
            options.QuickSlots);
    }

    /// <summary>The Spanish client name the skill table is checked against. Falls back to the English name when there is none.</summary>
    private static string GameSkillName(ComboSkill skill) => string.IsNullOrWhiteSpace(skill.Name.Es) ? skill.Name.En : skill.Name.Es!;

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
