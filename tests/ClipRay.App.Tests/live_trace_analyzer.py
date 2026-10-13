"""Summarize one ClipRay live trace without copying screenshots or translated text."""

import argparse
import hashlib
import json
import math
from pathlib import Path


def identity(event):
    fields = (event.get("capture_id"), event.get("pipeline_id"), event.get("viewport_generation"))
    return fields if all(isinstance(value, int) and not isinstance(value, bool) for value in fields) else None


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]


def analyze_ocr_drift(events):
    """Compare consecutive OCR outcomes without copying source text into the summary.

    Equal nonspace characters are a diagnostic clue, not proof of equal meaning:
    moving a space can change a word. Changed/missing region identities remain unknown.
    """
    frames = []
    previous = None
    for event in events:
        if not isinstance(event, dict) or event.get("kind") != "region_decisions":
            continue
        data = event.get("data")
        regions = data.get("Regions") if isinstance(data, dict) else None
        if not isinstance(regions, list):
            continue
        by_id = {region.get("Id"): region for region in regions if isinstance(region, dict)
                 and isinstance(region.get("Id"), str) and isinstance(region.get("SourceText"), str)}
        valid = len(by_id) == len(regions)
        comparable = previous is not None and valid and set(by_id) == set(previous)
        same_exact = same_nonspace = max_edge_delta = None
        if comparable:
            same_exact = all(previous[key]["SourceText"] == by_id[key]["SourceText"] for key in by_id)
            same_nonspace = all("".join(previous[key]["SourceText"].split())
                                == "".join(by_id[key]["SourceText"].split()) for key in by_id)
            edges = []
            for key in by_id:
                old, new = previous[key].get("Bounds"), by_id[key].get("Bounds")
                if not isinstance(old, dict) or not isinstance(new, dict):
                    edges = []
                    break
                values = [old.get(name) for name in ("Left", "Top", "Right", "Bottom")]
                values += [new.get(name) for name in ("Left", "Top", "Right", "Bottom")]
                if any(not isinstance(value, int) or isinstance(value, bool) for value in values):
                    edges = []
                    break
                edges.extend(abs(old[name] - new[name]) for name in ("Left", "Top", "Right", "Bottom"))
            max_edge_delta = max(edges) if edges else None
        failures = {}
        for region in regions:
            result = region.get("Result") if isinstance(region, dict) else None
            if isinstance(result, dict) and result.get("IsSuccess") is False:
                status = result.get("Status")
                status = status if isinstance(status, str) else "unknown"
                failures[status] = failures.get(status, 0) + 1
        frames.append({"capture_id": event.get("capture_id"), "region_count": len(regions),
                       "comparison_available": comparable, "same_exact_text": same_exact,
                       "same_nonspace_characters": same_nonspace,
                       "max_edge_delta_px": max_edge_delta,
                       "failed_region_count": sum(failures.values()),
                       "failed_statuses": failures})
        previous = by_id if valid else None
    return {"frames": frames,
            "whitespace_only_drift_frames": sum(frame["same_nonspace_characters"] is True
                                                and frame["same_exact_text"] is False for frame in frames),
            "frames_with_failed_regions": sum(frame["failed_region_count"] > 0 for frame in frames)}


