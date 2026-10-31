using System.Diagnostics;
using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;

namespace BdoComboSetup.Views;

/// <summary>
/// The "Settings" page. The page only forwards clicks and selections to <see cref="SettingsViewModel"/>, and opens the
/// folder picker, which needs the window.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.SettingsView;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>Maps the view model's severity to the InfoBar's enum (the view models do not reference WinUI).</summary>
    public InfoBarSeverity ToInfoBarSeverity(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Success => InfoBarSeverity.Success,
        StatusSeverity.Warning => InfoBarSeverity.Warning,
        StatusSeverity.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // The folders may have changed while another page was open.
        ViewModel.Refresh();
    }

    private void AccountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is AccountChoice choice && !ReferenceEquals(choice, ViewModel.SelectedAccount))
        {
            ViewModel.ChooseAccount(choice.Account);
        }
    }

    private void ShowWriteHint_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.ShowWriteHint)
        {
            ViewModel.ChooseShowWriteHint(toggle.IsOn);
        }
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is LanguageChoice choice && !ReferenceEquals(choice, ViewModel.SelectedLanguage))
        {
            ViewModel.ChooseLanguage(choice.Code);
        }
    }

    /// <summary>Opens the folder picker. A cancelled picker changes nothing; the chosen folder is validated by the view model.</summary>
    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var window = App.MainWindow;
        if (window is null)
        {
            return;
        }

        try
        {
            var picker = new FolderPicker(window.AppWindow.Id);
            var result = await picker.PickSingleFolderAsync();
            if (result is null)
            {
                App.Services.Logger.Debug("settings.browse_cancelled", "Folder picker closed without a choice");
                return;
            }

            ViewModel.ChooseDataRoot(result.Path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            App.Services.Logger.Error("settings.browse_failed", "The folder picker failed", ex);
            ViewModel.ReportPickerFailed();
        }
    }

    private void SaveFolder_Click(object sender, RoutedEventArgs e) => ViewModel.ChooseDataRoot(ViewModel.DataRootText);

    /// <summary>Starts a new copy of the app and closes this one, so a language or folder change applies to every page.</summary>
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        var path = Environment.ProcessPath;
        App.Services.Logger.Info("app.restart_requested", "Restart requested by the settings", new Dictionary<string, object?>
        {
            ["can_restart"] = path is not null,
        });
        if (path is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        Application.Current.Exit();
    }
}
