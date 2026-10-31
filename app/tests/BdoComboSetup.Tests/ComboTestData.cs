using System.Globalization;
using BdoComboSetup.Core.Combo;
using BdoComboSetup.Core.Localization;

namespace BdoComboSetup.Tests;

/// <summary>Builds guides in code, so the editor tests do not depend on the bundle.</summary>
internal static class ComboTestData
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static ComboSkill Skill(int skillNo, string en, string es, string? description = null) =>
        new(skillNo, new BilingualText(en, es), description, () => null);

    /// <summary>
    /// A guide with two chains: "Damage combo" (3 steps, the first one a Hotkey step) and "Auto buffs" (1 step).
    /// Skill numbers: 100 Flow, 200 Moonlight, 300 Heuklang, 400 Dragonblood, 500 Skybound (only in add-ons).
    /// </summary>
    public static ComboGuide TamerGuide(string title = "Tamer Awakening") => new(
        title,
        "PvE Combo Guide",
        "https://bdoclasshub.com/classes/tamer/awakening/index.html",
        [
            new ComboChain("skill-rotation", "Damage Combo", ["Repeat from the top."],
            [
                new ComboStep("Hotkey", [Skill(100, "Flow: Intimidation", "Flujo: Intimidación", "Projects the last hit.")], "opener", null),
                new ComboStep("R 2", [Skill(200, "Moonlight Strike", "Golpe de la Luna")], null, "Dragonblood"),
                new ComboStep("E", [Skill(300, "Summon Heuklang", "Invocar a Heuklang"), Skill(400, "Dragonblood", "Sangre de dragón")], null, null),
            ]),
            new ComboChain("buffs", "Auto buffs", [], [new ComboStep("F", [Skill(300, "Summon Heuklang", "Invocar a Heuklang")], null, null)]),
        ],
        [],
        [new ComboAddon("class-addons", Skill(500, "Skybound Stab", "Estocada celestial"), "Thrust.", [])]);

    public static ComboGuide ShaiGuide() => new(
        "Shai Talent",
        null,
        "https://bdoclasshub.com/classes/shai/index.html",
        [],
        [],
        []);
}
