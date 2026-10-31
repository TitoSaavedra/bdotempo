using System.Text;
using BdoComboSetup.Core.Backup;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Tests;

/// <summary>
/// Backups written by the Python tool and by the earlier C# build must still be listed and restored. The folders are built
/// here from synthetic bytes that follow the same layout and key names as the real backups; no real file is used.
/// </summary>
public sealed class BackupLayoutTests : IDisposable
{
    private const string PythonStamp = "20261029T160822Z";

    private readonly ComboTestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    /// <summary>A manifest with the Python keys, CRLF line ends and one file entry at <paramref name="relativePath"/>.</summary>
    private static string PythonManifest(byte[] copy, string relativePath) => $$"""
        {
          "tool": "bdo-combo",
          "tool_version": "0.1.0",
          "created_utc": "2026-10-29T16:08:23+00:00",
          "reason": "pre-apply",
          "account": "{{ComboTestRig.Account}}",
          "files": [
            {
              "pcid": "{{ComboTestRig.DefaultPcid}}",
              "path": "{{relativePath}}",
              "sha256": "{{BackupService.Sha256Hex(copy)}}",
              "size": {{copy.Length}}
            }
          ]
        }

        """.ReplaceLineEndings("\r\n");

    /// <summary>Writes the copy at account\pcid\gameVariable.xml and the manifest, as the Python tool does.</summary>
    private void WritePythonLayoutBackup(string stamp, byte[] copy, string? relativePath = null)
    {
        var folder = Path.Combine(_rig.BackupsRoot, stamp);
        var copyPath = Path.Combine(folder, ComboTestRig.Account, ComboTestRig.DefaultPcid, GameDataLocator.CharacterFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        File.WriteAllBytes(copyPath, copy);
        var path = relativePath ?? $"{ComboTestRig.Account}/{ComboTestRig.DefaultPcid}/{GameDataLocator.CharacterFileName}";
        File.WriteAllText(Path.Combine(folder, BackupService.ManifestName), PythonManifest(copy, path));
    }

    [Fact]
    public void Restore_reads_a_backup_in_the_python_layout_and_restores_it()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        WritePythonLayoutBackup(PythonStamp, original);
        _rig.WriteCharacter(Utf8("changed"));

        var listed = Assert.Single(_rig.Backups.List());
        Assert.True(listed.Readable);
        Assert.Equal(PythonStamp, listed.Name);
        Assert.Equal("pre-apply", listed.Reason);
        Assert.Equal(ComboTestRig.Account, listed.Account);
        Assert.Equal(1, listed.Characters);
        Assert.Equal("0.1.0", listed.ToolVersion);

        var dryRun = _rig.Backups.Restore(PythonStamp, [], write: false, forceWhileGameRuns: false);
        Assert.Equal(OperationOutcome.DryRun, dryRun.Outcome);
        Assert.False(dryRun.Items[0].CurrentIdentical);
        Assert.Equal(Utf8("changed"), _rig.ReadCharacter());

        var restored = _rig.Backups.Restore(PythonStamp, [], write: true, forceWhileGameRuns: false);
        Assert.Equal(OperationOutcome.Written, restored.Outcome);
        Assert.Equal(original, _rig.ReadCharacter());
        Assert.Contains("pre-restore", _rig.BackupReasons());
    }

    [Fact]
    public void Restore_refuses_a_backup_copy_that_does_not_match_the_python_manifest()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        WritePythonLayoutBackup(PythonStamp, original);
        File.WriteAllBytes(_rig.BackupCopyPath(PythonStamp), Utf8("corrupt"));

        var result = _rig.Backups.Restore(PythonStamp, [], write: false, forceWhileGameRuns: false);

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.BackupFailed, result.Problem);
    }

    [Fact]
    public void List_and_restore_read_the_earlier_csharp_layout_with_camel_case_keys()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        const string folderName = "20240101T000000-manual";
        var folder = Path.Combine(_rig.BackupsRoot, folderName);
        var copyPath = Path.Combine(folder, ComboTestRig.DefaultPcid, GameDataLocator.CharacterFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        File.WriteAllBytes(copyPath, original);
        var hash = BackupService.Sha256Hex(original);
        var manifest = $$"""
            {
              "tool": "bdo-combo-setup",
              "toolVersion": "0.1.0",
              "createdUtc": "2024-01-01T00:00:00Z",
              "reason": "manual",
              "account": "{{ComboTestRig.Account}}",
              "files": [
                {
                  "pcid": "{{ComboTestRig.DefaultPcid}}",
                  "path": "{{ComboTestRig.DefaultPcid}}/gameVariable.xml",
                  "sha256": "{{hash}}",
                  "copySha256": "{{hash}}",
                  "size": {{original.Length}}
                }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(folder, BackupService.ManifestName), manifest);

        var summary = Assert.Single(_rig.Backups.List());
        Assert.True(summary.Readable);
        Assert.Equal("manual", summary.Reason);
        Assert.Equal("0.1.0", summary.ToolVersion);
        Assert.Equal(1, summary.Characters);

        _rig.WriteCharacter(Utf8("changed"));
        var restored = _rig.Backups.Restore(folderName, [], write: true, forceWhileGameRuns: false);

        Assert.Equal(OperationOutcome.Written, restored.Outcome);
        Assert.Equal(original, _rig.ReadCharacter());
    }

    [Fact]
    public void Restore_refuses_a_manifest_entry_whose_path_is_not_a_known_layout()
    {
        var original = Utf8(SyntheticSave.Text());
        _rig.WriteCharacter(original);
        WritePythonLayoutBackup(PythonStamp, original, relativePath: "../escape/gameVariable.xml");

        var result = _rig.Backups.Restore(PythonStamp, [], write: false, forceWhileGameRuns: false);

        Assert.Equal(OperationOutcome.Refused, result.Outcome);
        Assert.Equal(OperationProblem.BackupFailed, result.Problem);
        Assert.Contains("unexpected path", result.ProblemDetail);
        Assert.Equal(original, _rig.ReadCharacter());
    }
}
