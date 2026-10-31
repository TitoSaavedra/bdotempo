using System.Globalization;
using System.Reflection;

namespace BdoComboSetup.Core.Assets;

/// <summary>Opens files embedded in an assembly by their logical name (for example "catalog.json").</summary>
public interface IEmbeddedResources
{
    /// <summary>A new stream of the resource, or null when the assembly has no resource with that name. The caller disposes it.</summary>
    Stream? Open(string name);
}

/// <summary>Reads the resources embedded in one assembly (the app's own, or a test assembly).</summary>
public sealed class AssemblyEmbeddedResources(Assembly assembly) : IEmbeddedResources
{
    private readonly Assembly _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));

    public Stream? Open(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _assembly.GetManifestResourceStream(name);
    }
}

/// <summary>File names of the asset bundle written by <c>bdo_combo.guides bundle</c>. Shared by the catalog and the icons.</summary>
public static class BundleResourceNames
{
    public const string Catalog = "catalog.json";
    public const string Skills = "skills.json";

    public static string Guide(string key) => $"guide-{key}.json";

    public static string SkillIcon(int skillNo) => $"skill-{skillNo.ToString(CultureInfo.InvariantCulture)}.png";

    public static string ClassIcon(int classType) => $"class-{classType.ToString(CultureInfo.InvariantCulture)}.png";
}
