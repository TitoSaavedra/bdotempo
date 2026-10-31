using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// One chain of the guide (for example "Damage combo") and its steps in the user's order. The steps collection is the
/// order of the combo: the list view reorders it by drag and drop, and the move buttons call <see cref="ComboEditorViewModel"/>.
/// </summary>
public sealed partial class ComboChainViewModel : ObservableObject
{
    private readonly ComboEditorViewModel _owner;
    private readonly ILocalizer _localizer;
    private readonly IReadOnlyList<SkillOption> _availableSkills;

    internal ComboChainViewModel(
        ComboEditorViewModel owner,
        int index,
        string title,
        string notesText,
        IEnumerable<ComboStepViewModel> steps,
        IReadOnlyList<SkillOption> availableSkills,
        ILocalizer localizer)
    {
        _owner = owner;
        Index = index;
        Title = title;
        NotesText = notesText;
        _availableSkills = availableSkills;
        _localizer = localizer;

        Steps = new ObservableCollection<ComboStepViewModel>(steps);
        FilteredSkills = new ObservableCollection<SkillOption>();
        RefreshFilter();
        RenumberSteps();

        // Subscribed after the first fill, so building a chain is not counted as an edit.
        Steps.CollectionChanged += OnStepsCollectionChanged;
    }

    /// <summary>The chain's index in the guide. The profile refers to chains by this index.</summary>
    public int Index { get; }

    public string Title { get; }

    /// <summary>The chain's notes from the guide, joined in one line. Empty when there are none.</summary>
    public string NotesText { get; }

    public ComboEditorViewModel Owner => _owner;

    public ObservableCollection<ComboStepViewModel> Steps { get; }

    public bool HasSteps => Steps.Count > 0;

    public string StepCountText => Steps.Count == 1
        ? _localizer.Get("Apply.StepCountOne")
        : _localizer.Format("Apply.StepCountMany", Steps.Count.ToString(CultureInfo.InvariantCulture));

    /// <summary>Every skill of the guide, for the "Add skill" list.</summary>
    public IReadOnlyList<SkillOption> AvailableSkills => _availableSkills;

    /// <summary>The "Add skill" list, filtered by <see cref="SkillSearch"/>.</summary>
    public ObservableCollection<SkillOption> FilteredSkills { get; }

    [ObservableProperty]
    private string _skillSearch = string.Empty;

    partial void OnSkillSearchChanged(string value) => RefreshFilter();

    /// <summary>Adds a skill the user chose at the end of this chain.</summary>
    [RelayCommand]
    private void AddSkill(SkillOption? option)
    {
        if (option is null)
        {
            return;
        }

        _owner.AddSkill(this, option);
        SkillSearch = string.Empty;
    }

    internal void RenumberSteps()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].Position = i + 1;
            Steps[i].IsLast = i == Steps.Count - 1;
        }

        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(StepCountText));
    }

    private void OnStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RenumberSteps();
        _owner.NotifyStepsChanged();
    }

    private void RefreshFilter()
    {
        var query = SkillSearch.Trim();
        FilteredSkills.Clear();
        foreach (var option in _availableSkills)
        {
            if (query.Length == 0
                || option.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || option.SkillNo.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal))
            {
                FilteredSkills.Add(option);
            }
        }
    }
}
