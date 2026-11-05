using System.Collections.ObjectModel;
using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.Localization;
using BdoComboSetup.Core.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>
/// The working copy of one guide: its chains and steps as the user arranges them, the selected step and the
/// customization. It starts from the guide, or from the saved profile on top of the guide. <see cref="ToProfile"/>
/// gives what to save; <see cref="ResetToBase"/> drops every customization.
/// </summary>
public sealed partial class ComboEditorViewModel : ObservableObject
{
    /// <summary>The first cooldown slot of the default layout's combo steps.</summary>
    private const int FirstComboSlot = 1;

    /// <summary>The last cooldown slot that the default layout gives to the steps of the other combos (the main combo may use up to 20).</summary>
    private const int LastComboSlot = 12;

    /// <summary>The first cooldown slot of the default layout's buff steps (later when the main combo needs more slots).</summary>
    private const int FirstBuffSlot = 13;

    private readonly ComboGuide _guide;
    private readonly ILocalizer _localizer;
    private readonly CultureInfo _culture;
    private readonly GuideTranslation _translation;
    private readonly AppLogger _logger;
    private readonly IChainKindResolver _kinds;
    private readonly ClassSkillSource? _classSkills;
    private readonly Dictionary<int, ComboSkill> _skillsByNo;
    private bool _tracking;

    /// <summary>The kind of each shown chain, by its index in the guide (the reminder of the remaining skills reads it).</summary>
    private readonly Dictionary<int, ComboChainKind> _chainKinds = [];

    private ComboEditorViewModel(
        int classType,
        string specId,
        ComboGuide guide,
        ILocalizer localizer,
        CultureInfo culture,
        AppLogger logger,
        IChainKindResolver kinds,
        ClassSkillSource? classSkills,
        GuideTranslation translation)
    {
        ClassType = classType;
        SpecId = specId;
        _guide = guide;
        _localizer = localizer;
        _culture = culture;
        _logger = logger;
        _kinds = kinds;
        _classSkills = classSkills;
        _translation = translation;
        Title = translation.Text(guide.Title);
        Description = translation.Text(guide.Description);

        AvailableSkills = BuildAvailableSkills(guide, culture);
        _skillsByNo = AvailableSkills.ToDictionary(option => option.SkillNo, option => option.Skill);
        Addons = guide.Addons.Select(addon => new ComboAddonViewModel(addon, culture, translation)).ToList();
        Chains = new ObservableCollection<ComboChainViewModel>();
        ManualChains = new ObservableCollection<ComboChainViewModel>();
        ContentChanged += (_, _) => RaiseRemainingChanged();
    }

    /// <summary>
    /// Raised whenever the content of the working copy changes: a step edited, moved, added or removed, or the guide reset.
    /// Unlike unsaved changes, it is raised from the first load on. The Preview page follows it.
    /// </summary>
    public event EventHandler? ContentChanged;

    public int ClassType { get; }

    public string SpecId { get; }

    /// <summary>The guide's title in the page language.</summary>
    public string Title { get; }

    /// <summary>The guide's kicker in the page language, or null when the site has none.</summary>
    public string? Description { get; }

