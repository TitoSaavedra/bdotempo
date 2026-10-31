using BdoComboSetup.Core.Logging;

namespace BdoComboSetup.Core.GameData;

/// <summary>
/// Reads a game file that the game may be writing at the same time. A locked file (IOException) is retried a few times
/// with a short pause; a missing file is not an error. Never writes.
/// </summary>
public sealed class GameFileReader
{
    public const int DefaultAttempts = 3;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(150);

    private readonly AppLogger _logger;
    private readonly Func<string, string> _readText;
    private readonly int _attempts;
    private readonly TimeSpan _retryDelay;

    /// <param name="readText">The read itself. Tests replace it to simulate a file the game is writing.</param>
    public GameFileReader(AppLogger logger, Func<string, string>? readText = null, int attempts = DefaultAttempts, TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (attempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), "at least one attempt is needed");
        }

        _logger = logger;
        _readText = readText ?? File.ReadAllText;
        _attempts = attempts;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    /// <summary>The text of the file, or null when it does not exist or is still locked after the last attempt (logged).</summary>
    public string? ReadText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fileName = Path.GetFileName(path);
        using var scope = _logger.BeginOperation("gamefile.read", new Dictionary<string, object?> { ["file"] = fileName });

        for (var attempt = 1; attempt <= _attempts; attempt++)
        {
            try
            {
                return _readText(path);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _logger.Debug("gamefile.missing", "Game file not present", new Dictionary<string, object?> { ["file"] = fileName });
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == _attempts)
                {
                    _logger.Warn("gamefile.unreadable", "Game file could not be read; it is skipped", new Dictionary<string, object?>
                    {
                        ["file"] = fileName,
                        ["attempts"] = attempt,
                        ["error"] = ex.GetType().Name,
                    });
                    scope.Fail(ex);
                    return null;
                }

                _logger.Debug("gamefile.retry", "Game file is busy; retrying", new Dictionary<string, object?>
                {
                    ["file"] = fileName,
                    ["attempt"] = attempt,
                });
                Thread.Sleep(_retryDelay);
            }
        }

        return null;
    }
}
