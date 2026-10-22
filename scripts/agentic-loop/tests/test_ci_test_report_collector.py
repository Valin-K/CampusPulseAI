from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIRECTORY / "tools"))

from collect_ci_test_reports import collect_ci_reports, parse_trx


VALID_TRX = """<?xml version=\"1.0\" encoding=\"utf-8\"?>
<TestRun>
  <ResultSummary outcome=\"Completed\">
    <Counters total=\"3\" executed=\"2\" passed=\"1\" failed=\"1\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" notExecuted=\"1\" />
  </ResultSummary>
  <Results>
    <UnitTestResult testName=\"Passed\" outcome=\"Passed\" />
    <UnitTestResult testName=\"Failed\" outcome=\"Failed\" />
    <UnitTestResult testName=\"Skipped\" outcome=\"NotExecuted\" />
  </Results>
</TestRun>
"""


class CiTestReportCollectorTests(unittest.TestCase):
    def test_parses_trx_counters_and_failures(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "results.trx"
            path.write_text(VALID_TRX, encoding="utf-8")
            parsed = parse_trx(path)
            self.assertEqual("FAIL", parsed["status"])
            self.assertEqual(1, parsed["passed"])
            self.assertEqual(1, parsed["failed"])
            self.assertEqual(1, parsed["skipped"])

    def test_malformed_and_empty_artifact_directory_are_incomplete(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            malformed = root / "bad.trx"
            malformed.write_text("<TestRun>", encoding="utf-8")
            self.assertEqual("INCOMPLETE", parse_trx(malformed)["status"])
            report = collect_ci_reports(root / "empty", root / "output", "abc123")
            self.assertEqual("INCOMPLETE", report["status"])
            self.assertTrue((root / "output" / "ci-test-report.md").exists())
            self.assertTrue((root / "output" / "ci-test-report.json").exists())

    def test_collector_records_provenance_and_missing_workflows(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            trx = root / "student-1-test-results" / "TestResults" / "academic-tests.trx"
            trx.parent.mkdir(parents=True)
            trx.write_text(VALID_TRX.replace('failed=\"1\"', 'failed=\"0\"').replace('outcome=\"Failed\"', 'outcome=\"Passed\"'), encoding="utf-8")
            report = collect_ci_reports(root, root / "output", "commit-sha")
            self.assertEqual("INCOMPLETE", report["status"])
            actual = next(record for record in report["records"] if record["file"])
            self.assertEqual("student-1-ci", actual["workflow"])
            self.assertEqual("student-1-test-results", actual["artifact"])
            self.assertEqual("commit-sha", actual["commitSha"])
            self.assertTrue(any(record["status"] == "INCOMPLETE" for record in report["records"]))
            saved = json.loads((root / "output" / "ci-test-report.json").read_text(encoding="utf-8"))
            self.assertEqual("INCOMPLETE", saved["status"])


if __name__ == "__main__":
    unittest.main()
