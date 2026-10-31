"""Tests for reading one guide page into chains, steps, notes and add-ons. Synthetic pages only."""

import unittest

from bdo_combo.guides.dom import parse_html
from bdo_combo.guides.pages import (
    data_paths,
    has_expert_link,
    parse_guide,
    skill_number_from_icon,
)
from guides_support import (
    chain,
    combo_section,
    page,
    skill_step,
    tamer_awakening_page,
    tips_aside,
)


class SkillNumberTests(unittest.TestCase):
    def test_number_is_the_digits_before_the_first_underscore(self):
        self.assertEqual(1926, skill_number_from_icon("../skills/icons/1926_legendary_beast_dance_iv.webp"))
        self.assertEqual(1347, skill_number_from_icon("1347_summon_heuklang_xi.webp?v=3"))

    def test_icons_without_a_number_give_none(self):
        self.assertIsNone(skill_number_from_icon("../skills/icons/pbw_skill.webp"))
        self.assertIsNone(skill_number_from_icon("../assets/alchemy-stones/stone.webp"))
        self.assertIsNone(skill_number_from_icon("../skills/icons/1926.webp"))


class ParseTamerPageTests(unittest.TestCase):
    def setUp(self):
        self.guide = parse_guide(parse_html(tamer_awakening_page()))

    def test_title_and_kicker_come_from_the_first_section(self):
        self.assertEqual("Tamer Awakening", self.guide.title)
        self.assertEqual("PvE Combo Guide", self.guide.description)

    def test_damage_chain_has_13_steps_from_1937_to_1932(self):
        damage = next(chain_ for chain_ in self.guide.chains if chain_["title"] == "Damage Combo (Dragon LBD)")

        self.assertEqual(13, len(damage["steps"]))
        self.assertEqual(1937, damage["steps"][0]["skills"][0]["skillNo"])
        self.assertEqual(1932, damage["steps"][-1]["skills"][0]["skillNo"])
        self.assertEqual("Flow: Intimidation", damage["steps"][0]["skills"][0]["nameEn"])

    def test_keys_extra_and_chain_note_are_read(self):
        damage = next(chain_ for chain_ in self.guide.chains if chain_["title"] == "Damage Combo (Dragon LBD)")

        self.assertEqual("Hotkey", damage["steps"][0]["keys"])
        self.assertEqual("opener", damage["steps"][0]["extra"])
        self.assertEqual("Shift+RMB", damage["steps"][2]["keys"])
        self.assertEqual(["After Moonlight Strike, go back to Allround Spinner."], damage["notes"])

    def test_self_buff_step_has_the_skill_and_extra(self):
        self_buff = self.guide.chains[0]

        self.assertEqual("Self Buff", self_buff["title"])
        self.assertEqual({"skillNo": 5467, "nameEn": "Absorb Heuklang"}, self_buff["steps"][1]["skills"][0])
        self.assertEqual("E buff", self_buff["steps"][1]["extra"])

    def test_tips_box_becomes_notes_with_inline_skills(self):
        self.assertEqual(1, len(self.guide.notes))
        note = self.guide.notes[0]
        self.assertEqual("skill-rotation", note["section"])
        self.assertTrue(note["text"].startswith("Core DPS first:"))
        self.assertEqual([{"skillNo": 1921, "nameEn": "Allround Spinner"}], note["skills"])

    def test_add_on_reads_skill_text_and_effects(self):
        self.assertEqual(1, len(self.guide.addons))
        addon = self.guide.addons[0]
        self.assertEqual({"skillNo": 1910, "nameEn": "Skybound Stab I"}, addon["skill"])
        self.assertEqual("Thrust the staff.", addon["description"])
        self.assertEqual([{"label": "Attack Speed", "value": "+10% for 12 sec"}], addon["effects"])

    def test_no_problems_on_a_clean_page(self):
        self.assertEqual([], self.guide.problems)
        self.assertTrue(self.guide.has_combo_content)


