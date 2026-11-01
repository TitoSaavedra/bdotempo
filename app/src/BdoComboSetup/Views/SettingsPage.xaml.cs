using System.Diagnostics;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Search;
using BdoComboSetup.Core.Updates;
using BdoComboSetup.Core.ViewModels;
using BdoComboSetup.Views.Controls;
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
        AccountPicker.SetFields(item => SearchFields.ForAccount(((AccountChoice)item).Account));

        // The characters are the Apply page's roster: the same list, the same edit and reset rules.
        var apply = App.Services.ApplyCombo;
        CharacterList.ItemsSource = apply.Characters;
        CharacterList.ItemTemplate = (DataTemplate)Resources["CharacterRowTemplate"];
        CharacterList.SetFields(item => apply.CharacterSearchTexts((CharacterOption)item));
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>The OCR scan of the character selection screen (shared by every visit to the page).</summary>
    public CharacterScanViewModel Scan => App.Services.CharacterScan.ViewModel;

    /// <summary>The texts the class picker of a scan row searches by: both names of the class.</summary>
    public static Func<object, IEnumerable<string?>> ClassChoiceFields { get; } =
        item => SearchFields.ForNames(((ClassChoice)item).Names);

    /// <summary>The update controls of the page (a getter: the x:Bind bindings are evaluated inside InitializeComponent).</summary>
    public UpdateViewModel Updates => App.Services.Updates;

    /// <summary>Turns the check at startup on or off. The switch only forwards the change; the view model saves it.</summary>
    private void UpdatesStartup_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != Updates.CheckOnStartup)
        {
            Updates.SetCheckOnStartup(toggle.IsOn);
        }
    }

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
        _ = RefreshCharactersAsync();
    }

    /// <summary>The class of a character in the current language, or the "unknown class" text when it has none.</summary>
    public static string ClassNameOf(int? classType)
    {
        var app = App.Services;
        var choice = classType is int value ? app.ApplyCombo.ClassChoices.FirstOrDefault(item => item.ClassType == value) : null;
        return choice?.Name ?? app.Localizer.Get("Settings.CharacterClassUnknown");
    }

    /// <summary>Reloads the active account's characters (a change in the data folder or account applies here too).</summary>
    private async Task RefreshCharactersAsync()
    {
        try
        {
            await App.Services.ApplyCombo.RefreshCharactersAsync();
            CharactersMessageText.Text = App.Services.ApplyCombo.CharactersMessage;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Services.Logger.Error("settings.characters.refresh_failed", "The characters could not be refreshed", ex);
        }
    }

    /// <summary>Resets the name and class the user set for a character: the game files decide them again.</summary>
    private async void ResetCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CharacterOption option })
        {
            await App.Services.ApplyCombo.ResetCharacterEditAsync(option);
        }
    }

    /// <summary>Starts the OCR scan of the selection screen (the game must be open on that screen).</summary>
    private async void ScanStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var started = await App.Services.CharacterScan.StartAsync();
            if (!started && Scan.IsMessageOpen)
            {
                await ShowScanDialogAsync(Scan.Message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.Services.Logger.Error("settings.scan_start_failed", "The character scan could not start", ex);
        }
    }

    /// <summary>
    /// The scan could not start (no game window, minimized, no account, no OCR language): says it in a dialog, because the
    /// small line under the buttons is easy to miss while the game is in front.
    /// </summary>
    private async Task ShowScanDialogAsync(string message)
    {
        var localizer = App.Services.Localizer;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = localizer.Get("Settings.ScanDialogTitle"),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = localizer.Get("Settings.ScanDialogClose"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    /// <summary>The user is done scrolling: stop reading and build the review table.</summary>
    private void ScanFinish_Click(object sender, RoutedEventArgs e) => App.Services.CharacterScan.Finish();

    /// <summary>Stops the scan and drops what it read.</summary>
    private void ScanCancel_Click(object sender, RoutedEventArgs e) => App.Services.CharacterScan.Cancel();

    /// <summary>Saves the reviewed names and classes (characters.json only) and refreshes the list.</summary>
    private async void ScanSave_Click(object sender, RoutedEventArgs e)
    {
        if (Scan.Save() > 0)
        {
            await RefreshCharactersAsync();
        }
    }

    /// <summary>The class picked in one row of the scan review.</summary>
    private void ScanClassPicker_Chosen(object? sender, object item)
    {
        if (sender is SearchablePicker { Tag: CharacterScanRow row } && item is ClassChoice choice)
        {
            row.Class = choice;
        }
    }

    /// <summary>
    /// Opens the editor of one character: the name and the class, set once and remembered in the app (the game does not change).
    /// The name box starts from the name the character has now; a name left unchanged is not saved, so it keeps following the game.
    /// </summary>
    private void EditCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CharacterOption option } button)
        {
            return;
        }

        var apply = App.Services.ApplyCombo;
        var localizer = App.Services.Localizer;
        var entry = apply.EntryOf(option);
        var nameBox = new TextBox
        {
            Header = localizer.Get("Characters.EditNameHeader"),
            PlaceholderText = localizer.Get("Characters.EditNamePlaceholder"),
            Text = entry?.Name ?? option.Name,
            MaxLength = CharacterOverrideStore.MaxNameLength,
        };
        var automatic = new CheckBox
        {
            Content = localizer.Get("Characters.EditAutomaticClass"),
            IsChecked = entry?.ClassSource != ClassSource.Custom,
        };
        var classList = new Controls.SearchableList
        {
            ItemsSource = apply.ClassChoices,
            ItemTemplate = (DataTemplate)Resources["ClassChoiceTemplate"],
            IsEnabled = automatic.IsChecked != true,
        };
        classList.SetFields(item => SearchFields.ForNames(((ClassChoice)item).Names));
        var chosenClass = apply.ClassChoices.FirstOrDefault(choice => choice.ClassType == entry?.ClassType);
        var chosenText = new TextBlock
        {
            Text = chosenClass is null ? string.Empty : localizer.Format("Characters.EditClassChosen", chosenClass.Name),
            FontSize = 12,
        };
        classList.ItemChosen += (_, item) =>
        {
            chosenClass = (ClassChoice)item;
            chosenText.Text = localizer.Format("Characters.EditClassChosen", chosenClass.Name);
        };
        automatic.Checked += (_, _) => classList.IsEnabled = false;
        automatic.Unchecked += (_, _) => classList.IsEnabled = true;

        var save = new Button { Content = localizer.Get("Characters.EditSave") };
        var reset = new Button { Content = localizer.Get("Characters.EditReset") };
        var flyout = new Flyout();
        save.Click += async (_, _) =>
        {
            flyout.Hide();
            var typed = nameBox.Text.Trim();
            var keepAutomaticName = entry is not null && entry.NameSource != NameSource.Custom && typed == entry.Name;
            var name = keepAutomaticName ? null : typed;
            var classType = automatic.IsChecked == true ? null : chosenClass?.ClassType;
            await apply.SaveCharacterEditAsync(option, name, classType);
        };
        reset.Click += async (_, _) =>
        {
            flyout.Hide();
            await apply.ResetCharacterEditAsync(option);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(save);
        buttons.Children.Add(reset);
        var panel = new StackPanel { Spacing = 10, Width = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = localizer.Get("Characters.EditTitle"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(automatic);
        panel.Children.Add(chosenText);
        panel.Children.Add(classList);
        panel.Children.Add(buttons);
        flyout.Content = panel;
        flyout.ShowAt(button);
    }

    /// <summary>Opens the folder picker. A cancelled picker changes nothing; the chosen folder is validated by the view model.</summary>

    /// <summary>Saves the account picked in the searchable picker (the numbers of the accounts are what the search matches).</summary>
    private void AccountPicker_Chosen(object? sender, object item)
    {
        if (item is AccountChoice choice)
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

    /// <summary>Reopens the initial setup (folder and account) without restarting the app.</summary>
    private void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow window)
        {
            window.ShowSetup(firstRun: false);
        }
    }

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
