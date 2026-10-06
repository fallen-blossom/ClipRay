"""Require image-bound human review before calling TestLab output offline-reviewed."""

import argparse
import hashlib
import json
from pathlib import Path


def valid_sha(value):
    return isinstance(value, str) and len(value) == 64 and all(character in "0123456789abcdefABCDEF" for character in value)


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def artifact(root, relative):
    if not isinstance(relative, str) or not relative or Path(relative).is_absolute():
        return None
    resolved_root = root.resolve(strict=True)
    resolved = (resolved_root / relative).resolve(strict=True)
    return resolved if resolved.is_relative_to(resolved_root) and resolved.is_file() else None


def audit(report, root, review):
    findings = []
    if report.get("SchemaVersion") != 3 or not report.get("Cases"):
        return {"status": "evidence-incomplete", "findings": ["report-schema-or-cases-invalid"]}
    if report.get("Status") != "expectation-met" or report.get("Errors"):
        findings.append("run-not-structurally-complete")
    if review is None:
        review = {}
    if review and (review.get("SchemaVersion") != 1 or review.get("RunId") != report.get("RunId")):
        findings.append("review-version-or-run-mismatch")
    decisions = review.get("Decisions", []) if isinstance(review.get("Decisions", []), list) else []
    by_key = {}
    for decision in decisions:
        if not isinstance(decision, dict):
            findings.append("invalid-review-decision")
            continue
        key = (decision.get("CaseId"), decision.get("Route"))
        if key in by_key:
            findings.append(f"duplicate-review:{key[0]}:{key[1]}")
        by_key[key] = decision
    passed = 0
    failed = False
    for case in report["Cases"]:
        if (not isinstance(case, dict) or not case.get("Id")
                or not isinstance(case.get("Routes"), list) or not case["Routes"]):
            findings.append("invalid-report-case")
            continue
        for route in case["Routes"]:
            if not isinstance(route, dict) or not route.get("Route"):
                findings.append(f"invalid-route:{case['Id']}")
                continue
            label = f"{case['Id']}:{route['Route']}"
            oracle = route.get("Oracle")
            evidence = route.get("Evidence")
            if not isinstance(oracle, dict) or not isinstance(evidence, dict):
                findings.append(f"independent-oracle-or-evidence-missing:{label}")
                continue
            if not valid_sha(case.get("ImageSha256")) or not valid_sha(oracle.get("OracleSha256")):
                findings.append(f"source-or-oracle-hash-invalid:{label}")
                continue
            hard = [item.get("Code") for item in oracle.get("Findings", [])
                    if isinstance(item, dict) and item.get("Code") != "meaning-signal-missing"]
            if hard or oracle.get("ContentQuality") in ("failed", "blocked") or oracle.get("LayoutQuality") in ("failed", "blocked-by-content"):
                findings.append(f"known-quality-failure:{label}")
                failed = True
                continue
            if (oracle.get("ContentQuality") not in ("screened-needs-human-review", "meaning-needs-human-review")
                    or oracle.get("LayoutQuality") != "machine-checked-needs-visual-review"):
                findings.append(f"oracle-screening-incomplete:{label}")
                continue
            if evidence.get("StructuralExpectation") != "met" or not route.get("ExpectationMet"):
                findings.append(f"structural-expectation-unmet:{label}")
                failed = True
                continue
            try:
                original = artifact(root, case.get("BeforePath"))
                rendered = artifact(root, route.get("AfterPath"))
            except (OSError, RuntimeError):
                original = None
                rendered = None
            if original is None or rendered is None:
                findings.append(f"review-artifact-missing-or-outside-root:{label}")
                continue
            decision = by_key.get((case["Id"], route["Route"]))
            if decision is None:
                findings.append(f"human-review-missing:{label}")
                continue
            hashes_match = (decision.get("ImageSha256") == case.get("ImageSha256")
                            and decision.get("OracleSha256") == oracle.get("OracleSha256")
                            and decision.get("BeforeSha256") == sha256(original)
                            and decision.get("AfterSha256") == sha256(rendered))
            if not hashes_match:
                findings.append(f"review-artifact-hash-mismatch:{label}")
                continue
            if (decision.get("OriginalReviewed") is not True or decision.get("RenderedReviewed") is not True
                    or not isinstance(decision.get("ReferenceTranslation"), str)
                    or not decision["ReferenceTranslation"].strip()
                    or not isinstance(decision.get("Reviewer"), str) or not decision["Reviewer"].strip()):
                findings.append(f"review-incomplete:{label}")
                continue
            semantic = decision.get("SemanticVerdict")
            visual = decision.get("VisualVerdict")
            if semantic not in ("pass", "fail") or visual not in ("pass", "fail"):
                findings.append(f"review-verdict-missing:{label}")
                continue
            if semantic == "fail" or visual == "fail":
                findings.append(f"human-review-failed:{label}")
                failed = True
                continue
            lexical_disagreement = any(item.get("Code") == "meaning-signal-missing"
                                       for item in oracle.get("Findings", []) if isinstance(item, dict))
            if lexical_disagreement and not str(decision.get("Notes", "")).strip():
                findings.append(f"lexical-disagreement-needs-explanation:{label}")
                continue
            passed += 1
    status = "offline-reviewed" if passed and not findings else "quality-failed" if failed else "evidence-incomplete"
    return {"status": status, "reviewed_routes": passed, "findings": findings,
            "scope": "Declared human review of fixed local artifacts; not foreground acceptance or proof of reviewer independence"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path, help="Exact TestLab schema v3 report JSON")
    parser.add_argument("root", type=Path, help="Local TestLab root containing rendered artifacts")
    parser.add_argument("--review", type=Path, help="Optional separate human review JSON; absent means pending")
    args = parser.parse_args()
    try:
        report = json.loads(args.report.read_text(encoding="utf-8"))
        review = json.loads(args.review.read_text(encoding="utf-8")) if args.review else None
        result = audit(report, args.root, review)
    except (OSError, ValueError, TypeError, json.JSONDecodeError) as error:
        parser.exit(2, f"quality review input failed: {error}\n")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["status"] == "offline-reviewed" else 1 if result["status"] == "quality-failed" else 2


if __name__ == "__main__":
    raise SystemExit(main())
