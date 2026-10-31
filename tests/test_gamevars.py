import unittest

from bdo_combo import gamevars
from bdo_combo.gamevars import GameVarsError
from support import MEMO_ENTRIES, SLOT_SKILLS, game_file_text

COMBO_SKILLS = [1937, 1921, 1912, 1926, 17, 1306, 1932]
COMBO_MEMOS = {1937: "R 1", 1921: "SH + DER", 1912: "F (mantener)", 1926: "A + DER", 17: "SH + Z", 1306: "F"}


def section_bounds(text: str, open_tag: str, close_tag: str) -> tuple[int, int]:
    start = text.index(open_tag)
    end = text.index(close_tag, start) + len(close_tag)
    return start, end


class ApplyTest(unittest.TestCase):
    def setUp(self) -> None:
        self.original = game_file_text("present")
        self.result = gamevars.apply_combo(self.original, COMBO_SKILLS, COMBO_MEMOS)

    def test_result_passes_verification(self) -> None:
        self.assertEqual([], gamevars.verify(self.original, self.result, COMBO_SKILLS, COMBO_MEMOS))

    def test_apply_is_idempotent(self) -> None:
        again = gamevars.apply_combo(self.result, COMBO_SKILLS, COMBO_MEMOS)
        self.assertEqual(self.result.encode("utf-8"), again.encode("utf-8"))

    def test_only_the_two_sections_change(self) -> None:
        slot_start, slot_end = section_bounds(self.original, "<SkillCoolTimeSlot Version", "</SkillCoolTimeSlot>")
        memo_start, memo_end = section_bounds(self.original, "<SkillCoolTimeMemo Version", "</SkillCoolTimeMemo>")
        new_slot_start, new_slot_end = section_bounds(self.result, "<SkillCoolTimeSlot Version", "</SkillCoolTimeSlot>")
        new_memo_start, new_memo_end = section_bounds(self.result, "<SkillCoolTimeMemo Version", "</SkillCoolTimeMemo>")
        self.assertEqual(self.original[:slot_start], self.result[:new_slot_start])
        self.assertEqual(self.original[slot_end:memo_start], self.result[new_slot_end:new_memo_start])
        self.assertEqual(self.original[memo_end:], self.result[new_memo_end:])

    def test_crlf_and_no_bom_preserved(self) -> None:
        raw = self.result.encode("utf-8")
        self.assertFalse(raw.startswith(b"\xef\xbb\xbf"))
        self.assertEqual(raw.count(b"\n"), raw.count(b"\r\n"))
        self.assertEqual(raw.decode("utf-8"), self.result)

    def test_slot_section_has_20_entries_with_padding(self) -> None:
        parsed = gamevars.parse(self.result)
        self.assertEqual(20, len(parsed.slots))
        self.assertEqual(COMBO_SKILLS, list(parsed.slots[: len(COMBO_SKILLS)]))
        self.assertEqual([0] * (20 - len(COMBO_SKILLS)), list(parsed.slots[len(COMBO_SKILLS):]))

    def test_memos_sorted_unique_and_unrelated_kept(self) -> None:
        parsed = gamevars.parse(self.result)
        numbers = [m.skill_no for m in parsed.memos]
        self.assertEqual(sorted(numbers), numbers)
        self.assertEqual(len(numbers), len(set(numbers)))
        self.assertIn('<SkillCoolTimeMemo SkillNo="5011" Memo="DIR + DER"/>', self.result)
        self.assertIn('<SkillCoolTimeMemo SkillNo="5162" Memo="SH + DER "/>', self.result)
        self.assertIn('<SkillCoolTimeMemo SkillNo="5200" Memo="A &amp; B &quot;x&quot; &lt;y&gt;"/>', self.result)

    def test_memo_values_are_escaped(self) -> None:
        text = gamevars.apply_combo(self.original, [1937], {1937: 'R & L "x" <y>'})
        self.assertIn('Memo="R &amp; L &quot;x&quot; &lt;y&gt;"', text)
        self.assertEqual([], gamevars.verify(self.original, text, [1937], {1937: 'R & L "x" <y>'}))
        self.assertEqual('R & L "x" <y>', gamevars.xml_unescape_attr(gamevars.xml_escape_attr('R & L "x" <y>')))

    def test_existing_memo_for_combo_skill_is_replaced_not_duplicated(self) -> None:
        updated = gamevars.apply_combo(self.original, [5124], {5124: "NEW"})
        self.assertEqual(1, updated.count('<SkillCoolTimeMemo SkillNo="5124"'))
        self.assertIn('<SkillCoolTimeMemo SkillNo="5124" Memo="NEW"/>', updated)
        self.assertNotIn('Memo="S + F"', updated)
        self.assertEqual([], gamevars.verify(self.original, updated, [5124], {5124: "NEW"}))

    def test_tampering_with_other_bytes_is_detected(self) -> None:
        tampered = self.result.replace("Quest Id", "Quest  Id", 1)
        problems = gamevars.verify(self.original, tampered, COMBO_SKILLS, COMBO_MEMOS)
        self.assertTrue(any("outside the slot and memo sections" in p for p in problems))

    def test_wrong_slot_list_is_detected(self) -> None:
        problems = gamevars.verify(self.original, self.result, COMBO_SKILLS[:-1], COMBO_MEMOS)
        self.assertTrue(problems)

    def test_too_many_slots_rejected(self) -> None:
        with self.assertRaises(GameVarsError):
            gamevars.apply_combo(self.original, [1] * 21, {})


