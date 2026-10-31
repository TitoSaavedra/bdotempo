using BdoComboSetup.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace BdoComboSetup.Views;

public sealed partial class ApplyComboPage : Microsoft.UI.Xaml.Controls.Page
{
    public ApplyComboPage()
    {
        ViewModel = App.Services.ApplyCombo;
        InitializeComponent();
    }

    public ApplyComboViewModel ViewModel { get; }

    /// <summary>Maps the view model's severity to the InfoBar's enum (the view models do not reference WinUI).</summary>
    public InfoBarSeverity ToInfoBarSeverity(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Warning => InfoBarSeverity.Warning,
        StatusSeverity.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };
}
