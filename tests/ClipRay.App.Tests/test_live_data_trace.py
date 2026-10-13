"""Data-only evidence must retain source/apply/render identity and fail on gaps."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from live_trace_analyzer import analyze, validate_bundle
from test_live_trace_analyzer import event, session_context


def bundle(root, mutate=None):
    fingerprint = {"sha256": "a" * 64, "width": 2, "height": 2,
                   "pixel_format": "BGRA32", "byte_count": 16}
    events = [event("started", 0), event("session_context", 1, data=session_context()),
              event("presentation", 2), event("source_frame_fingerprint", 3, capture=1, data=dict(fingerprint)),
              event("translation_requests", 4, capture=1), event("translation_results", 5, capture=1),
              event("region_decisions", 6, capture=1),
              event("applied", 7, capture=1, generation=1, data={"State": "Ready", "Layouts": 1}),
              event("rendered_frame_fingerprint", 8, capture=1, generation=1, data=dict(fingerprint)),
              event("render_cycle_observed", 9, capture=1, generation=1, data={"Visible": True, "Layouts": 1}),
              event("ended", 10), event("trace_closed", 11)]
    if mutate:
        mutate(events)
    for index, item in enumerate(events, 1):
        item.update(sequence=index, schema_version="clipray.live-evidence.v2-data")
    path = root / "events.jsonl"
    path.write_text("".join(json.dumps(e) + "\n" for e in events), encoding="utf-8")
    (root / "manifest.json").write_text(json.dumps({
        "schema_version": "clipray.live-evidence.v2-data", "evidence_mode": "data", "complete": True,
        "write_failures": 0, "source_frame_count": 1, "event_count": len(events),
        "events_sha256": hashlib.sha256(path.read_bytes()).hexdigest()}), encoding="utf-8")
    return path, events


class LiveDataTraceTests(unittest.TestCase):
    def test_complete_data_trace_requires_no_png(self):
        with tempfile.TemporaryDirectory() as directory:
            path, events = bundle(Path(directory))
            self.assertEqual([], validate_bundle(path, events))
            self.assertEqual([], analyze(events)["issues"])
            self.assertEqual(6, analyze(events)["source_to_render_ms"]["max"])
            self.assertFalse(list(Path(directory).glob("*.png")))

    def test_missing_source_or_render_or_ack_does_not_pass(self):
        for kind in ("source_frame_fingerprint", "rendered_frame_fingerprint", "render_cycle_observed"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                path, events = bundle(Path(directory), lambda events: events.__setitem__(
                    slice(None), [e for e in events if e["kind"] != kind]))
                self.assertTrue(validate_bundle(path, events) + analyze(events)["issues"])

    def test_invalid_fingerprint_and_wrong_frame_rejected(self):
        for field, value in (("sha256", "not-a-hash"), ("byte_count", 15), ("width", True)):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as directory:
                path, events = bundle(Path(directory), lambda events: events[3]["data"].update({field: value}))
                self.assertTrue(validate_bundle(path, events))
        with tempfile.TemporaryDirectory() as directory:
            path, events = bundle(Path(directory), lambda events: events[8].update(capture_id=2))
            self.assertTrue(validate_bundle(path, events))

    def test_data_mode_cannot_downgrade_image_schema(self):
        with tempfile.TemporaryDirectory() as directory:
            path, events = bundle(Path(directory))
            for item in events:
                item["schema_version"] = "clipray.live-evidence.v1"
            self.assertTrue(validate_bundle(path, events))


if __name__ == "__main__":
    unittest.main()
