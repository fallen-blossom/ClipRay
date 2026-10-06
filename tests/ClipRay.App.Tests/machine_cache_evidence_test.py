"""Exercise production protocol/cache logic with a deterministic backend switch."""
import ast
import io
import json
import re
import sys
from collections import OrderedDict
from pathlib import Path
from types import SimpleNamespace

worker = Path(sys.argv[1])
tree = ast.parse(worker.read_text(encoding="utf-8-sig"))
main = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "main")

class Tokenizer:
    def __init__(self, **kwargs):
        pass
    def decode(self, tokens):
        return "".join(tokens)

class Backend:
    def __init__(self, *args):
        self.calls = 0
        self.status = {"device": "cuda", "computeType": "float16", "verified": False, "fallbackReason": None}
    def translate_batch(self, batches, **kwargs):
        self.calls += 1
        if self.calls > 1:
            self.status.update(device="cpu", computeType="int8", fallbackReason="cuda-inference-failed")
        self.status["verified"] = True
        return [SimpleNamespace(hypotheses=[["translated"]], attention=[]) for _ in batches]

inputs = [["A"], ["B"], ["A", "B"], ["C", "A"], ["中文"]]
output = io.StringIO()
namespace = {
    "sys": SimpleNamespace(argv=[str(worker), "fixture", "--persistent"],
        stdin=io.StringIO("".join(json.dumps({"sourceLanguage": "en", "targetLanguage": "zh", "texts": texts}) + "\n" for texts in inputs)), stdout=output),
    "os": SimpleNamespace(path=SimpleNamespace(isfile=lambda path: True), environ={}),
    "json": json, "OrderedDict": OrderedDict, "SimpleNamespace": SimpleNamespace,
    "sentencepiece": SimpleNamespace(SentencePieceProcessor=Tokenizer), "ctranslate2": None,
    "register_cuda_libraries": lambda: [], "MachineTranslationBackend": Backend,
    "common_sentence_chunks": lambda text, tokenizer: [[text]],
    "is_chinese_prose": lambda text: text == "中文", "LITERAL_PATTERN": re.compile(r"never-matches"),
    "restore_aligned_literals": lambda tokens, result, tokenizer, language: "translated",
}
exec(compile(ast.Module(body=[main], type_ignores=[]), str(worker), "exec"), namespace)
assert namespace["main"]() == 0
responses = [json.loads(line) for line in output.getvalue().splitlines()]
assert responses[0]["textBackends"][0]["device"] == "cuda"
assert responses[1]["textBackends"][0]["device"] == "cpu"
assert [item["device"] for item in responses[2]["textBackends"]] == ["cuda", "cpu"]
assert [item["device"] for item in responses[3]["textBackends"]] == ["cpu", "cuda"]
assert responses[4]["textBackends"] == [None], "Preserved source inherited unrelated inference evidence"
print("Production cache protocol retains per-text backend evidence across fallback and bypass.")
