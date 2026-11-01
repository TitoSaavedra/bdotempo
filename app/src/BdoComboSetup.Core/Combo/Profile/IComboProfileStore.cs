namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Reads, writes and deletes the user's custom combos: up to ten named slots per class and spec, kept in one file per
/// class and spec. The bdoclasshub guide is never written here: a slot is always a copy of the working combo.
/// </summary>
public interface IComboProfileStore
{
    /// <summary>
    /// The saved slots of the class and spec, in slot order. Empty when there are none or the file cannot be read
    /// (the reason is logged). Empty slots are not listed.
    /// </summary>
    Task<IReadOnlyList<ComboProfileSlot>> ListSlotsAsync(int classType, string specId, CancellationToken cancellationToken = default);

    /// <summary>One slot, or null when it is empty or the file cannot be read (the reason is logged).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not 1..10.</exception>
    Task<ComboProfileSlot?> GetSlotAsync(int classType, string specId, int slot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the chains into a slot, replacing what the slot held, and stamps it with the current UTC time.
    /// The name is optional: null or blank means "no name" and clears the previous one.
    /// </summary>
    /// <returns>The slot as it was written, with its new timestamp.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not 1..10.</exception>
    Task<ComboProfileSlot> SaveSlotAsync(
        int classType,
        string specId,
        int slot,
        string? name,
        IReadOnlyList<ComboProfileChain> chains,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Empties one slot of the class and spec. Returns false when the slot was already empty. The file is removed when
    /// no slot is left.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not 1..10.</exception>
    bool DeleteSlot(int classType, string specId, int slot);

    /// <summary>
    /// Legacy single-combo API, kept while the page still saves one combo per guide: it reads and writes slot 1.
    /// New code should use the slot methods above.
    /// </summary>
    /// <remarks>The saved combo, or null when slot 1 is empty or the file cannot be read (the reason is logged).</remarks>
    Task<ComboProfile?> LoadAsync(int classType, string specId, CancellationToken cancellationToken = default);

    /// <summary>Legacy single-combo API: replaces the chains of slot 1 and keeps its name, if it had one.</summary>
    Task SaveAsync(ComboProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Legacy single-combo API: deletes slot 1. Returns false when it was already empty.</summary>
    bool Delete(int classType, string specId);
}
