"""Tests for the chain kind rules and the manual pool. Synthetic titles and steps only, never real game data."""

import unittest

from bdo_combo.guides.importer import manual_pool
from bdo_combo.guides.kinds import (
    AUTO_APPLY_KINDS,
    BUFF,
    CANCELS,
    COMBO,
    HIGH,
    KINDS,
    LOW,
    MANUAL_ONLY_KINDS,
    MEDIUM,
    MOBILITY,
    OTHER,
    classify_chain,
    normalise_title,
)


class KindSetsTests(unittest.TestCase):
    def test_every_kind_is_auto_applied_manual_only_or_other(self):
        self.assertEqual(set(KINDS), AUTO_APPLY_KINDS | MANUAL_ONLY_KINDS | {OTHER})

    def test_auto_and_manual_sets_do_not_overlap(self):
        self.assertFalse(AUTO_APPLY_KINDS & MANUAL_ONLY_KINDS)
        self.assertEqual({BUFF, COMBO}, AUTO_APPLY_KINDS)
        self.assertEqual({MOBILITY, CANCELS}, MANUAL_ONLY_KINDS)

    def test_normalise_title_collapses_spaces_and_lowercases(self):
        self.assertEqual("self buff", normalise_title("  Self \n Buff "))


class CancelsTests(unittest.TestCase):
    def test_class_cancels_section_is_cancels_whatever_the_title(self):
        verdict = classify_chain("class-cancels", "Cancels & Input Traps")

        self.assertEqual(CANCELS, verdict.kind)
        self.assertEqual(HIGH, verdict.confidence)


class BuffTitleTests(unittest.TestCase):
    def test_buff_title_in_a_combo_section_is_high_confidence(self):
        verdict = classify_chain("skill-rotation", "Self Buff")

        self.assertEqual(BUFF, verdict.kind)
        self.assertEqual(HIGH, verdict.confidence)

    def test_buff_like_title_in_a_mobility_section_stays_mobility_so_it_is_never_applied(self):
        # Regression: "Sprint Buff" and "Swap to Awakening" are movement techniques filed under class-mobility.
        sprint = classify_chain("class-mobility", "Sprint Buff", ["Sprint Speed +20%"], ["S+Q"])
        swap = classify_chain("class-mobility", "Swap to Awakening", [None], ["Shift+RMB"])

        self.assertEqual(MOBILITY, sprint.kind)
        self.assertEqual(HIGH, sprint.confidence)
        self.assertEqual(MOBILITY, swap.kind)
        self.assertEqual(LOW, swap.confidence)

    def test_prebuff_and_debuff_spellings_are_buffs(self):
        for title in ("Prebuff / Debuff", "Pre-Buffs", "Prebuff Skills", "Pre-Engage and Buffs"):
            with self.subTest(title=title):
                self.assertEqual(BUFF, classify_chain("skill-rotation", title).kind)

    def test_buff_list_without_key_presses_in_a_reference_section_is_other(self):
        verdict = classify_chain("class-specifics", "Buffs & Debuffs", keys=(None, None, None))

        self.assertEqual(OTHER, verdict.kind)
        self.assertEqual(HIGH, verdict.confidence)

    def test_buff_activation_with_key_presses_in_a_reference_section_is_buff(self):
        verdict = classify_chain(
            "class-specifics",
            "Fighting Buddha State",
            extras=("Absorb 4 hits", "Once the gourd is full"),
            keys=("C", "Q"),
        )

        self.assertEqual(BUFF, verdict.kind)
        self.assertEqual(MEDIUM, verdict.confidence)


class MobilityTests(unittest.TestCase):
    def test_class_mobility_section_is_mobility(self):
        verdict = classify_chain("class-mobility", "Movement Between Packs")

        self.assertEqual(MOBILITY, verdict.kind)
        self.assertEqual(HIGH, verdict.confidence)

    def test_class_mobility_title_without_a_movement_word_is_low_confidence(self):
        verdict = classify_chain("class-mobility", "When You Are in Trouble")

        self.assertEqual(MOBILITY, verdict.kind)
        self.assertEqual(LOW, verdict.confidence)

    def test_dragons_advance_is_a_movement_keyword(self):
        self.assertEqual(HIGH, classify_chain("class-mobility", "With Dragon's Advance").confidence)

    def test_teleport_title_in_a_pack_to_pack_section_is_mobility(self):
        verdict = classify_chain("pack-to-pack", "Teleport to Target")

        self.assertEqual(MOBILITY, verdict.kind)
        self.assertEqual(MEDIUM, verdict.confidence)

    def test_movement_skill_named_as_a_trigger_in_a_combo_title_is_a_combo(self):
        # Regression: "After Every Moonlight Dash" triggers the combo during E/Z buff, it is not a movement chain.
        verdict = classify_chain(
            "skill-rotation",
            "During E / Z Buff: After Every Moonlight Dash",
            extras=(None, None, None),
        )

        self.assertEqual(COMBO, verdict.kind)


