using System.Text;
using BdoComboSetup.Core.Backup;
using BdoComboSetup.Core.Files;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Writes a combo into one character's gameVariable.xml (port of the Python tool's apply command).
/// Order: validate the request, plan the cooldown slots, check the skills against the table, read the file, compute and
/// verify the new bytes, then (apply only) guard, back up, write, read back and roll back on failure.
/// </summary>
public sealed class ComboApplier : IComboApplier
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly IReadOnlyDictionary<int, string> NoMemos = new Dictionary<int, string>();

    private readonly GameDataLocator _locator;
    private readonly IBackupService _backups;
    private readonly IGameProcessGuard _guard;
    private readonly IFileStore _files;
    private readonly Func<SkillTable> _loadSkillTable;
    private readonly AppLogger _logger;
    private readonly Func<DateTime> _utcNow;

    public ComboApplier(
        GameDataLocator locator,
        IBackupService backups,
        IGameProcessGuard guard,
        IFileStore files,
        Func<SkillTable> loadSkillTable,
        AppLogger logger,
        Func<DateTime>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(loadSkillTable);
        ArgumentNullException.ThrowIfNull(logger);

        _locator = locator;
        _backups = backups;
        _guard = guard;
        _files = files;
        _loadSkillTable = loadSkillTable;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Run(request, write: true), cancellationToken);
    }

    public Task<ComboApplyResult> DryRunAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Run(request, write: false), cancellationToken);
    }

    private ComboApplyResult Run(ComboApplyRequest request, bool write)
    {
        var operation = write ? "combo.apply" : "combo.dry_run";
        using var scope = _logger.BeginOperation(operation, new Dictionary<string, object?>
        {
            ["pcid"] = request.Pcid,
            ["class_id"] = request.ClassId,
            ["cooldown_slots"] = request.CooldownSlots.Count,
            ["quick_slots"] = request.QuickSlots.Count,
            ["write_cooldown_bar"] = request.WriteCooldownBar,
            ["write_memos"] = request.WriteMemos,
            ["write_quick_slots"] = request.WriteQuickSlots,
            ["force"] = request.ForceWhileGameRuns,
        });

        var warnings = new List<string>();
        try
        {
            return Execute(request, write, warnings);
        }
        catch (OperationRefusedException ex)
        {
            _logger.Warn("combo.refused", ex.Message, new Dictionary<string, object?>
            {
                ["pcid"] = request.Pcid,
                ["problem"] = ex.Problem.ToString(),
            });
            return Result(OperationOutcome.Refused, ex.Problem, ex.Message, null, null, null, warnings);
        }
        catch (Exception ex)
        {
            scope.Fail(ex);
            return Result(OperationOutcome.Failed, OperationProblem.UnexpectedError, ex.Message, null, null, null, warnings);
        }
    }

    private ComboApplyResult Execute(ComboApplyRequest request, bool write, List<string> warnings)
    {
        ValidateRequest(request);
        var plan = CooldownPlanner.Build(request.CooldownSlots, _logger, warnings);
        var table = _loadSkillTable();
        CheckSkills(request, plan, table);

        var account = _locator.ResolveAccount();
        var path = _locator.CharacterFilePath(account, request.Pcid);
        if (!_files.Exists(path))
        {
            throw new OperationRefusedException(OperationProblem.CharacterNotFound, $"character {request.Pcid} not found under account {account}");
        }

        var original = _files.ReadAllBytes(path);
        var text = DecodeStrict(original);
        var before = Guard(() => GameVarsText.Parse(text));

        var skillsToWrite = request.WriteCooldownBar ? plan.Skills : before.Slots.ToArray();
        var memosToWrite = request.WriteMemos ? plan.Memos : NoMemos;
        var assignments = request.WriteQuickSlots ? request.QuickSlots : [];

        var cooldownText = Guard(() => GameVarsText.ApplyCombo(text, skillsToWrite, memosToWrite, request.WriteCooldownBar, request.WriteMemos));
        var problems = new List<string>();
        problems.AddRange(Guard(() => GameVarsText.Verify(text, cooldownText, skillsToWrite, memosToWrite)));
        if (!request.WriteCooldownBar && GameVarsText.SlotSectionText(text) != GameVarsText.SlotSectionText(cooldownText))
        {
            problems.Add("slot section changed although cooldown writing is off");
        }

        if (!request.WriteMemos && GameVarsText.MemoSectionText(text) != GameVarsText.MemoSectionText(cooldownText))
        {
            problems.Add("memo section changed although memo writing is off");
        }

        var finalText = cooldownText;
        if (assignments.Count > 0)
        {
            finalText = Guard(() => QuickSlotData.Apply(cooldownText, assignments));
            problems.AddRange(Guard(() => QuickSlotData.Verify(cooldownText, finalText, assignments)));
        }

        if (Guard(() => OuterSkeleton(text)) != Guard(() => OuterSkeleton(finalText)))
        {
            problems.Add("bytes outside the edited sections changed");
        }

        var finalBytes = Utf8NoBom.GetBytes(finalText);
        var changed = !finalBytes.SequenceEqual(original);
        var diff = BuildDiff(request, path, changed, text, before, plan, memosToWrite, assignments, table);
        _logger.Info("combo.computed", "Character computed", new Dictionary<string, object?>
        {
            ["pcid"] = request.Pcid,
            ["changed"] = changed,
            ["slots_changed"] = diff.Slots.Count,
            ["memos_added"] = diff.Memos.Count(m => m.Status == MemoStatus.Added),
            ["memos_changed"] = diff.Memos.Count(m => m.Status == MemoStatus.Changed),
            ["quick_slots_changed"] = diff.QuickSlots.Count,
            ["problems"] = problems.Count,
        });

        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                _logger.Log(LogLevel.Error, "combo.validation_problem", problem, new Dictionary<string, object?> { ["pcid"] = request.Pcid });
            }

            throw new OperationRefusedException(OperationProblem.ValidationFailed, string.Join("; ", problems));
        }

        if (!write)
        {
            return Result(OperationOutcome.DryRun, OperationProblem.None, null, diff, null, null, warnings);
        }

        if (!changed)
        {
            return Result(OperationOutcome.Unchanged, OperationProblem.None, null, diff, null, null, warnings);
        }

        WriteGuard.Check(
            _guard,
            [new GuardTarget(request.Pcid, path, _files.GetLastWriteUtc(path))],
            request.ForceWhileGameRuns,
            _utcNow(),
            _logger);

        // A failed backup throws OperationRefusedException (BackupFailed) here, before anything is written.
        var backup = _backups.Create(account, [new BackupTarget(request.Pcid, path)], "apply");

        try
        {
            _files.AtomicReplace(path, finalBytes);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, "combo.write_failed", "Writing the character file failed", new Dictionary<string, object?>
            {
                ["pcid"] = request.Pcid,
                ["error_type"] = ex.GetType().Name,
            }, ex);
            return RollBack(backup, account, request.Pcid, path, diff, warnings, OperationProblem.WriteFailed, [ex.Message]);
        }

        var written = _files.ReadAllBytes(path);
        var readBackProblems = ValidateWritten(text, cooldownText, written, finalBytes, skillsToWrite, memosToWrite, assignments);
        if (readBackProblems.Count > 0)
        {
            return RollBack(backup, account, request.Pcid, path, diff, warnings, OperationProblem.ValidationFailed, readBackProblems);
        }

        _logger.Info("combo.written", "Character file written and validated", new Dictionary<string, object?>
        {
            ["pcid"] = request.Pcid,
            ["backup"] = backup.Name,
        });
        return Result(OperationOutcome.Written, OperationProblem.None, null, diff, backup.Name, backup.FolderPath, warnings);
    }

    private static void ValidateRequest(ComboApplyRequest request)
    {
        if (!GameDataLocator.IsDigits(request.Pcid))
        {
            throw new OperationRefusedException(OperationProblem.InvalidRequest, "PCID must be digits");
        }

        if (request.ClassId <= 0)
        {
            throw new OperationRefusedException(OperationProblem.InvalidRequest, "classId must be positive");
        }

        if (!request.WriteCooldownBar && !request.WriteMemos && !request.WriteQuickSlots)
        {
            throw new OperationRefusedException(OperationProblem.InvalidRequest, "nothing is selected to write");
        }

        if (request.WriteCooldownBar && request.CooldownSlots.Count == 0)
        {
            throw new OperationRefusedException(OperationProblem.InvalidRequest, "the combo has no cooldown slots");
        }
    }

    private static void CheckSkills(ComboApplyRequest request, CooldownPlan plan, SkillTable table)
    {
        var refs = plan.Planned.Select(s => new SkillRef(s.Slot, s.GuideName ?? string.Empty, s.SkillName ?? string.Empty, s.SkillNo));
        var bad = table.Check(refs, request.ClassId).Where(f => !f.Ok).ToList();
        if (bad.Count > 0)
        {
            var details = string.Join("; ", bad.Select(f => $"slot {f.Slot} (skillNo {f.SkillNo}): {string.Join(", ", f.Problems)}"));
            throw new OperationRefusedException(OperationProblem.SkillMismatch, $"combo does not match the skill table: {details}");
        }

        foreach (var quick in request.QuickSlots.Where(q => q.SkillNo is not null))
        {
            var info = table.Get(quick.SkillNo!.Value)
                ?? throw new OperationRefusedException(
                    OperationProblem.SkillMismatch,
                    $"quick slot {quick.Position} (skillNo {quick.SkillNo}): skillNo not found in the skill table");
            if (!info.ClassIds.Contains(request.ClassId))
            {
                throw new OperationRefusedException(
                    OperationProblem.SkillMismatch,
                    $"quick slot {quick.Position} (skillNo {quick.SkillNo}): skillNo does not belong to classId {request.ClassId}");
            }
        }
    }

    private CharacterDiff BuildDiff(
        ComboApplyRequest request,
        string path,
        bool changed,
        string text,
        GameVars before,
        CooldownPlan plan,
        IReadOnlyDictionary<int, string> memosToWrite,
        IReadOnlyList<QuickSlotAssignment> assignments,
        SkillTable table)
    {
        var slots = new List<SlotChange>();
        if (request.WriteCooldownBar)
        {
            for (var slot = 0; slot < CooldownPlanner.SlotCount; slot++)
            {
                var oldSkill = before.Slots[slot];
                var newSkill = plan.Skills[slot];
                if (oldSkill != newSkill)
                {
                    slots.Add(new SlotChange(slot, oldSkill, newSkill, table.Describe(oldSkill), table.Describe(newSkill)));
                }
            }
        }

        var previous = new Dictionary<int, string>();
        foreach (var memo in before.Memos)
        {
            previous[memo.SkillNo] = GameVarsText.XmlUnescapeAttr(memo.Memo);
        }

        var memos = new List<MemoChange>();
        foreach (var (skillNo, value) in memosToWrite)
        {
            if (!previous.TryGetValue(skillNo, out var old))
            {
                memos.Add(new MemoChange(skillNo, null, value, MemoStatus.Added));
            }
            else
            {
                memos.Add(new MemoChange(skillNo, old, value, old == value ? MemoStatus.Unchanged : MemoStatus.Changed));
            }
        }

        var quick = assignments.Count > 0
            ? QuickSlotData.Diff(text, assignments)
            : [];

        return new CharacterDiff(request.Pcid, path, changed, slots, memos, quick);
    }

    private IReadOnlyList<string> ValidateWritten(
        string originalText,
        string cooldownText,
        byte[] written,
        byte[] expected,
        int[] skillsToWrite,
        IReadOnlyDictionary<int, string> memosToWrite,
        IReadOnlyList<QuickSlotAssignment> assignments)
    {
        if (!written.SequenceEqual(expected))
        {
            return ["file on disk differs from the computed result"];
        }

        string writtenText;
        try
        {
            writtenText = StrictUtf8.GetString(written);
        }
        catch (DecoderFallbackException)
        {
            return ["written file is not valid UTF-8"];
        }

        var problems = new List<string>();
        try
        {
            // GameVarsText.Verify compares everything outside the slot and memo sections. The quick slot section is
            // outside those sections on purpose, so it is masked here and checked by QuickSlotData.Verify below.
            problems.AddRange(GameVarsText.Verify(
                QuickSlotData.Mask(originalText),
                QuickSlotData.Mask(writtenText),
                skillsToWrite,
                memosToWrite));
            if (OuterSkeleton(originalText) != OuterSkeleton(writtenText))
            {
                problems.Add("bytes outside the edited sections changed");
            }

            if (assignments.Count > 0)
            {
                problems.AddRange(QuickSlotData.Verify(cooldownText, writtenText, assignments));
            }
        }
        catch (SaveFormatException ex)
        {
            problems.Add($"written file does not parse: {ex.Message}");
        }

        return problems;
    }

    /// <summary>
    /// Puts the original back after a failed write. The current bytes (the ones that failed) are backed up first, with
    /// reason "pre-rollback", like every other write. If that backup cannot be made, nothing is restored: the bad bytes
    /// stay on disk and the original stays in its backup.
    /// </summary>
    private ComboApplyResult RollBack(
        BackupSet backup,
        string account,
        string pcid,
        string path,
        CharacterDiff diff,
        List<string> warnings,
        OperationProblem problem,
        IReadOnlyList<string> problems)
    {
        foreach (var message in problems)
        {
            _logger.Log(LogLevel.Error, "combo.post_write_problem", message, new Dictionary<string, object?> { ["pcid"] = pcid });
        }

        BackupSet current;
        try
        {
            current = _backups.Create(account, [new BackupTarget(pcid, path)], "pre-rollback");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, "combo.rollback_backup_failed", "Backing up the current bytes before the rollback failed; nothing was restored", new Dictionary<string, object?>
            {
                ["pcid"] = pcid,
                ["backup"] = backup.Name,
            }, ex);
            return Result(
                OperationOutcome.Failed,
                OperationProblem.RollbackFailed,
                $"the file was not restored because its current bytes could not be backed up; the original is in backup {backup.Name}",
                diff,
                backup.Name,
                backup.FolderPath,
                warnings);
        }

        try
        {
            _backups.RestoreFile(backup.Name, pcid, path);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, "combo.rollback_failed", "Restoring the backup failed", new Dictionary<string, object?>
            {
                ["pcid"] = pcid,
                ["backup"] = backup.Name,
                ["pre_rollback"] = current.Name,
            }, ex);
            return Result(
                OperationOutcome.Failed,
                OperationProblem.RollbackFailed,
                $"the file could not be restored; the original is in backup {backup.Name}, the current bytes in backup {current.Name}",
                diff,
                backup.Name,
                backup.FolderPath,
                warnings);
        }

        _logger.Warn("combo.rolled_back", "File restored from the backup after a failed write", new Dictionary<string, object?>
        {
            ["pcid"] = pcid,
            ["backup"] = backup.Name,
            ["pre_rollback"] = current.Name,
        });
        var detail = string.Join("; ", problems.Append($"pre-rollback backup {current.Name}"));
        return Result(OperationOutcome.RolledBack, problem, detail, diff, backup.Name, backup.FolderPath, warnings);
    }

    /// <summary>Everything outside the cooldown, memo and quick slot sections, with those sections replaced by placeholders.</summary>
    private static string OuterSkeleton(string text) => QuickSlotData.Mask(GameVarsText.Skeleton(text));

    private static string DecodeStrict(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new OperationRefusedException(OperationProblem.UnsupportedLayout, $"gameVariable.xml is not valid UTF-8: {ex.Message}");
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (SaveFormatException ex)
        {
            throw new OperationRefusedException(OperationProblem.UnsupportedLayout, $"unexpected gameVariable.xml layout: {ex.Message}");
        }
        catch (QuickSlotRefusedException ex)
        {
            throw new OperationRefusedException(OperationProblem.QuickSlotRefused, ex.Message);
        }
    }

    private static ComboApplyResult Result(
        OperationOutcome outcome,
        OperationProblem problem,
        string? detail,
        CharacterDiff? diff,
        string? backupName,
        string? backupPath,
        List<string> warnings) =>
        new(outcome, problem, detail, diff, backupName, backupPath, warnings.ToList());
}
