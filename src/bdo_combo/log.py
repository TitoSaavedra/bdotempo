"""Structured JSON logging with W3C-style trace and span identifiers.

Every line is one JSON object written to stderr (and optionally appended to a
log file). Each CLI invocation gets one ``trace_id``; every operation runs
inside a span with its own ``span_id``. An incoming ``TRACEPARENT`` environment
variable (``00-<trace-id>-<parent-id>-<flags>``) continues an existing trace.

Never pass secrets, tokens or personal data as log context.
"""

from __future__ import annotations

import json
import os
import re
import secrets
import sys
import time
import traceback
from contextlib import contextmanager
from datetime import datetime, timezone
from typing import Any, Iterator, TextIO

LEVELS = {"debug": 10, "info": 20, "warn": 30, "error": 40}

_TRACEPARENT_RE = re.compile(r"^00-([0-9a-f]{32})-([0-9a-f]{16})-[0-9a-f]{2}$")
_ZERO_TRACE = "0" * 32


class _State:
    def __init__(self) -> None:
        self.trace_id: str = secrets.token_hex(16)
        self.parent_span_id: str | None = None
        self.min_level: int = LEVELS["info"]
        self.stream: TextIO | None = None
        self.file: TextIO | None = None
        self.spans: list[str] = []


_state = _State()


def new_span_id() -> str:
    return secrets.token_hex(8)


def trace_from_environment(value: str | None) -> tuple[str, str | None]:
    """Return (trace_id, parent_span_id) from a TRACEPARENT value, or a new trace."""
    if value:
        match = _TRACEPARENT_RE.match(value.strip().lower())
        if match and match.group(1) != _ZERO_TRACE:
            return match.group(1), match.group(2)
    return secrets.token_hex(16), None


def configure(
    *,
    log_file: str | None = None,
    verbose: bool = False,
    stream: TextIO | None = None,
    traceparent: str | None = None,
) -> str:
    """Set up logging for one CLI invocation and return its trace_id."""
    trace_id, parent = trace_from_environment(traceparent)
    _state.trace_id = trace_id
    _state.parent_span_id = parent
    _state.min_level = LEVELS["debug"] if verbose else LEVELS["info"]
    _state.stream = stream if stream is not None else sys.stderr
    _state.spans = []
    if _state.file is not None:
        _state.file.close()
        _state.file = None
    if log_file:
        _state.file = open(log_file, "a", encoding="utf-8", newline="\n")
    return trace_id


def close() -> None:
    if _state.file is not None:
        _state.file.close()
        _state.file = None


def trace_id() -> str:
    return _state.trace_id


def current_span_id() -> str | None:
    return _state.spans[-1] if _state.spans else None


def emit(level: str, msg: str, *, exc_info: bool = False, **context: Any) -> None:
    if LEVELS[level] < _state.min_level:
        return
    record: dict[str, Any] = {
        "ts": datetime.now(timezone.utc).isoformat(timespec="milliseconds"),
        "level": level,
        "msg": msg,
        "trace_id": _state.trace_id,
        "span_id": current_span_id() or new_span_id(),
    }
    if _state.parent_span_id:
        record["parent_span_id"] = _state.parent_span_id
    record.update(context)
    if exc_info:
        record["traceback"] = traceback.format_exc()
    line = json.dumps(record, ensure_ascii=False, default=str)
    stream = _state.stream if _state.stream is not None else sys.stderr
    stream.write(line + "\n")
    stream.flush()
    if _state.file is not None:
        _state.file.write(line + "\n")
        _state.file.flush()


def debug(msg: str, **context: Any) -> None:
    emit("debug", msg, **context)


def info(msg: str, **context: Any) -> None:
    emit("info", msg, **context)


def warn(msg: str, **context: Any) -> None:
    emit("warn", msg, **context)


def error(msg: str, *, exc_info: bool = True, **context: Any) -> None:
    emit("error", msg, exc_info=exc_info, **context)


@contextmanager
def span(name: str, **context: Any) -> Iterator[str]:
    """Run a block as a new span, logging start, end and duration_ms.

    Exceptions are logged with their traceback and re-raised.
    """
    span_id = new_span_id()
    _state.spans.append(span_id)
    started = time.perf_counter()
    emit("info", f"{name} started", operation=name, **context)
    try:
        yield span_id
    except BaseException as exc:
        duration = round((time.perf_counter() - started) * 1000, 1)
        emit(
            "error",
            f"{name} failed",
            exc_info=True,
            operation=name,
            duration_ms=duration,
            error_type=type(exc).__name__,
            error=str(exc),
            **context,
        )
        raise
    else:
        duration = round((time.perf_counter() - started) * 1000, 1)
        emit("info", f"{name} finished", operation=name, duration_ms=duration, **context)
    finally:
        _state.spans.pop()


def env_traceparent() -> str | None:
    return os.environ.get("TRACEPARENT")
