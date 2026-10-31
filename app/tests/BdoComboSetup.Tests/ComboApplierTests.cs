using System.Text;
using System.Text.Json;
using BdoComboSetup.Core.Backup;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Tests;

public sealed class ComboApplierTests : IDisposable
{
    private readonly ComboTestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static ComboApplyRequest Request(IReadOnlyList<CooldownSlot>? slots = null, IReadOnlyList<QuickSlotAssignment>? quick = null) =>
        TamerSlots.Request(ComboTestRig.DefaultPcid, slots, quick);

    [Fact]
    public async Task Dry_run_reports_the_diff_and_writes_nothing()
    {
        var original = SyntheticSave.Text();
        _rig.WriteCharacter(original);

        var result = await _rig.Applier.DryRunAsync(Request());

        Assert.Equal(OperationOutcome.DryRun, result.Outcome);
        Assert.Equal(OperationProblem.None, result.Problem);
        Assert.NotNull(result.Diff);
        Assert.True(result.Diff.Changed);
        Assert.Contains(result.Diff.Slots, s => s.Slot == 0 && s.OldSkillNo == 6457 && s.NewSkillNo == 1937);
        Assert.Contains(result.Diff.Memos, m => m.SkillNo == 1937 && m.Status == MemoStatus.Added);
        Assert.Contains(result.Diff.Memos, m => m.SkillNo == 1921 && m.Status == MemoStatus.Added);
        Assert.Equal(original, _rig.ReadCharacterText());
        Assert.Empty(_rig.BackupFolders());
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public async Task Apply_takes_a_verified_backup_first_then_writes_and_reads_back()
    {
        var original = SyntheticSave.Text();
        var originalBytes = Utf8(original);
        _rig.WriteCharacter(originalBytes);

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Written, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.BackupPath);
        Assert.Single(_rig.BackupFolders());

        var backedUp = Path.Combine(result.BackupPath!, ComboTestRig.Account, ComboTestRig.DefaultPcid, GameDataLocator.CharacterFileName);
        Assert.Equal(originalBytes, File.ReadAllBytes(backedUp));

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.BackupPath!, BackupService.ManifestName)));
        var root = manifest.RootElement;
        Assert.Equal("apply", root.GetProperty("reason").GetString());
        Assert.Equal(ComboTestRig.Account, root.GetProperty("account").GetString());
        var file = root.GetProperty("files")[0];
        Assert.Equal(BackupService.Sha256Hex(originalBytes), file.GetProperty("sha256").GetString());

        var written = GameVarsText.Parse(_rig.ReadCharacterText());
        Assert.Equal(1937, written.Slots[0]);
        Assert.Equal(1306, written.Slots[5]);
        Assert.Equal(0, written.Slots[6]);
    }

    [Fact]
    public async Task Applying_the_same_combo_again_is_unchanged_and_takes_no_second_backup()
    {
        _rig.WriteCharacter(SyntheticSave.Text());
        await _rig.Applier.ApplyAsync(Request());
        var afterFirst = _rig.ReadCharacter();

        var second = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Unchanged, second.Outcome);
        Assert.Equal(afterFirst, _rig.ReadCharacter());
        Assert.Single(_rig.BackupFolders());
    }

    [Fact]
    public async Task Game_running_and_a_fresh_file_refuses_the_write()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        _rig.Guard.Running = true;

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.GameRunning, result.Problem);
        Assert.Equal(original, _rig.ReadCharacter());
        Assert.Empty(_rig.BackupFolders());
    }

    [Fact]
    public async Task Forcing_the_write_while_the_game_runs_writes_anyway()
    {
        _rig.WriteCharacter(SyntheticSave.Text());
        _rig.Guard.Running = true;
        var request = new ComboApplyRequest(ComboTestRig.DefaultPcid, 16, TamerSlots.Six(), [], ForceWhileGameRuns: true);

        var result = await _rig.Applier.ApplyAsync(request);

        Assert.Equal(OperationOutcome.Written, result.Outcome);
    }

    [Fact]
    public async Task A_file_older_than_the_window_is_not_checked_against_the_game()
    {
        _rig.WriteCharacter(SyntheticSave.Text());
        _rig.AgeCharacterFile();
        _rig.Guard.Running = true;

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Written, result.Outcome);
        Assert.Equal(0, _rig.Guard.Calls);
    }

    [Fact]
    public async Task A_failed_backup_refuses_the_write_and_nothing_is_written()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        _rig.Files.FailWriteWhen = path => path.StartsWith(_rig.BackupsRoot, StringComparison.OrdinalIgnoreCase);

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.BackupFailed, result.Problem);
        Assert.Equal(original, _rig.ReadCharacter());
        Assert.Empty(_rig.BackupFolders());
        Assert.Equal(0, _rig.Files.AtomicReplaceCalls);
    }

    [Fact]
    public async Task A_backup_copy_that_does_not_match_the_original_refuses_the_write()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        _rig.Files.CorruptWriteWhen = path => path.StartsWith(_rig.BackupsRoot, StringComparison.OrdinalIgnoreCase);

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.BackupFailed, result.Problem);
        Assert.Contains("does not match the original", result.ProblemDetail);
        Assert.Equal(original, _rig.ReadCharacter());
        Assert.Empty(_rig.BackupFolders());
    }

    [Fact]
    public async Task A_result_that_fails_the_read_back_is_rolled_back_from_the_backup()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        byte[]? badBytes = null;
        _rig.Files.CorruptAtomicReplace = (path, call, data) =>
        {
            if (call != 1)
            {
                return data;
            }

            badBytes = data.Append((byte)'X').ToArray();
            return badBytes;
        };

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.RolledBack, result.Outcome);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(OperationProblem.ValidationFailed, result.Problem);
        Assert.Equal(original, _rig.ReadCharacter());

        // The apply backup holds the original; the bytes that failed the read back are kept in a "pre-rollback" backup first.
        var folders = _rig.BackupFolders();
        Assert.Equal(2, folders.Count);
        Assert.Equal(new[] { "apply", "pre-rollback" }, _rig.BackupReasons());
        var preRollback = folders[1];
        Assert.Equal(badBytes, _rig.ReadBackupCopy(preRollback));
        Assert.Contains($"pre-rollback backup {preRollback}", result.ProblemDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_write_is_rolled_back_from_the_backup()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        _rig.Files.FailAtomicReplaceWhen = call => call == 1;

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.RolledBack, result.Outcome);
        Assert.Equal(OperationProblem.WriteFailed, result.Problem);
        Assert.Equal(original, _rig.ReadCharacter());
        Assert.Equal(new[] { "apply", "pre-rollback" }, _rig.BackupReasons());
    }

    [Fact]
    public async Task A_rollback_whose_current_bytes_cannot_be_backed_up_restores_nothing()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        byte[]? badBytes = null;
        _rig.Files.CorruptAtomicReplace = (path, call, data) =>
        {
            if (call != 1)
            {
                return data;
            }

            badBytes = data.Append((byte)'X').ToArray();
            return badBytes;
        };

        // The apply backup takes two writes (its copy and its manifest); the third write is the pre-rollback backup's copy.
        var backupWrites = 0;
        _rig.Files.FailWriteWhen = path => path.StartsWith(_rig.BackupsRoot, StringComparison.OrdinalIgnoreCase) && ++backupWrites == 3;

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Failed, result.Outcome);
        Assert.Equal(OperationProblem.RollbackFailed, result.Problem);
        Assert.Equal(badBytes, _rig.ReadCharacter());
        Assert.Single(_rig.BackupFolders());
        Assert.Equal(new[] { "apply" }, _rig.BackupReasons());
        Assert.Equal(1, _rig.Files.AtomicReplaceCalls);
    }

    [Fact]
    public async Task A_skill_whose_name_does_not_match_the_table_is_refused_before_any_backup()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        var slots = new[] { new CooldownSlot(0, 1937, "Garuda", "R 1") };

        var result = await _rig.Applier.DryRunAsync(Request(slots));

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.SkillMismatch, result.Problem);
        Assert.Contains("does not match skillName", result.ProblemDetail);
        Assert.Equal(original, _rig.ReadCharacter());
    }

    [Fact]
    public async Task Without_a_skill_table_the_apply_is_refused()
    {
        _rig.WriteCharacter(SyntheticSave.Text());
        var applier = new ComboApplier(
            _rig.Locator,
            _rig.Backups,
            _rig.Guard,
            _rig.Files,
            () => SkillTable.Load(Path.Combine(_rig.Root, "missing.json"), _rig.Log.Logger),
            _rig.Log.Logger);

        var result = await applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.SkillTableMissing, result.Problem);
        Assert.Empty(_rig.BackupFolders());
    }

    [Fact]
    public async Task An_unknown_character_is_refused()
    {
        _rig.WriteCharacter(SyntheticSave.Text());

        var result = await _rig.Applier.DryRunAsync(TamerSlots.Request("10000000000000002"));

        Assert.Equal(OperationProblem.CharacterNotFound, result.Problem);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public async Task A_pcid_that_is_not_digits_is_refused(string pcid)
    {
        var result = await _rig.Applier.DryRunAsync(TamerSlots.Request(pcid));

        Assert.Equal(OperationProblem.InvalidRequest, result.Problem);
    }

    [Fact]
    public async Task Writing_the_cooldown_bar_needs_at_least_one_slot()
    {
        _rig.WriteCharacter(SyntheticSave.Text());

        var result = await _rig.Applier.DryRunAsync(Request(slots: []));

        Assert.Equal(OperationProblem.InvalidRequest, result.Problem);
    }

    [Fact]
    public async Task Choosing_nothing_to_write_is_refused()
    {
        _rig.WriteCharacter(SyntheticSave.Text());
        var request = new ComboApplyRequest(ComboTestRig.DefaultPcid, 16, TamerSlots.Six(), [], false, false, false);

        var result = await _rig.Applier.DryRunAsync(request);

        Assert.Equal(OperationProblem.InvalidRequest, result.Problem);
    }

    [Fact]
    public async Task A_slot_section_other_than_version_2_is_refused()
    {
        var original = Utf8(SyntheticSave.Text().Replace("SkillCoolTimeSlot Version=\"2\"", "SkillCoolTimeSlot Version=\"1\"", StringComparison.Ordinal));
        _rig.WriteCharacter(original);

        var result = await _rig.Applier.ApplyAsync(Request());

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.UnsupportedLayout, result.Problem);
        Assert.Contains("Version 1", result.ProblemDetail);
        Assert.Equal(original, _rig.ReadCharacter());
        Assert.Empty(_rig.BackupFolders());
    }

    [Fact]
    public async Task Turning_the_cooldown_bar_off_keeps_its_section_byte_for_byte()
    {
        var original = SyntheticSave.Text();
        _rig.WriteCharacter(original);
        var request = new ComboApplyRequest(ComboTestRig.DefaultPcid, 16, TamerSlots.Six(), [], WriteCooldownBar: false);

        var result = await _rig.Applier.ApplyAsync(request);

        Assert.Equal(OperationOutcome.Written, result.Outcome);
        Assert.Empty(result.Diff!.Slots);
        Assert.Equal(GameVarsText.SlotSectionText(original), GameVarsText.SlotSectionText(_rig.ReadCharacterText()));
        Assert.NotEqual(original, _rig.ReadCharacterText());
    }

    [Fact]
    public async Task Quick_slots_are_written_and_the_other_records_keep_their_bytes()
    {
        var section = SyntheticSave.QuickSection();
        var original = SyntheticSave.Text(quickSection: section);
        _rig.WriteCharacter(original);
        var quick = new[]
        {
            new QuickSlotAssignment(1, 1347),
            new QuickSlotAssignment(3, 1926),
            new QuickSlotAssignment(7, null),
        };
        var request = new ComboApplyRequest(ComboTestRig.DefaultPcid, 16, TamerSlots.Six(), quick, WriteCooldownBar: false, WriteMemos: false);

        var result = await _rig.Applier.ApplyAsync(request);

        Assert.Equal(OperationOutcome.Written, result.Outcome);
        Assert.Equal(
            new (int Position, int? OldSkill, int? NewSkill)[] { (1, 5153, 1347), (3, null, 1926), (7, 6457, null) },
            result.Diff!.QuickSlots.Select(c => (c.Position, c.OldSkillNo, c.NewSkillNo)).ToArray());

        var after = _rig.ReadCharacterText();
        var records = QuickSlotData.Read(after);
        Assert.Equal(1347, records[0].SkillNo);
        Assert.Equal(1926, records[2].SkillNo);
        Assert.Equal(QuickRecordKind.Empty, records[6].Kind);
        var before = QuickSlotData.Read(original);
        Assert.Equal(before[1].RawLine, records[1].RawLine);
        for (var key = 9; key < 20; key++)
        {
            Assert.Equal(before[key].RawLine, records[key].RawLine);
        }

        Assert.Equal(GameVarsText.SlotSectionText(original), GameVarsText.SlotSectionText(after));
    }

    [Fact]
    public async Task A_quick_slot_holding_an_item_is_refused_and_nothing_is_written()
    {
        var original = Utf8(SyntheticSave.Text(quickSection: SyntheticSave.QuickSectionWithItem()));
        _rig.WriteCharacter(original);
        var request = new ComboApplyRequest(
            ComboTestRig.DefaultPcid,
            16,
            TamerSlots.Six(),
            [new QuickSlotAssignment(3, 1926)]);

        var result = await _rig.Applier.ApplyAsync(request);

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.QuickSlotRefused, result.Problem);
        Assert.Contains("QuickSlotItemData", result.ProblemDetail);
        Assert.Equal(original, _rig.ReadCharacter());
        Assert.Empty(_rig.BackupFolders());
    }

    [Fact]
    public async Task A_file_without_a_quick_slot_section_refuses_quick_slot_writes()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        var request = new ComboApplyRequest(ComboTestRig.DefaultPcid, 16, TamerSlots.Six(), [new QuickSlotAssignment(1, 1347)]);

        var result = await _rig.Applier.ApplyAsync(request);

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(original, _rig.ReadCharacter());
    }

    [Fact]
    public async Task A_quick_slot_skill_of_another_class_is_refused()
    {
        _rig.WriteCharacter(SyntheticSave.Text(quickSection: SyntheticSave.QuickSection()));
        var request = new ComboApplyRequest(ComboTestRig.DefaultPcid, 16, TamerSlots.Six(), [new QuickSlotAssignment(1, 5011)]);

        var result = await _rig.Applier.DryRunAsync(request);

        Assert.Equal(OperationProblem.SkillMismatch, result.Problem);
        Assert.Contains("classId 16", result.ProblemDetail);
    }

    [Fact]
    public async Task The_operation_is_logged_with_a_trace_span_and_duration_and_no_character_name()
    {
        _rig.WriteCharacter(SyntheticSave.Text());

        await _rig.Applier.ApplyAsync(Request());

        var log = File.ReadAllText(_rig.Log.Logger.FilePath);
        Assert.Contains("\"event\":\"combo.apply.start\"", log);
        Assert.Contains("\"event\":\"combo.apply.end\"", log);
        Assert.Contains("\"duration_ms\"", log);
        Assert.Contains("\"trace_id\"", log);
        Assert.Contains("\"event\":\"backup.created\"", log);
    }
}
