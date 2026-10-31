using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// One step of a chain: a guide step (<see cref="BaseStep"/> is its index in the guide chain) or a skill the user added
/// (<see cref="IsAdded"/>). The customization fields are what the user can change; the rest comes from the guide.
/// Every change of a customization field raises <see cref="Edited"/>, which the editor uses to track unsaved changes.
/// </summary>
public sealed partial class ComboStepViewModel : ObservableObject
{
    /// <summary>
    /// The key legends that are not keys: the player binds the skill to their own key ("Hotkey") or to a bar slot ("Hotbar").
    /// The memo is shown instead of the legend.
    /// </summary>
    public static bool IsPlayerKeyLegend(string? keys) =>
        string.Equals(keys, "Hotkey", StringComparison.OrdinalIgnoreCase) || string.Equals(keys, "Hotbar", StringComparison.OrdinalIgnoreCase);

    private readonly ILocalizer _localizer;
    private readonly CultureInfo _culture;

    public ComboStepViewModel(
        int? baseStep,
        IReadOnlyList<ComboSkill> skills,
        string? keys,
        string? extra,
        string? note,
        ILocalizer localizer,
        CultureInfo culture,
        string? customName = null,
        string? memo = null,
        int? cooldownSlot = null,
        int? quickPosition = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);

        BaseStep = baseStep;
        Skills = skills;
        Keys = keys;
        Extra = extra;
        Note = note;
        _localizer = localizer;
        _culture = culture;
        _customName = customName;
        _memo = memo;
        _cooldownSlot = cooldownSlot;
        _quickPosition = quickPosition;
    }

    /// <summary>Raised when the user changes a customization field (name, memo, slots).</summary>
    public event EventHandler? Edited;

    /// <summary>Index of the guide step in its chain, or null for a skill the user added.</summary>
    public int? BaseStep { get; }

    public bool IsAdded => BaseStep is null;

    public IReadOnlyList<ComboSkill> Skills { get; }

    /// <summary>Key legend from the guide, for example "R 1" or "Hotkey".</summary>
    public string? Keys { get; }

    /// <summary>Extra line from the guide, such as "opener".</summary>
    public string? Extra { get; }

    /// <summary>Annotation from the guide.</summary>
    public string? Note { get; }

    /// <summary>The guide's extra line and annotation of this step, joined in one line. Empty when the guide has neither.</summary>
    public string GuideNotes => string.Join(" · ", new[] { Extra, Note }.Where(text => !string.IsNullOrWhiteSpace(text)));

    public bool HasGuideNotes => GuideNotes.Length > 0;

    public ComboSkill? PrimarySkill => Skills.Count > 0 ? Skills[0] : null;

    public string SkillName => PrimarySkill is { } skill ? skill.Name.For(_culture) : _localizer.Get("Apply.NoSkill");

    /// <summary>The Spanish game text of the primary skill, or null when the guide has none.</summary>
    public string? SkillDescription => PrimarySkill?.DescriptionEs;

    public bool HasDescription => !string.IsNullOrWhiteSpace(SkillDescription);

    /// <summary>Two letters from the skill name, drawn when the skill icon is missing.</summary>
    public string Glyph => SkillName.Where(char.IsLetter).Take(2).Aggregate(string.Empty, (text, letter) => text + char.ToUpperInvariant(letter));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(HasCustomName), nameof(AutomationName))]
    private string? _customName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemo), nameof(KeyLegend), nameof(AutomationName))]
    private string? _memo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CooldownLabel), nameof(HasCooldown))]
    private int? _cooldownSlot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuick), nameof(QuickLabel))]
    private int? _quickPosition;

    /// <summary>Position in the chain, counted from 1. Kept up to date by the chain.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private int _position;

    [ObservableProperty]
    private bool _isSelected;

    public bool HasCustomName => !string.IsNullOrWhiteSpace(CustomName);

    /// <summary>The user's name when there is one, otherwise the skill name.</summary>
    public string DisplayName => HasCustomName ? CustomName!.Trim() : SkillName;

    public bool HasMemo => !string.IsNullOrWhiteSpace(Memo);

    public bool IsHotkey => IsPlayerKeyLegend(Keys);

    /// <summary>
    /// The key shown under the icon. "Hotkey" means the player's own key, so the user's memo is shown instead,
    /// or a hint when there is no memo yet.
    /// </summary>
    public string KeyLegend => IsHotkey
        ? (HasMemo ? Memo!.Trim() : _localizer.Get("Apply.YourKey"))
        : (Keys ?? string.Empty);

    public bool HasCooldown => CooldownSlot is not null;

    public bool HasQuick => QuickPosition is not null;

    /// <summary>The input of the quick position, such as "M2" or "5" for a key position. Empty when there is none.</summary>
    public string QuickLabel => QuickPosition is int position
        ? QuickSlotMap.InputFor(position).MouseButton ?? position.ToString(CultureInfo.InvariantCulture)
        : string.Empty;

    /// <summary>"#n" for the cooldown slot, or empty.</summary>
    public string CooldownLabel => CooldownSlot is int slot ? "#" + slot.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Name and key for screen readers.</summary>
    public string AutomationName => string.IsNullOrEmpty(KeyLegend)
        ? _localizer.Format("Apply.StepAutomationName", Position, DisplayName)
        : _localizer.Format("Apply.StepAutomationNameWithKey", Position, DisplayName, KeyLegend);

    internal void RaiseEdited() => Edited?.Invoke(this, EventArgs.Empty);

    partial void OnCustomNameChanged(string? value) => RaiseEdited();

    partial void OnMemoChanged(string? value) => RaiseEdited();

    partial void OnCooldownSlotChanged(int? value) => RaiseEdited();

    partial void OnQuickPositionChanged(int? value) => RaiseEdited();
}