    /// <summary>The host of the guide page on bdoclasshub.com, shown as the source. Empty when the guide has none.</summary>
    public string SourceHost => Uri.TryCreate(_guide.Source, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

    /// <summary>The guide page on bdoclasshub.com the guide came from (every guide has one), or null when it has none.</summary>
    public Uri? SourceUri => Uri.TryCreate(_guide.Source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;

    /// <summary>True when the guide has a source page to link to.</summary>
    public bool HasSource => SourceUri is not null;

    /// <summary>The buff and combo chains, buffs first. These are the steps of the combo.</summary>
    public ObservableCollection<ComboChainViewModel> Chains { get; }

    /// <summary>
    /// The mobility chains: a manual pool. Their skills go to a cooldown slot only by hand (see <see cref="AddToFreeSlot"/>),
    /// never with the combo.
    /// </summary>
    public ObservableCollection<ComboChainViewModel> ManualChains { get; }

    private IReadOnlyList<PendingGroup> _pendingGroups = [];

    /// <summary>
    /// The skills of the guide that no cooldown slot holds, in groups: buffs, combo steps that did not fit, mobility and
    /// repositioning. The editor shows them as the "still to place" list that can be dragged into the slots.
    /// </summary>
    public IReadOnlyList<PendingGroup> PendingGroups => _pendingGroups;

    /// <summary>True when <see cref="PendingGroups"/> has something to remind.</summary>
    public bool HasPending => _pendingGroups.Count > 0;

    /// <summary>How many of the 20 cooldown slots hold a skill.</summary>
    public int UsedSlotCount => AllSteps().Where(step => step.CooldownSlot is not null).Select(step => step.CooldownSlot!.Value).Distinct().Count();

    /// <summary>The used slots as "16 / 20".</summary>
    public string UsedSlotsText => $"{UsedSlotCount} / {SlotAssignment.CooldownSlotCount}";

    /// <summary>The step that holds a cooldown slot, or null when the slot is empty.</summary>
    public ComboStepViewModel? StepAtSlot(int slot) => AllSteps().FirstOrDefault(step => step.CooldownSlot == slot);

    private void RaiseRemainingChanged()
    {
        RefreshSkillSlots();
        _pendingGroups = BuildPending();
        OnPropertyChanged(nameof(PendingGroups));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(UsedSlotCount));
        OnPropertyChanged(nameof(UsedSlotsText));
    }

    /// <summary>
    /// Gives every step the cooldown slots of its skill, in slot order, so a skill placed in several slots shows all of them on
    /// each of its cards. A step without a cooldown slot shows none.
    /// </summary>
    private void RefreshSkillSlots()
    {
        var steps = AllSteps().ToList();
        foreach (var step in steps)
        {
            var skillNo = step.PrimarySkill?.SkillNo;
            var slots = steps
                .Where(item => item.CooldownSlot is not null && skillNo is not null && item.PrimarySkill?.SkillNo == skillNo)
                .Select(item => item.CooldownSlot!.Value)
                .OrderBy(slot => slot)
                .ToList();
            step.SetSkillSlots(step.CooldownSlot is null || skillNo is null ? [] : slots);
        }
    }

    private List<PendingGroup> BuildPending()
    {
        var placed = AllSteps()
            .Where(step => step.CooldownSlot is not null && step.PrimarySkill is not null)
            .Select(step => step.PrimarySkill!.SkillNo)
            .ToHashSet();

        List<PendingSkill> Remaining(IEnumerable<ComboChainViewModel> chains)
        {
            var seen = new HashSet<int>();
            var items = new List<PendingSkill>();
            foreach (var step in chains.SelectMany(chain => chain.Steps))
            {
                if (step.PrimarySkill is { } skill && !placed.Contains(skill.SkillNo) && seen.Add(skill.SkillNo))
                {
                    items.Add(new PendingSkill(step, step.SkillName, step.Glyph, skill.OpenIcon));
                }
            }

            return items;
        }

        bool IsRepositioning(ComboChainViewModel chain) =>
            chain.Title.Contains("reposition", StringComparison.OrdinalIgnoreCase)
            || chain.Title.Contains("reposicion", StringComparison.OrdinalIgnoreCase);

        bool OfKind(ComboChainViewModel chain, ComboChainKind kind) => _chainKinds.TryGetValue(chain.Index, out var found) && found == kind;

        var groups = new List<PendingGroup>();
        void Add(string titleKey, List<PendingSkill> items)
        {
            if (items.Count > 0)
            {
                groups.Add(new PendingGroup(_localizer.Get(titleKey), items.Count.ToString(CultureInfo.InvariantCulture), items));
            }
        }

        Add("Apply.Remaining.GroupBuffs", Remaining(Chains.Where(chain => OfKind(chain, ComboChainKind.Buff))));
        Add("Apply.Remaining.GroupCombo", Remaining(Chains.Where(chain => OfKind(chain, ComboChainKind.Combo))));
        Add("Apply.Remaining.GroupMobility", Remaining(ManualChains.Where(chain => !IsRepositioning(chain))));
        Add("Apply.Remaining.GroupRepositioning", Remaining(ManualChains.Where(IsRepositioning)));
        return groups;
    }

    /// <summary>True when the guide has mobility skills for the manual pool.</summary>
    public bool HasManualSteps => ManualChains.Any(chain => chain.HasSteps);

    /// <summary>
    /// The class add-ons of the guide, read-only. They are information for the player: they never reach the character file.
    /// </summary>
    public IReadOnlyList<ComboAddonViewModel> Addons { get; }

    public bool HasAddons => Addons.Count > 0;

    /// <summary>Every skill of the guide, for "Add skill".</summary>
    public IReadOnlyList<SkillOption> AvailableSkills { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ComboStepViewModel? _selectedStep;

    public bool HasSelection => SelectedStep is not null;

    /// <summary>Total steps over all chains.</summary>
    public int TotalSteps => AllSteps().Count();

    /// <summary>True when the working copy was loaded from a saved profile or saved since.</summary>
    [ObservableProperty]
    private bool _hasSavedProfile;

    /// <summary>True when there are changes that are not saved yet.</summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// Builds the working copy of a guide. The profile, when given, orders the steps, drops the removed ones, adds the
    /// added ones and sets the names, memos and slots. Anything in the profile that does not fit the guide is skipped and logged.
    /// </summary>
    /// <param name="classSkills">
    /// The class skill catalog: it brings back a skill the slot editor put in a step and it is not in the guide. Null: such a
    /// saved step falls back to the guide's skill.
    /// </param>
    /// <param name="texts">The Spanish guide texts. Null: the guide stays in English.</param>
    public static ComboEditorViewModel Create(
        int classType,
        string specId,
        ComboGuide guide,
        ComboProfile? profile,
        ILocalizer localizer,
        CultureInfo culture,
        AppLogger logger,
        IChainKindResolver? kinds = null,
        ClassSkillSource? classSkills = null,
        GuideTexts? texts = null)
    {
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(logger);

        var editor = new ComboEditorViewModel(
            classType,
            specId,
            guide,
            localizer,
            culture,
            logger,
            kinds ?? TitleChainKindResolver.Instance,
            classSkills,
            (texts ?? GuideTexts.None).ForCulture(culture));
        editor.LoadChains(profile);
        return editor;
    }

    /// <summary>
    /// The profile that reproduces this working copy: every chain, in order, with the customization. A step whose skill the
    /// slot editor replaced writes that skill number, so opening the combo again shows the same skill.
    /// </summary>
    public ComboProfile ToProfile() =>
        new(
            ClassType,
            SpecId,
            AllChains().OrderBy(chain => chain.Index).Select(chain => new ComboProfileChain(
                chain.Index,
                chain.Steps.Select(step => new ComboProfileStep(
                    step.BaseStep,
                    step.IsAdded || step.HasReplacedSkill ? step.PrimarySkill?.SkillNo : null,
                    NullIfBlank(step.CustomName),
                    NullIfBlank(step.Memo),
                    step.CooldownSlot,
                    step.QuickPosition)).ToList())).ToList());

    /// <summary>Call after the profile was saved: the working copy now equals the saved profile.</summary>
    public void MarkSaved()
    {
        HasSavedProfile = true;
        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Call when the working copy no longer equals a saved combo: the saved slot it came from was deleted, or it was replaced
    /// by the combo of a character. What is on screen is kept, so the user can save it again or keep editing it.
    /// </summary>
    public void MarkNotSaved()
    {
        HasSavedProfile = false;
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Replaces the working copy with another saved combo of the same guide, as when the user loads a slot. The guide is
    /// untouched; anything the profile does not fit is skipped and logged, exactly as when the guide is opened.
    /// </summary>
    public void ApplyProfile(ComboProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        LoadChains(profile);
        ContentChanged?.Invoke(this, EventArgs.Empty);
        _logger.Info("apply.profile.applied", "Working copy replaced by a saved slot", new Dictionary<string, object?>
        {
            ["class_type"] = ClassType,
            ["spec"] = SpecId,
            ["chains"] = profile.Chains.Count,
            ["steps"] = profile.Chains.Sum(chain => chain.Steps.Count),
        });
    }

    /// <summary>
    /// Replaces the working copy with the combo that a character has in the game. The cooldown slots become steps of the
    /// guide's first combo chain in slot order; a skill that that chain has in the guide keeps the guide's step (its keys and
    /// notes), any other skill becomes a free step. Quick positions join the step of their skill, or become steps without a
    /// cooldown slot. A skill that neither the guide nor the class catalog knows is skipped and logged. The working copy is
    /// then unsaved, and the guide is untouched.
    /// </summary>
    /// <returns>How many steps were imported.</returns>
    public int ImportCharacterCombo(CharacterCombo combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        var target = FirstImportChain();
        var linked = new HashSet<int>();
        var imported = new List<ImportedStep>();
        var skipped = 0;

        foreach (var entry in combo.Cooldown.OrderBy(item => item.Slot))
        {
            if (ImportedStepFor(entry.SkillNo, target, linked, entry.Memo, entry.Slot, quickPosition: null) is { } step)
            {
                imported.Add(step);
            }
            else
            {
                skipped++;
            }
        }

        foreach (var entry in combo.Quick.OrderBy(item => item.Slot))
        {
            var holder = imported.FindIndex(item => item.SkillNo == entry.SkillNo && item.QuickPosition is null);
            if (holder >= 0)
            {
                imported[holder] = imported[holder] with { QuickPosition = entry.Slot };
            }
            else if (ImportedStepFor(entry.SkillNo, target, linked, entry.Memo, cooldownSlot: null, quickPosition: entry.Slot) is { } step)
            {
                imported.Add(step);
            }
            else
            {
                skipped++;
            }
        }

        var steps = imported
            .Select(item => new ComboProfileStep(
                item.BaseStep,
                item.BaseStep is null ? item.SkillNo : null,
                null,
                item.Memo,
                item.CooldownSlot,
                item.QuickPosition))
            .ToList();
        var chains = new List<ComboProfileChain>();
        if (target is int chainIndex)
        {
            chains.Add(new ComboProfileChain(chainIndex, steps));
        }

        ApplyProfile(new ComboProfile(ClassType, SpecId, chains));
        MarkNotSaved();

        _logger.Info("apply.import.applied", "The character's combo was imported into the working copy", new Dictionary<string, object?>
        {
            ["class_type"] = ClassType,
            ["spec"] = SpecId,
            ["cooldown_slots"] = combo.Cooldown.Count,
            ["quick_slots"] = combo.Quick.Count,
            ["steps"] = imported.Count,
            ["linked_steps"] = linked.Count,
            ["skipped"] = skipped,
        });
        return imported.Count;
    }

    /// <summary>Throws away every change and goes back to the guide. The saved profile, if any, is not touched here.</summary>
    public void ResetToBase()
    {
        _tracking = false;
        LoadChains(null);
        ContentChanged?.Invoke(this, EventArgs.Empty);
        _logger.Info("apply.guide.reset_to_base", "Working copy reset to the bdoclasshub guide", new Dictionary<string, object?>
        {
            ["class_type"] = ClassType,
            ["spec"] = SpecId,
        });
    }

    public IEnumerable<ComboStepViewModel> AllSteps() => AllChains().SelectMany(chain => chain.Steps);

    /// <summary>Every chain the editor holds: the combo chains, then the manual pool.</summary>
    private IEnumerable<ComboChainViewModel> AllChains() => Chains.Concat(ManualChains);

    [RelayCommand]
    public void SelectStep(ComboStepViewModel? step)
    {
        if (step is not null && AllChains().Any(chain => chain.Steps.Contains(step)))
        {
            SelectedStep = step;
        }
    }

    [RelayCommand]
    private void MoveEarlier() => MoveSelectedBy(-1);

    [RelayCommand]
    private void MoveLater() => MoveSelectedBy(1);

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedStep is { } step)
        {
            RemoveStep(step);
        }
    }

    [RelayCommand]
    private void ResetStep()
    {
        if (SelectedStep is not { } step)
        {
            return;
        }

        step.CustomName = null;
        step.Memo = null;
        step.CooldownSlot = null;
        step.QuickPosition = null;
        step.RestoreGuideSkill();
        _logger.Debug("apply.step.reset", "Step customization reset", new Dictionary<string, object?>
        {
            ["base_step"] = step.BaseStep,
        });
    }

    /// <summary>Moves the selected step within its chain. Does nothing at the ends of the chain.</summary>
    public void MoveSelectedBy(int delta)
    {
        if (SelectedStep is not { } step)
        {
            return;
        }

        var chain = FindChain(step);
        if (chain is null)
        {
            return;
        }

        var from = chain.Steps.IndexOf(step);
        var to = from + delta;
        if (to < 0 || to >= chain.Steps.Count)
        {
            return;
        }

        chain.Steps.Move(from, to);
        _logger.Debug("apply.step.moved", "Step moved within its chain", new Dictionary<string, object?>
        {
            ["chain"] = chain.Index,
            ["from"] = from + 1,
            ["to"] = to + 1,
        });
    }

    /// <summary>Removes a step from its chain. The selection moves to the step that takes its place.</summary>
    public void RemoveStep(ComboStepViewModel step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var chain = FindChain(step);
        if (chain is null)
        {
            return;
        }

        var index = chain.Steps.IndexOf(step);
        chain.Steps.RemoveAt(index);
        if (ReferenceEquals(SelectedStep, step))
        {
            SelectedStep = chain.Steps.Count == 0
                ? AllSteps().FirstOrDefault()
                : chain.Steps[Math.Min(index, chain.Steps.Count - 1)];
        }

        _logger.Info("apply.step.removed", "Step removed from the combo", new Dictionary<string, object?>
        {
            ["chain"] = chain.Index,
            ["added"] = step.IsAdded,
        });
    }

    /// <summary>Adds a skill at the end of a chain and selects it.</summary>
    public void AddSkill(ComboChainViewModel chain, SkillOption option)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(option);
        if (!Chains.Contains(chain))
        {
            return;
        }

        var step = new ComboStepViewModel(null, [option.Skill], null, null, null, _localizer, _culture);
        Attach(step);
        chain.Steps.Add(step);
        SelectedStep = step;
        _logger.Info("apply.step.added", "Skill added to the combo", new Dictionary<string, object?>
        {
            ["chain"] = chain.Index,
            ["skill_no"] = option.SkillNo,
        });
    }

    /// <summary>Sets a cooldown slot (1..20, or null) with the swap rule.</summary>
    public void AssignCooldown(ComboStepViewModel step, int? slot)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (slot is { } value && !SlotAssignment.IsCooldownSlot(value))
        {
            throw new ArgumentOutOfRangeException(nameof(slot), value, "Cooldown slots are 1 to 20.");
        }

        SlotAssignment.Assign(AllSteps(), step, slot, item => item.CooldownSlot, (item, value) => item.CooldownSlot = value);
    }

