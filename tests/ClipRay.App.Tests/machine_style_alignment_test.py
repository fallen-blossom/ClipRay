"""Production alignment geometry/protocol under controlled model attention, no model load."""
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
names = {"text_style_alignments", "normalize_model_input", "select_machine_device"}
nodes = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in names
    or isinstance(node, ast.Assign) and any(isinstance(target, ast.Name) and target.id == "LITERAL_PATTERN" for target in node.targets)]
namespace = {"re": re}
exec(compile(ast.Module(body=nodes, type_ignores=[]), str(worker), "exec"), namespace)


class Tokenizer:
    def __init__(self, **kwargs):
        pass
    def decode(self, tokens):
        return "".join(tokens)


align = namespace["text_style_alignments"]
tokens = ["I", " can", " read", " the", " room."]
result = SimpleNamespace(hypotheses=[["我", "能", "察言观色", "。"]],
    attention=[[[1, 0, 0, 0, 0], [0, 1, 0, 0, 0], [0, 0, .6, .15, .25], [0, 0, 0, 0, 1]]])
resolved = align("I can read the room.", [tokens], [result], ["我能察言观色。"], Tokenizer())
assert resolved[0] == {"sourceStart": 0, "sourceLength": 1, "targetStart": 0, "targetLength": 1, "confidence": 1, "evidence": 3}
assert resolved[2]["sourceStart"] == 6 and resolved[2]["sourceLength"] == 14, "An idiom's attention was narrowed to one source word"
diffuse = SimpleNamespace(hypotheses=result.hypotheses, attention=[[[.2] * 5] * 4])
assert not align("I can read the room.", [tokens], [diffuse], ["我能察言观色。"], Tokenizer()), "Diffuse model attention became confident styling"
assert not align("I can read the room.", [tokens], [result], ["我能看懂房间。"], Tokenizer()), "Post-restoration target edits retained stale offsets"
assert not align("I can read the room. I can read the room.", [tokens, tokens], [result, result], ["我能察言观色。"] * 2, Tokenizer()), "Repeated source chunks received guessed offsets"
emoji_result = SimpleNamespace(hypotheses=[["😀", "我"]], attention=[[[1, 0], [0, 1]]])
emoji = align("😀 I", [["😀", " I"]], [emoji_result], ["😀我"], Tokenizer())
assert emoji[1]["sourceStart"] == 3 and emoji[1]["targetStart"] == 2, "Python codepoints leaked into .NET UTF-16 offsets"


class Backend:
    def __init__(self, *args):
        self.status = {"device": "cpu", "computeType": "int8", "verified": True, "fallbackReason": None}
        self.calls = 0
    def translate_batch(self, batches, **options):
        self.calls += 1
        return [SimpleNamespace(hypotheses=[["我"]], attention=[[[0, 1, 0]]] if options["return_attention"] else []) for _ in batches]


output = io.StringIO()
requests = [{"sourceLanguage": "en", "targetLanguage": "zh", "texts": ["I"], "returnAlignments": value}
    for value in [False, True, True]]
main = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == "main")
namespace.update({"sys": SimpleNamespace(argv=[str(worker), "fixture", "--persistent"], stdin=io.StringIO(
    "".join(json.dumps(request) + "\n" for request in requests)), stdout=output),
    "os": SimpleNamespace(path=SimpleNamespace(isfile=lambda path: True), environ={}),
    "json": json, "OrderedDict": OrderedDict, "SimpleNamespace": SimpleNamespace,
    "sentencepiece": SimpleNamespace(SentencePieceProcessor=Tokenizer), "ctranslate2": None,
    "register_cuda_libraries": lambda: [], "MachineTranslationBackend": Backend,
    "common_sentence_chunks": lambda text, tokenizer: [[text]], "is_chinese_prose": lambda text: False,
    "restore_aligned_literals": lambda tokens, result, tokenizer, language: "我"})
exec(compile(ast.Module(body=[main], type_ignores=[]), str(worker), "exec"), namespace)
assert namespace["main"]() == 0
responses = [json.loads(line) for line in output.getvalue().splitlines()]
assert responses[0]["textAlignments"] == [[]]
assert responses[1]["textAlignments"][0][0]["sourceLength"] == 1, "Text-only cache was not upgraded when alignment became necessary"
assert responses[2]["textAlignments"] == responses[1]["textAlignments"], "Cache lost alignment evidence"
print("Production style alignment rejects diffuse/repeated/stale evidence, maps UTF-16, and upgrades cached text.")
