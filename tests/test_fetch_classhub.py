"""Tests for scripts/fetch_classhub.py. Synthetic pages and a fake transport only: no network."""

import contextlib
import hashlib
import io
import json
import sys
import unittest
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS_DIR))

import fetch_classhub as fc  # noqa: E402
from support import log_messages, quiet_logs, temp_root  # noqa: E402

WARRIOR_INDEX = fc.BASE_URL + "classes/warrior/index.html"
TAMER_INDEX = fc.BASE_URL + "classes/tamer/index.html"


def url_of(path: str, query: str = "") -> str:
    return fc.BASE_URL + path + (f"?{query}" if query else "")


class FakeTransport:
    """Answers from per-URL queues (the last item repeats). Unknown URLs get 404."""

    def __init__(self, routes: dict[str, list]) -> None:
        self.routes = {url: list(items) for url, items in routes.items()}
        self.calls: list[str] = []
        self.headers: list[dict[str, str]] = []

    def __call__(self, url: str, headers: dict[str, str]) -> fc.Response:
        self.calls.append(url)
        self.headers.append(dict(headers))
        queue = self.routes.get(url)
        if not queue:
            return fc.Response(404, b"")
        item = queue.pop(0) if len(queue) > 1 else queue[0]
        if isinstance(item, BaseException):
            raise item
        return item

    def paths(self) -> list[str]:
        """Site paths asked for, without the base URL and the query string."""
        return [url.removeprefix(fc.BASE_URL).split("?")[0] for url in self.calls]


def ok(text: str) -> fc.Response:
    return fc.Response(200, text.encode("utf-8"))


def status(code: int, retry_after: str | None = None) -> fc.Response:
    return fc.Response(code, b"", retry_after)


def make_client(transport: FakeTransport, sleeps: list[float]) -> fc.Client:
    return fc.Client(transport=transport, sleep=sleeps.append, delay=1.0, backoff=2.0, max_attempts=4)


# --- a small synthetic site -------------------------------------------------

# The spec pages each class page links to (class-relative or site-relative paths).
CLASS_LINKS = {
    "warrior": ["experts/warrior-awakening/index.html"],
    "tamer": [
        "classes/tamer/awakening/index.html",
        "experts/tamer-awakening/index.html",
        "experts/tamer-succession/index.html",
    ],
    "scholar": ["classes/scholar/ascension/index.html", "experts/scholar-ascension/index.html"],
    "wizard": ["experts/maegu-succession/index.html"],  # an expert of another class
}
# The site's own index also lists pages that no class page links to.
EXPERTS_INDEX = [
    {"name": "Meiimo", "class": "tamer", "path": "awakening",
     "href": "classes/tamer/awakening/index.html", "expertHref": "experts/tamer-awakening/index.html"},
    {"name": "Someone", "class": "tamer", "path": "succession",
     "href": "classes/tamer/succession/index.html", "expertHref": "experts/dosa-antido/index.html"},
]
SURVEY = [{"slug": "tamer", "href": "classes/tamer/succession/index.html", "img": "assets/class-portraits/tamer.jpg"}]
SKILL_ICON = "classes/tamer/skills/icons/1926_legendary_beast_dance_iv.webp"
IMAGE_PATHS = [
    SKILL_ICON,
    "classes/tamer/skills/icons/2000_b.webp",
    "classes/tamer/skills/icons/2001_c.webp",
    "classes/tamer/skills/icons/2001_c2.webp",
    "assets/vendor/site/img/classes/class_16/list_profile.jpg",
    "assets/bdo-logo-128.png",
    "assets/hero.jpg",
    "assets/fonts/inter.woff2",
    "assets/fonts/cinzel.woff2",
    "assets/class-portraits/tamer.jpg",
]


def href_for(path: str, class_name: str) -> str:
    """The href a class page uses for a page: class-relative for its own spec pages, expert links with a query."""
    own = f"classes/{class_name}/"
    if path.startswith(own):
        return path.removeprefix(own)
    return f"../../{path}?from=class&return=../../classes/{class_name}/index.html"