def analyze_layout_style(events):
    """Find review candidates in recorded typography without claiming visual quality.

    Raw text and image pixels stay in the local trace. A fitted font may shrink
    legitimately, so these signals are questions for the paired composite image.
    """
    candidates = []
    candidate_count = 0
    previous = {}
    previous_generation = None
    frame_count = 0
    observed_layouts = 0
    missing_style = 0

    def number(value):
        return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and value > 0

    def coordinate(value):
        return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)

    def record(candidate):
        nonlocal candidate_count
        candidate_count += 1
        if len(candidates) < 50:
            candidates.append(candidate)

    for event in events:
        if not isinstance(event, dict) or event.get("kind") != "region_decisions":
            continue
        data = event.get("data")
        if not isinstance(data, dict) or not isinstance(data.get("Layouts"), list):
            continue
        frame_count += 1
        generation = event.get("viewport_generation")
        if generation != previous_generation:
            previous.clear()
            previous_generation = generation
        regions = {region.get("Id"): region for region in data.get("Regions", [])
                   if isinstance(region, dict) and isinstance(region.get("Id"), str)}
        current = []
        for layout in data["Layouts"]:
            if not isinstance(layout, dict):
                missing_style += 1
                continue
            observed_layouts += 1
            region_id = layout.get("Region")
            region = regions.get(region_id)
            style = layout.get("SourceStyle")
            text_layout = layout.get("TextLayout")
            bounds = layout.get("Bounds")
            if (not isinstance(region_id, str) or not isinstance(region, dict)
                    or not isinstance(style, dict) or not isinstance(text_layout, dict)
                    or not isinstance(bounds, dict)):
                missing_style += 1
                continue
            source_size = style.get("EstimatedFontSizePixels")
            drawn_size = text_layout.get("FontSizePixels")
            if not number(source_size) or not number(drawn_size):
                missing_style += 1
                continue
            item = {"region_id": region_id, "region": region, "bounds": bounds,
                    "source_size": source_size, "drawn_size": drawn_size,
                    "role": region.get("Role")}
            current.append(item)
            anchor = {"capture_id": event.get("capture_id"), "region_id": region_id}
            if drawn_size / source_size < .8:
                record({**anchor, "kind": "font-shrunk",
                        "source_font_px": round(source_size, 2),
                        "drawn_font_px": round(drawn_size, 2)})
            if (style.get("Confidence") == "High"
                    and isinstance(style.get("ForegroundArgb"), str)
                    and isinstance(layout.get("ForegroundArgb"), str)
                    and layout["ForegroundArgb"] != style["ForegroundArgb"]):
                record({**anchor, "kind": "foreground-mismatch",
                        "source_argb": style.get("ForegroundArgb"),
                        "drawn_argb": layout.get("ForegroundArgb")})
            earlier = previous.get((event.get("viewport_generation"), region_id))
            if earlier is not None and region.get("SourceText") == earlier["source_text"]:
                old_bounds = earlier["bounds"]
                edges = ("Left", "Top", "Right", "Bottom")
                stable_bounds = all(coordinate(bounds.get(edge)) and coordinate(old_bounds.get(edge))
                                    and abs(bounds[edge] - old_bounds[edge]) <= 1 for edge in edges)
                stable_source_size = abs(source_size - earlier["source_size"]) / max(source_size, earlier["source_size"]) <= .05
                if (stable_bounds and stable_source_size
                        and abs(drawn_size - earlier["drawn_size"]) / max(drawn_size, earlier["drawn_size"]) > .1):
                    record({**anchor, "kind": "stable-source-font-jitter",
                            "prior_capture_id": earlier["capture_id"],
                            "prior_font_px": round(earlier["drawn_size"], 2),
                            "drawn_font_px": round(drawn_size, 2)})
            previous[(event.get("viewport_generation"), region_id)] = {
                "source_text": region.get("SourceText"), "bounds": bounds,
                "source_size": source_size, "drawn_size": drawn_size,
                "capture_id": event.get("capture_id")}

        for index, first in enumerate(current):
            for second in current[index + 1:]:
                left, right = first["bounds"], second["bounds"]
                if (first["role"] != second["role"]
                        or first["role"] not in ("Control", "Metadata")
                        or not all(coordinate(box.get("Left")) and coordinate(box.get("Top"))
                                   for box in (left, right))
                        or abs(left["Left"] - right["Left"]) > 12
                        or abs(left["Top"] - right["Top"]) < 5
                        or min(first["source_size"], second["source_size"]) /
                           max(first["source_size"], second["source_size"]) < .95
                        or min(first["drawn_size"], second["drawn_size"]) /
                           max(first["drawn_size"], second["drawn_size"]) >= .85):
                    continue
                record({"capture_id": event.get("capture_id"),
                        "kind": "same-source-scale-peer-mismatch",
                        "region_ids": [first["region_id"], second["region_id"]],
                        "drawn_font_px": [round(first["drawn_size"], 2), round(second["drawn_size"], 2)]})

    return {"frames_with_layout_evidence": frame_count, "observed_layouts": observed_layouts,
            "layouts_missing_style_evidence": missing_style,
            "review_candidate_count": candidate_count,
            "review_candidates": candidates, "review_candidates_truncated": candidate_count > 50,
            "scope": "Recorded layout signals only; paired pixels and language quality still require review"}


