using System.Text;
using System.Text.Json;
using BdoComboSetup.Core.Backup;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Files;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.GameData;

namespace BdoComboSetup.Tests;

/// <summary>A process guard whose answer the test sets. Counts the checks.</summary>
internal sealed class FakeGuard : IGameProcessGuard
{
    public bool Running { get; set; }

    public int Calls { get; private set; }

    public bool IsGameRunning()
    {
        Calls++;
        return Running;
    }
}

/// <summary>Wraps a real file store and injects faults: failed writes, corrupted copies and failed or corrupted replaces.</summary>
internal sealed class FaultyFileStore(IFileStore inner) : IFileStore
{
    /// <summary>Paths whose WriteAllBytes throws.</summary>
    public Func<string, bool>? FailWriteWhen { get; set; }

    /// <summary>Paths whose WriteAllBytes stores a corrupted copy (one extra byte).</summary>
    public Func<string, bool>? CorruptWriteWhen { get; set; }

    /// <summary>Called for every AtomicReplace with the 1-based call number; returns the bytes to store.</summary>
    public Func<string, int, byte[], byte[]>? CorruptAtomicReplace { get; set; }

    /// <summary>Called for every AtomicReplace; throws when it returns true.</summary>
    public Func<int, bool>? FailAtomicReplaceWhen { get; set; }

    public int AtomicReplaceCalls { get; private set; }

    public bool Exists(string path) => inner.Exists(path);

    public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] data)
    {
        if (FailWriteWhen?.Invoke(path) == true)
        {
            throw new IOException("injected write failure");
        }

        var payload = CorruptWriteWhen?.Invoke(path) == true ? data.Append((byte)'X').ToArray() : data;
        inner.WriteAllBytes(path, payload);
    }

    public void AtomicReplace(string path, byte[] data)
    {
        AtomicReplaceCalls++;
        if (FailAtomicReplaceWhen?.Invoke(AtomicReplaceCalls) == true)
        {
            throw new IOException("injected replace failure");
        }

        var payload = CorruptAtomicReplace?.Invoke(path, AtomicReplaceCalls, data) ?? data;
        inner.AtomicReplace(path, payload);
    }

    public DateTime GetLastWriteUtc(string path) => inner.GetLastWriteUtc(path);
}

/// <summary>
/// A temporary Black Desert data folder with one account, one character and a backups folder, plus the services wired
/// the way the app wires them. Synthetic data only. Disposing removes the folder.
/// </summary>
internal sealed class ComboTestRig : IDisposable
{
    public const string Account = "900001";
    public const string DefaultPcid = "10000000000000001";

    public ComboTestRig(string pcid = DefaultPcid)
    {
        Log = new TempLogger();
        Root = Path.Combine(Path.GetTempPath(), "BdoComboSetup.Tests", "rig-" + Guid.NewGuid().ToString("N"));
        DataRoot = Path.Combine(Root, "Black Desert");
        Directory.CreateDirectory(DataRoot);
        Pcid = pcid;
        SkillsPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "parity", "class_skills.json");
        Guard = new FakeGuard();
        Files = new FaultyFileStore(new LocalFileStore(Log.Logger));
        Locator = new GameDataLocator(DataRoot, Account, Log.Logger);
        Backups = new BackupService(BackupsRoot, Locator, Files, Guard, Log.Logger);
        Applier = new ComboApplier(Locator, Backups, Guard, Files, LoadTable, Log.Logger);
    }

    public TempLogger Log { get; }

    public string Root { get; }

    public string DataRoot { get; }

    public string BackupsRoot => Path.Combine(DataRoot, "_backups");

    public string SkillsPath { get; set; }

    public string Pcid { get; }

    public FakeGuard Guard { get; }

    public FaultyFileStore Files { get; }

    public GameDataLocator Locator { get; }

    public BackupService Backups { get; }

    public ComboApplier Applier { get; }

    public string CharacterPath => Path.Combine(DataRoot, "UserCache", Account, "11", Pcid, GameDataLocator.CharacterFileName);

    public void WriteCharacter(byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CharacterPath)!);
        File.WriteAllBytes(CharacterPath, bytes);
    }

    public void WriteCharacter(string text) => WriteCharacter(new UTF8Encoding(false).GetBytes(text));

    public byte[] ReadCharacter() => File.ReadAllBytes(CharacterPath);

    public string ReadCharacterText() => Encoding.UTF8.GetString(ReadCharacter());

    /// <summary>Makes the character file look as if it was last written an hour ago (outside the write guard window).</summary>
    public void AgeCharacterFile() => File.SetLastWriteTimeUtc(CharacterPath, DateTime.UtcNow.AddHours(-1));

    public IReadOnlyList<string> BackupFolders() =>
        Directory.Exists(BackupsRoot)
            ? Directory.EnumerateDirectories(BackupsRoot).Select(p => Path.GetFileName(p)).OrderBy(n => n, StringComparer.Ordinal).ToList()
            : [];

    /// <summary>Where the copy of this rig's character lies inside a backup folder (Python layout: account\pcid\file).</summary>
    public string BackupCopyPath(string folderName) =>
        Path.Combine(BackupsRoot, folderName, Account, Pcid, GameDataLocator.CharacterFileName);

    public byte[] ReadBackupCopy(string folderName) => File.ReadAllBytes(BackupCopyPath(folderName));

    /// <summary>The "reason" of every backup folder's manifest, in folder order.</summary>
    public IReadOnlyList<string> BackupReasons() =>
        BackupFolders()
            .Select(folder =>
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(BackupsRoot, folder, BackupService.ManifestName)));
                return manifest.RootElement.GetProperty("reason").GetString()!;
            })
            .ToList();

    public SkillTable LoadTable() => SkillTable.Load(SkillsPath, Log.Logger);

    public void Dispose()
    {
        Log.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

/// <summary>Shared tamer cooldown slots: the same values as the Python tests' tamer combo (XML slots 0..5).</summary>
internal static class TamerSlots
{
    public static IReadOnlyList<CooldownSlot> Six() =>
    [
        new CooldownSlot(0, 1937, "Flujo: Intimidación", "R 1"),
        new CooldownSlot(1, 1921, "Aguja giratoria completa", "SH + DER"),
        new CooldownSlot(2, 1912, "Garuda", "F (mantener)"),
        new CooldownSlot(3, 1926, "Baile de la Bestia Legendaria", "A + DER"),
        new CooldownSlot(4, 17, "Onda de Tumbo", "SH + Z"),
        new CooldownSlot(5, 1306, "Guardiacárcel Negro", "F"),
    ];

    public static ComboApplyRequest Request(string pcid, IReadOnlyList<CooldownSlot>? slots = null, IReadOnlyList<QuickSlotAssignment>? quick = null) =>
        new(pcid, 16, slots ?? Six(), quick ?? []);
}
