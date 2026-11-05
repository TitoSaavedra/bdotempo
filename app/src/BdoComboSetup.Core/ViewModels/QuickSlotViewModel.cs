using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>One of the 20 quick slots on the game screen. Its key legend is the bound key of the character, else the default.</summary>
public sealed partial class QuickSlotViewModel : PanelViewModel
{
    private readonly ILocalizer _localizer;

    public QuickSlotViewModel(int position, PreviewSlot? data, QuickSlotKey? key, ILocalizer localizer)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);
        ArgumentNullException.ThrowIfNull(localizer);

        Position = position;
        _localizer = localizer;
        Data = data;
        Key = key;
    }

    /// <summary>Quick slot number, 1 to 20. Slot 10 is the key 0.</summary>
    public int Position { get; }

    /// <summary>The bound key or mouse button, or null when the slot is unbound.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyLabel), nameof(HasKey), nameof(KeyLabelOrNone), nameof(AutomationName))]
    private QuickSlotKey? _key;

    /// <summary>The key legend at the top of the slot: M1 to M6 for mouse buttons, the character for keys, empty when unbound.</summary>
    public string KeyLabel => Key?.Label ?? string.Empty;

    public bool HasKey => Key is not null;

    /// <summary>The key, or the localized "no key" text (used in automation names and the detail panel).</summary>
    public string KeyLabelOrNone => Key?.Label ?? _localizer.Get("Preview.Quick.NoKey");

    /// <summary>The skill in the slot, or null when the slot is empty. Replaced when the combo changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkill), nameof(Title), nameof(Glyph), nameof(Icon), nameof(Skill), nameof(AutomationName), nameof(Memo), nameof(CustomName), nameof(CooldownNumber))]
    private PreviewSlot? _data;

    /// <summary>The bundle's skill in the slot (for its hover tooltip), or null when the slot is empty or has no guide skill.</summary>
    public ComboSkill? Skill => Data?.Skill;

    public bool HasSkill => Data is not null;

    public string Memo => Data?.Memo ?? string.Empty;

    public string CustomName => Data?.CustomName ?? string.Empty;

    /// <summary>The cooldown slot of the same skill, or 0 when unknown.</summary>
    public int CooldownNumber => Data?.CooldownNumber ?? 0;

    public string Title => Data is null
        ? _localizer.Get("Preview.Quick.Empty")
        : Data.SkillName ?? _localizer.Format("Preview.Slot.Placeholder", Position);

    public string Glyph => GlyphFor(Data?.SkillName, Position);

    /// <summary>Opens the skill icon: a <c>Func&lt;Stream?&gt;</c>, or null when the slot has no skill.</summary>
    public Func<Stream?>? Icon => Data?.OpenIcon;

    public string AutomationName => HasSkill
        ? _localizer.Format("Preview.Quick.Name", Position, KeyLabelOrNone, Title)
        : _localizer.Format("Preview.Quick.EmptyName", Position, KeyLabelOrNone);
}
