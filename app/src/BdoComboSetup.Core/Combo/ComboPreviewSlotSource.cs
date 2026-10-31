using System.ComponentModel;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Feeds the preview from the combo that the Apply page is editing: cooldown slot k is the step whose CooldownSlot is k,
/// and quick slot p is the step whose QuickPosition is p. With no editor loaded yet, every slot is empty.
/// The source follows the Apply page: <see cref="Changed"/> is raised when the guide is replaced, when a step changes,
/// and when the selected character changes (its bound quick keys are read again).
/// </summary>
public sealed class ComboPreviewSlotSource : IPreviewSlotSource, IDisposable
{
    private readonly ApplyComboViewModel _apply;
    private readonly Func<CharacterOption?, IReadOnlyDictionary<int, int>?> _boundKeysOf;
    private ComboEditorViewModel? _editor;
    private IReadOnlyDictionary<int, int>? _boundKeys;

    /// <param name="apply">The Apply page's view model: its editor is the combo shown.</param>
    /// <param name="boundKeysOf">Reads the quick keys bound by a character (quick slot to keyCode). Null means the defaults.</param>
    public ComboPreviewSlotSource(ApplyComboViewModel apply, Func<CharacterOption?, IReadOnlyDictionary<int, int>?> boundKeysOf)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(boundKeysOf);

        _apply = apply;
        _boundKeysOf = boundKeysOf;
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
            OpenIcon: skill?.OpenIcon);
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
