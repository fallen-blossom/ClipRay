"""Backend failure-path cases; deliberately not executed during the test pause."""
import ast
import os
from pathlib import Path
import sys

worker = Path(sys.argv[1])
tree = ast.parse(worker.read_text(encoding="utf-8-sig"))
selected = [node for node in tree.body if isinstance(node, ast.ClassDef)
            and node.name == "MachineTranslationBackend"]

class FakeTranslator:
    def __init__(self, owner, device):
        self.owner, self.device = owner, device
    def translate_batch(self, batches, **options):
        if self.device == "cuda" and self.owner.fail_inference:
            raise RuntimeError("CUDA dependency unavailable")
        return batches

class FakeRuntime:
    def __init__(self, fail_inference=False):
        self.fail_inference, self.starts = fail_inference, []
    def get_cuda_device_count(self):
        return 1
    def get_supported_compute_types(self, device):
        return {"int8_float16", "float16"}
    def Translator(self, model, **options):
        self.starts.append(options)
        return FakeTranslator(self, options["device"])

namespace = {}
exec(compile(ast.Module(body=selected, type_ignores=[]), str(worker), "exec"), namespace)
backend_type = namespace["MachineTranslationBackend"]
runtime = FakeRuntime(True)
backend = backend_type("model", runtime, "auto")
assert backend.translate_batch([["hello"]]) == [["hello"]]
assert backend.translate_batch([["again"]]) == [["again"]]
assert [item["device"] for item in runtime.starts] == ["cuda", "cpu"]
assert backend.status["device"] == "cpu" and backend.status["verified"]
runtime = FakeRuntime()
backend = backend_type("model", runtime, "cpu")
assert runtime.starts[0]["device"] == "cpu"
backend = backend_type("model", FakeRuntime(True), "cuda")
try:
    backend.translate_batch([["hello"]])
except RuntimeError:
    pass
else:
    raise AssertionError("Explicit CUDA failure was silently hidden")
