using BdoComboSetup.Core.Combo;

namespace BdoComboSetup.Core.Game;

public enum BarKind
{
    Cooldown,
    Quick,
}

/// <summary>Where a bar position comes from. Only UiData is exact; the others are estimates the user can correct by dragging.</summary>
public enum BarSource
{
    /// <summary>A verified UIData index gives the bar's top-left corner.</summary>
    UiData,

    /// <summary>The quick bar is estimated from the candidate panels of the layout.</summary>
    Candidates,

    /// <summary>The user dragged the bar; the saved position wins.</summary>
    Manual,

    /// <summary>No index and no saved position: a generic place at the bottom of the screen.</summary>
    Default,
}

/// <summary>Top-left corner of a bar as fractions (0..1) of the game screen, so it works at any resolution.</summary>
public sealed record NormalizedPosition(double X, double Y);

/// <summary>A bar in game pixels. Slot i sits at column i % Columns, row i / Columns, offset by PitchX and PitchY.</summary>
public sealed record BarPlacement(
    BarKind Kind,
    BarSource Source,
    double Left,
    double Top,
    double Width,
    double Height,
    double SlotSize,
    double PitchX,
    double PitchY,
    int Columns,
    int Rows,
    int? IndexUsed);

/// <summary>A visible UIData panel, drawn as a faint outline with its index in the debug overlay.</summary>
public sealed record UiPanelOutline(int Index, int? WindowIndex, double Left, double Top, double Width, double Height, bool IsCandidate);

/// <summary>Everything the bar placement depends on.</summary>
public sealed record BarLayoutInput(
    double GameWidth,
    double GameHeight,
    double UiScale,
    GameUiLayout? Layout,
    GameLayoutIndices Indices,
    int CooldownRows,
    int CooldownColumns,
    NormalizedPosition? CooldownOverride,
    NormalizedPosition? QuickOverride);

/// <summary>Computes the two bars in game pixels for one layout. Pure: no file or UI access.</summary>
public static class GameScreenLayoutResolver
{
    /// <summary>Slot size in game pixels at UI scale 1.</summary>
    public const double BaseSlotSize = 52;

    /// <summary>Gap between cooldown slots in game pixels at UI scale 1.</summary>
    public const double BaseCooldownGap = 6;

    /// <summary>Distance between the quick bar pitch and the slot size when no candidate pitch is known, at UI scale 1.</summary>
    public const double BaseDefaultQuickPitch = 72;

    /// <summary>Distance from the bottom of the screen to the generic quick bar, at UI scale 1.</summary>
    public const double BaseDefaultBottomMargin = 120;

    /// <summary>Gap between the generic cooldown bar and the quick bar, at UI scale 1.</summary>
    public const double BaseDefaultBarGap = 24;

    /// <summary>Size used to draw a UIData panel that has no SizeX/SizeY.</summary>
    public const double FallbackPanelSize = 40;

    public static BarPlacement ResolveCooldown(BarLayoutInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInput(input);

        var scale = input.UiScale;
        var slot = BaseSlotSize * scale;
        var gap = BaseCooldownGap * scale;
        var columns = input.CooldownColumns;
        var rows = input.CooldownRows;
        var width = columns * slot + (columns - 1) * gap;
        var height = rows * slot + (rows - 1) * gap;

        var fromIndex = FindPanelTopLeft(input, input.Indices.CooldownBarIndex);
        if (fromIndex is { } indexed)
        {
            return new BarPlacement(BarKind.Cooldown, BarSource.UiData, indexed.X, indexed.Y, width, height, slot, slot + gap, slot + gap, columns, rows, input.Indices.CooldownBarIndex);
        }

        if (input.CooldownOverride is { } manual)
        {
            return new BarPlacement(BarKind.Cooldown, BarSource.Manual, manual.X * input.GameWidth, manual.Y * input.GameHeight, width, height, slot, slot + gap, slot + gap, columns, rows, null);
        }

        // Generic place: centred, just above the generic quick bar.
        var quick = ResolveQuick(input with { CooldownOverride = null });
        var top = quick.Top - BaseDefaultBarGap * scale - height;
        return new BarPlacement(BarKind.Cooldown, BarSource.Default, (input.GameWidth - width) / 2, top, width, height, slot, slot + gap, slot + gap, columns, rows, null);
    }

