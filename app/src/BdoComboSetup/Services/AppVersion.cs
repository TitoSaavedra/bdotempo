using System.Reflection;
using BdoComboSetup.Core.Updates;

namespace BdoComboSetup.Services;

/// <summary>
/// The version of the running app, from the informational version that the build writes from <c>Version</c> in
/// app/Directory.Build.props (the single source of truth). The build metadata (for example a commit id) is dropped by the parser.
/// </summary>
public static class AppVersion
{
    public static SemanticVersion Current { get; } = Read();

    private static SemanticVersion Read()
    {
        var text = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!SemanticVersion.TryParse(text, out var version) || version is null)
        {
            throw new InvalidOperationException($"The app version '{text}' is not SemVer; check app/Directory.Build.props.");
        }

        return version;
    }
}
