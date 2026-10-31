namespace BdoComboSetup.Core.Operations;

/// <summary>What an operation did. Replaces the Python tool's exit codes (see <see cref="OperationOutcomeExtensions.ExitCode"/>).</summary>
public enum OperationOutcome
{
    /// <summary>Dry run: the diff was computed, nothing was written.</summary>
    DryRun,

    /// <summary>The files were written and read back successfully.</summary>
    Written,

    /// <summary>Nothing differs from the file on disk, so nothing was written.</summary>
    Unchanged,

    /// <summary>Refused before any write (bad input, validation, game running, backup failed, ...).</summary>
    Refused,

    /// <summary>An unexpected error. Nothing was left half-written; see the problem detail.</summary>
    Failed,

    /// <summary>Read-back validation failed after writing and the file was restored from the backup.</summary>
    RolledBack,
}

public static class OperationOutcomeExtensions
{
    /// <summary>Exit code with the same meaning as the Python tool: 0 ok, 1 refused or failed, 3 rolled back.</summary>
    public static int ExitCode(this OperationOutcome outcome) => outcome switch
    {
        OperationOutcome.DryRun or OperationOutcome.Written or OperationOutcome.Unchanged => 0,
        OperationOutcome.RolledBack => 3,
        _ => 1,
    };
}

/// <summary>Why an operation was refused or failed. The UI maps each value to a localized message.</summary>
public enum OperationProblem
{
    None,
    InvalidRequest,
    AccountNotFound,
    CharacterNotFound,
    SkillTableMissing,
    SkillMismatch,
    UnsupportedLayout,
    QuickSlotRefused,
    ValidationFailed,
    GameRunning,
    BackupFailed,
    WriteFailed,
    RollbackFailed,
    UnexpectedError,
}

/// <summary>An expected, user-facing refusal. Caught at the operation boundary, logged as a warning and returned as a result.</summary>
public sealed class OperationRefusedException(OperationProblem problem, string message) : Exception(message)
{
    public OperationProblem Problem { get; } = problem;
}
