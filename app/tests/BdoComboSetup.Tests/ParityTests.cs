using System.Text.Json;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Operations;

namespace BdoComboSetup.Tests;

/// <summary>
/// The C# writer must produce the same bytes as the Python tool for the same inputs. Each case folder holds a synthetic
/// input file, the combo file, its options and the output that the Python tool wrote (see Fixtures/parity/README.md).
/// </summary>
public sealed class ParityTests
{
    private sealed record CaseOptions(string Account, string Pcid, string[] Overrides, bool SkipOptional);

    public static IEnumerable<object[]> Cases()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "parity");
        foreach (var directory in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(directory, "case.json")))
            {
                yield return new object[] { Path.GetFileName(directory) };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_output_is_byte_identical_to_the_python_tool(string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "parity", name);
        var options = JsonSerializer.Deserialize<CaseOptions>(
            File.ReadAllText(Path.Combine(directory, "case.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(ComboTestRig.Account, options.Account);

        using var rig = new ComboTestRig(options.Pcid);
        var combo = ComboFileReader.Read(Path.Combine(directory, "combo.json"), rig.Log.Logger);
        var overrides = ComboFileReader.ParseOverrides(options.Overrides);
        var slots = ComboFileReader.BuildCooldownSlots(combo, overrides, options.SkipOptional, rig.Log.Logger);
        rig.WriteCharacter(File.ReadAllBytes(Path.Combine(directory, "input", "gameVariable.xml")));

        var request = new ComboApplyRequest(options.Pcid, combo.ClassId, slots, []);
        var result = await rig.Applier.ApplyAsync(request);

        Assert.True(
            result.Outcome == OperationOutcome.Written,
            $"{name}: expected Written, got {result.Outcome} ({result.Problem}: {result.ProblemDetail})");
        var expected = File.ReadAllBytes(Path.Combine(directory, "expected", "gameVariable.xml"));
        Assert.Equal(expected, rig.ReadCharacter());
    }

    [Fact]
    public void There_are_ten_parity_cases()
    {
        Assert.Equal(10, Cases().Count());
    }
}
