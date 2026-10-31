using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Combo;

public sealed record ComboApplyRequest(
    string ClassName,
    string SpecId,
    CharacterOption Character,
    bool DryRun);

public enum ComboApplyStatus
{
    NotConnected,
}

public sealed record ComboApplyResult(ComboApplyStatus Status);

/// <summary>
/// Writes a combo into the game's account files (with a backup first). The real implementation
/// will be the bdo_combo core; the app talks to it only through this interface.
/// </summary>
public interface IComboApplier
{
    Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Stub until the core is connected. It writes nothing and reports NotConnected.</summary>
public sealed class StubComboApplier(AppLogger logger) : IComboApplier
{
    public Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Character name is not logged: it is personal data.
        using var scope = logger.BeginOperation("combo.apply", new Dictionary<string, object?>
        {
            ["class"] = request.ClassName,
            ["spec"] = request.SpecId,
            ["dry_run"] = request.DryRun,
        });

        logger.Warn("combo.apply.not_connected", "Combo applier is a stub; nothing was written");
        return Task.FromResult(new ComboApplyResult(ComboApplyStatus.NotConnected));
    }
}