class MemoLayoutTest(unittest.TestCase):
    def test_missing_memo_section_is_created_after_slot_section(self) -> None:
        original = game_file_text("missing")
        result = gamevars.apply_combo(original, COMBO_SKILLS, COMBO_MEMOS)
        slot_end = result.index("</SkillCoolTimeSlot>") + len("</SkillCoolTimeSlot>")
        self.assertEqual("\r\n<SkillCoolTimeMemo Version=\"1\">", result[slot_end: slot_end + len("\r\n<SkillCoolTimeMemo Version=\"1\">")])
        self.assertEqual([], gamevars.verify(original, result, COMBO_SKILLS, COMBO_MEMOS))

    def test_self_closed_memo_section_is_expanded(self) -> None:
        original = game_file_text("self_closed")
        result = gamevars.apply_combo(original, COMBO_SKILLS, COMBO_MEMOS)
        self.assertNotIn('<SkillCoolTimeMemo Version="1"/>', result)
        self.assertIn('<SkillCoolTimeMemo Version="1">', result)
        self.assertEqual([], gamevars.verify(original, result, COMBO_SKILLS, COMBO_MEMOS))

    def test_no_memos_and_no_section_writes_self_closed_section(self) -> None:
        original = game_file_text("missing")
        result = gamevars.apply_combo(original, [1937], {})
        self.assertIn('<SkillCoolTimeMemo Version="1"/>', result)
        self.assertEqual([], gamevars.verify(original, result, [1937], {}))

    def test_missing_memo_and_self_closed_give_same_slots(self) -> None:
        a = gamevars.apply_combo(game_file_text("missing"), COMBO_SKILLS, COMBO_MEMOS)
        b = gamevars.apply_combo(game_file_text("self_closed"), COMBO_SKILLS, COMBO_MEMOS)
        self.assertEqual(gamevars.parse(a).slots, gamevars.parse(b).slots)
        self.assertEqual(gamevars.parse(a).memos, gamevars.parse(b).memos)

    def test_slot_section_with_wrong_count_is_refused(self) -> None:
        broken = game_file_text("present").replace('\t<SkillCoolTimeSlot SlotNo="19" SkillNo="0"/>\r\n', "")
        with self.assertRaises(GameVarsError):
            gamevars.parse(broken)

    def test_missing_slot_section_is_refused(self) -> None:
        text = game_file_text("present").replace("SkillCoolTimeSlot", "OtherSection")
        with self.assertRaises(GameVarsError):
            gamevars.apply_combo(text, [1], {})

    def test_parse_reads_existing_memos(self) -> None:
        parsed = gamevars.parse(game_file_text("present"))
        self.assertEqual(len(MEMO_ENTRIES), len(parsed.memos))
        self.assertEqual(SLOT_SKILLS, list(parsed.slots))


if __name__ == "__main__":
    unittest.main()
