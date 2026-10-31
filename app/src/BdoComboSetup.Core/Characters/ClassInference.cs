using BdoComboSetup.Core.GameData;

namespace BdoComboSetup.Core.Characters;

/// <summary>
/// Infers a character's class from the skill numbers in its files. Every distinct skill gives its classes an equal share
/// of one vote, so a skill shared by nine classes counts for less than a skill of one class.
/// </summary>
public static class ClassInference
{
    private const double TieTolerance = 1e-9;

    /// <returns>The class with the most votes, or null when no skill is known to the table or the top two classes tie.</returns>
    public static int? Infer(IEnumerable<int> skillNos, SkillClassTable table)
    {
        ArgumentNullException.ThrowIfNull(skillNos);
        ArgumentNullException.ThrowIfNull(table);

        var votes = new Dictionary<int, double>();
        foreach (var skillNo in skillNos.Distinct())
        {
            var classes = table.ClassesOf(skillNo);
            if (classes.Count == 0)
            {
                continue;
            }

            var share = 1.0 / classes.Count;
            foreach (var classType in classes)
            {
                votes[classType] = votes.GetValueOrDefault(classType) + share;
            }
        }

        if (votes.Count == 0)
        {
            return null;
        }

        var ranked = votes.OrderByDescending(vote => vote.Value).ThenBy(vote => vote.Key).ToList();
        if (ranked.Count > 1 && ranked[0].Value - ranked[1].Value < TieTolerance)
        {
            return null;
        }

        return ranked[0].Key;
    }
}
