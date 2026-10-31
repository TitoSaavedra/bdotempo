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
