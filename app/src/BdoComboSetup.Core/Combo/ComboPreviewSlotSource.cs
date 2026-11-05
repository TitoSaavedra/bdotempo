using System.ComponentModel;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Feeds the preview from the combo that the Apply page is editing: cooldown slot k is the step whose CooldownSlot is k,
/// and quick slot p is the step whose QuickPosition is p. With no editor loaded yet, every slot is empty.
/// The source follows the Apply page: <see cref="Changed"/> is raised when the guide is replaced, when a step changes,
/// and when the selected character changes (its bound quick keys are read again).
/// <para>
/// It is also the write side of the page (<see cref="ISlotSkillEditor"/>): the skill the user picks in a slot goes to the
/// same working copy, through <see cref="ComboEditorViewModel.ReplaceCooldownSkill"/>, so the guide is never touched and the
/// preview refreshes through the same <see cref="Changed"/> event.
/// </para>
/// </summary>
public sealed class ComboPreviewSlotSource : IPreviewSlotSource, ISlotSkillEditor, IDisposable
{
    private readonly ApplyComboViewModel _apply;
    private readonly Func<CharacterOption?, IReadOnlyDictionary<int, int>?> _boundKeysOf;
    private readonly ClassSkillSource? _classSkills;
    private ComboEditorViewModel? _editor;
    private IReadOnlyDictionary<int, int>? _boundKeys;

    /// <param name="apply">The Apply page's view model: its editor is the combo shown.</param>
    /// <param name="boundKeysOf">Reads the quick keys bound by a character (quick slot to keyCode). Null means the defaults.</param>
    public ComboPreviewSlotSource(ApplyComboViewModel apply, Func<CharacterOption?, IReadOnlyDictionary<int, int>?> boundKeysOf)
        : this(apply, boundKeysOf, null)
    {
    }

    /// <param name="classSkills">The skills of each class at their top rank, offered by the slot editor. Null: no picker.</param>
    public ComboPreviewSlotSource(
        ApplyComboViewModel apply,
        Func<CharacterOption?, IReadOnlyDictionary<int, int>?> boundKeysOf,
        ClassSkillSource? classSkills)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(boundKeysOf);

        _apply = apply;
        _boundKeysOf = boundKeysOf;
        _classSkills = classSkills;
        _boundKeys = boundKeysOf(apply.SelectedCharacter);
        Follow(apply.Editor);
        apply.PropertyChanged += OnApplyChanged;
    }

    public event EventHandler? Changed;

    public PreviewSlot? Cooldown(int number)
    {
        ValidateNumber(number, SlotAssignment.CooldownSlotCount);
        return Find(step => step.CooldownSlot == number);
    }

    public PreviewSlot? Quick(int number)
    {
        ValidateNumber(number, QuickSlotKeys.SlotCount);
        return Find(step => step.QuickPosition == number);
    }

    public QuickSlotKey? QuickKey(int number)
    {
        QuickSlotKeys.ValidateSlot(number);
        return QuickSlotLegend.Resolve(number, _boundKeys);
    }

    /// <summary>The class of the combo being edited, or null while the Apply page has no guide open.</summary>
    public int? ClassType => _editor?.ClassType;

    /// <summary>The skills of that class at their top rank (the picker rows). Empty without a combo or without a catalog.</summary>
    public IReadOnlyList<SkillOption> Skills => _editor is { } editor && _classSkills is { } catalog
        ? catalog.OptionsOf(editor.ClassType)
        : [];

    /// <summary>
    /// Puts a skill of the class in a cooldown slot of the combo being edited. False when there is no combo open, when the
    /// class catalog does not list that skill, or when no step of the combo holds that slot.
    /// </summary>
    public bool ReplaceCooldownSkill(int number, int skillNo)
    {
        ValidateNumber(number, SlotAssignment.CooldownSlotCount);
        if (_editor is not { } editor || _classSkills?.Find(editor.ClassType, skillNo) is not { } skill)
        {
            return false;
        }

        return editor.ReplaceCooldownSkill(number, skill);
    }

    public void Dispose()
    {
        _apply.PropertyChanged -= OnApplyChanged;
        Follow(null);
    }

    private PreviewSlot? Find(Func<ComboStepViewModel, bool> match)
    {
        var step = _editor?.AllSteps().FirstOrDefault(match);
        return step is null ? null : ToPreview(step);
    }

    private static PreviewSlot ToPreview(ComboStepViewModel step)
    {
        var skill = step.PrimarySkill;
        return new PreviewSlot(
            SkillNo: skill?.SkillNo,
            SkillName: skill is null ? null : step.SkillName,
            Memo: NullIfBlank(step.Memo),
            CustomName: NullIfBlank(step.CustomName),
            CooldownNumber: step.CooldownSlot,
            QuickPosition: step.QuickPosition,
            OpenIcon: skill?.OpenIcon,
            Skill: skill);
    }

    private void OnApplyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplyComboViewModel.Editor))
        {
            Follow(_apply.Editor);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (e.PropertyName == nameof(ApplyComboViewModel.SelectedCharacter))
        {
            _boundKeys = _boundKeysOf(_apply.SelectedCharacter);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Follow(ComboEditorViewModel? editor)
    {
        if (_editor is not null)
        {
            _editor.ContentChanged -= OnContentChanged;
        }

        _editor = editor;
        if (_editor is not null)
        {
            _editor.ContentChanged += OnContentChanged;
        }
    }

    private void OnContentChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static void ValidateNumber(int number, int max)
    {
        if (number < 1 || number > max)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"Slot numbers are 1 to {max}.");
        }
    }
}