def class_page(name: str, links_by_class: dict[str, list[str]]) -> str:
    links = "".join(f'<a href="../{other}/index.html">{other}</a>' for other in links_by_class)
    links += "".join(f'<a href="{href_for(path, name)}">spec</a>' for path in links_by_class[name])
    links += '<a href="../../experts.html">experts</a><a href="https://example.com/classes/x/index.html">x</a>'
    return (
        '<html><head><link rel="stylesheet" href="../../css/styles.css?v=1">'
        '<script src="../../js/skeleton.js?v=polish-1"></script></head>'
        f"<body>{links}</body></html>"
    )


def spec_page(path: str) -> str:
    """A spec page with a relative skill icon, a srcset and a CSS url; the icon is also used by others."""
    return (
        '<html><body><img src="../skills/icons/1926_legendary_beast_dance_iv.webp">'
        '<img data-src="../skills/icons/2000_b.webp" srcset="../skills/icons/2001_c.webp 1x, ../skills/icons/2001_c2.webp 2x">'
        '<a href="../../experts/tamer-succession/index.html">a link is not followed</a>'
        f'<div style="background:url(../../../assets/vendor/site/img/classes/class_16/list_profile.jpg)">{path}</div>'
        "</body></html>"
    )


def other_page(path: str) -> str:
    return f'<html><body><img src="/assets/bdo-logo-128.png">{path}<a href="/classes/tamer/index.html">a</a></body></html>'


def synthetic_site(*, missing: frozenset[str] = frozenset(), class_links: dict | None = None) -> dict[str, list]:
    """Routes for every file of the synthetic site. Paths in ``missing`` answer 404."""
    links = CLASS_LINKS if class_links is None else class_links
    routes: dict[str, list] = {}

    def add(path: str, body: str, query: str = "") -> None:
        if path not in missing:
            routes[url_of(path, query)] = [ok(body)]

    for name in links:
        add(f"classes/{name}/index.html", class_page(name, links))
    pages = {path for paths in links.values() for path in paths}
    pages |= {entry["href"] for entry in EXPERTS_INDEX} | {entry["expertHref"] for entry in EXPERTS_INDEX}
    for path in sorted(pages):
        if path.startswith("classes/"):
            add(path, spec_page(path))
        else:
            add(path, other_page(path))
    add("js/experts-index.json", json.dumps(EXPERTS_INDEX), "v=fh-bio-1")
    add("data/survey-classes.json", json.dumps(SURVEY), "v=survey-data-9")
    add("css/styles.css", "@import url(fonts.css);\n.hero{background:url('../assets/hero.jpg')}\n"
        "@font-face{src:url(../assets/fonts/inter.woff2?v=2)}", "v=1")
    add("css/fonts.css", "@font-face{src:url(../assets/fonts/cinzel.woff2)}")
    add("js/skeleton.js", "// script")
    add("data/skills/labels.json", "{}", "v=st-11")
    add("data/skills/en/tamer.json", json.dumps({"skills": [{"icon": "assets/class-portraits/tamer.jpg"}]}), "v=st-11")
    add("i18n/de/common.json", json.dumps({"Hello": "Hallo"}), "v=i18n-58")
    for path in IMAGE_PATHS:
        if path not in missing:
            routes[url_of(path)] = [fc.Response(200, b"image:" + path.encode("utf-8"))]
    return routes


def run_fetch(out_dir: Path, routes: dict[str, list], *, force: bool = False):
    transport = FakeTransport(routes)
    sleeps: list[float] = []
    with contextlib.redirect_stdout(io.StringIO()) as printed, contextlib.redirect_stderr(io.StringIO()), quiet_logs() as logs:
        fetch = fc.ClassHubFetch(out_dir, make_client(transport, sleeps), force=force)
        code = fetch.run()
    return code, transport, sleeps, logs, printed.getvalue()


def read_document(out_dir: Path) -> dict:
    return json.loads((out_dir / fc.MANIFEST_NAME).read_text(encoding="utf-8"))


