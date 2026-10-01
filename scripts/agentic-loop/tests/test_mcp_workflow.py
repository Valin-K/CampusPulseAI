from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIRECTORY))

import agentic_loop
from mcp_workflow.collector import McpCollector
from mcp_workflow.orchestrator import run_mcp_workflow
from mcp_workflow.pipeline import (
    build_implementation_prompt,
    build_integration_review_prompt,
)


def tool_contracts() -> list[dict[str, object]]:
    return [
        {
            "name": "student_count",
            "description": "Return the number of student profiles.",
            "owner": "Academic Records",
            "inputSchema": {},
        },
        {
            "name": "students_by_subject",
            "description": "Return the count for a subject.",
            "owner": "Academic Records",
            "inputSchema": {"subject_code": "Required subject code"},
        },
        {
            "name": "project_files",
            "description": "List a repository directory.",
            "owner": "Repository",
            "inputSchema": {"directory_path": "Repository-relative directory, default ."},
        },
        {
            "name": "ci_report",
            "description": "Read the latest CI evidence.",
            "owner": "Repository",
            "inputSchema": {},
        },
    ]


class FakeResponse:
    def __init__(self, status_code: int, body: object) -> None:
        self.status_code = status_code
        self.body = body
        self.text = json.dumps(body)

    def json(self) -> object:
        return self.body


