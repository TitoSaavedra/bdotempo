"""Tests for the small HTML tree used by the guide importer. Synthetic markup only."""

import unittest

from bdo_combo.guides.dom import parse_html


class ParseHtmlTests(unittest.TestCase):
    def test_nested_elements_classes_and_text(self):
        root = parse_html('<div class="a  b"><p class="x">Hello <b>big</b>\n  world</p></div>')

        div = root.first(tag="div")
        self.assertIsNotNone(div)
        self.assertTrue(div.has_class("a"))
        self.assertTrue(div.has_class("b"))
        self.assertFalse(div.has_class("x"))
        self.assertEqual("Hello big world", root.first(tag="p").text())

    def test_void_elements_do_not_swallow_their_siblings(self):
        root = parse_html('<li><img src="a.webp"><span class="name">Flow</span></li>')

        li = root.first(tag="li")
        self.assertEqual(["img", "span"], [child.tag for child in li.elements()])
        self.assertEqual("Flow", root.first(cls="name").text())

    def test_self_closing_tags_have_no_children(self):
        root = parse_html('<div><img src="x.webp" /><span>after</span></div>')

        div = root.first(tag="div")
        self.assertEqual(["img", "span"], [child.tag for child in div.elements()])
        self.assertEqual([], root.first(tag="img").children)

    def test_unmatched_end_tag_is_ignored(self):
        root = parse_html("<div><span>one</span></p></div>")

        self.assertEqual("one", root.first(tag="span").text())
        self.assertEqual(1, len(root.select(tag="div")))

    def test_select_finds_descendants_in_document_order(self):
        root = parse_html('<ul><li class="item">1</li><li><ul><li class="item">2</li></ul></li></ul>')

        self.assertEqual(["1", "2"], [el.text() for el in root.select(tag="li", cls="item")])

    def test_entities_are_decoded(self):
        root = parse_html("<p>Tom &amp; Jerry &#x27;s</p>")

        self.assertEqual("Tom & Jerry 's", root.first(tag="p").text())


if __name__ == "__main__":
    unittest.main()
