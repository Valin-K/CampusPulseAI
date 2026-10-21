from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIRECTORY))

from cloud_deployment_review_workflow.collector import collect_cloud_deployment_evidence
from cloud_deployment_review_workflow.orchestrator import run_cloud_deployment_review_workflow
from multi_agent_review_workflow.collector import collect_multi_agent_evidence
from multi_agent_review_workflow.orchestrator import run_multi_agent_review_workflow
from testing_review_workflow.collector import collect_testing_evidence
from testing_review_workflow.orchestrator import run_testing_review_workflow


def fake_ai_call(*args: object, **kwargs: object) -> tuple[str, None, dict[str, str]]:
    return "Evidence-based assessment; release decision remains human.", None, {"usedProvider": "mock", "usedModel": "mock-model"}


def workflow_export(directory: Path, status: str = "approved") -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    snapshot = {
        "id": "workflow-123",
        "feature": "academicrecords",
        "task": "PRIVATE_TASK_TEXT",
        "status": status,
        "round": 1,
        "version": 4,
        "evidence": [{"id": "e1", "source": "private source", "content": "PRIVATE_EVIDENCE_TEXT"}],
        "dependencies": [{"service": "mcp", "status": "available", "detail": "PRIVATE_DEPENDENCY_DETAIL"}],
        "agents": [
            {"agent": "planner", "plan": {"objective": "objective", "steps": ["step"], "evidenceRequired": ["e1"]}},
            {"agent": "worker", "work": {"output": "PRIVATE_AGENT_OUTPUT", "evidenceUsed": ["e1"], "limitations": []}},
            {"agent": "reviewer", "review": {"recommendation": "approve", "risks": [], "corrections": [], "retest": [], "evidenceChecked": ["e1"]}},
        ],
        "humanReviews": [{"decision": "approve", "reviewer": "reviewer", "comments": "PRIVATE_REVIEW_COMMENT", "round": 1}],
    }
    (directory / "workflow.json").write_text(json.dumps(snapshot), encoding="utf-8")
    (directory / "workflow_history.jsonl").write_text(json.dumps(snapshot) + "\n", encoding="utf-8")
    (directory / "coordination_audit.jsonl").write_text(json.dumps({"sequence": 1, "event": "human_approve", "status": status, "version": 4, "round": 1}) + "\n", encoding="utf-8")
    return directory


def testing_reports(directory: Path, hook_status: str = "PASS", failed_tests: int = 0) -> tuple[Path, Path]:
    directory.mkdir(parents=True, exist_ok=True)
    precommit = directory / "precommit.json"
    ci = directory / "ci.json"
    precommit.write_text(json.dumps({
        "status": "FAIL" if hook_status == "FAIL" else "PASS",
        "hooks": [
            {"id": "detect-secrets", "status": hook_status, "exitCode": 1 if hook_status == "FAIL" else 0},
            {"id": "dotnet-security-analyzers", "status": "PASS", "exitCode": 0},
            {"id": "nuget-vulnerable-packages", "status": "PASS", "exitCode": 0},
        ],
    }), encoding="utf-8")
    ci_status = "FAIL" if failed_tests else "PASS"
    ci.write_text(json.dumps({
        "status": ci_status,
        "totals": {"passed": 2, "failed": failed_tests, "skipped": 0, "total": 2 + failed_tests},
        "records": [{"workflow": "student-1-ci", "artifact": "student-1-test-results", "testProject": "Tests", "status": ci_status, "passed": 2, "failed": failed_tests, "skipped": 0, "total": 2 + failed_tests}],
    }), encoding="utf-8")
    return precommit, ci


def cloud_evidence(directory: Path, passed: bool = True) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    records = [{"Check": "portal root", "Url": "https://example.invalid/", "Status": 200 if passed else 503, "Passed": passed}]
    (directory / "evidence.json").write_text(json.dumps(records), encoding="utf-8")
    (directory / "report.md").write_text("# Smoke test\n\n| Check | URL | Status | Passed |\n|---|---|---|---|\n| portal root | URL | 200 | True |\n", encoding="utf-8")
    return directory


