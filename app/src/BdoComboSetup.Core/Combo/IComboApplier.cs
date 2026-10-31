using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.Combo;

/// <summary>One cooldown slot to write. <see cref="Slot"/> is the 0-based XML slot (0..19), the UI's slot number minus one.</summary>
/// <param name="SkillNo">Positive game skill number. Slots without an entry are written as empty (0).</param>
/// <param name="SkillName">Game name of the skill (Spanish client name), cross-checked against the skill table.</param>
/// <param name="Memo">Memo text shown for the skill. Null or empty creates no memo entry.</param>
public sealed record CooldownSlot(int Slot, int SkillNo, string? SkillName, string? Memo, string? GuideName = null);

/// <summary>
/// Everything needed to write one character: the PCID, the cooldown slots derived from a combo, optional quick slot
/// assignments, and which parts to write. The Write* flags let the UI skip a part without changing the rest of the file.
/// </summary>
public sealed record ComboApplyRequest(
    string Pcid,
    int ClassId,
    IReadOnlyList<CooldownSlot> CooldownSlots,
    IReadOnlyList<QuickSlotAssignment> QuickSlots,
    bool WriteCooldownBar = true,
    bool WriteMemos = true,
    bool WriteQuickSlots = true);

public enum MemoStatus
{
    Added,
    Changed,
    Unchanged,
}

public sealed record SlotChange(int Slot, int OldSkillNo, int NewSkillNo, string OldName, string NewName);

public sealed record MemoChange(int SkillNo, string? OldMemo, string NewMemo, MemoStatus Status);

/// <summary>The diff of one character file. Lists hold only the changed slots and quick slots; memos hold every planned memo.</summary>
public sealed record CharacterDiff(
    string Pcid,
    string FilePath,
    bool Changed,
    IReadOnlyList<SlotChange> Slots,
    IReadOnlyList<MemoChange> Memos,
    IReadOnlyList<QuickSlotChange> QuickSlots);

/// <summary>
/// Result of an apply or a dry run. <see cref="Problem"/> is set when the outcome is not a success; <see cref="ProblemDetail"/>
/// carries technical data (slot numbers, skill names) and never a sentence for the UI.
/// </summary>
public sealed record ComboApplyResult(
    OperationOutcome Outcome,
    OperationProblem Problem,
    string? ProblemDetail,
    CharacterDiff? Diff,
    string? BackupName,
    string? BackupPath,
    IReadOnlyList<string> Warnings)
{
    public int ExitCode => Outcome.ExitCode();
}

/// <summary>
/// Writes a combo into a character's gameVariable.xml. Every write takes a verified backup first, is refused while the
/// game runs and the file changed in the last 10 seconds (unless forced), and is read back and rolled back on failure.
/// </summary>
public interface IComboApplier
{
    /// <summary>Computes the diff, backs up the file, writes it and validates the result.</summary>
    Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Computes the same diff and checks, and writes nothing.</summary>
    Task<ComboApplyResult> DryRunAsync(ComboApplyRequest request, CancellationToken cancellationToken = default);
}
