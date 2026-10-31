namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Rules for the two slot kinds of a combo: a cooldown slot (1..20) and a quick bar position (1..9).
/// Each value belongs to one step at a time. Giving a value that another step holds swaps the two.
/// </summary>
public static class SlotAssignment
{
    /// <summary>Cooldown slots of the bar: 2 rows x 10 columns.</summary>
    public const int CooldownSlotCount = 20;

    public static bool IsCooldownSlot(int slot) => slot is >= 1 and <= CooldownSlotCount;

    public static bool IsQuickPosition(int position) => position is >= 1 and <= QuickSlotMap.Count;

    /// <summary>
    /// Gives <paramref name="value"/> to <paramref name="target"/>. If another item holds that value, that item receives the
    /// target's previous value, or none when the target had none. Null clears the target.
    /// </summary>
    public static void Assign<T>(IEnumerable<T> items, T target, int? value, Func<T, int?> get, Action<T, int?> set)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        var previous = get(target);
        if (previous == value)
        {
            return;
        }

        if (value is not null)
        {
            var holder = items.FirstOrDefault(item => !ReferenceEquals(item, target) && get(item) == value);
            if (holder is not null)
            {
                set(holder, previous);
            }
        }

        set(target, value);
    }
}
