using BdoComboSetup.Core.Logging;
using BdoComboSetup.Services;
using Microsoft.UI.Xaml;

namespace BdoComboSetup;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>Composition root: created once in OnLaunched, shared by the window and the pages.</summary>
    public static AppServices Services { get; private set; } = null!;

    /// <summary>The main window, used to attach the file pickers to it.</summary>
    public static Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = StartupOptions.Parse(Environment.GetCommandLineArgs());
        Services = AppServices.Create(options);
        Services.Logger.Info("app.started", "Application launched", new Dictionary<string, object?>
        {
            ["version"] = typeof(App).Assembly.GetName().Version?.ToString(),
            ["startup_page"] = options.Page,
            ["startup_layout"] = options.Layout?.ToString(),
        });

        _window = new MainWindow();
        MainWindow = _window;
        _window.Closed += (_, _) =>
        {
            Services.Logger.Info("app.stopped", "Main window closed");
            Services.Dispose();
        };
        _window.Activate();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Services?.Logger.Error("app.unhandled_exception", "Unhandled UI exception", e.Exception);
    }
}
