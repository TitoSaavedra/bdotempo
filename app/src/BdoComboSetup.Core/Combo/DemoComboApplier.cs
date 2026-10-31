using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The applier of "--demo". It reads no game file and writes none: a dry run describes the request as a diff (every
/// slot is a change), so the review can be shown without a game account; a write is always refused.
/// </summary>
public sealed class DemoComboApplier(AppLogger logger) : IComboApplier
{
    public Task<ComboApplyResult> DryRunAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var slots = request.CooldownSlots
            .Select(slot => new SlotChange(slot.Slot, 0, slot.SkillNo, string.Empty, slot.SkillName ?? string.Empty))
            .ToList();
        var memos = request.CooldownSlots
            .Where(slot => !string.IsNullOrEmpty(slot.Memo))
            .Select(slot => new MemoChange(slot.SkillNo, null, slot.Memo!, MemoStatus.Added))
            .ToList();
        var quick = request.QuickSlots
            .Select(slot => new QuickSlotChange(slot.Position, null, slot.SkillNo))
            .ToList();
        var diff = new CharacterDiff(request.Pcid, "demo", slots.Count > 0, slots, memos, quick);

        logger.Info("demo.dry_run", "Demo dry run: a synthetic diff, nothing is read or written", new Dictionary<string, object?>
        {
            ["cooldown_slots"] = slots.Count,
            ["quick_slots"] = quick.Count,
        });
        return Task.FromResult(new ComboApplyResult(OperationOutcome.DryRun, OperationProblem.None, null, diff, null, null, []));
    }

    public Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        logger.Warn("demo.write_refused", "Demo mode never writes; the write was refused");
        return Task.FromResult(new ComboApplyResult(
            OperationOutcome.Refused,
            OperationProblem.None,
            "demo mode: nothing is written",
            null,
            null,
            null,
            []));
    }
}
