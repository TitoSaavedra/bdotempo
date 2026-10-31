namespace BdoComboSetup.Core.Combo;

/// <summary>
/// The combo catalog: the classes with a guide, and the guides themselves. Read-only; the data ships with the app.
/// </summary>
public interface ICombosCatalog
{
    /// <summary>Classes in classType order. A class without a guide is not listed.</summary>
    IReadOnlyList<ClassInfo> Classes { get; }

    /// <summary>
    /// The guide of a class and spec, or null when the catalog has no such spec or its guide is missing.
    /// The same object is returned on repeated calls.
    /// </summary>
    Task<ComboGuide?> LoadGuideAsync(int classType, string specId);
}