def read_manifest(out_dir: Path) -> dict[str, dict]:
    return {entry["path"]: entry for entry in read_document(out_dir)["files"]}


# --- unit tests ---------------------------------------------------------------


class ParseTest(unittest.TestCase):
    def test_class_names_come_from_class_links_only(self) -> None:
        html = (
            '<a href="../tamer/index.html">Tamer</a>'
            '<a href="../dark-knight/index.html">DK</a>'
            '<a href="../tamer/index.html">again</a>'
            '<a href="awakening/index.html">local</a>'
            '<a href="../../experts/tamer-awakening/index.html">expert</a>'
            '<a href="../Bad_Name/index.html">bad</a>'
            '<a href="https://example.com/classes/x/index.html">abs</a>'
        )
        self.assertEqual(["dark-knight", "tamer"], fc.parse_class_names(html))

    def test_resolve_site_ref_drops_query_and_fragment_and_leaves_the_site(self) -> None:
        self.assertEqual(
            ("experts/tamer-awakening/index.html", "from=class&return=../../classes/tamer/index.html"),
            fc.resolve_site_ref("../../experts/tamer-awakening/index.html?from=class&return=../../classes/tamer/index.html#top",
                                "classes/tamer/index.html"),
        )
        self.assertEqual(("classes/tamer/awakening/index.html", ""),
                         fc.resolve_site_ref("awakening/index.html", "classes/tamer/index.html"))
        self.assertEqual(("experts/x/index.html", ""), fc.resolve_site_ref(fc.BASE_URL + "experts/x/index.html", "index.html"))
        self.assertEqual(("assets/a.png", ""), fc.resolve_site_ref("/assets/a.png", "classes/tamer/index.html"))
        for ref in ("https://example.com/experts/x/index.html", "mailto:a@example.com", "javascript:void(0)",
                    "data:image/png;base64,AAAA", "#top", "", "../../"):
            self.assertIsNone(fc.resolve_site_ref(ref, "classes/tamer/index.html"), ref)

    def test_expert_and_spec_pages_are_classified(self) -> None:
        expert = fc.classify_page("experts/dark-knight-awakening/index.html")
        self.assertEqual(("dark-knight", "awakening", "expert"), (expert.class_name, expert.spec, expert.kind))
        local = fc.classify_page("classes/scholar/ascension/index.html")
        self.assertEqual(("scholar", "ascension", "local"), (local.class_name, local.spec, local.kind))
        self.assertEqual("seraph", fc.classify_page("experts/seraph/index.html").class_name)
        self.assertIsNone(fc.classify_page("classes/tamer/awakening/index.html", own_class="scholar"))
        self.assertIsNone(fc.classify_page("classes/tamer/index.html"))

    def test_class_page_discovery_follows_experts_and_own_spec_pages_only(self) -> None:
        html = (
            '<a href="../../experts/ninja-elmermol/index.html?from=class&return=x">expert</a>'
            '<a href="awakening/index.html">own spec</a>'
            '<a href="../tamer/awakening/index.html">another class spec</a>'
            '<a href="../agent/index.html">class link</a>'
            '<a href="../../experts/ninja-elmermol/index.html#x">same expert again</a>'
            '<a href="../../experts/tamer-awakening/index.html">expert of another class</a>'
            '<a href="../../experts/tamer-awakening/extra/index.html">too deep</a>'
            '<a href="../../experts.html">not an expert page</a>'
        )
        found = [(l.class_name, l.spec, l.path, l.kind) for l in fc.discover_class_pages("ninja", html)]
        self.assertEqual(
            [
                ("ninja", "awakening", "classes/ninja/awakening/index.html", "local"),
                ("ninja", "elmermol", "experts/ninja-elmermol/index.html", "expert"),
                ("tamer", "awakening", "experts/tamer-awakening/index.html", "expert"),
            ],
            found,
        )

    def test_asset_references_of_a_page_and_a_stylesheet(self) -> None:
        html = (
            '<img src="../skills/icons/1_a.webp" alt="">'
            '<img srcset="a.jpg 1x, b.jpg 2x" data-src="c.png">'
            '<link rel="stylesheet" href="../../css/styles.css?v=1">'
            '<link rel="canonical" href="https://bdoclasshub.com/classes/x/index.html">'
            '<script src="../../js/skeleton.js?v=polish-1"></script>'
            '<a href="../../experts/x/index.html">not an asset reference</a>'
            '<div style="background:url(\'../assets/bg.jpg\')"></div>'
        )
        refs = fc.html_refs(html)
        for ref in ("../skills/icons/1_a.webp", "a.jpg", "b.jpg", "c.png", "../../css/styles.css?v=1",
                    "../../js/skeleton.js?v=polish-1", "../assets/bg.jpg"):
            self.assertIn(ref, refs)
        self.assertNotIn("../../experts/x/index.html", refs)
        css = fc.css_refs("@font-face{src:url(../assets/x.woff2)} @import url(fonts.css);")
        self.assertIn("../assets/x.woff2", css)
        self.assertIn("fonts.css", css)

    def test_kinds(self) -> None:
        cases = {
            "classes/tamer/index.html": "page",
            "data/skills/labels.json": "tooltip",
            "data/skills/ko/tamer.json": "tooltip",
            "classes/tamer/skills/icons/1926_x.webp": "icon",
            "assets/class-icons/tamer.png": "icon",
            "assets/vendor/site/img/classes/class_16/list_profile.jpg": "image",
            "js/skeleton.js": "script",
            "css/styles.css": "style",
            "assets/fonts/inter.woff2": "style",
            "i18n/de/common.json": "data",
            "site.webmanifest": "data",
        }
        for path, kind in cases.items():
            self.assertEqual(kind, fc.kind_for(path), path)

    def test_retry_after_parsing(self) -> None:
        self.assertEqual(3.0, fc.parse_retry_after("3"))
        self.assertEqual(fc.MAX_RETRY_AFTER_SECONDS, fc.parse_retry_after("9999"))
        self.assertIsNone(fc.parse_retry_after("Wed, 21 Oct 2026 07:28:00 GMT"))
        self.assertIsNone(fc.parse_retry_after("-1"))
        self.assertIsNone(fc.parse_retry_after("nan"))
        self.assertIsNone(fc.parse_retry_after(None))


