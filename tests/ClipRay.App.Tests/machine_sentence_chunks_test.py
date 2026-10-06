"""Exercise production chunk planning without loading model dependencies."""
import ast
from pathlib import Path
import re
import sys

worker = Path(sys.argv[1])
tree = ast.parse(worker.read_text(encoding="utf-8-sig"))
functions = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in ["sentence_token_chunks","restore_aligned_literals","normalize_model_input","is_japanese_han","is_chinese_prose","common_sentence_chunks"]
    or isinstance(node, ast.Assign) and any(isinstance(target, ast.Name) and target.id == "LITERAL_PATTERN" for target in node.targets)]
namespace = {"re": re, "MAX_INPUT_TOKENS": 192}
exec(compile(ast.Module(body=functions, type_ignores=[]), str(worker), "exec"), namespace)

class Tokenizer:
    def encode(self, text, out_type=str):
        return text.split()

common = namespace["common_sentence_chunks"]
assert common("これは日本語です。English should translate. 中文保持原样。",Tokenizer()) == [
    ["これは日本語です。"],["English","should","translate."],["中文保持原样。"]], "Mixed languages were sent as one decoding sentence"
assert common("Keep GPT-6 Astra at 0.5x. Do not change 20x.",Tokenizer()) == [
    ["Keep","GPT-6","Astra","at","0.5x."],["Do","not","change","20x."]], "Sentence splitting fragmented a version or multiplier"
assert namespace["is_chinese_prose"]("中文保持原样。")
assert not namespace["is_chinese_prose"]("今日は使えません。")
han_match = next(namespace["LITERAL_PATTERN"].finditer("手紙"))
assert namespace["is_japanese_han"]("手紙", han_match, "ja")
assert not namespace["is_japanese_han"]("手紙", han_match, "auto")
source="これは日本語です。 中文保持原样。"
for match in namespace["LITERAL_PATTERN"].finditer(source):
    assert namespace["is_japanese_han"](source,match) == (match.group()=="日本語"), "Japanese kanji and separate Chinese sentence shared protection policy"

plan = namespace["sentence_token_chunks"]
normalize=namespace["normalize_model_input"]
assert normalize("It’d work overnight.") == "It'd work overnight.", "Typographic contraction remained out of vocabulary"
assert normalize("Ask @Alice about https://example.com/it’d and ‘work’.") == "Ask @Alice about https://example.com/it’d and 'work'.", "Literal URL or handle changed during typography normalization"
long_sentence = " ".join(["word"] * 230) + "."
assert plan(long_sentence, Tokenizer()) == [long_sentence.split()], "A long single sentence was cut at a token count"
first = " ".join(["first"] * 120) + "."
second = " ".join(["second"] * 120) + "!"
assert plan(first + " " + second, Tokenizer()) == [first.split(), second.split()], "The boundary cut a sentence"
short = "I tried GPT-5.4 for 3 hours, but it was worse."
assert plan(short, Tokenizer()) == [short.split()]
assert len(plan("Hello world. " * 240, Tokenizer())) == 240, "Repeated complete sentences were packed into a context that collapses repetitions"

class CharacterTokenizer:
    def decode(self, pieces):
        return "".join(pieces)

class Result:
    hypotheses = [["密", "码", "赢了", "。"]]
    attention = [[[1,0,0,0,0,0,0,0,0,0,0], [0,1,0,0,0,0,0,0,0,0,0],
                  [0,0,0,0,0,0,1,0,0,0,0], [0,0,0,0,0,0,0,0,0,0,1]]]

restore = namespace["restore_aligned_literals"]
assert restore(list("Codex wins."), Result(), CharacterTokenizer()) == "Codex赢了。", "Aligned product name was translated as an ordinary noun"

class NumericResult:
    hypotheses = [["第", "9", "行", "。"]]
    attention = [[[1,0,0,0,0,0,0], [0,0,0,0,0,1,0],
                  [0,0,0,0,1,0,0], [0,0,0,0,0,0,1]]]

assert restore(list("Line 8."), NumericResult(), CharacterTokenizer()) == "第8行。", "An aligned source number was not restored after NMT changed it"
ambiguous = Result()
ambiguous.hypotheses = [["密", "赢了", "码", "。"]]
ambiguous.attention = [[Result.attention[0][0],Result.attention[0][2],Result.attention[0][1],Result.attention[0][3]]]
assert restore(list("Codex wins."), ambiguous, CharacterTokenizer()) == "密赢了码。", "Discontinuous attention replaced unrelated words"
