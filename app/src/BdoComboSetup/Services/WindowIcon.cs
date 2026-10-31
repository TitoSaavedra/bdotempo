using System.Reflection;
using BdoComboSetup.Core.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BdoComboSetup.Services;

/// <summary>
/// Window and title bar icon. The icon files are embedded in the exe (EmbeddedResource in the csproj), and copied to
/// %LOCALAPPDATA%\BdoComboSetup\icon at run time, because AppWindow.SetIcon takes the path of an .ico file.
/// The copy is rewritten only when its bytes differ. The icon is cosmetic: a failure is logged and the window still opens.
/// </summary>
internal static class WindowIcon
{
    private const string IconResourceName = "BdoComboSetup.app-icon.ico";
    private const string PngResourceName = "BdoComboSetup.app-icon-256.png";
    private const string IconFileName = "app-icon.ico";
    private const string PngFileName = "app-icon-256.png";

    /// <summary>Sets the taskbar and Alt+Tab icon (AppWindow) and the icon shown in the custom title bar.</summary>
    public static void Apply(AppWindow appWindow, TitleBar titleBar, AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(appWindow);
        ArgumentNullException.ThrowIfNull(titleBar);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BdoComboSetup",
                "icon");
            Directory.CreateDirectory(folder);

            var iconPath = ExtractResource(IconResourceName, Path.Combine(folder, IconFileName));
            var pngPath = ExtractResource(PngResourceName, Path.Combine(folder, PngFileName));

            appWindow.SetIcon(iconPath);

            // Images load asynchronously: a bad file surfaces as ImageFailed, not as an exception, so it is logged here.
            var image = new BitmapImage(new Uri(pngPath));
            image.ImageFailed += (_, args) => logger.Error(
                "window.icon_image_failed",
                "Title bar icon image failed to load",
                new InvalidOperationException(args.ErrorMessage),
                new Dictionary<string, object?> { ["icon_path"] = pngPath });
            titleBar.IconSource = new ImageIconSource { ImageSource = image };

            logger.Info("window.icon_applied", "Window and title bar icon applied", new Dictionary<string, object?>
            {
                ["icon_path"] = iconPath,
            });
        }
        catch (Exception exception)
        {
            logger.Error("window.icon_failed", "Window icon could not be applied; the window keeps the default icon", exception);
        }
    }

    private static string ExtractResource(string resourceName, string targetPath)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded icon resource '{resourceName}' is missing from the exe.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();

        if (!File.Exists(targetPath) || !File.ReadAllBytes(targetPath).AsSpan().SequenceEqual(bytes))
        {
            File.WriteAllBytes(targetPath, bytes);
        }

        return targetPath;
    }
}
