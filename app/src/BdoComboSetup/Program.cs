using BdoComboSetup.Cli;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BdoComboSetup;

/// <summary>
/// Custom entry point. In a single-file exe the Windows App SDK loads its runtime DLLs through
/// MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY, which must point at the extraction folder before the first WinRT call.
/// </summary>
public static class Program
{
    private const string RuntimeBaseDirectoryVariable = "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY";

    [STAThread]
    private static void Main(string[] args)
    {
        // Headless mode: assemble the asset parts into assets.bdoa and exit. No window, no WinRT.
        if (args.Length > 0 && string.Equals(args[0], AssetAssemblyCommand.Switch, StringComparison.Ordinal))
        {
            Environment.Exit(AssetAssemblyCommand.Run(args));
            return;
        }

        Environment.SetEnvironmentVariable(RuntimeBaseDirectoryVariable, AppContext.BaseDirectory);
        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = callbackParams;
            _ = new App();
        });
    }
}
