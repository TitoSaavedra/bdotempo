namespace BdoComboSetup.Core.ViewModels;

/// <summary>A rectangle of the game screen in game pixels: the area that the preview frames at its default zoom.</summary>
public readonly record struct SlotFrame(double Left, double Top, double Width, double Height)
{
    /// <summary>The smallest rectangle that holds every slot rectangle, grown by margin on each side and kept inside the screen.</summary>
    public static SlotFrame Around(IEnumerable<SlotFrame> slots, double margin, double screenWidth, double screenHeight)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var list = slots.ToList();
        if (list.Count == 0)
        {
            return new SlotFrame(0, 0, screenWidth, screenHeight);
        }

        var left = Math.Max(0, list.Min(slot => slot.Left) - margin);
        var top = Math.Max(0, list.Min(slot => slot.Top) - margin);
        var right = Math.Min(screenWidth, list.Max(slot => slot.Left + slot.Width) + margin);
        var bottom = Math.Min(screenHeight, list.Max(slot => slot.Top + slot.Height) + margin);
        return new SlotFrame(left, top, right - left, bottom - top);
    }
}
