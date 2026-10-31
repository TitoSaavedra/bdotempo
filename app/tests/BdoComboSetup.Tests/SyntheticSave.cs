namespace BdoComboSetup.Tests;

/// <summary>
/// Synthetic gameVariable.xml text with the same shape as the Python tests' fixture: CRLF, tabs, unrelated fragments,
/// no BOM. It contains no real player data.
/// </summary>
internal static class SyntheticSave
{
    public static readonly int[] SlotSkills = [6457, 5157, 5167, 5179, 5161, 0, 5172, 5177, 5164, 5014, 5124, 0, 5153, 5181, 0, 5152, 5180, 0, 0, 0];

    public static readonly string[] MemoEntries =
    [
        "<SkillCoolTimeMemo SkillNo=\"5011\" Memo=\"DIR + DER\"/>",
        "<SkillCoolTimeMemo SkillNo=\"5103\" Memo=\" R 2\"/>",
        "<SkillCoolTimeMemo SkillNo=\"5124\" Memo=\"S + F\"/>",
        "<SkillCoolTimeMemo SkillNo=\"5154\" Memo=\"S + IZQ\"/>",
        "<SkillCoolTimeMemo SkillNo=\"5200\" Memo=\"A &amp; B &quot;x&quot; &lt;y&gt;\"/>",
        "<SkillCoolTimeMemo SkillNo=\"5162\" Memo=\"SH + DER \"/>",
    ];

    /// <summary>
    /// The file text. <paramref name="memoMode"/> is "present", "missing" or "self_closed". <paramref name="quickSection"/>, when
    /// given, is inserted before the last two fragments.
    /// </summary>
    public static string Text(string memoMode = "present", bool crlf = true, string? quickSection = null)
    {
        var lines = new List<string>
        {
            "<CheckQuestList Version=\"1\">",
            "\t<Quest Id=\"1\" State=\"2\"/>",
            "</CheckQuestList>",
            "<QuestSortType Version=\"1\">",
            "</QuestSortType>",
            "<ShortcutKey Version=\"15\">",
            "\t<Key Index=\"0\" Code=\"81\" Note=\"Q &amp; W\"/>",
            "</ShortcutKey>",
            "<ChattingMacro Version=\"2\"/>",
            "<SkillCoolTimeSlot Version=\"2\">",
        };
        for (var i = 0; i < SlotSkills.Length; i++)
        {
            lines.Add($"\t<SkillCoolTimeSlot SlotNo=\"{i}\" SkillNo=\"{SlotSkills[i]}\"/>");
        }

        lines.Add("</SkillCoolTimeSlot>");
        lines.Add("<ItemLockedInInventory Version=\"3\"/>");
        lines.Add("<QuestOption Version=\"2\">");
        lines.Add("\t<Option Name=\"x &amp; y\"/>");
        lines.Add("</QuestOption>");

        switch (memoMode)
        {
            case "present":
                lines.Add("<SkillCoolTimeMemo Version=\"1\">");
                lines.AddRange(MemoEntries.Select(entry => "\t" + entry));
                lines.Add("</SkillCoolTimeMemo>");
                break;
            case "self_closed":
                lines.Add("<SkillCoolTimeMemo Version=\"1\"/>");
                break;
            case "missing":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(memoMode), memoMode, null);
        }

        if (quickSection is not null)
        {
            lines.Add(quickSection);
        }

        lines.Add("<UISettingPreset Version=\"1\"/>");
        lines.Add("<PetOrderList Version=\"3\"/>");

        var newline = crlf ? "\r\n" : "\n";
        return string.Join(newline, lines) + newline + newline;
    }

    /// <summary>
    /// A QuickSlotData section with 20 records: skill records at keys 0, 1 and 6 (one with an extra attribute, one without
    /// Memo), empty records elsewhere. Joined with <paramref name="newline"/>.
    /// </summary>
    public static string QuickSection(string newline = "\r\n")
    {
        var lines = new List<string> { "<QuickSlotData Version=\"3\">" };
        lines.Add("\t<QuickSlotSkillData Key=\"0\" SkillNo=\"5153\" SkillLevel=\"1\" Position=\"0.000000 0.000000\" KeyboardKey=\"48\" Memo=\"\"/>");
        lines.Add("\t<QuickSlotSkillData Key=\"1\" SkillNo=\"231\" SkillLevel=\"1\" Position=\"0.000000 0.000000\" KeyboardKey=\"49\" Memo=\"\" Note=\"kept\"/>");
        for (var key = 2; key < 20; key++)
        {
            if (key == 6)
            {
                lines.Add("\t<QuickSlotSkillData Key=\"6\" SkillNo=\"6457\" SkillLevel=\"1\" Position=\"0.000000 0.000000\" KeyboardKey=\"50\"/>");
            }
            else if (key >= 9)
            {
                lines.Add($"\t<QuickEmpty Key=\"{key}\" Position=\"0.000000 0.000000\" KeyboardKey=\"0\"/>");
            }
            else
            {
                lines.Add($"\t<QuickEmpty Key=\"{key}\" Position=\"0.000000 0.000000\" KeyboardKey=\"0\" Memo=\"\"/>");
            }
        }

        lines.Add("</QuickSlotData>");
        return string.Join(newline, lines);
    }

    /// <summary>The same section with key 2 holding an item record (never written by the tool).</summary>
    public static string QuickSectionWithItem(string newline = "\r\n") =>
        QuickSection(newline).Replace(
            "\t<QuickEmpty Key=\"2\" Position=\"0.000000 0.000000\" KeyboardKey=\"0\" Memo=\"\"/>",
            "\t<QuickSlotItemData Key=\"2\" ItemKey=\"9321\" ItemLevel=\"0\" Position=\"0.000000 0.000000\" KeyboardKey=\"50\" ItemNo=\"10000000000000002\" WhereType=\"0\" Memo=\"\"/>",
            StringComparison.Ordinal);
}
