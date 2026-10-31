using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Characters;
using BdoComboSetup.Core.Confirmation;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;
using BdoComboSetup.Core.Search;
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
/// of the guide (<see cref="Editor"/>), and saving, restoring and applying it to the character.
/// <para>
/// Applying is two steps. "Apply to character" runs a dry run and opens the review (the diff of the character file);
/// "Write to character" then writes it through the real applier, which takes a verified backup first. The write is
/// refused when the working copy changed after the review.
/// </para>
/// </summary>
public sealed partial class ApplyComboViewModel : ObservableObject
{
    private readonly ICombosCatalog _catalog;
    private readonly ICharacterSource _characters;
    private readonly WriteConfirmation _confirmation;
    private readonly IComboProfileStore _profiles;
    private readonly IComboApplier _applier;
    private readonly ILocalizer _localizer;
    private readonly CultureInfo _culture;
    private readonly AppLogger _logger;
    private readonly Dictionary<(int ClassType, string SpecId), ComboEditorViewModel> _editors = new();
    private ComboEditorViewModel? _observedEditor;
    private int _loadVersion;
    private bool _initialized;
    private Dictionary<string, CharacterEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>The request that the open review was computed for, and its signature, to refuse a write after an edit.</summary>
    private ComboApplyRequest? _reviewedRequest;
    private string? _reviewedSignature;

    public ApplyComboViewModel(
        ICombosCatalog catalog,
        IAssetProvider assets,
        ICharacterSource characters,
        IComboProfileStore profiles,
        IComboApplier applier,
        WriteConfirmation confirmation,
        ILocalizer localizer,
        CultureInfo culture,
        AppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _characters = characters;
        _confirmation = confirmation;
        _profiles = profiles;
        _applier = applier;
        _localizer = localizer;
        _culture = culture;
        _logger = logger;

        Characters = new ObservableCollection<CharacterOption>();
        Classes = new ObservableCollection<ClassOptionViewModel>(
            catalog.Classes.Select(info => new ClassOptionViewModel(info, culture, assets, option => SelectClassAsync(option))));
        ClassChoices = catalog.Classes
            .Select(info => new ClassChoice(info.ClassType, info.Name.For(culture), () => assets.OpenClassIcon(info.ClassType), info.Name))
            .ToList();
        FilteredClasses = new ObservableCollection<ClassOptionViewModel>();
        RefreshClassFilter();
        Specs = new ObservableCollection<SpecOptionViewModel>();
        ReviewLines = new ObservableCollection<string>();
    }

    /// <summary>The live characters of the active account, in the game's order. Filled by RefreshCharactersAsync.</summary>
    public ObservableCollection<CharacterOption> Characters { get; }

    /// <summary>Every class that can be picked in the character editor (all classes with a guide, in classType order).</summary>
    public IReadOnlyList<ClassChoice> ClassChoices { get; }

    /// <summary>The account the character list comes from. Null when no account can be found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveAccountText))]
    private string? _activeAccount;

    /// <summary>The label in the page header: the active account, or "No account found".</summary>
    public string ActiveAccountText => ActiveAccount is null
        ? _localizer.Get("Apply.NoAccount")
        : _localizer.Format("Apply.ActiveAccount", ActiveAccount);

    /// <summary>Why the character list is empty (no account, or no live character). Empty when there are characters.</summary>
    [ObservableProperty]
    private string _charactersMessage = string.Empty;

    /// <summary>Every class that has a guide, in classType order.</summary>
    public ObservableCollection<ClassOptionViewModel> Classes { get; }

    /// <summary>The class grid's quick filter text. Matches the class name in Spanish or English.</summary>
    [ObservableProperty]
    private string _classSearchText = string.Empty;

    /// <summary>The classes that match <see cref="ClassSearchText"/>, in classType order (the grid shows these).</summary>
    public ObservableCollection<ClassOptionViewModel> FilteredClasses { get; }

    partial void OnClassSearchTextChanged(string value) => RefreshClassFilter();