class McpCollectorTests(unittest.TestCase):
    def test_inspection_and_project_files_call_capture_live_evidence(self) -> None:
        contracts = tool_contracts()
        responses = [
            FakeResponse(200, {
                "status": "healthy",
                "service": "mcp",
                "toolCount": len(contracts),
            }),
            FakeResponse(200, contracts),
            FakeResponse(200, {
                "tool": "project_files",
                "isError": False,
                "result": {"path": ".", "files": ["README.md"]},
            }),
        ]
        requests_seen: list[dict[str, object]] = []

        def request_func(method: str, url: str, **kwargs: object) -> FakeResponse:
            requests_seen.append({"method": method, "url": url, **kwargs})
            return responses.pop(0)

        collector = McpCollector("http://localhost:5268", request_func=request_func)
        inspection = collector.inspect()
        prepared = collector.prepare_tool_call(
            inspection["tools"], "project_files", {}
        )
        invocation = collector.execute_tool(prepared)

        self.assertTrue(inspection["passed"])
        self.assertTrue(invocation["passed"])
        self.assertEqual(invocation["arguments"], {"directory_path": "."})
        self.assertEqual(invocation["request"]["body"]["tool"], "project_files")
        self.assertEqual(
            invocation["response"]["body"]["result"]["files"],
            ["README.md"],
        )
        self.assertEqual(len(requests_seen), 3)

    def test_collect_all_executes_and_captures_each_lab_tool(self) -> None:
        contracts = tool_contracts()
        responses = [
            FakeResponse(200, {
                "status": "healthy",
                "service": "mcp",
                "toolCount": len(contracts),
            }),
            FakeResponse(200, contracts),
            FakeResponse(200, {
                "tool": "student_count",
                "isError": False,
                "result": {"student_count": 12},
            }),
            FakeResponse(200, {
                "tool": "students_by_subject",
                "isError": False,
                "result": {"subject_code": "CS101", "student_count": 3},
            }),
            FakeResponse(200, {
                "tool": "project_files",
                "isError": False,
                "result": {"path": ".", "files": ["README.md"]},
            }),
            FakeResponse(200, {
                "tool": "ci_report",
                "isError": False,
                "result": {"path": "report.json", "report": "{}"},
            }),
        ]
        requests_seen: list[dict[str, object]] = []

        def request_func(method: str, url: str, **kwargs: object) -> FakeResponse:
            requests_seen.append({"method": method, "url": url, **kwargs})
            return responses.pop(0)

        evidence = McpCollector(
            "http://localhost:5268",
            request_func=request_func,
        ).collect_all()

        tool_calls = [
            request["json"]["tool"]
            for request in requests_seen
            if request["method"] == "POST"
        ]
        self.assertTrue(evidence["passed"])
        self.assertEqual(
            tool_calls,
            ["student_count", "students_by_subject", "project_files", "ci_report"],
        )
        self.assertEqual(
            evidence["toolExecutions"]["students_by_subject"]["arguments"],
            {"subject_code": "CS101"},
        )
        self.assertEqual(
            evidence["toolExecutions"]["project_files"]["arguments"],
            {"directory_path": "."},
        )
        self.assertTrue(all(
            execution["executed"] and execution["passed"]
            for execution in evidence["toolExecutions"].values()
        ))

    def test_collect_all_continues_after_a_tool_failure(self) -> None:
        contracts = tool_contracts()
        responses = [
            FakeResponse(200, {
                "status": "healthy",
                "service": "mcp",
                "toolCount": len(contracts),
            }),
            FakeResponse(200, contracts),
            FakeResponse(200, {
                "tool": "student_count",
                "isError": True,
                "result": {"error": "Academic Records unavailable."},
            }),
            FakeResponse(200, {
                "tool": "students_by_subject",
                "isError": False,
                "result": {"subject_code": "CS101", "student_count": 0},
            }),
            FakeResponse(200, {
                "tool": "project_files",
                "isError": False,
                "result": {"path": ".", "files": []},
            }),
            FakeResponse(200, {
                "tool": "ci_report",
                "isError": False,
                "result": {"path": "report.json", "report": "{}"},
            }),
        ]

        def request_func(method: str, url: str, **kwargs: object) -> FakeResponse:
            return responses.pop(0)

        evidence = McpCollector(
            "http://localhost:5268",
            request_func=request_func,
        ).collect_all()

        self.assertFalse(evidence["passed"])
        self.assertFalse(evidence["toolExecutions"]["student_count"]["passed"])
        self.assertTrue(evidence["toolExecutions"]["ci_report"]["passed"])
        self.assertEqual(len(evidence["toolExecutions"]), 4)

    def test_collect_all_skips_calls_when_a_required_tool_is_missing(self) -> None:
        contracts = tool_contracts()[:-1]
        responses = [
            FakeResponse(200, {
                "status": "healthy",
                "service": "mcp",
                "toolCount": len(contracts),
            }),
            FakeResponse(200, contracts),
        ]

        def request_func(method: str, url: str, **kwargs: object) -> FakeResponse:
            return responses.pop(0)

        evidence = McpCollector(
            "http://localhost:5268",
            request_func=request_func,
        ).collect_all()

        self.assertFalse(evidence["passed"])
        self.assertEqual(evidence["missingRequiredTools"], ["ci_report"])
        self.assertEqual(evidence["toolExecutions"], {})
        self.assertEqual(len(responses), 0)

    def test_tool_arguments_are_checked_before_any_call(self) -> None:
        collector = McpCollector("http://localhost:5268")
        prepared = collector.prepare_tool_call(
            tool_contracts(),
            "students_by_subject",
            {"unregistered": "value"},
        )

        self.assertFalse(prepared["valid"])
        self.assertTrue(any("subject_code" in error for error in prepared["errors"]))
        self.assertTrue(any("unregistered" in error for error in prepared["errors"]))
        self.assertFalse(collector.execute_tool(prepared)["executed"])

    def test_assessment_and_review_prompts_receive_collected_evidence(self) -> None:
        evidence = {
            "tools": tool_contracts(),
            "toolExecutions": {
                "student_count": {"passed": True, "result": {"student_count": 12}},
                "students_by_subject": {
                    "passed": True,
                    "result": {"subject_code": "CS101", "student_count": 3},
                },
                "project_files": {
                    "passed": True,
                    "result": {"path": ".", "files": ["README.md"]},
                },
                "ci_report": {
                    "passed": True,
                    "result": {"path": "report.json", "report": "{}"},
                },
            },
        }
        implementation_prompt = build_implementation_prompt(
            "Validate the MCP integration.", evidence
        )
        review_prompt = build_integration_review_prompt(
            "Assessment result.", evidence, {"decision": "pending", "notes": ""}
        )

        self.assertIn("student_count", implementation_prompt)
        self.assertIn("ci_report", implementation_prompt)
        self.assertIn("Assessment result.", review_prompt)
        self.assertIn("project_files", review_prompt)
        self.assertNotIn("Selected Tool:", implementation_prompt)


