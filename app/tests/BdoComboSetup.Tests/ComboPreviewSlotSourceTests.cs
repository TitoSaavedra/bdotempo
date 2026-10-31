using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Game;
using BdoComboSetup.Core.ViewModels;

namespace BdoComboSetup.Tests;

/// <summary>The preview's slot source: it reads the combo of the Apply page and follows its changes.</summary>
public sealed class ComboPreviewSlotSourceTests : IDisposable
{
    private readonly TempLogger _log = new();
    private readonly ApplyTestHelpers.ProfileFolder _profiles;

    public ComboPreviewSlotSourceTests()
    {
        _profiles = new ApplyTestHelpers.ProfileFolder(_log.Logger);
    }

    public void Dispose()
    {
        _profiles.Dispose();
        _log.Dispose();
    }

    private ApplyComboViewModel Apply() => new(
        FakeCombosCatalog.Sample(),
        new NoIconAssets(),
        new FakeCharacterSource(),
        _profiles.Store,
        new RecordingApplier(),
        TestConfirmation.Accepting(_log.Logger),
        new KeyLocalizer(),
        ApplyTestHelpers.Culture,
        _log.Logger);

    [Fact]
    public void With_no_combo_loaded_every_slot_is_empty_and_the_default_keys_are_used()
    {
        using var source = new ComboPreviewSlotSource(Apply(), _ => null);

        Assert.Null(source.Cooldown(1));
        Assert.Null(source.Quick(1));
        Assert.Equal("M1", source.QuickKey(1)?.Label);
        Assert.Equal("0", source.QuickKey(10)?.Label);
    }

    [Fact]
    public async Task Cooldown_and_quick_slots_come_from_the_steps_of_the_editor()
    {
        var apply = Apply();
        using var source = new ComboPreviewSlotSource(apply, _ => null);

        await apply.InitializeAsync("tamer", "awakening");

        var step = apply.Editor!.AllSteps().First(item => item.CooldownSlot == 1);
        var cooldown = source.Cooldown(1);
        Assert.NotNull(cooldown);
        Assert.Equal(1, cooldown.CooldownNumber);
        Assert.Equal(step.SkillName, cooldown.SkillName);
        Assert.Equal(step.PrimarySkill?.SkillNo, cooldown.SkillNo);
    }

    [Fact]
    public async Task Quick_positions_above_nine_are_never_filled_by_a_combo()
    {
        var apply = Apply();
        using var source = new ComboPreviewSlotSource(apply, _ => null);
        await apply.InitializeAsync("tamer", "awakening");

        Assert.Null(source.Quick(10));
        Assert.Null(source.Quick(20));
    }

    [Fact]
    public void Out_of_range_slot_numbers_are_refused()
    {
        using var source = new ComboPreviewSlotSource(Apply(), _ => null);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.Cooldown(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Cooldown(21));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Quick(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Quick(21));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.QuickKey(21));
    }

    [Fact]
    public async Task Changed_is_raised_when_a_step_is_edited_and_when_the_guide_is_reset()
    {
        var apply = Apply();
        using var source = new ComboPreviewSlotSource(apply, _ => null);
        await apply.InitializeAsync("tamer", "awakening");
        var raised = 0;
        source.Changed += (_, _) => raised++;
        var editor = apply.Editor!;

        editor.AssignCooldown(editor.AllSteps().First(item => item.CooldownSlot == 1), 19);
        var afterEdit = raised;
        editor.ResetToBase();

        Assert.True(afterEdit > 0);
        Assert.True(raised > afterEdit);
    }

    [Fact]
    public async Task Changed_is_raised_when_the_guide_on_the_apply_page_changes()
    {
        var apply = Apply();
        using var source = new ComboPreviewSlotSource(apply, _ => null);
        var raised = 0;
        source.Changed += (_, _) => raised++;

        await apply.InitializeAsync("tamer", "awakening");

        Assert.True(raised > 0);
        Assert.NotNull(source.Cooldown(1));
    }

    [Fact]
    public async Task After_dispose_the_source_no_longer_raises_changed()
    {
        var apply = Apply();
        var source = new ComboPreviewSlotSource(apply, _ => null);
        var raised = 0;
        source.Changed += (_, _) => raised++;
        source.Dispose();

        await apply.InitializeAsync("tamer", "awakening");

        Assert.Equal(0, raised);
    }
}
