"""Generate local experiment prompts; semanticCriteria are never sent to inference."""
import argparse
import json
from pathlib import Path


def render(case, context):
    text = (f"Translate the following text from {case['sourceLanguage']} into {case['targetLanguage']}. "
            "Return only the translation. Preserve product names and usernames.\n")
    if context:
        metadata = case["sourceMetadata"]
        text += "Source context: " + metadata["scene"] + ".\n"
        if metadata["terms"]:
            text += "Names in the source (preserve exactly): " + ", ".join(metadata["terms"]) + ".\n"
    return text + "\n" + case["sourceText"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--context", action="store_true")
    args = parser.parse_args()
    cases = json.loads(args.fixture.read_text(encoding="utf-8"))["cases"]
    args.output.mkdir(parents=True, exist_ok=False)
    for index, case in enumerate(cases):
        (args.output / f"{index}.prompt.txt").write_text(render(case, args.context), encoding="utf-8")
