"""Structural success and keyword matches cannot authorize a quality claim."""

import hashlib
import tempfile
import unittest
from pathlib import Path

from quality_review_gate import audit, sha256


class QualityReviewGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "before.png").write_bytes(b"original image fixture")
        (self.root / "after.png").write_bytes(b"rendered image fixture")
        self.image_hash = hashlib.sha256(b"input image bytes").hexdigest()
        self.oracle_hash = hashlib.sha256(b"independent oracle bytes").hexdigest()
        self.report = {
            "SchemaVersion": 3, "RunId": "fixed-run", "Status": "expectation-met",
            "Cases": [{"Id": "negative-action", "ImageSha256": self.image_hash,
                       "BeforePath": "before.png", "Routes": [{"Route": "local",
                       "ExpectationMet": True, "AfterPath": "after.png",
                       "Evidence": {"StructuralExpectation": "met"},
                       "Oracle": {"OracleSha256": self.oracle_hash,
                                  "ContentQuality": "screened-needs-human-review",
                                  "LayoutQuality": "machine-checked-needs-visual-review",
                                  "Findings": []}}]}]
        }
        self.decision = {"CaseId": "negative-action", "Route": "local",
                         "ImageSha256": self.image_hash, "OracleSha256": self.oracle_hash,
                         "BeforeSha256": sha256(self.root / "before.png"),
                         "AfterSha256": sha256(self.root / "after.png"),
                         "OriginalReviewed": True, "RenderedReviewed": True,
                         "ReferenceTranslation": "取消不会退出登录", "Reviewer": "local-reviewer",
                         "SemanticVerdict": "pass", "VisualVerdict": "pass"}
        self.review = {"SchemaVersion": 1, "RunId": "fixed-run", "Decisions": [self.decision]}

    def test_structural_success_and_clean_keyword_screening_still_need_review(self):
        result = audit(self.report, self.root, None)
        self.assertEqual("evidence-incomplete", result["status"])
        self.assertIn("human-review-missing:negative-action:local", result["findings"])

    def test_deliberate_opposite_meaning_fails_when_reviewed(self):
        # The lexical oracle has no finding; reading the whole action sentence reveals the reversal.
        self.decision["SemanticVerdict"] = "fail"
        result = audit(self.report, self.root, self.review)
        self.assertEqual("quality-failed", result["status"])
        self.assertIn("human-review-failed:negative-action:local", result["findings"])

    def test_changed_rendered_artifact_invalidates_previous_review(self):
        (self.root / "after.png").write_bytes(b"new translated image")
        result = audit(self.report, self.root, self.review)
        self.assertIn("review-artifact-hash-mismatch:negative-action:local", result["findings"])

    def test_known_ocr_failure_cannot_be_overridden_by_positive_review(self):
        self.report["Cases"][0]["Routes"][0]["Oracle"]["Findings"] = [
            {"Code": "text-mismatch", "Stage": "ocr"}]
        result = audit(self.report, self.root, self.review)
        self.assertEqual("quality-failed", result["status"])
        self.assertIn("known-quality-failure:negative-action:local", result["findings"])

    def test_keyword_disagreement_requires_explanation_even_when_human_passes(self):
        self.report["Cases"][0]["Routes"][0]["Oracle"]["ContentQuality"] = "meaning-needs-human-review"
        self.report["Cases"][0]["Routes"][0]["Oracle"]["Findings"] = [
            {"Code": "meaning-signal-missing", "Stage": "translation"}]
        result = audit(self.report, self.root, self.review)
        self.assertIn("lexical-disagreement-needs-explanation:negative-action:local", result["findings"])

    def test_matching_review_is_only_offline_reviewed(self):
        result = audit(self.report, self.root, self.review)
        self.assertEqual("offline-reviewed", result["status"])
        self.assertIn("not foreground acceptance", result["scope"])

    def test_rendered_path_cannot_escape_test_root(self):
        self.report["Cases"][0]["Routes"][0]["AfterPath"] = "../outside.png"
        result = audit(self.report, self.root, self.review)
        self.assertIn("review-artifact-missing-or-outside-root:negative-action:local", result["findings"])

    def test_empty_case_routes_cannot_be_skipped(self):
        self.report["Cases"].append({"Id": "unrun-case", "Routes": []})
        result = audit(self.report, self.root, self.review)
        self.assertEqual("evidence-incomplete", result["status"])
        self.assertIn("invalid-report-case", result["findings"])

    def test_failed_run_status_cannot_be_reviewed_as_complete(self):
        self.report["Status"] = "failed"
        result = audit(self.report, self.root, self.review)
        self.assertEqual("evidence-incomplete", result["status"])
        self.assertIn("run-not-structurally-complete", result["findings"])


if __name__ == "__main__":
    unittest.main()
