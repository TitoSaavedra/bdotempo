using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

/// <summary>The working copy of a guide: order, selection, add and remove, slot rules, chain kinds, and the profile it saves to.</summary>
public sealed class ComboEditorViewModelTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    /// <summary>The tamer guide: chain 0 "Damage Combo" (3 steps), chain 1 "Auto buffs" (1 step, a buff chain, shown first).</summary>
    private ComboEditorViewModel Create(ComboProfile? profile = null, IChainKindResolver? kinds = null) => ComboEditorViewModel.Create(
        16,
        "awakening",
        ComboTestData.TamerGuide(),
        profile,
        new KeyLocalizer(),
        ApplyTestHelpers.Culture,
        _log.Logger,
        kinds);

    private ComboEditorViewModel CreateWithMobility(ComboProfile? profile = null) => ComboEditorViewModel.Create(
        16,
        "awakening",
        ComboTestData.GuideWithMobility(),
        profile,
        new KeyLocalizer(),
        ApplyTestHelpers.Culture,
        _log.Logger);

    private static ComboChainViewModel Damage(ComboEditorViewModel editor) => editor.Chains.Single(chain => chain.Index == 0);

    private static ComboChainViewModel Buffs(ComboEditorViewModel editor) => editor.Chains.Single(chain => chain.Index == 1);

    private static ComboProfile ProfileOf(params ComboProfileChain[] chains) => new(16, "awakening", chains);

    private static ComboProfileChain ChainOf(ComboProfile profile, int index) => profile.Chains.Single(chain => chain.Chain == index);

    /// <summary>The skill numbers of a chain's steps, in order, as "100,200,300".</summary>
    private static string SkillsOf(ComboChainViewModel chain) => string.Join(",", chain.Steps.Select(step => step.Skills[0].SkillNo));

    /// <summary>The base step indices of a saved chain, as "0,2". "-" marks a step the user added.</summary>
    private static string BasesOf(ComboProfileChain chain) =>
        string.Join(",", chain.Steps.Select(step => step.BaseStep?.ToString() ?? "-"));

    /// <summary>One line per saved step, so profiles compare by value (the record lists compare by reference).</summary>
    private static IReadOnlyList<string> Describe(ComboProfile profile)
    {
        var lines = new List<string>();
        foreach (var chain in profile.Chains)
        {
            lines.Add($"chain {chain.Chain}");
            foreach (var step in chain.Steps)
            {
                lines.Add(string.Join("|",
                    step.BaseStep?.ToString() ?? "-",
                    step.SkillNo?.ToString() ?? "-",
                    step.CustomName ?? "-",
                    step.Memo ?? "-",
                    step.CooldownSlot?.ToString() ?? "-",
                    step.QuickPosition?.ToString() ?? "-"));
            }
        }

        return lines;
    }

    [Fact]
    public void A_new_editor_shows_buff_chains_first_then_combo_chains_and_selects_the_first_step()
    {
        var editor = Create();

        Assert.Equal("Tamer Awakening", editor.Title);
        Assert.Equal([1, 0], editor.Chains.Select(chain => chain.Index));
        Assert.Equal(["Auto buffs", "Damage Combo"], editor.Chains.Select(chain => chain.Title));
        Assert.Equal([1, 3], editor.Chains.Select(chain => chain.Steps.Count));
        Assert.Equal(4, editor.TotalSteps);
        Assert.Same(Buffs(editor).Steps[0], editor.SelectedStep);
        Assert.True(editor.HasSelection);
        Assert.False(editor.HasSavedProfile);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Empty(editor.ManualChains);
        Assert.False(editor.HasManualSteps);
    }

    [Fact]
    public void Steps_are_numbered_from_one_and_the_last_step_has_no_arrow()
    {
        var editor = Create();

        var damage = Damage(editor).Steps;
        Assert.Equal([1, 2, 3], damage.Select(step => step.Position));
        Assert.Equal([false, false, true], damage.Select(step => step.IsLast));
        Assert.Equal(1, Buffs(editor).Steps[0].Position);
        Assert.True(Buffs(editor).Steps[0].IsLast);
    }

    [Fact]
    public void Selecting_a_step_selects_only_that_step()
    {
        var editor = Create();
        var target = Damage(editor).Steps[1];

        editor.SelectStepCommand.Execute(target);

        Assert.Same(target, editor.SelectedStep);
        Assert.True(target.IsSelected);
        Assert.Equal(1, editor.AllSteps().Count(step => step.IsSelected));
    }

    [Fact]
    public void Selecting_a_step_that_is_not_in_the_editor_does_nothing()
    {
        var editor = Create();
        var before = editor.SelectedStep;
        var stranger = new ComboStepViewModel(0, [ComboTestData.Skill(900, "Other", "Otra")], null, null, null, new KeyLocalizer(), ApplyTestHelpers.Culture);

        editor.SelectStepCommand.Execute(stranger);

        Assert.Same(before, editor.SelectedStep);
    }

    [Fact]
    public void Moving_a_step_later_and_earlier_changes_the_order_and_the_numbers()
    {
        var editor = Create();
        var damage = Damage(editor);
        editor.SelectStepCommand.Execute(damage.Steps[0]);

        editor.MoveLaterCommand.Execute(null);

        Assert.Equal("200,100,300", SkillsOf(damage));
        Assert.Equal([1, 2, 3], damage.Steps.Select(step => step.Position));
        Assert.True(editor.HasUnsavedChanges);

        editor.MoveEarlierCommand.Execute(null);
        Assert.Equal("100,200,300", SkillsOf(damage));
    }

    [Fact]
    public void Moving_past_the_ends_of_a_chain_does_nothing()
    {
        var editor = Create();
        var damage = Damage(editor);
        editor.SelectStepCommand.Execute(damage.Steps[0]);

        editor.MoveEarlierCommand.Execute(null);

        Assert.Equal("100,200,300", SkillsOf(damage));
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void A_reorder_from_drag_and_drop_is_tracked_and_saved_in_the_new_order()
    {
        var editor = Create();
        var damage = Damage(editor);

        // The list view moves the item inside the collection; the chain then renumbers and marks the copy as changed.
        damage.Steps.Move(0, 2);

        Assert.Equal("200,300,100", SkillsOf(damage));
        Assert.Equal([1, 2, 3], damage.Steps.Select(step => step.Position));
        Assert.True(editor.HasUnsavedChanges);
        Assert.Equal("1,2,0", BasesOf(ChainOf(editor.ToProfile(), 0)));
    }

    [Fact]
    public void Removing_the_selected_step_selects_the_step_that_takes_its_place()
    {
        var editor = Create();
        var damage = Damage(editor);
        editor.SelectStepCommand.Execute(damage.Steps[1]);

        editor.RemoveSelectedCommand.Execute(null);

        Assert.Equal("100,300", SkillsOf(damage));
        Assert.Equal(300, editor.SelectedStep?.Skills[0].SkillNo);
        Assert.Equal(3, editor.TotalSteps);
        Assert.True(editor.HasUnsavedChanges);
        Assert.Equal("0,2", BasesOf(ChainOf(editor.ToProfile(), 0)));
    }

    [Fact]
    public void Removing_the_last_step_of_a_chain_selects_the_previous_one()
    {
        var editor = Create();
        var damage = Damage(editor);
        editor.SelectStepCommand.Execute(damage.Steps[2]);

        editor.RemoveSelectedCommand.Execute(null);

        Assert.Same(damage.Steps[1], editor.SelectedStep);
    }

    [Fact]
    public void Removing_every_step_of_a_chain_leaves_it_empty_and_saves_it_empty()
    {
        var editor = Create();
        var buffs = Buffs(editor);
        editor.RemoveStep(buffs.Steps[0]);

        Assert.False(buffs.HasSteps);
        Assert.Equal(3, editor.TotalSteps);
        Assert.Empty(ChainOf(editor.ToProfile(), 1).Steps);
    }

    [Fact]
    public void Reset_step_clears_the_customization_of_the_selected_step_only()
    {
        var editor = Create();
        var damage = Damage(editor);
        var step = damage.Steps[0];
        step.CustomName = "Opener";
        step.Memo = "R 1";
        editor.AssignCooldown(step, 4);
        editor.AssignQuick(step, 6);
        var other = damage.Steps[1];
        other.Memo = "keep";
        editor.SelectStepCommand.Execute(step);

        editor.ResetStepCommand.Execute(null);

        Assert.Null(step.CustomName);
        Assert.Null(step.Memo);
        Assert.Null(step.CooldownSlot);
        Assert.Null(step.QuickPosition);
        Assert.Equal("keep", other.Memo);
    }

    [Fact]
    public void Add_skill_appends_an_added_step_to_the_chain_and_selects_it()
    {
        var editor = Create();
        var damage = Damage(editor);
        var option = editor.AvailableSkills.Single(item => item.SkillNo == 500);

        damage.AddSkillCommand.Execute(option);

        var added = damage.Steps[^1];
        Assert.Equal(4, damage.Steps.Count);
        Assert.True(added.IsAdded);
        Assert.Same(added, editor.SelectedStep);
        Assert.Equal(500, added.PrimarySkill?.SkillNo);
        Assert.True(editor.HasUnsavedChanges);

        var saved = ChainOf(editor.ToProfile(), 0).Steps[^1];
        Assert.Null(saved.BaseStep);
        Assert.Equal(500, saved.SkillNo);
    }

    [Fact]
    public void Skill_search_filters_the_add_list_by_name_or_number()
    {
        var editor = Create();
        var damage = Damage(editor);

        damage.SkillSearch = "moon";
        Assert.Equal([200], damage.FilteredSkills.Select(option => option.SkillNo));

        damage.SkillSearch = "300";
        Assert.Equal([300], damage.FilteredSkills.Select(option => option.SkillNo));

        damage.SkillSearch = string.Empty;
        Assert.Equal(editor.AvailableSkills.Count, damage.FilteredSkills.Count);
    }

    [Fact]
    public void Available_skills_include_the_add_on_skills_and_are_sorted_by_name()
    {
        var editor = Create();

        Assert.Contains(editor.AvailableSkills, option => option.SkillNo == 500);
        var names = editor.AvailableSkills.Select(option => option.Name).ToList();
        Assert.Equal(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact]
    public void Assigning_a_free_cooldown_slot_just_sets_it()
    {
        var editor = Create();
        var step = Damage(editor).Steps[0];

        editor.AssignCooldown(step, 5);

        Assert.Equal(5, step.CooldownSlot);
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public void Assigning_a_cooldown_slot_held_by_another_step_moves_that_step_to_the_old_slot()
    {
        var editor = Create();
        var first = Damage(editor).Steps[0];
        var second = Damage(editor).Steps[1];
        editor.AssignCooldown(first, 1);
        editor.AssignCooldown(second, 2);

        editor.AssignCooldown(second, 1);

        Assert.Equal(2, first.CooldownSlot);
        Assert.Equal(1, second.CooldownSlot);
    }

    [Fact]
    public void Assigning_a_slot_held_by_another_step_to_a_step_without_one_frees_the_other_step()
    {
        var editor = Create();
        var first = Damage(editor).Steps[0];
        var second = Buffs(editor).Steps[0];
        editor.AssignCooldown(first, 7);

        editor.AssignCooldown(second, 7);

        Assert.Null(first.CooldownSlot);
        Assert.Equal(7, second.CooldownSlot);
    }

    [Fact]
    public void Clearing_a_cooldown_slot_sets_it_to_none()
    {
        var editor = Create();
        var step = Damage(editor).Steps[0];
        editor.AssignCooldown(step, 9);

        editor.AssignCooldown(step, null);

        Assert.Null(step.CooldownSlot);
    }

    [Fact]
    public void Quick_positions_follow_the_same_swap_rule()
    {
        var editor = Create();
        var first = Damage(editor).Steps[0];
        var second = Damage(editor).Steps[1];
        editor.AssignQuick(first, 3);
        editor.AssignQuick(second, 4);

        editor.AssignQuick(second, 3);

        Assert.Equal(4, first.QuickPosition);
        Assert.Equal(3, second.QuickPosition);
    }

    [Fact]
    public void Cooldown_slots_outside_1_to_20_and_quick_positions_outside_1_to_9_are_rejected()
    {
        var editor = Create();
        var step = Damage(editor).Steps[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AssignCooldown(step, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AssignCooldown(step, 21));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.AssignQuick(step, 10));
        Assert.Null(step.CooldownSlot);
        Assert.Null(step.QuickPosition);
    }

    [Fact]
    public void The_selected_slot_options_apply_the_swap_rule_to_the_selected_step()
    {
        var editor = Create();
        var first = Damage(editor).Steps[0];
        var second = Damage(editor).Steps[1];
        editor.AssignCooldown(first, 1);
        editor.SelectStepCommand.Execute(second);

        editor.SelectedCooldownOption = editor.CooldownOptions.Single(option => option.Slot == 1);

        Assert.Equal(1, second.CooldownSlot);
        Assert.Null(first.CooldownSlot);
        Assert.Equal(1, editor.SelectedCooldownOption?.Slot);
    }

    [Fact]
    public void Quick_options_are_none_then_positions_1_to_9_and_cooldown_options_are_none_then_1_to_20()
    {
        var editor = Create();

        Assert.Null(editor.QuickOptions[0].Slot);
        Assert.Equal(Enumerable.Range(1, 9), editor.QuickOptions.Skip(1).Select(option => option.Slot!.Value));
        Assert.Null(editor.CooldownOptions[0].Slot);
        Assert.Equal(Enumerable.Range(1, 20), editor.CooldownOptions.Skip(1).Select(option => option.Slot!.Value));
    }

    [Fact]
    public void The_profile_round_trips_order_removals_additions_and_customization()
    {
        var profile = ProfileOf(
            new ComboProfileChain(0,
            [
                new ComboProfileStep(2, null, "Ultimate", "R 1", 1, 3),
                new ComboProfileStep(0, null, null, null, 2, null),
                new ComboProfileStep(null, 500, "Skybound", null, null, 4),
            ]),
            new ComboProfileChain(1, []));
        var editor = Create(profile);

        var saved = editor.ToProfile();

        Assert.Equal(Describe(profile), Describe(saved));
        Assert.True(editor.HasSavedProfile);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void Saving_a_profile_and_rebuilding_from_it_gives_the_same_working_copy()
    {
        var editor = Create();
        var damage = Damage(editor);
        damage.Steps.Move(0, 2);
        editor.AssignCooldown(damage.Steps[0], 12);
        editor.AssignQuick(damage.Steps[1], 8);
        damage.Steps[2].Memo = "E";

        var profile = editor.ToProfile();
        var rebuilt = Create(profile);

        Assert.Equal(Describe(profile), Describe(rebuilt.ToProfile()));
    }

    [Fact]
    public void Profile_entries_that_do_not_fit_the_guide_are_skipped_and_bad_slots_are_cleared()
    {
        var profile = ProfileOf(
            new ComboProfileChain(0,
            [
                new ComboProfileStep(99, null, null, null, null, null),          // base step out of range: skipped
                new ComboProfileStep(0, null, "Kept", "x", 25, 10),              // slot and quick position out of range: cleared
                new ComboProfileStep(0, null, null, null, null, null),           // base step used twice: skipped
                new ComboProfileStep(null, 9999, null, null, null, null),         // unknown skill: skipped
                new ComboProfileStep(1, null, null, null, 1, 5),                 // kept
                new ComboProfileStep(2, null, null, null, 1, 5),                 // cooldown and quick already used: cleared
            ]));
        var editor = Create(profile);

        var steps = Damage(editor).Steps;
        Assert.Equal("0,1,2", string.Join(",", steps.Select(step => step.BaseStep)));
        Assert.Equal("Kept", steps[0].CustomName);
        Assert.Null(steps[0].CooldownSlot);
        Assert.Null(steps[0].QuickPosition);
        Assert.Equal(1, steps[1].CooldownSlot);
        Assert.Equal(5, steps[1].QuickPosition);
        Assert.Null(steps[2].CooldownSlot);
        Assert.Null(steps[2].QuickPosition);
        Assert.Equal(4, editor.TotalSteps);
    }

    [Fact]
    public void Reset_to_base_drops_every_customization_and_the_saved_flag()
    {
        var profile = ProfileOf(new ComboProfileChain(0, [new ComboProfileStep(0, null, "Kept", "m", 2, 3)]));
        var editor = Create(profile);
        Assert.True(editor.HasSavedProfile);

        editor.ResetToBase();

        Assert.False(editor.HasSavedProfile);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(4, editor.TotalSteps);
        Assert.Equal("0,1,2", BasesOf(ChainOf(editor.ToProfile(), 0)));
        Assert.Null(Damage(editor).Steps[0].Memo);
        Assert.Null(Damage(editor).Steps[0].CooldownSlot);
    }

    [Fact]
    public void Marking_the_copy_saved_clears_the_unsaved_flag()
    {
        var editor = Create();
        Damage(editor).Steps[0].Memo = "R 1";
        Assert.True(editor.HasUnsavedChanges);

        editor.MarkSaved();

        Assert.False(editor.HasUnsavedChanges);
        Assert.True(editor.HasSavedProfile);
    }

    [Fact]
    public void Hotkey_steps_show_the_players_memo_as_their_key_and_a_hint_without_one()
    {
        var editor = Create();
        var hotkey = Damage(editor).Steps[0];

        Assert.True(hotkey.IsHotkey);
        Assert.Equal("Apply.YourKey", hotkey.KeyLegend);

        hotkey.Memo = "  Shift+1  ";

        Assert.Equal("Shift+1", hotkey.KeyLegend);
        Assert.Equal("R 2", Damage(editor).Steps[1].KeyLegend);
    }

    [Fact]
    public void The_cooldown_label_is_the_slot_number_with_a_hash()
    {
        var editor = Create();
        var step = Damage(editor).Steps[0];

        Assert.Equal(string.Empty, step.CooldownLabel);
        editor.AssignCooldown(step, 14);

        Assert.Equal("#14", step.CooldownLabel);
    }

    [Fact]
    public void Step_count_text_uses_the_singular_for_one_step()
    {
        var editor = Create();

        Assert.Equal("Apply.StepCountMany(3)", Damage(editor).StepCountText);
        Assert.Equal("Apply.StepCountOne", Buffs(editor).StepCountText);
    }

    [Fact]
    public void Mobility_chains_are_a_manual_pool_outside_the_combo()
    {
        var editor = CreateWithMobility();

        Assert.Equal([1, 0], editor.Chains.Select(chain => chain.Index));
        Assert.Equal([2], editor.ManualChains.Select(chain => chain.Index));
        Assert.True(editor.HasManualSteps);
        Assert.Equal(2, editor.ManualChains[0].Steps.Count);
        Assert.Equal(4, editor.Chains.Sum(chain => chain.Steps.Count));
        Assert.Equal(6, editor.TotalSteps);
    }

    [Fact]
    public void Cancels_and_other_chains_are_not_shown()
    {
        var editor = CreateWithMobility();

        Assert.DoesNotContain(editor.Chains, chain => chain.Index == 3);
        Assert.DoesNotContain(editor.ManualChains, chain => chain.Index == 3);
        Assert.DoesNotContain(editor.AllSteps(), step => step.Skills.Any(skill => skill.SkillNo == 700));
    }

    [Fact]
    public void A_manual_skill_gets_the_lowest_free_cooldown_slot_by_hand()
    {
        var editor = CreateWithMobility();
        var pool = editor.ManualChains[0];
        editor.AssignCooldown(Damage(editor).Steps[0], 1);

        editor.AddToFreeSlotCommand.Execute(pool.Steps[0]);
        editor.AddToFreeSlotCommand.Execute(pool.Steps[1]);

        Assert.Equal(2, pool.Steps[0].CooldownSlot);
        Assert.Equal(3, pool.Steps[1].CooldownSlot);
    }

    [Fact]
    public void Adding_to_a_slot_never_adds_the_skill_to_the_combo()
    {
        var editor = CreateWithMobility();
        var chainCountBefore = editor.Chains.Sum(chain => chain.Steps.Count);

        editor.AddToFreeSlotCommand.Execute(editor.ManualChains[0].Steps[0]);

        Assert.Equal(chainCountBefore, editor.Chains.Sum(chain => chain.Steps.Count));
        Assert.DoesNotContain(editor.Chains.SelectMany(chain => chain.Steps), step => step.Skills.Any(skill => skill.SkillNo == 600));
    }

    [Fact]
    public void A_manual_skill_that_already_has_a_slot_is_left_alone()
    {
        var editor = CreateWithMobility();
        var step = editor.ManualChains[0].Steps[0];
        editor.AssignCooldown(step, 5);

        editor.AddToFreeSlotCommand.Execute(step);

        Assert.Equal(5, step.CooldownSlot);
    }

    [Fact]
    public void The_profile_saves_the_manual_pool_chain_with_its_index_and_not_the_hidden_ones()
    {
        var editor = CreateWithMobility();
        editor.AddToFreeSlotCommand.Execute(editor.ManualChains[0].Steps[1]);

        var profile = editor.ToProfile();

        Assert.Equal([0, 1, 2], profile.Chains.Select(chain => chain.Chain));
        var pool = ChainOf(profile, 2);
        Assert.Equal("0,1", BasesOf(pool));
        Assert.Equal(1, pool.Steps[1].CooldownSlot);
    }

    [Fact]
    public void A_profile_with_a_manual_pool_rebuilds_the_pool_with_its_slots()
    {
        var profile = ProfileOf(
            new ComboProfileChain(2, [new ComboProfileStep(1, null, null, null, 4, null), new ComboProfileStep(0, null, null, null, null, null)]));
        var editor = ComboEditorViewModel.Create(16, "awakening", ComboTestData.GuideWithMobility(), profile, new KeyLocalizer(), ApplyTestHelpers.Culture, _log.Logger);

        var pool = editor.ManualChains[0];
        Assert.Equal("1,0", string.Join(",", pool.Steps.Select(step => step.BaseStep)));
        Assert.Equal(4, pool.Steps[0].CooldownSlot);
    }

    [Fact]
    public void A_resolver_that_hides_everything_leaves_the_editor_empty()
    {
        var editor = Create(kinds: new HideEverythingResolver());

        Assert.Empty(editor.Chains);
        Assert.Empty(editor.ManualChains);
        Assert.Equal(0, editor.TotalSteps);
        Assert.Null(editor.SelectedStep);
    }

    [Fact]
    public void The_title_resolver_reads_the_section_first_and_then_the_title()
    {
        var resolver = TitleChainKindResolver.Instance;

        Assert.Equal(ComboChainKind.Mobility, resolver.Resolve(new ComboChain("class-mobility", "Sprint Buff", [], [])));
        Assert.Equal(ComboChainKind.Cancels, resolver.Resolve(new ComboChain("class-cancels", "Tiger Blade Cancel", [], [])));
        Assert.Equal(ComboChainKind.Buff, resolver.Resolve(new ComboChain("buffs", "Auto buffs", [], [])));
        Assert.Equal(ComboChainKind.Combo, resolver.Resolve(new ComboChain("skill-rotation", "Damage Combo", [], [])));
        Assert.Equal(ComboChainKind.Combo, resolver.Resolve(new ComboChain("dps-priority", "1) Highest Priority", [], [])));
        Assert.Equal(ComboChainKind.Other, resolver.Resolve(new ComboChain("class-specifics", "Core", [], [])));
    }

    [Fact]
    public void Describe_helper_reads_every_saved_field()
    {
        var lines = Describe(ProfileOf(new ComboProfileChain(0, [new ComboProfileStep(0, null, "a", "b", 1, 2)])));

        Assert.Equal(["chain 0", "0|-|a|b|1|2"], lines);
    }

    private sealed class HideEverythingResolver : IChainKindResolver
    {
        public ComboChainKind Resolve(ComboChain chain) => ComboChainKind.Other;
    }
}
