using System.Globalization;
using BdoComboSetup.Core.Combo;
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
