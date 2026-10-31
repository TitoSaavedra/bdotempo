using System.Globalization;
using BdoComboSetup.Core.Assets;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Logging;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Tests;

/// <summary>A catalog held in memory: the classes given, and the guides by class type and spec id.</summary>
internal sealed class FakeCombosCatalog(IReadOnlyList<ClassInfo> classes, IReadOnlyDictionary<(int ClassType, string SpecId), ComboGuide> guides) : ICombosCatalog
{
    public IReadOnlyList<ClassInfo> Classes { get; } = classes;

    public Task<ComboGuide?> LoadGuideAsync(int classType, string specId) =>
        Task.FromResult(guides.TryGetValue((classType, specId), out var guide) ? guide : null);

    /// <summary>Tamer (awakening, succession), Warrior (awakening) and Shai (talent, azminek). Shai's two experts share one guide.</summary>
    public static FakeCombosCatalog Sample()
    {
        var tamer = new ClassInfo(
            16,
            "tamer",
            new BilingualText("Tamer", "Domadora"),
            [
                new SpecInfo("awakening", new BilingualText("Awakening", "Despertar"), "tamer.awakening"),
                new SpecInfo("succession", new BilingualText("Succession", "Sucesión"), "tamer.succession"),
            ]);
        var warrior = new ClassInfo(
            0,
            "warrior",
            new BilingualText("Warrior", "Guerrero"),
            [new SpecInfo("awakening", new BilingualText("Awakening", "Despertar"), "warrior.awakening")]);
        var shai = new ClassInfo(
            17,
            "shai",
            new BilingualText("Shai", "Shai"),
            [
                new SpecInfo("azminek", new BilingualText("Azminek", "Azminek"), "shai.talent"),
                new SpecInfo("talent", new BilingualText("Talent", "Talent"), "shai.talent"),
            ]);

        var guides = new Dictionary<(int, string), ComboGuide>
        {
            [(16, "awakening")] = ComboTestData.TamerGuide("Tamer Awakening"),
            [(16, "succession")] = ComboTestData.TamerGuide("Tamer Succession"),
            [(0, "awakening")] = ComboTestData.TamerGuide("Warrior Awakening"),
            [(17, "azminek")] = ComboTestData.TamerGuide("Shai Talent"),
            [(17, "talent")] = ComboTestData.TamerGuide("Shai Talent"),
        };

        return new FakeCombosCatalog([warrior, tamer, shai], guides);
    }
}

/// <summary>An asset provider that has no icons: every answer is null, so the views draw glyphs.</summary>
internal sealed class NoIconAssets : IAssetProvider
{
    public Stream? OpenSkillIcon(int skillNo, string? className = null) => null;

    public Stream? OpenClassIcon(int classType) => null;

    public Stream? OpenSlotArt(SlotArt art) => null;
}

/// <summary>Helpers shared by the Apply tests.</summary>
internal static class ApplyTestHelpers
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>A temporary profile folder that is deleted on dispose, with a store on it.</summary>
    public sealed class ProfileFolder : IDisposable
    {
        public ProfileFolder(AppLogger logger)
        {
            Directory = Path.Combine(Path.GetTempPath(), "BdoComboSetup.Tests.Profiles", Guid.NewGuid().ToString("N"));
            Store = new ComboProfileStore(Directory, logger);
        }

        public string Directory { get; }

        public ComboProfileStore Store { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}

/// <summary>An applier that records every request. The dry run and the write answer with the given results.</summary>
internal sealed class RecordingApplier(ComboApplyResult? dryRunResult = null, ComboApplyResult? writeResult = null) : IComboApplier
{
    private static readonly ComboApplyResult Unchanged = new(OperationOutcome.Unchanged, OperationProblem.None, null, null, null, null, []);

    public List<ComboApplyRequest> DryRuns { get; } = [];

    public List<ComboApplyRequest> Writes { get; } = [];

    public Task<ComboApplyResult> DryRunAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
    {
        DryRuns.Add(request);
        return Task.FromResult(dryRunResult ?? Unchanged);
    }

    public Task<ComboApplyResult> ApplyAsync(ComboApplyRequest request, CancellationToken cancellationToken = default)
    {
        Writes.Add(request);
        return Task.FromResult(writeResult ?? Unchanged);
    }
}
