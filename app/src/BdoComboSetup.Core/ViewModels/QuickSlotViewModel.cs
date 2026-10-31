using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>One of the 20 quick slots on the game screen. Its key legend comes from the bound key (or the default).</summary>
public sealed partial class QuickSlotViewModel : PanelViewModel
{
    private readonly ILocalizer _localizer;
    private readonly string _classSlug;

    public QuickSlotViewModel(int position, PreviewSlot? data, string classSlug, QuickSlotKey? key, ILocalizer localizer)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);
        ArgumentException.ThrowIfNullOrWhiteSpace(classSlug);
        ArgumentNullException.ThrowIfNull(localizer);

        Position = position;
        Key = key;
        Data = data;
        _classSlug = classSlug;
        _localizer = localizer;
    }

    /// <summary>Quick slot number, 1 to 20. Slot 10 is the key 0.</summary>
    public int Position { get; }

    /// <summary>The bound key or mouse button, or null when the slot is unbound.</summary>
    public QuickSlotKey? Key { get; }

    /// <summary>The key legend at the top of the slot: M1 to M6 for mouse buttons, the character for keys, empty when unbound.</summary>
    public string KeyLabel => Key?.Label ?? string.Empty;

    public bool HasKey => Key is not null;

    public bool IsMouseKey => Key?.Kind == QuickKeyKind.Mouse;

    /// <summary>The key, or the localized "no key" text (used in the detail panel and automation names).</summary>
    public string KeyLabelOrNone => Key?.Label ?? _localizer.Get("Preview.Quick.NoKey");

    /// <summary>The skill in the slot, or null when the slot is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkill), nameof(SkillNo), nameof(SkillName), nameof(Memo), nameof(CustomName),
        nameof(CooldownNumber), nameof(IconRequest), nameof(Glyph), nameof(Title), nameof(AutomationName))]
    private PreviewSlot? _data;

    public bool HasSkill => Data is not null;

    public int? SkillNo => Data?.SkillNo;

    public string? SkillName => Data?.SkillName;

    public string Memo => Data?.Memo ?? string.Empty;

    public string CustomName => Data?.CustomName ?? string.Empty;

    /// <summary>The cooldown slot of the same skill, when known.</summary>
    public int? CooldownNumber => Data?.CooldownNumber;

    public string Title => Data is null
        ? _localizer.Get("Preview.Quick.Empty")
        : Data.SkillName ?? _localizer.Format("Preview.Slot.Placeholder", Position);

    public string Glyph => GlyphFor(SkillName, Position);

    public SkillIconRequest? IconRequest => SkillNo is { } skillNo ? new SkillIconRequest(skillNo, _classSlug) : null;

    public string AutomationName => HasSkill
        ? _localizer.Format("Preview.Quick.Name", Position, KeyLabelOrNone, Title)
        : _localizer.Format("Preview.Quick.EmptyName", Position, KeyLabelOrNone);

    /// <summary>True while the pointer is over the slot; the handle strip is shown then.</summary>
    [ObservableProperty]
    private bool _isHovered;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Replaces the skill of the slot (null empties it). Used by the detail panel.</summary>
    public void SetSkill(PreviewSlot? data) => Data = data;
}
