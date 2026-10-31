namespace BdoComboSetup.Core.Game;

/// <summary>
/// Synthetic UI layouts for the demo mode (screenshots and tests). They are invented, not copied from a player's file:
/// the demo never reads the account's gamevariable.xml. Each layout places the 20 cooldown and 20 quick slots,
/// with some quick slots and one cooldown slot hidden by the game (IsShow=false) to exercise the hidden-slots toggle.
/// </summary>
public static class DemoGameLayouts
{
    public const double GameWidth = 3440;
    public const double GameHeight = 1440;

    /// <summary>The display of the demo screen: the same 3440 x 1440 window as the sample screenshot, UI scale 1.</summary>
    public static GameDisplaySettings Display { get; } = new((int)GameWidth, (int)GameHeight, Windowed: true, UiScale: 1.0);

    /// <summary>The four layouts, every id present. Presets 1-3 move the bars around so the layouts are visibly different.</summary>
    public static GameUiLayoutSet Create()
    {
        var layouts = new Dictionary<GameUiLayoutId, GameUiLayout>
        {
            [GameUiLayoutId.General] = new(GameUiLayoutId.General, General()),
            [GameUiLayoutId.Preset1] = new(GameUiLayoutId.Preset1, Preset1()),
            [GameUiLayoutId.Preset2] = new(GameUiLayoutId.Preset2, Preset2()),
            [GameUiLayoutId.Preset3] = new(GameUiLayoutId.Preset3, Preset3()),
        };
        return new GameUiLayoutSet(layouts, malformedRows: 0);
    }

    /// <summary>Bottom-centre bars: cooldown 2 x 10 above a quick row of 10; quick 11-20 hidden off to the left.</summary>
    private static List<UiPanel> General() => Bars(
        cooldownLeft: 1443, cooldownTop: 1218, cooldownRowGap: 56, columnGap: 56,
        quickLeft: 1443, quickCentreY: 1390, hiddenQuickLeft: 400, hiddenCooldown: null);

    /// <summary>The bars shifted to the left, with cooldown slot 5 hidden.</summary>
    private static List<UiPanel> Preset1() => Bars(
        cooldownLeft: 700, cooldownTop: 1218, cooldownRowGap: 56, columnGap: 56,
        quickLeft: 700, quickCentreY: 1372, hiddenQuickLeft: 2500, hiddenCooldown: 5);

    /// <summary>The bars on the right, quick slots 6-10 hidden.</summary>
    private static List<UiPanel> Preset2() => Bars(
        cooldownLeft: 2300, cooldownTop: 1150, cooldownRowGap: 56, columnGap: 56,
        quickLeft: 2300, quickCentreY: 1300, hiddenQuickLeft: 400, hiddenCooldown: null, hiddenQuickFrom: 6, hiddenQuickTo: 10);

    /// <summary>A top-left block: everything visible, quick row above the cooldown rows.</summary>
    private static List<UiPanel> Preset3() => Bars(
        cooldownLeft: 80, cooldownTop: 140, cooldownRowGap: 56, columnGap: 56,
        quickLeft: 80, quickCentreY: 60, hiddenQuickLeft: 2600, hiddenCooldown: null, quickVisibleAll: true);

    private static List<UiPanel> Bars(
        double cooldownLeft,
        double cooldownTop,
        double cooldownRowGap,
        double columnGap,
        double quickLeft,
        double quickCentreY,
        double hiddenQuickLeft,
        int? hiddenCooldown,
        int hiddenQuickFrom = 0,
        int hiddenQuickTo = 0,
        bool quickVisibleAll = false)
    {
        var rows = new List<UiPanel>();

        // Cooldown slots 1-10 on the first row, 11-20 on the second. Centres are the first slot's centre plus the column step.
        for (var number = 1; number <= GameSlotIndices.CooldownSlotCount; number++)
        {
            var column = (number - 1) % 10;
            var row = (number - 1) / 10;
            var centreX = cooldownLeft + 25 + (column * columnGap);
            var centreY = cooldownTop + 25 + (row * cooldownRowGap);
            var show = number != hiddenCooldown;
            rows.Add(Row(GameSlotIndices.CooldownPanelIndex(number), show, centreX, centreY, 50, 50));
        }

        // Quick slots 1-10 in one row under the cooldown bar; 11-20 are hidden at a far position.
        for (var number = 1; number <= QuickSlotKeys.SlotCount; number++)
        {
            if (number <= 10)
            {
                var centreX = quickLeft + 25 + ((number - 1) * columnGap);
                var hidden = number >= hiddenQuickFrom && number <= hiddenQuickTo;
                rows.Add(Row(GameSlotIndices.QuickPanelIndex(number), !hidden, centreX, quickCentreY, 50, 65));
            }
            else
            {
                var centreX = hiddenQuickLeft + 25 + ((number - 11) * columnGap);
                rows.Add(Row(GameSlotIndices.QuickPanelIndex(number), quickVisibleAll, centreX, quickCentreY, 50, 65));
            }
        }

        return rows;
    }

    /// <summary>A row stored the way the game stores it: a top-left corner (LeftTop) and the centre divided by the screen size.</summary>
    private static UiPanel Row(int index, bool show, double centreX, double centreY, double width, double height) => new(
        Index: index,
        WindowIndex: null,
        IsShow: show,
        Anchor: UiAnchor.LeftTop,
        PosX: centreX - (width / 2),
        PosY: centreY - (height / 2),
        SizeX: null,
        SizeY: null,
        RelativeX: centreX / GameWidth,
        RelativeY: centreY / GameHeight);
}
