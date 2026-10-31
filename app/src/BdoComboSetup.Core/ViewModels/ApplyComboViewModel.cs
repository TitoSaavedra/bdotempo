using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BdoComboSetup.Core.ViewModels;

public enum StatusSeverity
{
    Information,
    Success,
    Warning,
    Error,
}

/// <summary>
/// The "Apply combo" page: the character, the class and spec (each with its guide from the catalog), the working copy
/// of the guide (<see cref="Editor"/>), and saving, restoring and applying it. Switching to another guide keeps the
/// working copy of the first one for the rest of the session, so unsaved edits are not lost.
/// </summary>
public sealed partial class ApplyComboViewModel : ObservableObject
{
    private readonly ICombosCatalog _catalog;
    private readonly IComboProfileStore _profiles;
    private readonly IComboApplier _applier;
    private readonly ILocalizer _localizer;
    private readonly CultureInfo _culture;
    private readonly AppLogger _logger;
    private readonly Dictionary<(int ClassType, string SpecId), ComboEditorViewModel> _editors = new();
    private ComboEditorViewModel? _observedEditor;
    private int _loadVersion;
    private bool _initialized;

    public ApplyComboViewModel(
        ICombosCatalog catalog,
        IComboDataSource data,
        IComboProfileStore profiles,
        IComboApplier applier,
        ILocalizer localizer,
        CultureInfo culture,
        AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _profiles = profiles;
        _applier = applier;
        _localizer = localizer;
        _culture = culture;
        _logger = logger;

        Characters = new ObservableCollection<CharacterOption>(data.GetCharacters());
        Classes = new ObservableCollection<ClassOptionViewModel>(
            catalog.Classes.Select(info => new ClassOptionViewModel(info, culture, option => SelectClassAsync(option))));
        Specs = new ObservableCollection<SpecOptionViewModel>();
        _selectedCharacter = Characters.FirstOrDefault();
    }

    public ObservableCollection<CharacterOption> Characters { get; }

    /// <summary>Every class that has a guide, in classType order.</summary>
    public ObservableCollection<ClassOptionViewModel> Classes { get; }