class MultiAgentReviewTests(unittest.TestCase):
    def test_valid_export_is_passed_and_sensitive_content_is_not_sent_to_ai(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            export = workflow_export(root / "export")
            collected = collect_multi_agent_evidence(export)
            self.assertEqual("PASS", collected["status"])
            self.assertNotIn("PRIVATE_EVIDENCE_TEXT", json.dumps(collected))
            self.assertNotIn("PRIVATE_AGENT_OUTPUT", json.dumps(collected))
            result = run_multi_agent_review_workflow(
                export_directory=export,
                evidence_dir=root / "outputs",
                ai_call=fake_ai_call,
                provider="mock",
                implementation_model="assessment",
                review_model="reviewer",
            )
            self.assertEqual(0, result)
            output = next((root / "outputs" / "multi-agent-review").iterdir())
            prompt_text = (output / "evidence.json").read_text(encoding="utf-8")
            for private_value in ("PRIVATE_TASK_TEXT", "PRIVATE_EVIDENCE_TEXT", "PRIVATE_AGENT_OUTPUT", "PRIVATE_REVIEW_COMMENT"):
                self.assertNotIn(private_value, prompt_text)
            self.assertTrue((output / "review-report.md").exists())
            self.assertTrue((output / "validation-log.json").exists())

    def test_missing_and_malformed_exports_are_not_passed(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            missing = collect_multi_agent_evidence(root / "missing")
            self.assertEqual("INCOMPLETE", missing["status"])
            malformed = root / "malformed"
            workflow_export(malformed)
            (malformed / "workflow.json").write_text("{", encoding="utf-8")
            (malformed / "workflow_history.jsonl").write_text("not-json\n", encoding="utf-8")
            self.assertEqual("FAIL", collect_multi_agent_evidence(malformed)["status"])

    def test_failed_workflow_is_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            result = collect_multi_agent_evidence(workflow_export(Path(temp) / "export", "failed"))
            self.assertEqual("FAIL", result["status"])
            self.assertEqual("failed", result["workflow"]["status"])


class TestingReviewTests(unittest.TestCase):
    def test_valid_reports_and_output_files(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            precommit, ci = testing_reports(root / "input")
            collected = collect_testing_evidence(precommit, ci)
            self.assertEqual("PASS", collected["status"])
            result = run_testing_review_workflow(
                precommit_report=precommit,
                ci_report=ci,
                evidence_dir=root / "outputs",
                ai_call=fake_ai_call,
                provider="mock",
                implementation_model="assessment",
                review_model="reviewer",
            )
            self.assertEqual(0, result)
            output = next((root / "outputs" / "testing-review").iterdir())
            self.assertTrue((output / "evidence.json").exists())
            self.assertTrue((output / "review-report.md").exists())
            self.assertTrue((output / "validation-log.json").exists())

    def test_missing_malformed_and_failed_scan_or_test_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.assertEqual("INCOMPLETE", collect_testing_evidence(root / "missing-pre.json", root / "missing-ci.json")["status"])
            malformed = root / "malformed.json"
            malformed.write_text("{", encoding="utf-8")
            self.assertEqual("INCOMPLETE", collect_testing_evidence(malformed, malformed)["status"])
            precommit, ci = testing_reports(root / "failed", hook_status="FAIL", failed_tests=1)
            failed = collect_testing_evidence(precommit, ci)
            self.assertEqual("FAIL", failed["status"])
            self.assertTrue(any("detect-secrets" in check["name"] and check["status"] == "FAIL" for check in failed["checks"]))
            self.assertTrue(any("CI test record" in check["name"] and check["status"] == "FAIL" for check in failed["checks"]))

    def test_ai_call_failure_is_incomplete_and_still_writes_validation(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            precommit, ci = testing_reports(root / "input")
            calls = []

            def failing_ai(*args: object, **kwargs: object) -> tuple[None, str, dict[str, str]]:
                calls.append(args)
                return None, "offline", {}

            result = run_testing_review_workflow(
                precommit_report=precommit,
                ci_report=ci,
                evidence_dir=root / "outputs",
                ai_call=failing_ai,
                provider="mock",
                implementation_model="assessment",
                review_model="reviewer",
            )
            self.assertEqual(2, result)
            self.assertEqual(2, len(calls))
            output = next((root / "outputs" / "testing-review").iterdir())
            log = json.loads((output / "validation-log.json").read_text(encoding="utf-8"))
            self.assertEqual("INCOMPLETE", log["status"])
            self.assertTrue((output / "review-report.md").exists())


class CloudDeploymentReviewTests(unittest.TestCase):
    def test_valid_cloud_evidence_records_scope_limits_and_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = cloud_evidence(root / "cloud")
            collected = collect_cloud_deployment_evidence(source)
            self.assertEqual("INCOMPLETE", collected["status"])
            self.assertIn("internal-only", collected["scope"])
            result = run_cloud_deployment_review_workflow(
                deployment_evidence_directory=source,
                evidence_dir=root / "outputs",
                ai_call=fake_ai_call,
                provider="mock",
                implementation_model="assessment",
                review_model="reviewer",
            )
            self.assertEqual(2, result)
            output = next((root / "outputs" / "cloud-deployment-review").iterdir())
            report = (output / "review-report.md").read_text(encoding="utf-8")
            self.assertIn("does not separately probe", report)
            self.assertTrue((output / "evidence.json").exists())
            self.assertTrue((output / "validation-log.json").exists())

    def test_missing_malformed_and_failed_cloud_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.assertEqual("INCOMPLETE", collect_cloud_deployment_evidence(root / "missing")["status"])
            malformed = root / "malformed"
            malformed.mkdir()
            (malformed / "report.md").write_text("report", encoding="utf-8")
            (malformed / "evidence.json").write_text("{", encoding="utf-8")
            self.assertEqual("FAIL", collect_cloud_deployment_evidence(malformed)["status"])
            failed = collect_cloud_deployment_evidence(cloud_evidence(root / "failed", passed=False))
            self.assertEqual("FAIL", failed["status"])


if __name__ == "__main__":
    unittest.main()