def valid_rectangle(value):
    if not isinstance(value, dict):
        return False
    coordinates = [value.get(name) for name in ("X", "Y", "Width", "Height")]
    return (all(isinstance(item, int) and not isinstance(item, bool) for item in coordinates)
            and coordinates[2] > 0 and coordinates[3] > 0)


def validate_context(data):
    """Validate the fields written by App.StartLiveTranslationAsync."""
    fields = {
        "AppVersion": lambda value: isinstance(value, str) and bool(value.strip()),
        "ExecutableSha256": lambda value: isinstance(value, str) and len(value) == 64
        and all(character in "0123456789abcdefABCDEF" for character in value),
        "Region": valid_rectangle,
        "Monitor": valid_rectangle,
        "Route": lambda value: isinstance(value, str) and bool(value.strip()),
        "TargetLanguage": lambda value: isinstance(value, str) and bool(value.strip()),
        "ShowInRecordings": lambda value: isinstance(value, bool),
    }
    if not isinstance(data, dict):
        return [f"session-context-field-invalid:{field}" for field in fields]
    issues = [f"session-context-field-invalid:{field}" for field, valid in fields.items()
              if not valid(data.get(field))]
    if valid_rectangle(data.get("Region")) and valid_rectangle(data.get("Monitor")):
        region, monitor = data["Region"], data["Monitor"]
        if (region["X"] < monitor["X"] or region["Y"] < monitor["Y"]
                or region["X"] + region["Width"] > monitor["X"] + monitor["Width"]
                or region["Y"] + region["Height"] > monitor["Y"] + monitor["Height"]):
            issues.append("session-context-region-outside-monitor")
    return issues


