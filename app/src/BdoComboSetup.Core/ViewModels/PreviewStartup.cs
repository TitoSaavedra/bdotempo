using BdoComboSetup.Core.Game;

namespace BdoComboSetup.Core.ViewModels;

/// <summary>Options given on the command line for one run of the Preview page. They are never saved.</summary>
/// <param name="Layout">Layout to open, or null to use the saved one.</param>
/// <param name="ShowHiddenSlots">Start with the hidden game slots drawn (dimmed, dashed outline).</param>
/// <param name="CooldownDemo">Start with the demo cooldown (60%) on the first cooldown slot that has a skill.</param>
/// <param name="Zoom">Zoom relative to fit to start with, or null for fit.</param>
/// <param name="NoCapture">Start with the game capture switched off, so the game window is not captured.</param>
public sealed record PreviewStartup(GameUiLayoutId? Layout, bool ShowHiddenSlots, bool CooldownDemo, double? Zoom, bool NoCapture = false);
