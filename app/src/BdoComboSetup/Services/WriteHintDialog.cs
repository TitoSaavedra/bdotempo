using BdoComboSetup.Core.Confirmation;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using Microsoft.UI.Xaml.Controls;

namespace BdoComboSetup.Services;

/// <summary>
/// The reminder before a write to a character: a ContentDialog with Continue, Cancel and a "Don't show again" check box.
/// It is shown over the main window. Any failure to show it is logged and counts as Cancel, so nothing is written.
/// </summary>
public sealed class WriteHintDialog(AppLogger logger, ILocalizer localizer) : IConfirmationPrompt
{
    public async Task<WriteHintAnswer> ShowWriteHintAsync()
    {
        var window = App.MainWindow;
        var root = window?.Content?.XamlRoot;
        if (root is null)
        {
            logger.Warn("write_hint.no_window", "The main window is not ready; the write is not confirmed", new Dictionary<string, object?>
            {
                ["window_set"] = window is not null,
                ["content_set"] = window?.Content is not null,
            });
            return new WriteHintAnswer(Continue: false, DontShowAgain: false);
        }

        var dontShowAgain = new CheckBox { Content = localizer.Get("Apply.WriteHint.DontShowAgain") };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = localizer.Get("Apply.WriteHint.Title"),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = localizer.Get("Apply.WriteHint.Message"), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                    dontShowAgain,
                },
            },
            PrimaryButtonText = localizer.Get("Apply.WriteHint.Continue"),
            SecondaryButtonText = localizer.Get("Apply.WriteHint.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        try
        {
            var result = await dialog.ShowAsync();
            var continued = result == ContentDialogResult.Primary;
            logger.Debug("write_hint.shown", "Write reminder closed", new Dictionary<string, object?>
            {
                ["continue"] = continued,
            });
            return new WriteHintAnswer(continued, continued && dontShowAgain.IsChecked == true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            logger.Error("write_hint.failed", "The write reminder could not be shown; nothing is written", ex);
            return new WriteHintAnswer(Continue: false, DontShowAgain: false);
        }
    }
}
