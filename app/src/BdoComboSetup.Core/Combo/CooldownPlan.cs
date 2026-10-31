using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The cooldown slots as they will be written: the 20 SkillNos (0 = empty) and one memo per SkillNo.
/// </summary>
public sealed record CooldownPlan(int[] Skills, IReadOnlyDictionary<int, string> Memos, IReadOnlyList<CooldownSlot> Planned);

/// <summary>Port of the planning rules of the Python tool's build_plan: validation, duplicate warnings and memo dedupe.</summary>
public static class CooldownPlanner
{
    public const int SlotCount = 20;

    /// <summary>
    /// Validates the slots and builds the plan. One memo per SkillNo: the first occurrence (lowest slot) wins, and a later
    /// occurrence with a different non-empty memo is ignored with a warning. Warnings are logged and added to
    /// <paramref name="warnings"/>.
    /// </summary>
    public static CooldownPlan Build(IReadOnlyList<CooldownSlot> slots, AppLogger logger, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var seenSlots = new HashSet<int>();
        foreach (var slot in slots)
        {
            if (slot.Slot is < 0 or >= SlotCount)
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"cooldown slot {slot.Slot} is outside 0..{SlotCount - 1}");
            }

            if (!seenSlots.Add(slot.Slot))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"cooldown slot {slot.Slot} is given twice");
            }

            if (slot.SkillNo <= 0)
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"cooldown slot {slot.Slot}: skillNo must be positive");
            }
        }

        var skills = new int[SlotCount];
        foreach (var slot in slots)
        {
            skills[slot.Slot] = slot.SkillNo;
        }

        foreach (var group in slots.GroupBy(s => s.SkillNo).Where(g => g.Count() > 1))
        {
            var message = $"same skillNo used in more than one slot of the plan ({group.Count()} slots)";
            warnings.Add(message);
            logger.Warn("combo.plan_duplicate_skill", "same skillNo used in more than one slot of the plan", new Dictionary<string, object?>
            {
                ["skill_no"] = group.Key,
                ["slots"] = group.Count(),
            });
        }

        var memos = new Dictionary<int, string>();
        foreach (var slot in slots.OrderBy(s => s.Slot))
        {
            var memo = slot.Memo;
            if (memo is not null && (memo.Contains('\n') || memo.Contains('\r')))
            {
                throw new OperationRefusedException(OperationProblem.InvalidRequest, $"cooldown slot {slot.Slot}: memo must be a single line");
            }

            if (string.IsNullOrEmpty(memo))
            {
                continue;
            }

            if (!memos.TryGetValue(slot.SkillNo, out var kept))
            {
                memos[slot.SkillNo] = memo;
            }
            else if (kept != memo)
            {
                var message = $"conflicting memos for SkillNo {slot.SkillNo}; keeping the first";
                warnings.Add(message);
                logger.Warn("combo.plan_conflicting_memo", "conflicting memos for the same SkillNo; keeping the first", new Dictionary<string, object?>
                {
                    ["skill_no"] = slot.SkillNo,
                    ["slot"] = slot.Slot,
                });
            }
        }

        logger.Info("combo.plan_built", "Cooldown plan built", new Dictionary<string, object?>
        {
            ["slots"] = slots.Count,
            ["memos"] = memos.Count,
        });
        return new CooldownPlan(skills, memos, slots.OrderBy(s => s.Slot).ToList());
    }
}