def validate_bundle(events_path, events):
    """Check evidence integrity before interpreting a live session."""
    root = events_path.parent
    issues = []
    manifest_path = root / "manifest.json"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return ["manifest-missing-or-invalid"]
    schema = manifest.get("schema_version")
    data_only = schema == "clipray.live-evidence.v2-data"
    if schema not in ("clipray.live-evidence.v1", "clipray.live-evidence.v2-data"):
        issues.append("manifest-schema-unknown")
    if data_only and manifest.get("evidence_mode") != "data":
        issues.append("evidence-mode-mismatch")
    if manifest.get("complete") is not True:
        issues.append("trace-manifest-incomplete")
    if (not isinstance(manifest.get("write_failures"), int)
            or isinstance(manifest.get("write_failures"), bool)
            or manifest["write_failures"] != 0):
        issues.append("trace-write-failed")
    if manifest.get("event_count") != len(events):
        issues.append("event-count-mismatch")
    actual_hash = hashlib.sha256(events_path.read_bytes()).hexdigest()
    if manifest.get("events_sha256") != actual_hash:
        issues.append("events-checksum-mismatch")

    kinds = [event.get("kind") for event in events if isinstance(event, dict)]
    if not kinds or kinds[0] != "started" or kinds[-1] != "trace_closed":
        issues.append("trace-boundary-missing")
    context_events = [event for event in events if isinstance(event, dict)
                      and event.get("kind") == "session_context"]
    if not context_events:
        issues.append("session-context-missing")
    elif len(context_events) > 1:
        issues.append("session-context-duplicate")
    if context_events:
        issues.extend(validate_context(context_events[0].get("data")))
    source_keys = set()
    outcome_keys = set()
    request_keys = set()
    result_keys = set()
    exception_keys = set()
    applied = set()
    rendered = set()
    for number, event in enumerate(events, 1):
        if not isinstance(event, dict):
            issues.append(f"invalid-event:{number}")
            continue
        if event.get("schema_version") != schema:
            issues.append(f"event-schema-unknown:{number}")
        if event.get("sequence") != number:
            issues.append(f"event-sequence-gap:{number}")
        kind = event.get("kind")
        key = identity(event)
        generation = event.get("generation")
        data = event.get("data") if isinstance(event.get("data"), dict) else {}
        if kind in ("source_image_saved", "rendered_image_saved"):
            if data_only:
                issues.append(f"unexpected-image-in-data-mode:{number}")
            name = data.get("name")
            expected = data.get("sha256")
            if not isinstance(name, str) or Path(name).name != name or not name.endswith(".png"):
                issues.append(f"image-name-invalid:{number}")
            elif not isinstance(expected, str):
                issues.append(f"image-checksum-missing:{number}")
            else:
                image_path = root / name
                try:
                    image_bytes = image_path.read_bytes()
                except OSError:
                    issues.append(f"image-missing:{number}")
                else:
                    actual = hashlib.sha256(image_bytes).hexdigest()
                    if actual != expected:
                        issues.append(f"image-checksum-mismatch:{number}")
                    if not image_bytes.startswith(b"\x89PNG\r\n\x1a\n"):
                        issues.append(f"image-format-invalid:{number}")
            if key is None:
                issues.append(f"image-identity-missing:{number}")
            elif kind == "source_image_saved":
                source_keys.add(key)
            else:
                rendered.add((key, generation))
        elif kind in ("source_frame_fingerprint", "rendered_frame_fingerprint"):
            if not data_only:
                issues.append(f"fingerprint-schema-mismatch:{number}")
            digest = data.get("sha256")
            width, height = data.get("width"), data.get("height")
            if (not isinstance(digest, str) or len(digest) != 64
                    or any(c not in "0123456789abcdef" for c in digest)
                    or type(width) is not int or type(height) is not int
                    or width <= 0 or height <= 0 or data.get("pixel_format") != "BGRA32"
                    or data.get("byte_count") != width * height * 4):
                issues.append(f"frame-fingerprint-invalid:{number}")
            if key is None:
                issues.append(f"fingerprint-identity-missing:{number}")
            elif kind == "source_frame_fingerprint":
                source_keys.add(key)
            else:
                rendered.add((key, generation))
        elif kind == "region_decisions" and key is not None:
            outcome_keys.add(key)
        elif kind == "pipeline_exception" and key is not None:
            exception_keys.add(key)
        elif kind == "translation_requests" and key is not None:
            request_keys.add(key)
        elif kind == "translation_results" and key is not None:
            result_keys.add(key)
        elif kind == "translation_exception" and key is not None:
            exception_keys.add(key)
        elif kind == "applied" and key is not None and isinstance(generation, int):
            applied.add((key, generation))
        elif (kind == "render_cycle_observed" and data.get("Visible") is True
              and isinstance(data.get("Layouts"), int) and data["Layouts"] > 0):
            if (key, generation) not in rendered:
                issues.append(f"rendered-image-unpaired:{number}")
    for key in source_keys - outcome_keys - exception_keys:
        issues.append(f"pipeline-outcome-missing:{key[0]}:{key[1]}:{key[2]}")
    for key in request_keys - result_keys - exception_keys:
        issues.append(f"translation-result-missing:{key[0]}:{key[1]}:{key[2]}")
    for key, generation in rendered - applied:
        issues.append(f"rendered-image-without-apply:{key[0]}:{generation}")
    for key, generation in applied - rendered:
        issues.append(f"apply-without-rendered-image:{key[0]}:{generation}")
    if data_only:
        count = sum(e.get("kind") == "source_frame_fingerprint" for e in events if isinstance(e, dict))
        if manifest.get("source_frame_count") != count:
            issues.append("source-frame-count-mismatch")
        for key, generation in applied:
            if key not in source_keys:
                issues.append(f"apply-without-source-fingerprint:{key[0]}:{generation}")
    return issues


