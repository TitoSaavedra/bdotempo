"""Download the bdoclasshub.com guide files that the site shows, into ``data/classhub/``.

For personal, non-commercial use. The fetch is polite by design: requests run
one at a time with a fixed delay, the User-Agent identifies the tool, files
already on disk are not downloaded again (unless ``--force``), and 429 and 5xx
responses are retried with backoff. No trace context is sent to the site; the
trace ids stay in this tool's own logs.

What is fetched, starting from the warrior class page (``classes/warrior/index.html``),
whose links give the class list:

* every class page, and the pages it links to: expert pages
  (``experts/<class>-<spec>/index.html``) and the class's own spec pages
  (``classes/<class>/<spec>/index.html``). Query strings and fragments are dropped;
* the pages listed by the site's own data, ``js/experts-index.json`` and
  ``data/survey-classes.json`` (spec pages are often not linked from class pages);
* the images, scripts and stylesheets those pages reference (``img``, ``srcset``,
  ``data-src``, ``link``, ``script`` and CSS ``url()``/``@import``), and the
  references inside stylesheets and JSON data;
* the skill tooltip data (``data/skills/labels.json`` and ``data/skills/<lang>/<class>.json``
  for every language in ``js/skill-tooltips.js``), and the translation files
  (``i18n/<lang>/common.json`` and ``i18n/<lang>/pages/<page>.json``) that
  ``js/i18n.js`` loads for each page.

Live services that the scripts call at runtime (Twitch, Google Apps Script,
YouTube, Google Analytics and the Flask ``/api/`` route) are not archived.

A link that answers 404 is a broken link on the site and is logged as one. A
file the scripts may request but the site does not have (for example a
translation) answers 404 without a warning. Neither fails the run.

Layout under the output folder (relative paths mirror the site)::

    classes/<class>/index.html
    classes/<class>/<spec>/index.html
    experts/<class>-<spec>/index.html
    css/, js/, assets/, data/, i18n/ (the files the pages load)
    manifest.json   every file of this run (kind, status, size, sha256, linked_from)
                    and the spec table (class, spec, expert_path, local_path)

Exit codes: 0 when every file was saved or is not available (404); 1 when a
file failed (any status other than 404), or the class list could not be read.
"""

from __future__ import annotations

import argparse
import dataclasses
import gzip
import hashlib
import http.client
import json
import os
import posixpath
import re
import sys
import time
from collections import deque
from dataclasses import dataclass
from datetime import datetime, timezone
from html.parser import HTMLParser
from pathlib import Path
from typing import Callable, Iterator, Sequence
from urllib import error as urlerror
from urllib import request as urlrequest
from urllib.parse import urljoin, urlsplit

from bdo_combo import __version__, log

BASE_URL = "https://bdoclasshub.com/"
SITE_HOST = urlsplit(BASE_URL).netloc
DISCOVERY_CLASS = "warrior"
REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUT_DIR = REPO_ROOT / "data" / "classhub"
MANIFEST_NAME = "manifest.json"
MANIFEST_SCHEMA = 2
MANIFEST_SAVE_EVERY = 100  # files between manifest writes (the run also saves at the end)

# Site data that the pages load. The query strings are the versions the site's scripts use.
EXPERTS_INDEX_PATH = "js/experts-index.json"
EXPERTS_INDEX_QUERY = "v=fh-bio-1"
SURVEY_PATH = "data/survey-classes.json"
SURVEY_QUERY = "v=survey-data-9"
LABELS_PATH = "data/skills/labels.json"
TOOLTIP_QUERY = "v=st-11"
# Languages of LANGS in js/skill-tooltips.js and js/i18n.js. English needs no translation file.
TOOLTIP_LANGS = ("en", "de", "fr", "es", "es-mx", "pt-br", "tr", "ru", "ko", "ja", "zh-tw", "th")
I18N_LANGS = tuple(lang for lang in TOOLTIP_LANGS if lang != "en")
I18N_QUERY = "v=i18n-58"