class ComboSectionTests(unittest.TestCase):
    def test_every_step_with_a_buff_marker_makes_the_chain_a_buff(self):
        verdict = classify_chain("skill-rotation", "Long Buff Set", ["E buff", "Dragon stance, lasts 1 hour"])

        self.assertEqual(BUFF, verdict.kind)
        self.assertEqual(MEDIUM, verdict.confidence)

    def test_only_some_buff_markers_keep_the_chain_a_combo_with_low_confidence(self):
        verdict = classify_chain("skill-rotation", "Main PvE Combo", ["E buff", None])

        self.assertEqual(COMBO, verdict.kind)
        self.assertEqual(LOW, verdict.confidence)

    def test_combo_section_chain_without_a_title_is_low_confidence(self):
        verdict = classify_chain("skill-rotation", "", [None])

        self.assertEqual(COMBO, verdict.kind)
        self.assertEqual(LOW, verdict.confidence)

    def test_title_with_a_combo_keyword_is_high_confidence(self):
        self.assertEqual(HIGH, classify_chain("skill-rotation", "Main PvE Combo").confidence)

    def test_chain_and_aoe_titles_are_high_confidence_combos(self):
        # Regression: "Chain 1" and "Big Frontal AoEs" are combos, they only lacked a keyword.
        for title in ("Chain 1", "Big Frontal AoEs", "Closer Range AoEs"):
            with self.subTest(title=title):
                verdict = classify_chain("alternative-combos", title)
                self.assertEqual(COMBO, verdict.kind)
                self.assertEqual(HIGH, verdict.confidence)

    def test_prebuff_variants_of_a_pack_to_pack_combo_are_combos(self):
        for title in ("With Prebuffs", "No Prebuffs"):
            with self.subTest(title=title):
                verdict = classify_chain("pack-to-pack", title, [None])
                self.assertEqual(COMBO, verdict.kind)
                self.assertEqual(HIGH, verdict.confidence)

    def test_title_without_a_keyword_stays_low_confidence(self):
        verdict = classify_chain("skill-rotation", "Star's Breath Generation", [None])

        self.assertEqual(COMBO, verdict.kind)
        self.assertEqual(LOW, verdict.confidence)


class OtherSectionTests(unittest.TestCase):
    def test_reference_sections_are_other_with_high_confidence(self):
        for section, title in (
            ("class-specifics", "Skills to Lock"),
            ("class-video", "Infinite Combo (Long)"),
            ("advanced-information", "Further Reading"),
            ("spiritforging", "Spiritforging"),
        ):
            with self.subTest(section=section):
                verdict = classify_chain(section, title)
                self.assertEqual(OTHER, verdict.kind)
                self.assertEqual(HIGH, verdict.confidence)

    def test_unknown_section_is_other_with_low_confidence(self):
        verdict = classify_chain("some-new-section", "Anything")

        self.assertEqual(OTHER, verdict.kind)
        self.assertEqual(LOW, verdict.confidence)


class ManualPoolTests(unittest.TestCase):
    @staticmethod
    def ref(number: int) -> dict:
        return {"skillNo": number, "nameEn": f"Skill {number}", "nameEs": f"Habilidad {number}"}

    def test_pool_holds_mobility_and_cancels_skills_once_in_document_order(self):
        guide = {
            "chains": [
                {"kind": COMBO, "steps": [{"skills": [self.ref(1)]}, {"skills": [self.ref(2)]}]},
                {"kind": MOBILITY, "steps": [{"skills": [self.ref(2)]}, {"skills": [self.ref(3)]}]},
                {"kind": CANCELS, "steps": [{"skills": [self.ref(4)]}, {"skills": [self.ref(3)]}]},
                {"kind": OTHER, "steps": [{"skills": [self.ref(5)]}]},
            ],
        }

        pool = manual_pool(guide)

        self.assertEqual([2, 3, 4], [entry["skillNo"] for entry in pool])
        self.assertEqual({"skillNo": 2, "nameEn": "Skill 2", "nameEs": "Habilidad 2"}, pool[0])

    def test_guide_without_mobility_or_cancels_has_an_empty_pool(self):
        guide = {"chains": [{"kind": BUFF, "steps": [{"skills": [self.ref(1)]}]}]}

        self.assertEqual([], manual_pool(guide))


if __name__ == "__main__":
    unittest.main()
