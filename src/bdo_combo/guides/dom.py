"""A small DOM built on the standard library's HTML parser.

Only what the guide importer needs: elements with attributes and children,
class and tag queries, and whitespace-normalised text. The site's markup is
well formed, but an unmatched end tag is ignored rather than failing the page.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from html.parser import HTMLParser
from typing import Iterator

VOID_TAGS = frozenset(
    {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"}
)


@dataclass
class Element:
    tag: str
    attrs: dict[str, str] = field(default_factory=dict)
    children: list[Element | str] = field(default_factory=list)

    @property
    def classes(self) -> frozenset[str]:
        return frozenset(self.attrs.get("class", "").split())

    def has_class(self, name: str) -> bool:
        return name in self.classes

    def elements(self) -> Iterator[Element]:
        """Child elements, in document order (text nodes skipped)."""
        return (child for child in self.children if isinstance(child, Element))

    def descendants(self) -> Iterator[Element]:
        """All descendant elements, depth first, in document order (self excluded)."""
        for child in self.elements():
            yield child
            yield from child.descendants()

    def select(self, tag: str | None = None, cls: str | None = None) -> list[Element]:
        """Descendants matching a tag name and/or a single class token."""
        return [
            el
            for el in self.descendants()
            if (tag is None or el.tag == tag) and (cls is None or el.has_class(cls))
        ]

    def first(self, tag: str | None = None, cls: str | None = None) -> Element | None:
        found = self.select(tag=tag, cls=cls)
        return found[0] if found else None

    def text(self) -> str:
        """All text under this element, with runs of whitespace collapsed to one space."""
        return " ".join(_raw_text(self).split())


def _raw_text(node: Element) -> str:
    parts: list[str] = []
    for child in node.children:
        if isinstance(child, str):
            parts.append(child)
        else:
            parts.append(_raw_text(child))
    return "".join(parts)


class _TreeBuilder(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.root = Element("#document")
        self._stack: list[Element] = [self.root]

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        element = self._new(tag, attrs)
        if tag not in VOID_TAGS:
            self._stack.append(element)

    def handle_startendtag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        self._new(tag, attrs)

    def handle_endtag(self, tag: str) -> None:
        for index in range(len(self._stack) - 1, 0, -1):
            if self._stack[index].tag == tag:
                del self._stack[index:]
                return

    def handle_data(self, data: str) -> None:
        self._stack[-1].children.append(data)

    def _new(self, tag: str, attrs: list[tuple[str, str | None]]) -> Element:
        element = Element(tag, {name: value or "" for name, value in attrs})
        self._stack[-1].children.append(element)
        return element


def parse_html(text: str) -> Element:
    """Parse an HTML document and return a synthetic ``#document`` root."""
    builder = _TreeBuilder()
    builder.feed(text)
    builder.close()
    return builder.root