USER_AGENT = f"bdo-combo-setup/{__version__} (personal, non-commercial use; fetch-classhub)"
REQUEST_DELAY_SECONDS = 1.0
TIMEOUT_SECONDS = 30.0
MAX_ATTEMPTS = 4
BACKOFF_SECONDS = 2.0  # waits 2, 4 and 8 seconds between the attempts
MAX_RETRY_AFTER_SECONDS = 60.0

KINDS = ("page", "tooltip", "icon", "image", "script", "style", "data")
IMAGE_EXT = frozenset({".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg", ".ico"})
STYLE_EXT = frozenset({".css", ".woff2", ".woff", ".ttf"})
DATA_EXT = frozenset({".json", ".webmanifest"})
ASSET_EXT = IMAGE_EXT | STYLE_EXT | DATA_EXT | {".js"}
TEXT_EXT = frozenset({".html", ".css", ".json", ".webmanifest"})

CLASS_LINK_RE = re.compile(r"^\.\./([a-z][a-z0-9-]*)/index\.html$")
CLASS_PAGE_RE = re.compile(r"^classes/([a-z0-9][a-z0-9-]*)/index\.html$")
# "<class>-<spec>" with the spec as the last hyphen part; a slug without a spec is kept whole.
EXPERT_PAGE_RE = re.compile(r"^experts/([a-z0-9][a-z0-9-]*?)(?:-([a-z0-9]+))?/index\.html$")
SPEC_PAGE_RE = re.compile(r"^classes/([a-z0-9][a-z0-9-]*)/([a-z0-9]+)/index\.html$")
PLAIN_REF_RE = re.compile(r"[A-Za-z0-9._~/-]+(?:\?[A-Za-z0-9._=&~-]*)?")
CSS_URL_RE = re.compile(r"""url\(\s*(['"]?)([^'")\s]+)\1\s*\)""")
CSS_IMPORT_RE = re.compile(r"""@import\s+(?:url\(\s*)?(['"])([^'"]+)\1""")
REF_ATTRS = {
    "img": ("src", "srcset", "data-src", "data-srcset"),
    "source": ("src", "srcset", "data-src", "data-srcset"),
    "script": ("src",),
    "link": ("href",),
    "video": ("poster",),
    "audio": ("src",),
}


# --- transport --------------------------------------------------------------


@dataclass(frozen=True)
class Response:
    status: int
    body: bytes
    retry_after: str | None = None


class NetworkError(Exception):
    """A connection-level failure (DNS, reset, timeout). Retried like a 5xx."""


Transport = Callable[[str, dict[str, str]], Response]


def urllib_transport(url: str, headers: dict[str, str]) -> Response:
    request = urlrequest.Request(url, headers=headers, method="GET")
    try:
        with urlrequest.urlopen(request, timeout=TIMEOUT_SECONDS) as response:
            body = response.read()
            if response.headers.get("Content-Encoding", "").lower() == "gzip":
                body = gzip.decompress(body)
            return Response(response.status, body, response.headers.get("Retry-After"))
    except urlerror.HTTPError as exc:
        retry_after = exc.headers.get("Retry-After") if exc.headers else None
        exc.close()
        return Response(exc.code, b"", retry_after)
    except (OSError, http.client.HTTPException) as exc:
        raise NetworkError(f"{type(exc).__name__}: {exc}") from exc


def parse_retry_after(value: str | None) -> float | None:
    """Seconds from a Retry-After header (delta-seconds form only), capped."""
    if value is None:
        return None
    try:
        seconds = float(value.strip())
    except ValueError:
        return None
    if not 0 <= seconds:  # also rejects NaN
        return None
    return min(seconds, MAX_RETRY_AFTER_SECONDS)


@dataclass(frozen=True)
class Fetched:
    status: str  # "ok", "not_found" or "failed"
    http_status: int | None
    body: bytes | None = None
    error: str | None = None