def analyze(events):
    issues = []
    if not events:
        return {"status": "trace-incomplete", "issues": ["empty-trace"], "scope": "WPF render cycle only; visual and semantic quality unreviewed"}
    prior_time = -1
    source_times = {}
    first_source_key = None
    applied = {}
    observed = set()
    render_failures = {}
    apply_times = []
    completed = []
    clears = []
    stops = []
    ends = []
    presentation = None
    for line_number, event in enumerate(events, 1):
        if not isinstance(event, dict) or not isinstance(event.get("kind"), str):
            issues.append(f"invalid-event:{line_number}")
            continue
        elapsed = event.get("elapsed_ms")
        if not isinstance(elapsed, int) or isinstance(elapsed, bool) or elapsed < prior_time:
            issues.append(f"invalid-time:{line_number}")
            continue
        prior_time = elapsed
        kind = event["kind"]
        key = identity(event)
        generation = event.get("generation")
        data = event.get("data") if isinstance(event.get("data"), dict) else {}
        if kind == "presentation":
            presentation = elapsed if presentation is None else presentation
        elif kind in ("source_image_saved", "source_frame_fingerprint"):
            if key is None:
                issues.append(f"source-missing-identity:{line_number}")
            else:
                source_times[key] = elapsed
                if first_source_key is None:
                    first_source_key = key
        elif kind == "applied":
            apply_times.append((elapsed, line_number))
            if key is None or not isinstance(generation, int) or isinstance(generation, bool):
                issues.append(f"apply-missing-identity:{line_number}")
            else:
                applied[(key, generation)] = (elapsed, data.get("State"), data.get("Layouts", 0))
        elif kind == "render_cycle_observed":
            pair = applied.get((key, generation)) if key is not None else None
            source = source_times.get(key) if key is not None else None
            if pair is None or source is None:
                issues.append(f"render-unpaired:{line_number}")
            elif (data.get("Visible") is True
                  and isinstance(data.get("Layouts"), int) and data["Layouts"] > 0
                  and isinstance(pair[2], int) and pair[2] > 0):
                if elapsed < source or elapsed < pair[0]:
                    issues.append(f"render-before-source-or-apply:{line_number}")
                    continue
                observed.add((key, generation))
                completed.append({"capture_id": key[0], "pipeline_id": key[1],
                                  "viewport_generation": key[2], "generation": generation,
                                  "source_to_render_ms": elapsed - source, "elapsed_ms": elapsed,
                                  "event_index": line_number, "state": pair[1]})
        elif kind == "render_cycle_not_observed":
            if key is None or not isinstance(generation, int) or isinstance(generation, bool):
                issues.append(f"render-failure-missing-identity:{line_number}")
            else:
                render_failures[(key, generation)] = line_number
        elif kind == "cleared":
            clears.append((elapsed, line_number))
        elif kind in ("stop_button", "escape_stop"):
            stops.append((elapsed, line_number))
        elif kind == "ended":
            ends.append((elapsed, line_number))
    if presentation is None:
        issues.append("presentation-missing")
    if not completed:
        issues.append("no-render-cycle-with-layout")
    if not ends:
        issues.append("session-end-missing")
    for key, generation in applied:
        if (key, generation) in observed:
            continue
        failure_line = render_failures.get((key, generation))
        issues.append(f"render-cycle-not-observed:{failure_line}" if failure_line is not None
                      else f"apply-without-render-cycle:{key[0]}:{generation}")
    first = completed[0] if completed else None
    if first_source_key is None:
        issues.append("source-image-missing")
        first = None
    elif not any((item["capture_id"], item["pipeline_id"], item["viewport_generation"])
                 == first_source_key for item in completed):
        issues.append("first-source-without-render:" + ":".join(map(str, first_source_key)))
        first = None
    elif first is not None and (first["capture_id"], first["pipeline_id"],
                                first["viewport_generation"]) != first_source_key:
        issues.append("first-source-not-first-rendered:" + ":".join(map(str, first_source_key)))
        first = None
    if first is not None and presentation is not None and first["elapsed_ms"] < presentation:
        issues.append("render-before-presentation")
    first_ms = first["elapsed_ms"] - presentation if first is not None and presentation is not None else None
    update_ms = []
    for clear in clears:
        if any(stop < clear <= end for stop in stops for end in ends if end >= stop):
            continue  # Teardown may clear the overlay without another frame.
        next_render = next((item for item in completed if item["event_index"] > clear[1]), None)
        next_stop = next((stop for stop in stops if stop > clear), None)
        if next_render is not None and (next_stop is None or (next_render["elapsed_ms"], next_render["event_index"]) < next_stop):
            update_ms.append(next_render["elapsed_ms"] - clear[0])
        elif next_stop is None:
            issues.append(f"clear-without-refresh:{clear[0]}")
    stop_ms = []
    for stop in stops:
        end = next((value for value in ends if value > stop), None)
        if end is None:
            issues.append(f"stop-without-end:{stop[0]}")
        else:
            stop_ms.append(end[0] - stop[0])
        if any(stop < applied_at <= (end if end is not None else (math.inf, math.inf)) for applied_at in apply_times):
            issues.append(f"apply-after-stop:{stop[0]}")
    source_latencies = [item["source_to_render_ms"] for item in completed]
    return {
        "status": "trace-incomplete" if issues else "trace-consistent-needs-visual-semantic-review",
        "issues": issues,
        "scope": "WPF render cycle only; desktop visibility, reading quality and semantic accuracy unreviewed",
        "first_render_cycle_ms": first_ms,
        "rendered_frames": len(completed),
        "ready_frames": sum(item["state"] == "Ready" for item in completed),
        "partial_frames": sum(item["state"] == "AcceptablePartial" for item in completed),
        "source_frames_without_render": len(set(source_times) - {(item["capture_id"], item["pipeline_id"], item["viewport_generation"]) for item in completed}),
        "source_to_render_ms": {"p50": percentile(source_latencies, .5), "p95": percentile(source_latencies, .95), "max": max(source_latencies, default=None)},
        "clear_to_render_ms": {"count": len(update_ms), "p50": percentile(update_ms, .5), "p95": percentile(update_ms, .95), "max": max(update_ms, default=None)},
        "stop_to_end_ms": {"count": len(stop_ms), "p50": percentile(stop_ms, .5), "p95": percentile(stop_ms, .95), "max": max(stop_ms, default=None)},
        "frames": [{key: value for key, value in frame.items() if key != "event_index"} for frame in completed],
        "ocr_drift": analyze_ocr_drift(events),
        "layout_style": analyze_layout_style(events),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("events", type=Path, help="Exact local events.jsonl from one live session")
    parser.add_argument("--output", type=Path, help="Optional local JSON summary path")
    args = parser.parse_args()
    events = []
    try:
        with args.events.open(encoding="utf-8") as stream:
            for line_number, line in enumerate(stream, 1):
                if not line.strip():
                    continue
                try:
                    events.append(json.loads(line))
                except json.JSONDecodeError as error:
                    raise ValueError(f"invalid JSON at line {line_number}") from error
        result = analyze(events)
        bundle_issues = validate_bundle(args.events, events)
        if bundle_issues:
            result["issues"] = bundle_issues + result["issues"]
            result["status"] = "trace-incomplete"
    except (OSError, ValueError) as error:
        parser.exit(2, f"trace input failed: {error}\n")
    serialized = json.dumps(result, ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(serialized + "\n", encoding="utf-8")
    else:
        print(serialized)
    return 2 if result["issues"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