class ClientTest(unittest.TestCase):
    URL = fc.BASE_URL + "classes/tamer/index.html"

    def test_retry_after_is_honoured_on_429(self) -> None:
        transport = FakeTransport({self.URL: [status(429, "3"), ok("<html/>")]})
        sleeps: list[float] = []
        with quiet_logs():
            result = make_client(transport, sleeps).get(self.URL)
        self.assertEqual("ok", result.status)
        self.assertEqual(2, len(transport.calls))
        self.assertEqual([3.0], sleeps)

    def test_5xx_backs_off_then_gives_up(self) -> None:
        transport = FakeTransport({self.URL: [status(503)]})
        sleeps: list[float] = []
        with quiet_logs():
            result = make_client(transport, sleeps).get(self.URL)
        self.assertEqual("failed", result.status)
        self.assertEqual(503, result.http_status)
        self.assertEqual(4, len(transport.calls))
        self.assertEqual([2.0, 4.0, 8.0], sleeps)

    def test_404_is_not_available_and_not_retried(self) -> None:
        transport = FakeTransport({self.URL: [status(404)]})
        sleeps: list[float] = []
        with quiet_logs():
            result = make_client(transport, sleeps).get(self.URL)
        self.assertEqual("not_found", result.status)
        self.assertEqual(1, len(transport.calls))

    def test_other_client_errors_fail_without_retry(self) -> None:
        transport = FakeTransport({self.URL: [status(403)]})
        with quiet_logs() as logs:
            result = make_client(transport, []).get(self.URL)
        self.assertEqual("failed", result.status)
        self.assertEqual(1, len(transport.calls))
        self.assertTrue(any(m["level"] == "error" for m in log_messages(logs)))

    def test_network_error_is_retried(self) -> None:
        transport = FakeTransport({self.URL: [fc.NetworkError("reset"), ok("<html/>")]})
        with quiet_logs():
            result = make_client(transport, []).get(self.URL)
        self.assertEqual("ok", result.status)
        self.assertEqual(2, len(transport.calls))

    def test_request_identifies_the_tool_and_sends_no_trace_header(self) -> None:
        transport = FakeTransport({self.URL: [ok("<html/>")]})
        with quiet_logs():
            make_client(transport, []).get(self.URL)
        headers = transport.headers[0]
        self.assertIn("bdo-combo-setup", headers["User-Agent"])
        self.assertIn("non-commercial", headers["User-Agent"])
        lowered = {name.lower() for name in headers}
        self.assertNotIn("traceparent", lowered)
        self.assertNotIn("tracestate", lowered)

    def test_first_request_has_no_delay_and_later_ones_do(self) -> None:
        transport = FakeTransport({self.URL: [ok("a")], self.URL + "?b": [ok("b")]})
        sleeps: list[float] = []
        client = make_client(transport, sleeps)
        with quiet_logs():
            client.get(self.URL)
            client.get(self.URL + "?b")
        self.assertEqual([1.0], sleeps)


