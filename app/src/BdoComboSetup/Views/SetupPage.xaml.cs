using BdoComboSetup.Core.Search;
using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;

namespace BdoComboSetup.Views;

/// <summary>
/// The initial setup: the Black Desert data folder (found on this PC, or picked with the folder picker) and the account.
/// It is shown on the first start and can be reopened from Settings. The page only forwards clicks; the rules are in
/// <see cref="SetupViewModel"/>.
/// </summary>
public sealed partial class SetupPage : Page
{
    private bool _firstRun;

    public SetupPage()
    {
        ViewModel = App.Services.Setup;
        InitializeComponent();
        AccountPicker.SetFields(item => SearchFields.ForAccount(((AccountChoice)item).Account));
    }

    public SetupViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _firstRun = e.Parameter is true;
        CancelButton.Visibility = _firstRun ? Visibility.Collapsed : Visibility.Visible;
        LaterButton.Visibility = _firstRun ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.Refresh();
    }

    /// <summary>Opens the folder picker: the user selects the folder, never types the path.</summary>
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
                App.Services.Logger.Debug("setup.browse_cancelled", "Folder picker closed without a choice");
                return;
            }

            ViewModel.ChooseFolder(result.Path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            App.Services.Logger.Error("setup.browse_failed", "The folder picker failed", ex);
        }
    }

    /// <summary>Saves the folder and the account and opens the app (Settings when the setup was reopened from there).</summary>
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Finish() && App.MainWindow is MainWindow window)
        {
            window.LeaveSetup(goToSettings: !_firstRun);
        }
    }

    /// <summary>First start only: opens the app without configuring anything; Settings can do it later.</summary>
    private void Later_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Skip();
        if (App.MainWindow is MainWindow window)
        {
            window.LeaveSetup(goToSettings: false);
        }
    }

    /// <summary>Reopened setup only: goes back to Settings without changing anything.</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow window)
        {
            window.LeaveSetup(goToSettings: true);
        }
    }

    private void AccountPicker_Chosen(object? sender, object item)
    {
        if (item is AccountChoice choice)
        {
            ViewModel.SelectedAccount = choice;
        }
    }
}
