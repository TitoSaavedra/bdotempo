namespace BdoComboSetup.Core.Combo;

/// <summary>
/// What a chain of a guide is for. Buff and Combo chains are shown; Mobility chains are a manual pool (never added to the
/// combo by itself); Cancels and Other are not shown yet.
/// </summary>
public enum ComboChainKind
{
    Buff,
    Combo,
    Mobility,
    Cancels,
    Other,
}

/// <summary>
/// The seam between the Apply page and the chain kinds. The data layer will give every chain a kind
/// (<c>ComboChain.Kind</c>); until it does, <see cref="TitleChainKindResolver"/> works it out from the site section and title.
/// The Apply page only asks this interface, so switching to the real kind is one class.
/// </summary>
public interface IChainKindResolver
{
    ComboChainKind Resolve(ComboChain chain);
}

/// <summary>
/// Temporary resolver until the data layer adds <c>ComboChain.Kind</c>. It reads the bdoclasshub section first
/// (<c>class-mobility</c> is mobility, <c>class-cancels</c> is cancels, the combo sections are combos), then a title with
/// "buff" is a buff chain. Anything else is Other, which the page hides.
/// </summary>
public sealed class TitleChainKindResolver : IChainKindResolver
{
    public static readonly TitleChainKindResolver Instance = new();

    private static readonly HashSet<string> ComboSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "skill-rotation",
        "basic-combo",
        "alternative-combos",
        "easy-combo",
        "dps-priority",
        "pack-to-pack",
    };

    public ComboChainKind Resolve(ComboChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var section = chain.Section ?? string.Empty;
        if (string.Equals(section, "class-cancels", StringComparison.OrdinalIgnoreCase))
        {
            return ComboChainKind.Cancels;
        }

        if (string.Equals(section, "class-mobility", StringComparison.OrdinalIgnoreCase))
        {
            return ComboChainKind.Mobility;
        }

        if ((chain.Title ?? string.Empty).Contains("buff", StringComparison.OrdinalIgnoreCase))
        {
            return ComboChainKind.Buff;
        }

        return ComboSections.Contains(section) ? ComboChainKind.Combo : ComboChainKind.Other;
    }
}
