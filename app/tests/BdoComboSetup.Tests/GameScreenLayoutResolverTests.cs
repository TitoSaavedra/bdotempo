using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Tests;

public sealed class GameScreenLayoutResolverTests
{
    private static readonly GameUiLayoutSet Set = GameUiLayoutReader.Parse(UiLayoutFixtures.Sample);

    private static BarLayoutInput Input(
        GameLayoutIndices? indices = null,
        double uiScale = 1.0,
        NormalizedPosition? cooldownOverride = null,
        NormalizedPosition? quickOverride = null) => Build(Set.Get(GameUiLayoutId.General), indices, uiScale, cooldownOverride, quickOverride);

    private static BarLayoutInput InputWithoutLayout() => Build(null, null, 1.0, null, null);

    private static BarLayoutInput Build(
        GameUiLayout? layout,
        GameLayoutIndices? indices,
        double uiScale,
        NormalizedPosition? cooldownOverride,
        NormalizedPosition? quickOverride) => new(
            GameWidth: UiLayoutFixtures.GameWidth,
            GameHeight: UiLayoutFixtures.GameHeight,
            UiScale: uiScale,
            Layout: layout,
            Indices: indices ?? GameLayoutIndices.Default,
            CooldownRows: 2,
            CooldownColumns: 10,
            CooldownOverride: cooldownOverride,
            QuickOverride: quickOverride);

    [Fact]
    public void Default_indices_place_the_quick_bar_from_the_bottom_candidates()
    {
        var quick = GameScreenLayoutResolver.ResolveQuick(Input());

        Assert.Equal(BarSource.Candidates, quick.Source);
        Assert.Equal(BarKind.Quick, quick.Kind);
        Assert.Equal(9, quick.Columns);
        Assert.Equal(1, quick.Rows);
        Assert.Equal(GameScreenLayoutResolver.BaseSlotSize, quick.SlotSize);

        // Candidate left edges: 1449, 1522, 1591, 1664, 1734, 1809, 1881, 1952, so the median pitch is 72.
        Assert.Equal(72.0, quick.PitchX, precision: 6);
        Assert.Equal(0.0, quick.PitchY);
        Assert.Equal(8 * 72.0 + 52.0, quick.Width, precision: 6);
    }

    [Fact]
    public void Quick_bar_is_centred_on_the_candidate_span_and_sits_on_their_row()
    {
        var quick = GameScreenLayoutResolver.ResolveQuick(Input());

        // Span from 1449 (index 100) to 1952 + 52 (index 97): centre 1726.5, so the left edge is 1726.5 - 628 / 2.
        Assert.Equal(1726.5 - quick.Width / 2, quick.Left, precision: 6);
        Assert.InRange(quick.Top, 1360, 1363);
    }

    [Fact]
    public void Without_any_candidate_the_quick_bar_is_at_the_generic_bottom_place()
    {
        var quick = GameScreenLayoutResolver.ResolveQuick(InputWithoutLayout());

        Assert.Equal(BarSource.Default, quick.Source);
        Assert.Equal(1440 - 120 - 52, quick.Top);
        Assert.Equal((3440 - quick.Width) / 2, quick.Left, precision: 6);
    }

    [Fact]
    public void Cooldown_bar_without_an_index_is_generic_and_sits_above_the_quick_bar()
    {
        var input = Input();
        var quick = GameScreenLayoutResolver.ResolveQuick(input);

        var cooldown = GameScreenLayoutResolver.ResolveCooldown(input);

        Assert.Equal(BarSource.Default, cooldown.Source);
        Assert.Null(cooldown.IndexUsed);
        Assert.Equal(10, cooldown.Columns);
        Assert.Equal(2, cooldown.Rows);
        Assert.Equal(10 * 52 + 9 * 6, cooldown.Width);
        Assert.Equal(2 * 52 + 6, cooldown.Height);
        Assert.True(cooldown.Top + cooldown.Height < quick.Top);
        Assert.Equal((3440 - cooldown.Width) / 2, cooldown.Left, precision: 6);
    }

    [Fact]
    public void Verified_cooldown_index_gives_the_exact_top_left_corner()
    {
        var indices = new GameLayoutIndices(CooldownBarIndex: 13, QuickSlotBarIndex: null, QuickSlotBarCandidates: GameLayoutIndices.DefaultQuickSlotCandidates);

        var cooldown = GameScreenLayoutResolver.ResolveCooldown(Input(indices: indices));

        Assert.Equal(BarSource.UiData, cooldown.Source);
        Assert.Equal(13, cooldown.IndexUsed);
        Assert.Equal(1524, cooldown.Left);
        Assert.Equal(1264, cooldown.Top);
    }

