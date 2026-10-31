using System.Text.Json;
using BdoComboSetup.Core.Assets.Pack;
using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Cli;

/// <summary>
/// The headless command <c>BdoComboSetup.exe --assemble-assets &lt;partsDir&gt; &lt;outFile&gt;</c>. The installer runs it after
/// copying the parts; the portable build can run it too. It opens no window and touches no WinRT API.
/// The exit code is the contract (see <see cref="ExitCodes"/>). Stdout gets one JSON line on success, stderr one on failure;
/// the structured log of the run is in %LOCALAPPDATA%\BdoComboSetup\logs.
/// </summary>
public static class AssetAssemblyCommand
{
    public const string Switch = "--assemble-assets";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>The exit codes of the command. 0 is the only success.</summary>
    public static class ExitCodes
    {
        public const int Ok = 0;
        public const int Unexpected = 1;
        public const int Usage = 2;
        public const int MissingParts = 3;
        public const int WrongParts = 4;
        public const int Damaged = 5;
        public const int WriteFailed = 6;
    }

    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != 3 || !string.Equals(args[0], Switch, StringComparison.Ordinal))
        {
            WriteLine(Console.Error, new Dictionary<string, object?>
            {
                ["status"] = "error",
                ["code"] = "usage",
                ["usage"] = $"BdoComboSetup.exe {Switch} <partsDir> <outFile>",
            });
            return ExitCodes.Usage;
        }

        using var logger = AppLogger.CreateDefault("assets.assemble");
        try
        {
            var result = AssetPackAssembler.Assemble(args[1], args[2], logger);
            WriteLine(Console.Out, new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["output"] = result.Output,
                ["parts"] = result.Parts,
                ["entries"] = result.Entries,
                ["bytes"] = result.Bytes,
            });
            return ExitCodes.Ok;
        }
        catch (AssetPackException ex)
        {
            WriteLine(Console.Error, new Dictionary<string, object?>
            {
                ["status"] = "error",
                ["code"] = ex.Code,
                ["message"] = ex.Message,
            });
            return ExitCodeFor(ex.Code);
        }
        catch (Exception ex)
        {
            logger.Error("assets.assemble.unexpected", "The assembly stopped with an unexpected error", ex);
            WriteLine(Console.Error, new Dictionary<string, object?>
            {
                ["status"] = "error",
                ["code"] = "unexpected",
                ["message"] = ex.Message,
            });
            return ExitCodes.Unexpected;
        }
    }

    /// <summary>Maps an error code of the pack to the exit code of the command.</summary>
    public static int ExitCodeFor(string code) => code switch
    {
        AssetPackErrorCodes.PartsDirMissing or AssetPackErrorCodes.PartMissing => ExitCodes.MissingParts,
        AssetPackErrorCodes.NotAPart or AssetPackErrorCodes.PartMismatch or AssetPackErrorCodes.DuplicateEntry => ExitCodes.WrongParts,
        AssetPackErrorCodes.WriteFailed => ExitCodes.WriteFailed,
        _ => ExitCodes.Damaged,
    };

    private static void WriteLine(TextWriter writer, IReadOnlyDictionary<string, object?> fields) =>
        writer.WriteLine(JsonSerializer.Serialize(fields, JsonOptions));
}