    /// <summary>The specs of the selected class. Each one has its own guide.</summary>
    public ObservableCollection<SpecOptionViewModel> Specs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharacterPcidText), nameof(CanRun))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DryRunCommand))]
    private CharacterOption? _selectedCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassName))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DryRunCommand))]
    private ClassOptionViewModel? _selectedClass;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpecName))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DryRunCommand))]
    private SpecOptionViewModel? _selectedSpec;

    /// <summary>The working copy of the selected guide. Null while loading or when the catalog has no guide for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor), nameof(IsGuideMissing), nameof(ProfileStateText), nameof(CanRun))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DryRunCommand), nameof(SaveProfileCommand), nameof(RestoreBaseCommand))]
    private ComboEditorViewModel? _editor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuideMissing))]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Information;

    [ObservableProperty]
    private bool _isStatusOpen;

    public bool HasEditor => Editor is not null;

    /// <summary>A spec is selected, its guide is not loading, and the catalog has no guide for it.</summary>
    public bool IsGuideMissing => !IsLoading && Editor is null && SelectedSpec is not null;

    public bool CanRun => SelectedCharacter is not null && Editor is not null;

    public string ClassName => SelectedClass?.Name ?? string.Empty;

    public string SpecName => SelectedSpec?.Label ?? string.Empty;

    public string CharacterPcidText => SelectedCharacter is null
        ? string.Empty
        : _localizer.Format("Apply_PcidFormat", SelectedCharacter.Pcid);

    /// <summary>Which copy is on screen: unsaved changes, the user's saved combo, or the bdoclasshub guide. Empty without a guide.</summary>
    public string ProfileStateText
    {
        get
        {
            if (Editor is null)
            {
                return string.Empty;
            }

            return _localizer.Get(Editor.HasUnsavedChanges
                ? "Apply_State_Unsaved"
                : Editor.HasSavedProfile ? "Apply_State_Saved" : "Apply_State_Base");
        }
    }

    /// <summary>
    /// Selects the startup class and spec, once. Without them the character's class is used, then the first class.
    /// The spec falls back to the first spec of the class.
    /// </summary>
    public async Task InitializeAsync(string? classSlug = null, string? specId = null)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        ClassOptionViewModel? option = null;
        if (!string.IsNullOrWhiteSpace(classSlug))
        {
            option = Classes.FirstOrDefault(item => string.Equals(item.Slug, classSlug, StringComparison.OrdinalIgnoreCase));
        }

        if (option is null && SelectedCharacter?.ClassType is int classType)
        {
            option = Classes.FirstOrDefault(item => item.ClassType == classType);
        }

        option ??= Classes.FirstOrDefault();
        if (option is null)
        {
            _logger.Warn("apply.catalog.empty", "The combo catalog has no classes with a guide");
            return;
        }

        await SelectClassAsync(option, specId);
    }

    /// <summary>Chooses a character. When the character's class is known, that class is selected too.</summary>
    [RelayCommand]
    private async Task SelectCharacter(CharacterOption? option)
    {
        if (option is null)
        {
            return;
        }

        SelectedCharacter = option;
        if (option.ClassType is int classType)
        {
            var target = Classes.FirstOrDefault(item => item.ClassType == classType);
            if (target is not null && !ReferenceEquals(target, SelectedClass))
            {
                await SelectClassAsync(target);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasEditor))]
    private async Task SaveProfile()
    {
        var editor = Editor;
        if (editor is null)
        {
            return;
        }

        var profile = editor.ToProfile();
        try
        {
            await _profiles.SaveAsync(profile);
            editor.MarkSaved();
            ShowStatus(StatusSeverity.Success, "Apply_Status_Saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("apply.profile.save_failed", "The combo could not be saved", ex, new Dictionary<string, object?>
            {
                ["class_type"] = editor.ClassType,
                ["spec"] = editor.SpecId,
            });
            ShowStatus(StatusSeverity.Error, "Apply_Status_SaveFailed");
        }
    }

    /// <summary>Deletes the saved combo of this guide and shows the bdoclasshub guide again.</summary>
    [RelayCommand(CanExecute = nameof(HasEditor))]
    private void RestoreBase()
    {
        var editor = Editor;
        if (editor is null)
        {
            return;
        }

        try
        {
            _profiles.Delete(editor.ClassType, editor.SpecId);
            editor.ResetToBase();
            ShowStatus(StatusSeverity.Success, "Apply_Status_Restored");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("apply.profile.restore_failed", "The saved combo could not be deleted", ex, new Dictionary<string, object?>
            {
                ["class_type"] = editor.ClassType,
                ["spec"] = editor.SpecId,
            });
            ShowStatus(StatusSeverity.Error, "Apply_Status_Failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Apply() => RunAsync(dryRun: false);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DryRun() => RunAsync(dryRun: true);

    private async Task SelectClassAsync(ClassOptionViewModel option, string? preferredSpecId = null)
    {
        foreach (var item in Classes)
        {
            item.IsSelected = ReferenceEquals(item, option);
        }

        SelectedClass = option;
        Specs.Clear();
        foreach (var info in option.Info.Specs)
        {
            Specs.Add(new SpecOptionViewModel(info, _culture, spec => SelectSpecAsync(spec)));
        }

        var spec = Specs.FirstOrDefault(item => string.Equals(item.Id, preferredSpecId, StringComparison.Ordinal)) ?? Specs.FirstOrDefault();
        SetSelectedSpec(spec);
        _logger.Info("apply.class.selected", "Class selected", new Dictionary<string, object?>
        {
            ["class_type"] = option.ClassType,
            ["specs"] = Specs.Count,
        });
        await LoadEditorAsync();
    }

    private async Task SelectSpecAsync(SpecOptionViewModel spec)
    {
        if (ReferenceEquals(spec, SelectedSpec))
        {
            return;
        }

        SetSelectedSpec(spec);
        _logger.Info("apply.spec.selected", "Spec selected", new Dictionary<string, object?>
        {
            ["class_type"] = SelectedClass?.ClassType,
            ["spec"] = spec.Id,
        });
        await LoadEditorAsync();
    }

    private void SetSelectedSpec(SpecOptionViewModel? spec)
    {
        foreach (var item in Specs)
        {
            item.IsSelected = ReferenceEquals(item, spec);
        }

        SelectedSpec = spec;
    }

    /// <summary>
    /// Loads the guide of the selected class and spec, and the user's profile on top of it, unless a working copy is already
    /// open for it. A response that arrives after a newer selection is dropped.
    /// </summary>
    private async Task LoadEditorAsync()
    {
        var version = ++_loadVersion;
        var classOption = SelectedClass;
        var spec = SelectedSpec;
        if (classOption is null || spec is null)
        {
            Editor = null;
            return;
        }

        IsLoading = true;
        try
        {
            var key = (classOption.ClassType, spec.Id);
            if (!_editors.TryGetValue(key, out var editor))
            {
                var guide = await _catalog.LoadGuideAsync(classOption.ClassType, spec.Id);
                if (version != _loadVersion)
                {
                    return;
                }

                if (guide is null)
                {
                    Editor = null;
                    return;
                }

                var profile = await _profiles.LoadAsync(classOption.ClassType, spec.Id);
                if (version != _loadVersion)
                {
                    return;
                }

                editor = ComboEditorViewModel.Create(classOption.ClassType, spec.Id, guide, profile, _localizer, _culture, _logger);
                _editors[key] = editor;
            }

            if (version == _loadVersion)
            {
                Editor = editor;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("apply.guide.load_failed", "The guide could not be loaded", ex, new Dictionary<string, object?>
            {
                ["class_type"] = classOption.ClassType,
                ["spec"] = spec.Id,
            });
            Editor = null;
            ShowStatus(StatusSeverity.Error, "Apply_Status_Failed");
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    partial void OnEditorChanged(ComboEditorViewModel? value)
    {
        if (_observedEditor is not null)
        {
            _observedEditor.PropertyChanged -= OnEditorPropertyChanged;
        }

        _observedEditor = value;
        if (value is not null)
        {
            value.PropertyChanged += OnEditorPropertyChanged;
        }
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComboEditorViewModel.HasUnsavedChanges) or nameof(ComboEditorViewModel.HasSavedProfile))
        {
            OnPropertyChanged(nameof(ProfileStateText));
        }
    }

    private async Task RunAsync(bool dryRun)
    {
        if (SelectedCharacter is null || SelectedClass is null || SelectedSpec is null)
        {
            return;
        }

        var request = new ComboApplyRequest(SelectedClass.Slug, SelectedSpec.Id, SelectedCharacter, dryRun);
        _logger.Info("apply.requested", "User requested a combo apply", new Dictionary<string, object?>
        {
            ["dry_run"] = dryRun,
            ["class"] = request.ClassName,
            ["spec"] = request.SpecId,
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