class Client:
    """Polite GET: one request at a time, a fixed delay between requests, retries with backoff."""

    def __init__(
        self,
        *,
        transport: Transport = urllib_transport,
        sleep: Callable[[float], None] = time.sleep,
        delay: float = REQUEST_DELAY_SECONDS,
        backoff: float = BACKOFF_SECONDS,
        max_attempts: int = MAX_ATTEMPTS,
    ) -> None:
        self._transport = transport
        self._sleep = sleep
        self._delay = delay
        self._backoff = backoff
        self._max_attempts = max_attempts
        self._requests_made = 0

    def get(self, url: str) -> Fetched:
        if self._requests_made:
            self._sleep(self._delay)
        with log.span("http get", url=url):
            # Only the User-Agent goes to the site. The trace stays in this tool's own logs.
            headers = {
                "User-Agent": USER_AGENT,
                "Accept": "text/html,application/xhtml+xml,text/css,application/javascript,image/*,application/json;q=0.9,*/*;q=0.8",
            }
            error: str | None = None
            last_status: int | None = None
            retry_after: float | None = None
            for attempt in range(1, self._max_attempts + 1):
                if attempt > 1:
                    wait = retry_after if retry_after is not None else self._backoff * 2 ** (attempt - 2)
                    log.warn("retrying after a wait", url=url, attempt=attempt, wait_seconds=wait, reason=error)
                    self._sleep(wait)
                    retry_after = None
                self._requests_made += 1
                try:
                    response = self._transport(url, headers)
                except NetworkError as exc:
                    error = str(exc)
                    log.warn("network error", url=url, attempt=attempt, error=error)
                    continue
                last_status = response.status
                if 200 <= response.status < 300:
                    log.info("response received", url=url, http_status=response.status,
                             attempts=attempt, bytes=len(response.body))
                    return Fetched("ok", response.status, body=response.body)
                if response.status == 404:
                    log.info("page not available (404)", url=url, attempts=attempt)
                    return Fetched("not_found", 404)
                if response.status == 429 or response.status >= 500:
                    error = f"HTTP {response.status}"
                    retry_after = parse_retry_after(response.retry_after)
                    log.warn("retryable HTTP status", url=url, attempt=attempt, http_status=response.status)
                    continue
                log.error("HTTP status not retried", url=url, http_status=response.status, exc_info=False)
                return Fetched("failed", response.status, error=f"HTTP {response.status}")
            log.error("giving up after retries", url=url, attempts=self._max_attempts,
                      http_status=last_status, error=error, exc_info=False)
            return Fetched("failed", last_status, error=error)


# --- parsing ----------------------------------------------------------------


class _LinkCollector(HTMLParser):
    """The ``<a href>`` links of a page: the links a visitor follows."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.hrefs: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag != "a":
            return
        for name, value in attrs:
            if name == "href" and value:
                self.hrefs.append(value.strip())


class _RefCollector(HTMLParser):
    """The files a page loads: images, sources, scripts, stylesheets, icons and posters."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.refs: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        names = REF_ATTRS.get(tag)
        if not names:
            return
        for name, value in attrs:
            if name not in names or not value:
                continue
            if name.endswith("srcset"):
                self.refs.extend(part.split()[0] for part in value.split(",") if part.strip())
            else:
                self.refs.append(value.strip())


def parse_class_names(html_text: str) -> list[str]:
    """Class names from the ``../<class>/index.html`` links of a class page, sorted and unique."""
    parser = _LinkCollector()
    parser.feed(html_text)
    parser.close()
    names: set[str] = set()
    for href in parser.hrefs:
        match = CLASS_LINK_RE.match(href)
        if match:
            names.add(match.group(1))
    return sorted(names)


def css_refs(text: str) -> list[str]:
    """``url()`` and ``@import`` references of a stylesheet (or of the inline styles of a page)."""
    return [m.group(2) for m in CSS_URL_RE.finditer(text)] + [m.group(2) for m in CSS_IMPORT_RE.finditer(text)]


def html_refs(text: str) -> list[str]:
    """Files an HTML page loads: element attributes plus CSS ``url()`` in styles."""
    collector = _RefCollector()
    collector.feed(text)
    collector.close()
    return collector.refs + [m.group(2) for m in CSS_URL_RE.finditer(text)]


