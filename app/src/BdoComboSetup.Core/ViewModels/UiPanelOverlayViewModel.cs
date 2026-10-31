namespace BdoComboSetup.Core.ViewModels;

/// <summary>A visible UIData panel in the debug overlay. Label is its index (and window, for chat windows).</summary>
public sealed record UiPanelOverlayViewModel(PixelOffset Offset, double Width, double Height, string Label, bool IsCandidate);