    /// <summary>True when the class grid's search matches no class (the grid shows the empty-state text).</summary>
    public bool NoClassMatches => FilteredClasses.Count == 0;

    private void RefreshClassFilter()
    {
        FilteredClasses.Clear();
        foreach (var option in SearchText.Filter(Classes, ClassSearchText, option => SearchFields.ForClass(option.Info)))
        {
            FilteredClasses.Add(option);
        }

        OnPropertyChanged(nameof(NoClassMatches));
    }

    /// <summary>
    /// The texts a character is searched by: its name and its class in both languages. The class comes from the catalog,
    /// so the search finds "Tamer" and "Domadora" alike whatever the UI language is.
    /// </summary>
    public IEnumerable<string?> CharacterSearchTexts(CharacterOption character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var classInfo = character.ClassType is int classType
            ? Classes.FirstOrDefault(option => option.ClassType == classType)?.Info
            : null;
        return SearchFields.ForCharacter(character, classInfo);
    }

    /// <summary>The specs of the selected class. Each one has its own guide.</summary>
    public ObservableCollection<SpecOptionViewModel> Specs { get; }

    /// <summary>The lines of the open review: one per changed slot, memo and quick slot.</summary>
    public ObservableCollection<string> ReviewLines { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
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

    /// <summary>True while a dry run or a write is running. Every action waits for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DryRunCommand), nameof(ConfirmWriteCommand), nameof(CancelReviewCommand))]
    private bool _isBusy;

    /// <summary>Which parts of the combo are written. The three checkboxes of the write options.</summary>
    [ObservableProperty]
    private bool _includeCooldownBar = true;

    [ObservableProperty]
    private bool _includeMemos = true;

    [ObservableProperty]
    private bool _includeQuickSlots = true;

    /// <summary>True while the review of the character file is shown.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmWriteCommand), nameof(CancelReviewCommand))]
    private bool _isReviewOpen;

    /// <summary>The review summary: "no changes", or the number of changes. Empty when no review is open.</summary>
    [ObservableProperty]
    private string _reviewSummary = string.Empty;

    /// <summary>True when the review has changes that a write would make. A review without changes has no write button.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmWriteCommand))]
    private bool _reviewHasChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Information;

    [ObservableProperty]
    private bool _isStatusOpen;

    public bool HasEditor => Editor is not null;

    /// <summary>A spec is selected, its guide is not loading, and the catalog has no guide for it.</summary>
    public bool IsGuideMissing => !IsLoading && Editor is null && SelectedSpec is not null;

    /// <summary>A character and a guide are selected, and nothing is running.</summary>
    public bool CanRun => SelectedCharacter is not null && Editor is not null && !IsBusy;

    public string ClassName => SelectedClass?.Name ?? string.Empty;

    public string SpecName => SelectedSpec?.Label ?? string.Empty;

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
                ? "Apply.StateUnsaved"
                : Editor.HasSavedProfile ? "Apply.StateSaved" : "Apply.StateBase");
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
            await RefreshCharactersAsync();
            return;
        }

        _initialized = true;
        await RefreshCharactersAsync();
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

        var character = Characters.FirstOrDefault(item => item.ClassType == option.ClassType);
        if (character is not null)
        {
            SelectedCharacter = character;
        }

        await SelectClassAsync(option, specId);
    }

    /// <summary>
    /// Reloads the live characters of the active account (the account can change in the settings, and a name or class can
    /// change in the editor). The selected character is kept when it is still there; its class follows it when it changed.
    /// </summary>
    public async Task RefreshCharactersAsync()
    {
        string? account = null;
        IReadOnlyList<CharacterEntry> entries = [];
        try
        {
            account = _characters.ActiveAccount();
            if (account is not null)
            {
                // On the UI thread: the names come from the resource loader, which is used from the UI thread only.
                entries = _characters.Load(account);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("apply.characters.failed", "The characters could not be read", ex);
            ShowStatus(StatusSeverity.Error, "Apply.StatusCharactersFailed");
        }

        var previous = SelectedCharacter?.Pcid;
        ActiveAccount = account;
        _entries = entries.ToDictionary(entry => entry.Pcid, StringComparer.Ordinal);
        Characters.Clear();
        foreach (var entry in entries)
        {
            Characters.Add(new CharacterOption(entry.Name, entry.Pcid, entry.ClassType));
        }

        CharactersMessage = account is null
            ? _localizer.Get("Apply.NoAccount")
            : entries.Count == 0 ? _localizer.Get("Apply.NoCharacters") : string.Empty;

        var kept = Characters.FirstOrDefault(item => item.Pcid == previous) ?? Characters.FirstOrDefault();
        SelectedCharacter = kept;
        if (kept is not null && kept.Pcid != previous && kept.ClassType is int classType)
        {
            var target = Classes.FirstOrDefault(item => item.ClassType == classType);
            if (target is not null && !ReferenceEquals(target, SelectedClass))
            {
                await SelectClassAsync(target);
            }
        }

        _logger.Info("apply.characters.refreshed", "Character list refreshed", new Dictionary<string, object?>
        {
            ["characters"] = Characters.Count,
            ["account_found"] = account is not null,
        });
    }

    /// <summary>What the editor starts from for a character: the resolved name and class, and where each one comes from.</summary>
    public CharacterEntry? EntryOf(CharacterOption option) => _entries.GetValueOrDefault(option.Pcid);

    /// <summary>
    /// Saves the name and class the user set for a character. A null name or class clears that part, so the roster falls
    /// back to the in-game label and the inferred class. The class must be one of ClassChoices.
    /// </summary>
    public async Task SaveCharacterEditAsync(CharacterOption option, string? name, int? classType)
    {
        ArgumentNullException.ThrowIfNull(option);
        var account = ActiveAccount;
        if (account is null)
        {
            ShowStatus(StatusSeverity.Warning, "Apply.StatusCharacterNoAccount");
            return;
        }

        if (classType is int chosen && ClassChoices.All(choice => choice.ClassType != chosen))
        {
            _logger.Warn("apply.character.class_unknown", "The chosen class is not in the catalog; nothing saved");
            ShowStatus(StatusSeverity.Warning, "Apply.StatusCharacterInvalid");
            return;
        }

        try
        {
            _characters.SaveOverride(account, option.Pcid, name, classType);
            _logger.Info("apply.character.saved", "Character name or class saved from the editor");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.Error("apply.character.save_failed", "The character name or class could not be saved", ex);
            ShowStatus(StatusSeverity.Error, "Apply.StatusCharacterFailed");
            return;
        }

        ShowStatus(StatusSeverity.Success, "Apply.StatusCharacterSaved");
        await RefreshCharactersAsync();
    }

    /// <summary>Clears the name and class the user set for a character: the game files decide them again.</summary>
    public Task ResetCharacterEditAsync(CharacterOption option) => SaveCharacterEditAsync(option, null, null);

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
            ShowStatus(StatusSeverity.Success, "Apply.StatusSaved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("apply.profile.save_failed", "The combo could not be saved", ex, new Dictionary<string, object?>
            {
                ["class_type"] = editor.ClassType,
                ["spec"] = editor.SpecId,
            });
            ShowStatus(StatusSeverity.Error, "Apply.StatusSaveFailed");
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
            CloseReview();
            ShowStatus(StatusSeverity.Success, "Apply.StatusRestored");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("apply.profile.restore_failed", "The saved combo could not be deleted", ex, new Dictionary<string, object?>
            {
                ["class_type"] = editor.ClassType,
                ["spec"] = editor.SpecId,
            });
            ShowStatus(StatusSeverity.Error, "Apply.StatusFailed");
        }
    }

    /// <summary>Computes the changes the write would make and shows them. Writes nothing.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DryRun() => ReviewAsync(allowWrite: false);

    /// <summary>Computes the changes and opens the review, where they can be written to the character.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Apply() => ReviewAsync(allowWrite: true);

    /// <summary>Writes the reviewed changes. Refused when the working copy changed after the review.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmWrite))]
    private async Task ConfirmWrite()
    {
        var editor = Editor;
        var character = SelectedCharacter;
        if (editor is null || character is null || _reviewedRequest is null || _reviewedSignature is null)
        {
            CloseReview();
            return;
        }

        var request = BuildRequest(editor, character);
        if (!string.Equals(Signature(request), _reviewedSignature, StringComparison.Ordinal))
        {
            _logger.Warn("apply.review.stale", "The working copy changed after the review; the write was refused");
            CloseReview();
            ShowStatus(StatusSeverity.Warning, "Apply.StatusStale");
            return;
        }

        IsBusy = true;
        try
        {
            // The reminder comes first: Cancel writes nothing and keeps the review open.
            if (!await _confirmation.ConfirmAsync())
            {
                _logger.Info("apply.write.declined", "The write was not confirmed; nothing was written");
                return;
            }

            _logger.Info("apply.write.requested", "User confirmed the write to the character", new Dictionary<string, object?>
            {
                ["class_type"] = editor.ClassType,
                ["spec"] = editor.SpecId,
                ["cooldown_slots"] = request.CooldownSlots.Count,
                ["quick_slots"] = request.QuickSlots.Count,
            });
            var result = await _applier.ApplyAsync(request);
            ShowResult(result);
            if (result.Outcome is OperationOutcome.Written or OperationOutcome.Unchanged)
            {
                CloseReview();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("apply.failed", "Combo write request failed", ex);
            ShowStatus(StatusSeverity.Error, "Apply.StatusFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelReview))]
    private void CancelReview()
    {
        CloseReview();
        _logger.Info("apply.review.cancelled", "Review closed without writing");
    }

    private bool CanConfirmWrite() => IsReviewOpen && ReviewHasChanges && !IsBusy;

    private bool CanCancelReview() => IsReviewOpen && !IsBusy;

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
        CloseReview();
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
            ShowStatus(StatusSeverity.Error, "Apply.StatusFailed");
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

        CloseReview();
    }

    partial void OnSelectedCharacterChanged(CharacterOption? value) => CloseReview();

    partial void OnIncludeCooldownBarChanged(bool value) => CloseReview();

    partial void OnIncludeMemosChanged(bool value) => CloseReview();

    partial void OnIncludeQuickSlotsChanged(bool value) => CloseReview();

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComboEditorViewModel.HasUnsavedChanges) or nameof(ComboEditorViewModel.HasSavedProfile))
        {
            OnPropertyChanged(nameof(ProfileStateText));
        }
    }

    /// <summary>Runs a dry run with the current working copy and opens the review with its changes.</summary>
    private async Task ReviewAsync(bool allowWrite)
    {
        var editor = Editor;
        var character = SelectedCharacter;
        if (editor is null || character is null)
        {
            return;
        }

        var request = BuildRequest(editor, character);
        _logger.Info("apply.requested", "User requested a combo review", new Dictionary<string, object?>
        {
            ["allow_write"] = allowWrite,
            ["class_type"] = editor.ClassType,
            ["spec"] = editor.SpecId,
            ["cooldown_slots"] = request.CooldownSlots.Count,
            ["quick_slots"] = request.QuickSlots.Count,
        });

        IsBusy = true;
        try
        {
            var result = await _applier.DryRunAsync(request);
            if (result.Outcome is OperationOutcome.Refused or OperationOutcome.Failed)
            {
                CloseReview();
                ShowResult(result);
                return;
            }

            OpenReview(request, result, allowWrite);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("apply.failed", "Combo dry run failed", ex);
            CloseReview();
            ShowStatus(StatusSeverity.Error, "Apply.StatusFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fills the review from a dry run. The write button is offered only when there is a change to write, and the
    /// request is kept with its signature so the write can check that nothing changed since.
    /// </summary>
    private void OpenReview(ComboApplyRequest request, ComboApplyResult result, bool allowWrite)
    {
        ReviewLines.Clear();
        var diff = result.Diff;
        var changed = diff is { Changed: true };
        if (diff is not null)
        {
            foreach (var slot in diff.Slots.OrderBy(item => item.Slot))
            {
                ReviewLines.Add(_localizer.Format(
                    "Apply.Review.Slot",
                    slot.Slot + 1,
                    NameOrEmpty(slot.OldName, slot.OldSkillNo),
                    NameOrEmpty(slot.NewName, slot.NewSkillNo)));
            }

            foreach (var memo in diff.Memos.Where(item => item.Status != MemoStatus.Unchanged))
            {
                ReviewLines.Add(_localizer.Format("Apply.Review.Memo", SkillName(memo.SkillNo), memo.NewMemo));
            }

            foreach (var quick in diff.QuickSlots.OrderBy(item => item.Position))
            {
                ReviewLines.Add(_localizer.Format(
                    "Apply.Review.Quick",
                    QuickSlotMap.InputFor(quick.Position).MouseButton ?? quick.Position.ToString(CultureInfo.InvariantCulture),
                    NameOrEmpty(null, quick.OldSkillNo),
                    NameOrEmpty(null, quick.NewSkillNo)));
            }
        }

        ReviewSummary = changed
            ? _localizer.Format("Apply.Review.Summary", ReviewLines.Count)
            : _localizer.Get("Apply.Review.NoChanges");

        _reviewedRequest = request;
        _reviewedSignature = Signature(request);
        ReviewHasChanges = changed && allowWrite;
        IsReviewOpen = true;
        _logger.Info("apply.review.opened", "Review of the character file opened", new Dictionary<string, object?>
        {
            ["changed"] = changed,
            ["lines"] = ReviewLines.Count,
            ["allow_write"] = allowWrite,
        });
    }

    private void CloseReview()
    {
        if (!IsReviewOpen && _reviewedRequest is null)
        {
            return;
        }

        IsReviewOpen = false;
        ReviewHasChanges = false;
        ReviewLines.Clear();
        ReviewSummary = string.Empty;
        _reviewedRequest = null;
        _reviewedSignature = null;
    }

    private ComboApplyRequest BuildRequest(ComboEditorViewModel editor, CharacterOption character) =>
        ComboApplyRequestBuilder.Build(
            editor,
            character.Pcid,
            new ComboWriteOptions(IncludeCooldownBar, IncludeMemos, IncludeQuickSlots));

    /// <summary>Everything the applier is given, as text. Two requests with the same signature write the same bytes.</summary>
    private static string Signature(ComboApplyRequest request)
    {
        var parts = new List<string>
        {
            request.Pcid,
            request.ClassId.ToString(CultureInfo.InvariantCulture),
            request.WriteCooldownBar ? "1" : "0",
            request.WriteMemos ? "1" : "0",
            request.WriteQuickSlots ? "1" : "0",
        };
        parts.AddRange(request.CooldownSlots.Select(slot => $"c{slot.Slot}:{slot.SkillNo}:{slot.SkillName}:{slot.Memo}"));
        parts.AddRange(request.QuickSlots.Select(slot => $"q{slot.Position}:{slot.SkillNo}"));
        return string.Join("|", parts);
    }

    /// <summary>Shows the outcome and the backup path in the status bar.</summary>
    private void ShowResult(ComboApplyResult result)
    {
        var diff = result.Diff;
        var lines = new List<string>();
        var severity = StatusSeverity.Information;
        switch (result.Outcome)
        {
            case OperationOutcome.DryRun:
                lines.Add(diff is { Changed: true }
                    ? _localizer.Format("Apply.Result.DryRun", ChangeCount(diff))
                    : _localizer.Get("Apply.Result.DryRunUnchanged"));
                break;
            case OperationOutcome.Written:
                severity = StatusSeverity.Success;
                lines.Add(_localizer.Format("Apply.Result.Written", ChangeCount(diff)));
                break;
            case OperationOutcome.Unchanged:
                lines.Add(_localizer.Get("Apply.Result.Unchanged"));
                break;
            case OperationOutcome.Refused:
                severity = StatusSeverity.Warning;
                lines.Add(_localizer.Format("Apply.Result.Refused", ProblemText(result)));
                break;
            case OperationOutcome.RolledBack:
                severity = StatusSeverity.Error;
                lines.Add(_localizer.Format("Apply.Result.RolledBack", ProblemText(result)));
                break;
            default:
                severity = StatusSeverity.Error;
                lines.Add(_localizer.Format("Apply.Result.Failed", ProblemText(result)));
                break;
        }

        if (result.BackupPath is not null)
        {
            lines.Add(_localizer.Format("Apply.Result.Backup", result.BackupPath));
        }

        if (result.Warnings.Count > 0)
        {
            lines.Add(_localizer.Format("Apply.Result.Warnings", result.Warnings.Count));
        }

        StatusMessage = string.Join(Environment.NewLine, lines);
        StatusSeverity = severity;
        IsStatusOpen = true;
    }

    private static int ChangeCount(CharacterDiff? diff) =>
        diff is null ? 0 : diff.Slots.Count + diff.Memos.Count(memo => memo.Status != MemoStatus.Unchanged) + diff.QuickSlots.Count;

    private string ProblemText(ComboApplyResult result)
    {
        var title = _localizer.Get(ProblemKey(result.Problem));
        return string.IsNullOrEmpty(result.ProblemDetail)
            ? title
            : _localizer.Format("Apply.ProblemDetail", title, result.ProblemDetail);
    }

    /// <summary>The resource key of an applier problem. Each key is written out, so the resource check can see every one.</summary>
    private static string ProblemKey(OperationProblem problem) => problem switch
    {
        OperationProblem.None => "Apply.Problem.None",
        OperationProblem.InvalidRequest => "Apply.Problem.InvalidRequest",
        OperationProblem.AccountNotFound => "Apply.Problem.AccountNotFound",
        OperationProblem.CharacterNotFound => "Apply.Problem.CharacterNotFound",
        OperationProblem.SkillTableMissing => "Apply.Problem.SkillTableMissing",
        OperationProblem.SkillMismatch => "Apply.Problem.SkillMismatch",
        OperationProblem.UnsupportedLayout => "Apply.Problem.UnsupportedLayout",
        OperationProblem.QuickSlotRefused => "Apply.Problem.QuickSlotRefused",
        OperationProblem.ValidationFailed => "Apply.Problem.ValidationFailed",
        OperationProblem.GameRunning => "Apply.Problem.GameRunning",
        OperationProblem.BackupFailed => "Apply.Problem.BackupFailed",
        OperationProblem.WriteFailed => "Apply.Problem.WriteFailed",
        OperationProblem.RollbackFailed => "Apply.Problem.RollbackFailed",
        _ => "Apply.Problem.UnexpectedError",
    };

    private string NameOrEmpty(string? name, int? skillNo)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name!;
        }

        return skillNo is > 0 ? SkillName(skillNo.Value) : _localizer.Get("Apply.Review.Empty");
    }

    private string SkillName(int skillNo)
    {
        var option = Editor?.AvailableSkills.FirstOrDefault(item => item.SkillNo == skillNo);
        return option?.Name ?? _localizer.Format("Apply.Review.UnknownSkill", skillNo);
    }

    private void ShowStatus(StatusSeverity severity, string messageKey)
    {
        StatusMessage = _localizer.Get(messageKey);
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}

/// <summary>A class in the character editor's picker: its id, its name in the page language, and its icon.</summary>
public sealed record ClassChoice(int ClassType, string Name, Func<Stream?> OpenIcon, BilingualText Names);
