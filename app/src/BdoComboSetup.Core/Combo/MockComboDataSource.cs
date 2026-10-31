namespace BdoComboSetup.Core.Combo;

/// <summary>
/// Mock data for the UI. Skill names are the game's Spanish client names, taken from the mockup.
/// The same layout is returned for every mode until the real core exists.
/// </summary>
public sealed class MockComboDataSource : IComboDataSource
{
    private const string MockDescription = "LBD Dragon damage combo";
    private const string MockSourceUrl = "https://bdoclasshub.com/classes/tamer/awakening/index.html";

    /// <summary>Classes of the mock characters, in the game data (Tamer 16, Warrior 0, Shai 17).</summary>
    private const int TamerClassType = 16;
    private const int WarriorClassType = 0;
    private const int ShaiClassType = 17;

    public IReadOnlyList<CharacterOption> GetCharacters() =>
    [
        new CharacterOption("AhrinSong", "10000000006346933", TamerClassType),
        new CharacterOption("Sample Warrior", "10000000000000001", WarriorClassType),
        new CharacterOption("Sample Shai", "10000000000000002", ShaiClassType),
    ];

    public ComboDefinition GetCombo(string className, ComboMode mode) =>
        new(className, mode, MockDescription, MockSourceUrl, BuildSlots());

    private static IReadOnlyList<SlotData> BuildSlots() =>
    [
        new(1, SlotGroup.Combo, "Flujo: Intimidación", "R 1", "Intim.", 7, 1937),
        new(2, SlotGroup.Combo, "Moonlight Strike", "R 2", null, 8, 1932),
        new(3, SlotGroup.Combo, null, null, null, 3),
        new(4, SlotGroup.Combo, null, null, null, 4),
        new(5, SlotGroup.Combo, null, null, null, 5),
        new(6, SlotGroup.Combo, null, null, null, 6),
        new(7, SlotGroup.Combo, null, null, null, 0),
        new(8, SlotGroup.Combo, "Baile de la Bestia Legendaria IV", "A+RMB", null, 0, 1926),
        new(9, SlotGroup.Combo, null, null, null, 9),
        new(10, SlotGroup.Combo, null, null, null, 0),
        new(11, SlotGroup.Combo, null, null, null, 0),
        new(12, SlotGroup.Combo, null, null, null, 0),
        new(13, SlotGroup.PriorityBuff, "Invocar a Heuklang", null, null, 1, 1347),
        new(14, SlotGroup.PriorityBuff, "Absorción de Heuklang VI", "E", null, 2, 231),
        new(15, SlotGroup.PriorityBuff, null, null, null, 0),
        new(16, SlotGroup.Empty, null, null, null, 0),
        new(17, SlotGroup.Empty, null, null, null, 0),
        new(18, SlotGroup.Empty, null, null, null, 0),
        new(19, SlotGroup.Empty, null, null, null, 0),
        new(20, SlotGroup.Empty, null, null, null, 0),
    ];
}
