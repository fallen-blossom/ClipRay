"""Deliberately bad traces must not look like complete live evidence."""

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from live_trace_analyzer import analyze, analyze_layout_style, analyze_ocr_drift, validate_bundle


def session_context():
    return {"AppVersion": "1.0.0", "ExecutableSha256": "a" * 64,
            "Region": {"X": 100, "Y": 100, "Width": 300, "Height": 200},
            "Monitor": {"X": 0, "Y": 0, "Width": 1920, "Height": 1080},
            "Route": "DeepSeekFlash", "TargetLanguage": "简体中文",
            "ShowInRecordings": False}


def event(kind, time, *, capture=None, generation=None, data=None):
    return {"kind": kind, "elapsed_ms": time, "generation": generation,
            "capture_id": capture, "pipeline_id": capture,
            "viewport_generation": capture, "data": data}


def write_bundle(root, events):
    for number, item in enumerate(events, 1):
        item["schema_version"] = "clipray.live-evidence.v1"
        item["sequence"] = number
    path = root / "events.jsonl"
    path.write_text("".join(json.dumps(item) + "\n" for item in events), encoding="utf-8")
    (root / "manifest.json").write_text(json.dumps({
        "schema_version": "clipray.live-evidence.v1", "complete": True,
        "write_failures": 0, "event_count": len(events),
        "events_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    }), encoding="utf-8")
    return path


class LiveTraceAnalyzerTests(unittest.TestCase):
    def test_layout_style_flags_peer_scale_jitter_and_color_without_leaking_text(self):
        def region(name, top):
            return {"Id": name, "SourceText": "private source " + name,
                    "Role": "Control", "Bounds": {"Left": 0, "Top": top,
                                                   "Right": 120, "Bottom": top + 30}}

        def layout(name, top, drawn, color="FFEEEEEE"):
            return {"Region": name, "Bounds": {"Left": 0, "Top": top,
                                                  "Right": 120, "Bottom": top + 30},
                    "ForegroundArgb": color,
                    "SourceStyle": {"EstimatedFontSizePixels": 20, "Confidence": "High",
                                    "ForegroundArgb": "FFEEEEEE"},
                    "TextLayout": {"FontSizePixels": drawn}}

        first = event("region_decisions", 1, capture=1,
                      data={"Regions": [region("r1", 0), region("r2", 50)],
                            "Layouts": [layout("r1", 0, 20), layout("r2", 50, 14)]})
        second = event("region_decisions", 2, capture=2,
                       data={"Regions": [region("r1", 0), region("r2", 50)],
                             "Layouts": [layout("r1", 0, 16, "FFDDDDDD"),
                                         layout("r2", 50, 14)]})
        second["viewport_generation"] = 1
        result = analyze_layout_style([first, second])
        kinds = [candidate["kind"] for candidate in result["review_candidates"]]
        self.assertEqual(2, result["frames_with_layout_evidence"])
        self.assertEqual(4, result["observed_layouts"])
        self.assertEqual(2, kinds.count("font-shrunk"))
        self.assertIn("same-source-scale-peer-mismatch", kinds)
        self.assertIn("stable-source-font-jitter", kinds)
        self.assertIn("foreground-mismatch", kinds)
        self.assertNotIn("private source", json.dumps(result))

        many = [event("region_decisions", index, capture=index,
                      data={"Regions": [region("r1", 0)],
                            "Layouts": [layout("r1", 0, 10)]}) for index in range(60)]
        bounded = analyze_layout_style(many)
        self.assertEqual(60, bounded["review_candidate_count"])
        self.assertEqual(50, len(bounded["review_candidates"]))
        self.assertTrue(bounded["review_candidates_truncated"])

    def test_ocr_drift_classifies_spacing_geometry_and_partial_failure_without_text_leak(self):
        def region(text, x, result=True):
            return {"Id": "r1", "SourceText": text,
                    "Bounds": {"Left": x, "Top": 10, "Right": x + 100, "Bottom": 30},
                    "Result": {"IsSuccess": result,
                               "Status": "machine-content-integrity-failed" if not result else "machine-completed"}}

        events = [event("region_decisions", 1, capture=1,
                        data={"Regions": [region("Keep ticket XR-17", 10)]}),
                  event("region_decisions", 2, capture=2,
                        data={"Regions": [region("Keepticket XR-17", 13, False)]}),
                  event("region_decisions", 3, capture=3,
                        data={"Regions": [region("Keep ticket XR-18", 13)]})]
        drift = analyze_ocr_drift(events)
        self.assertEqual(1, drift["whitespace_only_drift_frames"])
        self.assertEqual(1, drift["frames_with_failed_regions"])
        self.assertTrue(drift["frames"][1]["same_nonspace_characters"])
        self.assertFalse(drift["frames"][1]["same_exact_text"])
        self.assertEqual(3, drift["frames"][1]["max_edge_delta_px"])
        self.assertEqual({"machine-content-integrity-failed": 1},
                         drift["frames"][1]["failed_statuses"])
        self.assertFalse(drift["frames"][2]["same_nonspace_characters"])
        self.assertNotIn("Keepticket", json.dumps(drift))

    def test_ocr_drift_does_not_guess_when_region_identity_changes(self):
        original = {"Id": "r1", "SourceText": "first",
                    "Bounds": {"Left": 1, "Top": 1, "Right": 5, "Bottom": 5}}
        changed = {**original, "Id": "r9"}
        drift = analyze_ocr_drift([
            event("region_decisions", 1, capture=1, data={"Regions": [original]}),
            event("region_decisions", 2, capture=2, data={"Regions": [changed]})])
        self.assertFalse(drift["frames"][1]["comparison_available"])
        self.assertIsNone(drift["frames"][1]["same_nonspace_characters"])

    def test_session_context_requires_the_recorded_version_and_target_fields(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            events = [event("started", 0), event("session_context", 1, data=session_context()),
                      event("ended", 2), event("trace_closed", 3)]
            path = write_bundle(root, events)
            self.assertEqual([], validate_bundle(path, events))
            for field, invalid in (("AppVersion", ""), ("ExecutableSha256", ""),
                                   ("Region", None), ("Monitor", None),
                                   ("Route", ""), ("TargetLanguage", ""),
                                   ("ShowInRecordings", None)):
                with self.subTest(field=field):
                    invalid_context = {**session_context(), field: invalid}
                    broken = [event("started", 0),
                              event("session_context", 1, data=invalid_context),
                              event("ended", 2), event("trace_closed", 3)]
                    path = write_bundle(root, broken)
                    self.assertIn(f"session-context-field-invalid:{field}",
                                  validate_bundle(path, broken))
            outside = {**session_context(), "Region": {"X": 1900, "Y": 100,
                                                      "Width": 300, "Height": 200}}
            broken = [event("started", 0), event("session_context", 1, data=outside),
                      event("ended", 2), event("trace_closed", 3)]
            path = write_bundle(root, broken)
            self.assertIn("session-context-region-outside-monitor",
                          validate_bundle(path, broken))

    def test_first_source_without_render_cannot_borrow_second_frame(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            png = b"\x89PNG\r\n\x1a\n" + b"synthetic"
            (root / "source.png").write_bytes(png)
            (root / "rendered.png").write_bytes(png)
            digest = hashlib.sha256(png).hexdigest()
            events = [
                event("started", 0),
                event("session_context", 1, data=session_context()),
                event("presentation", 10),
                event("source_image_saved", 20, capture=1,
                      data={"name": "source.png", "sha256": digest}),
                event("region_decisions", 30, capture=1),
                event("source_image_saved", 100, capture=2,
                      data={"name": "source.png", "sha256": digest}),
                event("region_decisions", 110, capture=2),
                event("applied", 120, capture=2, generation=2,
                      data={"State": "Ready", "Layouts": 1}),
                event("rendered_image_saved", 130, capture=2, generation=2,
                      data={"name": "rendered.png", "sha256": digest}),
                event("render_cycle_observed", 140, capture=2, generation=2,
                      data={"Visible": True, "Layouts": 1}),
                event("ended", 150), event("trace_closed", 160),
            ]
            path = write_bundle(root, events)
            self.assertEqual([], validate_bundle(path, events))
            result = analyze(events)
            self.assertIn("first-source-without-render:1:1:1", result["issues"])
            self.assertEqual("trace-incomplete", result["status"])
            self.assertIsNone(result["first_render_cycle_ms"])
            self.assertEqual(1, result["rendered_frames"])
            cli = subprocess.run([sys.executable, str(Path(__file__).with_name("live_trace_analyzer.py")),
                                  str(path)], capture_output=True, text=True, check=False)
            self.assertEqual(2, cli.returncode)
            self.assertEqual("trace-incomplete", json.loads(cli.stdout)["status"])

    def test_first_source_rendering_after_later_source_is_not_first_frame(self):
        events = [event("presentation", 0),
                  event("source_image_saved", 1, capture=1),
                  event("source_image_saved", 2, capture=2),
                  event("applied", 3, capture=2, generation=2,
                        data={"State": "Ready", "Layouts": 1}),
                  event("render_cycle_observed", 4, capture=2, generation=2,
                        data={"Visible": True, "Layouts": 1}),
                  event("applied", 5, capture=1, generation=1,
                        data={"State": "Ready", "Layouts": 1}),
                  event("render_cycle_observed", 6, capture=1, generation=1,
                        data={"Visible": True, "Layouts": 1}),
                  event("ended", 7)]
        result = analyze(events)
        self.assertIn("first-source-not-first-rendered:1:1:1", result["issues"])
        self.assertIsNone(result["first_render_cycle_ms"])

    def test_bundle_checks_event_and_image_integrity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            png = b"\x89PNG\r\n\x1a\n" + b"evidence-image"
            (root / "source.png").write_bytes(png)
            (root / "rendered.png").write_bytes(png)
            digest = hashlib.sha256(png).hexdigest()
            events = [event("started", 0), event("session_context", 1, data=session_context()),
                      event("presentation", 2),
                      event("source_image_saved", 3, capture=1,
                            data={"name": "source.png", "sha256": digest}),
                      event("translation_requests", 4, capture=1),
                      event("translation_results", 5, capture=1),
                      event("region_decisions", 6, capture=1),
                      event("applied", 7, capture=1, generation=1,
                            data={"State": "Ready", "Layouts": 1}),
                      event("rendered_image_saved", 8, capture=1, generation=1,
                            data={"name": "rendered.png", "sha256": digest}),
                      event("render_cycle_observed", 9, capture=1, generation=1,
                            data={"Visible": True, "Layouts": 1}),
                      event("ended", 10), event("trace_closed", 11)]
            for number, item in enumerate(events, 1):
                item["schema_version"] = "clipray.live-evidence.v1"
                item["sequence"] = number
            path = root / "events.jsonl"
            path.write_text("".join(json.dumps(item) + "\n" for item in events), encoding="utf-8")
            manifest = {"schema_version": "clipray.live-evidence.v1", "complete": True,
                        "write_failures": 0, "event_count": len(events),
                        "events_sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
            (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            self.assertEqual([], validate_bundle(path, events))

            (root / "rendered.png").write_bytes(b"broken")
            issues = validate_bundle(path, events)
            self.assertIn("image-checksum-mismatch:9", issues)
            self.assertIn("image-format-invalid:9", issues)

            (root / "rendered.png").write_bytes(png)
            without_result = events[:5] + events[6:]
            self.assertIn("translation-result-missing:1:1:1",
                          validate_bundle(path, without_result))
            (root / "manifest.json").write_text("{}", encoding="utf-8")
            self.assertIn("trace-write-failed", validate_bundle(path, events))

    def test_incomplete_render_evidence_does_not_claim_a_write_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            events = [event("started", 0), event("session_context", 1, data=session_context()),
                      event("ended", 2), event("trace_closed", 3)]
            path = write_bundle(root, events)
            manifest_path = root / "manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            manifest["complete"] = False
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            issues = validate_bundle(path, events)
            self.assertIn("trace-manifest-incomplete", issues)
            self.assertNotIn("trace-write-failed", issues)

            manifest["write_failures"] = 1
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            self.assertIn("trace-write-failed", validate_bundle(path, events))

    def test_pairs_source_apply_and_render_without_claiming_quality(self):
        events = [event("started", 0), event("presentation", 100),
                  event("source_image_saved", 130, capture=1),
                  event("applied", 400, capture=1, generation=1,
                        data={"State": "Ready", "Layouts": 2}),
                  event("render_cycle_observed", 420, capture=1, generation=1,
                        data={"Visible": True, "Layouts": 2}),
                  event("cleared", 1000),
                  event("source_image_saved", 1050, capture=2),
                  event("applied", 1200, capture=2, generation=2,
                        data={"State": "AcceptablePartial", "Layouts": 1}),
                  event("render_cycle_observed", 1220, capture=2, generation=2,
                        data={"Visible": True, "Layouts": 1}),
                  event("stop_button", 1500), event("ended", 1510)]
        result = analyze(events)
        self.assertEqual("trace-consistent-needs-visual-semantic-review", result["status"])
        self.assertEqual(320, result["first_render_cycle_ms"])
        self.assertEqual(220, result["clear_to_render_ms"]["max"])
        self.assertEqual(10, result["stop_to_end_ms"]["max"])
        self.assertEqual(1, result["partial_frames"])

    def test_second_applied_frame_requires_its_own_image_and_render_cycle(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            png = b"\x89PNG\r\n\x1a\n" + b"synthetic"
            (root / "source.png").write_bytes(png)
            (root / "rendered.png").write_bytes(png)
            digest = hashlib.sha256(png).hexdigest()
            events = [
                event("started", 0), event("session_context", 1, data=session_context()), event("presentation", 2),
                event("source_image_saved", 3, capture=1,
                      data={"name": "source.png", "sha256": digest}),
                event("region_decisions", 4, capture=1),
                event("applied", 5, capture=1, generation=1,
                      data={"State": "Ready", "Layouts": 1}),
                event("rendered_image_saved", 6, capture=1, generation=1,
                      data={"name": "rendered.png", "sha256": digest}),
                event("render_cycle_observed", 7, capture=1, generation=1,
                      data={"Visible": True, "Layouts": 1}),
                event("source_image_saved", 8, capture=2,
                      data={"name": "source.png", "sha256": digest}),
                event("region_decisions", 9, capture=2),
                event("applied", 10, capture=2, generation=2,
                      data={"State": "Ready", "Layouts": 1}),
                event("rendered_image_saved", 11, capture=2, generation=2,
                      data={"name": "rendered.png", "sha256": digest}),
                event("render_cycle_not_observed", 12, capture=2, generation=2),
                event("ended", 13), event("trace_closed", 14),
            ]

            def write_bundle(items):
                for number, item in enumerate(items, 1):
                    item["schema_version"] = "clipray.live-evidence.v1"
                    item["sequence"] = number
                path = root / "events.jsonl"
                path.write_text("".join(json.dumps(item) + "\n" for item in items), encoding="utf-8")
                (root / "manifest.json").write_text(json.dumps({
                    "schema_version": "clipray.live-evidence.v1", "complete": True,
                    "write_failures": 0, "event_count": len(items),
                    "events_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                }), encoding="utf-8")
                return path

            path = write_bundle(events)
            self.assertIn("render-cycle-not-observed:13", analyze(events)["issues"])
            self.assertEqual([], validate_bundle(path, events))

            without_image = [item for item in events if not (
                item["kind"] == "rendered_image_saved" and item["capture_id"] == 2)]
            path = write_bundle(without_image)
            self.assertIn("apply-without-rendered-image:2:2",
                          validate_bundle(path, without_image))

    def test_render_without_source_is_rejected(self):
        result = analyze([event("presentation", 0),
                          event("applied", 10, capture=1, generation=1,
                                data={"State": "Ready", "Layouts": 1}),
                          event("render_cycle_observed", 20, capture=1, generation=1,
                                data={"Visible": True, "Layouts": 1}),
                          event("ended", 30)])
        self.assertIn("render-unpaired:3", result["issues"])
        self.assertIn("no-render-cycle-with-layout", result["issues"])

    def test_unrefreshed_change_and_post_stop_apply_are_rejected(self):
        result = analyze([event("presentation", 0),
                          event("source_image_saved", 5, capture=1),
                          event("applied", 10, capture=1, generation=1,
                                data={"State": "Ready", "Layouts": 1}),
                          event("render_cycle_observed", 15, capture=1, generation=1,
                                data={"Visible": True, "Layouts": 1}),
                          event("cleared", 20), event("stop_button", 30),
                          event("applied", 35, capture=1, generation=2,
                                data={"State": "Ready", "Layouts": 1}),
                          event("ended", 40)])
        self.assertIn("apply-after-stop:30", result["issues"])

    def test_change_without_refresh_is_rejected(self):
        result = analyze([event("presentation", 0),
                          event("source_image_saved", 5, capture=1),
                          event("applied", 10, capture=1, generation=1,
                                data={"State": "Ready", "Layouts": 1}),
                          event("render_cycle_observed", 15, capture=1, generation=1,
                                data={"Visible": True, "Layouts": 1}),
                          event("cleared", 20), event("ended", 40)])
        self.assertIn("clear-without-refresh:20", result["issues"])

    def test_old_trace_without_frame_identity_cannot_supply_first_frame_evidence(self):
        result = analyze([event("presentation", 0),
                          event("applied", 10, generation=1, data={"Layouts": 1}),
                          event("ended", 20)])
        self.assertEqual("trace-incomplete", result["status"])
        self.assertIsNone(result["first_render_cycle_ms"])

    def test_same_millisecond_order_does_not_count_old_render_as_refresh(self):
        result = analyze([event("presentation", 0),
                          event("source_image_saved", 1, capture=1),
                          event("applied", 2, capture=1, generation=1,
                                data={"State": "Ready", "Layouts": 1}),
                          event("render_cycle_observed", 3, capture=1, generation=1,
                                data={"Visible": True, "Layouts": 1}),
                          event("cleared", 3), event("ended", 4)])
        self.assertIn("clear-without-refresh:3", result["issues"])


if __name__ == "__main__":
    unittest.main()
