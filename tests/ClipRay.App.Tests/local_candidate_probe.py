"""Local raw-text model comparison. No network, OCR, production route or quality pass claim."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--cli", type=Path, required=True)
    parser.add_argument("--oracle", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--case", required=True)
    parser.add_argument("--source-language", required=True)
    parser.add_argument("--gpu-layers", type=int, default=20)
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--prompt-style", choices=["baseline", "faithful", "translategemma"], default="baseline")
    parser.add_argument("--target-language-code", default="zh")
    parser.add_argument("--cli-default-conversation", action="store_true",
                        help="For newer llama-cli builds that removed the --conversation switch")
    args = parser.parse_args()
    model_hash = digest(args.model)
    if model_hash != args.sha256.lower():
        raise ValueError("Model checksum mismatch; inference was not started")
    case = next(c for c in json.loads(args.oracle.read_text(encoding="utf-8-sig"))["cases"]
                if c["id"] == args.case)
    # Refuse reuse so an older report can never be mistaken for this run.
    args.output.mkdir(parents=True, exist_ok=False)
    report = {"modelSha256": model_hash, "cliSha256": digest(args.cli),
              "oracleSha256": digest(args.oracle), "case": args.case,
              "sourceLanguage": args.source_language, "gpuLayers": args.gpu_layers,
              "promptStyle": args.prompt_style,
              "cliDefaultConversation": args.cli_default_conversation,
              "templateLanguageCodes": {"source_lang_code": args.source_language,
                                        "target_lang_code": args.target_language_code}
                  if args.prompt_style == "translategemma" else None,
              "scope": "raw source text; no OCR/layout/foreground acceptance",
              "quality": "requires semantic review", "regions": []}
    for index, region in enumerate(case["regions"]):
        rules = "Return only the translation. Preserve product names and usernames."
        if args.prompt_style == "faithful":
            rules = ("Return only the translation, without explanation. Keep product names, organization names, "
                     "usernames and code identifiers exactly as written, including capitalization; do not translate "
                     "them as common nouns. Preserve who performs each action, who receives it, negation, "
                     "conditions, comparisons, quantities and uncertainty. Do not add, omit or summarize claims.")
        prompt = (f"Translate the following text from {args.source_language} into {case['targetLanguage']}. "
                  + rules + "\n\n" + region["sourceText"])
        if args.prompt_style == "translategemma":
            # The model's own specialized template supplies translation instructions.
            # Retain verbose runtime output to verify the language codes actually applied.
            prompt = region["sourceText"]
        prompt_file = args.output / f"{index}.prompt.txt"
        prompt_file.write_text(prompt, encoding="utf-8")
        command = [str(args.cli.resolve()), "--model", str(args.model.resolve()),
                   "--conversation", "--single-turn", "--no-escape", "--no-display-prompt",
                   "--no-warmup", "--simple-io", "--log-disable", "--temp", "0",
                   "--ctx-size", "2048", "--predict", "512", "--reasoning-budget", "0",
                   "--gpu-layers", str(args.gpu_layers), "--file", str(prompt_file.resolve())]
        if args.prompt_style == "translategemma":
            command.remove("--log-disable")
            command.extend(["--verbose-prompt", "--jinja", "--skip-chat-parsing", "--chat-template-kwargs", json.dumps({
                "source_lang_code": args.source_language, "target_lang_code": args.target_language_code})])
        if args.cli_default_conversation:
            command.remove("--conversation")
        started = time.perf_counter()
        timed_out = False
        with (args.output / f"{index}.response.txt").open("wb") as out, \
                (args.output / f"{index}.runtime.log").open("wb") as err:
            process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=out, stderr=err,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            try:
                process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                timed_out = True
                process.kill()
                process.wait()
            except BaseException:
                process.kill()
                process.wait()
                raise
        report["regions"].append({"id": region["id"], "input": region["sourceText"],
            "exitCode": process.returncode, "timedOut": timed_out,
            "elapsedMs": round((time.perf_counter() - started) * 1000),
            "rawResponseFile": f"{index}.response.txt"})
        (args.output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"region {index}: exit={process.returncode}; timeout={timed_out}", flush=True)
        if timed_out or process.returncode:
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
