using System.Text;
using System.Text.Json;
using BdoComboSetup.Core.Backup;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private static readonly DateTimeOffset FixedClock = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly ComboTestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private BackupTarget Target() => new(ComboTestRig.DefaultPcid, _rig.CharacterPath);

    [Fact]
    public void Create_writes_the_python_layout_with_a_manifest_and_verified_copies()
    {
        var bytes = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(bytes);

        var set = _rig.Backups.Create(ComboTestRig.Account, [Target()], "manual");

        Assert.Matches("^[0-9]{8}T[0-9]{6}Z$", set.Name);
        Assert.Equal(bytes, _rig.ReadBackupCopy(set.Name));
        Assert.Equal(
            Path.Combine(_rig.BackupsRoot, set.Name, ComboTestRig.Account, ComboTestRig.DefaultPcid, GameDataLocator.CharacterFileName),
            _rig.BackupCopyPath(set.Name));

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(set.FolderPath, BackupService.ManifestName)));
        var root = manifest.RootElement;
        Assert.Equal(new[] { "tool", "tool_version", "created_utc", "reason", "account", "files" }, root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("bdo-combo", root.GetProperty("tool").GetString());
        Assert.Equal("0.1.0", root.GetProperty("tool_version").GetString());
        Assert.Equal("manual", root.GetProperty("reason").GetString());
        Assert.Equal(ComboTestRig.Account, root.GetProperty("account").GetString());
        var file = root.GetProperty("files")[0];
        Assert.Equal(new[] { "pcid", "path", "sha256", "size" }, file.EnumerateObject().Select(p => p.Name));
        Assert.Equal(ComboTestRig.DefaultPcid, file.GetProperty("pcid").GetString());
        Assert.Equal($"{ComboTestRig.Account}/{ComboTestRig.DefaultPcid}/gameVariable.xml", file.GetProperty("path").GetString());
        Assert.Equal(BackupService.Sha256Hex(bytes), file.GetProperty("sha256").GetString());
        Assert.Equal(bytes.Length, file.GetProperty("size").GetInt64());
    }

    [Fact]
    public void The_manifest_text_has_the_python_shape_with_crlf_and_unescaped_slashes()
    {
        var bytes = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(bytes);
        var service = new BackupService(_rig.BackupsRoot, _rig.Locator, _rig.Files, _rig.Guard, _rig.Log.Logger, () => FixedClock);

        var set = service.Create(ComboTestRig.Account, [Target()], "manual");

        var expected = $$"""
        {
          "tool": "bdo-combo",
          "tool_version": "0.1.0",
          "created_utc": "2026-10-31T12:00:00+00:00",
          "reason": "manual",
          "account": "{{ComboTestRig.Account}}",
          "files": [
            {
              "pcid": "{{ComboTestRig.DefaultPcid}}",
              "path": "{{ComboTestRig.Account}}/{{ComboTestRig.DefaultPcid}}/gameVariable.xml",
              "sha256": "{{BackupService.Sha256Hex(bytes)}}",
              "size": {{bytes.Length}}
            }
          ]
        }

        """.ReplaceLineEndings("\r\n");
        Assert.Equal(expected, File.ReadAllText(Path.Combine(set.FolderPath, BackupService.ManifestName)));
    }

    [Fact]
    public void Backups_taken_in_the_same_second_never_overwrite_each_other()
    {
        _rig.WriteCharacter(Utf8(SyntheticSave.Text()));
        var service = new BackupService(_rig.BackupsRoot, _rig.Locator, _rig.Files, _rig.Guard, _rig.Log.Logger, () => FixedClock);

        var first = service.Create(ComboTestRig.Account, [Target()], "manual");
        var second = service.Create(ComboTestRig.Account, [Target()], "manual");

        Assert.Equal("20261031T120000Z", first.Name);
        Assert.Equal("20261031T120001Z", second.Name);
        Assert.Equal(2, _rig.BackupFolders().Count);
    }

    [Fact]
    public void Restore_dry_run_reports_the_difference_and_writes_nothing()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        var set = _rig.Backups.Create(ComboTestRig.Account, [Target()], "apply");
        _rig.WriteCharacter(Utf8("changed"));

        var result = _rig.Backups.Restore(set.Name, [], write: false, forceWhileGameRuns: false);

        Assert.Equal(OperationOutcome.DryRun, result.Outcome);
        Assert.Single(result.Items);
        Assert.False(result.Items[0].CurrentIdentical);
        Assert.Equal(Utf8("changed"), _rig.ReadCharacter());
        Assert.Single(_rig.BackupFolders());
    }

    [Fact]
    public void Restore_write_takes_a_pre_restore_backup_and_restores_the_file()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        var set = _rig.Backups.Create(ComboTestRig.Account, [Target()], "apply");
        _rig.WriteCharacter(Utf8("changed"));

        var result = _rig.Backups.Restore(set.Name, [ComboTestRig.DefaultPcid], write: true, forceWhileGameRuns: false);

        Assert.Equal(OperationOutcome.Written, result.Outcome);
        Assert.Equal(original, _rig.ReadCharacter());
        Assert.NotNull(result.PreRestoreBackup);
        Assert.Equal(2, _rig.BackupFolders().Count);
        Assert.Contains("pre-restore", _rig.BackupReasons());
    }

    [Fact]
    public async Task An_apply_can_be_undone_with_a_restore()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        var applied = await _rig.Applier.ApplyAsync(TamerSlots.Request(ComboTestRig.DefaultPcid));
        Assert.Equal(OperationOutcome.Written, applied.Outcome);

        var restored = _rig.Backups.Restore(applied.BackupName!, [], write: true, forceWhileGameRuns: false);

        Assert.Equal(OperationOutcome.Written, restored.Outcome);
        Assert.Equal(original, _rig.ReadCharacter());
    }

    [Fact]
    public void Restore_refuses_a_backup_file_that_does_not_match_its_manifest()
    {
        _rig.WriteCharacter(Utf8(SyntheticSave.Text()));
        var set = _rig.Backups.Create(ComboTestRig.Account, [Target()], "apply");
        File.WriteAllBytes(_rig.BackupCopyPath(set.Name), Utf8("corrupt"));

        var result = _rig.Backups.Restore(set.Name, [], write: false, forceWhileGameRuns: false);

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.BackupFailed, result.Problem);
        Assert.Contains("does not match its manifest", result.ProblemDetail);
    }

    [Fact]
    public void Restore_refuses_while_the_game_runs_and_the_file_is_fresh()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        var set = _rig.Backups.Create(ComboTestRig.Account, [Target()], "apply");
        _rig.WriteCharacter(Utf8("changed"));
        _rig.Guard.Running = true;

        var result = _rig.Backups.Restore(set.Name, [], write: true, forceWhileGameRuns: false);

        Assert.Equal(OperationProblem.GameRunning, result.Problem);
        Assert.Equal(Utf8("changed"), _rig.ReadCharacter());
        Assert.Single(_rig.BackupFolders());
    }

    [Fact]
    public void Restore_with_an_unknown_pcid_or_a_bad_name_is_refused()
    {
        _rig.WriteCharacter(Utf8(SyntheticSave.Text()));
        var set = _rig.Backups.Create(ComboTestRig.Account, [Target()], "apply");

        Assert.Equal(OperationProblem.InvalidRequest, _rig.Backups.Restore(set.Name, ["10000000000000002"], write: false, forceWhileGameRuns: false).Problem);
        Assert.Equal(OperationProblem.InvalidRequest, _rig.Backups.Restore("..\\etc", [], write: false, forceWhileGameRuns: false).Problem);
        Assert.Equal(OperationProblem.InvalidRequest, _rig.Backups.Restore("20001231T000000Z", [], write: false, forceWhileGameRuns: false).Problem);
    }

    [Fact]
    public void List_shows_the_backups_and_flags_unreadable_manifests()
    {
        _rig.WriteCharacter(Utf8(SyntheticSave.Text()));
        var set = _rig.Backups.Create(ComboTestRig.Account, [Target()], "apply");
        var broken = Path.Combine(_rig.BackupsRoot, "20240101T000000Z");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, BackupService.ManifestName), "{ not json");

        var summaries = _rig.Backups.List();

        var good = Assert.Single(summaries, s => s.Name == set.Name);
        Assert.True(good.Readable);
        Assert.Equal("apply", good.Reason);
        Assert.Equal(1, good.Characters);
        Assert.Equal(ComboTestRig.Account, good.Account);
        var bad = Assert.Single(summaries, s => s.Name == "20240101T000000Z");
        Assert.False(bad.Readable);
    }

    [Fact]
    public void List_is_empty_without_a_backups_folder()
    {
        Assert.Empty(_rig.Backups.List());
    }

    [Fact]
    public void Create_refuses_nothing_to_back_up_and_bad_reasons()
    {
        _rig.WriteCharacter(Utf8(SyntheticSave.Text()));

        Assert.Throws<OperationRefusedException>(() => _rig.Backups.Create(ComboTestRig.Account, [], "manual"));
        Assert.Throws<OperationRefusedException>(() => _rig.Backups.Create(ComboTestRig.Account, [Target()], "Bad Reason"));
        Assert.Empty(_rig.BackupFolders());
    }
}
