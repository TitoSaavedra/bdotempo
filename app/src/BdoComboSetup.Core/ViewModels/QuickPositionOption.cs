namespace BdoComboSetup.Core.ViewModels;

/// <summary>One entry of the quick position editor. Position 0 means "not on the quick bar".</summary>
public sealed record QuickPositionOption(int Position, string Label);
