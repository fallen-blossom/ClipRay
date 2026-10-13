import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).parents[1] / "scripts" / "analyze_screenshot_data_trace.py"
spec = importlib.util.spec_from_file_location("screenshot_data_trace", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def bundle(root, missing=None):
    fingerprint = {"sha256": "a" * 64, "width": 2, "height": 2,
                   "byte_count": 16, "pixel_format": "BGRA32"}
    kinds = ["started", "run_context", "source_frame_fingerprint", "translation_inputs",
             "translation_outputs", "pipeline_outcome", "overlay_applied",
             "rendered_layer_fingerprint", "trace_closed"]
    events = [{"schema_version": module.SCHEMA, "sequence": index, "elapsed_ms": index,
               "kind": kind, "data": fingerprint if "fingerprint" in kind else {}}
              for index, kind in enumerate(k for k in kinds if k != missing)]
    for index, event in enumerate(events, 1):
        event["sequence"] = index
    event_bytes = "".join(json.dumps(e) + "\n" for e in events).encode()
    (root / "events.jsonl").write_bytes(event_bytes)
    manifest_bytes = json.dumps({"schema_version": module.SCHEMA, "evidence_mode": "data",
        "complete": True, "write_failures": 0, "event_count": len(events),
        "translation_required": True, "rendered_layer_required": True,
        "source_sha256": "a" * 64, "rendered_layer_sha256": "a" * 64,
        "events_sha256": hashlib.sha256(event_bytes).hexdigest()}).encode()
    (root / "manifest.json").write_bytes(manifest_bytes)
    (root / "manifest.sha256").write_text(hashlib.sha256(manifest_bytes).hexdigest())


class ScreenshotDataTraceTests(unittest.TestCase):
    def test_data_chain_has_no_image_dependency(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            bundle(root)
            self.assertEqual([], module.analyze(root)["issues"])

    def test_missing_source_translation_outcome_and_render_do_not_pass(self):
        for missing in ("source_frame_fingerprint", "translation_outputs", "pipeline_outcome",
                        "rendered_layer_fingerprint", "overlay_applied"):
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                bundle(root, missing)
                self.assertTrue(module.analyze(root)["issues"])

    def test_checksum_and_unexpected_image_do_not_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            bundle(root)
            (root / "manifest.sha256").write_text("changed")
            (root / "source.png").write_bytes(b"unexpected")
            self.assertIn("manifest-checksum-mismatch", module.analyze(root)["issues"])
            self.assertIn("unexpected-image-artifact", module.analyze(root)["issues"])


if __name__ == "__main__":
    unittest.main()
