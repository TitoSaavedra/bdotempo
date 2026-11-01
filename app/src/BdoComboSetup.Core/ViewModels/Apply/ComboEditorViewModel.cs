using System.Collections.ObjectModel;
using System.Globalization;
using BdoComboSetup.Core.Combo;
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

    /// <summary>The last cooldown slot that the default layout gives to combo steps; buffs use the slots after it.</summary>
    private const int LastComboSlot = 12;

    /// <summary>The first cooldown slot of the default layout's buff steps.</summary>
    private const int FirstBuffSlot = 13;

    private readonly ComboGuide _guide;
    private readonly ILocalizer _localizer;
    private readonly CultureInfo _culture;
    private readonly AppLogger _logger;
    private readonly IChainKindResolver _kinds;
    private readonly ClassSkillSource? _classSkills;
    private readonly Dictionary<int, ComboSkill> _skillsByNo;
    private bool _tracking;

    private ComboEditorViewModel(
        int classType,
        string specId,
        ComboGuide guide,
        ILocalizer localizer,
        CultureInfo culture,
        AppLogger logger,
        IChainKindResolver kinds,
        ClassSkillSource? classSkills)
    {
        ClassType = classType;
        SpecId = specId;
        _guide = guide;
        _localizer = localizer;
        _culture = culture;
        _logger = logger;
        _kinds = kinds;
        _classSkills = classSkills;

        AvailableSkills = BuildAvailableSkills(guide, culture);
        _skillsByNo = AvailableSkills.ToDictionary(option => option.SkillNo, option => option.Skill);
        Addons = guide.Addons.Select(addon => new ComboAddonViewModel(addon, culture)).ToList();
        CooldownOptions = BuildCooldownOptions(localizer);
        QuickOptions = BuildQuickOptions(localizer);
        Chains = new ObservableCollection<ComboChainViewModel>();
        ManualChains = new ObservableCollection<ComboChainViewModel>();
    }

    /// <summary>
    /// Raised whenever the content of the working copy changes: a step edited, moved, added or removed, or the guide reset.
    /// Unlike unsaved changes, it is raised from the first load on. The Preview page follows it.
    /// </summary>
    public event EventHandler? ContentChanged;

    public int ClassType { get; }

    public string SpecId { get; }

    public string Title => _guide.Title;

    public string? Description => _guide.Description;

    /// <summary>The host of the guide page on bdoclasshub.com, shown as the source. Empty when the guide has none.</summary>
    public string SourceHost => Uri.TryCreate(_guide.Source, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

    /// <summary>The buff and combo chains, buffs first. These are the steps of the combo.</summary>
    public ObservableCollection<ComboChainViewModel> Chains { get; }

    /// <summary>
    /// The mobility chains: a manual pool. Their skills go to a cooldown slot only by hand (see <see cref="AddToFreeSlot"/>),
    /// never with the combo.
    /// </summary>
    public ObservableCollection<ComboChainViewModel> ManualChains { get; }

    /// <summary>True when the guide has mobility skills for the manual pool.</summary>
    public bool HasManualSteps => ManualChains.Any(chain => chain.HasSteps);

    /// <summary>
    /// The class add-ons of the guide, read-only. They are information for the player: they never reach the character file.
    /// </summary>
    public IReadOnlyList<ComboAddonViewModel> Addons { get; }

    public bool HasAddons => Addons.Count > 0;

    /// <summary>Every skill of the guide, for "Add skill".</summary>
    public IReadOnlyList<SkillOption> AvailableSkills { get; }

    /// <summary>The cooldown slot choices: none, then 1..20.</summary>
    public IReadOnlyList<SlotOption> CooldownOptions { get; }

    /// <summary>The quick position choices: none, then the nine positions with their input labels.</summary>
    public IReadOnlyList<SlotOption> QuickOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedCooldownOption), nameof(SelectedQuickOption))]
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
    /// The cooldown slot of the selected step, as the ComboBox sees it. Setting it applies the swap rule. The ComboBox two-way
    /// binding writes back what it shows, so a write is applied only when it is a real choice (a null is the ComboBox having no
    /// item yet, and "none" is an item of its own) and the value differs: otherwise the change notification would set the
    /// ComboBox again and loop.
    /// </summary>
    public SlotOption? SelectedCooldownOption
    {
        get => SelectedStep is null ? null : CooldownOptions.First(option => option.Slot == SelectedStep.CooldownSlot);
        set
        {
            if (value is null || SelectedStep is not { } step || value.Slot == step.CooldownSlot)
            {
                return;
            }

            AssignCooldown(step, value.Slot);
        }
    }

    /// <summary>
    /// The quick position of the selected step, as the ComboBox sees it. Setting it applies the swap rule. Same write-back
    /// rule as <see cref="SelectedCooldownOption"/>: null and an unchanged value are ignored.
    /// </summary>
    public SlotOption? SelectedQuickOption
    {
        get => SelectedStep is null ? null : QuickOptions.First(option => option.Slot == SelectedStep.QuickPosition);
        set
        {
            if (value is null || SelectedStep is not { } step || value.Slot == step.QuickPosition)
            {
                return;
            }

            AssignQuick(step, value.Slot);
        }
    }

    /// <summary>
    /// Builds the working copy of a guide. The profile, when given, orders the steps, drops the removed ones, adds the
    /// added ones and sets the names, memos and slots. Anything in the profile that does not fit the guide is skipped and logged.
    /// </summary>
    /// <param name="classSkills">
    /// The class skill catalog: it brings back a skill the slot editor put in a step and it is not in the guide. Null: such a
    /// saved step falls back to the guide's skill.
    /// </param>
    public static ComboEditorViewModel Create(
        int classType,
        string specId,
        ComboGuide guide,
        ComboProfile? profile,
        ILocalizer localizer,
        CultureInfo culture,
        AppLogger logger,
        IChainKindResolver? kinds = null,
        ClassSkillSource? classSkills = null)
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
            classSkills);
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
    /// Call when the saved slot this working copy came from was deleted: what is on screen is no longer saved anywhere.
    /// The working copy itself is kept, so the user can save it again or keep editing it.
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
        OnPropertyChanged(nameof(SelectedCooldownOption));
    }

    /// <summary>Sets a quick bar position (1..9, or null) with the swap rule.</summary>
    public void AssignQuick(ComboStepViewModel step, int? position)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (position is { } value && !SlotAssignment.IsQuickPosition(value))
        {
            throw new ArgumentOutOfRangeException(nameof(position), value, "Quick positions are 1 to 9.");
        }

        SlotAssignment.Assign(AllSteps(), step, position, item => item.QuickPosition, (item, value) => item.QuickPosition = value);
        OnPropertyChanged(nameof(SelectedQuickOption));
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
                : chain.Title;
            built.Add((kind, new ComboChainViewModel(this, index, title, string.Join(" · ", chain.Notes), steps, AvailableSkills, _localizer)));
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
        _tracking = true;

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
                        step.Extra,
                        step.Note,
                        _localizer,
                        _culture,
                        cooldownSlot: assigned.Slot,
                        memo: assigned.Memo);
                }

                return new ComboStepViewModel(index, step.Skills, step.Keys, step.Extra, step.Note, _localizer, _culture);
            })
            .ToList();

    /// <summary>A default cooldown slot of a guide step, with the key legend of the step as its memo (null when none).</summary>
    private sealed record DefaultSlot(int Slot, string? Memo);

    /// <summary>
    /// The default layout of a guide with no saved profile. Only buff and combo chains take part (mobility is a manual
    /// pool): combo steps fill cooldown slots 1..12 in guide order, then buff steps fill 13..20. A skill takes one slot,
    /// at its first step. The key legend of the step becomes the memo, except the player's own key ("Hotkey" or "Hotbar").
    /// </summary>
    private Dictionary<(int Chain, int Step), DefaultSlot> DefaultSlots()
    {
        var placements = new List<(int Chain, int Step, ComboChainKind Kind, ComboSkill Skill, string? Keys)>();
        for (var chainIndex = 0; chainIndex < _guide.Chains.Count; chainIndex++)
        {
            var chain = _guide.Chains[chainIndex];
            var kind = _kinds.Resolve(chain);
            if (kind is not (ComboChainKind.Combo or ComboChainKind.Buff))
            {
                continue;
            }

            for (var stepIndex = 0; stepIndex < chain.Steps.Count; stepIndex++)
            {
                if (chain.Steps[stepIndex].Skills.Count > 0)
                {
                    placements.Add((chainIndex, stepIndex, kind, chain.Steps[stepIndex].Skills[0], chain.Steps[stepIndex].Keys));
                }
            }
        }

        var result = new Dictionary<(int Chain, int Step), DefaultSlot>();
        var placedSkills = new HashSet<int>();
        var nextComboSlot = FirstComboSlot;
        var nextBuffSlot = FirstBuffSlot;
        foreach (var placement in placements.OrderBy(item => item.Kind == ComboChainKind.Combo ? 0 : 1))
        {
            if (!placedSkills.Add(placement.Skill.SkillNo))
            {
                continue;
            }

            int slot;
            if (placement.Kind == ComboChainKind.Combo)
            {
                if (nextComboSlot > LastComboSlot)
                {
                    continue;
                }

                slot = nextComboSlot++;
            }
            else
            {
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
                    guideStep.Extra,
                    guideStep.Note,
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

    private static IReadOnlyList<SlotOption> BuildCooldownOptions(ILocalizer localizer)
    {
        var options = new List<SlotOption> { new(null, localizer.Get("Apply.OptionNone")) };
        for (var slot = 1; slot <= SlotAssignment.CooldownSlotCount; slot++)
        {
            options.Add(new SlotOption(slot, slot.ToString(CultureInfo.InvariantCulture)));
        }

        return options;
    }

    private static IReadOnlyList<SlotOption> BuildQuickOptions(ILocalizer localizer)
    {
        var options = new List<SlotOption> { new(null, localizer.Get("Apply.OptionNone")) };
        foreach (var input in QuickSlotMap.Inputs)
        {
            var label = input.MouseButton ?? input.Position.ToString(CultureInfo.InvariantCulture);
            options.Add(new SlotOption(input.Position, localizer.Format("Apply.QuickPositionOption", input.Position, label)));
        }

        return options;
    }
}
