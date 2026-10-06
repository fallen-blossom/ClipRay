"""Build diagnostic composites from an opt-in ClipRay screenshot or live trace.

The source and transparent replacement layer must match by recorded identity and
SHA-256. Composites help inspect masking and layout; they are not desktop or
semantic acceptance evidence. Pillow is required only for this test helper.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


def _events(root: Path) -> list[dict]:
    path = root / "events.jsonl"
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def _verified_image(root: Path, entry: dict):
    from PIL import Image

    name = entry.get("data", {}).get("name")
    expected_hash = entry.get("data", {}).get("sha256")
    if not isinstance(name, str) or not name or Path(name).name != name:
        raise ValueError("image name is missing or outside the trace root")
    if not isinstance(expected_hash, str) or len(expected_hash) != 64:
        raise ValueError(f"missing SHA-256 for {name}")
    path = root / name
    content = path.read_bytes()
    if hashlib.sha256(content).hexdigest().lower() != expected_hash.lower():
        raise ValueError(f"SHA-256 mismatch for {name}")
    with Image.open(path) as opened:
        return opened.convert("RGBA")


def _identity(event: dict) -> tuple[int | None, int | None, int | None]:
    return (event.get("capture_id"), event.get("pipeline_id"), event.get("viewport_generation"))


def render(root: Path, output: Path) -> dict:
    from PIL import Image, ImageChops

    root = root.resolve(strict=True)
    events = _events(root)
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    schema = manifest.get("schema_version")
    pairs: list[tuple[str, dict, dict]] = []
    skipped_unobserved = 0
    if schema == "clipray.screenshot-evidence.v1":
        source = next((e for e in events if e.get("kind") == "source_image_saved"), None)
        layer = next((e for e in events if e.get("kind") == "rendered_layer_saved"), None)
        if source is None or layer is None:
            raise ValueError("screenshot source or rendered layer is missing")
        pairs.append(("composite-screenshot.png", source, layer))
    elif schema == "clipray.live-evidence.v1":
        sources = {_identity(e): e for e in events if e.get("kind") == "source_image_saved"}
        observed = {(_identity(e), e.get("generation")) for e in events
                    if e.get("kind") == "render_cycle_observed" and e.get("data", {}).get("Visible") is True}
        for layer in (e for e in events if e.get("kind") == "rendered_image_saved"):
            key = (_identity(layer), layer.get("generation"))
            if key not in observed:
                skipped_unobserved += 1
                continue
            source = sources.get(key[0])
            if source is None:
                raise ValueError(f"observed render has no exact source identity: {key}")
            if any(value is None for value in (*key[0], key[1])):
                raise ValueError("observed render has incomplete identity")
            pairs.append((f"composite-c{key[0][0]}-g{key[1]}.png", source, layer))
    else:
        raise ValueError(f"unsupported trace schema: {schema}")

    if not pairs:
        raise ValueError("no observed source/render pairs")
    output.mkdir(parents=True, exist_ok=True)
    products = []
    for name, source_event, layer_event in pairs:
        source = _verified_image(root, source_event)
        layer = _verified_image(root, layer_event)
        if source.size != layer.size:
            raise ValueError(f"image dimensions differ for {name}")
        composite = Image.alpha_composite(source, layer)
        changed = ImageChops.difference(source.convert("RGB"), composite.convert("RGB"))
        changed_pixels = source.width * source.height - changed.convert("L").histogram()[0]
        path = output / name
        composite.save(path)
        products.append({
            "name": name,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "source": source_event["data"]["name"],
            "layer": layer_event["data"]["name"],
            "changed_pixels": changed_pixels,
            "width": source.width,
            "height": source.height,
        })
    report = {
        "schema": "clipray.trace-composites.v1",
        "trace_schema": schema,
        "trace_complete": manifest.get("complete"),
        "observed_pairs": len(products),
        "skipped_unobserved_layers": skipped_unobserved,
        "visual_semantic_verdict": "not_assessed",
        "desktop_visibility_verdict": "not_assessed",
        "composites": products,
    }
    (output / "composite-manifest.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trace_root", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = render(args.trace_root, args.output)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        parser.exit(2, f"trace composite failed: {error}\n")
    print(json.dumps({key: value for key, value in report.items() if key != "composites"}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
