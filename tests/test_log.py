import io
import unittest

from bdo_combo import log
from support import log_messages, quiet_logs


class TraceTest(unittest.TestCase):
    def test_traceparent_is_continued(self) -> None:
        trace, parent = log.trace_from_environment("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")
        self.assertEqual("4bf92f3577b34da6a3ce929d0e0e4736", trace)
        self.assertEqual("00f067aa0ba902b7", parent)

    def test_invalid_traceparent_starts_new_trace(self) -> None:
        for value in [None, "", "garbage", "00-" + "0" * 32 + "-00f067aa0ba902b7-01"]:
            trace, parent = log.trace_from_environment(value)
            self.assertRegex(trace, r"^[0-9a-f]{32}$")
            self.assertIsNone(parent)

    def test_records_are_json_with_ids_and_duration(self) -> None:
        with quiet_logs() as buffer:
            with log.span("sample", pcid="1"):
                log.info("inside")
            messages = log_messages(buffer)
        self.assertEqual(["sample started", "inside", "sample finished"], [m["msg"] for m in messages])
        for record in messages:
            self.assertRegex(record["trace_id"], r"^[0-9a-f]{32}$")
            self.assertRegex(record["span_id"], r"^[0-9a-f]{16}$")
            self.assertRegex(record["ts"], r"^\d{4}-\d{2}-\d{2}T")
        self.assertIn("duration_ms", messages[-1])

    def test_failed_span_logs_traceback_and_reraises(self) -> None:
        with quiet_logs() as buffer:
            with self.assertRaises(ValueError):
                with log.span("boom"):
                    raise ValueError("bad input")
            messages = log_messages(buffer)
        failed = messages[-1]
        self.assertEqual("error", failed["level"])
        self.assertIn("Traceback", failed["traceback"])
        self.assertEqual("ValueError", failed["error_type"])

    def test_debug_hidden_unless_verbose(self) -> None:
        buffer = io.StringIO()
        log.configure(stream=buffer, verbose=False)
        log.debug("hidden")
        self.assertEqual("", buffer.getvalue())
        log.configure(stream=buffer, verbose=True)
        log.debug("shown")
        self.assertIn("shown", buffer.getvalue())
        log.configure(stream=io.StringIO())


if __name__ == "__main__":
    unittest.main()
