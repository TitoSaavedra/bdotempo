namespace BdoComboSetup.Core.Updates;

/// <summary>
/// Tells the portable zip from the installed copy. Only the portable zip contains <c>portable.flag</c> next to the exe, so the
/// installer never writes it and a portable copy never runs the installer.
/// </summary>
public static class InstallKindDetector
{
    public static InstallKind Detect(string appDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        return File.Exists(Path.Combine(appDirectory, UpdateRepository.PortableMarkerFileName))
            ? InstallKind.Portable
            : InstallKind.Installed;
    }
}
