"""Closed subject-pronoun anchors use the actual merged-token attention shape.

Loads only production Python functions through AST; never loads model libraries.
"""
import ast
import json
import re
import sys
from pathlib import Path
from types import SimpleNamespace

worker = Path(sys.argv[1])
tree = ast.parse(worker.read_text(encoding="utf-8-sig"))
selected = [node for node in tree.body if isinstance(node, ast.FunctionDef)
    and node.name in {"normalize_model_input", "text_style_alignments"}
    or isinstance(node, ast.Assign) and any(isinstance(target, ast.Name) and target.id == "LITERAL_PATTERN" for target in node.targets)]
namespace = {"re": re}
exec(compile(ast.Module(body=selected, type_ignores=[]), str(worker), "exec"), namespace)
align = namespace["text_style_alignments"]


class Tokenizer:
    def decode(self, pieces):
        return "".join(pieces).replace("▁", " ").lstrip()


tokenizer = Tokenizer()
# Copied numeric row from root attention-probe.json for generated public text.
actual_row = [.3501094877719879, .028096308931708336, .0005697821034118533,
              .00023871476878412068, .0004652102943509817, .00016370326920878142]
source = ["▁I", "▁can", "▁read", "▁the", "▁room", "."]
target = ["▁我能", "读", "到", "房间", "。"]
actual = SimpleNamespace(hypotheses=[target], attention=[[actual_row] + [[0] * 6 for _ in range(4)]])
result = align(tokenizer.decode(source), [source], [actual], [tokenizer.decode(target)], tokenizer)
assert len(result) == 1 and result[0]["sourceStart"] == 0 and result[0]["sourceLength"] == 1
assert result[0]["targetStart"] == 0 and result[0]["targetLength"] == 1 and result[0]["evidence"] == 3
assert .92 < result[0]["confidence"] < .93, "Raw attention was mistaken for normalized quality"


def mapped(source_pieces, target_pieces, row, index=0):
    rows = [[0] * len(source_pieces) for _ in target_pieces]
    rows[index] = row
    output = align(tokenizer.decode(source_pieces), [source_pieces],
        [SimpleNamespace(hypotheses=[target_pieces], attention=[rows])], [tokenizer.decode(target_pieces)], tokenizer)
    return output


def anchored(source_pieces, target_pieces, row, index=0):
    return [item for item in mapped(source_pieces, target_pieces, row, index) if item["evidence"] == 3]


for source_word, translated in [("I", "我能"), ("you", "你能"), ("he", "他能"), ("she", "她能"),
                                 ("we", "我们能"), ("they", "他们能"), ("they", "她们能")]:
    candidates = anchored(["▁" + source_word, "▁can"], ["▁" + translated], [.35, .02])
    assert len(candidates) == 1 and candidates[0]["targetLength"] == (2 if source_word in {"we", "they"} else 1)

assert not anchored(["▁I", "▁think", "▁I", "▁can"], ["▁我能"], [.001, .001, .35, .02]), "Repeated source pronoun was guessed"
assert not anchored(["▁I", "▁can"], ["▁我能我也能"], [.35, .02]), "Repeated target pronoun was guessed"
assert not anchored(["▁they", "▁can"], ["▁他们和她们能"], [.35, .02]), "Different plural alternatives were both treated as unique"
for compound in ["我们能", "我方能", "我国能", "我家的", "自我能", "忘我能", "我的"]:
    assert not anchored(["▁I", "▁can"], ["▁" + compound], [.35, .02]), "Singular I matched a plural/compound: " + compound
    assert not mapped(["▁I", "▁can"], ["▁" + compound], [.95, .02]), "Rejected compound regained generic whole-token color: " + compound
assert not mapped(["▁I", "▁think", "▁I", "▁can"], ["▁我能"], [.001, .001, .95, .02]), "Repeated source regained generic coloring"
assert not mapped(["▁I", "▁can"], ["▁我能我也能"], [.95, .02]), "Repeated target regained generic coloring"
assert not anchored(["▁he", "▁can"], ["▁他们能"], [.35, .02])
assert not anchored(["▁he", "▁can"], ["▁其他能"], [.35, .02])
assert not anchored(["▁you", "▁can"], ["▁你们能"], [.35, .02])
assert not anchored(["▁bank", "▁can"], ["▁银行能"], [.35, .02]), "Ordinary polysemous bank entered the closed pronoun path"
assert not anchored(["▁bank", "▁can"], ["▁岸边能"], [.35, .02])
assert not anchored(["▁Inside", "▁can"], ["▁我能"], [.35, .02]), "A word-internal I became an independent pronoun"
for weak in [[.29, .001], [.35, .05], [.35, .10], [.02, .35], [float("nan"), .02]]:
    assert not anchored(["▁I", "▁can"], ["▁我能"], weak), "Weak/invalid/non-maximal pronoun attention was accepted"
assert not anchored(["▁I", "▁can"], ["▁我们", "我能"], [.35, .02], index=0), "Pronoun outside the current target token was used"
emoji = anchored(["😀", "▁I", "▁can"], ["😀", "我能"], [.001, .35, .02], index=1)
assert emoji[0]["sourceStart"] == 3 and emoji[0]["targetStart"] == 2, "UTF-16 source/target geometry drifted"
print("Closed pronoun anchors narrow actual merged-token attention; repeated/compound/bank/weak evidence stays unresolved.")
