import json
import re
import sys
import os
from types import SimpleNamespace
from collections import OrderedDict

try:
    import ctranslate2
    import sentencepiece
except ModuleNotFoundError:
    # Report a fixed code only; import errors can contain local paths.
    print(json.dumps({"error": "machine-dependency-unavailable"}), flush=True)
    raise SystemExit(1)
except (ImportError, OSError):
    print(json.dumps({"error": "machine-runtime-load-failed"}), flush=True)
    raise SystemExit(1)

MAX_INPUT_TOKENS = 192


def register_cuda_libraries():
    # Keep DLL directory handles alive for this worker. Do not alter system PATH.
    directory = os.environ.get("CLIPRAY_CUDA_BIN") or os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "gpu")
    handles = []
    if os.name == "nt" and os.path.isabs(directory) and os.path.isdir(directory):
        handles.append(os.add_dll_directory(directory))
        os.environ["PATH"] = directory + os.pathsep + os.environ.get("PATH", "")
    return handles


class MachineTranslationBackend:
    def __init__(self, model, runtime, requested="auto"):
        if requested not in ("auto", "cpu", "cuda"):
            raise ValueError("Unsupported machine translation device")
        self.model, self.runtime, self.requested = model, runtime, requested
        self.status = {"device": "cpu", "computeType": "int8", "verified": False,
                       "fallbackReason": None}
        self.translator = None
        if requested != "cpu":
            try:
                if runtime.get_cuda_device_count() > 0:
                    supported = runtime.get_supported_compute_types("cuda")
                    compute = next((item for item in ("int8_float16", "float16", "int8_float32", "float32")
                                    if item in supported), None)
                    if compute is None:
                        raise RuntimeError("No supported CUDA compute type")
                    self.translator = runtime.Translator(model, device="cuda", compute_type=compute,
                                                         intra_threads=4)
                    self.status.update(device="cuda", computeType=compute)
                elif requested == "cuda":
                    raise RuntimeError("CUDA device unavailable")
            except (RuntimeError, ValueError):
                if requested == "cuda":
                    raise
                self.status["fallbackReason"] = "cuda-initialization-failed"
        if self.translator is None:
            self._use_cpu()

    def _use_cpu(self):
        self.translator = self.runtime.Translator(self.model, device="cpu", compute_type="int8",
                                                 intra_threads=4)
        self.status.update(device="cpu", computeType="int8", verified=False)

    def translate_batch(self, batches, **options):
        try:
            result = self.translator.translate_batch(batches, **options)
        except RuntimeError:
            if self.requested != "auto" or self.status["device"] != "cuda":
                raise
            # Lazy CUDA loading may fail only on first inference. Retry exactly
            # once on CPU, then retain that backend for all subsequent requests.
            self.translator = None
            self.status["fallbackReason"] = "cuda-inference-failed"
            self._use_cpu()
            result = self.translator.translate_batch(batches, **options)
        self.status["verified"] = True
        return result

def is_japanese_han(source, match, source_language="auto"):
    if not re.fullmatch(r"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]+", match.group()):
        return False
    if source_language == "ja":
        return True
    boundaries = r"[。！？.!?\n]"
    before = list(re.finditer(boundaries, source[:match.start()]))
    begin = before[-1].end() if before else 0
    after = re.search(boundaries, source[match.end():])
    end = match.end() + after.start() if after else len(source)
    return bool(re.search(r"[\u3040-\u30ff\uff66-\uff9d]", source[begin:end]))

def is_chinese_prose(source):
    if re.search(r"[\u3040-\u30ff\uff66-\uff9d\uac00-\ud7af\u0400-\u052f\u0600-\u06ff]", source):
        return False
    han = len(re.findall(r"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]", source))
    return han > 0 and han >= len(re.findall(r"[A-Za-z]+(?:['-][A-Za-z]+)*", source))

def common_sentence_chunks(text, tokenizer):
    sentences = re.split(r"(?<=[。！？])\s*|(?<=[.!?])\s+(?=[A-Za-z\u3040-\u30ff\u4e00-\u9fff\u0400-\u052f\u0600-\u06ff\uac00-\ud7af])|\n", text)
    return [tokens for sentence in sentences if sentence.strip()
        for tokens in sentence_token_chunks(sentence, tokenizer)]
