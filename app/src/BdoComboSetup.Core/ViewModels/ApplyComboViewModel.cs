using System.Collections.ObjectModel;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BdoComboSetup.Core.ViewModels;

public enum StatusSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>State of the "Apply combo" page: character, class, mode, the three apply options and the summary.</summary>
public sealed partial class ApplyComboViewModel : ObservableObject
{
    private readonly ComboDefinition _combo;
    private readonly IComboApplier _applier;
    private readonly ILocalizer _localizer;
    private readonly AppLogger _logger;
    private readonly string _sourceHost;

    public ApplyComboViewModel(
        IComboDataSource data,
        ComboDefinition combo,
        IComboApplier applier,
        ILocalizer localizer,
        AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(combo);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _combo = combo;
        _applier = applier;
        _localizer = localizer;
        _logger = logger;
        _sourceHost = new Uri(combo.SourceUrl).Host;

        Characters = new ObservableCollection<CharacterOption>(data.GetCharacters());
        ClassNames = new ObservableCollection<string>(data.GetClassNames());
        SelectedCharacter = Characters.FirstOrDefault();
        SelectedClassName = ClassNames.FirstOrDefault();
    }

    public ObservableCollection<CharacterOption> Characters { get; }

    public ObservableCollection<string> ClassNames { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DryRunCommand))]
    private CharacterOption? _selectedCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryTitle))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DryRunCommand))]
    private string? _selectedClassName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryTitle), nameof(ModeLabel), nameof(IsSuccession), nameof(IsAwakening))]
    private ComboMode _mode = ComboMode.Awakening;

    /// <summary>Two-way bridge for the "Succession" radio button.</summary>
    public bool IsSuccession
    {
        get => Mode == ComboMode.Succession;
        set
        {
            if (value)
            {
                Mode = ComboMode.Succession;
            }
        }
    }

    /// <summary>Two-way bridge for the "Awakening" radio button.</summary>
    public bool IsAwakening
    {
        get => Mode == ComboMode.Awakening;
        set
        {
            if (value)
            {
                Mode = ComboMode.Awakening;
            }
        }
    }

    [ObservableProperty]
    private bool _includeCooldownBar = true;

    [ObservableProperty]
    private bool _includeMemos = true;

    [ObservableProperty]
    private bool _includeQuickSlots = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Information;

    [ObservableProperty]
    private bool _isStatusOpen;

    public string ModeLabel => _localizer.Get(Mode == ComboMode.Succession ? "Mode_Succession" : "Mode_Awakening");

    public string SummaryTitle => _localizer.Format("Apply_SummaryTitle", SelectedClassName ?? string.Empty, ModeLabel);

    public string SummaryDescription => _combo.Description;

    public string SummarySource => _localizer.Format("Apply_SourceFormat", _sourceHost);

    public int ComboSkillCount => _combo.Slots.Count(slot => slot.Group == SlotGroup.Combo);

    public int PriorityBuffCount => _combo.Slots.Count(slot => slot.Group == SlotGroup.PriorityBuff);

    public int QuickSlotCount => _combo.Slots.Count(slot => slot.QuickPosition > 0);

    public string ComboSkillsText => _localizer.Format("Apply_ComboSkillsCount", ComboSkillCount);

    public string PriorityBuffsText => _localizer.Format("Apply_PriorityBuffsCount", PriorityBuffCount);

    public string QuickSlotsText => _localizer.Format("Apply_QuickSlotsCount", QuickSlotCount);

    public bool CanRun => SelectedCharacter is not null && !string.IsNullOrEmpty(SelectedClassName);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Apply() => RunAsync(dryRun: false);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DryRun() => RunAsync(dryRun: true);

    private async Task RunAsync(bool dryRun)
    {
        if (SelectedCharacter is null || string.IsNullOrEmpty(SelectedClassName))
        {
            return;
        }

        var request = new ComboApplyRequest(
            SelectedClassName,
            Mode,
            SelectedCharacter,
            IncludeCooldownBar,
            IncludeMemos,
            IncludeQuickSlots,
            dryRun);

        _logger.Info("apply.requested", "User requested a combo apply", new Dictionary<string, object?>
        {
            ["dry_run"] = dryRun,
            ["mode"] = Mode.ToString(),
        });

        try
        {
            var result = await _applier.ApplyAsync(request);
            switch (result.Status)
            {
                case ComboApplyStatus.NotConnected:
                    ShowStatus(StatusSeverity.Warning, "Apply_Status_NotConnected");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unhandled apply status.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("apply.failed", "Combo apply request failed", ex, new Dictionary<string, object?>
            {
                ["dry_run"] = dryRun,
            });
            ShowStatus(StatusSeverity.Error, "Apply_Status_Failed");
        }
    }

    private void ShowStatus(StatusSeverity severity, string messageKey)
    {
        StatusMessage = _localizer.Get(messageKey);
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
