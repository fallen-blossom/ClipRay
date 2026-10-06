import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from render_translation_trace import render


def image_event(root, kind, name, pixels, identity=None, generation=None):
    image = Image.new("RGBA", (2, 1))
    image.putdata(pixels)
    image.save(root / name)
    event = {
        "kind": kind,
        "data": {"name": name, "sha256": hashlib.sha256((root / name).read_bytes()).hexdigest()},
    }
    if identity is not None:
        event.update(zip(("capture_id", "pipeline_id", "viewport_generation"), identity))
    if generation is not None:
        event["generation"] = generation
    return event


class RenderTranslationTraceTests(unittest.TestCase):
    def test_screenshot_uses_alpha_instead_of_black_background(self):
        with tempfile.TemporaryDirectory(prefix="clipray-trace-composite-") as temp:
            root = Path(temp)
            (root / "manifest.json").write_text(
                json.dumps({"schema_version": "clipray.screenshot-evidence.v1", "complete": True}))
            events = [
                image_event(root, "source_image_saved", "source.png",
                            [(255, 255, 255, 255), (255, 255, 255, 255)]),
                image_event(root, "rendered_layer_saved", "layer.png",
                            [(0, 0, 0, 255), (0, 0, 0, 0)]),
            ]
            (root / "events.jsonl").write_text("\n".join(map(json.dumps, events)))
            result = render(root, root / "out")
            with Image.open(root / "out" / "composite-screenshot.png") as composite:
                self.assertEqual(composite.convert("RGB").getpixel((0, 0)), (0, 0, 0))
                self.assertEqual(composite.convert("RGB").getpixel((1, 0)), (255, 255, 255))
            self.assertEqual(result["composites"][0]["changed_pixels"], 1)
            self.assertEqual(result["desktop_visibility_verdict"], "not_assessed")

    def test_live_rejects_observed_layer_from_different_source_identity(self):
        with tempfile.TemporaryDirectory(prefix="clipray-trace-composite-") as temp:
            root = Path(temp)
            (root / "manifest.json").write_text(
                json.dumps({"schema_version": "clipray.live-evidence.v1", "complete": False}))
            source = image_event(root, "source_image_saved", "source.png",
                                 [(255, 255, 255, 255)] * 2, (1, 1, 1))
            layer = image_event(root, "rendered_image_saved", "layer.png",
                                [(0, 0, 0, 255), (0, 0, 0, 0)], (2, 2, 1), 4)
            observed = {"kind": "render_cycle_observed", "capture_id": 2,
                        "pipeline_id": 2, "viewport_generation": 1, "generation": 4,
                        "data": {"Visible": True}}
            (root / "events.jsonl").write_text("\n".join(map(json.dumps, [source, layer, observed])))
            with self.assertRaisesRegex(ValueError, "no exact source identity"):
                render(root, root / "out")

    def test_live_skips_superseded_layer_and_renders_observed_one(self):
        with tempfile.TemporaryDirectory(prefix="clipray-trace-composite-") as temp:
            root = Path(temp)
            (root / "manifest.json").write_text(
                json.dumps({"schema_version": "clipray.live-evidence.v1", "complete": False}))
            source = image_event(root, "source_image_saved", "source.png",
                                 [(255, 255, 255, 255)] * 2, (1, 1, 1))
            skipped = image_event(root, "rendered_image_saved", "superseded.png",
                                  [(0, 0, 0, 255), (0, 0, 0, 0)], (1, 1, 1), 3)
            layer = image_event(root, "rendered_image_saved", "rendered.png",
                                [(0, 0, 0, 255), (0, 0, 0, 0)], (1, 1, 1), 4)
            observed = {"kind": "render_cycle_observed", "capture_id": 1,
                        "pipeline_id": 1, "viewport_generation": 1, "generation": 4,
                        "data": {"Visible": True}}
            (root / "events.jsonl").write_text("\n".join(map(json.dumps, [source, skipped, layer, observed])))
            result = render(root, root / "out")
            self.assertEqual(result["observed_pairs"], 1)
            self.assertEqual(result["skipped_unobserved_layers"], 1)
            self.assertFalse(result["trace_complete"])

    def test_refuses_source_modified_after_trace(self):
        with tempfile.TemporaryDirectory(prefix="clipray-trace-composite-") as temp:
            root = Path(temp)
            (root / "manifest.json").write_text(
                json.dumps({"schema_version": "clipray.screenshot-evidence.v1", "complete": True}))
            source = image_event(root, "source_image_saved", "source.png",
                                 [(255, 255, 255, 255)] * 2)
            layer = image_event(root, "rendered_layer_saved", "layer.png",
                                [(0, 0, 0, 255), (0, 0, 0, 0)])
            (root / "events.jsonl").write_text("\n".join(map(json.dumps, [source, layer])))
            (root / "source.png").write_bytes((root / "source.png").read_bytes() + b"changed")
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                render(root, root / "out")


if __name__ == "__main__":
    unittest.main()
