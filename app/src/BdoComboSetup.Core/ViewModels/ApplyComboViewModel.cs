using System.Collections.ObjectModel;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.GameData;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;
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

        var request = BuildRequest(SelectedCharacter.Pcid);
        _logger.Info("apply.requested", "User requested a combo apply", new Dictionary<string, object?>
        {
            ["dry_run"] = dryRun,
            ["mode"] = Mode.ToString(),
            ["cooldown_slots"] = request.CooldownSlots.Count,
            ["quick_slots"] = request.QuickSlots.Count,
        });

        try
        {
            var result = dryRun
                ? await _applier.DryRunAsync(request)
                : await _applier.ApplyAsync(request);
            ShowResult(result);
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

    /// <summary>
    /// Cooldown slots are the combo's slots that have a skill (the UI numbers them 1..20, the file 0..19; empty slots
    /// are written as 0). Quick slots are the quick bar positions with a skill. A position without a skill is left as it is.
    /// </summary>
    private ComboApplyRequest BuildRequest(string pcid)
    {
        var cooldown = _combo.Slots
            .Where(slot => slot.SkillNo is > 0)
            .Select(slot => new CooldownSlot(slot.Index - 1, slot.SkillNo!.Value, slot.SkillName, slot.Memo))
            .ToList();

        var quick = IncludeQuickSlots
            ? _combo.Slots
                .Where(slot => slot.QuickPosition > 0 && slot.SkillNo is > 0)
                .Select(slot => new QuickSlotAssignment(slot.QuickPosition, slot.SkillNo))
                .ToList()
            : new List<QuickSlotAssignment>();

        return new ComboApplyRequest(
            pcid,
            _combo.ClassId,
            cooldown,
            quick,
            IncludeCooldownBar,
            IncludeMemos,
            IncludeQuickSlots);
    }

    /// <summary>Shows the outcome, the diff counts and the backup path in the status bar.</summary>
    private void ShowResult(ComboApplyResult result)
    {
        var diff = result.Diff;
        var summary = diff is null
            ? string.Empty
            : _localizer.Format(
                "Apply_Result_Summary",
                diff.Slots.Count,
                diff.Memos.Count(m => m.Status == MemoStatus.Added),
                diff.Memos.Count(m => m.Status == MemoStatus.Changed),
                diff.QuickSlots.Count);

        var severity = StatusSeverity.Information;
        var lines = new List<string>();
        switch (result.Outcome)
        {
            case OperationOutcome.DryRun:
                lines.Add(diff is { Changed: true }
                    ? _localizer.Format("Apply_Result_DryRun", summary)
                    : _localizer.Get("Apply_Result_DryRunUnchanged"));
                break;
            case OperationOutcome.Written:
                lines.Add(_localizer.Format("Apply_Result_Written", summary));
                break;
            case OperationOutcome.Unchanged:
                lines.Add(_localizer.Get("Apply_Result_Unchanged"));
                break;
            case OperationOutcome.Refused:
                severity = StatusSeverity.Warning;
                lines.Add(_localizer.Format("Apply_Result_Refused", ProblemText(result)));
                break;
            case OperationOutcome.RolledBack:
                severity = StatusSeverity.Error;
                lines.Add(_localizer.Format("Apply_Result_RolledBack", ProblemText(result)));
                break;
            default:
                severity = StatusSeverity.Error;
                lines.Add(_localizer.Format("Apply_Result_Failed", ProblemText(result)));
                break;
        }

        if (result.BackupPath is not null)
        {
            lines.Add(_localizer.Format("Apply_Result_Backup", result.BackupPath));
        }

        if (result.Warnings.Count > 0)
        {
            lines.Add(_localizer.Format("Apply_Result_Warnings", result.Warnings.Count));
        }

        StatusMessage = string.Join(Environment.NewLine, lines);
        StatusSeverity = severity;
        IsStatusOpen = true;
    }

    private string ProblemText(ComboApplyResult result)
    {
        var title = _localizer.Get("Apply_Problem_" + result.Problem);
        return string.IsNullOrEmpty(result.ProblemDetail)
            ? title
            : _localizer.Format("Apply_ProblemDetail", title, result.ProblemDetail);
    }

    private void ShowStatus(StatusSeverity severity, string messageKey)
    {
        StatusMessage = _localizer.Get(messageKey);
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