    public static BarPlacement ResolveQuick(BarLayoutInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInput(input);

        var scale = input.UiScale;
        var slot = BaseSlotSize * scale;
        var slotCount = QuickSlotMap.Count;
        var defaultPitch = BaseDefaultQuickPitch * scale;

        if (input.QuickOverride is { } manual)
        {
            var pitch = defaultPitch;
            return QuickBar(BarSource.Manual, manual.X * input.GameWidth, manual.Y * input.GameHeight, slot, pitch, slotCount, null);
        }

        var verified = FindPanelTopLeft(input, input.Indices.QuickSlotBarIndex);
        if (verified is { } indexed)
        {
            return QuickBar(BarSource.UiData, indexed.X, indexed.Y, slot, defaultPitch, slotCount, input.Indices.QuickSlotBarIndex);
        }

        var candidates = input.Indices.QuickSlotBarCandidates
            .Select(index => (Index: index, Point: FindPanelTopLeft(input, index)))
            .Where(candidate => candidate.Point is not null)
            .Select(candidate => (candidate.Index, Point: candidate.Point!.Value))
            .OrderBy(candidate => candidate.Point.X)
            .ToList();

        if (candidates.Count > 0)
        {
            var lefts = candidates.Select(candidate => candidate.Point.X).ToList();
            var tops = candidates.Select(candidate => candidate.Point.Y).ToList();

            // Pitch between neighbouring candidates; the centre of the candidate span is the centre of the bar.
            var gaps = lefts.Zip(lefts.Skip(1), (a, b) => b - a).Where(gap => gap > 0).ToList();
            var candidatePitch = gaps.Count > 0 ? Median(gaps) : defaultPitch;
            var centreX = (lefts.Min() + lefts.Max() + slot) / 2;
            var barWidth = (slotCount - 1) * candidatePitch + slot;
            return QuickBar(BarSource.Candidates, centreX - barWidth / 2, Median(tops), slot, candidatePitch, slotCount, null);
        }

        var defaultWidth = (slotCount - 1) * defaultPitch + slot;
        return QuickBar(BarSource.Default, (input.GameWidth - defaultWidth) / 2, input.GameHeight - BaseDefaultBottomMargin * scale - slot, slot, defaultPitch, slotCount, null);
    }

    /// <summary>The visible UIData panels of the layout, for the debug overlay. Panels without a position are left out.</summary>
    public static IReadOnlyList<UiPanelOutline> BuildOutlines(BarLayoutInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInput(input);

        if (input.Layout is null)
        {
            return [];
        }

        var candidates = input.Indices.QuickSlotBarCandidates.ToHashSet();
        var outlines = new List<UiPanelOutline>();
        foreach (var panel in input.Layout.Panels)
        {
            if (!panel.IsShow || UiPanelGeometry.TopLeft(panel, input.GameWidth, input.GameHeight) is not { } point)
            {
                continue;
            }

            var width = panel.HasSize ? panel.SizeX!.Value : FallbackPanelSize;
            var height = panel.HasSize ? panel.SizeY!.Value : FallbackPanelSize;
            outlines.Add(new UiPanelOutline(panel.Index, panel.WindowIndex, point.X, point.Y, width, height, candidates.Contains(panel.Index)));
        }

        return outlines;
    }

    /// <summary>Normalized top-left corner of a bar for a point in game pixels (used when the user drags it).</summary>
    public static NormalizedPosition Normalize(double left, double top, double gameWidth, double gameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameHeight);
        return new NormalizedPosition(Math.Clamp(left / gameWidth, 0, 1), Math.Clamp(top / gameHeight, 0, 1));
    }

    private static BarPlacement QuickBar(BarSource source, double left, double top, double slot, double pitch, int count, int? index)
    {
        var width = (count - 1) * pitch + slot;
        return new BarPlacement(BarKind.Quick, source, left, top, width, slot, slot, pitch, 0, count, 1, index);
    }

    private static UiPoint? FindPanelTopLeft(BarLayoutInput input, int? index)
    {
        if (index is null)
        {
            return null;
        }

        var panel = input.Layout?.Find(index.Value);
        if (panel is null || !panel.IsShow)
        {
            return null;
        }

        return UiPanelGeometry.TopLeft(panel, input.GameWidth, input.GameHeight);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(value => value).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static void ValidateInput(BarLayoutInput input)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.GameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.GameHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.UiScale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.CooldownRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.CooldownColumns);
    }
}