class McpWorkflowTests(unittest.TestCase):
    def test_workflow_assesses_four_tool_evidence_and_writes_reports(self) -> None:
        contracts = tool_contracts()
        tool_executions = {
            "student_count": {
                "tool": "student_count",
                "arguments": {},
                "executed": True,
                "passed": True,
                "response": {"body": {"result": {"student_count": 12}}},
            },
            "students_by_subject": {
                "tool": "students_by_subject",
                "arguments": {"subject_code": "CS101"},
                "executed": True,
                "passed": True,
                "response": {"body": {"result": {
                    "subject_code": "CS101",
                    "student_count": 3,
                }}},
            },
            "project_files": {
                "tool": "project_files",
                "arguments": {"directory_path": "."},
                "executed": True,
                "passed": True,
                "response": {"body": {"result": {
                    "path": ".",
                    "files": ["README.md"],
                }}},
            },
            "ci_report": {
                "tool": "ci_report",
                "arguments": {},
                "executed": True,
                "passed": True,
                "response": {"body": {"result": {
                    "path": "report.json",
                    "report": "{}",
                }}},
            },
        }
        collected_evidence = {
            "passed": True,
            "tools": contracts,
            "toolExecutions": tool_executions,
            "checks": [],
        }

        with tempfile.TemporaryDirectory() as temporary_directory:
            with patch("mcp_workflow.orchestrator.McpCollector") as collector_type:
                collector = collector_type.return_value
                collector.collect_all.return_value = collected_evidence

                outputs = iter([
                    "Assessment: Four registered MCP tools executed and returned valid results.\nGaps: None evidenced.\nRecommendation: Preserve tool boundaries.",
                    "Strengths: Four live tool results were captured.\nRisks: None evidenced.\nRecommendations: Keep result-contract checks.",
                ])
                model_calls: list[tuple[object, ...]] = []

                def fake_ai_call(*args: object, **kwargs: object) -> tuple[str, None, dict[str, str]]:
                    model_calls.append((*args, kwargs))
                    return next(outputs), None, {"usedProvider": "test", "usedModel": "fake"}

                exit_code = run_mcp_workflow(
                    server_url="http://localhost:5268",
                    evidence_dir=Path(temporary_directory),
                    start_services=lambda: [{"name": "Start MCP", "passed": True, "skipped": False}],
                    ai_call=fake_ai_call,
                    provider="test",
                    implementation_model="implementation",
                    review_model="reviewer",
                    goal="Validate all four MCP tools.",
                )

            run_directory = next((Path(temporary_directory) / "mcp").iterdir())
            evidence = json.loads((run_directory / "evidence.json").read_text(encoding="utf-8"))

            self.assertEqual(exit_code, 0)
            self.assertEqual(
                [stage["name"] for stage in evidence["stages"]],
                ["OBSERVE", "IMPLEMENTATION", "REVIEW"],
            )
            self.assertEqual(len(model_calls), 2)
            self.assertEqual(
                [model["stage"] for model in evidence["models"]],
                ["IMPLEMENTATION", "REVIEW"],
            )
            self.assertEqual(
                set(evidence["mcpEvidence"]["toolExecutions"]),
                {"student_count", "students_by_subject", "project_files", "ci_report"},
            )
            self.assertIn("student_count", evidence["models"][0]["userPrompt"])
            self.assertIn("Four live tool results were captured", evidence["models"][1]["rawOutput"])
            self.assertNotIn("selectedTool", evidence)
            self.assertEqual(evidence["humanReview"]["decision"], "pending")
            for report in (
                "run-report.md",
                "boundary-analysis.md",
                "integration-report.md",
                "tool-review.md",
            ):
                self.assertTrue((run_directory / report).is_file())


class McpServiceStartupTests(unittest.TestCase):
    def test_mcp_mode_starts_required_compose_target_and_waits_for_readiness(self) -> None:
        passed_check = {"name": "check", "passed": True, "skipped": False}
        with (
            patch("agentic_loop.run_command", return_value=passed_check) as run_command,
            patch("agentic_loop.wait_for_http_service", return_value=passed_check) as wait_for,
            patch("agentic_loop.ensure_local_service", return_value=passed_check) as ensure_local,
        ):
            checks = agentic_loop.start_validation_services("mcp")

        self.assertTrue(all(check["passed"] for check in checks))
        self.assertEqual(
            run_command.call_args.args[1],
            ["docker", "compose", "up", "--detach", "--build", "academicrecords.backend"],
        )
        self.assertEqual(
            [call.args[1] for call in wait_for.call_args_list],
            ["http://localhost:5021/health", "http://localhost:5020/health"],
        )
        self.assertEqual(ensure_local.call_args.args[0], "MCP server")

    def test_mcp_startup_does_not_start_mcp_until_compose_succeeds(self) -> None:
        compose_failure = {
            "name": "Start Academic Records Compose dependencies",
            "passed": False,
            "skipped": False,
            "output": "Docker is unavailable.",
        }
        with (
            patch("agentic_loop.run_command", return_value=compose_failure),
            patch("agentic_loop.wait_for_http_service") as wait_for,
            patch("agentic_loop.ensure_local_service") as ensure_local,
        ):
            checks = agentic_loop.start_validation_services("mcp")

        self.assertEqual(checks, [compose_failure])
        wait_for.assert_not_called()
        ensure_local.assert_not_called()


if __name__ == "__main__":
    unittest.main()