    [Fact]
    public void Verified_cooldown_index_that_is_hidden_falls_back_to_the_generic_place()
    {
        // Index 2 is IsShow=true but has no position; index 21 is IsShow=false.
        var indices = new GameLayoutIndices(CooldownBarIndex: 21, QuickSlotBarIndex: null, QuickSlotBarCandidates: []);

        var cooldown = GameScreenLayoutResolver.ResolveCooldown(Input(indices: indices));

        Assert.Equal(BarSource.Default, cooldown.Source);
    }

    [Fact]
    public void Verified_quick_index_gives_the_bar_origin_and_default_pitch()
    {
        var indices = new GameLayoutIndices(CooldownBarIndex: null, QuickSlotBarIndex: 91, QuickSlotBarCandidates: []);

        var quick = GameScreenLayoutResolver.ResolveQuick(Input(indices: indices));

        Assert.Equal(BarSource.UiData, quick.Source);
        Assert.Equal(1522, quick.Left);
        Assert.Equal(1362, quick.Top);
        Assert.Equal(GameScreenLayoutResolver.BaseDefaultQuickPitch, quick.PitchX);
    }

    [Fact]
    public void A_saved_cooldown_position_wins_over_the_generic_place()
    {
        var saved = new NormalizedPosition(0.1, 0.2);

        var cooldown = GameScreenLayoutResolver.ResolveCooldown(Input(cooldownOverride: saved));

        Assert.Equal(BarSource.Manual, cooldown.Source);
        Assert.Equal(0.1 * 3440, cooldown.Left, precision: 6);
        Assert.Equal(0.2 * 1440, cooldown.Top, precision: 6);
    }

    [Fact]
    public void A_saved_quick_position_wins_over_the_candidates()
    {
        var quick = GameScreenLayoutResolver.ResolveQuick(Input(quickOverride: new NormalizedPosition(0.5, 0.9)));

        Assert.Equal(BarSource.Manual, quick.Source);
        Assert.Equal(1720, quick.Left);
        Assert.Equal(1296, quick.Top);
    }

    [Fact]
    public void Ui_scale_multiplies_the_slot_size_but_keeps_the_measured_candidate_pitch()
    {
        // The candidate positions were measured at the game's current UI scale, so their spacing is already scaled.
        var quick = GameScreenLayoutResolver.ResolveQuick(Input(uiScale: 1.5));

        Assert.Equal(52 * 1.5, quick.SlotSize, precision: 6);
        Assert.Equal(72.0, quick.PitchX, precision: 6);
        Assert.Equal(8 * 72.0 + 52 * 1.5, quick.Width, precision: 6);

        // The centre of the candidate span moves with the slot size: (1449 + 1952 + 78) / 2.
        Assert.Equal((1449 + 1952 + 78) / 2.0 - quick.Width / 2, quick.Left, precision: 6);
    }

    [Fact]
    public void Outlines_are_the_visible_panels_with_a_position_and_flag_the_candidates()
    {
        var outlines = GameScreenLayoutResolver.BuildOutlines(Input());

        Assert.DoesNotContain(outlines, outline => outline.Index == 2);   // IsShow=true but no position
        Assert.DoesNotContain(outlines, outline => outline.Index == 21);  // IsShow=false
        Assert.DoesNotContain(outlines, outline => outline.Index == 32 && outline.WindowIndex == 1);  // chat window, IsShow=false

        var bar = outlines.Single(outline => outline.Index == 91);
        Assert.True(bar.IsCandidate);
        Assert.Equal(1522, bar.Left);
        Assert.Equal(1362, bar.Top);
        Assert.Equal(40, bar.Width);   // no SizeX on this row: fallback size

        Assert.False(outlines.Single(outline => outline.Index == 13).IsCandidate);
        Assert.Equal(644, outlines.Single(outline => outline.Index == 32 && outline.WindowIndex == 0).Width);
    }

    [Fact]
    public void No_layout_means_no_outlines()
    {
        var outlines = GameScreenLayoutResolver.BuildOutlines(InputWithoutLayout());

        Assert.Empty(outlines);
    }

    [Fact]
    public void Normalize_turns_a_point_into_screen_fractions_and_clamps_it()
    {
        var normalized = GameScreenLayoutResolver.Normalize(1720, 720, 3440, 1440);

        Assert.Equal(0.5, normalized.X, precision: 9);
        Assert.Equal(0.5, normalized.Y, precision: 9);
        Assert.Equal(new NormalizedPosition(0, 1), GameScreenLayoutResolver.Normalize(-10, 5000, 3440, 1440));
    }
}