LITERAL_PATTERN = re.compile(
    r"(?<![A-Za-z0-9_@])(?:https?://)?(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?:[/?#][^\s<>]*)?"
    r"|[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]+|\{\{[^{}]+\}\}|\{[^{}]+\}|</?[^>]+>"
    r"|(?<!\w)@[A-Za-z0-9_][\w.-]*|%(?:\d+\$)?[a-zA-Z]|\b(?:Codex|ChatGPT|Anthropic|OpenAI|Grok|Astra)\b"
    r"|\b[A-Za-z][A-Za-z0-9]*(?:[._-]\d+)+\b"
    r"|\b(?:Ctrl|Alt|Shift|Win)(?:\+[A-Za-z0-9]+)+\b|\bF(?:[1-9]|1\d|2[0-4])\b"
    r"|(?<![A-Za-z0-9_])\d+(?:[.,]\d+)*(?![A-Za-z0-9_])", re.IGNORECASE)


def restore_aligned_literals(source_tokens, result, tokenizer, source_language="auto"):
    target = list(result.hypotheses[0])
    decoded = tokenizer.decode(target).replace("\u2581", " ")
    source = tokenizer.decode(source_tokens)
    matches = [match for match in LITERAL_PATTERN.finditer(source) if not is_japanese_han(source, match, source_language)]
    if not matches or not result.attention:
        return decoded
    prefixes = [tokenizer.decode(source_tokens[:i]) for i in range(len(source_tokens)+1)]
    if any(not source.startswith(prefix) for prefix in prefixes):
        return decoded
    geometry = [(len(before),len(after)) for before,after in zip(prefixes,prefixes[1:])]
    edits = []
    for match in matches:
        literal = match.group()
        expected = sum(item.group() == literal for item in matches)
        if decoded.count(literal) == expected:
            continue
        indices = {i for i, (begin,end) in enumerate(geometry) if begin < match.end() and end > match.start()}
        aligned = [i for i, row in enumerate(result.attention[0][:len(target)])
            if row and max(range(len(row)), key=row.__getitem__) in indices
            and sum(row[j] for j in indices) >= 0.45]
        # Ambiguous/discontinuous attention is not evidence for replacing text.
        if not aligned or aligned != list(range(min(aligned), max(aligned)+1)):
            continue
        start, end = min(aligned), max(aligned)+1
        if any(start < other_end and end > other_start for other_start,other_end,_ in edits):
            return decoded
        edits.append((start,end,literal))
    for start,end,literal in sorted(edits,reverse=True):
        target[start:end] = [literal]
    return tokenizer.decode(target).replace("\u2581", " ")


def normalize_model_input(text):
    # Typography variants are equivalent punctuation, but not equivalent bytes
    # inside a URL/identifier. Preserve those literal spans exactly.
    table = str.maketrans({"’": "'", "‘": "'", "“": '"', "”": '"'})
    parts = []
    cursor = 0
    for literal in LITERAL_PATTERN.finditer(text):
        parts.append(text[cursor:literal.start()].translate(table))
        parts.append(literal.group())
        cursor = literal.end()
    parts.append(text[cursor:].translate(table))
    return "".join(parts)


def sentence_token_chunks(text, tokenizer):
    text = normalize_model_input(text)
    tokens = tokenizer.encode(text, out_type=str)
    sentences = [sentence.strip() for sentence in re.split(
        r"(?<=[.!?。！？])\s+(?=[A-Za-z\u4e00-\u9fff])|\n\s*\n", text) if sentence.strip()]
    # Repeated sentences are independently complete. Packing them together lets
    # decoding collapse repetitions; preserve their count without fragmenting words.
    if len(set(sentences)) < len(sentences):
        return [tokenizer.encode(sentence, out_type=str) for sentence in sentences]
    if len(tokens) <= MAX_INPUT_TOKENS:
        return [tokens] if tokens else []
    # The token budget is a packing target, never a boundary inside a sentence.
    # Oversized single sentences are passed intact; CT2 must not truncate them.
    chunks = []
    pending = []
    for sentence in sentences:
        current = tokenizer.encode(sentence, out_type=str)
        if pending and len(pending) + len(current) > MAX_INPUT_TOKENS:
            chunks.append(pending)
            pending = []
        pending.extend(current)
    if pending:
        chunks.append(pending)
    return chunks


