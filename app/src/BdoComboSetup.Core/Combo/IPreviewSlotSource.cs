using System.IO;
using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// One skill as the preview shows it. CooldownNumber is the cooldown slot (1-20) of the same skill and QuickPosition
/// its quick slot (1-9), when known. OpenIcon returns a new stream of the icon, or null when there is none. Skill is the
/// bundle's skill when the slot comes from a guide, for its hover tooltip; null otherwise.
/// </summary>
public sealed record PreviewSlot(
    int? SkillNo,
    string? SkillName,
    string? Memo,
    string? CustomName,
    int? CooldownNumber = null,
    int? QuickPosition = null,
    Func<Stream?>? OpenIcon = null,
    ComboSkill? Skill = null);

/// <summary>
/// Where the preview gets what its slots show. <see cref="Changed"/> is raised when any of that changes: the combo
/// being edited on the Apply page, or the character whose quick keys are shown.
/// </summary>
public interface IPreviewSlotSource
{
    event EventHandler? Changed;

    /// <summary>The skill of cooldown slot 1-20, or null when the slot is empty.</summary>
    PreviewSlot? Cooldown(int number);

    /// <summary>The skill of quick slot 1-20, or null when the quick slot is empty. A combo only fills quick slots 1-9.</summary>
    PreviewSlot? Quick(int number);

    /// <summary>The key drawn on quick slot 1-20: the character's bound key, else the default. Null when the slot is unbound.</summary>
    QuickSlotKey? QuickKey(int number);
}
