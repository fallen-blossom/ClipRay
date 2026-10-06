"""Local-only acceptance probe: trusted installed runtime, data-only model files."""
import argparse
import json
import time
from pathlib import Path
import ctranslate2
import sentencepiece

parser = argparse.ArgumentParser()
parser.add_argument("model_root")
parser.add_argument("fixtures")
parser.add_argument("--device", choices=("cpu", "cuda"), default="cpu")
args = parser.parse_args()
root = Path(args.model_root)
fixtures = json.loads(Path(args.fixtures).read_text(encoding="utf-8"))
sp = sentencepiece.SentencePieceProcessor(model_file=str(root / "sentencepiece.bpe.model"))
start = time.perf_counter()
model = ctranslate2.Translator(str(root), device=args.device,
    compute_type="int8_float16" if args.device == "cuda" else "int8", intra_threads=4)
print(json.dumps({"kind":"loaded","device":args.device,"milliseconds":round((time.perf_counter()-start)*1000)}), flush=True)
tokens = [["__zh__"] + sp.encode(item["text"], out_type=str) + ["</s>"] for item in fixtures]
# SMaLL-100's target language is encoded on the SOURCE. Do not force an
# untrained decoder language token merely because a community example does.
model.translate_batch(tokens[:1], beam_size=1, max_input_length=0)
for beam in (1,4):
    start = time.perf_counter()
    results = model.translate_batch(tokens, beam_size=beam, max_input_length=0, max_decoding_length=512)
    elapsed = round((time.perf_counter()-start)*1000)
    for item, result in zip(fixtures, results):
        target = sp.decode([token for token in result.hypotheses[0] if token not in ("__zh__","</s>","<s>")])
        print(json.dumps({"kind":"translation","device":args.device,"beam":beam,"batch_ms":elapsed,
            **item,"translation":target}, ensure_ascii=False), flush=True)