def main() -> int:
    if len(sys.argv) not in (2, 3):
        return 2

    model_root = sys.argv[1]
    common = os.path.isfile(f"{model_root}/sentencepiece.bpe.model")
    tokenizer = sentencepiece.SentencePieceProcessor(
        model_file=f"{model_root}/sentencepiece.bpe.model" if common else f"{model_root}/sentencepiece.model"
    )
    cuda_handles = register_cuda_libraries()
    translator = MachineTranslationBackend(model_root if common else f"{model_root}/model",
                                            ctranslate2, os.environ.get("CLIPRAY_MT_DEVICE", "auto").lower())

    cache = OrderedDict()
    cache_language = None
    while True:
        request_line = sys.stdin.readline()
        if not request_line:
            break
        request = json.loads(request_line)
        source_language = request.get("sourceLanguage", "auto")
        if source_language != cache_language:
            cache.clear()
            cache_language = source_language
        if (not common and request.get("sourceLanguage") != "en") or request.get("targetLanguage") != "zh":
            return 4

        texts = request.get("texts")
        if not isinstance(texts, list) or not all(isinstance(text, str) for text in texts):
            return 5

        missing = list(dict.fromkeys(text for text in texts if text not in cache))
        original_texts = texts
        texts = missing
        token_batches = []
        chunk_counts = []
        for text in texts:
            if common:
                # Split complete sentences for independent language decoding;
                # their Chinese output stays in the same visual paragraph.
                chunks = common_sentence_chunks(text, tokenizer)
            else:
                chunks = sentence_token_chunks(text, tokenizer)
            chunk_counts.append(len(chunks))
            token_batches.extend(chunks)

        unique_batches = list(dict.fromkeys(tuple(tokens) for tokens in token_batches
            if not (common and source_language != "ja" and is_chinese_prose(tokenizer.decode(tokens)))))
        unique_translations = (
            translator.translate_batch([(["__zh__"] + list(tokens) + ["</s>"]) if common else list(tokens)
                for tokens in unique_batches], beam_size=1 if common else 4, max_input_length=0,
                return_attention=any(LITERAL_PATTERN.search(tokenizer.decode(tokens)) for tokens in token_batches),
                max_decoding_length=min(4096,max(256,max(map(len,token_batches))*3)))
            if unique_batches
            else []
        )
        by_tokens = dict(zip(unique_batches, unique_translations))
        translations = [by_tokens.get(tuple(tokens)) for tokens in token_batches]
        if common:
            # Alignment columns include the encoder's language prefix and EOS;
            # remove those columns before the literal restorer sees source tokens.
            translations = [SimpleNamespace(hypotheses=item.hypotheses,
                attention=[[row[1:-1] for row in attention] for attention in item.attention] if item.attention else [])
                if item is not None else None for item in translations]
        # Target pieces can be unknown to the source-language SentencePiece vocabulary.
        # decode preserves their literal word-boundary marker; normalize it as Argos does.
        # Keep decode (including byte fallback), and do not alter ordinary underscores.
        decoded_chunks = [
            restore_aligned_literals(source_tokens,item,tokenizer,source_language) if item is not None else tokenizer.decode(source_tokens)
            for source_tokens,item in zip(token_batches,translations)
        ]
        output = []
        output_backends = []
        chunk_index = 0
        for text, chunk_count in zip(texts, chunk_counts):
            if chunk_count == 0:
                output.append(text)
                output_backends.append(None)
                continue
            output.append("".join(decoded_chunks[chunk_index : chunk_index + chunk_count]))
            output_backends.append(dict(translator.status) if any(
                item is not None for item in translations[chunk_index : chunk_index + chunk_count]) else None)
            chunk_index += chunk_count
        for text, translated, backend in zip(texts, output, output_backends):
            cache[text] = (translated, backend)
        result = [cache[text][0] for text in original_texts]
        result_backends = [cache[text][1] for text in original_texts]
        for text in original_texts:
            cache.move_to_end(text)
        while len(cache) > 512:
            cache.popitem(last=False)
        sys.stdout.write(json.dumps({"texts": result, "backend": translator.status,
                                    "textBackends": result_backends}, ensure_ascii=False) + "\n")
        sys.stdout.flush()
        if len(sys.argv) == 2:
            break
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
