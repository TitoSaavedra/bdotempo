using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>One of the 20 cooldown slots on the game screen. Its skill comes from <see cref="IPreviewSlotSource"/>.</summary>
public sealed partial class SlotViewModel : PanelViewModel
{
    private readonly ILocalizer _localizer;
    private readonly string _classSlug;

    public SlotViewModel(int number, PreviewSlot? data, string classSlug, ILocalizer localizer)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(classSlug);
        ArgumentNullException.ThrowIfNull(localizer);

        Number = number;
        Data = data;
        _classSlug = classSlug;
        _localizer = localizer;
    }

    /// <summary>Cooldown slot number, 1 to 20.</summary>
    public int Number { get; }

    /// <summary>The skill in the slot, or null when the slot is empty.</summary>
    public PreviewSlot? Data { get; }

    public bool IsEmpty => Data is null;

    public bool HasSkill => Data is not null;

    public int? SkillNo => Data?.SkillNo;

    public string? SkillName => Data?.SkillName;

    public string Memo => Data?.Memo ?? string.Empty;

    public string CustomName => Data?.CustomName ?? string.Empty;

    public string Title => Data is null
        ? _localizer.Get("Preview.Slot.Empty")
        : Data.SkillName ?? _localizer.Format("Preview.Slot.Placeholder", Number);

    /// <summary>Two letters of the skill name, drawn under the icon (visible when the icon is missing).</summary>
    public string Glyph => GlyphFor(SkillName, Number);

    public SkillIconRequest? IconRequest => SkillNo is { } skillNo ? new SkillIconRequest(skillNo, _classSlug) : null;

    public string AutomationName => Data is null
        ? _localizer.Format("Preview.Slot.Cooldown.EmptyName", Number)
        : _localizer.Format("Preview.Slot.Cooldown.Name", Number, Title);

    /// <summary>Quick slot (1 to 10) that holds this skill, or 0 when the skill is not on the quick bar.</summary>
    [ObservableProperty]
    private int _quickPosition;

    [ObservableProperty]
    private bool _isSelected;

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
