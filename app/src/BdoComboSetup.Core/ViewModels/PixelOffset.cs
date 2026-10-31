namespace BdoComboSetup.Core.ViewModels;

/// <summary>Top-left offset in game pixels, relative to the game screen or to the bar that holds the item.</summary>
public readonly record struct PixelOffset(double Left, double Top);

/// <summary>One entry of the layout selector.</summary>
public sealed record LayoutOption(Game.GameUiLayoutId Id, string Label);