    /// <summary>Sets a quick bar position (1 to <see cref="QuickSlotKeys.ComboPositionCount"/>, or null) with the swap rule.</summary>
    public void AssignQuick(ComboStepViewModel step, int? position)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (position is { } value && !SlotAssignment.IsQuickPosition(value))
        {
            throw new ArgumentOutOfRangeException(nameof(position), value, $"Quick positions are 1 to {QuickSlotKeys.ComboPositionCount}.");
        }

        SlotAssignment.Assign(AllSteps(), step, position, item => item.QuickPosition, (item, value) => item.QuickPosition = value);
    }

    /// <summary>
    /// Puts a step on a board position the way a drop does. A step dragged from a chain card that holds no position on the
    /// board is placed, and a step that is moved from a cell follows the swap rule. When <see cref="DropMakesDuplicate"/> is
    /// true, the position gets a new step with the same skill instead, inserted right after the step in its chain; the step
    /// keeps its position. The guide is never touched.
    /// </summary>
    /// <returns>The step that holds the position: the duplicate when one was made, otherwise <paramref name="step"/>.</returns>
    public ComboStepViewModel DropOnBoard(ComboStepViewModel step, ComboBoard board, int position, BoardDrop how)
    {
        ArgumentNullException.ThrowIfNull(step);
        ValidatePosition(board, position);
        var chain = FindChain(step) ?? throw new ArgumentException("The step is not in this combo.", nameof(step));
        if (!DropMakesDuplicate(step, board, position, how))
        {
            SlotAssignment.Assign(AllSteps(), step, position, item => SlotOf(item, board), (item, value) => SetSlot(item, board, value));
            return step;
        }

        var copy = new ComboStepViewModel(null, [step.PrimarySkill!], null, null, null, _localizer, _culture, memo: step.Memo);
        Attach(copy);
        chain.Steps.Insert(chain.Steps.IndexOf(step) + 1, copy);
        SlotAssignment.Assign(AllSteps(), copy, position, item => SlotOf(item, board), (item, value) => SetSlot(item, board, value));
        _logger.Info("apply.step.duplicated", "A step was copied onto a board position; the original keeps its position", new Dictionary<string, object?>
        {
            ["chain"] = chain.Index,
            ["board"] = board.ToString(),
            ["position"] = position,
            ["skill_no"] = copy.PrimarySkill?.SkillNo,
        });
        return copy;
    }

    /// <summary>
    /// True when a drop of the step on the position makes a duplicate rather than a move: the step is copied (<see cref="BoardDrop.Copy"/>),
    /// or it is dragged from a chain card while it already holds a position on this board. A step dropped on the position it
    /// holds changes nothing, so it is not a duplicate either.
    /// </summary>
    public bool DropMakesDuplicate(ComboStepViewModel step, ComboBoard board, int position, BoardDrop how)
    {
        ArgumentNullException.ThrowIfNull(step);
        var held = SlotOf(step, board);
        if (held == position || step.PrimarySkill is null)
        {
            return false;
        }

        return how == BoardDrop.Copy || (how == BoardDrop.Place && held is not null);
    }

    private static void ValidatePosition(ComboBoard board, int position)
    {
        if (board == ComboBoard.Cooldown && !SlotAssignment.IsCooldownSlot(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Cooldown slots are 1 to 20.");
        }

        if (board == ComboBoard.Quick && !SlotAssignment.IsQuickPosition(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, $"Quick positions are 1 to {QuickSlotKeys.ComboPositionCount}.");
        }
    }

    private static int? SlotOf(ComboStepViewModel step, ComboBoard board) => board == ComboBoard.Cooldown ? step.CooldownSlot : step.QuickPosition;

    private static void SetSlot(ComboStepViewModel step, ComboBoard board, int? value)
    {
        if (board == ComboBoard.Cooldown)
        {
            step.CooldownSlot = value;
        }
        else
        {
            step.QuickPosition = value;
        }
    }

    /// <summary>
    /// Puts another skill in the step that holds a cooldown slot: the edit that the "Slot editor" page makes. The step keeps
    /// its place in the chain, its keys, notes, memo, quick position and custom name; only the skill changes.
    /// <para>
    /// It is a customization of the working copy, so the guide is never touched, the change counts as unsaved, the preview
    /// refreshes (<see cref="ContentChanged"/>) and <see cref="ToProfile"/> writes the skill number, which
    /// <see cref="Create"/> resolves again through the class skill catalog. False when no step holds that slot.
    /// </para>
    /// </summary>
    public bool ReplaceCooldownSkill(int cooldownSlot, ComboSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (!SlotAssignment.IsCooldownSlot(cooldownSlot))
        {
            throw new ArgumentOutOfRangeException(nameof(cooldownSlot), cooldownSlot, "Cooldown slots are 1 to 20.");
        }

        var step = AllSteps().FirstOrDefault(item => item.CooldownSlot == cooldownSlot);
        if (step is null)
        {
            _logger.Info("apply.slot_skill.no_step", "A cooldown slot with no step in the combo cannot take a skill", new Dictionary<string, object?>
            {
                ["slot"] = cooldownSlot,
            });
            return false;
        }

        step.ReplacePrimarySkill(skill);
        _skillsByNo[skill.SkillNo] = skill;
        _logger.Info("apply.slot_skill.replaced", "The skill of a cooldown slot was replaced", new Dictionary<string, object?>
        {
            ["slot"] = cooldownSlot,
            ["skill_no"] = skill.SkillNo,
            ["added_step"] = step.IsAdded,
        });
        return true;
    }

    /// <summary>Called by a chain when its steps changed (moved, added, removed).</summary>
    internal void NotifyStepsChanged()
    {
        OnPropertyChanged(nameof(TotalSteps));
        OnPropertyChanged(nameof(HasManualSteps));
        MarkChanged();
    }

    partial void OnSelectedStepChanged(ComboStepViewModel? value)
    {
        foreach (var step in AllSteps())
        {
            step.IsSelected = ReferenceEquals(step, value);
        }
    }

    private void MarkChanged()
    {
        ContentChanged?.Invoke(this, EventArgs.Empty);
        if (_tracking)
        {
            HasUnsavedChanges = true;
        }
    }

    private void Attach(ComboStepViewModel step) => step.Edited += (_, _) => MarkChanged();

    private ComboChainViewModel? FindChain(ComboStepViewModel step) =>
        AllChains().FirstOrDefault(chain => chain.Steps.Contains(step));

    /// <summary>
    /// Gives a mobility skill the lowest cooldown slot that no step holds. It does nothing when the step already has a slot
    /// or when all 20 slots are used. The skill is never added to the combo by this.
    /// </summary>
    [RelayCommand]
    public void AddToFreeSlot(ComboStepViewModel? step)
    {
        if (step is null || step.CooldownSlot is not null)
        {
            return;
        }

        var used = AllSteps().Where(item => item.CooldownSlot is not null).Select(item => item.CooldownSlot!.Value).ToHashSet();
        for (var slot = 1; slot <= SlotAssignment.CooldownSlotCount; slot++)
        {
            if (used.Contains(slot))
            {
                continue;
            }

            AssignCooldown(step, slot);
            _logger.Info("apply.slot.added_by_hand", "Manual skill given a free cooldown slot", new Dictionary<string, object?>
            {
                ["slot"] = slot,
                ["base_step"] = step.BaseStep,
            });
            return;
        }

        _logger.Info("apply.slot.none_free", "Every cooldown slot is in use; the skill was not added");
    }

    /// <summary>
    /// Fills the chains from the guide and the profile, then selects the first step. Buff chains come first, then combo
    /// chains, each in guide order. Mobility chains go to <see cref="ManualChains"/>. Cancels and other chains are not shown.
    /// </summary>
    private void LoadChains(ComboProfile? profile)
    {
        _tracking = false;

        SelectedStep = null;
        Chains.Clear();
        ManualChains.Clear();
        _chainKinds.Clear();
        var profileChains = IndexProfileChains(profile);
        var defaults = profile is null ? DefaultSlots() : new Dictionary<(int Chain, int Step), DefaultSlot>();
        var built = new List<(ComboChainKind Kind, ComboChainViewModel Chain)>();
        for (var index = 0; index < _guide.Chains.Count; index++)
        {
            var chain = _guide.Chains[index];
            var kind = _kinds.Resolve(chain);
            if (kind is ComboChainKind.Cancels or ComboChainKind.Other)
            {
                _logger.Debug("apply.chain.hidden", "Chain kind is not shown yet", new Dictionary<string, object?>
                {
                    ["chain"] = index,
                    ["kind"] = kind.ToString(),
                });
                continue;
            }

            var steps = profileChains.TryGetValue(index, out var saved)
                ? StepsFromProfile(index, chain, saved)
                : BaseSteps(index, chain, defaults);
            var title = string.IsNullOrWhiteSpace(chain.Title)
                ? _localizer.Format("Apply.ChainFallbackTitle", (index + 1).ToString(CultureInfo.InvariantCulture))
                : _translation.Text(chain.Title);
            _chainKinds[index] = kind;
            built.Add((kind, new ComboChainViewModel(this, index, title, string.Join(" · ", chain.Notes.Select(note => _translation.Text(note))), steps, AvailableSkills, _localizer)));
        }

        ClearInvalidSlots(built.SelectMany(item => item.Chain.Steps).ToList());
        foreach (var item in built.Where(item => item.Kind == ComboChainKind.Buff).Concat(built.Where(item => item.Kind == ComboChainKind.Combo)))
        {
            Chains.Add(item.Chain);
        }

        foreach (var item in built.Where(item => item.Kind == ComboChainKind.Mobility))
        {
            ManualChains.Add(item.Chain);
        }

        foreach (var step in AllSteps())
        {
            Attach(step);
        }

        HasSavedProfile = profile is not null;
        HasUnsavedChanges = false;
        SelectedStep = AllSteps().FirstOrDefault();
        OnPropertyChanged(nameof(TotalSteps));
        OnPropertyChanged(nameof(HasManualSteps));
        RaiseRemainingChanged();
        _tracking = true;

        _logger.Debug("apply.guide.texts", "Guide prose translated", new Dictionary<string, object?>
        {
            ["missing_translations"] = _translation.MissingCount,
        });
        _logger.Info("apply.guide.loaded", "Working copy built", new Dictionary<string, object?>
        {
            ["class_type"] = ClassType,
            ["spec"] = SpecId,
            ["chains"] = Chains.Count,
            ["manual_chains"] = ManualChains.Count,
            ["steps"] = TotalSteps,
            ["from_profile"] = profile is not null,
        });
    }

    private Dictionary<int, ComboProfileChain> IndexProfileChains(ComboProfile? profile)
    {
        var result = new Dictionary<int, ComboProfileChain>();
        if (profile is null)
        {
            return result;
        }

        foreach (var chain in profile.Chains)
        {
            if (!result.TryAdd(chain.Chain, chain))
            {
                _logger.Warn("apply.profile.duplicate_chain", "The saved profile lists a chain twice; the first one is used", new Dictionary<string, object?>
                {
                    ["chain"] = chain.Chain,
                });
            }
        }

        return result;
    }

    private List<ComboStepViewModel> BaseSteps(int chainIndex, ComboChain chain, IReadOnlyDictionary<(int Chain, int Step), DefaultSlot> defaults) =>
        chain.Steps
            .Select((step, index) =>
            {
                if (defaults.TryGetValue((chainIndex, index), out var assigned))
                {
                    return new ComboStepViewModel(
                        index,
                        step.Skills,
                        step.Keys,
                        _translation.Text(step.Extra),
                        _translation.Text(step.Note),
                        _localizer,
                        _culture,
                        cooldownSlot: assigned.Slot,
                        memo: assigned.Memo);
                }

                return new ComboStepViewModel(index, step.Skills, step.Keys, _translation.Text(step.Extra), _translation.Text(step.Note), _localizer, _culture);
            })
            .ToList();

    /// <summary>A default cooldown slot of a guide step, with the key legend of the step as its memo (null when none).</summary>
    private sealed record DefaultSlot(int Slot, string? Memo);

    /// <summary>
    /// The default layout of a guide with no saved profile. Only buff and combo chains take part (mobility is a manual
    /// pool). The main combo (the first combo chain of the guide) fills cooldown slots 1..20 in guide order, so none of its
    /// steps is lost. The steps of the other combos continue up to slot 12, then the buff steps take the slots from 13 (or
    /// right after the main combo when it is longer) up to 20. Every step takes its own slot, even when its skill repeats
    /// (a skill cast again at the end of the combo keeps its second slot). The key legend of the step becomes the memo,
    /// except the player's own key ("Hotkey" or "Hotbar").
    /// </summary>
    private Dictionary<(int Chain, int Step), DefaultSlot> DefaultSlots()
    {
        var placements = new List<(int Chain, int Step, ComboChainKind Kind, string? Keys)>();
        var mainComboChain = -1;
        for (var chainIndex = 0; chainIndex < _guide.Chains.Count; chainIndex++)
        {
            var chain = _guide.Chains[chainIndex];
            var kind = _kinds.Resolve(chain);
            if (kind is not (ComboChainKind.Combo or ComboChainKind.Buff))
            {
                continue;
            }

            if (kind == ComboChainKind.Combo && mainComboChain < 0)
            {
                mainComboChain = chainIndex;
            }

            for (var stepIndex = 0; stepIndex < chain.Steps.Count; stepIndex++)
            {
                if (chain.Steps[stepIndex].Skills.Count > 0)
                {
                    placements.Add((chainIndex, stepIndex, kind, chain.Steps[stepIndex].Keys));
                }
            }
        }

        var result = new Dictionary<(int Chain, int Step), DefaultSlot>();
        var nextComboSlot = FirstComboSlot;
        var nextBuffSlot = 0;
        var ordered = placements
            .OrderBy(item => item.Chain == mainComboChain ? 0 : item.Kind == ComboChainKind.Combo ? 1 : 2);
        foreach (var placement in ordered)
        {
            int slot;
            if (placement.Kind == ComboChainKind.Combo)
            {
                var lastSlot = placement.Chain == mainComboChain ? SlotAssignment.CooldownSlotCount : LastComboSlot;
                if (nextComboSlot > lastSlot)
                {
                    continue;
                }

                slot = nextComboSlot++;
            }
            else
            {
                if (nextBuffSlot == 0)
                {
                    nextBuffSlot = Math.Max(FirstBuffSlot, nextComboSlot);
                }

                if (nextBuffSlot > SlotAssignment.CooldownSlotCount)
                {
                    continue;
                }

                slot = nextBuffSlot++;
            }

            var keys = placement.Keys?.Trim();
            var memo = string.IsNullOrEmpty(keys) || ComboStepViewModel.IsPlayerKeyLegend(keys)
                ? null
                : keys;
            result[(placement.Chain, placement.Step)] = new DefaultSlot(slot, memo);
        }

        return result;
    }

    private List<ComboStepViewModel> StepsFromProfile(int chainIndex, ComboChain chain, ComboProfileChain saved)
    {
        var used = new HashSet<int>();
        var steps = new List<ComboStepViewModel>();
        foreach (var entry in saved.Steps)
        {
            if (entry.BaseStep is { } baseStep)
            {
                if (baseStep < 0 || baseStep >= chain.Steps.Count || !used.Add(baseStep))
                {
                    SkipProfileStep(chainIndex, "base_step");
                    continue;
                }

                var guideStep = chain.Steps[baseStep];
                var step = new ComboStepViewModel(
                    baseStep,
                    guideStep.Skills,
                    guideStep.Keys,
                    _translation.Text(guideStep.Extra),
                    _translation.Text(guideStep.Note),
                    _localizer,
                    _culture,
                    entry.CustomName,
                    entry.Memo,
                    entry.CooldownSlot,
                    entry.QuickPosition);
                RestoreReplacedSkill(step, entry.SkillNo);
                steps.Add(step);
            }
            else if (entry.SkillNo is { } skillNo && ResolveSkill(skillNo) is { } skill)
            {
                steps.Add(new ComboStepViewModel(null, [skill], null, null, null, _localizer, _culture, entry.CustomName, entry.Memo, entry.CooldownSlot, entry.QuickPosition));
            }
            else
            {
                SkipProfileStep(chainIndex, "skill");
            }
        }

        return steps;
    }

    private void SkipProfileStep(int chainIndex, string reason) =>
        _logger.Warn("apply.profile.step_skipped", "A saved step does not match the guide and is skipped", new Dictionary<string, object?>
        {
            ["chain"] = chainIndex,
            ["reason"] = reason,
        });

    /// <summary>
    /// A saved step of the guide may name another skill: the one the slot editor put there. It is resolved through the guide
    /// first and the class skill catalog second; when neither has it, the guide's own skill stays and the difference is logged.
    /// </summary>
    private void RestoreReplacedSkill(ComboStepViewModel step, int? savedSkillNo)
    {
        if (savedSkillNo is not { } skillNo || step.PrimarySkill?.SkillNo == skillNo)
        {
            return;
        }

        if (ResolveSkill(skillNo) is { } skill)
        {
            step.ReplacePrimarySkill(skill);
            return;
        }

        _logger.Warn("apply.profile.skill_skipped", "A saved step names a skill that is not in the guide or the class catalog; the guide's skill stays", new Dictionary<string, object?>
        {
            ["skill_no"] = skillNo,
        });
    }

    /// <summary>A step built by the import, before it becomes a profile step.</summary>
    private sealed record ImportedStep(int SkillNo, int? BaseStep, string? Memo, int? CooldownSlot, int? QuickPosition);

    /// <summary>
    /// The chain the imported steps go to: the guide's first combo chain, or its first buff chain when it has no combo chain.
    /// Null when the guide has neither, so nothing can be placed.
    /// </summary>
    private int? FirstImportChain()
    {
        var kinds = _guide.Chains.Select(chain => _kinds.Resolve(chain)).ToList();
        var combo = kinds.IndexOf(ComboChainKind.Combo);
        if (combo >= 0)
        {
            return combo;
        }

        var buff = kinds.IndexOf(ComboChainKind.Buff);
        return buff >= 0 ? buff : null;
    }

    /// <summary>
    /// The step for one skill of the character, or null when the skill is unknown (it is logged and skipped). A guide step of
    /// the target chain with that skill is linked (the step keeps its keys and notes) unless an earlier import step took it.
    /// </summary>
    private ImportedStep? ImportedStepFor(int skillNo, int? target, HashSet<int> linked, string? memo, int? cooldownSlot, int? quickPosition)
    {
        var baseStep = FindImportBaseStep(target, skillNo, linked);
        if (baseStep is null && ResolveSkill(skillNo) is null)
        {
            _logger.Warn("apply.import.skill_unknown", "A skill of the character is in neither the guide nor the class catalog; it is skipped", new Dictionary<string, object?>
            {
                ["skill_no"] = skillNo,
            });
            return null;
        }

        if (baseStep is int index)
        {
            linked.Add(index);
        }

        return new ImportedStep(skillNo, baseStep, NullIfBlank(memo), cooldownSlot, quickPosition);
    }

    /// <summary>The index of the guide step of the target chain whose skill is <paramref name="skillNo"/>, not taken yet. Null when none.</summary>
    private int? FindImportBaseStep(int? target, int skillNo, HashSet<int> linked)
    {
        if (target is not int chainIndex)
        {
            return null;
        }

        var steps = _guide.Chains[chainIndex].Steps;
        for (var index = 0; index < steps.Count; index++)
        {
            if (!linked.Contains(index) && steps[index].Skills.Count > 0 && steps[index].Skills[0].SkillNo == skillNo)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>The skill of the guide or, failing that, of the class catalog of this class. Null when both do not know it.</summary>
    private ComboSkill? ResolveSkill(int skillNo)
    {
        if (_skillsByNo.TryGetValue(skillNo, out var known))
        {
            return known;
        }

        var resolved = _classSkills?.Find(ClassType, skillNo);
        if (resolved is not null)
        {
            _skillsByNo[skillNo] = resolved;
        }

        return resolved;
    }

    /// <summary>Clears cooldown slots and quick positions that are out of range or held twice (the first holder keeps them).</summary>
    private void ClearInvalidSlots(IReadOnlyList<ComboStepViewModel> steps)
    {
        var cooldowns = new HashSet<int>();
        var quick = new HashSet<int>();
        foreach (var step in steps)
        {
            if (step.CooldownSlot is { } slot && (!SlotAssignment.IsCooldownSlot(slot) || !cooldowns.Add(slot)))
            {
                _logger.Warn("apply.profile.slot_cleared", "A saved cooldown slot is invalid or used twice and is cleared", new Dictionary<string, object?>
                {
                    ["slot"] = slot,
                });
                step.CooldownSlot = null;
            }

            if (step.QuickPosition is { } position && (!SlotAssignment.IsQuickPosition(position) || !quick.Add(position)))
            {
                _logger.Warn("apply.profile.slot_cleared", "A saved quick position is invalid or used twice and is cleared", new Dictionary<string, object?>
                {
                    ["quick_position"] = position,
                });
                step.QuickPosition = null;
            }
        }
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static IReadOnlyList<SkillOption> BuildAvailableSkills(ComboGuide guide, CultureInfo culture)
    {
        var byNo = new Dictionary<int, SkillOption>();
        void Add(ComboSkill skill)
        {
            if (!byNo.ContainsKey(skill.SkillNo))
            {
                byNo[skill.SkillNo] = new SkillOption(skill, skill.Name.For(culture));
            }
        }

        foreach (var chain in guide.Chains)
        {
            foreach (var step in chain.Steps)
            {
                foreach (var skill in step.Skills)
                {
                    Add(skill);
                }
            }
        }

        foreach (var note in guide.Notes)
        {
            foreach (var skill in note.Skills)
            {
                Add(skill);
            }
        }

        foreach (var addon in guide.Addons)
        {
            Add(addon.Skill);
        }

        return byNo.Values
            .OrderBy(option => option.Name, StringComparer.Create(culture, ignoreCase: true))
            .ToList();
    }
}
