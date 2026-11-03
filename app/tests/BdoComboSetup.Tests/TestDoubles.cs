using System.Globalization;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Confirmation;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Tests;

/// <summary>Returns the key itself (plus the arguments), so tests can assert on keys instead of copy.</summary>
internal sealed class KeyLocalizer : ILocalizer
{
    public string Get(string key) => key;

    public string Format(string key, params object[] args) =>
        args.Length == 0
            ? key
            : $"{key}({string.Join(",", args.Select(arg => Convert.ToString(arg, CultureInfo.InvariantCulture)))})";
}

/// <summary>An AppLogger that writes into a temporary folder which is deleted on dispose.</summary>
internal sealed class TempLogger : IDisposable
{
    public TempLogger()
    {
        Directory = Path.Combine(Path.GetTempPath(), "BdoComboSetup.Tests", Guid.NewGuid().ToString("N"));
        Logger = new AppLogger(Directory, "test.session");
    }

    public string Directory { get; }

    public AppLogger Logger { get; }

    public void Dispose()
    {
        Logger.Dispose();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

/// <summary>Builds the tamer combo the same way the mock data source does, with a fresh copy per test.</summary>
internal static class TestCombos
{
    public static ComboDefinition Tamer() => new MockComboDataSource().GetCombo("Tamer", ComboMode.Awakening);
}

/// <summary>A character source with fixed characters (the same three the old mock had). Records the saved overrides.</summary>
internal sealed class FakeCharacterSource : ICharacterSource
{
    /// <summary>The account the fake reports as active. Null means no account can be found.</summary>
    public string? AccountName { get; set; } = "330";

    public List<CharacterEntry> Entries { get; } =
    [
        new CharacterEntry("10000000000000011", 0, "Demo Tamer", NameSource.Label, 16, ClassSource.Inferred),
        new CharacterEntry("10000000000000001", 1, "Demo Warrior", NameSource.Label, 0, ClassSource.Inferred),
        new CharacterEntry("10000000000000002", 2, "Demo Shai", NameSource.Label, 17, ClassSource.Inferred),
    ];

    public List<(string Account, string Pcid, string? Name, int? ClassType)> Saved { get; } = [];

    public string? ActiveAccount() => AccountName;

    public IReadOnlyList<CharacterEntry> Load(string account) => account == AccountName ? Entries.ToList() : [];

    public void SaveOverride(string account, string pcid, string? name, int? classType) => Saved.Add((account, pcid, name, classType));

    public int ResetOverrides(string account) => 0;
}

/// <summary>The reminder before a write: answers with the given answer and counts how often it was shown.</summary>
internal sealed class FakeConfirmationPrompt(WriteHintAnswer answer) : IConfirmationPrompt
{
    public int Shown { get; private set; }

    public Task<WriteHintAnswer> ShowWriteHintAsync()
    {
        Shown++;
        return Task.FromResult(answer);
    }
}

/// <summary>The "don't show again" flag kept in memory.</summary>
internal sealed class FakeWriteHintSetting : IWriteHintSetting
{
    public bool SkipWriteHint { get; set; }

    public void SetSkipWriteHint(bool skip) => SkipWriteHint = skip;
}

/// <summary>Write confirmations for the view model tests.</summary>
internal static class TestConfirmation
{
    /// <summary>A confirmation whose reminder is answered with Continue, and not "don't show again".</summary>
    public static WriteConfirmation Accepting(AppLogger logger) =>
        new(new FakeWriteHintSetting(), new FakeConfirmationPrompt(new WriteHintAnswer(Continue: true, DontShowAgain: false)), logger);
}
