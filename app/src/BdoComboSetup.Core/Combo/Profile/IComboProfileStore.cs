namespace BdoComboSetup.Core.Combo;

/// <summary>Reads, writes and deletes the user's combo profiles (one file per class and spec).</summary>
public interface IComboProfileStore
{
    /// <summary>The saved profile, or null when there is none or the file cannot be read (the reason is logged).</summary>
    Task<ComboProfile?> LoadAsync(int classType, string specId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the saved profile of the class and spec.</summary>
    Task SaveAsync(ComboProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Removes the saved profile. Returns false when there was none.</summary>
    bool Delete(int classType, string specId);
}