# --- the run ----------------------------------------------------------------


class RunTest(unittest.TestCase):
    def test_full_run_follows_only_what_the_site_links_and_loads(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            code, transport, sleeps, _, printed = run_fetch(out, synthetic_site())
            self.assertEqual(0, code)
            paths = transport.paths()
            self.assertEqual(len(paths), len(set(transport.calls)), "no URL is requested twice")
            self.assertEqual(len(transport.calls) - 1, len(sleeps), "one polite pause between requests")
            self.assertTrue(all(delay == 1.0 for delay in sleeps))
            self.assertEqual(WARRIOR_INDEX, transport.calls[0])
            # linked pages, including the ones only the site index lists
            for expected in ("experts/tamer-awakening/index.html", "experts/tamer-succession/index.html",
                             "experts/maegu-succession/index.html", "classes/tamer/awakening/index.html",
                             "classes/tamer/succession/index.html", "experts/dosa-antido/index.html"):
                self.assertIn(expected, paths)
            # pages nobody links to are not requested
            self.assertNotIn("classes/scholar/awakening/index.html", paths)
            self.assertNotIn("experts/tamer-succession/extra/index.html", paths)
            # assets: relative refs, srcset, data-src, CSS url() and @import, JSON references
            for expected in (SKILL_ICON, "classes/tamer/skills/icons/2000_b.webp", "classes/tamer/skills/icons/2001_c2.webp",
                             "assets/vendor/site/img/classes/class_16/list_profile.jpg", "assets/bdo-logo-128.png",
                             "css/styles.css", "css/fonts.css", "assets/hero.jpg", "assets/fonts/inter.woff2",
                             "assets/fonts/cinzel.woff2", "assets/class-portraits/tamer.jpg", "assets/class-icons/tamer.png",
                             "js/skeleton.js", "js/experts-index.json", "data/survey-classes.json"):
                self.assertIn(expected, paths)
            self.assertEqual(1, paths.count(SKILL_ICON), "a file linked from two pages is requested once")
            # the tooltip data and the translations the scripts load
            for lang in fc.TOOLTIP_LANGS:
                self.assertIn(f"data/skills/{lang}/tamer.json", paths)
            self.assertIn("data/skills/labels.json", paths)
            self.assertIn("i18n/de/common.json", paths)
            self.assertIn("i18n/de/pages/classes/tamer/index.json", paths)
            self.assertNotIn("i18n/en/common.json", paths)
            self.assertTrue((out / SKILL_ICON).is_file())
            self.assertIn("ok:", printed)
            self.assertIn("total size on disk", printed)

    def test_manifest_has_kinds_and_the_spec_table(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            run_fetch(out, synthetic_site())
            document = read_document(out)
            files = {entry["path"]: entry for entry in document["files"]}
            self.assertEqual(fc.MANIFEST_SCHEMA, document["schema"])
            self.assertEqual("page", files["experts/tamer-awakening/index.html"]["kind"])
            self.assertEqual("icon", files[SKILL_ICON]["kind"])
            self.assertEqual("tooltip", files["data/skills/en/tamer.json"]["kind"])
            self.assertEqual("style", files["css/styles.css"]["kind"])
            self.assertEqual("script", files["js/skeleton.js"]["kind"])
            self.assertEqual("data", files["i18n/de/common.json"]["kind"])
            self.assertEqual("classes/tamer/awakening/index.html", files[SKILL_ICON]["linked_from"])
            self.assertEqual(files[SKILL_ICON]["sha256"],
                             hashlib.sha256((out / SKILL_ICON).read_bytes()).hexdigest())
            self.assertEqual(
                [
                    {"class": "dosa", "spec": "antido", "expert_path": "experts/dosa-antido/index.html", "local_path": None},
                    {"class": "maegu", "spec": "succession", "expert_path": "experts/maegu-succession/index.html", "local_path": None},
                    {"class": "scholar", "spec": "ascension", "expert_path": "experts/scholar-ascension/index.html",
                     "local_path": "classes/scholar/ascension/index.html"},
                    {"class": "tamer", "spec": "awakening", "expert_path": "experts/tamer-awakening/index.html",
                     "local_path": "classes/tamer/awakening/index.html"},
                    {"class": "tamer", "spec": "succession", "expert_path": "experts/tamer-succession/index.html",
                     "local_path": "classes/tamer/succession/index.html"},
                    {"class": "warrior", "spec": "awakening", "expert_path": "experts/warrior-awakening/index.html",
                     "local_path": None},
                    # wizard's page links only maegu's expert, so wizard has no spec of its own
                    {"class": "wizard", "spec": None, "expert_path": None, "local_path": None},
                ],
                document["specs"],
            )

    def test_class_without_spec_links_gets_an_empty_row(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            routes = synthetic_site(class_links={"warrior": []})
            code, _, _, _, _ = run_fetch(out, routes)
            self.assertEqual(0, code)
            warrior_rows = [row for row in read_document(out)["specs"] if row["class"] == "warrior"]
            self.assertEqual([{"class": "warrior", "spec": None, "expert_path": None, "local_path": None}], warrior_rows)

    def test_linked_404_is_a_broken_link_and_does_not_fail_the_run(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            code, _, _, logs, printed = run_fetch(out, synthetic_site(missing=frozenset({"experts/tamer-succession/index.html"})))
            self.assertEqual(0, code)
            manifest = read_manifest(out)
            self.assertEqual("not_found", manifest["experts/tamer-succession/index.html"]["status"])
            self.assertIsNone(manifest["experts/tamer-succession/index.html"]["sha256"])
            self.assertFalse((out / "experts/tamer-succession/index.html").exists())
            broken = [m for m in log_messages(logs) if "broken link" in m["msg"]]
            self.assertTrue(any(m.get("path") == "experts/tamer-succession/index.html"
                                and m.get("linked_from") == "classes/tamer/index.html" for m in broken))
            self.assertIn("not available (404): ", printed)

    def test_optional_files_that_answer_404_are_not_broken_links(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            _, _, _, logs, _ = run_fetch(out, synthetic_site())
            broken = [m["path"] for m in log_messages(logs) if "broken link" in m["msg"]]
            self.assertNotIn("data/skills/ko/tamer.json", broken)
            self.assertNotIn("i18n/ko/common.json", broken)

    def test_one_failed_page_does_not_stop_the_run(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            routes = synthetic_site()
            routes[url_of("experts/tamer-awakening/index.html")] = [status(500)]
            code, _, _, _, printed = run_fetch(out, routes)
            manifest = read_manifest(out)
            self.assertEqual(1, code)
            self.assertEqual("failed", manifest["experts/tamer-awakening/index.html"]["status"])
            self.assertEqual("ok", manifest["experts/tamer-succession/index.html"]["status"])
            self.assertIn("failed: 1", printed)

    def test_second_run_skips_files_already_on_disk(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            routes = synthetic_site(missing=frozenset({"experts/tamer-succession/index.html"}))
            _, first, _, _, _ = run_fetch(out, routes)
            not_found_first = {url for url in first.calls if not (out / url.removeprefix(fc.BASE_URL).split("?")[0]).is_file()}
            code, second, _, _, printed = run_fetch(out, routes)
            self.assertEqual(0, code)
            self.assertNotIn(WARRIOR_INDEX, second.calls)
            self.assertNotIn(url_of(SKILL_ICON), second.calls)
            self.assertIn("downloaded 0", printed)
            self.assertIn(url_of("experts/tamer-succession/index.html"), second.calls)
            self.assertTrue(set(second.calls) <= not_found_first, "only files that are not on disk are asked again")

    def test_force_downloads_again(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            routes = synthetic_site()
            run_fetch(out, routes)
            _, transport, _, _, _ = run_fetch(out, routes, force=True)
            self.assertIn(WARRIOR_INDEX, transport.calls)
            self.assertIn(url_of(SKILL_ICON), transport.calls)

    def test_unreadable_warrior_page_stops_the_run(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            routes = synthetic_site()
            routes[WARRIOR_INDEX] = [status(500)]
            code, transport, _, _, _ = run_fetch(out, routes)
            self.assertEqual(1, code)
            self.assertEqual([WARRIOR_INDEX] * 4, transport.calls)

    def test_warrior_page_without_class_links_stops_the_run(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            routes = synthetic_site()
            routes[WARRIOR_INDEX] = [ok("<html><body>no links here</body></html>")]
            code, transport, _, logs, _ = run_fetch(out, routes)
            self.assertEqual(1, code)
            self.assertTrue(all(url.startswith(WARRIOR_INDEX) for url in transport.calls))
            self.assertTrue(any("no class links" in m["msg"] for m in log_messages(logs)))

    def test_save_failure_is_logged_and_the_run_continues(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            (out / "experts/tamer-awakening/index.html").mkdir(parents=True)  # a folder where the page should go
            code, transport, _, logs, _ = run_fetch(out, synthetic_site())
            self.assertEqual(1, code)
            errors = [m for m in log_messages(logs) if m["msg"] == "could not save page"]
            self.assertEqual(1, len(errors))
            self.assertEqual("failed", read_manifest(out)["experts/tamer-awakening/index.html"]["status"])
            self.assertIn(url_of("classes/scholar/ascension/index.html"), transport.calls)

    def test_stale_manifest_entries_are_dropped_and_a_corrupt_manifest_is_replaced(self) -> None:
        with temp_root() as root:
            out = root / "classhub"
            out.mkdir()
            stale = {"schema": 1, "files": [{"path": "classes/agent/awakening/index.html", "status": "not_found"}]}
            (out / fc.MANIFEST_NAME).write_text(json.dumps(stale), encoding="utf-8")
            run_fetch(out, synthetic_site())
            self.assertNotIn("classes/agent/awakening/index.html", read_manifest(out))
            (out / fc.MANIFEST_NAME).write_text("{not json", encoding="utf-8")
            code, _, _, _, _ = run_fetch(out, synthetic_site())
            self.assertEqual(0, code)
            self.assertIn("files", read_document(out))

    def test_logs_contain_trace_ids(self) -> None:
        with temp_root() as root:
            _, _, _, logs, _ = run_fetch(root / "classhub", synthetic_site(class_links={"warrior": []}))
            messages = log_messages(logs)
            for record in messages:
                self.assertRegex(record["trace_id"], r"^[0-9a-f]{32}$")
            self.assertTrue(any(m["msg"] == "http get started" for m in messages))
            self.assertTrue(any(m["msg"] == "http get finished" for m in messages))


if __name__ == "__main__":
    unittest.main()
