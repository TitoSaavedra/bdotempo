using System.Globalization;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>An entry of a slot ComboBox. Slot is null for "none".</summary>
public sealed record SlotOption(int? Slot, string Label);

/// <summary>A skill of the guide, offered by the "Add skill" list. Name is in the page language.</summary>
public sealed record SkillOption(ComboSkill Skill, string Name)
{
    public int SkillNo => Skill.SkillNo;
}

/// <summary>
/// One of the ten custom combo slots of the selected class and spec, as the slot selector shows it: its number, the name
/// the user gave it and when it was saved. An empty slot has no name and no time.
/// </summary>
public sealed partial class ComboSlotOption : ObservableObject
{
    private readonly ILocalizer _localizer;

    public ComboSlotOption(int slot, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        Slot = slot;
        _localizer = localizer;
        Label = BuildLabel();
    }

    /// <summary>1..10, the fixed place of the combo.</summary>
    public int Slot { get; }

    /// <summary>The user's name for the combo, or null when the slot has no name.</summary>
    public string? Name { get; private set; }

    /// <summary>When the slot was last saved (UTC), or null when it is empty.</summary>
    public DateTimeOffset? SavedAt { get; private set; }

    public bool IsFilled => SavedAt is not null;

    /// <summary>"3 · Mi combo", "3 · Sin nombre" or "3 · Vacía". Refreshed when the slot is saved, loaded or deleted.</summary>
    public string Label { get; private set; }

    /// <summary>Takes the slot as the store returned it, or null for an empty slot.</summary>
    public void Refresh(ComboProfileSlot? saved)
    {
        Name = saved?.Name;
        SavedAt = saved?.SavedAt;
        Label = BuildLabel();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SavedAt));
        OnPropertyChanged(nameof(IsFilled));
        OnPropertyChanged(nameof(Label));
    }

    private string BuildLabel()
    {
        var text = SavedAt is null
            ? _localizer.Get("Apply.ComboSlotEmpty")
            : Name ?? _localizer.Get("Apply.ComboSlotUnnamed");
        return _localizer.Format("Apply.ComboSlotLabel", Slot, text);
    }
}

/// <summary>A class of the catalog, shown as a button in the class grid.</summary>
public sealed partial class ClassOptionViewModel : ObservableObject
{
    public ClassOptionViewModel(ClassInfo info, CultureInfo culture, IAssetProvider assets, Func<ClassOptionViewModel, Task> select)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(select);

        Info = info;
        Name = info.Name.For(culture);
        OpenIcon = () => assets.OpenClassIcon(info.ClassType);
        SelectCommand = new AsyncRelayCommand(() => select(this));
    }

    public ClassInfo Info { get; }

    /// <summary>Opens the class icon (white on transparent). Null from the function means the icon is missing.</summary>
    public Func<Stream?> OpenIcon { get; }

    public int ClassType => Info.ClassType;

    public string Slug => Info.Slug;

    public string Name { get; }

    /// <summary>Two letters, drawn when the class icon is missing.</summary>
    public string Glyph => Name.Where(char.IsLetter).Take(2).Aggregate(string.Empty, (text, letter) => text + char.ToUpperInvariant(letter));

    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A spec of the selected class. Specs that share a guide (the three Shai experts) differ only here.</summary>
public sealed partial class SpecOptionViewModel : ObservableObject
{
    public SpecOptionViewModel(SpecInfo info, CultureInfo culture, Func<SpecOptionViewModel, Task> select)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(select);

        Info = info;
        Label = info.DisplayName.For(culture);
        SelectCommand = new AsyncRelayCommand(() => select(this));
    }

    public SpecInfo Info { get; }

    public string Id => Info.Id;

    public string Label { get; }

    public IAsyncRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
}
