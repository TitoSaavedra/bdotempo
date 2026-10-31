using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>One cooldown slot as shown on the game screen. Only the quick slot position can change.</summary>
public sealed partial class SlotViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;

    public SlotViewModel(SlotData data, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(localizer);

        _localizer = localizer;
        Index = data.Index;
        Group = data.Group;
        SkillName = data.SkillName;
        SkillNo = data.SkillNo;
        Memo = data.Memo ?? string.Empty;
        CustomName = data.CustomName ?? string.Empty;
        _quickPosition = data.QuickPosition;
    }

    public int Index { get; }

    public SlotGroup Group { get; }

    /// <summary>Null for placeholder slots, which show a localized "Skill N" title.</summary>
    public string? SkillName { get; }

    /// <summary>Game skill number, or null. It selects the icon.</summary>
    public int? SkillNo { get; }

    public string Memo { get; }

    public string CustomName { get; }

    public bool IsEmpty => Group == SlotGroup.Empty;

    public bool IsComboSkill => Group == SlotGroup.Combo;

    public bool IsPriorityBuff => Group == SlotGroup.PriorityBuff;

    public string Title => IsEmpty
        ? _localizer.Get("Preview_EmptySlot")
        : SkillName ?? _localizer.Format("Preview_PlaceholderSkill", Index);

    /// <summary>Two-letter placeholder glyph, used when no icon is found. Placeholders use the slot number.</summary>
    public string Glyph
    {
        get
        {
            if (SkillName is null)
            {
                return Index.ToString("00", CultureInfo.InvariantCulture);
            }

            return new string(SkillName.Where(char.IsLetter).Take(2).ToArray()).ToUpperInvariant();
        }
    }

    public string GroupLabel => Group switch
    {
        SlotGroup.Combo => _localizer.Get("Preview_Group_Combo"),
        SlotGroup.PriorityBuff => _localizer.Get("Preview_Group_PriorityBuff"),
        _ => _localizer.Get("Preview_Group_Empty"),
    };

    /// <summary>1..9 for a quick bar position, 0 when the slot is not on the quick bar.</summary>
    [ObservableProperty]
    private int _quickPosition;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Absolute path of the skill icon, or null (the glyph is drawn instead).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private string? _iconPath;

    public bool HasIcon => IconPath is not null;

    /// <summary>Top-left of the slot relative to the cooldown bar.</summary>
    [ObservableProperty]
    private PixelOffset _offset;

    [ObservableProperty]
    private double _size;

    /// <summary>False when the bar has fewer cells than slots (the extra slots are hidden).</summary>
    [ObservableProperty]
    private bool _isPlaced = true;
}
