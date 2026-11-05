using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>One of the 20 cooldown slots on the game screen. Its skill comes from <see cref="IPreviewSlotSource"/>.</summary>
public sealed partial class SlotViewModel : PanelViewModel
{
    private readonly ILocalizer _localizer;

    public SlotViewModel(int number, PreviewSlot? data, ILocalizer localizer)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentNullException.ThrowIfNull(localizer);

        Number = number;
        _localizer = localizer;
        Data = data;
    }

    /// <summary>Cooldown slot number, 1 to 20.</summary>
    public int Number { get; }

    /// <summary>The skill in the slot, or null when the slot is empty. Replaced when the combo changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkill), nameof(Title), nameof(Glyph), nameof(Icon), nameof(Skill), nameof(AutomationName), nameof(Memo), nameof(CustomName), nameof(QuickPosition))]
    private PreviewSlot? _data;

    public bool HasSkill => Data is not null;

    /// <summary>The bundle's skill in the slot (for its hover tooltip), or null when the slot is empty or has no guide skill.</summary>
    public ComboSkill? Skill => Data?.Skill;

    public string Title => Data is null
        ? _localizer.Get("Preview.Slot.Empty")
        : Data.SkillName ?? _localizer.Format("Preview.Slot.Placeholder", Number);

    public string Memo => Data?.Memo ?? string.Empty;

    public string CustomName => Data?.CustomName ?? string.Empty;

    /// <summary>Quick slot (1-20) that holds this skill, or 0 when the skill is not on the quick bar.</summary>
    public int QuickPosition => Data?.QuickPosition ?? 0;

    /// <summary>Two letters of the skill name, shown when the icon is missing.</summary>
    public string Glyph => GlyphFor(Data?.SkillName, Number);

    /// <summary>Opens the skill icon: a <c>Func&lt;Stream?&gt;</c>, or null when the slot has no skill.</summary>
    public Func<Stream?>? Icon => Data?.OpenIcon;

    public string AutomationName => Data is null
        ? _localizer.Format("Preview.Slot.Cooldown.EmptyName", Number)
        : _localizer.Format("Preview.Slot.Cooldown.Name", Number, Title);

    /// <summary>Remaining fraction of the cooldown shown as a dark sweep. 0 when not cooling down (only the debug demo sets it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCoolingDown))]
    private double _cooldownRatio;

    /// <summary>Number drawn on the cooldown sweep.</summary>
    [ObservableProperty]
    private string _cooldownText = string.Empty;

    public bool IsCoolingDown => CooldownRatio > 0;

    /// <summary>Sets the demo cooldown. A ratio of 0 clears it.</summary>
    public void SetCooldownDemo(double ratio, string text)
    {
        CooldownRatio = Math.Clamp(ratio, 0, 1);
        CooldownText = text;
    }
}