def resolve_site_ref(ref: str, base_path: str) -> tuple[str, str] | None:
    """(site path, query) that ``ref`` points to on the page or file ``base_path``.

    None when the reference leaves the site (other host, data:, mailto:, javascript:),
    is a bare fragment, or names a folder.
    """
    ref = ref.strip()
    if not ref or ref.startswith("#"):
        return None
    parts = urlsplit(urljoin(BASE_URL + base_path, ref))
    if parts.scheme not in ("http", "https") or parts.netloc != SITE_HOST:
        return None
    path = parts.path.lstrip("/")
    if not path or path.endswith("/"):
        return None
    return path, parts.query


def kind_for(path: str) -> str:
    """The manifest kind of a site file: page, tooltip, icon, image, script, style or data."""
    ext = posixpath.splitext(path.lower())[1]
    if ext == ".html":
        return "page"
    if path.startswith("data/skills/"):
        return "tooltip"
    if ext in IMAGE_EXT:
        return "icon" if "icons/" in path else "image"
    if ext == ".js":
        return "script"
    if ext in STYLE_EXT:
        return "style"
    return "data"


def is_asset_path(path: str) -> bool:
    return posixpath.splitext(path.lower())[1] in ASSET_EXT


@dataclass(frozen=True)
class SpecLink:
    """A page that belongs to a class spec: an expert page, or the class's own spec page."""

    class_name: str
    spec: str | None
    path: str  # site path, e.g. "experts/tamer-awakening/index.html"
    kind: str  # "expert" or "local"


def classify_page(path: str, own_class: str | None = None) -> SpecLink | None:
    """SpecLink for an expert page, or for a spec page (only of ``own_class`` when given)."""
    expert = EXPERT_PAGE_RE.match(path)
    if expert:
        return SpecLink(expert.group(1), expert.group(2), path, "expert")
    local = SPEC_PAGE_RE.match(path)
    if local and own_class in (None, local.group(1)):
        return SpecLink(local.group(1), local.group(2), path, "local")
    return None


def discover_class_pages(class_name: str, html_text: str) -> list[SpecLink]:
    """Spec links of one class page: the expert pages it links to, and its own spec pages.

    Other links (class pages, the spec pages of other classes, external sites) are not followed.
    """
    parser = _LinkCollector()
    parser.feed(html_text)
    parser.close()
    base = f"classes/{class_name}/index.html"
    found: dict[str, SpecLink] = {}
    for href in parser.hrefs:
        resolved = resolve_site_ref(href, base)
        if resolved is None:
            continue
        link = classify_page(resolved[0], own_class=class_name)
        if link is not None:
            found.setdefault(link.path, link)
    return [found[path] for path in sorted(found)]


def iter_strings(value: object) -> Iterator[str]:
    if isinstance(value, dict):
        for item in value.values():
            yield from iter_strings(item)
    elif isinstance(value, list):
        for item in value:
            yield from iter_strings(item)
    elif isinstance(value, str):
        yield value


# --- files and manifest -----------------------------------------------------


@dataclass(frozen=True)
class Item:
    """One file to fetch: its site path, query string (the site's version), and who linked to it."""

    path: str
    query: str = ""
    linked_from: str | None = None

    @property
    def kind(self) -> str:
        return kind_for(self.path)


