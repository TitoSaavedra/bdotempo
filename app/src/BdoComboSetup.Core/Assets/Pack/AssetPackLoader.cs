using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.Assets.Pack;

/// <summary>The result of opening the pack at run time. When <see cref="ErrorCode"/> is set, <see cref="Pack"/> is empty.</summary>
/// <param name="Pack">The pack, or an empty pack when it could not be opened.</param>
/// <param name="ErrorCode">Null when the pack is open; otherwise an <see cref="AssetPackErrorCodes"/> value (missing, read_failed, ...).</param>
/// <param name="Path">The file that was opened.</param>
public sealed record AssetPackLoadResult(IAssetPack Pack, string? ErrorCode, string Path);

/// <summary>
/// Opens the complete asset pack the app needs: <c>assets.bdoa</c> in the folder next to the exe. The app does not
/// fall back to anything else: a missing or invalid file is logged and reported, and the UI shows an error page.
/// </summary>
public static class AssetPackLoader
{
    /// <summary>The file name of the complete pack, next to the exe (the installer or the portable zip puts it there).</summary>
    public const string FileName = "assets.bdoa";

    /// <summary>The path of the pack next to the running exe.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static AssetPackLoadResult LoadFile(string path, AppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        using var scope = logger.BeginOperation("asset.pack.load", new Dictionary<string, object?>
        {
            ["path"] = path,
        });

        if (!File.Exists(path))
        {
            logger.Log(LogLevel.Error, "asset.pack.missing", "assets.bdoa is not next to the app; icons and the catalog cannot be shown", new Dictionary<string, object?>
            {
                ["path"] = path,
                ["code"] = AssetPackErrorCodes.Missing,
            });
            return Failed(AssetPackErrorCodes.Missing, path);
        }

        try
        {
            return new AssetPackLoadResult(AssetPack.Open(File.OpenRead(path), logger), null, path);
        }
        catch (AssetPackException ex)
        {
            scope.Fail(ex);
            logger.Error("asset.pack.invalid", "assets.bdoa is damaged or does not match this app; icons and the catalog cannot be shown", ex, new Dictionary<string, object?>
            {
                ["path"] = path,
                ["code"] = ex.Code,
            });
            return Failed(ex.Code, path);
        }
        catch (IOException ex)
        {
            scope.Fail(ex);
            logger.Error("asset.pack.unreadable", "assets.bdoa could not be read", ex, new Dictionary<string, object?>
            {
                ["path"] = path,
                ["code"] = AssetPackErrorCodes.ReadFailed,
            });
            return Failed(AssetPackErrorCodes.ReadFailed, path);
        }
        catch (UnauthorizedAccessException ex)
        {
            scope.Fail(ex);
            logger.Error("asset.pack.unreadable", "assets.bdoa could not be read", ex, new Dictionary<string, object?>
            {
                ["path"] = path,
                ["code"] = AssetPackErrorCodes.ReadFailed,
            });
            return Failed(AssetPackErrorCodes.ReadFailed, path);
        }
    }

    private static AssetPackLoadResult Failed(string code, string path) =>
        new(InMemoryAssetPack.Empty, code, path);
}
