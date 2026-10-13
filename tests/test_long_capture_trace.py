import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("trace", Path(__file__).resolve().parents[1] / "scripts/analyze_long_capture_trace.py")
trace = importlib.util.module_from_spec(spec)
spec.loader.exec_module(trace)


class TraceTests(unittest.TestCase):
    def check_case(self, mutate=None, summary=True):
        events = [dict(SchemaVersion=1, RunId="test", BinaryVersion="matching-version", Sequence=index + 1, ElapsedMs=index * 10, Stage=stage)
                  for index, stage in enumerate(("run_started", "preview_ready", "capture_stopped", "output_saved"))]
        events[1]["Detail"] = dict(Width=100, Height=200)
        size = dict(Width=1000, Height=2000)
        final = dict(EventCount=4, TraceWriteFailed=False, AcceptedFrameCount=3,
                     CompositeSize=size, SaveInputSize=size, SaveOutputSize=size, FinalPngSize=size)
        if mutate:
            mutate(events, final)
        with tempfile.TemporaryDirectory(prefix="clipray-long-trace-") as root:
            path = Path(root) / "long-capture-test.events.jsonl"
            path.write_text("\n".join(json.dumps(event) for event in events), encoding="utf-8")
            if summary:
                path.with_name("long-capture-test.json").write_text(json.dumps(final), encoding="utf-8")
            return trace.analyze(path)

    def test_valid(self):
        self.assertTrue(self.check_case()["consistent"])

    def test_missing_event_fails(self):
        self.assertFalse(self.check_case(lambda events, _: events.pop(1))["consistent"])

    def test_missing_summary_fails(self):
        self.assertFalse(self.check_case(summary=False)["consistent"])

    def test_dimension_mismatch_fails(self):
        self.assertFalse(self.check_case(lambda _, final: final.update(FinalPngSize=dict(Width=1, Height=1)))["consistent"])

    def test_oversize_preview_fails(self):
        self.assertFalse(self.check_case(lambda events, _: events[1]["Detail"].update(Height=10000))["consistent"])


if __name__ == "__main__":
    unittest.main()