@dataclass(frozen=True)
class Outcome:
    path: str
    url: str
    kind: str
    linked_from: str | None
    status: str  # "ok", "not_found" or "failed"
    origin: str | None  # "downloaded", "on_disk" or None
    body: bytes | None
    http_status: int | None
    error: str | None
    fetched_at: str
    sha256: str | None = None
    size: int | None = None


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def file_time_iso(path: Path) -> str:
    return datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat(timespec="seconds")


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write_bytes_atomic(path: Path, data: bytes) -> None:
    """Write through a temporary file next to the target, then replace the target."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    try:
        tmp.write_bytes(data)
        os.replace(tmp, path)
    except BaseException:
        tmp.unlink(missing_ok=True)
        raise


def summarize(outcome: Outcome) -> Outcome:
    """The outcome without its body, with the size and SHA-256 of the saved file."""
    body = outcome.body
    if outcome.status == "ok" and body is not None:
        return dataclasses.replace(outcome, body=None, sha256=sha256_hex(body), size=len(body))
    return dataclasses.replace(outcome, body=None)


# --- the fetch --------------------------------------------------------------


class ClassHubFetch:
    def __init__(self, out_dir: Path, client: Client, *, force: bool = False) -> None:
        self.out_dir = out_dir
        self.client = client
        self.force = force
        self.manifest_path = out_dir / MANIFEST_NAME
        self.results: dict[str, Outcome] = {}
        self.specs: dict[tuple[str, str | None], dict] = {}
        self.checked_classes: set[str] = set()
        self._seen: set[str] = set()
        self._queue: deque[Item] = deque()
        self._processed = 0

    def run(self) -> int:
        with log.span("fetch-classhub", out_dir=str(self.out_dir), force=self.force):
            classes: list[str] | None = None
            try:
                classes = self._start()
                if classes is not None:
                    self._drain()
            finally:
                self._save_manifest()
            totals = self._totals()
            log.info("fetch-classhub summary", classes=len(classes or []),
                     **{k: v for k, v in totals.items() if k != "by_kind"}, by_kind=totals["by_kind"])
            self._print_summary(len(classes or []), totals)
        return 1 if totals["failed"] or classes is None else 0

    def _start(self) -> list[str] | None:
        """Read the warrior class page (the class list); queue everything else. None stops the run."""
        warrior = Item(f"classes/{DISCOVERY_CLASS}/index.html")
        self._seen.add(warrior.path)
        text = self._process(warrior)
        if text is None:
            log.error("class list unavailable: the warrior page could not be read", exc_info=False)
            return None
        names = parse_class_names(text)
        if not names:
            log.error("no class links found on the warrior page", exc_info=False)
            return None
        log.info("classes discovered", count=len(names), classes=names)
        self._enqueue(Item(EXPERTS_INDEX_PATH, EXPERTS_INDEX_QUERY))
        self._enqueue(Item(SURVEY_PATH, SURVEY_QUERY))
        for name in names:
            self._enqueue(Item(f"classes/{name}/index.html", linked_from=warrior.path))
        return names

    def _enqueue(self, item: Item) -> None:
        if item.path in self._seen:
            return
        self._seen.add(item.path)
        self._queue.append(item)

    def _drain(self) -> None:
        while self._queue:
            self._process(self._queue.popleft())

    def _process(self, item: Item) -> str | None:
        """Fetch and record one file, then queue what it links to. Returns the text of a saved text file."""
        outcome = self._fetch(item)
        text = None
        if outcome.status == "ok" and outcome.body is not None and posixpath.splitext(item.path.lower())[1] in TEXT_EXT:
            text = outcome.body.decode("utf-8", errors="replace")
        if outcome.status == "not_found" and item.linked_from:
            log.warn("broken link: a linked page or file answered 404", path=item.path, linked_from=item.linked_from)
        self.results[item.path] = summarize(outcome)
        self._processed += 1
        if self._processed % MANIFEST_SAVE_EVERY == 0:
            self._save_manifest()
        if text is not None:
            self._expand(item, text)
        return text

    def _fetch(self, item: Item) -> Outcome:
        url = BASE_URL + item.path + (f"?{item.query}" if item.query else "")
        target = self.out_dir / item.path
        try:
            if target.is_file() and not self.force:
                body = target.read_bytes()
                log.info("already on disk, not downloaded", path=item.path)
                return Outcome(path=item.path, url=url, kind=item.kind, linked_from=item.linked_from, status="ok",
                               origin="on_disk", body=body, http_status=None, error=None,
                               fetched_at=file_time_iso(target))
            fetched = self.client.get(url)
            if fetched.status != "ok" or fetched.body is None:
                return Outcome(path=item.path, url=url, kind=item.kind, linked_from=item.linked_from,
                               status=fetched.status, origin=None, body=None, http_status=fetched.http_status,
                               error=fetched.error, fetched_at=now_iso())
            write_bytes_atomic(target, fetched.body)
            log.info("saved", path=item.path, bytes=len(fetched.body))
            return Outcome(path=item.path, url=url, kind=item.kind, linked_from=item.linked_from, status="ok",
                           origin="downloaded", body=fetched.body, http_status=fetched.http_status, error=None,
                           fetched_at=now_iso())
        except Exception as exc:  # noqa: BLE001 - logged with traceback; the run goes on with the next file
            log.error("could not save page", path=item.path, error_type=type(exc).__name__, error=str(exc))
            return Outcome(path=item.path, url=url, kind=item.kind, linked_from=item.linked_from, status="failed",
                           origin=None, body=None, http_status=None,
                           error=f"{type(exc).__name__}: {exc}", fetched_at=now_iso())

    def _expand(self, item: Item, text: str) -> None:
        """Queue the files that a saved text file loads or links to."""
        path = item.path
        if path.endswith(".html"):
            class_match = CLASS_PAGE_RE.match(path)
            if class_match:
                self._expand_class(class_match.group(1), path, text)
            self._enqueue_translations(path)
            refs = html_refs(text)
        elif path.endswith(".css"):
            refs = css_refs(text)
        else:
            refs = []
        for ref in refs:
            self._enqueue_ref(ref, path)
        if posixpath.splitext(path.lower())[1] in DATA_EXT:
            self._expand_json(path, text)

    def _expand_class(self, name: str, page_path: str, text: str) -> None:
        self.checked_classes.add(name)
        for link in discover_class_pages(name, text):
            self._add_spec(link)
            self._enqueue(Item(link.path, linked_from=page_path))
        self._enqueue(Item(LABELS_PATH, TOOLTIP_QUERY))
        for lang in TOOLTIP_LANGS:
            self._enqueue(Item(f"data/skills/{lang}/{name}.json", TOOLTIP_QUERY))

    def _enqueue_translations(self, page_path: str) -> None:
        page_key = page_path[: -len(".html")]  # the key js/i18n.js derives from the page path
        for lang in I18N_LANGS:
            self._enqueue(Item(f"i18n/{lang}/common.json", I18N_QUERY))
            self._enqueue(Item(f"i18n/{lang}/pages/{page_key}.json", I18N_QUERY))

    def _enqueue_ref(self, ref: str, base_path: str) -> None:
        resolved = resolve_site_ref(ref, base_path)
        if resolved is None:
            return
        path, query = resolved
        if is_asset_path(path):
            self._enqueue(Item(path, query, linked_from=base_path))

    def _expand_json(self, path: str, text: str) -> None:
        """Pages and files named in a JSON data file (site index, survey, skill data, manifests)."""
        try:
            data = json.loads(text)
        except ValueError as exc:
            log.warn("JSON data could not be read for links", path=path, error=str(exc))
            return
        for value in iter_strings(data):
            if not PLAIN_REF_RE.fullmatch(value):
                continue
            resolved = resolve_site_ref(value, "index.html")
            if resolved is None:
                continue
            ref_path, query = resolved
            link = classify_page(ref_path)
            if link is not None:
                self._add_spec(link)
                self._enqueue(Item(ref_path, linked_from=path))
            elif CLASS_PAGE_RE.match(ref_path):
                self._enqueue(Item(ref_path, linked_from=path))
            elif is_asset_path(ref_path):
                self._enqueue(Item(ref_path, query, linked_from=path))
        if path == EXPERTS_INDEX_PATH and isinstance(data, list):
            for entry in data:
                cls = entry.get("class") if isinstance(entry, dict) else None
                if isinstance(cls, str) and PLAIN_REF_RE.fullmatch(cls):
                    # js/live-now.js shows this image for each live expert of the class
                    self._enqueue(Item(f"assets/class-icons/{cls}.png", linked_from=path))

    def _add_spec(self, link: SpecLink) -> None:
        row = self.specs.setdefault(
            (link.class_name, link.spec),
            {"class": link.class_name, "spec": link.spec, "expert_path": None, "local_path": None},
        )
        row["expert_path" if link.kind == "expert" else "local_path"] = link.path

    def _spec_rows(self) -> list[dict]:
        rows = list(self.specs.values())
        listed = {row["class"] for row in rows}
        for name in sorted(self.checked_classes - listed):
            rows.append({"class": name, "spec": None, "expert_path": None, "local_path": None})
        return sorted(rows, key=lambda row: (row["class"], row["spec"] or ""))

    def _entry(self, outcome: Outcome) -> dict:
        return {
            "url": outcome.url,
            "path": outcome.path,
            "kind": outcome.kind,
            "linked_from": outcome.linked_from,
            "status": outcome.status,
            "http_status": outcome.http_status,
            "bytes": outcome.size,
            "sha256": outcome.sha256,
            "fetched_at": outcome.fetched_at,
            "error": outcome.error,
        }

    def _save_manifest(self) -> None:
        document = {
            "schema": MANIFEST_SCHEMA,
            "tool": "bdo-combo fetch-classhub",
            "tool_version": __version__,
            "base_url": BASE_URL,
            "updated_at": now_iso(),
            "specs": self._spec_rows(),
            "files": [self._entry(self.results[key]) for key in sorted(self.results)],
        }
        text = json.dumps(document, indent=2, ensure_ascii=False) + "\n"
        write_bytes_atomic(self.manifest_path, text.encode("utf-8"))

    def _totals(self) -> dict:
        values = list(self.results.values())
        by_kind = {kind: {"ok": 0, "not_found": 0, "failed": 0, "bytes": 0} for kind in KINDS}
        for outcome in values:
            bucket = by_kind[outcome.kind]
            bucket[outcome.status] += 1
            bucket["bytes"] += outcome.size or 0
        return {
            "pages": len(values),
            "ok": sum(1 for o in values if o.status == "ok"),
            "downloaded": sum(1 for o in values if o.origin == "downloaded"),
            "on_disk": sum(1 for o in values if o.origin == "on_disk"),
            "not_found": sum(1 for o in values if o.status == "not_found"),
            "failed": sum(1 for o in values if o.status == "failed"),
            "bytes": sum(bucket["bytes"] for bucket in by_kind.values()),
            "by_kind": by_kind,
        }

    def _print_summary(self, class_count: int, totals: dict) -> None:
        print(f"classes discovered: {class_count}")
        print(f"files checked this run: {totals['pages']}")
        print(f"  ok: {totals['ok']} (downloaded {totals['downloaded']}, already on disk {totals['on_disk']})")
        print(f"  not available (404): {totals['not_found']}")
        print(f"  failed: {totals['failed']}")
        print("  by kind (ok / not available / failed / size on disk):")
        for kind in KINDS:
            bucket = totals["by_kind"][kind]
            print(f"    {kind:<8} {bucket['ok']:>6} / {bucket['not_found']:>5} / {bucket['failed']:>3}"
                  f"   {bucket['bytes'] / 1_048_576:>9.1f} MB")
        print(f"  total size on disk: {totals['bytes'] / 1_048_576:.1f} MB")
        for outcome in self.results.values():
            if outcome.status == "failed":
                print(f"    {outcome.path}: {outcome.error}", file=sys.stderr)
        print(f"manifest: {self.manifest_path}")


# --- command line -----------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="fetch_classhub.py",
        description="Download the bdoclasshub.com guide files into data/classhub/ (personal use).",
    )
    parser.add_argument("--force", action="store_true", help="download every file again, even if it is on disk")
    parser.add_argument("--out-dir", type=Path, default=DEFAULT_OUT_DIR, help="output folder (default: data/classhub)")
    parser.add_argument("--log-file", help="also append JSON logs to this file")
    parser.add_argument("--verbose", action="store_true", help="debug logs")
    return parser


def main(argv: Sequence[str] | None = None, *, client: Client | None = None) -> int:
    args = build_parser().parse_args(argv)
    log.configure(log_file=args.log_file, verbose=args.verbose, traceparent=log.env_traceparent())
    try:
        return ClassHubFetch(args.out_dir, client or Client(), force=args.force).run()
    except KeyboardInterrupt:
        log.warn("interrupted; files already saved are recorded in the manifest")
        print("interrupted", file=sys.stderr)
        return 130
    except Exception as exc:  # noqa: BLE001 - the span already logged the traceback
        print(f"unexpected error ({type(exc).__name__}): {exc}", file=sys.stderr)
        return 1
    finally:
        log.close()


if __name__ == "__main__":
    raise SystemExit(main())