class ParseStepEdgeCaseTests(unittest.TestCase):
    def test_text_only_skill_goes_to_the_step_note(self):
        text_only = '<span class="combo-skill"><span class="combo-skill-name">Forward run</span></span>'
        step = (
            '<li class="combo-step"><div class="combo-keys"><kbd>S+LMB</kbd></div>'
            '<div class="combo-skills"><span class="combo-skill"><img class="combo-skill-icon" '
            'src="../skills/icons/1456_chase_ii.webp" alt="" /><span class="combo-skill-name">Chase</span></span>'
            f"{text_only}</div></li>"
        )
        html = page(combo_section("skill-rotation", "Musa", chain("Filler", [step])))

        step = parse_guide(parse_html(html)).chains[0]["steps"][0]

        self.assertEqual([{"skillNo": 1456, "nameEn": "Chase"}], step["skills"])
        self.assertEqual("Forward run", step["note"])

    def test_solo_annotation_and_form_chip_are_kept_as_note(self):
        html = page(combo_section(
            "skill-rotation",
            "Berserker",
            chain("Opener", [skill_step("Shift+D → RMB", [(1100, "Evasion"), (1101, "Fierce Strike")], annotation="Dragonblood")]),
        ))

        step = parse_guide(parse_html(html)).chains[0]["steps"][0]

        self.assertEqual("Dragonblood", step["note"])
        self.assertEqual("Shift+D → RMB", step["keys"])
        self.assertEqual(2, len(step["skills"]))

    def test_step_without_keys_is_allowed(self):
        html = page(combo_section("class-specifics", "Class Specifics", chain("Skills to Lock", [skill_step(None, [(2927, "Absolute: Throat-Burn")])])))

        step = parse_guide(parse_html(html)).chains[0]["steps"][0]

        self.assertIsNone(step["keys"])
        self.assertEqual(2927, step["skills"][0]["skillNo"])

    def test_icon_without_a_number_is_skipped_and_reported(self):
        bad_step = skill_step("F", [(1, "Pending")]).replace("1_skill.webp", "pending_skill.webp")
        html = page(combo_section("skill-rotation", "X", chain("Broken", [bad_step, skill_step("LMB", [(2, "Fine")])])))

        guide = parse_guide(parse_html(html))

        self.assertEqual([{"skillNo": 2, "nameEn": "Fine"}], guide.chains[0]["steps"][1]["skills"])
        self.assertEqual([], guide.chains[0]["steps"][0]["skills"])
        self.assertEqual(1, len(guide.problems))

    def test_intro_and_loop_paragraphs_become_chain_notes_in_order(self):
        body = (
            '<div class="combo-chain"><h3 class="combo-chain-title">Loop</h3>'
            '<p class="combo-intro">Start here.</p>'
            f'<ol class="combo-steps">{skill_step("Hotkey", [(7, "Seven")])}</ol>'
            '<p class="combo-repeat combo-loop-cue">Repeat from the top.</p></div>'
        )

        guide = parse_guide(parse_html(page(combo_section("skill-rotation", "Loop", body))))

        self.assertEqual(["Start here.", "Repeat from the top."], guide.chains[0]["notes"])

    def test_page_without_guide_sections_has_no_combo_content(self):
        guide = parse_guide(parse_html("<html><body><p>Coming soon</p></body></html>"))

        self.assertFalse(guide.has_combo_content)
        self.assertEqual("", guide.title)
        self.assertIsNone(guide.description)


class ClassPageHelpersTests(unittest.TestCase):
    def setUp(self):
        self.root = parse_html(page(
            '<div class="class-experts">'
            '<a href="../../experts/shai-azminek/index.html?from=class" data-class="shai" data-path="talent">Azminek</a>'
            '<a href="../../experts/shai-wootaru/index.html?from=class" data-class="shai" data-path="talent">Wootaru</a>'
            "</div>",
        ))

    def test_data_paths_lists_the_paths_on_the_page(self):
        self.assertEqual({"talent"}, data_paths(self.root))

    def test_expert_link_gives_the_path_of_that_expert(self):
        self.assertEqual("talent", has_expert_link(self.root, "shai", "azminek"))
        self.assertIsNone(has_expert_link(self.root, "shai", "ninja"))


def note_list_section(section_id: str, title: str, heading: str, items: list[tuple[int, str, str]]) -> str:
    """A section written as a list of notes (as the cancels and spiritforging sections are): one aside, one list."""
    rows = "".join(
        f'<li>{text} <span class="skill-inline"><img class="skill-inline-icon" '
        f'src="../skills/icons/{number}_{name.lower().replace(" ", "_")}.webp" alt="" />{name}</span>.</li>'
        for number, name, text in items
    )
    aside = f'<aside class="combo-notes"><h3 class="combo-notes-title">{heading}</h3><ul>{rows}</ul></aside>'
    return combo_section(section_id, title, aside, kicker="Notes")


class NoteListSectionTests(unittest.TestCase):
    def test_cancels_section_becomes_one_chain_with_a_step_per_note(self):
        html = page(note_list_section(
            "class-cancels",
            "Cancels & Input Traps",
            "Notable Cancels",
            [(2239, "Hellfire", "Magma Bomb is faster after"), (6464, "Magma Bomb", "Hellfire goes faster into")],
        ))

        guide = parse_guide(parse_html(html))

        self.assertEqual(1, len(guide.chains))
        cancels = guide.chains[0]
        self.assertEqual("class-cancels", cancels["section"])
        self.assertEqual("Cancels & Input Traps", cancels["title"])
        self.assertEqual(["Notable Cancels", "Notable Cancels"], [step["extra"] for step in cancels["steps"]])
        self.assertEqual([2239, 6464], [step["skills"][0]["skillNo"] for step in cancels["steps"]])
        self.assertEqual("Hellfire", cancels["steps"][0]["skills"][0]["nameEn"])
        self.assertIsNone(cancels["steps"][0]["keys"])
        self.assertEqual([], guide.notes)

    def test_spiritforging_section_is_kept_as_a_chain_so_its_skills_keep_their_numbers(self):
        html = page(note_list_section(
            "spiritforging",
            "Spiritforging",
            "Losing Spiritforged State",
            [(7365, "Foxflare Ambush", "Spiritforged state is lost when you stop")],
        ))

        guide = parse_guide(parse_html(html))

        self.assertEqual("spiritforging", guide.chains[0]["section"])
        self.assertEqual(7365, guide.chains[0]["steps"][0]["skills"][0]["skillNo"])

    def test_tips_box_of_a_normal_section_stays_a_note(self):
        html = page(combo_section(
            "skill-rotation",
            "Tamer Awakening",
            chain("DPS", [skill_step("F", [(1347, "Summon Heuklang")])]) + tips_aside(["Core DPS first."]),
        ))

        guide = parse_guide(parse_html(html))

        self.assertEqual(["Core DPS first."], [note["text"] for note in guide.notes])
        self.assertEqual([], [chain_ for chain_ in guide.chains if chain_["section"] == "class-cancels"])


if __name__ == "__main__":
    unittest.main()
